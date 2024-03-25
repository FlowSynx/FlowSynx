﻿using EnsureThat;
using Microsoft.Extensions.Logging;
using FlowSynx.Commons;
using FlowSynx.Core.Exceptions;
using FlowSynx.Plugin.Abstractions;

namespace FlowSynx.Core.Parers.Namespace;

internal class NamespaceParser : INamespaceParser
{
    private readonly ILogger<NamespaceParser> _logger;

    public NamespaceParser(ILogger<NamespaceParser> logger)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        _logger = logger;
    }

    public PluginNamespace Parse(string type)
    {
        try
        {
            var terms = type.Split("/");
            if (terms.Length != 2)
            {
                _logger.LogError($"The given type {type} is not valid!");
                throw new NamespaceParserException(string.Format(Resources.NamespaceParserInvalidType, type));
            }

            var firstTerm = terms[0].Split(".");
            if (firstTerm.Length != 2)
            {
                _logger.LogError($"The given type {terms[0]} is not valid!");
                throw new NamespaceParserException(string.Format(Resources.NamespaceParserInvalidType, terms[0]));
            }

            if (!string.Equals(firstTerm[0], "flowsynx", StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogError($"The given type {terms[0]} is not valid!");
                throw new NamespaceParserException(string.Format(Resources.NamespaceParserInvalidType, terms[0]));
            }

            return EnumUtils.GetEnumValueOrDefault<PluginNamespace>(firstTerm[1])!.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            throw new NamespaceParserException(ex.Message);
        }
    }

    public void Dispose() { }
}