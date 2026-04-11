using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts source materials into dither materials via registry mapping rules.
/// </summary>
public class MaterialConverter
{
    /// <summary>
    /// Converts a source material using a style profile into a new in-memory material.
    /// </summary>
    public ConversionResult Convert(Material sourceMaterial, DitherStyleProfile styleProfile)
    {
        var result = new ConversionResult();

        if (sourceMaterial == null)
        {
            result.AddError("Source material is null.");
            return result;
        }

        if (styleProfile == null)
        {
            result.AddError("Style profile is null.");
            return result;
        }

        if (styleProfile.ShaderAdapterRegistry == null)
        {
            result.AddError("Style profile is missing Shader Adapter Registry.");
            return result;
        }

        ShaderAdapterMapping mapping = FindMapping(sourceMaterial.shader, styleProfile.ShaderAdapterRegistry);
        if (mapping == null)
        {
            result.AddError("No shader adapter mapping found for source shader '" + sourceMaterial.shader.name + "'.");
            return result;
        }

        if (mapping.TargetShader == null)
        {
            result.AddError("Target shader is missing for source shader '" + mapping.SourceShaderName + "'.");
            return result;
        }

        result.AdapterUsed = mapping.SourceShaderName + " -> " + mapping.TargetShader.name;

        var targetMaterial = new Material(mapping.TargetShader);
        targetMaterial.name = BuildDeterministicMaterialName(sourceMaterial, styleProfile);
        result.ConvertedMaterial = targetMaterial;

        ApplyRules(sourceMaterial, targetMaterial, mapping, result);
        WarnExplicitUnsupportedProperties(sourceMaterial.shader, mapping, result);
        WarnUnmappedSourceProperties(sourceMaterial.shader, mapping, result);

        return result;
    }

    /// <summary>
    /// Converts a source material without writing any assets and reports the deterministic output path.
    /// </summary>
    public ConversionResult DryRunConvert(Material sourceMaterial, DitherStyleProfile styleProfile, string outputDirectory = null)
    {
        ConversionResult result = Convert(sourceMaterial, styleProfile);
        if (!result.Success || sourceMaterial == null)
            return result;

        string resolvedDirectory = ResolveOutputDirectory(sourceMaterial, outputDirectory);
        if (string.IsNullOrEmpty(resolvedDirectory))
        {
            result.AddError("Failed to resolve output directory.");
            return result;
        }

        result.OutputAssetPath = BuildDeterministicAssetPath(resolvedDirectory, sourceMaterial, styleProfile);
        return result;
    }

    /// <summary>
    /// Converts and persists a deterministic converted material asset.
    /// </summary>
    public ConversionResult ConvertAndPersist(Material sourceMaterial, DitherStyleProfile styleProfile, string outputDirectory = null)
    {
        ConversionResult result = Convert(sourceMaterial, styleProfile);
        if (!result.Success || result.ConvertedMaterial == null)
            return result;

        string resolvedDirectory = ResolveOutputDirectory(sourceMaterial, outputDirectory);
        if (string.IsNullOrEmpty(resolvedDirectory))
        {
            result.AddError("Failed to resolve output directory.");
            return result;
        }

        EnsureFolderExists(resolvedDirectory);
        string outputPath = BuildDeterministicAssetPath(resolvedDirectory, sourceMaterial, styleProfile);

        if (AssetDatabase.LoadAssetAtPath<Material>(outputPath) != null)
            AssetDatabase.DeleteAsset(outputPath);

        AssetDatabase.CreateAsset(result.ConvertedMaterial, outputPath);
        AssetDatabase.SaveAssets();
        result.OutputAssetPath = outputPath;

        GeneratedMaterialLinkMetadata metadata = GeneratedMaterialLinkMetadataUtility.CreateFor(sourceMaterial, styleProfile);
        string metadataError;
        if (!GeneratedMaterialLinkMetadataUtility.WriteAtPath(outputPath, metadata, out metadataError))
            result.AddWarning("Generated material link metadata was not written: " + metadataError);

        return result;
    }

