using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Result of a processing operation.
/// </summary>
public class ProcessResult
{
    public List<IJellyseerrItem> ItemsProcessed { get; set; } = new();
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();
    public List<IJellyseerrItem> ItemsDeleted { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Created => ItemsAdded.Count;
    public int Updated => ItemsUpdated.Count;
    public int Deleted => ItemsDeleted.Count;

    // Combine two ProcessResult instances into a new aggregated result
    public static ProcessResult operator +(ProcessResult left, ProcessResult right)
    {
        var combined = new ProcessResult();

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
    public static ProcessResult Combine(IEnumerable<ProcessResult> results)
    {
        var total = new ProcessResult();
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
/// Result of a sync operation.
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public ProcessResult MoviesResult { get; set; } = new();
    public ProcessResult ShowsResult { get; set; } = new();

    public override string ToString()
    {
        return $"Movies: {MoviesResult.Created} created, {MoviesResult.Updated} updated | Shows: {ShowsResult.Created} created, {ShowsResult.Updated} updated";
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
    public List<UserFavorites> UserFavorites { get; set; } = new();
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