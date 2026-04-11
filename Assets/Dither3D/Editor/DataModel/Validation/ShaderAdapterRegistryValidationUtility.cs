using System;
using System.Collections.Generic;

/// <summary>
/// Provides editor-time validation for shader adapter registry assets.
/// </summary>
public static class ShaderAdapterRegistryValidationUtility
{
    const string SupportedLabel = "supported";
    const string UnsupportedLabel = "unsupported";

    /// <summary>
    /// Validates a shader adapter registry and returns user-fixable messages.
    /// </summary>
    public static List<string> Validate(ShaderAdapterRegistry registry)
    {
        var messages = new List<string>();
        if (registry == null)
        {
            messages.Add("ShaderAdapterRegistry reference is null.");
            return messages;
        }

        var sourceShaderToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var mappings = registry.ShaderMappings;

        for (int i = 0; i < mappings.Count; i++)
        {
            ShaderAdapterMapping mapping = mappings[i];
            if (mapping == null)
            {
                messages.Add($"Mapping entry #{i} is null.");
                continue;
            }

            string sourceShaderName = (mapping.SourceShaderName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(sourceShaderName))
            {
                messages.Add($"Mapping entry #{i} has an empty source shader name.");
            }
            else
            {
                if (sourceShaderToIndex.TryGetValue(sourceShaderName, out int firstIndex))
                {
                    messages.Add(
                        $"Mapping entry #{i} duplicates source shader '{sourceShaderName}', already used by entry #{firstIndex}.");
                }
                else
                {
                    sourceShaderToIndex[sourceShaderName] = i;
                }
            }

            if (mapping.TargetShader == null)
            {
                string sourceLabel = string.IsNullOrEmpty(sourceShaderName) ? "<empty>" : sourceShaderName;
                messages.Add($"Mapping entry #{i} (source '{sourceLabel}') is missing a target shader.");
            }

            ValidatePropertyRules(mapping, i, messages);
        }

        return messages;
    }

    /// <summary>
    /// Validates a style profile and nested shader adapter registry.
    /// </summary>
    public static List<string> Validate(DitherStyleProfile profile)
    {
        var messages = new List<string>();
        if (profile == null)
        {
            messages.Add("DitherStyleProfile reference is null.");
            return messages;
        }

        if (profile.ShaderAdapterRegistry == null)
        {
            messages.Add($"DitherStyleProfile '{profile.name}' is missing its Shader Adapter Registry reference.");
            return messages;
        }

        return Validate(profile.ShaderAdapterRegistry);
    }

    static void ValidatePropertyRules(ShaderAdapterMapping mapping, int mappingIndex, List<string> messages)
    {
        var sourcePropertiesUsedInRules = new HashSet<string>(StringComparer.Ordinal);
        var propertyRules = mapping.PropertyRemapRules;
        for (int i = 0; i < propertyRules.Count; i++)
        {
            PropertyRemapRule rule = propertyRules[i];
            if (rule == null)
            {
                messages.Add($"Mapping entry #{mappingIndex} has null property rule at index {i}.");
                continue;
            }

            string sourcePropertyName = (rule.SourcePropertyName ?? string.Empty).Trim();
            string targetPropertyName = (rule.TargetPropertyName ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(sourcePropertyName))
            {
                messages.Add($"Mapping entry #{mappingIndex}, property rule #{i} has an empty source property name.");
            }
            else if (!IsValidPropertyName(sourcePropertyName))
            {
                messages.Add(
                    $"Mapping entry #{mappingIndex}, property rule #{i} has invalid source property name '{sourcePropertyName}'. Property names must start with '_' and use only letters, digits, or '_'.");
            }
            else
            {
                sourcePropertiesUsedInRules.Add(sourcePropertyName);
            }

            if (string.IsNullOrEmpty(targetPropertyName))
            {
                messages.Add($"Mapping entry #{mappingIndex}, property rule #{i} has an empty target property name.");
            }
            else if (!IsValidPropertyName(targetPropertyName))
            {
                messages.Add(
                    $"Mapping entry #{mappingIndex}, property rule #{i} has invalid target property name '{targetPropertyName}'. Property names must start with '_' and use only letters, digits, or '_'.");
            }
        }

        ValidatePropertyNameList(mapping.SupportedSourceProperties, SupportedLabel, mappingIndex, sourcePropertiesUsedInRules, messages);
        ValidatePropertyNameList(mapping.UnsupportedSourceProperties, UnsupportedLabel, mappingIndex, sourcePropertiesUsedInRules, messages);

        var declaredSupported = new HashSet<string>(mapping.SupportedSourceProperties, StringComparer.Ordinal);
        foreach (string propertyName in sourcePropertiesUsedInRules)
        {
            if (!declaredSupported.Contains(propertyName))
            {
                messages.Add(
                    $"Mapping entry #{mappingIndex}, property rule source '{propertyName}' is not declared in the supported property list.");
            }
        }
    }

    static void ValidatePropertyNameList(
        IReadOnlyList<string> propertyNames,
        string label,
        int mappingIndex,
        HashSet<string> sourcePropertiesUsedInRules,
        List<string> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < propertyNames.Count; i++)
        {
            string propertyName = (propertyNames[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(propertyName))
            {
                messages.Add($"Mapping entry #{mappingIndex}, {label} property list item #{i} is empty.");
                continue;
            }

            if (!IsValidPropertyName(propertyName))
            {
                messages.Add(
                    $"Mapping entry #{mappingIndex}, {label} property list item #{i} has invalid property name '{propertyName}'. Property names must start with '_' and use only letters, digits, or '_'.");
                continue;
            }

            if (!seen.Add(propertyName))
            {
                messages.Add($"Mapping entry #{mappingIndex}, {label} property list item #{i} duplicates '{propertyName}'.");
            }

            if (string.Equals(label, SupportedLabel, StringComparison.Ordinal) && !sourcePropertiesUsedInRules.Contains(propertyName))
            {
                messages.Add(
                    $"Mapping entry #{mappingIndex}, supported property '{propertyName}' is not present in property remap rules.");
            }
        }
    }

    static bool IsValidPropertyName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return false;

        if (propertyName[0] != '_')
            return false;

        for (int i = 1; i < propertyName.Length; i++)
        {
            char c = propertyName[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
