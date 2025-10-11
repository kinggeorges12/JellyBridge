using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service that handles Jellyseerr Bridge configuration and data management.
/// This service acts as a bridge between the plugin configuration and the external Jellyseerr API.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly JellyseerrApiService _apiService;
    private readonly ILibraryManager _libraryManager;

    public JellyseerrBridgeService(
        ILogger<JellyseerrBridgeService> logger,
        JellyseerrApiService apiService,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _apiService = apiService;
        _libraryManager = libraryManager;
    }


    /// <summary>
    /// Gets existing items from Jellyfin libraries, excluding those in the sync directory.
    /// </summary>
    /// <typeparam name="T">The type of item to retrieve (Movie or Series).</typeparam>
    /// <returns>List of existing items from main libraries.</returns>
    public Task<List<T>> GetExistingItemsAsync<T>() where T : BaseItem
    {
        var existingItems = new List<T>();
        
        try
        {
            var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            var baseItemKind = typeof(T) == typeof(Movie) ? BaseItemKind.Movie : BaseItemKind.Series;
            var itemTypeName = typeof(T) == typeof(Movie) ? "movie" : "TV show";
            
            _logger.LogInformation("[JellyseerrBridge] Starting Jellyfin {ItemType} library scan...", itemTypeName);
            _logger.LogInformation("[JellyseerrBridge] Sync directory: {SyncDirectory}", syncDirectory);

            // Get all items from Jellyfin libraries
            var allItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { baseItemKind },
                Recursive = true
            });

            _logger.LogInformation("[JellyseerrBridge] Found {TotalItemCount} total {ItemType}s in Jellyfin libraries", allItems.Count, itemTypeName);

            int excludedCount = 0;
            int processedCount = 0;

            foreach (var item in allItems)
            {
                if (item is T typedItem)
                {
                    // Skip items that are in the sync directory
                    if (!string.IsNullOrEmpty(typedItem.Path) && 
                        !string.IsNullOrEmpty(syncDirectory) && 
                        typedItem.Path.StartsWith(syncDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        excludedCount++;
                        continue;
                    }

                    existingItems.Add(typedItem);
                    processedCount++;
                }
            }

            _logger.LogInformation("[JellyseerrBridge] Excluded {ExcludedCount} {ItemType}s from sync directory", excludedCount, itemTypeName);
            _logger.LogInformation("[JellyseerrBridge] Processed {ProcessedCount} {ItemType}s from main libraries", processedCount, itemTypeName);
        }
        catch (Exception ex)
        {
            var itemTypeName = typeof(T) == typeof(Movie) ? "movie" : "TV show";
            _logger.LogError(ex, "[JellyseerrBridge] Error scanning Jellyfin {ItemType} libraries", itemTypeName);
        }

        return Task.FromResult(existingItems);
    }

    /// <summary>
    /// Filters movies to exclude those that already exist in main libraries.
    /// </summary>
    /// <param name="movies">List of movies to filter.</param>
    /// <returns>Filtered list of movies.</returns>
    public async Task<List<JellyseerrMovie>> FilterMoviesAsync(List<JellyseerrMovie> movies)
    {
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;
        
        if (!excludeFromMainLibraries)
        {
            _logger.LogInformation("[JellyseerrBridge] ExcludeFromMainLibraries is disabled - returning all {MovieCount} movies", movies.Count);
            return movies;
        }

        var existingMovies = await GetExistingItemsAsync<Movie>();
        _logger.LogInformation("[JellyseerrBridge] Found {ExistingMovieCount} existing movies in main libraries", existingMovies.Count);

        var filteredMovies = new List<JellyseerrMovie>();
        int skippedCount = 0;

        foreach (var movie in movies)
        {
            if (existingMovies.Any(existing => movie.Equals(existing)))
            {
                _logger.LogInformation("[JellyseerrBridge] Skipping movie {MovieTitle} - already exists in main libraries", movie.Title);
                skippedCount++;
                continue;
            }

            filteredMovies.Add(movie);
        }

        _logger.LogInformation("[JellyseerrBridge] Filtered {OriginalCount} movies to {FilteredCount} (skipped {SkippedCount})", 
            movies.Count, filteredMovies.Count, skippedCount);

        return filteredMovies;
    }

    /// <summary>
    /// Filters TV shows to exclude those that already exist in main libraries.
    /// </summary>
    /// <param name="shows">List of TV shows to filter.</param>
    /// <returns>Filtered list of TV shows.</returns>
    public async Task<List<JellyseerrShow>> FilterShowsAsync(List<JellyseerrShow> shows)
    {
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;
        
        if (!excludeFromMainLibraries)
        {
            _logger.LogInformation("[JellyseerrBridge] ExcludeFromMainLibraries is disabled - returning all {ShowCount} TV shows", shows.Count);
            return shows;
        }

        var existingShows = await GetExistingItemsAsync<Series>();
        _logger.LogInformation("[JellyseerrBridge] Found {ExistingShowCount} existing TV shows in main libraries", existingShows.Count);

        var filteredShows = new List<JellyseerrShow>();
        int skippedCount = 0;

        foreach (var show in shows)
        {
            if (existingShows.Any(existing => show.Equals(existing)))
            {
                _logger.LogInformation("[JellyseerrBridge] Skipping TV show {ShowName} - already exists in main libraries", show.Name);
                skippedCount++;
                continue;
            }

            filteredShows.Add(show);
        }

        _logger.LogInformation("[JellyseerrBridge] Filtered {OriginalCount} TV shows to {FilteredCount} (skipped {SkippedCount})", 
            shows.Count, filteredShows.Count, skippedCount);

        return filteredShows;
    }

    /// <summary>
    /// Read metadata.json files from bridge folders.
    /// </summary>
    public async Task<List<T>> ReadBridgeFolderMetadataAsync<T>() where T : JellyseerrItem
    {
        var metadata = new List<T>();
        
        try
        {
            var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                _logger.LogDebug("Bridge folder directory not configured or does not exist: {Directory}", baseDirectory);
                return metadata;
            }

            var directories = Directory.GetDirectories(baseDirectory);
            _logger.LogDebug("Found {Count} directories in {Directory}", directories.Length, baseDirectory);

            foreach (var directory in directories)
            {
                try
                {
                    var metadataFile = Path.Combine(directory, "metadata.json");
                    if (File.Exists(metadataFile))
                    {
                        var jsonContent = await File.ReadAllTextAsync(metadataFile);
                        var item = JsonSerializer.Deserialize<T>(jsonContent);
                        if (item != null)
                        {
                            metadata.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read metadata from {Directory}", directory);
                }
            }

            _logger.LogDebug("Successfully read {Count} metadata files", metadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading bridge folder metadata");
        }

        return metadata;
    }

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge folder metadata using the built-in comparators.
    /// </summary>
    public async Task<List<object>> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : JellyseerrItem, IEquatable<BaseItem>
    {
        var matches = new List<object>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        foreach (var existingItem in existingItems)
        {
            // Use the built-in IEquatable<TJellyfin> implementation
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => bm.Equals(existingItem));

            if (bridgeMatch != null)
            {
                // Find the bridge folder directory for this item
                var bridgeFolderPath = await FindBridgeFolderPathAsync(syncDirectory, bridgeMatch);
                
                // Create .ignore file with Jellyfin item metadata
                if (!string.IsNullOrEmpty(bridgeFolderPath))
                {
                    await CreateIgnoreFileAsync(bridgeFolderPath, existingItem);
                }

                // Create match result object
                var matchResult = new
                {
                    JellyfinItem = new
                    {
                        Name = existingItem.Name,
                        TmdbId = existingItem.GetProviderId("Tmdb"),
                        ImdbId = existingItem.GetProviderId("Imdb"),
                        TvdbId = existingItem.GetProviderId("Tvdb"),
                        Year = existingItem.PremiereDate?.Year,
                        Path = existingItem.Path
                    },
                    BridgeMetadata = new
                    {
                        Type = bridgeMatch.Type,
                        Name = bridgeMatch.Type == "Movie" ? 
                            ((JellyseerrMovie)(object)bridgeMatch).Title : 
                            ((JellyseerrShow)(object)bridgeMatch).Name,
                        TmdbId = bridgeMatch.Type == "Movie" ? 
                            ((JellyseerrMovie)(object)bridgeMatch).Id : 
                            ((JellyseerrShow)(object)bridgeMatch).Id,
                        ImdbId = bridgeMatch.Type == "Movie" ? 
                            ((JellyseerrMovie)(object)bridgeMatch).ImdbId : 
                            ((JellyseerrShow)(object)bridgeMatch).ExternalIds?.ImdbId,
                        TvdbId = bridgeMatch.Type == "Show" ? 
                            ((JellyseerrShow)(object)bridgeMatch).MediaInfo?.TvdbId : null,
                        ReleaseDate = bridgeMatch.Type == "Movie" ? 
                            ((JellyseerrMovie)(object)bridgeMatch).ReleaseDate : 
                            ((JellyseerrShow)(object)bridgeMatch).FirstAirDate
                    },
                    BridgeFolderPath = bridgeFolderPath,
                    MatchType = "Comparator"
                };

                matches.Add(matchResult);
            }
        }

        return matches;
    }

    /// <summary>
    /// Find the bridge folder path for a given Jellyseerr item.
    /// </summary>
    public async Task<string?> FindBridgeFolderPathAsync(string baseDirectory, object item)
    {
        try
        {
            if (!Directory.Exists(baseDirectory))
            {
                return null;
            }

            var directories = Directory.GetDirectories(baseDirectory);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    var jsonContent = await File.ReadAllTextAsync(metadataFile);
                    
                    // Check if this directory contains the matching item
                    if (item is JellyseerrMovie movie)
                    {
                        var dirMovie = JsonSerializer.Deserialize<JellyseerrMovie>(jsonContent);
                        if (dirMovie != null && dirMovie.Id == movie.Id)
                        {
                            return directory;
                        }
                    }
                    else if (item is JellyseerrShow show)
                    {
                        var dirShow = JsonSerializer.Deserialize<JellyseerrShow>(jsonContent);
                        if (dirShow != null && dirShow.Id == show.Id)
                        {
                            return directory;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding bridge folder path for {ItemType}", item.GetType().Name);
        }

        return null;
    }

    /// <summary>
    /// Create a .ignore file with Jellyfin item metadata.
    /// </summary>
    public async Task CreateIgnoreFileAsync(string bridgeFolderPath, BaseItem jellyfinItem)
    {
        try
        {
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            
            var json = JsonSerializer.Serialize(jellyfinItem, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ignoreFilePath, json);
            
            _logger.LogDebug("Created .ignore file for {ItemType} in {Path}", jellyfinItem.GetType().Name, bridgeFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating .ignore file in {Path}", bridgeFolderPath);
        }
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// </summary>
    public async Task<object> TestLibraryScanAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;

        try
        {
            _logger.LogInformation("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            if (!excludeFromMainLibraries)
            {
                return new
                {
                    SyncDirectory = syncDirectory,
                    ExcludeFromMainLibraries = excludeFromMainLibraries,
                    Message = "ExcludeFromMainLibraries is disabled - no library scan performed",
                    Error = (string?)null
                };
            }

            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                return new
                {
                    SyncDirectory = syncDirectory,
                    ExcludeFromMainLibraries = excludeFromMainLibraries,
                    Message = "Sync directory not configured or does not exist",
                    Error = "Sync directory not found"
                };
            }

            // Get existing Jellyfin items
            var existingMovies = await GetExistingItemsAsync<Movie>();
            var existingShows = await GetExistingItemsAsync<Series>();

            // Read bridge folder metadata
            var bridgeMovieMetadata = await ReadBridgeFolderMetadataAsync<JellyseerrMovie>();
            var bridgeShowMetadata = await ReadBridgeFolderMetadataAsync<JellyseerrShow>();

            // Compare and find matches
            var movieMatches = await FindMatches(existingMovies, bridgeMovieMetadata);
            var showMatches = await FindMatches(existingShows, bridgeShowMetadata);

            return new
            {
                SyncDirectory = syncDirectory,
                ExcludeFromMainLibraries = excludeFromMainLibraries,
                ExistingMovies = new
                {
                    Count = existingMovies.Count,
                    Items = existingMovies.Select(m => new
                    {
                        Name = m.Name,
                        TmdbId = m.GetProviderId("Tmdb"),
                        ImdbId = m.GetProviderId("Imdb"),
                        Year = m.PremiereDate?.Year
                    }).ToList()
                },
                ExistingShows = new
                {
                    Count = existingShows.Count,
                    Items = existingShows.Select(s => new
                    {
                        Name = s.Name,
                        TmdbId = s.GetProviderId("Tmdb"),
                        TvdbId = s.GetProviderId("Tvdb"),
                        ImdbId = s.GetProviderId("Imdb"),
                        Year = s.PremiereDate?.Year
                    }).ToList()
                },
                BridgeFolderMetadata = new
                {
                    Movies = new
                    {
                        Count = bridgeMovieMetadata.Count,
                        Items = bridgeMovieMetadata.Select(m => new
                        {
                            Title = m.Title,
                            TmdbId = m.Id,
                            ImdbId = m.ImdbId,
                            ReleaseDate = m.ReleaseDate
                        }).ToList()
                    },
                    Shows = new
                    {
                        Count = bridgeShowMetadata.Count,
                        Items = bridgeShowMetadata.Select(s => new
                        {
                            Name = s.Name,
                            TmdbId = s.Id,
                            TvdbId = s.MediaInfo?.TvdbId,
                            ImdbId = s.ExternalIds?.ImdbId,
                            FirstAirDate = s.FirstAirDate
                        }).ToList()
                    }
                },
                Matches = new
                {
                    Movies = movieMatches,
                    Shows = showMatches
                },
                Error = (string?)null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return new
            {
                SyncDirectory = syncDirectory,
                ExcludeFromMainLibraries = excludeFromMainLibraries,
                Error = ex.Message
            };
        }
    }
}
