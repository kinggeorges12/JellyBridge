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
    public List<JellyseerrMovie> IgnoredMovies { get; set; } = new();
    public List<JellyseerrShow> IgnoredShows { get; set; } = new();
    public List<JellyseerrMovie> DeletedMovies { get; set; } = new();
    public List<JellyseerrShow> DeletedShows { get; set; } = new();

    public override string ToString()
    {
        var parts = new List<string>();
        const string addedExplanation = "added items in the JellyBridge library from content in Jellyseerr discover pages";
        const string updatedExplanation = "updated items in the JellyBridge library from content in Jellyseerr discover pages";
        const string ignoredExplanation = "ignored JellyBridge folders in Jellyfin to avoid duplicate media";
        const string deletedExplanation = "deleted items in the JellyBridge library due to retention policy";
        
        var totalAdded = AddedMovies.Count + AddedShows.Count;
        var totalUpdated = UpdatedMovies.Count + UpdatedShows.Count;
        var totalIgnored = IgnoredMovies.Count + IgnoredShows.Count;
        var totalDeleted = DeletedMovies.Count + DeletedShows.Count;
        
        if (totalAdded > 0) parts.Add($"Added: {totalAdded} ({addedExplanation})");
        if (totalUpdated > 0) parts.Add($"Updated: {totalUpdated} ({updatedExplanation})");
        if (totalIgnored > 0) parts.Add($"Ignored: {totalIgnored} ({ignoredExplanation})");
        if (totalDeleted > 0) parts.Add($"Deleted: {totalDeleted} ({deletedExplanation})");
        
        return parts.Count > 0 ? string.Join("\n", parts) : "No items processed";
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
        const string processedExplanation = "number of favorites in Jellyfin";
        const string foundExplanation = "number of favorites in JellyBridge library";
        const string createdExplanation = "requests created in Jellyseerr";
        const string deletedExplanation = "items unfavorited after successful requests";
        const string blockedExplanation = "requests blocked by Jellyseerr due to quota limits or permission issues";
        
        var totalProcessed = MoviesResult.Processed + ShowsResult.Processed;
        var totalFound = MoviesResult.Found + ShowsResult.Found;
        var totalCreated = MoviesResult.Created + ShowsResult.Created;
        var totalDeleted = MoviesResult.Removed + ShowsResult.Removed;
        var totalBlocked = MoviesResult.Blocked + ShowsResult.Blocked;
        
        if (totalProcessed > 0) parts.Add($"Processed: {totalProcessed} ({processedExplanation})");
        if (totalFound > 0) parts.Add($"Found: {totalFound} ({foundExplanation})");
        if (totalCreated > 0) parts.Add($"Created: {totalCreated} ({createdExplanation})");
        if (totalDeleted > 0) parts.Add($"Deleted: {totalDeleted} ({deletedExplanation})");
        if (totalBlocked > 0) parts.Add($"Blocked: {totalBlocked} ({blockedExplanation})");
        
        return parts.Count > 0 ? string.Join("\n", parts) : "No items processed";
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
