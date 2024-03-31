﻿using FlowSynx.Plugin.Abstractions;
using FlowSynx.Reflections;
using System.Text.Json;

namespace FlowSynx.Core.Parers.Specifications;

public class SpecificationsParser : ISpecificationsParser
{
    private readonly IPluginsManager _pluginsManager;

    public SpecificationsParser(IPluginsManager pluginsManager)
    {
        _pluginsManager = pluginsManager;
    }

    public SpecificationsResult Parse(string type, Dictionary<string, object?>? specifications)
    {
        IPlugin plugin = _pluginsManager.GetPlugin(type);
        var specificationsType = plugin.SpecificationsType;
        var requiredProperties = specificationsType
            .Properties()
            .Where(prop => Attribute.IsDefined(prop, typeof(RequiredMemberAttribute)))
            .ToList();

        if (!requiredProperties.Any())
            return new SpecificationsResult { Valid = true };

        var convertedSpecifications = ConvertKeysToLowerCase(specifications);

        foreach (var property in requiredProperties)
        {
            if (convertedSpecifications == null || !convertedSpecifications.Any() || !ContainsKey(convertedSpecifications, property.Name))
            {
                var specs = string.Join(", ", requiredProperties.Select(p => p.Name));
                return new SpecificationsResult
                {
                    Valid = false,
                    Message = string.Format(Resources.SpecificationsMustHaveValue, specs)
                };
            }

            var value = convertedSpecifications[property.Name.ToLower()];
            switch (((JsonElement?)value)?.ValueKind)
            {
                case null:
                case JsonValueKind.Null:
                case JsonValueKind.String when string.IsNullOrEmpty(value.ToString()):
                    return new SpecificationsResult
                    {
                        Valid = false,
                        Message = string.Format(Resources.SpecificationsRequiredMemberMustHaveValue, property.Name)
                    };
            }
        }

        return new SpecificationsResult { Valid = true };
    }

    private Dictionary<string, object?>? ConvertKeysToLowerCase(Dictionary<string, object?>? dictionaries)
    {
        var convertedDictionary = new Dictionary<string, object?>();

        if (dictionaries == null)
            return convertedDictionary;

        foreach (string key in dictionaries.Keys)
        {
            convertedDictionary.Add(key.ToLower(), dictionaries[key]);
        }
        return convertedDictionary;
    }

    private bool ContainsKey(Dictionary<string, object?> dictionary, string value)
    {
        return dictionary.Keys.Any(k => string.Equals(k, value, StringComparison.CurrentCultureIgnoreCase));
    }
}