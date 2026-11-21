using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    /// <summary>
    /// Read-only view of what the libraries migration (task 4) would do: the new library list, every title
    /// folder move, every Jellyfin library repoint, the duplicate copies and the warnings. It runs the
    /// planner without taking the sync lock and changes nothing on disk or in the configuration — used by
    /// the container tests and to review the plan before the live deploy.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("JellyBridge")]
    public class LibrariesMigrationController : ControllerBase
    {
        private readonly DebugLogger<LibrariesMigrationController> _logger;
        private readonly LibraryMigrationService _migrationService;
        private readonly JellyfinILibraryManager _libraryManager;

        public LibrariesMigrationController(ILoggerFactory loggerFactory, LibraryMigrationService migrationService, JellyfinILibraryManager libraryManager)
        {
            _logger = new DebugLogger<LibrariesMigrationController>(loggerFactory.CreateLogger<LibrariesMigrationController>());
            _migrationService = migrationService;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Get the migration plan for the current configuration, without running anything.
        /// </summary>
        [HttpGet("Libraries/MigrationPlan")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<MigrationPlan> GetMigrationPlan()
        {
            _logger.LogInformation("Migration plan requested from plugin configuration page.");
            var config = Plugin.GetConfiguration();
            var plan = _migrationService.Plan(config, _libraryManager.Inner.GetVirtualFolders());
            _logger.LogInformation(
                "Migration plan: {Libraries} libraries, {Moves} moves, {Repoints} repoints, {Duplicates} duplicate copies, {Warnings} warnings{Empty}.",
                plan.Libraries.Count, plan.Moves.Count, plan.Repoints.Count, plan.Duplicates.Count, plan.Warnings.Count,
                plan.IsEmpty ? " (empty plan — nothing to migrate)" : string.Empty);
            return Ok(plan);
        }
    }
}
