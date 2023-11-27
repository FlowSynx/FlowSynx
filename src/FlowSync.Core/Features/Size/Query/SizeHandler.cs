﻿using MediatR;
using FlowSync.Abstractions;
using Microsoft.Extensions.Logging;
using FlowSync.Abstractions.Entities;
using FlowSync.Core.FileSystem;
using FlowSync.Core.FileSystem.Filter;
using FlowSync.Abstractions.Helpers;
using FlowSync.Core.Common.Models;
using FlowSync.Core.Common.Utilities;
using EnsureThat;
using FlowSync.Abstractions.Models;

namespace FlowSync.Core.Features.Size.Query;

internal class SizeHandler : IRequestHandler<SizeRequest, Result<SizeResponse>>
{
    private readonly ILogger<SizeHandler> _logger;
    private readonly IFileSystemService _fileSystem;

    public SizeHandler(ILogger<SizeHandler> logger, IFileSystemService fileSystem)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(fileSystem, nameof(fileSystem));
        _logger = logger;
        _fileSystem = fileSystem;
    }

    public async Task<Result<SizeResponse>> Handle(SizeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var filters = new FileSystemFilterOptions()
            {
                Kind = string.IsNullOrEmpty(request.Kind) ? FilterItemKind.FileAndDirectory : EnumUtils.GetEnumValueOrDefault<FilterItemKind>(request.Kind)!.Value,
                Include = request.Include,
                Exclude = request.Exclude,
                MinimumAge = request.MinAge,
                MaximumAge = request.MaxAge,
                MinimumSize = request.MinSize,
                MaximumSize = request.MaxSize,
                Sorting = request.Sorting,
                CaseSensitive = request.CaseSensitive ?? false,
                Recurse = request.Recurse ?? false,
                MaxResults = request.MaxResults ?? 10
            };

            var entities = await _fileSystem.List(request.Path, filters, cancellationToken);

            var response = new SizeResponse()
            {
                Size = ByteSizeHelper.FormatByteSize(entities.Sum(x => x.Size), request.FormatSize),
            };

            return await Result<SizeResponse>.SuccessAsync(response);
        }
        catch (Exception ex)
        {
            return await Result<SizeResponse>.FailAsync(new List<string> { ex.Message });
        }
    }
}