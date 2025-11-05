using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
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
    public List<IJellyseerrItem> ItemsIgnored { get; set; } = new();

    public int Processed => ItemsProcessed.Count;
    public int Created => ItemsAdded.Count;
    public int Updated => ItemsUpdated.Count;
    public int Ignored => ItemsIgnored.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"{"📦 Processed:",-15}{Processed,15}");
        result.AppendLine($"{"➕ Added:",-15}{Created,15}");
        result.AppendLine($"{"🛠️ Updated:",-15}{Updated,15}");
        result.AppendLine($"{"⏭️ Ignored:",-15}{Ignored,15}");
        
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
    public string Details { get; set; } = "🔄 Refresh: Refreshes all Jellyfin libraries containing the JellyBridge folder using the metadata options\n📦 Processed: Number of items processed from Jellyseerr\n➕ Added: Items added in the JellyBridge library from content in Jellyseerr discover pages\n🛠️ Updated: Items updated in the JellyBridge library from content in Jellyseerr discover pages\n⏭️ Ignored: duplicate discover content or the content already exists in another Jellyfin library\n🙈 Hidden: Items newly hidden from Jellyfin using .ignore files (will be ignored on subsequent runs)";
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();
    public List<IJellyseerrItem> ItemsIgnored { get; set; } = new();
    public List<IJellyseerrItem> ItemsHidden { get; set; } = new();
    
    // Computed properties - filter by type
    public List<JellyseerrMovie> AddedMovies => ItemsAdded.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> AddedShows => ItemsAdded.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> UpdatedMovies => ItemsUpdated.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> UpdatedShows => ItemsUpdated.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> IgnoredMovies => ItemsIgnored.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> IgnoredShows => ItemsIgnored.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> HiddenMovies => ItemsHidden.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> HiddenShows => ItemsHidden.OfType<JellyseerrShow>().ToList();
    
    // Count properties for table display
    public int MoviesProcessed => AddedMovies.Count + UpdatedMovies.Count + IgnoredMovies.Count;
    public int ShowsProcessed => AddedShows.Count + UpdatedShows.Count + IgnoredShows.Count;
    public int MoviesAdded => AddedMovies.Count;
    public int ShowsAdded => AddedShows.Count;
    public int MoviesUpdated => UpdatedMovies.Count;
    public int ShowsUpdated => UpdatedShows.Count;
    public int MoviesIgnored => IgnoredMovies.Count;
    public int ShowsIgnored => IgnoredShows.Count;
    public int MoviesHidden => HiddenMovies.Count;
    public int ShowsHidden => HiddenShows.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\nRefresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|--------------|--------------|--------------|--------------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-14}{separator}{"Movies",-14}{separator}{"Shows",-14}{separator}{"Total",-14}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦 Processed",-14}{separator}{MoviesProcessed,14}{separator}{ShowsProcessed,14}{separator}{MoviesProcessed + ShowsProcessed,14}{separator}");
        result.AppendLine($"{separator}{"➕ Added",-14}{separator}{MoviesAdded,14}{separator}{ShowsAdded,14}{separator}{MoviesAdded + ShowsAdded,14}{separator}");
        result.AppendLine($"{separator}{"🛠️ Updated",-14}{separator}{MoviesUpdated,14}{separator}{ShowsUpdated,14}{separator}{MoviesUpdated + ShowsUpdated,14}{separator}");
        result.AppendLine($"{separator}{"⏭️ Ignored",-14}{separator}{MoviesIgnored,14}{separator}{ShowsIgnored,14}{separator}{MoviesIgnored + ShowsIgnored,14}{separator}");
        
        // Hidden row (only show if there are hidden items)
        if (MoviesHidden > 0 || ShowsHidden > 0)
        {
            result.AppendLine($"{separator}{"🙈 Hidden",-14}{separator}{MoviesHidden,14}{separator}{ShowsHidden,14}{separator}{MoviesHidden + ShowsHidden,14}{separator}");
        }
        
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
        result.AppendLine($"{"❤️ Processed:",-15}{Processed,15}");
        result.AppendLine($"{"🔍 Found:",-15}{Found,15}");
        result.AppendLine($"{"➕ Created:",-15}{Created,15}");
        result.AppendLine($"{"🚫 Blocked:",-15}{Blocked,15}");
        result.AppendLine($"{"• Removed:",-15}{Removed,15}");
        
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
    public string Details { get; set; } = "🔄 Refresh: Refreshes all Jellyfin libraries containing the JellyBridge folder using the metadata options\n❤️ Processed: Number of favorites in Jellyfin\n🔍 Found: Number of favorites in JellyBridge library\n➕ Created: Requests created in Jellyseerr\n🚫 Blocked: Requests blocked by Jellyseerr due to quota limits or permission issues\n🙈 Hidden: Jellyfin items marked with an .ignore file after requesting them from Jellyseerr\n👁️ Unhidden: Requests in Jellyseerr that are declined are shown in Jellyfin";
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyfinItem> ItemsProcessed { get; set; } = new();
    public List<IJellyfinItem> ItemsFound { get; set; } = new();
    public List<JellyseerrMediaRequest> ItemsCreated { get; set; } = new();
    public List<IJellyfinItem> ItemsBlocked { get; set; } = new();
    public List<IJellyseerrItem> ItemsHidden { get; set; } = new();
    public List<IJellyseerrItem> ItemsUnhidden { get; set; } = new();
    
    // Computed properties - filter by type
    public List<JellyfinMovie> ProcessedMovies => ItemsProcessed.OfType<JellyfinMovie>().ToList();
    public List<JellyfinSeries> ProcessedShows => ItemsProcessed.OfType<JellyfinSeries>().ToList();
    public List<JellyfinMovie> FoundMovies => ItemsFound.OfType<JellyfinMovie>().ToList();
    public List<JellyfinSeries> FoundShows => ItemsFound.OfType<JellyfinSeries>().ToList();
    public List<JellyseerrMediaRequest> CreatedMovies => ItemsCreated.Where(r => r?.Media?.MediaType == JellyseerrModel.MediaType.MOVIE).ToList();
    public List<JellyseerrMediaRequest> CreatedShows => ItemsCreated.Where(r => r?.Media?.MediaType == JellyseerrModel.MediaType.TV).ToList();
    public List<JellyfinMovie> BlockedMovies => ItemsBlocked.OfType<JellyfinMovie>().ToList();
    public List<JellyfinSeries> BlockedShows => ItemsBlocked.OfType<JellyfinSeries>().ToList();
    public List<JellyseerrMovie> HiddenMovies => ItemsHidden.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> HiddenShows => ItemsHidden.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> UnhiddenMovies => ItemsUnhidden.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> UnhiddenShows => ItemsUnhidden.OfType<JellyseerrShow>().ToList();
    
    // Count properties for table display
    public int MoviesProcessed => ProcessedMovies.Count;
    public int ShowsProcessed => ProcessedShows.Count;
    public int MoviesFound => FoundMovies.Count;
    public int ShowsFound => FoundShows.Count;
    public int MoviesCreated => CreatedMovies.Count;
    public int ShowsCreated => CreatedShows.Count;
    public int MoviesBlocked => BlockedMovies.Count;
    public int ShowsBlocked => BlockedShows.Count;
    public int MoviesHidden => HiddenMovies.Count;
    public int ShowsHidden => HiddenShows.Count;
    public int MoviesUnhidden => UnhiddenMovies.Count;
    public int ShowsUnhidden => UnhiddenShows.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\nRefresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|--------------|--------------|--------------|--------------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-14}{separator}{"Movies",-14}{separator}{"Shows",-14}{separator}{"Total",-14}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"❤️ Processed",-14}{separator}{MoviesProcessed,14}{separator}{ShowsProcessed,14}{separator}{MoviesProcessed + ShowsProcessed,14}{separator}");
        result.AppendLine($"{separator}{"🔍 Found",-14}{separator}{MoviesFound,14}{separator}{ShowsFound,14}{separator}{MoviesFound + ShowsFound,14}{separator}");
        result.AppendLine($"{separator}{"➕ Created",-14}{separator}{MoviesCreated,14}{separator}{ShowsCreated,14}{separator}{MoviesCreated + ShowsCreated,14}{separator}");
        result.AppendLine($"{separator}{"🚫 Blocked",-14}{separator}{MoviesBlocked,14}{separator}{ShowsBlocked,14}{separator}{MoviesBlocked + ShowsBlocked,14}{separator}");
        
        // Hidden row (only show if there are hidden items)
        if (MoviesHidden > 0 || ShowsHidden > 0)
        {
            result.AppendLine($"{separator}{"🙈 Hidden",-14}{separator}{MoviesHidden,14}{separator}{ShowsHidden,14}{separator}{MoviesHidden + ShowsHidden,14}{separator}");
        }
        
        // Unhidden row (only show if there are unhidden items)
        if (MoviesUnhidden > 0 || ShowsUnhidden > 0)
        {
            result.AppendLine($"{separator}{"👁️ Unhidden",-14}{separator}{MoviesUnhidden,14}{separator}{ShowsUnhidden,14}{separator}{MoviesUnhidden + ShowsUnhidden,14}{separator}");
        }
        
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
    // Total unique items considered for sorting (movies + shows) regardless of user count
    public int Processed { get; set; }
    
    // Separate items by type
    public List<(IJellyfinItem item, int playCount)> MoviesSorted => ItemsSorted.Where(x => x.item is JellyfinMovie).ToList();
    public List<(IJellyfinItem item, int playCount)> ShowsSorted => ItemsSorted.Where(x => x.item is JellyfinSeries).ToList();
    public List<(IJellyfinItem? item, string path)> MoviesSkipped => ItemsSkipped.Where(x => x.item is JellyfinMovie).ToList();
    public List<(IJellyfinItem? item, string path)> ShowsSkipped => ItemsSkipped.Where(x => x.item is JellyfinSeries).ToList();
    
    // For failed items (just paths), we can't easily determine type without item reference
    // So we'll show total failed count for both columns
    
    public int MoviesSortedCount => MoviesSorted.Count;
    public int ShowsSortedCount => ShowsSorted.Count;
    public int MoviesSkippedCount => MoviesSkipped.Count;
    public int ShowsSkippedCount => ShowsSkipped.Count;
    
    // Processed counts - approximate based on sorted + skipped (failed items can't be categorized)
    public int MoviesProcessed => MoviesSortedCount + MoviesSkippedCount;
    public int ShowsProcessed => ShowsSortedCount + ShowsSkippedCount;

    public int Sorted => ItemsSorted.Count;
    public int Failed => ItemsFailed.Count;
    public int Skipped => ItemsSkipped.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine($"{"📦 Processed:",-15}{Processed,15}");
        result.AppendLine($"{"✅ Sorted:",-15}{Sorted,15}");
        result.AppendLine($"{"⏭️ Skipped:",-15}{Skipped,15}");
        result.AppendLine($"{"❌ Failed:",-15}{Failed,15}");
        
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
    public string Details { get; set; } = "🎲 Algorithm: The sort order algorithm used (None, Random, Smart, Smartish)\n👥 Users: Play counts are individually updated for each user in the JellyBridge library\n🔄 Refresh: Refreshes all Jellyfin libraries containing the JellyBridge folder using the metadata options\n📦 Processed: Total items in JellyBridge libraries (movies + shows)\n✅ Sorted: Items whose play counts were updated this run\n⏭️ Skipped: Items excluded from sorting (e.g., .ignore files)\n❌ Failed: Items that could not be processed (not found/type mismatch/errors)";
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
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        result.AppendLine();
        
        // Add individual items with 2-space indentation
        result.AppendLine($"🎲 Algorithm: {SortAlgorithm}");
        result.AppendLine($"👥 Users: {Users.Count}");
        
        if (Refresh != null)
        {
            // Sort always uses refreshUserData: false, which results in "Scan for new and updated files"
            result.AppendLine($"Refresh: Scan for new and updated files, Do not replace images");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|--------------|--------------|--------------|--------------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-14}{separator}{"Movies",-14}{separator}{"Shows",-14}{separator}{"Total",-14}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦 Processed",-14}{separator}{ProcessResult.MoviesProcessed,14}{separator}{ProcessResult.ShowsProcessed,14}{separator}{ProcessResult.MoviesProcessed + ProcessResult.ShowsProcessed,14}{separator}");
        result.AppendLine($"{separator}{"✅ Sorted",-14}{separator}{ProcessResult.MoviesSortedCount,14}{separator}{ProcessResult.ShowsSortedCount,14}{separator}{ProcessResult.MoviesSortedCount + ProcessResult.ShowsSortedCount,14}{separator}");
        result.AppendLine($"{separator}{"⏭️ Skipped",-14}{separator}{ProcessResult.MoviesSkippedCount,14}{separator}{ProcessResult.ShowsSkippedCount,14}{separator}{ProcessResult.MoviesSkippedCount + ProcessResult.ShowsSkippedCount,14}{separator}");
        result.AppendLine($"{separator}{"❌ Failed",-14}{separator}{ProcessResult.Failed,14}{separator}{ProcessResult.Failed,14}{separator}{ProcessResult.Failed * 2,14}{separator}");
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// Cleanup Operation Results
// ============================================================================

