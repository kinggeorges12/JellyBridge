using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using MediaBrowser.Controller.MediaEncoding;

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

    private readonly DebugLogger<PlaceholderVideoGenerator> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly string _assetsPath;
    private readonly string _placeholderPath;
    
    // Asset file names for different media types
    private static readonly string MovieAsset = "movie.png";
    private static readonly string ShowAsset = "show.png";
    private static readonly string SeasonAsset = "S00E999.png";
    
    // Season folder name
    private static readonly string SeasonFolderName = "Season 00";
    
    // Asset file extension
    public static readonly string AssetExtension = ".mp4";

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder)
    {
        _logger = new DebugLogger<PlaceholderVideoGenerator>(logger);
        _mediaEncoder = mediaEncoder;

        // Get the configured temp folder, defaulting to system temp path if not set
        var jellyBridgeTempDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyBridgeTempDirectory));
        // Assets are embedded in the plugin assembly
        _assetsPath = Path.Combine(jellyBridgeTempDirectory, "assets");
        Directory.CreateDirectory(_assetsPath);
        _placeholderPath = Path.Combine(jellyBridgeTempDirectory, "placeholders");
        Directory.CreateDirectory(_placeholderPath);
        
        _logger.LogTrace("FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}, Placeholder video path: {PlaceholderPath}", 
            _mediaEncoder.EncoderPath, _assetsPath, _placeholderPath);
    }

    /// <summary>
    /// Generate a placeholder video for a movie (asset: movie.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderMovieAsync(string movieFolderPath)
    {
        return await GeneratePlaceholderAsync(movieFolderPath, MovieAsset);
    }

    /// <summary>
    /// Get the season folder path for a show, creating it if it doesn't exist.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    /// <returns>The path to the season folder.</returns>
    private string GetSeasonFolder(string showFolderPath)
    {
        var seasonFolderPath = Path.Combine(showFolderPath, SeasonFolderName);
        
        // Create season folder if it doesn't exist
        if (!Directory.Exists(seasonFolderPath))
        {
            Directory.CreateDirectory(seasonFolderPath);
            _logger.LogDebug("Created season folder: '{SeasonFolderPath}'", seasonFolderPath);
        }
        
        return seasonFolderPath;
    }

    /// <summary>
    /// Generate a placeholder video for a season (asset: season.png).
    /// Takes the show folder path and calculates the season folder internally.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    public async Task<bool> GeneratePlaceholderSeasonAsync(string showFolderPath)
    {
        var seasonFolderPath = GetSeasonFolder(showFolderPath);
        return await GeneratePlaceholderAsync(seasonFolderPath, SeasonAsset);
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
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));
            var assetStem = Path.GetFileNameWithoutExtension(assetName);
            var cachePath = Path.Combine(_placeholderPath, $"{assetStem}_{videoDuration}{AssetExtension}");

            // Get or create a semaphore for this specific cache path to serialize generation
            var semaphore = _cacheGenerationSemaphores.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
            
            await semaphore.WaitAsync();
            try
            {
                // Double-check pattern: after acquiring the lock, check if file was already created by another task
                if (File.Exists(cachePath))
                {
                    _logger.LogTrace("Cached placeholder already exists: {CachePath}", cachePath);
                    return cachePath;
                }
                
                // Wait for the file to be created and have content before releasing the semaphore
                // This handles cases where the file system is still writing the file
                const int maxAttempts = 5;
                
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    _logger.LogTrace("Waiting for cache file to be ready, attempt {Attempt}/{TotalAttempts}: {CachePath}", 
                        attempt, maxAttempts, cachePath);
                    
                    if (File.Exists(cachePath))
                    {
                        var fileInfo = new FileInfo(cachePath);
                        if (fileInfo.Length > 0)
                        {
                            try {
                                using (FileStream stream = File.Open(cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                {
                                    _logger.LogTrace("Cache file is ready: {CachePath} ({Size} bytes) (attempt {Attempt}/{TotalAttempts})", 
                                        cachePath, fileInfo.Length, attempt, maxAttempts);
                                }
                                return cachePath;
                            }
                            catch (Exception)
                            {
                                _logger.LogTrace("Failed to open cache file, waiting...: {CachePath}", cachePath);
                            }
                        }
                        else
                        {
                            _logger.LogTrace("Cache file exists but is empty, waiting...: {CachePath}", cachePath);
                        }
                    }
                    else
                    {
                        _logger.LogTrace("Cached placeholder not found for {Asset}, generating at {CachePath}", assetName, cachePath);

                        var ok = await GeneratePlaceholderVideoAsync(assetName, cachePath);
                        if (!ok)
                        {
                            _logger.LogTrace("Generating video failed, waiting...: {CachePath}", cachePath);
                        }
                    }
                    
                    // Wait with exponential backoff (1s, 2s, 4s)
                    var waitSeconds = Math.Pow(2, attempt - 1);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                }
                
                // If we get here, all retry attempts failed
                _logger.LogError("Cache file was not created after waiting 30 seconds: {CachePath}", cachePath);
                
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
    /// Generate a placeholder video from an asset image and ensure it's available in the target directory.
    /// </summary>
    /// <param name="targetDirectory">The target directory to place the placeholder video</param>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <returns>True if successful, false otherwise</returns>
    private async Task<bool> GeneratePlaceholderAsync(string targetDirectory, string assetName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                return false;
            }

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // Ensure cached placeholder exists
            var cachedPath = await EnsureCachedPlaceholderAsync(assetName);
            if (string.IsNullOrEmpty(cachedPath))
            {
                return false;
            }

            // Determine target file name based on asset
            var assetStem = Path.GetFileNameWithoutExtension(assetName);
            var targetFile = assetStem + AssetExtension;
            var targetPath = Path.Combine(targetDirectory, targetFile);

            // Copy cached file to target directory
            File.Copy(cachedPath, targetPath, overwrite: true);
            _logger.LogTrace("Copied placeholder to {TargetPath} (overwrite enabled)", targetPath);

            // Delete all extra placeholders except the designated one
            DeleteExtraPlaceholders(targetDirectory, assetName);

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
        // Get or create a semaphore for this specific asset to serialize extraction
        var semaphore = _assetExtractionSemaphores.GetOrAdd(assetName, _ => new SemaphoreSlim(1, 1));
        
        await semaphore.WaitAsync();
        try
        {
            var assetPath = Path.Combine(_assetsPath, assetName);
            
            // After acquiring the lock, check if file was already created by another task
            if (File.Exists(assetPath))
            {
                _logger.LogTrace("Asset already extracted: {AssetPath}", assetPath);
                return assetPath;
            }
            
            // Construct the embedded resource name programmatically
            // Pattern: {RootNamespace}.{FolderPath}.{FileName}
            // Example: Jellyfin.Plugin.JellyBridge.Assets.movie.png
            // Embedded resources use RootNamespace from csproj, which matches the root of the type namespace
            var assembly = typeof(PlaceholderVideoGenerator).Assembly;
            
            // Get root namespace from a type in the root namespace (e.g., Plugin class)
            var rootNamespace = typeof(Plugin).Namespace ?? throw new InvalidOperationException("Plugin.Namespace is null");
            
            // Construct resource name: RootNamespace.Assets.assetName
            var resourceName = $"{rootNamespace}.Assets.{assetName}";
            
            _logger.LogTrace("Looking for embedded resource: {ResourceName} (root namespace: {RootNamespace})", 
                resourceName, rootNamespace);
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                var allResources = assembly.GetManifestResourceNames();
                var errorMessage = $"Embedded asset not found: {assetName}. Tried: {resourceName}. Available resources: {string.Join(", ", allResources)}";
                throw new InvalidOperationException(errorMessage);
            }
            
            using var fileStream = File.Create(assetPath);
            await stream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
            
            _logger.LogTrace("Extracted embedded asset: {AssetPath}", assetPath);
            return assetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract asset: {AssetName}", assetName);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Generate a placeholder video from an asset image using FFmpeg.
    /// </summary>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <param name="outputPath">The output video file path</param>
    /// <returns>True if successful, false otherwise</returns>
    private async Task<bool> GeneratePlaceholderVideoAsync(string assetName, string outputPath)
    {
        try
        {
            var assetPath = await EnsureAssetExtractedAsync(assetName);
            
            if (string.IsNullOrEmpty(assetPath))
            {
                _logger.LogError("Asset file not found: {AssetName}", assetName);
                return false;
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Resolve duration from configuration
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));
            
            if (videoDuration <= 0)
            {
                _logger.LogError("Invalid duration: {Duration}. Must be greater than 0.", videoDuration);
                return false;
            }

            // Build FFmpeg command
            var arguments = $"-loop 1 -i \"{assetPath}\" -t {videoDuration} -vf \"format=yuv420p\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";

            _logger.LogTrace("Generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputPath);
            _logger.LogTrace("Asset path: {AssetPath}, Duration: {Duration}", 
                assetPath, videoDuration);
            _logger.LogTrace("FFmpeg command: {FFmpegPath} {Arguments}", 
                _mediaEncoder.EncoderPath, arguments);

            var processInfo = new ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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
                if (!File.Exists(outputPath))
                {
                    _logger.LogError("FFmpeg succeeded but output file does not exist: {OutputPath}", outputPath);
                    return false;
                }
                
                var outputFileInfo = new FileInfo(outputPath);
                if (outputFileInfo.Length == 0)
                {
                    _logger.LogError("FFmpeg succeeded but output file is empty: {OutputPath}", outputPath);
                    return false;
                }
                
                _logger.LogDebug("Successfully generated placeholder video: {OutputPath} ({Size} bytes)", 
                    outputPath, outputFileInfo.Length);
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
                assetName, outputPath);
            return false;
        }
    }

    /// <summary>
    /// Deletes all files with AssetExtension in the given folder except the designated asset file.
    /// </summary>
    /// <param name="folderPath">The folder to clean up</param>
    /// <param name="fileAsset">The file name (with extension) to keep</param>
    private void DeleteExtraPlaceholders(string folderPath, string fileAsset)
    {
        if (Directory.Exists(folderPath))
        {
            var allPlaceholders = Directory.GetFiles(folderPath, "*" + AssetExtension, SearchOption.TopDirectoryOnly)
                .Where(f => !string.Equals(Path.GetFileName(f), fileAsset, StringComparison.OrdinalIgnoreCase));
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
    /// Deletes placeholder video files from the specified directory and its subfolders.
    /// </summary>
    /// <param name="bridgeFolderPath">The bridge folder path to clean up</param>
    /// <returns>Number of placeholder files deleted</returns>
    public async Task<int> DeletePlaceholderVideosAsync(string bridgeFolderPath)
    {
        var deletedCount = 0;
        var placeholderFiles = new[] { 
            Path.GetFileNameWithoutExtension(MovieAsset) + AssetExtension,
            Path.GetFileNameWithoutExtension(ShowAsset) + AssetExtension, 
            Path.GetFileNameWithoutExtension(SeasonAsset) + AssetExtension
        };

        try
        {
            if (string.IsNullOrEmpty(bridgeFolderPath) || !Directory.Exists(bridgeFolderPath))
            {
                throw new InvalidOperationException($"Bridge folder does not exist: {bridgeFolderPath}");
            }

            _logger.LogDebug("Cleaning up placeholder videos in: {BridgeFolderPath}", bridgeFolderPath);

            // Delete placeholder files in current directory and all subdirectories
            foreach (var placeholderFile in placeholderFiles)
            {
                var searchPattern = $"*{placeholderFile}";
                var files = Directory.GetFiles(bridgeFolderPath, searchPattern, SearchOption.AllDirectories);
                
                foreach (var filePath in files)
                {
                    try
                    {
                        await Task.Run(() => File.Delete(filePath));
                        _logger.LogTrace("Deleted placeholder file: {FilePath}", filePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete placeholder file: {FilePath}", filePath);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {DeletedCount} placeholder video files from {BridgeFolderPath}", 
                    deletedCount, bridgeFolderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up placeholder videos in {BridgeFolderPath}", bridgeFolderPath);
        }

        return deletedCount;
    }
}