    /// <summary>
    /// Returns mapped target property names for a source material/profile pair.
    /// </summary>
    public bool TryGetMappedTargetProperties(Material sourceMaterial, DitherStyleProfile styleProfile, out HashSet<string> mappedTargetProperties, out string error)
    {
        mappedTargetProperties = new HashSet<string>(StringComparer.Ordinal);
        error = string.Empty;

        if (sourceMaterial == null)
        {
            error = "Source material is null.";
            return false;
        }

        if (styleProfile == null)
        {
            error = "Style profile is null.";
            return false;
        }

        if (styleProfile.ShaderAdapterRegistry == null)
        {
            error = "Style profile is missing Shader Adapter Registry.";
            return false;
        }

        ShaderAdapterMapping mapping = FindMapping(sourceMaterial.shader, styleProfile.ShaderAdapterRegistry);
        if (mapping == null)
        {
            error = "No shader adapter mapping found for source shader '" + sourceMaterial.shader.name + "'.";
            return false;
        }

        IReadOnlyList<PropertyRemapRule> rules = mapping.PropertyRemapRules;
        for (int i = 0; i < rules.Count; i++)
        {
            PropertyRemapRule rule = rules[i];
            if (rule == null)
                continue;

            string targetProperty = (rule.TargetPropertyName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(targetProperty))
                mappedTargetProperties.Add(targetProperty);
        }

        return true;
    }

    /// <summary>
    /// Builds a stable generated material name for deterministic conversion outputs.
    /// </summary>
    public static string BuildDeterministicMaterialName(Material sourceMaterial, DitherStyleProfile styleProfile)
    {
        string sourcePart = SanitizeName(sourceMaterial != null ? sourceMaterial.name : "Source");
        string profileName = styleProfile != null && !string.IsNullOrEmpty(styleProfile.ProfileName)
            ? styleProfile.ProfileName
            : (styleProfile != null ? styleProfile.name : "Profile");

        string profilePart = SanitizeName(profileName);
        return sourcePart + "__" + profilePart + "__Dither";
    }

    /// <summary>
    /// Builds a stable output asset path.
    /// </summary>
    public static string BuildDeterministicAssetPath(string outputDirectory, Material sourceMaterial, DitherStyleProfile styleProfile)
    {
        string fileName = BuildDeterministicMaterialName(sourceMaterial, styleProfile) + ".mat";
        return NormalizePath(Path.Combine(outputDirectory, fileName));
    }

    static ShaderAdapterMapping FindMapping(Shader sourceShader, ShaderAdapterRegistry registry)
    {
        if (sourceShader == null || registry == null)
            return null;

        IReadOnlyList<ShaderAdapterMapping> mappings = registry.ShaderMappings;
        for (int i = 0; i < mappings.Count; i++)
        {
            ShaderAdapterMapping mapping = mappings[i];
            if (mapping == null)
                continue;

            string sourceShaderName = (mapping.SourceShaderName ?? string.Empty).Trim();
            if (string.Equals(sourceShaderName, sourceShader.name, StringComparison.Ordinal))
                return mapping;
        }

        return null;
    }

    static void ApplyRules(Material sourceMaterial, Material targetMaterial, ShaderAdapterMapping mapping, ConversionResult result)
    {
        IReadOnlyList<PropertyRemapRule> rules = mapping.PropertyRemapRules;
        for (int i = 0; i < rules.Count; i++)
        {
            PropertyRemapRule rule = rules[i];
            if (rule == null)
            {
                result.AddWarning("Property rule #" + i + " is null and was skipped.");
                continue;
            }

            string sourceProperty = (rule.SourcePropertyName ?? string.Empty).Trim();
            string targetProperty = (rule.TargetPropertyName ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(sourceProperty) || string.IsNullOrEmpty(targetProperty))
            {
                result.AddWarning("Property rule #" + i + " has empty source or target property and was skipped.");
                continue;
            }

            if (!targetMaterial.HasProperty(targetProperty))
            {
                result.AddWarning("Target property '" + targetProperty + "' does not exist and rule #" + i + " was skipped.");
                continue;
            }

            switch (rule.RuleKind)
            {
                case PropertyRemapRuleKind.DirectCopy:
                    ApplyDirectCopy(sourceMaterial, targetMaterial, sourceProperty, targetProperty, i, result);
                    break;
                case PropertyRemapRuleKind.ScaleBias:
                    ApplyScaleBias(sourceMaterial, targetMaterial, sourceProperty, targetProperty, rule, i, result);
                    break;
                case PropertyRemapRuleKind.ConstantFallback:
                    ApplyConstantFallback(sourceMaterial, targetMaterial, sourceProperty, targetProperty, rule, i, result);
                    break;
                case PropertyRemapRuleKind.SkipWithWarning:
                    result.AddWarning("Rule #" + i + " skipped mapping '" + sourceProperty + "' -> '" + targetProperty + "'.");
                    break;
                default:
                    result.AddWarning("Rule #" + i + " has unsupported rule kind '" + rule.RuleKind + "'.");
                    break;
            }
        }
    }

