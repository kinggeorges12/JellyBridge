using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

// ============================================================================
// SyncFromJellyseerr Operation Results
// ============================================================================

/// <summary>
/// Result of processing Jellyseerr items with counts for created, updated, deleted items.
/// </summary>
public class ProcessJellyseerrResult
{
    public List<IJellyseerrItem> ItemsProcessed { get; set; } = new();
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();
    public List<IJellyseerrItem> ItemsDeleted { get; set; } = new();
    public List<IJellyseerrItem> ItemsIgnored { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Created => ItemsAdded.Count;
    public int Updated => ItemsUpdated.Count;
    public int Deleted => ItemsDeleted.Count;
    public int Ignored => ItemsIgnored.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"  Processed: {Processed}");
        result.AppendLine($"  Added: {Created}");
        result.AppendLine($"  Updated: {Updated}");
        result.AppendLine($"  Ignored: {Ignored}");
        result.AppendLine($"  Deleted: {Deleted}");
        
        return result.ToString().TrimEnd();
    }
}

/// <summary>
/// Result of a sync operation to Jellyseerr (creating requests).
/// </summary>
public class SyncJellyseerrResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = "• 📦 Processed: Number of items processed from Jellyseerr\n• ➕ Added: Items added in the JellyBridge library from content in Jellyseerr discover pages\n• 🔄 Updated: Items updated in the JellyBridge library from content in Jellyseerr discover pages\n• ⏭️ Ignored: Items ignored - duplicates or already in Jellyfin library\n• 🗑️ Deleted: Items deleted in the JellyBridge library due to retention policy";
    public RefreshPlan? Refresh { get; set; }
    public ProcessJellyseerrResult MoviesResult { get; set; } = new();
    public ProcessJellyseerrResult ShowsResult { get; set; } = new();
    
    // Aliases for convenience - delegate to MoviesResult and ShowsResult
    // These support AddRange operations by forwarding to the underlying lists
    // Note: Assignments should use the underlying list directly (e.g., result.MoviesResult.ItemsAdded = list)
    // or use AddRange on the alias (e.g., result.AddedMovies.AddRange(list))
    public ListAlias<JellyseerrMovie, IJellyseerrItem> AddedMovies
    {
        get => new ListAlias<JellyseerrMovie, IJellyseerrItem>(MoviesResult.ItemsAdded);
        set => MoviesResult.ItemsAdded = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrMovie, IJellyseerrItem> UpdatedMovies
    {
        get => new ListAlias<JellyseerrMovie, IJellyseerrItem>(MoviesResult.ItemsUpdated);
        set => MoviesResult.ItemsUpdated = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrMovie, IJellyseerrItem> DeletedMovies
    {
        get => new ListAlias<JellyseerrMovie, IJellyseerrItem>(MoviesResult.ItemsDeleted);
        set => MoviesResult.ItemsDeleted = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrShow, IJellyseerrItem> AddedShows
    {
        get => new ListAlias<JellyseerrShow, IJellyseerrItem>(ShowsResult.ItemsAdded);
        set => ShowsResult.ItemsAdded = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrShow, IJellyseerrItem> UpdatedShows
    {
        get => new ListAlias<JellyseerrShow, IJellyseerrItem>(ShowsResult.ItemsUpdated);
        set => ShowsResult.ItemsUpdated = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrShow, IJellyseerrItem> DeletedShows
    {
        get => new ListAlias<JellyseerrShow, IJellyseerrItem>(ShowsResult.ItemsDeleted);
        set => ShowsResult.ItemsDeleted = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrMovie, IJellyseerrItem> IgnoredMovies
    {
        get => new ListAlias<JellyseerrMovie, IJellyseerrItem>(MoviesResult.ItemsIgnored);
        set => MoviesResult.ItemsIgnored = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }
    
    public ListAlias<JellyseerrShow, IJellyseerrItem> IgnoredShows
    {
        get => new ListAlias<JellyseerrShow, IJellyseerrItem>(ShowsResult.ItemsIgnored);
        set => ShowsResult.ItemsIgnored = value != null ? new List<IJellyseerrItem>(value.Cast<IJellyseerrItem>()) : new List<IJellyseerrItem>();
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:\n");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\nRefresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine("\nMovies Result:");
        result.AppendLine(MoviesResult.ToString());
        
        result.AppendLine("\nShows Result:");
        result.AppendLine(ShowsResult.ToString());
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// SyncToJellyseerr Operation Results
// ============================================================================

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
        var result = new System.Text.StringBuilder();
        result.AppendLine($"  Processed: {Processed}");
        result.AppendLine($"  Found: {Found}");
        result.AppendLine($"  Created: {Created}");
        result.AppendLine($"  Blocked: {Blocked}");
        result.AppendLine($"  Deleted: {Removed}");
        
        return result.ToString().TrimEnd();
    }
}

/// <summary>
/// Result of a sync operation from Jellyfin (processing favorites).
/// </summary>
public class SyncJellyfinResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = "• ❤️ Processed: Number of favorites in Jellyfin\n• 🔍 Found: Number of favorites in JellyBridge library\n• ➕ Created: Requests created in Jellyseerr\n• 🗑️ Deleted: Items unfavorited after successful requests\n• 🚫 Blocked: Requests blocked by Jellyseerr due to quota limits or permission issues";
    public RefreshPlan? Refresh { get; set; }
    public ProcessJellyfinResult MoviesResult { get; set; } = new();
    public ProcessJellyfinResult ShowsResult { get; set; } = new();
    
    // Aliases for convenience - delegate to MoviesResult and ShowsResult
    // These support AddRange operations by forwarding to the underlying lists
    public ListAlias<JellyfinMovie, IJellyfinItem> ProcessedMovies
    {
        get => new ListAlias<JellyfinMovie, IJellyfinItem>(MoviesResult.ItemsProcessed);
        set => MoviesResult.ItemsProcessed = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }
    
    public ListAlias<JellyfinSeries, IJellyfinItem> ProcessedShows
    {
        get => new ListAlias<JellyfinSeries, IJellyfinItem>(ShowsResult.ItemsProcessed);
        set => ShowsResult.ItemsProcessed = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }
    
    public List<JellyseerrMediaRequest> CreatedMovies
    {
        get => MoviesResult.ItemsCreated;
        set => MoviesResult.ItemsCreated = value;
    }
    
    public List<JellyseerrMediaRequest> CreatedShows
    {
        get => ShowsResult.ItemsCreated;
        set => ShowsResult.ItemsCreated = value;
    }
    
    public ListAlias<JellyfinMovie, IJellyfinItem> BlockedMovies
    {
        get => new ListAlias<JellyfinMovie, IJellyfinItem>(MoviesResult.ItemsBlocked);
        set => MoviesResult.ItemsBlocked = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }
    
    public ListAlias<JellyfinSeries, IJellyfinItem> BlockedShows
    {
        get => new ListAlias<JellyfinSeries, IJellyfinItem>(ShowsResult.ItemsBlocked);
        set => ShowsResult.ItemsBlocked = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }
    
    public ListAlias<JellyfinMovie, IJellyfinItem> RemovedMovies
    {
        get => new ListAlias<JellyfinMovie, IJellyfinItem>(MoviesResult.ItemsRemoved);
        set => MoviesResult.ItemsRemoved = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }
    
    public ListAlias<JellyfinSeries, IJellyfinItem> RemovedShows
    {
        get => new ListAlias<JellyfinSeries, IJellyfinItem>(ShowsResult.ItemsRemoved);
        set => ShowsResult.ItemsRemoved = value != null ? new List<IJellyfinItem>(value.Cast<IJellyfinItem>()) : new List<IJellyfinItem>();
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:\n");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\nRefresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine("\nMovies Result:");
        result.AppendLine(MoviesResult.ToString());
        
        result.AppendLine("\nShows Result:");
        result.AppendLine(ShowsResult.ToString());
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// SortJellyBridge Operation Results
// ============================================================================

/// <summary>
/// Result of processing sort operations with counts for sorted, failed, and skipped items.
/// </summary>
public class ProcessSortResult
{
    public List<(IJellyfinItem item, int playCount)> ItemsSorted { get; set; } = new();
    public List<string> ItemsFailed { get; set; } = new();
    public List<(IJellyfinItem? item, string path)> ItemsSkipped { get; set; } = new();

    public int Sorted => ItemsSorted.Count;
    public int Failed => ItemsFailed.Count;
    public int Skipped => ItemsSkipped.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"  Sorted: {Sorted} (items sorted by updating play counts)");
        result.AppendLine($"  Skipped: {Skipped} (items skipped - ignored files)");
        result.AppendLine($"  Failed: {Failed} (items failed to update, possibly not processed yet by Jellyfin)");
        
        return result.ToString().TrimEnd();
    }
}

/// <summary>
/// Result of sorting the discover library by updating play counts.
/// </summary>
public class SortLibraryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = "Algorithm is the sort order, Users have play counts individually updated in the JellyBridge library, Refresh type is true for Replace all metadata or false for Search for missing metadata, Sort results include movies and shows that had play counts changed.";
    public BridgeConfiguration.SortOrderOptions SortAlgorithm { get; set; }
    public List<JellyfinUser> Users { get; set; } = new();
    public ProcessSortResult ProcessResult { get; set; } = new();
    public RefreshPlan? Refresh { get; set; }
    
    // Aliases for convenience - delegate to ProcessResult
    public List<(IJellyfinItem item, int playCount)> ItemsSorted
    {
        get => ProcessResult.ItemsSorted;
        set => ProcessResult.ItemsSorted = value;
    }
    
    public List<string> ItemsFailed
    {
        get => ProcessResult.ItemsFailed;
        set => ProcessResult.ItemsFailed = value;
    }
    
    public List<(IJellyfinItem? item, string path)> ItemsSkipped
    {
        get => ProcessResult.ItemsSkipped;
        set => ProcessResult.ItemsSkipped = value;
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:\n");
        result.AppendLine(Details);
        result.AppendLine();
        
        // Add individual items with 2-space indentation
        result.AppendLine($"  Algorithm: {SortAlgorithm}");
        result.AppendLine($"  Users: {Users.Count}");
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\n  Refresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine("\n  Sort Results:");
        var processResult = ProcessResult.ToString();
        // Indent each line of ProcessResult by 2 more spaces (4 total)
        var lines = processResult.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                result.AppendLine($"    {line.TrimStart()}");
            }
        }
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// Helper Classes
// ============================================================================

/// <summary>
/// Wrapper for List that forwards AddRange operations to an underlying list with a different type.
/// </summary>
public class ListAlias<T, TBase> : ICollection<T> where T : TBase
{
    private readonly List<TBase> _underlying;
    
    public ListAlias(List<TBase> underlying)
    {
        _underlying = underlying;
    }
    
    public void AddRange(IEnumerable<T> collection)
    {
        _underlying.AddRange(collection.Cast<TBase>());
    }
    
    public void Add(T item)
    {
        _underlying.Add(item);
    }
    
    public void Clear() => _underlying.Clear();
    public bool Contains(T item) => _underlying.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _underlying.Cast<T>().ToList().CopyTo(array, arrayIndex);
    public bool Remove(T item) => _underlying.Remove(item);
    public int Count => _underlying.Count;
    public bool IsReadOnly => false;
    public IEnumerator<T> GetEnumerator() => _underlying.OfType<T>().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Plan describing how to refresh Jellyfin libraries after a sync.
/// </summary>
public class RefreshPlan
{
    public bool FullRefresh { get; set; }
    public bool RefreshImages { get; set; }
}