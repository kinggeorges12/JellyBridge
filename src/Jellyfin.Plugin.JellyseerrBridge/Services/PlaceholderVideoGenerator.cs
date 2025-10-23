using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.MediaEncoding;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for generating placeholder videos from asset images using FFmpeg.
/// </summary>
public class PlaceholderVideoGenerator
{
    private readonly ILogger<PlaceholderVideoGenerator> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly string _assetsPath;
    
    // Asset file names for different media types
    private const string MovieAsset = "movie.png";
    private const string ShowAsset = "show.png";
    private const string SeasonAsset = "season.png";

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        
        // Assets are embedded in the plugin assembly
        _assetsPath = Path.Combine(Path.GetTempPath(), "JellyseerrBridge", "assets");
        Directory.CreateDirectory(_assetsPath);
        
        _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}", 
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
    public async Task<bool> GeneratePlaceholderShowAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, ShowAsset);
    }

    /// <summary>
    /// Generate a placeholder video for a season (asset: season.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderSeasonAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, SeasonAsset);
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
            var cacheDir = Path.Combine(Path.GetTempPath(), "JellyseerrBridge", "placeholders");
            Directory.CreateDirectory(cacheDir);
            var videoDuration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));
            var assetStem = Path.GetFileNameWithoutExtension(assetName);
            var cachePath = Path.Combine(cacheDir, $"{assetStem}_{videoDuration}.mp4");

            if (!System.IO.File.Exists(cachePath))
            {
                _logger.LogInformation("[PlaceholderVideoGenerator] Cached placeholder not found for {Asset}, generating at {CachePath}", assetName, cachePath);
                
                // Check if FFmpeg is available before attempting to generate
                if (!await IsFFmpegAvailableAsync())
                {
                    _logger.LogError("[PlaceholderVideoGenerator] FFmpeg is not available, cannot generate placeholder for {Asset}", assetName);
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
            _logger.LogError(ex, "[PlaceholderVideoGenerator] Failed ensuring cached placeholder for {Asset}", assetName);
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
            var targetFile = assetStem + ".mp4";
            var targetPath = Path.Combine(targetDirectory, targetFile);

            // Copy cached file to target directory
            File.Copy(cachedPath, targetPath, overwrite: true);
            _logger.LogInformation("[PlaceholderVideoGenerator] Copied placeholder to {TargetPath} (overwrite enabled)", targetPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlaceholderVideoGenerator] Error generating placeholder video: {AssetName} -> {TargetDirectory}", 
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
            
            if (System.IO.File.Exists(assetPath))
            {
                return assetPath;
            }
            
            // Extract embedded resource
            var resourceName = $"Jellyfin.Plugin.JellyseerrBridge.Assets.{assetName}";
            _logger.LogDebug("[PlaceholderVideoGenerator] Looking for embedded resource: {ResourceName}", resourceName);
            
            // List all available resources for debugging
            var allResources = typeof(PlaceholderVideoGenerator).Assembly.GetManifestResourceNames();
            _logger.LogDebug("[PlaceholderVideoGenerator] Available resources: {Resources}", string.Join(", ", allResources));
            
            using var stream = typeof(PlaceholderVideoGenerator).Assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                _logger.LogError("[PlaceholderVideoGenerator] Embedded asset not found: {ResourceName}", resourceName);
                return null;
            }
            
            using var fileStream = File.Create(assetPath);
            await stream.CopyToAsync(fileStream);
            
            _logger.LogDebug("[PlaceholderVideoGenerator] Extracted embedded asset: {AssetPath}", assetPath);
            
            // Verify the extracted file exists and has content
            if (!System.IO.File.Exists(assetPath))
            {
                _logger.LogError("[PlaceholderVideoGenerator] Extracted asset file does not exist: {AssetPath}", assetPath);
                return null;
            }
            
            var fileInfo = new FileInfo(assetPath);
            if (fileInfo.Length == 0)
            {
                _logger.LogError("[PlaceholderVideoGenerator] Extracted asset file is empty: {AssetPath}", assetPath);
                return null;
            }
            
            _logger.LogDebug("[PlaceholderVideoGenerator] Asset file verified: {AssetPath} ({Size} bytes)", assetPath, fileInfo.Length);
            return assetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlaceholderVideoGenerator] Failed to extract asset: {AssetName}", assetName);
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
                _logger.LogError("[PlaceholderVideoGenerator] Asset file not found: {AssetName}", assetName);
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
                _logger.LogError("[PlaceholderVideoGenerator] Invalid duration: {Duration}. Must be greater than 0.", videoDuration);
                return false;
            }

            // Build FFmpeg command
            var arguments = $"-loop 1 -i \"{assetPath}\" -t {videoDuration} -vf \"format=yuv420p\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";

            _logger.LogInformation("[PlaceholderVideoGenerator] Generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputPath);
            _logger.LogInformation("[PlaceholderVideoGenerator] Asset path: {AssetPath}, Duration: {Duration}", 
                assetPath, videoDuration);
            _logger.LogInformation("[PlaceholderVideoGenerator] FFmpeg command: {FFmpegPath} {Arguments}", 
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
                if (!System.IO.File.Exists(outputPath))
                {
                    _logger.LogError("[PlaceholderVideoGenerator] FFmpeg succeeded but output file does not exist: {OutputPath}", outputPath);
                    return false;
                }
                
                var outputFileInfo = new FileInfo(outputPath);
                if (outputFileInfo.Length == 0)
                {
                    _logger.LogError("[PlaceholderVideoGenerator] FFmpeg succeeded but output file is empty: {OutputPath}", outputPath);
                    return false;
                }
                
                _logger.LogInformation("[PlaceholderVideoGenerator] Successfully generated placeholder video: {OutputPath} ({Size} bytes)", 
                    outputPath, outputFileInfo.Length);
                _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg output: {Output}", outputBuilder.ToString());
                return true;
            }
            else
            {
                _logger.LogError("[PlaceholderVideoGenerator] FFmpeg failed with exit code {ExitCode}. Error output: {ErrorOutput}", 
                    process.ExitCode, errorBuilder.ToString());
                _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg output: {Output}", outputBuilder.ToString());
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlaceholderVideoGenerator] Error generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputPath);
            return false;
        }
    }

    /// <summary>
    /// Check if FFmpeg is available and working.
    /// </summary>
    /// <returns>True if FFmpeg is available, false otherwise</returns>
    public async Task<bool> IsFFmpegAvailableAsync()
    {
        try
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

            var isAvailable = process.ExitCode == 0;
            _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg availability check: {IsAvailable}", isAvailable);
            return isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PlaceholderVideoGenerator] FFmpeg availability check failed");
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
            Path.GetFileNameWithoutExtension(MovieAsset) + ".mp4",
            Path.GetFileNameWithoutExtension(ShowAsset) + ".mp4", 
            Path.GetFileNameWithoutExtension(SeasonAsset) + ".mp4"
        };

        try
        {
            if (string.IsNullOrEmpty(bridgeFolderPath) || !Directory.Exists(bridgeFolderPath))
            {
                throw new InvalidOperationException($"Bridge folder does not exist: {bridgeFolderPath}");
            }

            _logger.LogDebug("[PlaceholderVideoGenerator] Cleaning up placeholder videos in: {BridgeFolderPath}", bridgeFolderPath);

            // Delete placeholder files in current directory and all subdirectories
            foreach (var placeholderFile in placeholderFiles)
            {
                var searchPattern = $"*{placeholderFile}";
                var files = Directory.GetFiles(bridgeFolderPath, searchPattern, SearchOption.AllDirectories);
                
                foreach (var filePath in files)
                {
                    try
                    {
                        await Task.Run(() => System.IO.File.Delete(filePath));
                        _logger.LogDebug("[PlaceholderVideoGenerator] Deleted placeholder file: {FilePath}", filePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[PlaceholderVideoGenerator] Failed to delete placeholder file: {FilePath}", filePath);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("[PlaceholderVideoGenerator] Deleted {DeletedCount} placeholder video files from {BridgeFolderPath}", 
                    deletedCount, bridgeFolderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PlaceholderVideoGenerator] Error cleaning up placeholder videos in {BridgeFolderPath}", bridgeFolderPath);
        }

        return deletedCount;
    }
}
