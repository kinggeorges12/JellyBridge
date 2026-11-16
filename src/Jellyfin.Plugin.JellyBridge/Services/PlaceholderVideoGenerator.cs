using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller.MediaEncoding;
using Jellyfin.Plugin.JellyBridge.BridgeModels;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for generating placeholder videos from asset images using FFmpeg.
/// </summary>
public class PlaceholderVideoGenerator
{
    
    // Semaphores to serialize asset extraction per asset name (prevents race conditions)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _assetExtractionSemaphores = new();
    
    // Semaphores to serialize cache file generation per cache path (prevents race conditions)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheGenerationSemaphores = new();

    // Semaphore to serialize invalidation of cached placeholders (prevents race conditions)
    private static readonly SemaphoreSlim _invalidationSemaphore = new SemaphoreSlim(1, 1);

    // Title promos: at most two FFmpeg renders at a time, since a sync can touch hundreds of titles
    private static readonly SemaphoreSlim _titlePromoRenderSemaphore = new SemaphoreSlim(2, 2);

    // Title promos older than this are rendered again (set by Generate Promo Videos)
    private static DateTime _titlePromosValidAfterUtc = DateTime.MinValue;

    // Overlay with the "favorite to request" note that goes over the title's backdrop
    private static readonly string TitlePromoOverlayAsset = "promo-overlay.png";

