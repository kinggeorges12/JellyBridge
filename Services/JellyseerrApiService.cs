using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Jellyseerr API service for interacting with Jellyseerr API.
/// </summary>
public class JellyseerrApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JellyseerrApiService> _logger;
    private readonly ConfigurationService _configurationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrApiService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationService">The configuration service.</param>
    public JellyseerrApiService(HttpClient httpClient, ILogger<JellyseerrApiService> logger, ConfigurationService configurationService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configurationService = configurationService;
    }

    /// <summary>
    /// Authenticates with Jellyseerr and returns a session.
    /// </summary>
    /// <returns>True if authentication successful, false otherwise.</returns>
    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var authUrl = $"{config.JellyseerrUrl}/api/v1/auth/local";
            
            var authData = new
            {
                email = config.Email,
                password = config.Password
            };

            var json = JsonConvert.SerializeObject(authData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Attempting to authenticate with Jellyseerr");
            var response = await _httpClient.PostAsync(authUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully authenticated with Jellyseerr");
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to authenticate with Jellyseerr. Status: {StatusCode}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating with Jellyseerr");
            return false;
        }
    }

    /// <summary>
    /// Fetches all shows for a given network ID.
    /// </summary>
    /// <param name="networkId">The network ID.</param>
    /// <returns>List of shows.</returns>
    public async Task<List<JellyseerrShow>> FetchShowsAsync(int networkId)
    {
        var allShows = new List<JellyseerrShow>();
        var page = 1;
        var totalPages = 1;

        try
        {
            var config = _configurationService.GetConfiguration();
            
            while (page <= totalPages)
            {
                var url = $"{config.JellyseerrUrl}/api/v1/discover/tv?network={networkId}&page={page}";
                
                _logger.LogDebug("Fetching shows for network {NetworkId}, page {Page}", networkId, page);
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("accept", "application/json");
                request.Headers.Add("X-Api-Key", config.ApiKey);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<JellyseerrApiResponse>(json);
                    
                    if (data?.Results != null)
                    {
                        allShows.AddRange(data.Results);
                        totalPages = data.TotalPages;
                        _logger.LogDebug("Fetched {Count} shows from page {Page}", data.Results.Count, page);
                    }
                    else
                    {
                        _logger.LogWarning("No results found for network {NetworkId}, page {Page}", networkId, page);
                        break;
                    }
                }
                else
                {
                    _logger.LogError("Failed to fetch shows for network {NetworkId}, page {Page}. Status: {StatusCode}", 
                        networkId, page, response.StatusCode);
                    break;
                }

                page++;
            }

            _logger.LogInformation("Fetched {Count} total shows for network {NetworkId}", allShows.Count, networkId);
            return allShows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching shows for network {NetworkId}", networkId);
            return allShows;
        }
    }

    /// <summary>
    /// Fetches detailed information for a show.
    /// </summary>
    /// <param name="showId">The show ID.</param>
    /// <returns>Detailed show information.</returns>
    public async Task<JellyseerrShowDetails?> FetchShowDetailsAsync(int showId)
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var url = $"{config.JellyseerrUrl}/api/v1/tv/{showId}";
            
            _logger.LogDebug("Fetching details for show {ShowId}", showId);
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("X-Api-Key", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var showDetails = JsonConvert.DeserializeObject<JellyseerrShowDetails>(json);
                
                _logger.LogDebug("Successfully fetched details for show {ShowId}", showId);
                return showDetails;
            }
            else
            {
                _logger.LogError("Failed to fetch details for show {ShowId}. Status: {StatusCode}", 
                    showId, response.StatusCode);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching details for show {ShowId}", showId);
            return null;
        }
    }

    /// <summary>
    /// Requests a show download.
    /// </summary>
    /// <param name="showId">The show ID.</param>
    /// <param name="numberOfSeasons">The number of seasons to request.</param>
    /// <returns>True if request successful, false otherwise.</returns>
    public async Task<bool> RequestShowAsync(int showId, int numberOfSeasons)
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var url = $"{config.JellyseerrUrl}/api/v1/request";
            
            var seasons = Enumerable.Range(1, numberOfSeasons).ToList();
            
            var requestData = new
            {
                mediaType = "tv",
                mediaId = showId,
                tvdbId = showId,
                seasons = seasons,
                is4k = config.Request4K,
                serverId = 0,
                profileId = 0,
                rootFolder = config.RootFolder,
                languageProfileId = 0,
                userId = config.UserId
            };

            var json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Requesting show {ShowId} with {Seasons} seasons", showId, numberOfSeasons);
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("Content-Type", "application/json");
            request.Headers.Add("X-Api-Key", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully requested show {ShowId}", showId);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to request show {ShowId}. Status: {StatusCode}, Error: {Error}", 
                    showId, response.StatusCode, errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting show {ShowId}", showId);
            return false;
        }
    }
}

/// <summary>
/// Jellyseerr API response model.
/// </summary>
public class JellyseerrApiResponse
{
    [JsonProperty("results")]
    public List<JellyseerrShow>? Results { get; set; }

    [JsonProperty("totalPages")]
    public int TotalPages { get; set; }
}

/// <summary>
/// Jellyseerr show model.
/// </summary>
public class JellyseerrShow
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("overview")]
    public string? Overview { get; set; }

    [JsonProperty("firstAirDate")]
    public string? FirstAirDate { get; set; }

    [JsonProperty("posterPath")]
    public string? PosterPath { get; set; }

    [JsonProperty("backdropPath")]
    public string? BackdropPath { get; set; }
}

/// <summary>
/// Jellyseerr show details model.
/// </summary>
public class JellyseerrShowDetails
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("overview")]
    public string? Overview { get; set; }

    [JsonProperty("firstAirDate")]
    public string? FirstAirDate { get; set; }

    [JsonProperty("numberOfSeasons")]
    public int NumberOfSeasons { get; set; }

    [JsonProperty("numberOfEpisodes")]
    public int NumberOfEpisodes { get; set; }

    [JsonProperty("posterPath")]
    public string? PosterPath { get; set; }

    [JsonProperty("backdropPath")]
    public string? BackdropPath { get; set; }
}
