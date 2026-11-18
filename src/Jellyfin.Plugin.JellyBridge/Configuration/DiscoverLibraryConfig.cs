using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Jellyfin.Plugin.JellyBridge.Utils;
using static Jellyfin.Plugin.JellyBridge.BridgeModels.BridgeConfiguration;

namespace Jellyfin.Plugin.JellyBridge.Configuration;

/// <summary>
/// Which Jellyseerr list fills a Discover library. Each list is also the template used when adding a library.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoverListType
{
    /// <summary>Discover, by popularity (the original single-library behaviour).</summary>
    Popular = 0,

    /// <summary>Discover, released up to today, newest first.</summary>
    NewReleases = 1,

    /// <summary>Discover, release date after today. Unreleased titles aren't on a service yet, so networks are a warning.</summary>
    ComingSoon = 2,

    /// <summary>Jellyseerr's trending list, in its own order. Never combined with networks or rating filters.</summary>
    Trending = 3,

    /// <summary>Discover, by score, with a vote-count floor.</summary>
    TopRated = 4
}

/// <summary>
/// What a Discover library's folder holds.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoverLibraryType
{
    /// <summary>Movies and series together.</summary>
    All = 0,

    /// <summary>Movies only.</summary>
    Movies = 1,

    /// <summary>Series only.</summary>
    Series = 2
}

/// <summary>
/// "Order in Jellyfin": how the titles of a Discover library end up ordered in the Jellyfin UI.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoverLibraryOrder
{
    /// <summary>The list's own order (newest first, trending rank, score): play counts follow the fetch position.</summary>
    AsList = 0,

    /// <summary>Leave ordering to Jellyfin (no play count changes; today's "None").</summary>
    Jellyfin = 1,

    /// <summary>The Random sort order.</summary>
    Random = 2,

    /// <summary>The Smart sort order.</summary>
    Smart = 3,

    /// <summary>The Smartish sort order.</summary>
    Smartish = 4
}

/// <summary>
/// One Discover library: one subfolder of the Library Directory, one Jellyfin library, and the Jellyseerr list,
/// networks and settings that fill it. Several live side by side; exactly one is <see cref="IsDefault"/>.
/// </summary>
public class DiscoverLibraryConfig
{
    /// <summary>
    /// Tag given to titles in a Trending library (stored like a network name).
    /// </summary>
    public const string TrendingTag = "Trending";

    /// <summary>
    /// Gets or sets the library name, used for the card, the folder and the Jellyfin library. Unique ignoring case.
    /// Characters not allowed in folder names are stripped from the folder name only (see <see cref="FolderName"/>).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which Jellyseerr list fills this library.
    /// </summary>
    [JsonPropertyName("listType")]
    public DiscoverListType ListType { get; set; } = DiscoverListType.Popular;

