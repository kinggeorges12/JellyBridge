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

    public PlaceholderVideoGenerator(ILogger<PlaceholderVideoGenerator> logger, IMediaEncoder mediaEncoder)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        
        // Assets are embedded in the plugin assembly
        _assetsPath = Path.Combine(Path.GetDirectoryName(typeof(PlaceholderVideoGenerator).Assembly.Location)!, "Assets");
        
        _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg path: {FFmpegPath}, Assets path: {AssetsPath}", 
            _mediaEncoder.EncoderPath, _assetsPath);
    }

    /// <summary>
    /// Generate a placeholder video for a movie (asset: movie.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderMovieAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, "movie.png");
    }

    /// <summary>
    /// Generate a placeholder video for a TV show (asset: show.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderShowAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, "show.png");
    }

    /// <summary>
    /// Generate a placeholder video for a season (asset: season.png).
    /// </summary>
    public async Task<bool> GeneratePlaceholderSeasonAsync(string targetDirectory)
    {
        return await GeneratePlaceholderAsync(targetDirectory, "season.png");
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
            var dur = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));
            var assetStem = Path.GetFileNameWithoutExtension(assetName);
            var cachePath = Path.Combine(cacheDir, $"{assetStem}_{dur}.mp4");

            if (!File.Exists(cachePath))
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

            if (File.Exists(targetPath))
            {
                _logger.LogDebug("[PlaceholderVideoGenerator] Placeholder already exists at {TargetPath}", targetPath);
                return true;
            }

            // Copy cached file to target directory
            File.Copy(cachedPath, targetPath, overwrite: false);
            _logger.LogInformation("[PlaceholderVideoGenerator] Copied placeholder to {TargetPath}", targetPath);
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
    /// Generate a placeholder video from an asset image using FFmpeg.
    /// </summary>
    /// <param name="assetName">The asset filename (e.g., "movie.png")</param>
    /// <param name="outputPath">The output video file path</param>
    /// <returns>True if successful, false otherwise</returns>
    private async Task<bool> GeneratePlaceholderVideoAsync(string assetName, string outputPath)
    {
        try
        {
            var assetPath = Path.Combine(_assetsPath, assetName);
            
            if (!File.Exists(assetPath))
            {
                _logger.LogError("[PlaceholderVideoGenerator] Asset file not found: {AssetPath}", assetPath);
                return false;
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Resolve duration from configuration
            var duration = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.PlaceholderDurationSeconds));

            // Build FFmpeg command
            var arguments = $"-loop 1 -i \"{assetPath}\" -t {duration} -vf \"format=yuv420p\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";

            _logger.LogInformation("[PlaceholderVideoGenerator] Generating placeholder video: {AssetName} -> {OutputPath}", 
                assetName, outputPath);
            _logger.LogDebug("[PlaceholderVideoGenerator] FFmpeg command: {FFmpegPath} {Arguments}", 
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
                _logger.LogInformation("[PlaceholderVideoGenerator] Successfully generated placeholder video: {OutputPath}", outputPath);
                return true;
            }
            else
            {
                _logger.LogError("[PlaceholderVideoGenerator] FFmpeg failed with exit code {ExitCode}. Error output: {ErrorOutput}", 
                    process.ExitCode, errorBuilder.ToString());
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
}
