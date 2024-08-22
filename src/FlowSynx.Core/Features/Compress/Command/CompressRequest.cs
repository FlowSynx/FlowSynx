﻿using MediatR;
using FlowSynx.Abstractions;
using FlowSynx.Plugin.Storage;
using FlowSynx.Core.Services;
using FlowSynx.Plugin.Storage.Abstractions;

namespace FlowSynx.Core.Features.Compress.Command;

public class CompressRequest : IRequest<Result<CompressResponse>>
{
    public required string Path { get; set; }
    //public string? Kind { get; set; } = StorageFilterItemKind.FileAndDirectory.ToString();
    public string? Include { get; set; }
    public string? Exclude { get; set; }
    public string? MinAge { get; set; }
    public string? MaxAge { get; set; }
    public string? MinSize { get; set; }
    public string? MaxSize { get; set; }
    public bool? CaseSensitive { get; set; } = false;
    public bool? Recurse { get; set; } = false;
    public string? MaxResults { get; set; }
    public bool? Hashing { get; set; } = false;
    public string? CompressType { get; set; } = IO.Compression.CompressType.Zip.ToString();
}