    static void ApplyDirectCopy(
        Material sourceMaterial,
        Material targetMaterial,
        string sourceProperty,
        string targetProperty,
        int ruleIndex,
        ConversionResult result)
    {
        if (!sourceMaterial.HasProperty(sourceProperty))
        {
            result.AddWarning("Rule #" + ruleIndex + " source property '" + sourceProperty + "' does not exist.");
            return;
        }

        if (!TryCopyPropertyValue(sourceMaterial, targetMaterial, sourceProperty, targetProperty))
        {
            result.AddWarning(
                "Rule #" + ruleIndex + " could not copy '" + sourceProperty + "' to '" + targetProperty + "' due to type mismatch.");
        }
    }

    static void ApplyScaleBias(
        Material sourceMaterial,
        Material targetMaterial,
        string sourceProperty,
        string targetProperty,
        PropertyRemapRule rule,
        int ruleIndex,
        ConversionResult result)
    {
        if (!sourceMaterial.HasProperty(sourceProperty))
        {
            result.AddWarning("Rule #" + ruleIndex + " source property '" + sourceProperty + "' does not exist.");
            return;
        }

        ShaderUtil.ShaderPropertyType sourceType;
        ShaderUtil.ShaderPropertyType targetType;
        if (!TryGetShaderPropertyType(sourceMaterial.shader, sourceProperty, out sourceType) ||
            !TryGetShaderPropertyType(targetMaterial.shader, targetProperty, out targetType) ||
            !IsFloatLike(sourceType) ||
            !IsFloatLike(targetType))
        {
            result.AddWarning(
                "Rule #" + ruleIndex + " scale/bias requires float-like source and target properties for '" + sourceProperty + "' -> '" + targetProperty + "'.");
            return;
        }

        float sourceValue = sourceMaterial.GetFloat(sourceProperty);
        targetMaterial.SetFloat(targetProperty, sourceValue * rule.Scale + rule.Bias);
    }

    static void ApplyConstantFallback(
        Material sourceMaterial,
        Material targetMaterial,
        string sourceProperty,
        string targetProperty,
        PropertyRemapRule rule,
        int ruleIndex,
        ConversionResult result)
    {
        ShaderUtil.ShaderPropertyType targetType;
        if (!TryGetShaderPropertyType(targetMaterial.shader, targetProperty, out targetType) || !IsFloatLike(targetType))
        {
            result.AddWarning(
                "Rule #" + ruleIndex + " constant fallback requires a float-like target property '" + targetProperty + "'.");
            return;
        }

        if (sourceMaterial.HasProperty(sourceProperty))
        {
            ShaderUtil.ShaderPropertyType sourceType;
            if (TryGetShaderPropertyType(sourceMaterial.shader, sourceProperty, out sourceType) && IsFloatLike(sourceType))
            {
                targetMaterial.SetFloat(targetProperty, sourceMaterial.GetFloat(sourceProperty));
                return;
            }
        }

        targetMaterial.SetFloat(targetProperty, rule.ConstantFallback);
    }

    static bool TryCopyPropertyValue(Material sourceMaterial, Material targetMaterial, string sourceProperty, string targetProperty)
    {
        ShaderUtil.ShaderPropertyType sourceType;
        ShaderUtil.ShaderPropertyType targetType;
        if (!TryGetShaderPropertyType(sourceMaterial.shader, sourceProperty, out sourceType) ||
            !TryGetShaderPropertyType(targetMaterial.shader, targetProperty, out targetType))
        {
            return false;
        }

        if (sourceType == ShaderUtil.ShaderPropertyType.Float || sourceType == ShaderUtil.ShaderPropertyType.Range)
        {
            if (!IsFloatLike(targetType))
                return false;

            targetMaterial.SetFloat(targetProperty, sourceMaterial.GetFloat(sourceProperty));
            return true;
        }

        if (sourceType == ShaderUtil.ShaderPropertyType.Color && targetType == ShaderUtil.ShaderPropertyType.Color)
        {
            targetMaterial.SetColor(targetProperty, sourceMaterial.GetColor(sourceProperty));
            return true;
        }

        if (sourceType == ShaderUtil.ShaderPropertyType.Vector && targetType == ShaderUtil.ShaderPropertyType.Vector)
        {
            targetMaterial.SetVector(targetProperty, sourceMaterial.GetVector(sourceProperty));
            return true;
        }

        if (sourceType == ShaderUtil.ShaderPropertyType.TexEnv && targetType == ShaderUtil.ShaderPropertyType.TexEnv)
        {
            targetMaterial.SetTexture(targetProperty, sourceMaterial.GetTexture(sourceProperty));
            targetMaterial.SetTextureScale(targetProperty, sourceMaterial.GetTextureScale(sourceProperty));
            targetMaterial.SetTextureOffset(targetProperty, sourceMaterial.GetTextureOffset(sourceProperty));
            return true;
        }

        return false;
    }

