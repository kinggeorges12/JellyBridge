using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing metadata files and folder operations for Jellyseerr bridge items.
/// </summary>
public class MetadataService
{
    private readonly DebugLogger<MetadataService> _logger;

    public MetadataService(ILogger<MetadataService> logger)
    {
        _logger = new DebugLogger<MetadataService>(logger);
    }

    /// <summary>
    /// Read all metadata files from the bridge folder, detecting movie vs show based on NFO files.
    /// </summary>
    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadMetadataAsync()
    {
        var movies = new List<JellyseerrMovie>();
        var shows = new List<JellyseerrShow>();

        try
        {
            // Get categorized directories
            var (movieDirectories, showDirectories) = ReadMetadataInternal();

            // Parse all movie directories
            foreach (var directory in movieDirectories)
            {
                try
                {
                    var metadataFile = Path.Combine(directory, IJellyseerrItem.GetMetadataFilename());
                    var json = await File.ReadAllTextAsync(metadataFile);
                    _logger.LogTrace("Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                    
                    var movie = JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(json);
                    if (movie != null)
                    {
                        _logger.LogTrace("Successfully deserialized movie - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                            movie.MediaName, movie.Id, movie.MediaType, movie.Year);
                        movies.Add(movie);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize movie from {MetadataFile}", metadataFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading metadata file from directory: {Directory}", directory);
                }
            }

            // Parse all show directories
            foreach (var directory in showDirectories)
            {
                try
                {
                    var metadataFile = Path.Combine(directory, IJellyseerrItem.GetMetadataFilename());
                    var json = await File.ReadAllTextAsync(metadataFile);
                    _logger.LogTrace("Reading metadata from {MetadataFile}: {Json}", metadataFile, json);
                    
                    var show = JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(json);
                    if (show != null)
                    {
                        _logger.LogTrace("Successfully deserialized show - MediaName: '{MediaName}', Id: {Id}, MediaType: '{MediaType}', Year: '{Year}'", 
                            show.MediaName, show.Id, show.MediaType, show.Year);
                        shows.Add(show);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize show from {MetadataFile}", metadataFile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading metadata file from directory: {Directory}", directory);
                }
            }

            _logger.LogDebug("Read {MovieCount} movies and {ShowCount} shows from bridge folders", 
                movies.Count, shows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading metadata from bridge folders");
        }

        return (movies, shows);
    }

    /// <summary>
    /// Internal method to discover and categorize directories containing metadata files.
    /// </summary>
    /// <returns>Tuple containing lists of movie directories and show directories</returns>
    private (List<string> movieDirectories, List<string> showDirectories) ReadMetadataInternal()
    {
        var movieDirectories = new List<string>();
        var showDirectories = new List<string>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

            // Get all subdirectories that contain metadata files
            var metadataFiles = Directory.GetFiles(syncDirectory, IJellyseerrItem.GetMetadataFilename(), SearchOption.AllDirectories);
            
            foreach (var metadataFile in metadataFiles)
            {
                var directory = Path.GetDirectoryName(metadataFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    // Check for movie.nfo to identify movie folders
                    var movieNfoFile = Path.Combine(directory, JellyseerrMovie.GetNfoFilename());
                    var showNfoFile = Path.Combine(directory, JellyseerrShow.GetNfoFilename());
                    
                    if (File.Exists(movieNfoFile))
                    {
                        movieDirectories.Add(directory);
                    }
                    else if (File.Exists(showNfoFile))
                    {
                        showDirectories.Add(directory);
                    }
                    else
                    {
                        _logger.LogWarning("No NFO file found in directory {Directory} - skipping", directory);
                    }
                }
            }

            _logger.LogDebug("Found {MovieCount} movie directories and {ShowCount} show directories", 
                movieDirectories.Count, showDirectories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering metadata directories from {SyncDirectory}", syncDirectory);
        }

        return (movieDirectories, showDirectories);
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<(List<TJellyseerr> added, List<TJellyseerr> updated)> CreateFolderMetadataAsync<TJellyseerr>(List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var addedItems = new List<TJellyseerr>();
        var updatedItems = new List<TJellyseerr>();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogDebug("Starting folder creation for {ItemType} - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            typeof(TJellyseerr).Name, baseDirectory, items.Count);
        
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                _logger.LogTrace("Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    i + 1, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = GetJellyseerrItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogTrace("Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata using folder manager
                var success = await WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
                    {
                        updatedItems.Add(item);
                        _logger.LogTrace("✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
                        addedItems.Add(item);
                        _logger.LogTrace("✅ CREATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                }
                else
                {
                    _logger.LogError("❌ FAILED to create folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                        item, item.MediaName, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR creating folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        _logger.LogDebug("Completed folder creation for {ItemType} - Added: {Added}, Updated: {Updated}", 
            typeof(TJellyseerr).Name, addedItems.Count, updatedItems.Count);
        
        return (addedItems, updatedItems);
    }

    /// <summary>
    /// Write metadata for a single item to the appropriate folder.
    /// </summary>
    private async Task<bool> WriteMetadataAsync<TJellyseerr>(TJellyseerr item) where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        try
        {
            var targetDirectory = GetJellyseerrItemDirectory(item);

            // Create directory if it doesn't exist
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogDebug("Created directory: {TargetDirectory}", targetDirectory);
            }

            // Set CreatedDate to current time when writing
            item.CreatedDate = DateTimeOffset.Now;
            
            // Write JSON metadata - serialize as concrete type to preserve JSON attributes
            var json = JellyBridgeJsonSerializer.Serialize(item);
            
            var metadataFile = Path.Combine(targetDirectory, IJellyseerrItem.GetMetadataFilename());
            await File.WriteAllTextAsync(metadataFile, json);
            _logger.LogTrace("Wrote metadata to {MetadataFile}", metadataFile);
            
            // Write XML metadata only if NFO file doesn't exist
            var xmlFile = Path.Combine(targetDirectory, IJellyseerrItem.GetNfoFilename(item));
            if (!File.Exists(xmlFile))
            {
                var xmlText = item.ToXmlString();
                await File.WriteAllTextAsync(xmlFile, xmlText);
                _logger.LogTrace("Wrote XML to {XmlFile}", xmlFile);
            }
            else
            {
                _logger.LogTrace("Skipped writing XML to {XmlFile} - file already exists", xmlFile);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing metadata for {ItemMediaName}", item.MediaName);
            return false;
        }
    }


    #region Helpers

    /// <summary>
    /// Get the directory path for a specific item.
    /// </summary>
    public string GetJellyseerrItemDirectory(IJellyseerrItem? item = null)
    {
        if (item == null)
        {
            return FolderUtils.GetBaseDirectory();
        }
        var itemString = item.ToString();
        if (string.IsNullOrEmpty(itemString))
        {
            throw new ArgumentException($"Item {item.GetType().Name} returned null or empty string from ToString()", nameof(item));
        }
        var itemFolder = FolderUtils.SanitizeFileName(itemString);
        if(Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.CreateSeparateLibraries)) && !string.IsNullOrEmpty(item.NetworkTag))
        {
            var networkPrefix = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryPrefix));
            var networkFolder = FolderUtils.SanitizeFileName(networkPrefix + item.NetworkTag);
            return Path.Combine(FolderUtils.GetBaseDirectory(), networkFolder, itemFolder);
        }
        // If not using network prefix, just store in the base directory with the folder name
        return Path.Combine(FolderUtils.GetBaseDirectory(), itemFolder);
    }

    /// <summary>
    /// Updates the dateadded field in all NFO files with unique dates distributed over the last X days (where X = number of items).
    /// Dates start from yesterday and go backwards, with time set to 00:00:00.
    /// Uses ReadMetadataInternal to discover movie and show directories.
    /// </summary>
    /// <returns>A tuple containing a list of successful updates (name, type, dateAdded) and a list of failed file paths.</returns>
    public async Task<(List<(string name, string type, DateTimeOffset dateAdded)> successes, List<string> failures)> RandomizeNfoDateAddedAsync()
    {
        var successes = new List<(string name, string type, DateTimeOffset dateAdded)>();
        var failures = new List<string>();
        
        try
        {
            // Get categorized directories
            var (movieDirectories, showDirectories) = ReadMetadataInternal();

            // Combine all directories with their appropriate NFO filenames into a single list
            var xmlFiles = new List<string>();
            xmlFiles.AddRange(movieDirectories.Select(d => Path.Combine(d, JellyseerrMovie.GetNfoFilename())));
            xmlFiles.AddRange(showDirectories.Select(d => Path.Combine(d, JellyseerrShow.GetNfoFilename())));

            // Only process files that actually exist
            xmlFiles = xmlFiles.Where(f => File.Exists(f)).ToList();

            if (xmlFiles.Count == 0)
            {
                _logger.LogDebug("No NFO files found to update");
                return (successes, failures);
            }

            // Shuffle the files randomly to get random sort order
            var random = System.Random.Shared;
            xmlFiles = xmlFiles.OrderBy(_ => random.Next()).ToList();

            // Assign unique dates starting from yesterday going backwards
            // Each item gets a unique date with time set to 00:00:00
            var dateMap = new Dictionary<string, DateTimeOffset>();
            var yesterday = DateTimeOffset.Now.AddDays(-1).Date;
            
            for (int i = 0; i < xmlFiles.Count; i++)
            {
                // Start from yesterday (i=0) and go backwards
                var assignedDate = yesterday.AddDays(-i);
                // Set time to midnight (00:00:00)
                var dateWithTime = new DateTimeOffset(assignedDate, TimeSpan.Zero);
                dateMap[xmlFiles[i]] = dateWithTime;
            }

            // Process all XML files with their assigned dates
            foreach (var xmlFile in xmlFiles)
            {
                try
                {
                    var assignedDate = dateMap[xmlFile];
                    var result = await UpdateDateAddedInNfoFile(xmlFile, assignedDate);
                    successes.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update dateadded in NFO file: {XmlFile}", xmlFile);
                    failures.Add(xmlFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating dateadded in NFO files");
        }
        
        return (successes, failures);
    }

    /// <summary>
    /// Helper method to update the dateadded field in a single NFO file.
    /// </summary>
    /// <param name="xmlFile">Path to the NFO file to update</param>
    /// <param name="dateAdded">The DateTimeOffset to set (should have time set to 00:00:00)</param>
    /// <returns>A tuple containing (name, type, dateAdded)</returns>
    /// <exception cref="InvalidOperationException">Thrown if the NFO file cannot be parsed (root element is null)</exception>
    private async Task<(string name, string type, DateTimeOffset dateAdded)> UpdateDateAddedInNfoFile(string xmlFile, DateTimeOffset dateAdded)
    {
        var xmlContent = await File.ReadAllTextAsync(xmlFile);
        var xmlDoc = XDocument.Parse(xmlContent);
        var root = xmlDoc.Root;
        
        if (root == null)
        {
            throw new InvalidOperationException($"Failed to parse NFO file: {xmlFile} - root element is null");
        }
        
        // Extract type from root element name and title from XML
        string type = root.Name.LocalName;
        var titleElement = root.Element("title");
        string name = titleElement?.Value ?? Path.GetFileName(Path.GetDirectoryName(xmlFile)) ?? $"Unknown {type}";
        
        // Format date with time set to 00:00:00
        var dateAddedString = dateAdded.ToString("yyyy-MM-dd HH:mm:ss");
        
        // Remove existing dateadded if present
        var existingDateAdded = root.Element("dateadded");
        if (existingDateAdded != null)
        {
            existingDateAdded.Remove();
        }
        
        // Add new dateadded element (insert after the first element for better formatting)
        var firstElement = root.Elements().FirstOrDefault();
        var newDateAdded = new XElement("dateadded", dateAddedString);
        if (firstElement != null)
        {
            firstElement.AddAfterSelf(newDateAdded);
        }
        else
        {
            root.Add(newDateAdded);
        }
        
        // Write the updated XML back
        await File.WriteAllTextAsync(xmlFile, xmlDoc.ToString());
        _logger.LogTrace("Updated dateadded in {XmlFile} to {DateAdded}", xmlFile, dateAddedString);
        
        return (name, type, dateAdded);
    }

    #endregion
}