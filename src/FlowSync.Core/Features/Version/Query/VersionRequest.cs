﻿using MediatR;
using FlowSync.Core.Common.Models;

namespace FlowSync.Core.Features.Version.Query;

public class VersionRequest : IRequest<Result<VersionResponse>>
{
    public bool? Check { get; set; } = false;
}