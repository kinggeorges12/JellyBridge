using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Adds Seerr titles that are in no library to Jellyfin's item search (GET /Items?searchTerm=).
/// </summary>
public sealed class SearchResultsFilter : IAsyncActionFilter
{
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(3);
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "GetItems", "GetItemsByUserIdLegacy" };

    // Arguments a Seerr title can satisfy; any other argument with a value (genres, favorites, years...) narrows the search
    private static readonly HashSet<string> Neutral = new(StringComparer.Ordinal)
    {
        "userId", "searchTerm", "includeItemTypes", "excludeItemTypes", "recursive", "limit", "startIndex", "fields",
        "enableTotalRecordCount", "imageTypeLimit", "enableImageTypes", "enableImages", "enableUserData",
        "mediaTypes", "isMissing", "sortBy", "sortOrder", "collapseBoxSetItems",
    };

    private readonly SearchService _searchService;
    private readonly IServerApplicationHost _host;
    private readonly DebugLogger<SearchResultsFilter> _logger;

    public SearchResultsFilter(SearchService searchService, IServerApplicationHost host, ILogger<SearchResultsFilter> logger)
    {
        _searchService = searchService;
        _host = host;
        _logger = new DebugLogger<SearchResultsFilter>(logger);
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var plan = Plan(context);
        if (plan == null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var search = _searchService.StartSearch(plan.Value.Term);
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception != null || executed.Result is not ObjectResult { StatusCode: null or 200, Value: QueryResult<BaseItemDto> page })
        {
            return;
        }

        if (plan.Value.Limit is int limit && page.Items.Count >= limit)
        {
            return;
        }

        if (await Task.WhenAny(search, Task.Delay(SearchWait)).ConfigureAwait(false) != search)
        {
            _logger.LogDebug("Seerr search for {Term} took too long, showing library results only", plan.Value.Term);
            return;
        }

        var cards = _searchService.CreateCards(await search.ConfigureAwait(false), page.Items, plan.Value.Movies, plan.Value.Shows, _host.SystemId);
        if (cards.Count > 0)
        {
            page.Items = page.Items.Concat(cards).ToList();
            page.TotalRecordCount += cards.Count;
        }
    }

    private (string Term, bool Movies, bool Shows, int? Limit)? Plan(ActionExecutingContext context)
    {
        if (!SearchService.IsEnabled
            || context.ActionDescriptor is not ControllerActionDescriptor { ControllerName: "Items" } action
            || !Actions.Contains(action.ActionName))
        {
            return null;
        }

        var args = context.ActionArguments;
        if (!args.TryGetValue("searchTerm", out var value) || value is not string term || term.Trim().Length < 2
            || (args.TryGetValue("startIndex", out var start) && start is int s && s > 0)
            || (args.TryGetValue("isMissing", out var missing) && missing is true)
            || args.Any(a => !Neutral.Contains(a.Key) && HasValue(a.Value)))
        {
            return null;
        }

        var include = args.TryGetValue("includeItemTypes", out var inc) ? inc as BaseItemKind[] : null;
        var exclude = args.TryGetValue("excludeItemTypes", out var exc) ? exc as BaseItemKind[] : null;
        bool Allowed(BaseItemKind kind) => include is { Length: > 0 } ? include.Contains(kind) : exclude?.Contains(kind) != true;
        var movies = Allowed(BaseItemKind.Movie);
        var shows = Allowed(BaseItemKind.Series);

        // A media type filter without Video means no videos; with Video, series (folders) don't belong in the list
        if (args.TryGetValue("mediaTypes", out var media) && media is MediaType[] { Length: > 0 } types)
        {
            movies &= types.Contains(MediaType.Video);
            shows = false;
        }

        var userId = args.TryGetValue("userId", out var uid) && uid is Guid id && id != Guid.Empty ? id : UserIdFromClaims(context);
        if ((!movies && !shows) || userId == Guid.Empty || !_searchService.UserCanSeeDiscover(userId))
        {
            return null;
        }

        int? limit = args.TryGetValue("limit", out var l) && l is int max ? max : null;
        return (term.Trim(), movies, shows, limit);
    }

    private static Guid UserIdFromClaims(ActionExecutingContext context) =>
        Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var id) ? id : Guid.Empty;

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        Array array => array.Length > 0,
        string text => text.Length > 0,
        _ => true,
    };
}
