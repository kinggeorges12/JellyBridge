using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

public interface RefreshableResult
{
    abstract RefreshPlan? Refresh { get; set; }
}

// ============================================================================
// SyncFromJellyseerr Operation Results
// ============================================================================

/// <summary>
/// Result of a sync operation to Jellyseerr (creating requests).
/// </summary>
public class SyncJellyseerrResult : RefreshableResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Join("\n", 
        ResultDetails.Refresh, 
        ResultDetails.Processed, 
        ResultDetails.ItemsAdded, 
        ResultDetails.ItemsUpdated, 
        ResultDetails.ItemsSkipped, 
        ResultDetails.ItemsHidden);
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();
    public List<IJellyseerrItem> ItemsSkipped { get; set; } = new();
    public List<IJellyseerrItem> ItemsHidden { get; set; } = new();
    
    // Computed properties - filter by type
    public List<JellyseerrMovie> AddedMovies => ItemsAdded.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> AddedShows => ItemsAdded.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> UpdatedMovies => ItemsUpdated.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> UpdatedShows => ItemsUpdated.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> SkippedMovies => ItemsSkipped.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> SkippedShows => ItemsSkipped.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> HiddenMovies => ItemsHidden.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> HiddenShows => ItemsHidden.OfType<JellyseerrShow>().ToList();
    
    // Count properties for table display
    public int ProcessedMoviesCount => AddedMovies.Count + UpdatedMovies.Count + SkippedMovies.Count;
    public int ProcessedShowsCount => AddedShows.Count + UpdatedShows.Count + SkippedShows.Count;

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        result.AppendLine(Message);
        
        result.AppendLine("\nDetails:");
        result.AppendLine(Details);
        
        if (Refresh != null)
        {
            result.AppendLine();
            result.AppendLine(Refresh.ToString());
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Series  ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{ProcessedMoviesCount,8}  "}{separator}{$"{ProcessedShowsCount,8}  "}{separator}{$"{ProcessedMoviesCount + ProcessedShowsCount,8}  "}{separator}");
        result.AppendLine($"{separator}{"➕\t"}{"Added",-10}{separator}{$"{AddedMovies.Count,8}  "}{separator}{$"{AddedShows.Count,8}  "}{separator}{$"{AddedMovies.Count + AddedShows.Count,8}  "}{separator}");
        result.AppendLine($"{separator}{"🛠️\t"}{"Updated",-10}{separator}{$"{UpdatedMovies.Count,8}  "}{separator}{$"{UpdatedShows.Count,8}  "}{separator}{$"{UpdatedMovies.Count + UpdatedShows.Count,8}  "}{separator}");
        result.AppendLine($"{separator}{"⏭️\t"}{"Skipped",-10}{separator}{$"{SkippedMovies.Count,8}  "}{separator}{$"{SkippedShows.Count,8}  "}{separator}{$"{SkippedMovies.Count + SkippedShows.Count,8}  "}{separator}");
        result.AppendLine($"{separator}{"🙈\t"}{"Hidden",-10}{separator}{$"{HiddenMovies.Count,8}  "}{separator}{$"{HiddenShows.Count,8}  "}{separator}{$"{HiddenMovies.Count + HiddenShows.Count,8}  "}{separator}");
        
        return result.ToString();
    }
}

// ============================================================================
// SyncToJellyseerr Operation Results
// ============================================================================