/// <summary>
/// Result of a cleanup operation.
/// </summary>
public class CleanupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = "🧹 Cleanup: Removes old metadata items and folders without metadata.json files\n🔄 Refresh: When items are deleted, refreshes all Jellyfin libraries containing the JellyBridge folder (search for missing metadata only, do not replace images)\n📦 Processed: Number of items checked for cleanup\n🗑️ Deleted: Items deleted due to retention policy or missing metadata.json";
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyseerrItem> ItemsProcessed { get; set; } = new();
    public List<IJellyseerrItem> ItemsDeleted { get; set; } = new();
    public int FoldersWithoutMetadataDeleted { get; set; }
    
    // Computed properties - filter by type
    public List<JellyseerrMovie> ProcessedMovies => ItemsProcessed.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> ProcessedShows => ItemsProcessed.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> DeletedMovies => ItemsDeleted.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> DeletedShows => ItemsDeleted.OfType<JellyseerrShow>().ToList();
    
    // Count properties
    public int MoviesProcessed => ProcessedMovies.Count;
    public int ShowsProcessed => ProcessedShows.Count;
    public int TotalProcessed => ItemsProcessed.Count;
    public int MoviesDeleted => DeletedMovies.Count;
    public int ShowsDeleted => DeletedShows.Count;
    public int TotalDeleted => ItemsDeleted.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            var refreshType = Refresh.FullRefresh ? "Replace all metadata" : "Search for missing metadata";
            var refreshImages = Refresh.RefreshImages ? "Replace existing images" : "Do not replace images";
            result.AppendLine($"\nRefresh Plan: {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|--------------|--------------|--------------|--------------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-14}{separator}{"Movies",-14}{separator}{"Shows",-14}{separator}{"Total",-14}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦 Processed",-14}{separator}{MoviesProcessed,14}{separator}{ShowsProcessed,14}{separator}{TotalProcessed,14}{separator}");
        result.AppendLine($"{separator}{"🗑️ Deleted",-14}{separator}{MoviesDeleted,14}{separator}{ShowsDeleted,14}{separator}{TotalDeleted,14}{separator}");
        
        if (FoldersWithoutMetadataDeleted > 0)
        {
            result.AppendLine();
            result.AppendLine($"Folders without metadata.json deleted: {FoldersWithoutMetadataDeleted}");
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