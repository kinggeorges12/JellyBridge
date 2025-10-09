using System;
using System.Text.Json;
using Jellyfin.Plugin.JellyseerrBridge.Services;

// Test JSON deserialization
var testJson = """
{
    "page": 1,
    "totalPages": 1,
    "totalResults": 1,
    "keywords": [],
    "results": [
        {
            "id": 286801,
            "firstAirDate": "2025-10-03",
            "genreIds": [18, 80],
            "mediaType": "tv",
            "name": "Monster: The Ed Gein Story",
            "originCountry": ["US"],
            "originalLanguage": "en",
            "originalName": "Monster: The Ed Gein Story",
            "overview": "The shocking true-life tale of Ed Gein",
            "popularity": 344.2734,
            "voteAverage": 7.5,
            "voteCount": 122,
            "backdropPath": "/cm2oUAPiTE1ERoYYOzzgloQw4YZ.jpg",
            "posterPath": "/iDHzRALtZCzHVmx7uyjTTKvMAPB.jpg"
        }
    ]
}
""";

try
{
    var result = JsonSerializer.Deserialize<JellyseerrPaginatedResponse<JellyseerrTvShow>>(testJson);
    Console.WriteLine("Deserialization successful!");
    Console.WriteLine($"Page: {result.Page}, Total: {result.TotalResults}");
    Console.WriteLine($"First show: {result.Results[0].Name}");
}
catch (Exception ex)
{
    Console.WriteLine($"Deserialization failed: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
