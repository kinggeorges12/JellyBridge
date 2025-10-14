using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class IssueResultsResponse : PaginatedResponse
{
    [JsonPropertyName("results")]
    public List<Issue> Results { get; set; } = new();

}

