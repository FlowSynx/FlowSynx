﻿namespace FlowSynx.Plugin.Stream.Csv;

public class ListOptions
{
    public string Delimiter { get; set; } = ",";
    public string? Fields { get; set; }
    public string? Filter { get; set; }
    public bool CaseSensitive { get; set; } = false;
    public string? Sort { get; set; }
    public string? Limit { get; set; }
    public bool? IncludeMetadata { get; set; } = false;

}