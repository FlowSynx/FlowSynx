﻿using MediatR;
using Microsoft.Extensions.Logging;
using EnsureThat;
using FlowSynx.Abstractions;
using FlowSynx.Plugin.Abstractions;
using FlowSynx.Reflections;
using System.Security.Principal;
using FlowSynx.Core.Extensions;

namespace FlowSynx.Core.Features.Plugins.Query.Details;

internal class PluginDetailsHandler : IRequestHandler<PluginDetailsRequest, Result<PluginDetailsResponse>>
{
    private readonly ILogger<PluginDetailsHandler> _logger;
    private readonly IPluginsManager _pluginsManager;

    public PluginDetailsHandler(ILogger<PluginDetailsHandler> logger, IPluginsManager pluginsManager)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(pluginsManager, nameof(pluginsManager));
        _logger = logger;
        _pluginsManager = pluginsManager;
    }

    public async Task<Result<PluginDetailsResponse>> Handle(PluginDetailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var plugin = _pluginsManager.Plugins().FirstOrDefault(x=>x.Id.Equals(request.Id));

            if (plugin == null)
            {
                _logger.LogError("The desired plugin are not found!");
                return await Result<PluginDetailsResponse>.FailAsync("The desired plugin are not found!");
            }

            var specificationsType = plugin.SpecificationsType;
            var properties = specificationsType.Properties().ToList();
            var specifications = properties
                .Select(property => new PluginDetailsSpecification
                {
                    Key = property.Name, 
                    Type = property.PropertyType.GetPrimitiveType(), 
                    IsRequired = Attribute.IsDefined(property, typeof(RequiredMemberAttribute))
                }).ToList();

            var response = new PluginDetailsResponse
            {
                Id = plugin.Id,
                Type = plugin.Type,
                Description = plugin.Description,
                Specifications = specifications
            };

            return await Result<PluginDetailsResponse>.SuccessAsync(response);
        }
        catch (Exception ex)
        {
            return await Result<PluginDetailsResponse>.FailAsync(new List<string> { ex.Message });
        }
    }
}