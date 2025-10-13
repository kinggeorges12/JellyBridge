using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class PageInfo
{
    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("results")]
    public int Results { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

}

public class PaginatedResponse
{
    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();

}

public class NonFunctionPropertyNames<T>
{
    public T Value { get; set; } = default(T)!;
}


public class NonFunctionProperties<T>
{
    public T Value { get; set; } = default(T)!;
}