/// <summary>
/// Result of a sync operation from Jellyfin (processing favorites).
/// </summary>
public class SyncJellyfinResult : RefreshableResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Join("\n", 
        ResultDetails.Refresh, 
        ResultDetails.FavoritesProcessed, 
        ResultDetails.FavoritesFound, 
        ResultDetails.RequestsCreated, 
        ResultDetails.RequestsBlocked, 
        ResultDetails.ItemsHidden, 
        ResultDetails.ItemsCleared, 
        ResultDetails.ItemsUnhidden);
    public RefreshPlan? Refresh { get; set; }
    
    // Unified collections
    public List<IJellyfinItem> ItemsProcessed { get; set; } = new();
    public List<IJellyfinItem> ItemsFound { get; set; } = new();
    public List<JellyseerrMediaRequest> ItemsCreated { get; set; } = new();
    public List<IJellyfinItem> ItemsBlocked { get; set; } = new();
    public List<IJellyseerrItem> ItemsHidden { get; set; } = new();
    public List<IJellyseerrItem> ItemsCleared { get; set; } = new();
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
    public List<JellyseerrMovie> ClearedMovies => ItemsCleared.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> ClearedShows => ItemsCleared.OfType<JellyseerrShow>().ToList();
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
    public int MoviesCleared => ClearedMovies.Count;
    public int ShowsCleared => ClearedShows.Count;
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
            result.AppendLine();
            result.AppendLine(Refresh.ToString());
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Series  ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"❤️\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{MoviesProcessed + ShowsProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"🔍\t"}{"Found",-10}{separator}{$"{MoviesFound,8}  "}{separator}{$"{ShowsFound,8}  "}{separator}{$"{MoviesFound + ShowsFound,8}  "}{separator}");
        result.AppendLine($"{separator}{"➕\t"}{"Created",-10}{separator}{$"{MoviesCreated,8}  "}{separator}{$"{ShowsCreated,8}  "}{separator}{$"{MoviesCreated + ShowsCreated,8}  "}{separator}");
        result.AppendLine($"{separator}{"🚫\t"}{"Blocked",-10}{separator}{$"{MoviesBlocked,8}  "}{separator}{$"{ShowsBlocked,8}  "}{separator}{$"{MoviesBlocked + ShowsBlocked,8}  "}{separator}");
        result.AppendLine($"{separator}{"🙈\t"}{"Hidden",-10}{separator}{$"{MoviesHidden,8}  "}{separator}{$"{ShowsHidden,8}  "}{separator}{$"{MoviesHidden + ShowsHidden,8}  "}{separator}");
        result.AppendLine($"{separator}{"🧹\t"}{"Cleared",-10}{separator}{$"{MoviesCleared,8}  "}{separator}{$"{ShowsCleared,8}  "}{separator}{$"{MoviesCleared + ShowsCleared,8}  "}{separator}");
        result.AppendLine($"{separator}{"👁️\t"}{"Unhidden",-10}{separator}{$"{MoviesUnhidden,8}  "}{separator}{$"{ShowsUnhidden,8}  "}{separator}{$"{MoviesUnhidden + ShowsUnhidden,8}  "}{separator}");
        
        return result.ToString();
    }
}

// ============================================================================
// SortJellyBridge Operation Results
// ============================================================================

/// <summary>
/// Result of sorting the discover library by updating play counts.
/// </summary>
public class SortLibraryResult : RefreshableResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Join("\n", 
        ResultDetails.SortAlgorithm, 
        ResultDetails.SortUsers, 
        ResultDetails.Refresh, 
        ResultDetails.SortProcessed, 
        ResultDetails.SortSorted, 
        ResultDetails.SortSkipped, 
        ResultDetails.SortFailed);
    public BridgeConfiguration.SortOrderOptions SortAlgorithm { get; set; }
    public List<JellyfinUser> Users { get; set; } = new();
    public RefreshPlan? Refresh { get; set; }
    
    // Collections
    public List<(IJellyfinItem item, int playCount)> ItemsSorted { get; set; } = new();
    public List<(BaseItemKind mediaType, string folder)> ItemsFailed { get; set; } = new();
    public List<(BaseItemKind mediaType, string path)> ItemsSkipped { get; set; } = new();
    
    // Separate items by type
    public List<(IJellyfinItem item, int playCount)> MoviesSorted => ItemsSorted.Where(x => x.item is JellyfinMovie).ToList();
    public List<(IJellyfinItem item, int playCount)> ShowsSorted => ItemsSorted.Where(x => x.item is JellyfinSeries).ToList();
    public List<(BaseItemKind mediaType, string path)> MoviesSkipped => ItemsSkipped.Where(x => x.mediaType == BaseItemKind.Movie).ToList();
    public List<(BaseItemKind mediaType, string path)> ShowsSkipped => ItemsSkipped.Where(x => x.mediaType == BaseItemKind.Series).ToList();
    public List<(BaseItemKind mediaType, string path)> MoviesFailed => ItemsFailed.Where(x => x.mediaType == BaseItemKind.Movie).ToList();
    public List<(BaseItemKind mediaType, string path)> ShowsFailed => ItemsFailed.Where(x => x.mediaType == BaseItemKind.Series).ToList();
    
    public int MoviesSortedCount => MoviesSorted.Count;
    public int ShowsSortedCount => ShowsSorted.Count;
    public int MoviesSkippedCount => MoviesSkipped.Count;
    public int ShowsSkippedCount => ShowsSkipped.Count;
    public int MoviesFailedCount => MoviesFailed.Count;
    public int ShowsFailedCount => ShowsFailed.Count;
    
    // Processed counts - approximate based on sorted + skipped
    public int MoviesProcessed => MoviesSortedCount + MoviesSkippedCount + MoviesFailedCount;
    public int ShowsProcessed => ShowsSortedCount + ShowsSkippedCount + ShowsFailedCount;


    public int Sorted => ItemsSorted.Count;
    public int Skipped => ItemsSkipped.Count;
    public int Failed => ItemsFailed.Count;
    public int Processed => Sorted + Skipped + Failed;

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
            result.AppendLine(Refresh.ToString());
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Series  ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{Processed,8}  "}{separator}");
        result.AppendLine($"{separator}{"✅\t"}{"Sorted",-10}{separator}{$"{MoviesSortedCount,8}  "}{separator}{$"{ShowsSortedCount,8}  "}{separator}{$"{Sorted,8}  "}{separator}");
        result.AppendLine($"{separator}{"⏭️\t"}{"Skipped",-10}{separator}{$"{MoviesSkippedCount,8}  "}{separator}{$"{ShowsSkippedCount,8}  "}{separator}{$"{Skipped,8}  "}{separator}");
        result.AppendLine($"{separator}{"❌\t"}{"Failed",-10}{separator}{$"{MoviesFailedCount,8}  "}{separator}{$"{ShowsFailedCount,8}  "}{separator}{$"{Failed,8}  "}{separator}");
        
        return result.ToString();
    }
}

