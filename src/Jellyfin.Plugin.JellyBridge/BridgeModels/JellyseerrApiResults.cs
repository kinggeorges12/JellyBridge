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
            var refreshImages = Refresh.RefreshImages ? "☑️ Replace existing images" : "🔳 Replace existing images";
            result.AppendLine($"\n🔄 {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Shows   ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{MoviesProcessed + ShowsProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"➕\t"}{"Added",-10}{separator}{$"{MoviesAdded,8}  "}{separator}{$"{ShowsAdded,8}  "}{separator}{$"{MoviesAdded + ShowsAdded,8}  "}{separator}");
        result.AppendLine($"{separator}{"🛠️\t"}{"Updated",-10}{separator}{$"{MoviesUpdated,8}  "}{separator}{$"{ShowsUpdated,8}  "}{separator}{$"{MoviesUpdated + ShowsUpdated,8}  "}{separator}");
        result.AppendLine($"{separator}{"⏭️\t"}{"Ignored",-10}{separator}{$"{MoviesIgnored,8}  "}{separator}{$"{ShowsIgnored,8}  "}{separator}{$"{MoviesIgnored + ShowsIgnored,8}  "}{separator}");
        result.AppendLine($"{separator}{"🙈\t"}{"Hidden",-10}{separator}{$"{MoviesHidden,8}  "}{separator}{$"{ShowsHidden,8}  "}{separator}{$"{MoviesHidden + ShowsHidden,8}  "}{separator}");
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// SyncToJellyseerr Operation Results
// ============================================================================

/// <summary>
/// Result of a sync operation from Jellyfin (processing favorites).
/// </summary>
public class SyncJellyfinResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = "🔄 Refresh: Refreshes all Jellyfin libraries containing the JellyBridge folder using the metadata options\n❤️ Processed: Number of favorites in Jellyfin\n🔍 Found: Number of favorites in JellyBridge library\n➕ Created: Requests created in Jellyseerr\n🚫 Blocked: Requests blocked by Jellyseerr due to quota limits or permission issues\n🙈 Hidden: Jellyfin items marked with an .ignore file after requesting them from Jellyseerr\n🧹 Cleaned: Items where favorite and/or play status was unmarked after requesting from Jellyseerr\n👁️ Unhidden: Requests in Jellyseerr that are declined are shown in Jellyfin";
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyfinItem> ItemsProcessed { get; set; } = new();
    public List<IJellyfinItem> ItemsFound { get; set; } = new();
    public List<JellyseerrMediaRequest> ItemsCreated { get; set; } = new();
    public List<IJellyfinItem> ItemsBlocked { get; set; } = new();
    public List<IJellyseerrItem> ItemsHidden { get; set; } = new();
    public List<IJellyseerrItem> ItemsCleaned { get; set; } = new();
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
    public List<JellyseerrMovie> CleanedMovies => ItemsCleaned.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> CleanedShows => ItemsCleaned.OfType<JellyseerrShow>().ToList();
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
    public int MoviesCleaned => CleanedMovies.Count;
    public int ShowsCleaned => CleanedShows.Count;
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
            var refreshImages = Refresh.RefreshImages ? "☑️ Replace existing images" : "🔳 Replace existing images";
            result.AppendLine($"\n🔄 {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Shows   ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"❤️\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{MoviesProcessed + ShowsProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"🔍\t"}{"Found",-10}{separator}{$"{MoviesFound,8}  "}{separator}{$"{ShowsFound,8}  "}{separator}{$"{MoviesFound + ShowsFound,8}  "}{separator}");
        result.AppendLine($"{separator}{"➕\t"}{"Created",-10}{separator}{$"{MoviesCreated,8}  "}{separator}{$"{ShowsCreated,8}  "}{separator}{$"{MoviesCreated + ShowsCreated,8}  "}{separator}");
        result.AppendLine($"{separator}{"🚫\t"}{"Blocked",-10}{separator}{$"{MoviesBlocked,8}  "}{separator}{$"{ShowsBlocked,8}  "}{separator}{$"{MoviesBlocked + ShowsBlocked,8}  "}{separator}");
        result.AppendLine($"{separator}{"🙈\t"}{"Hidden",-10}{separator}{$"{MoviesHidden,8}  "}{separator}{$"{ShowsHidden,8}  "}{separator}{$"{MoviesHidden + ShowsHidden,8}  "}{separator}");
        result.AppendLine($"{separator}{"🧹\t"}{"Cleaned",-10}{separator}{$"{MoviesCleaned,8}  "}{separator}{$"{ShowsCleaned,8}  "}{separator}{$"{MoviesCleaned + ShowsCleaned,8}  "}{separator}");
        result.AppendLine($"{separator}{"👁️\t"}{"Unhidden",-10}{separator}{$"{MoviesUnhidden,8}  "}{separator}{$"{ShowsUnhidden,8}  "}{separator}{$"{MoviesUnhidden + ShowsUnhidden,8}  "}{separator}");
        
        return result.ToString().TrimEnd();
    }
}

// ============================================================================
// SortJellyBridge Operation Results
// ============================================================================

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
    public RefreshPlan? Refresh { get; set; }
    
    // Collections
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
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        result.AppendLine();
        
        // Add individual items with 2-space indentation
        result.AppendLine($"🎲 {SortAlgorithm}");
        result.AppendLine($"👥 {Users.Count}");
        
        if (Refresh != null)
        {
            // Sort always uses refreshUserData: false, which results in "Scan for new and updated files"
            result.AppendLine($"🔄 Scan for new and updated files, 🔳 Replace existing images");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Shows   ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{MoviesProcessed + ShowsProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"✅\t"}{"Sorted",-10}{separator}{$"{MoviesSortedCount,8}  "}{separator}{$"{ShowsSortedCount,8}  "}{separator}{$"{MoviesSortedCount + ShowsSortedCount,8}  "}{separator}");
        result.AppendLine($"{separator}{"⏭️\t"}{"Skipped",-10}{separator}{$"{MoviesSkippedCount,8}  "}{separator}{$"{ShowsSkippedCount,8}  "}{separator}{$"{MoviesSkippedCount + ShowsSkippedCount,8}  "}{separator}");
        result.AppendLine($"{separator}{"❌\t"}{"Failed",-10}{separator}{$"{Failed,8}  "}{separator}{$"{Failed,8}  "}{separator}{$"{Failed * 2,8}  "}{separator}");
        
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
            var refreshImages = Refresh.RefreshImages ? "☑️ Replace existing images" : "🔳 Replace existing images";
            result.AppendLine($"\n🔄 {refreshType}, {refreshImages}");
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Shows   ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{TotalProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"🗑️\t"}{"Deleted",-10}{separator}{$"{MoviesDeleted,8}  "}{separator}{$"{ShowsDeleted,8}  "}{separator}{$"{TotalDeleted,8}  "}{separator}");
        
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