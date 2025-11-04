using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Plan describing how to refresh Jellyfin libraries after a sync.
/// </summary>
public class RefreshPlan
{
    public bool FullRefresh { get; set; }
    public bool RefreshImages { get; set; }
}

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
    public RefreshPlan? Refresh { get; set; }
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
    public RefreshPlan? Refresh { get; set; }
    public ProcessJellyfinResult MoviesResult { get; set; } = new();
    public ProcessJellyfinResult ShowsResult { get; set; } = new();

    public override string ToString()
    {
        var parts = new List<string>();
        parts.Add($"Movies: {MoviesResult.Processed} processed, {MoviesResult.Created} created");
        if (MoviesResult.Blocked > 0) parts.Add($"{MoviesResult.Blocked} blocked");
        parts.Add($"Shows: {ShowsResult.Processed} processed, {ShowsResult.Created} created");
        if (ShowsResult.Blocked > 0) parts.Add($"{ShowsResult.Blocked} blocked");
        return string.Join(" | ", parts);
    }
}

/// <summary>
/// Result of processing Jellyfin items with lists of IJellyfinItem for processed items and JellyseerrMediaRequest for created items.
/// </summary>
public class ProcessJellyfinResult
{
    public List<IJellyfinItem> ItemsProcessed { get; set; } = new();
    public List<IJellyfinItem> ItemsFound { get; set; } = new();
    public List<JellyseerrMediaRequest> ItemsCreated { get; set; } = new();
    public List<IJellyfinItem> ItemsRemoved { get; set; } = new();
    public List<IJellyfinItem> ItemsBlocked { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Found => ItemsFound.Count;
    public int Created => ItemsCreated.Count;
    public int Removed => ItemsRemoved.Count;
    public int Blocked => ItemsBlocked.Count;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Processed > 0) parts.Add($"Processed: {Processed}");
        if (Found > 0) parts.Add($"Found: {Found}");
        if (Created > 0) parts.Add($"Created: {Created}");
        if (Removed > 0) parts.Add($"Removed: {Removed}");
        if (Blocked > 0) parts.Add($"Blocked: {Blocked}");
        
        return parts.Count > 0 ? string.Join(", ", parts) : "No items processed";
    }
}
