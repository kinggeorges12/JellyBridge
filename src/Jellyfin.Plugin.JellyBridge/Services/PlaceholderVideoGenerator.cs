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
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

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

    // Font for the text drawn over the backdrop: Noto Sans, the typeface of the text baked into the overlay, bundled so the
    // promo looks the same on every server. Its copyright and the SIL Open Font License text are stored inside the font file.
    private static readonly string TitlePromoFontAsset = "promo-font.ttf";

    // Layout of the 1920x1080 frame, top to bottom like a streaming service's title page: logo, info line, plot, then the
    // overlay's "favorite to request" note, which starts at y 680. The text block is stacked upwards from just above the
    // note and the logo moves up to make room; with no text the logo keeps its old place (bottom at y 630).
    // drawtext puts the top of the text at y; a line of the promo font (Noto Sans, like the overlay) takes about 1.3 x the font size plus the line spacing.
    private const int PromoTextLeft = 140;
    private const int PromoTextWidth = 1060;
    private const int PromoTextBottom = 650;
    private const int PromoLogoBottom = 630;
    private const int PromoLogoGap = 30;
    private const int PromoInfoFontSize = 34;
    private const int PromoInfoHeight = 33;
    private const int PromoInfoGap = 22;
    private const int PromoInfoMaxChars = 58;
    private const int PromoPlotFontSize = 34;
    private const int PromoPlotMaxLines = 3;
    private const int PromoPlotSpacing = 10;
    private const int PromoPlotLineAdvance = 56;
    private const int PromoPlotLastLineHeight = 33;
    private const int PromoBadgeY = 88;
    private const int PromoBadgeRight = 140;
    private const int PromoBadgeFontSize = 36;
    // Plain white with no outline or shadow, like the text baked into the overlay; the overlay darkens the side it sits on
    private const string PromoTextColor = "white@0.96";

    // TMDB genre ids, for titles Jellyfin has not scanned yet (metadata.json only has the ids)
    private static readonly Dictionary<int, string> TmdbGenres = new()
    {
        { 28, "Action" }, { 12, "Adventure" }, { 16, "Animation" }, { 35, "Comedy" }, { 80, "Crime" }, { 99, "Documentary" },
        { 18, "Drama" }, { 10751, "Family" }, { 14, "Fantasy" }, { 36, "History" }, { 27, "Horror" }, { 10402, "Music" },
        { 9648, "Mystery" }, { 10749, "Romance" }, { 878, "Science Fiction" }, { 10770, "TV Movie" }, { 53, "Thriller" },
        { 10752, "War" }, { 37, "Western" }, { 10759, "Action & Adventure" }, { 10762, "Kids" }, { 10763, "News" },
        { 10764, "Reality" }, { 10765, "Sci-Fi & Fantasy" }, { 10766, "Soap" }, { 10767, "Talk" }, { 10768, "War & Politics" },
    };

    // Records the text that went into each rendered title promo, so a title is re-rendered only when its text changes
    private string _promoTextPath = "";
    private readonly JellyfinILibraryManager _libraryManager;

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

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder, JellyfinILibraryManager libraryManager)
    {
        _logger = new DebugLogger<PlaceholderVideoGenerator>(logger);
        _mediaEncoder = mediaEncoder;
        _libraryManager = libraryManager;
        // Read the temp folder from the config and start with an empty placeholder cache
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
    /// When set (for the current async flow), placeholders use the default promo even with title promos on. Search sets it
    /// so Jellyfin can build a new item without waiting for the title promo render.
    /// </summary>
    public static readonly AsyncLocal<bool> DefaultPromosOnly = new();

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
            if (Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos)) && !DefaultPromosOnly.Value)
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
                else if (assetName == TitlePromoOverlayAsset || assetName == TitlePromoFontAsset)
                {
                    // Always the asset that ships with the plugin; there is no custom font or overlay to choose
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
    /// Swaps the default promos in a title folder for its title promo once Jellyfin has saved the title's backdrop.
    /// A sync only makes placeholders for the titles it fetched, so a title that left the fetched pages before its artwork
    /// arrived would keep the default promo until it is removed. Cheap when there is nothing to do: a few file lookups.
    /// Returns the number of placeholders replaced.
    /// </summary>
    public async Task<int> UpgradeDefaultPromosAsync(string titleFolder)
    {
        if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos))
            || !Directory.Exists(titleFolder) || FindArtwork(titleFolder, BackdropPatterns) == null)
        {
            return 0;
        }

        var placeholders = new List<(string Path, string Asset)>();
        var moviePath = Path.Combine(titleFolder, GetVideoFilename(MovieAsset));
        if (File.Exists(moviePath))
        {
            placeholders.Add((moviePath, MovieAsset));
        }
        else
        {
            foreach (var seasonFolder in Directory.EnumerateDirectories(titleFolder))
            {
                if (ParseSeasonFolderNumber(seasonFolder) is not { } seasonNumber)
                {
                    continue;
                }
                var file = Path.Combine(seasonFolder, seasonNumber == 0 ? GetVideoFilename(SeasonAsset) : GetSeasonPlaceholderFilename(seasonNumber));
                if (File.Exists(file))
                {
                    placeholders.Add((file, SeasonAsset));
                }
            }
        }

        var upgraded = 0;
        foreach (var (path, asset) in placeholders)
        {
            var defaultPromo = await EnsureCachedPlaceholderAsync(asset);
            if (defaultPromo == null || new FileInfo(path).Length != new FileInfo(defaultPromo).Length)
            {
                continue;
            }
            if (await TryTitlePromoAsync(titleFolder, path, asset))
            {
                upgraded++;
            }
        }
        if (upgraded > 0)
        {
            _logger.LogDebug("Replaced {Count} default promos with the title promo in {TitleFolder}", upgraded, titleFolder);
        }
        return upgraded;
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

            var duration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PromoVideoDurationSeconds));
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(titleFolder)))[..16];

            // What the promo should say, from the metadata Jellyfin (or, before its first scan, Jellyseerr) has for this title
            var promoText = ReadPromoText(titleFolder);
            var sigFile = Path.Combine(_promoTextPath, $"title_{key}.sig");
            var renderedText = File.Exists(sigFile) ? File.ReadAllText(sigFile) : null;
            // "plain" records that drawing text failed for this exact text, so it is not retried until the text changes
            var textToken = renderedText == "plain|" + promoText.Signature ? "plain" : "text";

            // The text is part of the cache name, so changed text never reuses a render made for the old text
            var cachePath = Path.Combine(_placeholderPath, $"title_{key}_{duration}_{textToken}_{promoText.Signature}{AssetExtension}");

            // A show's season placeholders all share one render: one that was copied before the current render was made
            // still shows the old text, even though the signature file already names the new one
            var cacheTime = File.Exists(cachePath) ? File.GetLastWriteTimeUtc(cachePath) : DateTime.MinValue;
            if (IsCurrent(targetPath) && renderedText == textToken + "|" + promoText.Signature
                && File.GetLastWriteTimeUtc(targetPath) >= cacheTime)
            {
                return true;
            }

            var semaphore = _cacheGenerationSemaphores.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var rendered = IsCurrent(cachePath);
                var textDrawn = textToken == "text";
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
                    var layout = textToken == "text" ? await BuildTextLayoutAsync(key, promoText) : PromoLayout.Plain;
                    rendered = await RenderTitlePromoAsync(backdrop, logo, duration, cachePath, layout);
                    if (!rendered && layout.Filters.Count > 0)
                    {
                        // Text needs FFmpeg's drawtext and a readable font; the artwork alone still makes a usable promo
                        _logger.LogWarning("Rendering the title promo for {TitleFolder} failed with text, retrying without it", titleFolder);
                        textDrawn = false;
                        rendered = await RenderTitlePromoAsync(backdrop, logo, duration, cachePath, PromoLayout.Plain);
                    }
                    DeletePromoTextFiles(key);
                }

                if (!rendered)
                {
                    // Keep an existing title promo rather than going back to the default promo; the next sync tries again
                    var existing = new FileInfo(targetPath);
                    return existing.Exists && existing.Length > 0 && existing.Length != defaultLength;
                }

                Directory.CreateDirectory(_promoTextPath);
                File.WriteAllText(sigFile, (textDrawn ? "text|" : "plain|") + promoText.Signature);
                DeleteOtherTitleRenders(key, cachePath);
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
    /// Renders a title promo: the backdrop fills the frame, the overlay darkens it and adds the "favorite to request"
    /// note, and the logo sits above the title's info and plot, which sit above the note.
    /// The picture is composed once and repeated at 2 fps, so a render costs the same whatever it says.
    /// </summary>
    private async Task<bool> RenderTitlePromoAsync(string backdrop, string? logo, int duration, string outputPath, PromoLayout layout)
    {
        var overlay = await EnsureAssetExtractedAsync(TitlePromoOverlayAsset);
        if (overlay == null || duration <= 0)
        {
            return false;
        }

        // Each stage hands its frame to a labelled pad, so text filters can be appended one at a time
        var arguments = new List<string> { "-y", "-nostdin", "-i", backdrop, "-i", overlay };
        var filter = "[0:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,setsar=1[bg];[bg][1:v]overlay=0:0[jb0]";
        var stage = "jb0";
        var nextStage = 1;
        if (logo != null)
        {
            arguments.AddRange(new[] { "-i", logo });
            filter += $";[2:v]scale=760:300:force_original_aspect_ratio=decrease[logo];[{stage}][logo]overlay=x=140:y={layout.LogoBottom}-overlay_h[jb{nextStage}]";
            (stage, nextStage) = ($"jb{nextStage}", nextStage + 1);
        }
        foreach (var textFilter in layout.Filters)
        {
            filter += $";[{stage}]{textFilter}[jb{nextStage}]";
            (stage, nextStage) = ($"jb{nextStage}", nextStage + 1);
        }
        // 2 fps: a 1-second promo needs at least two frames, or players like mpv treat it as a still image
        filter += $";[{stage}]format=yuv420p,loop=loop=-1:size=1,fps=2[v]";
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
    /// The text drawn over a title's backdrop: what the title is and when it comes out.
    /// All of it comes from metadata the server already has, so drawing it costs nothing at render time.
    /// </summary>
    private sealed record TitlePromoText(string? Info, string? Plot, string? Badge)
    {
        /// <summary>Changes whenever the drawn text changes, so a promo is only re-rendered when it has to be.</summary>
        public string Signature { get; init; } = "none";
    }

    /// <summary>
    /// The text filters for one promo and where the logo goes above them.
    /// </summary>
    private sealed record PromoLayout(IReadOnlyList<string> Filters, int LogoBottom)
    {
        public static readonly PromoLayout Plain = new(Array.Empty<string>(), PromoLogoBottom);
    }

    /// <summary>
    /// Reads what a title's promo should say. The Jellyfin item that owns the folder is preferred; until Jellyfin has
    /// scanned the folder (a title opened from search plays before that), the metadata.json the plugin wrote from
    /// Jellyseerr fills in.
    /// </summary>
    private TitlePromoText ReadPromoText(string titleFolder)
    {
        var info = (string?)null;
        var plot = (string?)null;
        var badge = (string?)null;
        try
        {
            var item = _libraryManager.FindJellyfinItemByPath(titleFolder);
            var meta = item == null ? ReadJellyseerrMetadata(titleFolder) : null;

            // Shortest and most useful first, so the genres are what gets dropped when the line runs out of room.
            // No runtime: Jellyfin measures it from the placeholder video (every movie would say 0m), and a show's
            // runtime is per episode.
            var parts = new List<string>();
            var year = item?.ProductionYear ?? meta?.Year;
            if (year is int y && y > 0)
            {
                parts.Add(y.ToString(CultureInfo.InvariantCulture));
            }
            // "Ended"/"Continuing" only says something about a show; a movie is simply released or not.
            // Read through the show so the enum type never has to be named - it moved between Jellyfin versions.
            if (item is MediaBrowser.Controller.Entities.TV.Series show)
            {
                var status = show.Status?.ToString();
                if (status is "Ended" or "Continuing")
                {
                    parts.Add(status == "Continuing" ? "Ongoing" : status);
                }
            }
            var rating = item?.CommunityRating ?? meta?.Rating;
            if (rating is float r && r > 0)
            {
                parts.Add("\u2605 " + r.ToString("0.0", CultureInfo.InvariantCulture));
            }
            var genres = item != null ? (item.Genres ?? Array.Empty<string>()) : (meta?.Genres ?? Array.Empty<string>());
            parts.AddRange(genres.Select(g => g.Trim()).Where(g => g.Length > 0));
            parts.RemoveAll(part => !CanDrawInPromoFont(part));
            info = FitOneLine(parts, PromoInfoMaxChars);

            plot = WrapPromoPlot(item != null ? item.Overview : meta?.Overview);
            if (!CanDrawInPromoFont(plot))
            {
                plot = null;
            }

            badge = UpcomingReleaseText(item != null ? ReleaseDayOf(item.PremiereDate) : meta?.ReleaseDay);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the title info for {TitleFolder}, rendering the promo without text", titleFolder);
        }
        var text = new TitlePromoText(info, plot, badge);
        return text with { Signature = PromoTextSignature(text) };
    }

    private sealed record JellyseerrPromoMetadata(int? Year, float? Rating, string[] Genres, string? Overview, DateTime? ReleaseDay);

    /// <summary>
    /// The few fields a promo needs from the metadata.json the plugin writes into every title folder. Read directly, so
    /// an unexpected value in an unrelated field can never stop the promo.
    /// </summary>
    private static JellyseerrPromoMetadata? ReadJellyseerrMetadata(string titleFolder)
    {
        var file = Path.Combine(titleFolder, IJellyseerrItem.GetMetadataFilename());
        if (!File.Exists(file))
        {
            return null;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        string? Str(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

        var dateText = Str("releaseDate") ?? Str("firstAirDate");
        DateTime? releaseDay = DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date : null;
        float? rating = root.TryGetProperty("voteAverage", out var va) && va.ValueKind == System.Text.Json.JsonValueKind.Number
            ? (float)va.GetDouble() : null;
        var genres = root.TryGetProperty("genreIds", out var ids) && ids.ValueKind == System.Text.Json.JsonValueKind.Array
            ? ids.EnumerateArray()
                .Where(id => id.ValueKind == System.Text.Json.JsonValueKind.Number && TmdbGenres.ContainsKey(id.GetInt32()))
                .Select(id => TmdbGenres[id.GetInt32()]).ToArray()
            : Array.Empty<string>();
        return new JellyseerrPromoMetadata(releaseDay?.Year, rating, genres, Str("overview"), releaseDay);
    }

    /// <summary>
    /// Whether the bundled font can draw every character of this text. It covers Latin with its accents, Greek,
    /// Cyrillic and the punctuation used here; a plot in Arabic, Hebrew, Thai or CJK would come out as a row of
    /// empty boxes, so that text is left off the promo instead.
    /// </summary>
    private static bool CanDrawInPromoFont(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch is '\n' or '\r' or '\t')
            {
                continue;
            }

            var supported = ch is (>= '\u0020' and <= '\u024F') or (>= '\u0370' and <= '\u04FF') or (>= '\u2000' and <= '\u206F')
                or (>= '\u20A0' and <= '\u20CF') or '\u2605';
            if (!supported)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Joins as many of the parts as fit on one line, keeping the ones that came first.
    /// </summary>
    private static string? FitOneLine(List<string> parts, int maxChars)
    {
        var line = "";
        foreach (var part in parts)
        {
            var candidate = line.Length == 0 ? part : line + " \u00b7 " + part;
            if (candidate.Length > maxChars && line.Length > 0)
            {
                break;
            }
            line = candidate;
        }
        return line.Length > 0 ? line : null;
    }

    /// <summary>
    /// Wraps the plot into the few lines a promo can carry, ending it with an ellipsis rather than a ragged word.
    /// Wraps by character count, which is close enough at the size and width used here.
    /// </summary>
    private static string? WrapPromoPlot(string? overview)
    {
        if (string.IsNullOrWhiteSpace(overview))
        {
            return null;
        }
        var maxChars = Math.Max(20, (int)(PromoTextWidth / (PromoPlotFontSize * 0.52)));
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in overview.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(word);
        }
        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        var cut = lines.Count > PromoPlotMaxLines;
        if (cut)
        {
            lines = lines.GetRange(0, PromoPlotMaxLines);
        }
        var plot = string.Join("\n", lines);
        if (cut)
        {
            plot = plot.TrimEnd('.', ',', ':', ';') + "\u2026";
        }
        return plot.Length > 0 ? plot : null;
    }

    /// <summary>
    /// The release day the details page shows. Jellyfin stores the date as local midnight converted to UTC.
    /// </summary>
    private static DateTime? ReleaseDayOf(DateTime? premiereDate)
    {
        if (premiereDate == null)
        {
            return null;
        }
        var utc = premiereDate.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(premiereDate.Value, DateTimeKind.Utc)
            : premiereDate.Value.ToUniversalTime();
        return utc.ToLocalTime().Date;
    }

    /// <summary>
    /// "Releases 12 Mar 2027" for a title that has not come out yet, which is the one thing worth knowing about a
    /// placeholder nobody can watch. Null once it is out, or when no date is known.
    /// </summary>
    private static string? UpcomingReleaseText(DateTime? releaseDay)
    {
        if (releaseDay == null || releaseDay.Value.Date <= DateTime.Today)
        {
            return null;
        }
        return "Releases " + releaseDay.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string PromoTextSignature(TitlePromoText text)
    {
        if (text.Info == null && text.Plot == null && text.Badge == null)
        {
            return "none";
        }
        // The layout is part of what is drawn, so a change to it re-renders every title once
        var joined = string.Join("\n", "layout8", text.Info ?? "", text.Plot ?? "", text.Badge ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Builds the FFmpeg filters that draw a title's text, stacked upwards from just above the "favorite to request"
    /// note, and works out where the logo goes above them. The text goes into files that drawtext reads with textfile,
    /// so quotes, newlines and accents in a plot never have to be escaped for the filter graph.
    /// </summary>
    private async Task<PromoLayout> BuildTextLayoutAsync(string key, TitlePromoText text)
    {
        if (text.Info == null && text.Plot == null && text.Badge == null)
        {
            return PromoLayout.Plain;
        }
        var font = await EnsureAssetExtractedAsync(TitlePromoFontAsset);
        if (string.IsNullOrEmpty(font))
        {
            return PromoLayout.Plain;
        }

        var filters = new List<string>();
        var top = PromoTextBottom;
        if (!string.IsNullOrEmpty(text.Plot))
        {
            var lines = text.Plot.Count(ch => ch == '\n') + 1;
            top -= ((lines - 1) * PromoPlotLineAdvance) + PromoPlotLastLineHeight;
            filters.Add(DrawTextFilter(WritePromoTextFile(key, "plot", text.Plot), font, top, PromoPlotFontSize,
                PromoTextLeft.ToString(CultureInfo.InvariantCulture), lineSpacing: PromoPlotSpacing));
        }
        if (!string.IsNullOrEmpty(text.Info))
        {
            top -= (top < PromoTextBottom ? PromoInfoGap : 0) + PromoInfoHeight;
            filters.Add(DrawTextFilter(WritePromoTextFile(key, "info", text.Info), font, top, PromoInfoFontSize,
                PromoTextLeft.ToString(CultureInfo.InvariantCulture)));
        }
        if (!string.IsNullOrEmpty(text.Badge))
        {
            // Top right corner, opposite the logo, where nothing else is drawn
            filters.Add(DrawTextFilter(WritePromoTextFile(key, "badge", text.Badge), font, PromoBadgeY, PromoBadgeFontSize,
                $"w-tw-{PromoBadgeRight}", boxed: true));
        }
        return new PromoLayout(filters, top < PromoTextBottom ? top - PromoLogoGap : PromoLogoBottom);
    }

    private static string DrawTextFilter(string textFile, string font, int y, int fontSize, string x, int lineSpacing = 0, bool boxed = false)
    {
        var filter = $"drawtext=fontfile={FilterGraphPath(font)}:textfile={FilterGraphPath(textFile)}:expansion=none:fontsize={fontSize}"
            + $":fontcolor={PromoTextColor}:x={x}:y={y}";
        if (lineSpacing > 0)
        {
            filter += $":line_spacing={lineSpacing}";
        }
        return boxed ? filter + ":box=1:boxcolor=black@0.6:boxborderw=18" : filter;
    }

    /// <summary>
    /// A file path as a drawtext option inside -filter_complex. FFmpeg unescapes such a value twice, once as a filter
    /// option (where ':' separates options) and once in the filter graph (where ',', ';' and '[' do), so a Windows
    /// path like C:\ProgramData\... must be escaped for both; plain quoting only survives the first pass.
    /// </summary>
    private static string FilterGraphPath(string path)
    {
        static string Escape(string value, string special)
        {
            var escaped = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                if (special.Contains(ch, StringComparison.Ordinal))
                {
                    escaped.Append('\\');
                }
                escaped.Append(ch);
            }
            return escaped.ToString();
        }

        var option = Escape(path.Replace('\\', '/'), "\\':");
        return Escape(option, "\\'[],;");
    }

    private string WritePromoTextFile(string key, string name, string content)
    {
        var path = Path.Combine(_placeholderPath, $"title_{key}_{name}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private void DeletePromoTextFiles(string key)
    {
        foreach (var name in new[] { "plot", "info", "badge" })
        {
            try
            {
                File.Delete(Path.Combine(_placeholderPath, $"title_{key}_{name}.txt"));
            }
            catch (IOException)
            {
                // Left for the cache cleanup at the next restart
            }
        }
    }

    /// <summary>
    /// Removes renders of this title made for text or a duration it no longer has, so the cache holds one per title.
    /// </summary>
    private void DeleteOtherTitleRenders(string key, string keepPath)
    {
        foreach (var path in Directory.EnumerateFiles(_placeholderPath, $"title_{key}_*{AssetExtension}"))
        {
            if (!string.Equals(path, keepPath, StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // In use by a copy right now; the next restart clears the cache anyway
                }
            }
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
            // Which text went into each title promo; kept outside the folders below so a restart does not re-render every title
            _promoTextPath = Path.Combine(jellyBridgeTempDirectory, "promo-text");
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
            Directory.CreateDirectory(_promoTextPath);
            _logger.LogTrace("FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}, Placeholder video path: {PlaceholderPath}", 
                _mediaEncoder.EncoderPath, _assetPath, _placeholderPath);
        }
        finally
        {
            _invalidationSemaphore.Release();
        }
    }
}