    /// <summary>
    /// Gets or sets the ids of the networks in this library, referring to the global NetworkMap. Empty means all
    /// networks. Ignored for <see cref="DiscoverListType.Trending"/>.
    /// </summary>
    [JsonPropertyName("networkIds")]
    public List<int> NetworkIds { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the library holds movies, series or both.
    /// </summary>
    [JsonPropertyName("libraryType")]
    public DiscoverLibraryType LibraryType { get; set; } = DiscoverLibraryType.All;

    /// <summary>
    /// Gets or sets whether this is the default library: titles opened from search go here, and it can't be deleted.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; set; }

    /// <summary>
    /// Gets or sets whether the library syncs. Null means on.
    /// </summary>
    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets how many list pages to fetch (0 = unlimited); null falls back to the global Discover Pages.
    /// </summary>
    [JsonPropertyName("pages")]
    public int? Pages { get; set; }

    /// <summary>
    /// Gets or sets the minimum TMDB rating for this library (0 = off); null falls back to the global Minimum Rating.
    /// </summary>
    [JsonPropertyName("minimumRating")]
    public double? MinimumRating { get; set; }

    /// <summary>
    /// Gets or sets the minimum TMDB vote count used with <see cref="MinimumRating"/>; null falls back to the global value.
    /// </summary>
    [JsonPropertyName("minimumVotes")]
    public int? MinimumVotes { get; set; }

    /// <summary>
    /// Gets or sets the retention in days before cleanup removes a title that left the list; null falls back to the global value.
    /// </summary>
    [JsonPropertyName("retentionDays")]
    public int? RetentionDays { get; set; }

    /// <summary>
    /// Gets or sets how titles are ordered in Jellyfin; null means <see cref="DiscoverLibraryOrder.AsList"/>.
    /// </summary>
    [JsonPropertyName("order")]
    public DiscoverLibraryOrder? Order { get; set; }

    /// <summary>
    /// Gets or sets the list this library last synced against. Decision 9: when <see cref="ListType"/> changes,
    /// the next sync removes the old list's titles right away — the sync compares against this value and writes
    /// it back at the end of every successful sync. It must survive a restart, so it persists like the other
    /// settings. Null until a sync first records it (a first sync changes nothing).
    /// </summary>
    [JsonPropertyName("lastSyncedListType")]
    public DiscoverListType? LastSyncedListType { get; set; }

    /// <summary>
    /// Gets or sets a folder that overrides the usual subfolder of the Library Directory. Only used by the libraries
    /// synthesized from pre-library settings until the startup migration moves them into their subfolder; never persisted.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public string? DirectoryOverride { get; set; }

    /// <summary>
    /// Gets a value indicating whether this library was synthesized from the previous single-library settings and the
    /// migration hasn't persisted it yet. Such a library keeps the legacy folder behaviour (network folders, mixed/separate).
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool Legacy { get; init; }

    /// <summary>
    /// Gets the folder name derived from <see cref="Name"/>: invalid folder characters are stripped.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public string FolderName => FolderUtils.SanitizeFileName(Name);

    /// <summary>
    /// Gets a value indicating whether the library syncs (null means on).
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool EnabledOrDefault => IsEnabled ?? true;

    /// <summary>
    /// Gets a value indicating whether this is the default library.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool DefaultOrDefault => IsDefault ?? false;

    /// <summary>
    /// Gets the "Order in Jellyfin" of the library (null means the list's own order).
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public DiscoverLibraryOrder OrderOrDefault => Order ?? DiscoverLibraryOrder.AsList;

    /// <summary>
    /// Gets a value indicating whether the library's list differs from the one it last synced against
    /// (decision 9). The sync after the change removes the old list's titles immediately instead of waiting for
    /// retention. False when no list has been recorded yet — the first sync never removes anything.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool ListTypeChangedSinceLastSync => LastSyncedListType.HasValue && LastSyncedListType.Value != ListType;

    /// <summary>
    /// Gets or sets the title-rule signature this library last synced against (task 9, reviewer item 6): the
    /// settings that change WHICH titles belong (list, type, pages, minimum rating/votes, networks). The
    /// reconciler runs the library's sync when the current <see cref="SyncSignature"/> differs — one stored value
    /// covers every such setting, so no per-setting change flags are needed. Null until the first sync or save
    /// after the upgrade records it (an upgrade changes nothing).
    /// </summary>
    [JsonPropertyName("lastSyncedSignature")]
    public string? LastSyncedSignature { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="Order"/> the library was last sorted by (task 9, reviewer item 7). The
    /// reconciler re-sorts the library when this differs from the current <see cref="OrderOrDefault"/>. Null
    /// until the first save-reconcile records it — upgrading never re-sorts every library.
    /// </summary>
    [JsonPropertyName("lastSyncedOrder")]
    public DiscoverLibraryOrder? LastSyncedOrder { get; set; }

    /// <summary>
    /// Gets or sets the old name of a renaming save (task 9, reviewer item 2): the settings page sends it
    /// transiently with the POST so the server knows which folder to move — it is cleared before the config is
    /// saved, so it never persists.
    /// </summary>
    [JsonPropertyName("renamedFrom")]
    [XmlIgnore]
    public string? RenamedFrom { get; set; }

    /// <summary>
    /// Gets or sets the old name of a rename that has not fully applied yet (9a-b, review finding 3). Unlike
    /// <see cref="RenamedFrom"/> this PERSISTS: when a rename is refused (something is playing) or fails, the
    /// next sync's safety-net reconcile reads it and retries the move + re-create — otherwise the safety net
    /// would see the card's new name with no folder, find no library to adopt and create a SECOND empty
    /// library while the old one stays orphaned. The reconciler clears it only once folder, Jellyfin name and
    /// Jellyfin path all match the card.
    /// </summary>
    [JsonPropertyName("pendingRenameFrom")]
    public string? PendingRenameFrom { get; set; }

    /// <summary>
    /// Gets the title-rule signature: every setting that changes which titles the library holds (task 9 reviewer
    /// item 6). The reconciler compares it with <see cref="LastSyncedSignature"/>; everything else (name, order,
    /// on/off) is reconciled without a sync.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public string SyncSignature => string.Join('|',
        ListType.ToString(),
        LibraryType.ToString(),
        (Pages ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        (MinimumRating ?? -1).ToString("0.0################", System.Globalization.CultureInfo.InvariantCulture),
        (MinimumVotes ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        // 9a-9: keep the networks' stored ORDER — it decides the merge order of the fetch, so a re-order of the
        // same ids is a real change.
        string.Join(',', NetworkIds.Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    /// <summary>
    /// Gets a value indicating whether the title rules changed since the last sync (task 9). False when no
    /// signature has been recorded yet — the first sync after an upgrade removes nothing beyond its own cleanup.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool SyncSignatureChangedSinceLastSync => !string.IsNullOrEmpty(LastSyncedSignature)
        && LastSyncedSignature != SyncSignature;

    /// <summary>
    /// Gets the folder of this library under the given Library Directory: its subfolder, or the legacy folder a
    /// synthesized library still uses before the migration moved it.
    /// </summary>
    /// <param name="root">The Library Directory.</param>
    /// <returns>The folder path without a trailing separator.</returns>
    public string GetDirectory(string root)
    {
        if (!string.IsNullOrWhiteSpace(DirectoryOverride))
        {
            return FolderUtils.GetNormalizedPath(DirectoryOverride)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var folderName = string.IsNullOrWhiteSpace(FolderName) ? DiscoverLibraries.DefaultName : FolderName;
        return FolderUtils.GetNormalizedPath(Path.Combine(root, folderName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Gets a value indicating whether this library's list is fetched per network.
    /// </summary>
    [XmlIgnore]
    [JsonIgnore]
    public bool UsesNetworks => UsesNetworksFor(ListType);

    /// <summary>
    /// Gets a value indicating whether a list can be narrowed to networks. Trending is a single network-less list.
    /// </summary>
    /// <param name="listType">The list to check.</param>
    /// <returns>True when the list can be narrowed to networks.</returns>
    public static bool UsesNetworksFor(DiscoverListType listType) => listType != DiscoverListType.Trending;

    /// <summary>
    /// Creates a library from a template (the list type), pre-filling the settings the "More options" section shows.
    /// Popular defaults to the Smart order; the other lists are only meaningful in their own order, so they default
    /// to "Same as the list". Top rated starts with a 300-vote floor (decision D1) and new releases with a 30-vote
    /// floor (decision D2) so a few early votes can't carry a title to the top.
    /// </summary>
    /// <param name="name">The library name.</param>
    /// <param name="listType">The list that fills it.</param>
    /// <param name="libraryType">Movies, Series or All.</param>
    /// <param name="networkIds">Network ids, or empty for all networks.</param>
    /// <returns>The new library.</returns>
    public static DiscoverLibraryConfig ForList(string name, DiscoverListType listType, DiscoverLibraryType libraryType, List<int>? networkIds = null)
    {
        var library = new DiscoverLibraryConfig
        {
            Name = name,
            ListType = listType,
            LibraryType = libraryType,
            NetworkIds = listType == DiscoverListType.Trending ? new List<int>() : networkIds ?? new List<int>(),
            IsEnabled = true,
            Order = listType == DiscoverListType.Popular ? DiscoverLibraryOrder.Smart : DiscoverLibraryOrder.AsList
        };

        // Template vote floors (D1/D2): Top rated 300, New releases 30. Other lists keep the global Minimum Votes.
        switch (listType)
        {
            case DiscoverListType.TopRated:
                library.MinimumVotes = 300;
                break;
            case DiscoverListType.NewReleases:
                library.MinimumVotes = 30;
                break;
        }

        return library;
    }

    /// <summary>
    /// Maps the pre-libraries sort settings onto one library's "Order in Jellyfin" (used by the libraries
    /// migration, and for the synthesized Legacy library): with the automated sort task off the ordering was left
    /// to Jellyfin, otherwise it was the global SortOrder (None meant the same as Jellyfin).
    /// </summary>
    /// <param name="enableAutomatedSortTask">The old EnableAutomatedSortTask setting.</param>
    /// <param name="globalSortOrder">The old global SortOrder setting.</param>
    /// <returns>The order the migration should store.</returns>
    public static DiscoverLibraryOrder OrderFromLegacySettings(bool enableAutomatedSortTask, SortOrderOptions globalSortOrder)
    {
        if (!enableAutomatedSortTask)
        {
            return DiscoverLibraryOrder.Jellyfin;
        }

        return globalSortOrder switch
        {
            SortOrderOptions.Random => DiscoverLibraryOrder.Random,
            SortOrderOptions.Smart => DiscoverLibraryOrder.Smart,
            SortOrderOptions.Smartish => DiscoverLibraryOrder.Smartish,
            _ => DiscoverLibraryOrder.Jellyfin
        };
    }

    /// <summary>
    /// Copies this library's settings to a new name, as the Duplicate button does.
    /// </summary>
    /// <param name="newName">The new library name.</param>
    /// <returns>The copy.</returns>
    public DiscoverLibraryConfig Copy(string newName)
    {
        return new DiscoverLibraryConfig
        {
            Name = newName,
            ListType = ListType,
            NetworkIds = NetworkIds.ToList(),
            LibraryType = LibraryType,
            IsDefault = false,
            IsEnabled = IsEnabled,
            Pages = Pages,
            MinimumRating = MinimumRating,
            MinimumVotes = MinimumVotes,
            RetentionDays = RetentionDays,
            Order = Order
        };
    }
}
