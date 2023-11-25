﻿namespace FlowSync.Core.Features.Config.Query.Details;

public class ConfigDetailsResponse
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public object? Specifications { get; set; }
}