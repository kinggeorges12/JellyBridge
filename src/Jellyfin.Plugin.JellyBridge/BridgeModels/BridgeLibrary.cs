using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Represents a JellyBridge library containing metadata items and their locations.
/// </summary>
public class BridgeLibrary
{
    private readonly List<IJellyseerrItem> _items = new();

    /// <summary>
    /// Gets the library name (e.g., "Movies", "TV Shows").
    /// </summary>
    public string LibraryName { get; }

    /// <summary>
    /// Gets the normalized location paths for this library.
    /// </summary>
    public HashSet<string> Locations { get; }

    /// <summary>
    /// Gets the items in this library.
    /// </summary>
    public List<IJellyseerrItem> Items => _items;

    /// <summary>
    /// Gets the movie items in this library.
    /// </summary>
    public IEnumerable<JellyseerrMovie> Movies => _items.OfType<JellyseerrMovie>();

    /// <summary>
    /// Gets the show items in this library.
    /// </summary>
    public IEnumerable<JellyseerrShow> Shows => _items.OfType<JellyseerrShow>();

    /// <summary>
    /// Gets the count of items in this library.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets the count of movies in this library.
    /// </summary>
    public int MovieCount => _items.OfType<JellyseerrMovie>().Count();

    /// <summary>
    /// Gets the count of shows in this library.
    /// </summary>
    public int ShowCount => _items.OfType<JellyseerrShow>().Count();

    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeLibrary"/> class.
    /// </summary>
    /// <param name="libraryName">The name of the library.</param>
    /// <param name="locations">The normalized location paths for this library.</param>
    public BridgeLibrary(string libraryName, HashSet<string>? locations = null)
    {
        LibraryName = libraryName ?? string.Empty;
        Locations = locations ?? new HashSet<string>();
    }

    /// <summary>
    /// Adds an item to the library.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(IJellyseerrItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        _items.Add(item);
    }

    /// <summary>
    /// Adds multiple items to the library.
    /// </summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<IJellyseerrItem> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        _items.AddRange(items);
    }

    /// <summary>
    /// Gets the item hash codes for all items in this library.
    /// </summary>
    public HashSet<int> GetItemHashCodes()
    {
        var hashes = new HashSet<int>();
        foreach (var item in _items)
        {
            hashes.Add(item.GetItemHashCode());
        }
        return hashes;
    }

    /// <summary>
    /// Gets a tuple of item hash codes for deduplication.
    /// </summary>
    public HashSet<(string libraryName, int itemHashCode)> ToDeduplicationSet()
    {
        var set = new HashSet<(string libraryName, int itemHashCode)>();
        foreach (var item in _items)
        {
            set.Add((LibraryName, item.GetItemHashCode()));
        }
        return set;
    }

    /// <summary>
    /// Checks if an item with the given hash code exists in this library.
    /// </summary>
    public bool ContainsItemHash(int itemHashCode)
    {
        return _items.Any(item => item.GetItemHashCode() == itemHashCode);
    }

    /// <summary>
    /// Checks if a location path matches this library.
    /// Uses case-sensitive comparison for Linux compatibility.
    /// </summary>
    public bool ContainsLocation(string path)
    {
        // Search locations to see if path is a subdirectory
        foreach (var location in Locations)
        {
            if (FolderUtils.IsPathInDirectory(location, path)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns a string representation of the library.
    /// </summary>
    public override string ToString()
    {
        return $"[{LibraryName}] {MovieCount} movies, {ShowCount} shows ({Count} total) at {Locations.Count} location(s)";
    }
}