    static void WarnUnmappedSourceProperties(Shader sourceShader, ShaderAdapterMapping mapping, ConversionResult result)
    {
        var mappedProperties = new HashSet<string>(StringComparer.Ordinal);
        var explicitlyUnsupportedProperties = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<PropertyRemapRule> rules = mapping.PropertyRemapRules;
        for (int i = 0; i < rules.Count; i++)
        {
            PropertyRemapRule rule = rules[i];
            if (rule == null)
                continue;

            string sourceProperty = (rule.SourcePropertyName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(sourceProperty))
                mappedProperties.Add(sourceProperty);
        }

        IReadOnlyList<string> unsupportedProperties = mapping.UnsupportedSourceProperties;
        for (int i = 0; i < unsupportedProperties.Count; i++)
        {
            string unsupportedProperty = (unsupportedProperties[i] ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(unsupportedProperty))
                explicitlyUnsupportedProperties.Add(unsupportedProperty);
        }

        int propertyCount = ShaderUtil.GetPropertyCount(sourceShader);
        for (int i = 0; i < propertyCount; i++)
        {
            string sourcePropertyName = ShaderUtil.GetPropertyName(sourceShader, i);
            if (!mappedProperties.Contains(sourcePropertyName) && !explicitlyUnsupportedProperties.Contains(sourcePropertyName))
            {
                result.AddWarning(
                    "Unmapped source property '" + sourcePropertyName + "' was skipped (no implicit mapping is performed).");
            }
        }
    }

    static void WarnExplicitUnsupportedProperties(Shader sourceShader, ShaderAdapterMapping mapping, ConversionResult result)
    {
        IReadOnlyList<string> unsupportedProperties = mapping.UnsupportedSourceProperties;
        for (int i = 0; i < unsupportedProperties.Count; i++)
        {
            string propertyName = (unsupportedProperties[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(propertyName))
                continue;

            bool existsInSource = ShaderHasProperty(sourceShader, propertyName);
            if (existsInSource)
            {
                result.AddWarning(
                    "Explicitly unsupported source property '" + propertyName + "' was skipped by adapter '" +
                    mapping.SourceShaderName + "'.");
            }
            else
            {
                result.AddWarning(
                    "Explicitly unsupported source property '" + propertyName +
                    "' is not present on this source shader variant and remained unmapped.");
            }
        }
    }

    static bool ShaderHasProperty(Shader shader, string propertyName)
    {
        ShaderUtil.ShaderPropertyType ignored;
        return TryGetShaderPropertyType(shader, propertyName, out ignored);
    }

    static bool TryGetShaderPropertyType(Shader shader, string propertyName, out ShaderUtil.ShaderPropertyType propertyType)
    {
        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < propertyCount; i++)
        {
            if (string.Equals(ShaderUtil.GetPropertyName(shader, i), propertyName, StringComparison.Ordinal))
            {
                propertyType = ShaderUtil.GetPropertyType(shader, i);
                return true;
            }
        }

        propertyType = ShaderUtil.ShaderPropertyType.Float;
        return false;
    }

    static bool IsFloatLike(ShaderUtil.ShaderPropertyType propertyType)
    {
        return propertyType == ShaderUtil.ShaderPropertyType.Float || propertyType == ShaderUtil.ShaderPropertyType.Range;
    }

    static string ResolveOutputDirectory(Material sourceMaterial, string outputDirectory)
    {
        if (!string.IsNullOrEmpty(outputDirectory))
            return NormalizePath(outputDirectory);

        string sourcePath = AssetDatabase.GetAssetPath(sourceMaterial);
        if (string.IsNullOrEmpty(sourcePath))
            return "Assets";

        return NormalizePath(Path.GetDirectoryName(sourcePath));
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] segments = folderPath.Split('/');
        if (segments.Length == 0)
            return;

        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }

    static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/');
    }

    static string SanitizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Unnamed";

        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                chars[i] = '_';
        }

        return new string(chars);
    }
}
