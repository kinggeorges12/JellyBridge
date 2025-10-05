using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Library management API controller.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge/Library")]
public class LibraryController : ControllerBase
{
    private readonly LibraryManagementService _libraryManagementService;
    private readonly LibraryFilterService _libraryFilterService;
    private readonly ILogger<LibraryController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryController"/> class.
    /// </summary>
    /// <param name="libraryManagementService">The library management service.</param>
    /// <param name="libraryFilterService">The library filter service.</param>
    /// <param name="logger">The logger.</param>
    public LibraryController(LibraryManagementService libraryManagementService, LibraryFilterService libraryFilterService, ILogger<LibraryController> logger)
    {
        _libraryManagementService = libraryManagementService;
        _libraryFilterService = libraryFilterService;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a show should be excluded from main libraries.
    /// </summary>
    /// <param name="showPath">The path to the show.</param>
    /// <returns>Exclusion status.</returns>
    [HttpPost("CheckExclusion")]
    public ActionResult<object> CheckExclusion([FromBody] CheckExclusionRequest request)
    {
        try
        {
            _logger.LogInformation("Checking exclusion for show: {Path}", request.ShowPath);
            
            var isPlaceholder = _libraryFilterService.IsPlaceholderShow(request.ShowPath);
            var shouldExclude = _libraryFilterService.ShouldExcludeFromMainLibraries(request.ShowPath);
            var libraryType = _libraryFilterService.GetLibraryType(request.ShowPath);
            
            var result = new
            {
                ShowPath = request.ShowPath,
                IsPlaceholder = isPlaceholder,
                ShouldExcludeFromMainLibraries = shouldExclude,
                LibraryType = libraryType.ToString(),
                Message = shouldExclude ? "Show will be excluded from main libraries" : "Show will be included in main libraries"
            };
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking exclusion for show: {Path}", request.ShowPath);
            return StatusCode(500, new { Error = "Error checking exclusion", Message = ex.Message });
        }
    }

    /// <summary>
    /// Filters a list of shows based on library type.
    /// </summary>
    /// <param name="request">The filter request.</param>
    /// <returns>Filtered show list.</returns>
    [HttpPost("FilterShows")]
    public ActionResult<object> FilterShows([FromBody] FilterShowsRequest request)
    {
        try
        {
            _logger.LogInformation("Filtering {Count} shows for {LibraryType} library", request.ShowPaths.Count, request.TargetLibraryType);
            
            var filteredShows = _libraryManagementService.FilterShows(request.ShowPaths, request.TargetLibraryType);
            
            var result = new
            {
                OriginalCount = request.ShowPaths.Count,
                FilteredCount = filteredShows.Count,
                FilteredShows = filteredShows,
                TargetLibraryType = request.TargetLibraryType.ToString(),
                Message = $"Filtered {request.ShowPaths.Count} shows to {filteredShows.Count} shows"
            };
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering shows");
            return StatusCode(500, new { Error = "Error filtering shows", Message = ex.Message });
        }
    }

    /// <summary>
    /// Gets library statistics.
    /// </summary>
    /// <returns>Library statistics.</returns>
    [HttpGet("Statistics")]
    public ActionResult<object> GetStatistics()
    {
        try
        {
            _logger.LogInformation("Getting library statistics");
            
            var libraryDirectory = _libraryManagementService.GetLibraryDirectory();
            var directoryExists = Directory.Exists(libraryDirectory);
            
            var result = new
            {
                LibraryDirectory = libraryDirectory,
                DirectoryExists = directoryExists,
                ExcludeFromMainLibraries = _libraryFilterService.ShouldExcludeFromMainLibraries(""),
                CreateSeparateLibraries = true, // This would come from configuration
                Message = "Library statistics retrieved successfully"
            };
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting library statistics");
            return StatusCode(500, new { Error = "Error getting statistics", Message = ex.Message });
        }
    }
}

/// <summary>
/// Request model for checking exclusion.
/// </summary>
public class CheckExclusionRequest
{
    /// <summary>
    /// Gets or sets the show path.
    /// </summary>
    public string ShowPath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for filtering shows.
/// </summary>
public class FilterShowsRequest
{
    /// <summary>
    /// Gets or sets the list of show paths.
    /// </summary>
    public List<string> ShowPaths { get; set; } = new();

    /// <summary>
    /// Gets or sets the target library type.
    /// </summary>
    public LibraryType TargetLibraryType { get; set; } = LibraryType.Main;
}
