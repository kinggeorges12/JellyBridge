using Jellyfin.Plugin.JellyBridge.BridgeModels;
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Result of processing Jellyseerr items with counts for created, updated, deleted items.
/// </summary>
public class ProcessJellyseerrResult
{
    public List<IJellyseerrItem> ItemsProcessed { get; set; } = new();
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();
    public List<IJellyseerrItem> ItemsDeleted { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Created => ItemsAdded.Count;
    public int Updated => ItemsUpdated.Count;
    public int Deleted => ItemsDeleted.Count;

    // Combine two ProcessJellyseerrResult instances into a new aggregated result
    public static ProcessJellyseerrResult operator +(ProcessJellyseerrResult left, ProcessJellyseerrResult right)
    {
        var combined = new ProcessJellyseerrResult();

        if (left != null)
        {
            AddUniqueRange(combined.ItemsProcessed, left.ItemsProcessed);
            AddUniqueRange(combined.ItemsAdded, left.ItemsAdded);
            AddUniqueRange(combined.ItemsUpdated, left.ItemsUpdated);
            AddUniqueRange(combined.ItemsDeleted, left.ItemsDeleted);
        }

        if (right != null)
        {
            AddUniqueRange(combined.ItemsProcessed, right.ItemsProcessed);
            AddUniqueRange(combined.ItemsAdded, right.ItemsAdded);
            AddUniqueRange(combined.ItemsUpdated, right.ItemsUpdated);
            AddUniqueRange(combined.ItemsDeleted, right.ItemsDeleted);
        }

        return combined;
    }

    // Helper to combine many results at once
    public static ProcessJellyseerrResult Combine(IEnumerable<ProcessJellyseerrResult> results)
    {
        var total = new ProcessJellyseerrResult();
        if (results == null)
        {
            return total;
        }
        foreach (var r in results)
        {
            total = total + r;
        }
        return total;
    }

    private static void AddUniqueRange(List<IJellyseerrItem> target, IEnumerable<IJellyseerrItem> source)
    {
        if (source == null)
        {
            return;
        }
        var existingHashes = new HashSet<int>(target.Select(item => item?.GetHashCode() ?? 0));
        foreach (var item in source)
        {
            if (item != null && existingHashes.Add(item.GetHashCode()))
            {
                target.Add(item);
            }
        }
    }

    public override string ToString()
    {
        var parts = new List<string>();
        
        if (Processed > 0) parts.Add($"Processed: {Processed}");
        if (Created > 0) parts.Add($"Created: {Created}");
        if (Updated > 0) parts.Add($"Updated: {Updated}");
        if (Deleted > 0) parts.Add($"Deleted: {Deleted}");
        
        return parts.Count > 0 ? string.Join(", ", parts) : "No items processed";
    }
}

/// <summary>
/// Result of a sync operation to Jellyseerr (creating requests).
/// </summary>
public class SyncJellyseerrResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public List<JellyseerrMovie> AddedMovies { get; set; } = new();
    public List<JellyseerrMovie> UpdatedMovies { get; set; } = new();
    public List<JellyseerrShow> AddedShows { get; set; } = new();
    public List<JellyseerrShow> UpdatedShows { get; set; } = new();
    public List<JellyseerrMovie> DeletedMovies { get; set; } = new();
    public List<JellyseerrShow> DeletedShows { get; set; } = new();

    public override string ToString()
    {
        return $"Movies: {AddedMovies.Count} added, {UpdatedMovies.Count} updated, {DeletedMovies.Count} deleted | Shows: {AddedShows.Count} added, {UpdatedShows.Count} updated, {DeletedShows.Count} deleted";
    }
}

/// <summary>
/// Result of a sync operation from Jellyfin (processing favorites).
/// </summary>
public class SyncJellyfinResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public ProcessJellyfinResult MoviesResult { get; set; } = new();
    public ProcessJellyfinResult ShowsResult { get; set; } = new();

    public override string ToString()
    {
        return $"Movies: {MoviesResult.Processed} processed, {MoviesResult.Found} found, {MoviesResult.Created} created | Shows: {ShowsResult.Processed} processed, {ShowsResult.Found} found, {ShowsResult.Created} created";
    }
}

/// <summary>
/// Result of processing Jellyfin items with lists of BaseItem for processed, updated items and JellyseerrMediaRequest for created items.
/// </summary>
public class ProcessJellyfinResult
{
    public List<BaseItem> ItemsProcessed { get; set; } = new();
    public List<BaseItem> ItemsFound { get; set; } = new();
    public List<JellyseerrMediaRequest> ItemsCreated { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Found => ItemsFound.Count;
    public int Created => ItemsCreated.Count;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Processed > 0) parts.Add($"Processed: {Processed}");
        if (Found > 0) parts.Add($"Found: {Found}");
        if (Created > 0) parts.Add($"Created: {Created}");
        
        return parts.Count > 0 ? string.Join(", ", parts) : "No items processed";
    }
}

/// <summary>
/// Result of a favorites scan test.
/// </summary>
public class TestFavoritesResult
{
    public int TotalUsers { get; set; }
    public int UsersWithFavorites { get; set; }
    public int TotalFavorites { get; set; }
    public int UsersWithRequests { get; set; }
    public int TotalRequests { get; set; }
    public List<JellyseerrMediaRequest> Requests { get; set; } = new();
}

/// <summary>
/// User favorites data.
/// </summary>
public class UserFavorites
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int FavoriteCount { get; set; }
    public List<FavoriteItem> Favorites { get; set; } = new();
    public int RequestCount { get; set; }
    public List<RequestItem> Requests { get; set; } = new();
}

/// <summary>
/// Individual favorite item.
/// </summary>
public class FavoriteItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Path { get; set; }
}

/// <summary>
/// Individual request item from Jellyseerr.
/// </summary>
public class RequestItem
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? MediaUrl { get; set; }
    public string? ServiceUrl { get; set; }
    public bool Is4k { get; set; }
    public int SeasonCount { get; set; }
}