    // Artwork Jellyfin saves next to the media when "Save artwork into media folders" is on; a refresh
    // can replace backdrop.jpg with a numbered backdrop1.jpg, so numbered names count too
    private static readonly string[] BackdropPatterns = { "backdrop.*", "fanart.*", "backdrop*.*", "fanart*.*" };
    private static readonly string[] LogoPatterns = { "logo.*", "clearlogo.*", "logo*.*" };
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };


    private readonly DebugLogger<PlaceholderVideoGenerator> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private string _assetPath = "";
    private string _placeholderPath = "";
    
    // Asset file names for different media types
    private static readonly string MovieAsset = "movie.png";
    private static readonly string SeasonAsset = "S00E9999.png";
    
    // Season folder name
    private static readonly string SeasonFolderName = "Season 00";

    // Dictionary mapping media types to their corresponding asset file names
    private static readonly Dictionary<string, string> EmbeddedAsset = new Dictionary<string, string>
    {
        { MovieAsset, "movie.png" },
        { SeasonAsset, "show.png" }
    };
    
    // Asset file extension
    public static readonly string AssetExtension = ".mp4";
    public static readonly string AssetSearchPattern = "*" + AssetExtension;

    // One placeholder episode per season: "Season NN/SNNE9999.mp4"
    private static readonly Regex SeasonPlaceholderRegex = new(@"^S(\d+)E9999\.mp4$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonFolderRegex = new(@"^Season\s+(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Folder name for a season placeholder, e.g. "Season 02".
    /// </summary>
    public static string GetSeasonFolderName(int seasonNumber) => $"Season {seasonNumber:00}";

    /// <summary>
    /// File name for a season placeholder episode, e.g. "S02E9999.mp4".
    /// </summary>
    public static string GetSeasonPlaceholderFilename(int seasonNumber) => $"S{seasonNumber:00}E9999{AssetExtension}";

    /// <summary>
    /// Parse "Season NN" from a folder path. Returns null if the folder is not a season folder.
    /// </summary>
    public static int? ParseSeasonFolderNumber(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var match = SeasonFolderRegex.Match(Path.GetFileName(path.TrimEnd('/', '\\')));
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
        {
            return number;
        }

        return null;
    }

    /// <summary>
    /// Returns the season number (> 0) if the file is a per-season placeholder (SNNE9999.mp4), otherwise null.
    /// The specials placeholder S00E9999.mp4 returns null.
    /// </summary>
    public static int? ParseSeasonPlaceholderNumber(string? filePath)
    {
        var match = SeasonPlaceholderRegex.Match(Path.GetFileName(filePath ?? string.Empty));
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0)
        {
            return number;
        }
        return null;
    }

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder)
    {
        _logger = new DebugLogger<PlaceholderVideoGenerator>(logger);
        _mediaEncoder = mediaEncoder;
        // Set path for cached placeholder videos in the system temp directory
        InvalidateCachedPlaceholdersAsync().Wait();
    }

    private string GetVideoFilename(string assetName)
    {
        var fileStem = Path.GetFileNameWithoutExtension(assetName);
        return fileStem + AssetExtension;
    }

    /// <summary>
    /// Generate a placeholder video for a movie (asset: movie.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderMovieAsync(string movieFolderPath)
    {
        // This is the approved matching pattern for Jellyfin, although movie.mp4 also works for now.
        // var assetStem = Path.GetFileName(movieFolderPath);
        var targetFilename = GetVideoFilename(MovieAsset);
        return await GeneratePlaceholderAsync(movieFolderPath, targetFilename, MovieAsset);
    }

    /// <summary>
    /// Generate a placeholder video for a season (asset: season.png).
    /// Takes the show folder path and calculates the season folder internally.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    public async Task<bool> GeneratePlaceholderSeasonAsync(string showFolderPath)
    {
        var seasonFolderPath = Path.Combine(showFolderPath, SeasonFolderName);
        var targetFile = GetVideoFilename(SeasonAsset);
        return await GeneratePlaceholderAsync(seasonFolderPath, targetFile, SeasonAsset);
    }

    /// <summary>
    /// Generate the placeholder episode for a real season ("Season NN/SNNE9999.mp4"), reusing the cached series promo video.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    /// <param name="seasonNumber">Season number (> 0).</param>
    /// <param name="overwrite">When false, an existing placeholder with the same size as the cached video is left alone.</param>
    public async Task<bool> GeneratePlaceholderSeasonNumberAsync(string showFolderPath, int seasonNumber, bool overwrite = true)
    {
        if (seasonNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonNumber), "Season placeholders are only created for seasons > 0");
        }
        var seasonFolderPath = Path.Combine(showFolderPath, GetSeasonFolderName(seasonNumber));
        return await GeneratePlaceholderAsync(seasonFolderPath, GetSeasonPlaceholderFilename(seasonNumber), SeasonAsset, overwrite);
    }

    /// <summary>
    /// Ensures a cached placeholder video exists in the system temp directory for the given asset.
    /// Returns the path to the cached file if successful, null otherwise.
    /// Uses a semaphore per cache path to prevent race conditions when multiple tasks try to generate the same cache file.
    /// </summary>
    /// <param name="assetName">The asset image filename to base the placeholder on (e.g., "movie.png")</param>
    /// <returns>Path to cached file if successful, null otherwise</returns>
    private async Task<string?> EnsureCachedPlaceholderAsync(string assetName)
    {
        try
        {
            // Create cached placeholder videos in the configured or system temp path
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PromoVideoDurationSeconds));
            var assetStem = Path.GetFileNameWithoutExtension(assetName);

            // Include _custom in cache filename when a custom asset is active to prevent stale cache
            var cacheFilepath = Path.Combine(_placeholderPath, $"{assetStem}_{videoDuration}{AssetExtension}");

            // Get or create a semaphore for this specific cache path to serialize generation
            var semaphore = _cacheGenerationSemaphores.GetOrAdd(cacheFilepath, _ => new SemaphoreSlim(1, 1));
            
            var timeout = TimeSpan.FromMinutes(Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes)));
            await semaphore.WaitAsync(timeout);
            try
            {
                // Double-check pattern: after acquiring the lock, check if file was already created by another task
                if (File.Exists(cacheFilepath))
                {
                    _logger.LogTrace("Cached placeholder already exists: {CacheFile}", cacheFilepath);
                    return cacheFilepath;
                }
                
                // Wait for the file to be created and have content before releasing the semaphore
                // This handles cases where the file system is still writing the file
                const int maxAttempts = 5;
                
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    _logger.LogTrace("Waiting for cache file to be ready, attempt {Attempt}/{TotalAttempts}: {CachePath}", 
                        attempt, maxAttempts, cacheFilepath);
                    
                    if (File.Exists(cacheFilepath))
                    {
                        var fileInfo = new FileInfo(cacheFilepath);
                        if (fileInfo.Length > 0)
                        {
                            try {
                                using (FileStream stream = File.Open(cacheFilepath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                {
                                    _logger.LogTrace("Cache file is ready: {CachePath} ({Size} bytes) (attempt {Attempt}/{TotalAttempts})", 
                                        cacheFilepath, fileInfo.Length, attempt, maxAttempts);
                                }
                                return cacheFilepath;
                            }
                            catch (Exception)
                            {
                                _logger.LogTrace("Failed to open cache file, waiting...: {CachePath}", cacheFilepath);
                            }
                        }
                        else
                        {
                            _logger.LogTrace("Cache file exists but is empty, waiting...: {CachePath}", cacheFilepath);
                        }
                    }
                    else
                    {
                        _logger.LogTrace("Cached placeholder not found for {Asset}, generating at {CachePath}", assetName, cacheFilepath);

                        var ok = await GeneratePlaceholderVideoAsync(assetName, cacheFilepath);
                        if (!ok)
                        {
                            _logger.LogTrace("Generating video failed, waiting...: {CachePath}", cacheFilepath);
                        }
                    }
                    
                    // Wait with exponential backoff (1s, 2s, 4s)
                    var waitSeconds = Math.Pow(2, attempt - 1);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                }
                
                // If we get here, all retry attempts failed
                _logger.LogError("Cache file was not created after waiting 30 seconds: {CachePath}", cacheFilepath);
                
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed ensuring cached placeholder for {Asset}", assetName);
            return null;
        }
    }

    /// <summary>
    /// Copy to a temporary file next to the target and rename it over the target, so a player or ffprobe reading the old
    /// placeholder never sees a half-written file.
    /// </summary>
    private static void CopyReplacing(string source, string targetPath)
    {
        var temp = targetPath + ".jbtmp";
        File.Copy(source, temp, overwrite: true);
        File.Move(temp, targetPath, overwrite: true);
    }

    /// <summary>
    /// Generate a placeholder video from an asset image and ensure it's available in the target directory.
    /// </summary>
    /// <param name="targetDirectory">The target directory to place the placeholder video</param>
    /// <param name="targetFile">The target file name</param>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <returns>True if successful, false otherwise</returns>
    private async Task<bool> GeneratePlaceholderAsync(string targetDirectory, string targetFile, string assetName, bool overwrite = true)
    {
        try
        {
            if (!FolderUtils.FolderExistsThrowNull(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // Use target file from the movie or season generator
            var targetPath = Path.Combine(targetDirectory, targetFile);

            // Title promo from the title's own artwork; movies keep it in the movie folder, shows above the season folder
            if (Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos)))
            {
                var titleFolder = assetName == MovieAsset ? targetDirectory : Path.GetDirectoryName(targetDirectory);
                if (titleFolder != null && await TryTitlePromoAsync(titleFolder, targetPath, assetName))
                {
                    DeleteExtraPlaceholders(targetDirectory, targetFile);
                    return true;
                }
            }

            // Ensure cached placeholder exists
            var cachedPath = await EnsureCachedPlaceholderAsync(assetName);
            if (string.IsNullOrEmpty(cachedPath))
            {
                return false;
            }

            // Skip the copy when the existing placeholder already matches the cached video
            if (!overwrite && File.Exists(targetPath) && new FileInfo(targetPath).Length == new FileInfo(cachedPath).Length)
            {
                _logger.LogTrace("Placeholder already up to date: {TargetPath}", targetPath);
                DeleteExtraPlaceholders(targetDirectory, targetFile);
                return true;
            }

            // Copy cached file to target directory
            CopyReplacing(cachedPath, targetPath);
            _logger.LogTrace("Copied placeholder to {TargetPath} (overwrite enabled)", targetPath);

            // Delete all extra placeholders except the designated one
            DeleteExtraPlaceholders(targetDirectory, targetFile);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating placeholder video: {AssetName} -> {TargetDirectory}", 
                assetName, targetDirectory);
            return false;
        }
    }
    
    /// <summary>
    /// Ensures an embedded asset is extracted to the temp directory.
    /// Uses a semaphore per asset name to prevent race conditions when multiple tasks try to extract the same asset simultaneously.
    /// </summary>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <returns>Path to the extracted asset file, or null if failed</returns>
    private async Task<string?> EnsureAssetExtractedAsync(string assetName)
    {
        string assetFilepath = Path.Combine(_assetPath, assetName);
        SemaphoreSlim extractAssetSemaphore = _assetExtractionSemaphores.GetOrAdd(assetName, _ => new SemaphoreSlim(1, 1));
        
        var timeout = TimeSpan.FromMinutes(Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes)));
        await extractAssetSemaphore.WaitAsync(timeout);
        try
        {
            if (!File.Exists(assetFilepath))
            {
                bool useDefaultAsset = false;
                bool useCustomAsset = false;
                string customAssetPath = string.Empty;
                
                // Get configuration for this asset type
                if (assetName == MovieAsset)
                {
                    useDefaultAsset = Plugin.GetConfigOrDefault<bool>("DefaultMoviesPromo");
                    customAssetPath = Plugin.GetConfigOrDefault<string>("CustomMoviesPromo");
                }
                else if (assetName == SeasonAsset)
                {
                    useDefaultAsset = Plugin.GetConfigOrDefault<bool>("DefaultSeriesPromo");
                    customAssetPath = Plugin.GetConfigOrDefault<string>("CustomSeriesPromo");
                }
                else if (assetName == TitlePromoOverlayAsset)
                {
                    useDefaultAsset = true;
                }
                
                // Determine if we should use a custom asset
                if (!useDefaultAsset)
                {
                    if (!string.IsNullOrEmpty(customAssetPath) && File.Exists(customAssetPath))
                    {
                        useCustomAsset = true;
                    }
                    else
                    {
                        useCustomAsset = false;
                        _logger.LogWarning("Custom asset for {AssetName} is not configured or does not exist: {CustomAssetPath}", 
                            assetName, customAssetPath);
                    }
                }
                
                // Build asset (custom or embedded)
                if (useCustomAsset)
                {
                    _logger.LogTrace("Using custom asset for {AssetName}: {CustomAssetPath}", assetName, customAssetPath);
                    File.Copy(customAssetPath, assetFilepath, overwrite: true);
                    _logger.LogTrace("Copied custom asset to {AssetPath}", assetFilepath);
                }
                else
                {
                    _logger.LogTrace("Using default asset for {AssetName}: {assetFilepath}", assetName, assetFilepath);
                
                    // Construct the embedded resource name programmatically
                    // Pattern: {RootNamespace}.{FolderPath}.{FileName}
                    // Example: Jellyfin.Plugin.JellyBridge.Assets.movie.png
                    // Embedded resources use RootNamespace from csproj, which matches the root of the type namespace
                    var assembly = typeof(PlaceholderVideoGenerator).Assembly;
                    
                    // Get root namespace from a type in the root namespace (e.g., Plugin class)
                    var rootNamespace = typeof(Plugin).Namespace ?? throw new InvalidOperationException("Plugin.Namespace is null");
                    
                    // Map the asset name to the correct folder structure
                    // For example, "movie.png" should be mapped to "Assets/movie.png"
                    var mappedAssetName = EmbeddedAsset.TryGetValue(assetName, out var value) ? value : assetName;

                    // Construct resource name: RootNamespace.Assets.assetName
                    var resourceName = $"{rootNamespace}.Assets.{mappedAssetName}";
                    
                    _logger.LogTrace("Looking for embedded resource: {ResourceName} (root namespace: {RootNamespace})", 
                        resourceName, rootNamespace);
                    
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    
                    if (stream == null)
                    {
                        var allResources = assembly.GetManifestResourceNames();
                        var errorMessage = $"Embedded asset not found: {mappedAssetName}. Tried: {resourceName}. Available resources: {string.Join(", ", allResources)}";
                        throw new InvalidOperationException(errorMessage);
                    }
                    
                    using var fileStream = File.Create(assetFilepath);
                    await stream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                    
                    _logger.LogTrace("Extracted embedded asset: {AssetPath}", assetFilepath);
                }
            }
            return assetFilepath;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to extract asset: {AssetName}", assetName);
            return null;
        }
        finally
        {
            extractAssetSemaphore.Release();
        }
    }

    /// <summary>
    /// Generate a placeholder video from an asset image using FFmpeg.
    /// </summary>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <param name="outputPath">The output video file path</param>
    /// <returns>True if successful, false otherwise</returns>
    private async Task<bool> GeneratePlaceholderVideoAsync(string assetName, string outputFilepath)
    {
        try
        {
            // Check for a custom placeholder asset first; fall back to embedded extraction
            var assetPath = await EnsureAssetExtractedAsync(assetName);

            if (string.IsNullOrEmpty(assetPath))
            {
                _logger.LogError("Asset file not found: {AssetName}", assetName);
                return false;
            }

            // Ensure output directory exists, defaults to throw if directory is null
            var outputDir = Path.GetDirectoryName(outputFilepath) ?? string.Empty;
            if (!FolderUtils.FolderExistsThrowNull(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Resolve duration from configuration
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PromoVideoDurationSeconds));
            
            if (videoDuration <= 0)
            {
                _logger.LogError("Invalid duration: {Duration}. Must be greater than 0.", videoDuration);
                return false;
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set target resolution for the placeholder video
            int width = 1920;
            int height = 1080;

            // Build FFmpeg command
            // Scale down to 1920x1080 max (only if larger), preserve aspect ratio, ensure even dimensions for yuv420p
            var vf = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black," + "format=yuv420p";
            var arguments =new string[]
            {
                "-loop", "1",
                "-i", assetPath,
                "-t", videoDuration.ToString(),
                "-vf", vf,
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                outputFilepath
            };
            foreach (string arg in arguments)
            {
                processInfo.ArgumentList.Add(arg);
            }

            _logger.LogTrace("Generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputFilepath);
            _logger.LogTrace("Asset path: {AssetPath}, Duration: {Duration}", 
                assetPath, videoDuration);
            _logger.LogTrace("FFmpeg command: {FFmpegPath} {Arguments}", 
                _mediaEncoder.EncoderPath, arguments);

            using var process = new Process { StartInfo = processInfo };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Verify the output file was created and has content
                if (!File.Exists(outputFilepath))
                {
                    _logger.LogError("FFmpeg succeeded but output file does not exist: {OutputPath}", outputFilepath);
                    return false;
                }
                
                var outputFileInfo = new FileInfo(outputFilepath);
                if (outputFileInfo.Length == 0)
                {
                    _logger.LogError("FFmpeg succeeded but output file is empty: {OutputPath}", outputFilepath);
                    return false;
                }
                
                _logger.LogDebug("Successfully generated placeholder video: {OutputPath} ({Size} bytes)", 
                    outputFilepath, outputFileInfo.Length);
                _logger.LogTrace("FFmpeg output: {Output}", outputBuilder.ToString());
                return true;
            }
            else
            {
                _logger.LogError("FFmpeg failed with exit code {ExitCode}. Error output: {ErrorOutput}", 
                    process.ExitCode, errorBuilder.ToString());
                _logger.LogTrace("FFmpeg output: {Output}", outputBuilder.ToString());
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputFilepath);
            return false;
        }
    }

    /// <summary>
    /// Deletes all files with .mp4 extension in the given folder except the designated asset file.
    /// </summary>
    /// <param name="folderPath">The folder to clean up</param>
    /// <param name="fileAsset">The file name (with extension) to keep</param>
    private void DeleteExtraPlaceholders(string folderPath, string fileAsset)
    {
        if (FolderUtils.FolderExistsThrowNull(folderPath))
        {
            var allPlaceholders = Directory.GetFiles(folderPath, AssetSearchPattern, SearchOption.TopDirectoryOnly)
                .Where(f => !string.Equals(Path.GetFileName(f), fileAsset));
            foreach (var file in allPlaceholders)
            {
                try {
                    File.Delete(file); _logger.LogTrace("Deleted extra placeholder: {File}", file);
                } catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete extra placeholder: {File}", file);
                }
            }
        }
    }

    /// <summary>
    ///     Get the cached placeholder video for movies, generating it if necessary.
    /// </summary>
    public async Task<string?> GetMoviesPlaceholderAsync()
    {
        return await EnsureCachedPlaceholderAsync(MovieAsset);
    }

    /// <summary>
    ///     Get the cached placeholder video for series, generating it if necessary.
    /// </summary>
    public async Task<string?> GetSeriesPlaceholderAsync()
    {
        return await EnsureCachedPlaceholderAsync(SeasonAsset);
    }

    /// <summary>
    /// Refreshes all existing placeholder videos in the library by regenerating them from the current assets.
    /// </summary>
    /// <returns>A tuple containing lists of successful and failed item paths.</returns>
    public async Task<(List<string> successMovies, List<string> successSeries,
        List<string> failures)> RefreshAllPlaceholdersAsync()
    {
        List<string> successMovies = new List<string>();
        List<string> successSeries = new List<string>();
        List<string> failures = new List<string>();

        // Get the library root once
        string libraryRoot = FolderUtils.GetBaseDirectory();
        if (!FolderUtils.FolderExistsThrowNull(libraryRoot))
        {
            _logger.LogDebug("Library directory not configured or doesn't exist, skipping placeholder refresh");
            return (successMovies, successSeries, failures);
        }

        // Invalidate cache once at the start, and render title promos again
        _titlePromosValidAfterUtc = DateTime.UtcNow;
        await InvalidateCachedPlaceholdersAsync();
        try
        {
            List<Task> tasks = new List<Task>();
            ConcurrentBag<string> taskSuccessMovies = new ConcurrentBag<string>();
            ConcurrentBag<string> taskSuccessSeries = new ConcurrentBag<string>();
            ConcurrentBag<string> taskFailure = new ConcurrentBag<string>();

            // Find all existing placeholder files in the library
            var existingFiles = Directory.GetFiles(libraryRoot, AssetSearchPattern, SearchOption.AllDirectories);

            // Process each existing placeholder file in parallel
            foreach (string existingFile in existingFiles)
            {
                tasks.Add(Task.Run(async delegate
                {
                    try
                    {
                        // Get the parent directory of the existing placeholder
                        var directoryName = FolderUtils.GetExistingFolderOrThrow(Path.GetDirectoryName(existingFile) ?? string.Empty);

                        // Regenerate based on asset type
                        var movieFile = Path.Combine(directoryName, GetVideoFilename(MovieAsset));
                        var seriesFile = Path.Combine(directoryName, GetVideoFilename(SeasonAsset));
                        var seasonNumber = ParseSeasonPlaceholderNumber(existingFile);
                        if (seasonNumber.HasValue)
                        {
                            // Season placeholder "Season NN/SNNE9999.mp4", regenerate from the series asset
                            var showFolderPath = FolderUtils.GetExistingFolderOrThrow(Path.GetDirectoryName(directoryName) ?? string.Empty);
                            await GeneratePlaceholderSeasonNumberAsync(showFolderPath, seasonNumber.Value, overwrite: true);
                            taskSuccessSeries.Add(existingFile);
                        }
                        else if (File.Exists(movieFile))
                        {
                            // For movies, the placeholder is in the movie folder itself
                            await GeneratePlaceholderMovieAsync(directoryName);
                            taskSuccessMovies.Add(existingFile);
                        }
                        else if (File.Exists(seriesFile))
                        {
                            // For series, we need to get the show folder (parent of the season folder)
                            var parentFolderPath = FolderUtils.GetExistingFolderOrThrow(Path.GetDirectoryName(directoryName) ?? string.Empty);
                            await GeneratePlaceholderSeasonAsync(parentFolderPath);
                            taskSuccessSeries.Add(existingFile);
                        }
                        else
                        {
                            throw new InvalidOperationException("Unknown asset type for video file: " + existingFile);
                        }

                        _logger.LogTrace("Refreshed placeholder: {ExistingFile}", existingFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to refresh placeholder: {ExistingFile}", existingFile);
                        taskFailure.Add(existingFile);
                    }
                }));
            }

            // Wait for all parallel tasks to complete for this asset type
            await Task.WhenAll(tasks);

            // Add results to the overall lists
            successMovies.AddRange(taskSuccessMovies);
            successSeries.AddRange(taskSuccessSeries);
            failures.AddRange(taskFailure);

            _logger.LogDebug("Refreshed {CountMovies} Movies and {CountSeries} Series out of {Total}", 
                taskSuccessMovies.Count, taskSuccessSeries.Count, tasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing placeholders tasks");
        }

        return (successMovies, successSeries, failures);
    }

    /// <summary>
    /// Makes the placeholder from the title's backdrop and logo with the promo overlay on top.
    /// Returns false when the title has no backdrop yet or rendering fails, so the caller uses the default promo;
    /// the next sync tries again once Jellyfin has saved the artwork. Renders once per title and artwork change,
    /// and the season placeholders of a show share one render.
    /// </summary>
    private async Task<bool> TryTitlePromoAsync(string titleFolder, string targetPath, string assetName)
    {
        try
        {
            string? backdrop = FindArtwork(titleFolder, BackdropPatterns);
            if (backdrop == null)
            {
                return false;
            }
            var logo = FindArtwork(titleFolder, LogoPatterns);
            var artworkTime = File.GetLastWriteTimeUtc(backdrop);
            if (logo != null && File.GetLastWriteTimeUtc(logo) > artworkTime)
            {
                artworkTime = File.GetLastWriteTimeUtc(logo);
            }

            // A file the size of the default promo is the default promo, not a title promo
            var defaultPromo = await EnsureCachedPlaceholderAsync(assetName);
            var defaultLength = defaultPromo != null ? new FileInfo(defaultPromo).Length : -1;
            bool IsCurrent(string path)
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length > 0 && info.Length != defaultLength
                    && info.LastWriteTimeUtc >= artworkTime && info.LastWriteTimeUtc >= _titlePromosValidAfterUtc;
            }

            if (IsCurrent(targetPath))
            {
                return true;
            }

            var duration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PromoVideoDurationSeconds));
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(titleFolder)))[..16];
            var cachePath = Path.Combine(_placeholderPath, $"title_{key}_{duration}{AssetExtension}");
            var semaphore = _cacheGenerationSemaphores.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var rendered = IsCurrent(cachePath);
                for (var attempt = 1; !rendered && attempt <= 2; attempt++)
                {
                    if (attempt > 1)
                    {
                        // Jellyfin may be replacing the artwork right now (backdrop.jpg can come back as backdrop1.jpg), so look again
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        backdrop = FindArtwork(titleFolder, BackdropPatterns);
                        logo = FindArtwork(titleFolder, LogoPatterns);
                        if (backdrop == null)
                        {
                            break;
                        }
                    }
                    rendered = await RenderTitlePromoAsync(backdrop, logo, duration, cachePath);
                }

                if (!rendered)
                {
                    // Keep an existing title promo rather than going back to the default promo; the next sync tries again
                    var existing = new FileInfo(targetPath);
                    return existing.Exists && existing.Length > 0 && existing.Length != defaultLength;
                }
            }
            finally
            {
                semaphore.Release();
            }

            CopyReplacing(cachePath, targetPath);
            File.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow);
            _logger.LogTrace("Copied title promo to {TargetPath}", targetPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not make a title promo for {TitleFolder}, using the default promo", titleFolder);
            return false;
        }
    }

    private static string? FindArtwork(string folder, string[] patterns) =>
        patterns.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            .FirstOrDefault(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                && new FileInfo(path).Length > 0);

    /// <summary>
    /// Renders a title promo: the backdrop fills the frame, the overlay darkens it and adds the
    /// "favorite to request" note, and the logo (if any) sits just above the note. The picture is
    /// composed once and repeated at 2 fps, so the render takes about the same time for any duration.
    /// </summary>
    private async Task<bool> RenderTitlePromoAsync(string backdrop, string? logo, int duration, string outputPath)
    {
        var overlay = await EnsureAssetExtractedAsync(TitlePromoOverlayAsset);
        if (overlay == null || duration <= 0)
        {
            return false;
        }

        var arguments = new List<string> { "-y", "-nostdin", "-i", backdrop, "-i", overlay };
        var filter = "[0:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,setsar=1[bg];[bg][1:v]overlay=0:0";
        if (logo != null)
        {
            arguments.AddRange(new[] { "-i", logo });
            filter += "[base];[2:v]scale=760:300:force_original_aspect_ratio=decrease[logo];[base][logo]overlay=x=140:y=630-overlay_h";
        }
        // 2 fps: a 1-second promo needs at least two frames, or players like mpv treat it as a still image
        filter += ",format=yuv420p,loop=loop=-1:size=1,fps=2[v]";
        arguments.AddRange(new[]
        {
            "-filter_complex", filter, "-map", "[v]",
            "-t", duration.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "veryfast", "-tune", "stillimage", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
            outputPath
        });

        await _titlePromoRenderSemaphore.WaitAsync();
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                processInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("FFmpeg did not start");
            var errorOutput = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                _logger.LogWarning("FFmpeg could not render the title promo {OutputPath} (exit code {ExitCode}): {ErrorOutput}",
                    outputPath, process.ExitCode, await errorOutput);
                return false;
            }

            _logger.LogTrace("Rendered title promo {OutputPath}", outputPath);
            return true;
        }
        finally
        {
            _titlePromoRenderSemaphore.Release();
        }
    }

    /// <summary>
    /// Invalidates the cached placeholder videos by deleting the temp directories and recreating them.
    /// </summary>
    private async Task InvalidateCachedPlaceholdersAsync()
    {
        // Get the configured temp folder, defaulting to system temp path if not set
        var jellyBridgeTempDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyBridgeTempDirectory));
        // Set timeout to default task timeout
        var timeout = TimeSpan.FromMinutes(Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes)));
        await _invalidationSemaphore.WaitAsync(timeout);
        try
        {
            // Assets are embedded in the plugin assembly
            _assetPath = Path.Combine(jellyBridgeTempDirectory, "assets");
            _placeholderPath = Path.Combine(jellyBridgeTempDirectory, "placeholders");
            // Delete any existing assets and placeholders to ensure a clean state
            if (FolderUtils.FolderExistsThrowNull(_assetPath))
            {
                Directory.Delete(_assetPath, recursive: true);
            }
            if (FolderUtils.FolderExistsThrowNull(_placeholderPath))
            {
                Directory.Delete(_placeholderPath, recursive: true);
            }
            Directory.CreateDirectory(_assetPath);
            Directory.CreateDirectory(_placeholderPath);
            _logger.LogTrace("FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}, Placeholder video path: {PlaceholderPath}", 
                _mediaEncoder.EncoderPath, _assetPath, _placeholderPath);
        }
        finally
        {
            _invalidationSemaphore.Release();
        }
    }
}
