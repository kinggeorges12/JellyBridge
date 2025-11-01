using System.Diagnostics;
using System.IO;
using System.Text;
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
    private readonly DebugLogger<PlaceholderVideoGenerator> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly string _assetsPath;
    
    // Asset file names for different media types
    private static readonly string MovieAsset = "movie.png";
    private static readonly string ShowAsset = "show.png";
    private static readonly string SeasonAsset = "S00E00.png";
    
    // Season folder name
    private static readonly string SeasonFolderName = "Season 00";
    
    // Asset file extension
    private static readonly string AssetExtension = ".mp4";

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder)
    {
        _logger = new DebugLogger<PlaceholderVideoGenerator>(logger);
        _mediaEncoder = mediaEncoder;
        
        // Assets are embedded in the plugin assembly
        _assetsPath = Path.Combine(Path.GetTempPath(), "JellyBridge", "assets");
        Directory.CreateDirectory(_assetsPath);
        
        _logger.LogTrace("FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}", 
            _mediaEncoder.EncoderPath, _assetsPath);
    }

    /// <summary>
    /// Generate a placeholder video for a movie (asset: movie.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderMovieAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, MovieAsset);
    }

    /// <summary>
    /// Generate a placeholder video for a TV show (asset: show.png).
    /// </summary>
    public Task<bool> GeneratePlaceholderShowAsync(string targetDirectory)
    {
        throw new NotSupportedException("GeneratePlaceholderShowAsync is obsolete and no longer supported. Use GeneratePlaceholderSeasonAsync instead.");
    }

    /// <summary>
    /// Get the season placeholder path for a show.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    /// <returns>The path to the season placeholder video (in the season folder).</returns>
    public static string GetSeasonPlaceholderPath(string showFolderPath)
    {
        var seasonFolderPath = GetSeasonFolder(showFolderPath);
        var assetStem = Path.GetFileNameWithoutExtension(SeasonAsset);
        var targetFile = assetStem + AssetExtension;
        return Path.Combine(seasonFolderPath, targetFile);
    }

    /// <summary>
    /// Get the season folder path for a show, creating it if it doesn't exist.
    /// </summary>
    /// <param name="showFolderPath">The path to the show folder.</param>
    /// <param name="logger">Optional logger instance for logging folder creation.</param>
    /// <returns>The path to the season folder.</returns>
    public static string GetSeasonFolder(string showFolderPath, ILogger<PlaceholderVideoGenerator>? logger = null)
    {
        var seasonFolderPath = Path.Combine(showFolderPath, SeasonFolderName);
        
        // Create season folder if it doesn't exist
        if (!Directory.Exists(seasonFolderPath))
        {
            Directory.CreateDirectory(seasonFolderPath);
            logger?.LogDebug("Created season folder: '{SeasonFolderPath}'", seasonFolderPath);
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
    /// </summary>
    /// <param name="assetName">The asset image filename to base the placeholder on (e.g., "movie.png")</param>
    /// <returns>Path to cached file if successful, null otherwise</returns>
    private async Task<string?> EnsureCachedPlaceholderAsync(string assetName)
    {
        try
        {
            // Build cache directory in the system temp path
            var cacheDir = Path.Combine(Path.GetTempPath(), "JellyBridge", "placeholders");
            Directory.CreateDirectory(cacheDir);
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));
            var assetStem = Path.GetFileNameWithoutExtension(assetName);
            var cachePath = Path.Combine(cacheDir, $"{assetStem}_{videoDuration}{AssetExtension}");

            if (!File.Exists(cachePath))
            {
                _logger.LogTrace("Cached placeholder not found for {Asset}, generating at {CachePath}", assetName, cachePath);
                
                // Check if FFmpeg is available before attempting to generate (with retry logic)
                if (!await IsFFmpegAvailableAsync())
                {
                    _logger.LogError("FFmpeg is not available, cannot generate placeholder for {Asset}", assetName);
                    return null;
                }
                
                var ok = await GeneratePlaceholderVideoAsync(assetName, cachePath);
                if (!ok)
                {
                    return null;
                }
            }

            return cachePath;
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
    /// </summary>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <returns>Path to the extracted asset file, or null if failed</returns>
    private async Task<string?> EnsureAssetExtractedAsync(string assetName)
    {
        try
        {
            var assetPath = Path.Combine(_assetsPath, assetName);
            
            if (File.Exists(assetPath))
            {
                return assetPath;
            }
            
            // Extract embedded resource
            var resourceName = $"Jellyfin.Plugin.JellyBridge.Assets.{assetName}";
            _logger.LogTrace("Looking for embedded resource: {ResourceName}", resourceName);
            
            // List all available resources for debugging
            var allResources = typeof(PlaceholderVideoGenerator).Assembly.GetManifestResourceNames();
            _logger.LogTrace("Available resources: {Resources}", string.Join(", ", allResources));
            
            using var stream = typeof(PlaceholderVideoGenerator).Assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                _logger.LogError("Embedded asset not found: {ResourceName}", resourceName);
                return null;
            }
            
            using var fileStream = File.Create(assetPath);
            await stream.CopyToAsync(fileStream);
            
            _logger.LogTrace("Extracted embedded asset: {AssetPath}", assetPath);
            
            // Verify the extracted file exists and has content
            if (!File.Exists(assetPath))
            {
                _logger.LogError("Extracted asset file does not exist: {AssetPath}", assetPath);
                return null;
            }
            
            var fileInfo = new FileInfo(assetPath);
            if (fileInfo.Length == 0)
            {
                _logger.LogError("Extracted asset file is empty: {AssetPath}", assetPath);
                return null;
            }
            
            _logger.LogTrace("Asset file verified: {AssetPath} ({Size} bytes)", assetPath, fileInfo.Length);
            return assetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract asset: {AssetName}", assetName);
            return null;
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

    /// <summary>
    /// Check if FFmpeg is available and working, with retry logic.
    /// </summary>
    /// <returns>True if FFmpeg is available, false otherwise</returns>
    public async Task<bool> IsFFmpegAvailableAsync()
    {
        try
        {
            var requestTimeout = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.RequestTimeout));
            var retryAttempts = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.RetryAttempts));
            
            for (int attempt = 1; attempt <= retryAttempts; attempt++)
            {
                _logger.LogTrace("FFmpeg availability check attempt {Attempt}/{TotalAttempts}", attempt, retryAttempts);
                
                var attemptStartTime = DateTime.UtcNow;
                var maxWaitTime = TimeSpan.FromSeconds(requestTimeout);
                
                // Keep trying until the timeout period expires for this attempt
                while (DateTime.UtcNow - attemptStartTime < maxWaitTime)
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = _mediaEncoder.EncoderPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = processInfo };
                    process.Start();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation("FFmpeg is now available (attempt {Attempt}/{TotalAttempts})", attempt, retryAttempts);
                        return true;
                    }
                    
                    // Wait a bit before retrying within this attempt
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                
                _logger.LogDebug("Attempt {Attempt}/{TotalAttempts} timed out after {RequestTimeout} seconds", attempt, retryAttempts, requestTimeout);
            }
            
            _logger.LogWarning("FFmpeg availability check failed after {RetryAttempts} attempts", retryAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg availability check failed");
            return false;
        }
    }
}
