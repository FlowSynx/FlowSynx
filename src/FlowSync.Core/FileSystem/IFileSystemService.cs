﻿using FlowSync.Abstractions;
using FlowSync.Abstractions.Entities;
using FlowSync.Core.Common.Models;

namespace FlowSync.Core.FileSystem;

internal interface IFileSystemService
{
    Task<Usage> About(string path, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileSystemEntity>> List(string path, FileSystemFilterOptions fileSystemFilters, CancellationToken cancellationToken = default);
    Task<FileStream> ReadAsync(string path, CancellationToken cancellationToken = default);
}