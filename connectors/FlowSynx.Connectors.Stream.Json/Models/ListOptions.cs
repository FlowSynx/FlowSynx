﻿namespace FlowSynx.Connectors.Stream.Json.Models;

public class ListOptions
{
    public string? Fields { get; set; }
    public string? Filter { get; set; }
    public bool CaseSensitive { get; set; } = false;
    public string? Sort { get; set; }
    public string? Paging { get; set; }
    public bool? IncludeMetadata { get; set; } = false;

}