// ============================================================================
// Cleanup Operation Results
// ============================================================================

/// <summary>
/// Result of a cleanup operation.
/// </summary>
public class CleanupResult : RefreshableResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Join("\n", 
        ResultDetails.Refresh, 
        ResultDetails.CleanupProcessed, 
        ResultDetails.CleanupCreated, 
        ResultDetails.CleanupDeleted, 
        ResultDetails.CleanupFolders);
    public RefreshPlan? Refresh { get; set; }

    // Unified collections
    public List<IJellyseerrItem> ItemsProcessed { get; set; } = new();
    public List<IJellyseerrItem> ItemsDeleted { get; set; } = new();
    public List<IJellyseerrItem> ItemsCreated { get; set; } = new();
    public int MoviesCleaned { get; set; }
    public int ShowsCleaned { get; set; }
    public int ItemsCleaned => MoviesCleaned + ShowsCleaned;

    // Computed properties - filter by type
    public List<JellyseerrMovie> ProcessedMovies => ItemsProcessed.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> ProcessedShows => ItemsProcessed.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> CreatedMovies => ItemsCreated.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> CreatedShows => ItemsCreated.OfType<JellyseerrShow>().ToList();
    public List<JellyseerrMovie> DeletedMovies => ItemsDeleted.OfType<JellyseerrMovie>().ToList();
    public List<JellyseerrShow> DeletedShows => ItemsDeleted.OfType<JellyseerrShow>().ToList();

    // Count properties
    public int MoviesProcessed => ProcessedMovies.Count;
    public int ShowsProcessed => ProcessedShows.Count;
    public int MoviesCreated => CreatedMovies.Count;
    public int ShowsCreated => CreatedShows.Count;
    public int TotalCreated => ItemsCreated.Count;
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
            result.AppendLine();
            result.AppendLine(Refresh.ToString());
        }
        
        result.AppendLine();
        const string separator = "|";
        const string rowBorder = "|-----------------|----------|----------|----------|";
        
        // Header row
        result.AppendLine($"{separator}{"",-17}{separator}{"  Movies  ",-10}{separator}{"  Series  ",-10}{separator}{"  Total   ",-10}{separator}");
        result.AppendLine($"{rowBorder}");
        // Data rows
        result.AppendLine($"{separator}{"📦\t"}{"Processed",-10}{separator}{$"{MoviesProcessed,8}  "}{separator}{$"{ShowsProcessed,8}  "}{separator}{$"{TotalProcessed,8}  "}{separator}");
        result.AppendLine($"{separator}{"➕\t"}{"Created",-10}{separator}{$"{MoviesCreated,8}  "}{separator}{$"{ShowsCreated,8}  "}{separator}{$"{TotalCreated,8}  "}{separator}");
        result.AppendLine($"{separator}{"🗑️\t"}{"Deleted",-10}{separator}{$"{MoviesDeleted,8}  "}{separator}{$"{ShowsDeleted,8}  "}{separator}{$"{TotalDeleted,8}  "}{separator}");
        result.AppendLine($"{separator}{"🧹\t"}{"Cleaned",-10}{separator}{$"{MoviesCleaned,8}  "}{separator}{$"{ShowsCleaned,8}  "}{separator}{$"{ItemsCleaned,8}  "}{separator}");
        
        return result.ToString();
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
    public bool ScanAllLibraries { get; set; } = false;
    public bool CreateRefresh { get; set; }
    public bool RemoveRefresh { get; set; }
    public bool RefreshImages { get; set; }
    
    public RefreshPlan()
    {
        ScanAllLibraries = CreateRefresh || RemoveRefresh;
    }

    public override string ToString()
    {
        var result = new System.Text.StringBuilder();
        var refreshImagesTrue = "☑️ Replace existing images";
        var refreshImagesFalse = "🔳 Replace existing images";
        var refreshImages = RefreshImages ? refreshImagesTrue : refreshImagesFalse;
        if (ScanAllLibraries)
        {
            result.AppendLine($"⟳ Scan All Libraries");
        }
        if (RemoveRefresh)
        {
            result.AppendLine($"🔄 Search for missing metadata, {refreshImagesFalse}");
        }
        result.AppendLine($"🔄 Scan for new and updated files");
        if (CreateRefresh)
        {
            result.AppendLine($"🔄 Replace all metadata, {refreshImages}");
        }
        return result.ToString().TrimEnd();
    }
}

