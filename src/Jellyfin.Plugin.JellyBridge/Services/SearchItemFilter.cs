using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Opens Seerr search result cards: image requests get the poster, any other request adds the title to the Discover library
/// first and then uses the real item id, whatever the argument is called (itemId, seriesId, parentId...).
/// </summary>
public sealed class SearchItemFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> ImageActions = new(StringComparer.Ordinal) { "GetItemImage", "GetItemImageByIndex", "GetItemImage2" };

    // Requests that change something but are fine on a newly added title
    private static readonly HashSet<string> SafeWrites = new(StringComparer.Ordinal)
    {
        "GetPostedPlaybackInfo", "MarkFavoriteItem", "MarkFavoriteItemLegacy", "UnmarkFavoriteItem", "UnmarkFavoriteItemLegacy",
    };

    private readonly SearchService _searchService;

    public SearchItemFilter(SearchService searchService)
    {
        _searchService = searchService;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cards = context.ActionArguments
            .Where(a => a.Value is Guid id && _searchService.IsSearchResult(id))
            .Select(a => (a.Key, Id: (Guid)a.Value!))
            .ToList();
        var cardLists = context.ActionArguments
            .Where(a => a.Value is Guid[] ids && ids.Any(_searchService.IsSearchResult))
            .Select(a => (a.Key, Ids: (Guid[])a.Value!))
            .ToList();
        if (cards.Count == 0 && cardLists.Count == 0)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (action is { ControllerName: "Image" } && ImageActions.Contains(action.ActionName) && cards.Count == 1)
        {
            var poster = await _searchService.GetPosterAsync(cards[0].Id, context.HttpContext.RequestAborted).ConfigureAwait(false);
            if (poster == null)
            {
                context.Result = new NotFoundResult();
                return;
            }

            context.HttpContext.Response.Headers.CacheControl = "public, max-age=86400";
            context.Result = new FileContentResult(poster.Value.Bytes, poster.Value.ContentType);
            return;
        }

        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && (action == null || !SafeWrites.Contains(action.ActionName)))
        {
            await next().ConfigureAwait(false);
            return;
        }

        foreach (var (key, id) in cards)
        {
            var itemId = await _searchService.OpenAsync(id, context.HttpContext.RequestAborted).ConfigureAwait(false);
            if (itemId == null)
            {
                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                return;
            }

            context.ActionArguments[key] = itemId.Value;
        }

        foreach (var (key, ids) in cardLists)
        {
            var mapped = new Guid[ids.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                mapped[i] = _searchService.IsSearchResult(ids[i])
                    ? await _searchService.OpenAsync(ids[i], context.HttpContext.RequestAborted).ConfigureAwait(false) ?? ids[i]
                    : ids[i];
            }

            context.ActionArguments[key] = mapped;
        }

        await next().ConfigureAwait(false);
    }
}
