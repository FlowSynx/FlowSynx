﻿namespace FlowSynx.Plugin.Stream.Csv;

public class CreateOptions
{
    public string Delimiter { get; set; } = ",";
    public string Headers { get; set; } = string.Empty;
    public bool? Overwrite { get; set; } = false;
}