public static class ResultDetails
{
    public const string Refresh = "🔄 Refresh: Refreshes all Jellyfin libraries containing the JellyBridge folder using the metadata options";
    public const string Processed = "📦 Processed: Number of items processed from Jellyseerr";
    public const string ItemsAdded = "➕ Added: Items added in the JellyBridge library from content in Jellyseerr discover pages";
    public const string ItemsUpdated = "🛠️ Updated: Items updated in the JellyBridge library from content in Jellyseerr discover pages";
    public const string ItemsSkipped = "⏭️ Skipped: Items not processed from discover content (folder contains .ignore file)";
    public const string ItemsHidden = "🙈 Hidden: Jellyfin items marked with an .ignore file";
    public const string FavoritesProcessed = "❤️ Processed: Total items favorited in Jellyfin";
    public const string FavoritesFound = "🔍 Found: Items favorited in JellyBridge library";
    public const string RequestsCreated = "➕ Created: Requests created in Jellyseerr";
    public const string RequestsBlocked = "🚫 Blocked: Requests blocked by Jellyseerr due to quota limits or permission issues";
    public const string ItemsCleared = "💔 Cleared: Items that are successfully requested in Jellyseerr are unfavorited and view counts reset to zero";
    public const string ItemsUnhidden = "👁️ Unhidden: Requests in Jellyseerr that are declined are shown in Jellyfin";
    public const string CleanupProcessed = "📦 Processed: Total items checked for cleanup";
    public const string CleanupCreated = "➕ Created: Total placeholder videos created during cleanup";
    public const string CleanupDeleted = "🗑️ Deleted: Items deleted due to retention policy";
    public const string CleanupFolders = "🧹 Cleaned: Items deleted that were not managed by JellyBridge (folder missing metadata.json file)";
    public const string SortAlgorithm = "🎲 Algorithm: The sort order algorithm used (None, Random, Smart, Smartish)";
    public const string SortUsers = "👥 Users: Play counts are individually updated for each user in the JellyBridge library";
    public const string SortProcessed = "📦 Processed: Total items in JellyBridge libraries";
    public const string SortSorted = "✅ Sorted: Items whose play counts were updated this run";
    public const string SortSkipped = "⏭️ Skipped: Ignored items are excluded from sorting";
    public const string SortFailed = "❌ Failed: Items with missing or unsynced metadata in Jellyfin";
}