using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public enum LinkedGeneratedMaterialUpdatePolicy
{
    StyleParametersOnly,
    AllMappedParameters
}

public class LinkedGeneratedMaterialReapplySummary
{
    public int scannedMaterials;
    public int linkedMaterials;
    public int updatedMaterials;
    public int failedMaterials;
    public int brokenLinks;
    public readonly List<string> messages = new List<string>();
}

public static class LinkedGeneratedMaterialReapplyUtility
{
    public static LinkedGeneratedMaterialReapplySummary Reapply(DitherStyleProfile styleProfile, LinkedGeneratedMaterialUpdatePolicy updatePolicy)
    {
        var summary = new LinkedGeneratedMaterialReapplySummary();
        if (styleProfile == null)
        {
            summary.failedMaterials = 1;
            summary.messages.Add("Style profile is null.");
            return summary;
        }

        if (styleProfile.ShaderAdapterRegistry == null)
        {
            summary.failedMaterials = 1;
            summary.messages.Add("Style profile is missing Shader Adapter Registry.");
            return summary;
        }

        string styleProfilePath = AssetDatabase.GetAssetPath(styleProfile);
        if (string.IsNullOrEmpty(styleProfilePath))
        {
            summary.failedMaterials = 1;
            summary.messages.Add("Style profile must be a persisted asset.");
            return summary;
        }

        List<string> validationMessages = ShaderAdapterRegistryValidationUtility.Validate(styleProfile);
        if (validationMessages.Count > 0)
        {
            summary.failedMaterials = 1;
            for (int i = 0; i < validationMessages.Count; i++)
                summary.messages.Add(validationMessages[i]);
            return summary;
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        var converter = new MaterialConverter();
        bool hasDirtyAssets = false;

        try
        {
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                summary.scannedMaterials++;
                EditorUtility.DisplayProgressBar(
                    "Dither 3D Reapply Profile",
                    "Scanning linked materials: " + materialPath,
                    materialGuids.Length > 0 ? (float)i / materialGuids.Length : 1f);

                GeneratedMaterialLinkMetadata metadata;
                string metadataError;
                if (!GeneratedMaterialLinkMetadataUtility.TryReadAtPath(materialPath, out metadata, out metadataError))
                    continue;

                if (metadata == null || !string.Equals(metadata.styleProfileAssetPath, styleProfilePath, StringComparison.Ordinal))
                    continue;

                summary.linkedMaterials++;

                Material generatedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (generatedMaterial == null)
                {
                    RegisterBrokenLink(summary, materialPath, "Generated material could not be loaded.");
                    continue;
                }

                if (string.IsNullOrEmpty(metadata.sourceMaterialAssetPath))
                {
                    RegisterBrokenLink(summary, materialPath, "Missing source material path in metadata.");
                    continue;
                }

                Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(metadata.sourceMaterialAssetPath);
                if (sourceMaterial == null)
                {
                    RegisterBrokenLink(summary, materialPath, "Source material no longer exists at '" + metadata.sourceMaterialAssetPath + "'.");
                    continue;
                }

                ConversionResult conversion = converter.Convert(sourceMaterial, styleProfile);
                if (!conversion.Success || conversion.ConvertedMaterial == null)
                {
                    summary.failedMaterials++;
                    summary.messages.Add(materialPath + " failed to re-convert: " + string.Join(" | ", conversion.Errors));
                    continue;
                }

                bool applied = ApplyConvertedMaterial(generatedMaterial, conversion.ConvertedMaterial, sourceMaterial, styleProfile, converter, updatePolicy, out string applyError);
                UnityEngine.Object.DestroyImmediate(conversion.ConvertedMaterial);

                if (!applied)
                {
                    summary.failedMaterials++;
                    summary.messages.Add(materialPath + " failed to apply: " + applyError);
                    continue;
                }

                metadata.styleProfileDependencyHash = ComputeAssetDependencyHash(styleProfile);
                metadata.adapterRegistryAssetPath = AssetDatabase.GetAssetPath(styleProfile.ShaderAdapterRegistry);
                metadata.adapterRegistryDependencyHash = ComputeAssetDependencyHash(styleProfile.ShaderAdapterRegistry);
                metadata.lastReappliedAtUtc = DateTime.UtcNow.ToString("o");

                string writeError;
                if (!GeneratedMaterialLinkMetadataUtility.WriteAtPath(materialPath, metadata, out writeError))
                {
                    summary.failedMaterials++;
                    summary.messages.Add(materialPath + " metadata update failed: " + writeError);
                    continue;
                }

                EditorUtility.SetDirty(generatedMaterial);
                hasDirtyAssets = true;
                summary.updatedMaterials++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (hasDirtyAssets)
            AssetDatabase.SaveAssets();

        return summary;
    }

    static bool ApplyConvertedMaterial(
        Material destination,
        Material converted,
        Material sourceMaterial,
        DitherStyleProfile styleProfile,
        MaterialConverter converter,
        LinkedGeneratedMaterialUpdatePolicy updatePolicy,
        out string error)
    {
        error = string.Empty;
        if (destination == null || converted == null)
        {
            error = "Destination or converted material is null.";
            return false;
        }

        if (updatePolicy == LinkedGeneratedMaterialUpdatePolicy.AllMappedParameters)
        {
            if (destination.shader != converted.shader)
                destination.shader = converted.shader;

            destination.CopyPropertiesFromMaterial(converted);
            return true;
        }

        if (destination.shader != converted.shader)
        {
            error = "Target shader mismatch; style-only updates require unchanged target shader.";
            return false;
        }

        HashSet<string> mappedTargetProperties;
        if (!converter.TryGetMappedTargetProperties(sourceMaterial, styleProfile, out mappedTargetProperties, out error))
            return false;

        CopyUnmappedProperties(converted, destination, mappedTargetProperties);
        return true;
    }

    static void CopyUnmappedProperties(Material source, Material destination, HashSet<string> mappedTargetProperties)
    {
        int propertyCount = source.shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            string propertyName = source.shader.GetPropertyName(i);
            if (mappedTargetProperties.Contains(propertyName) || !destination.HasProperty(propertyName))
                continue;

            ShaderPropertyType propertyType = source.shader.GetPropertyType(i);
            switch (propertyType)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    destination.SetFloat(propertyName, source.GetFloat(propertyName));
                    break;
                case ShaderPropertyType.Color:
                    destination.SetColor(propertyName, source.GetColor(propertyName));
                    break;
                case ShaderPropertyType.Vector:
                    destination.SetVector(propertyName, source.GetVector(propertyName));
                    break;
                case ShaderPropertyType.Texture:
                    destination.SetTexture(propertyName, source.GetTexture(propertyName));
                    destination.SetTextureScale(propertyName, source.GetTextureScale(propertyName));
                    destination.SetTextureOffset(propertyName, source.GetTextureOffset(propertyName));
                    break;
            }
        }
    }

    static void RegisterBrokenLink(LinkedGeneratedMaterialReapplySummary summary, string materialPath, string reason)
    {
        summary.brokenLinks++;
        summary.failedMaterials++;
        string message = "Broken generated-material link at '" + materialPath + "': " + reason;
        summary.messages.Add(message);
        Debug.LogWarning(message);
    }

    static string ComputeAssetDependencyHash(UnityEngine.Object asset)
    {
        if (asset == null)
            return string.Empty;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return AssetDatabase.GetAssetDependencyHash(path).ToString();
    }
}

public static class LinkedGeneratedMaterialReapplyCommand
{
    [MenuItem("Tools/Dither 3D/Reapply Linked Generated Materials")]
    static void ReapplyLinkedGeneratedMaterials()
    {
        DitherStyleProfile profile = Selection.activeObject as DitherStyleProfile;
        if (profile == null)
        {
            EditorUtility.DisplayDialog(
                "Dither 3D Reapply",
                "Select a DitherStyleProfile asset, then run this command again.",
                "OK");
            return;
        }

        int selectedOption = EditorUtility.DisplayDialogComplex(
            "Dither 3D Reapply",
            "Choose update policy for linked generated materials.",
            "Style Params Only",
            "Cancel",
            "Update All Mapped Params");

        if (selectedOption == 1)
            return;

        LinkedGeneratedMaterialUpdatePolicy policy = selectedOption == 0
            ? LinkedGeneratedMaterialUpdatePolicy.StyleParametersOnly
            : LinkedGeneratedMaterialUpdatePolicy.AllMappedParameters;

        LinkedGeneratedMaterialReapplySummary summary = LinkedGeneratedMaterialReapplyUtility.Reapply(profile, policy);
        string consoleSummary = "Dither 3D reapply summary for '" + profile.name +
                                "': scanned=" + summary.scannedMaterials +
                                ", linked=" + summary.linkedMaterials +
                                ", updated=" + summary.updatedMaterials +
                                ", failed=" + summary.failedMaterials +
                                ", brokenLinks=" + summary.brokenLinks;
        if (summary.failedMaterials > 0)
            Debug.LogWarning(consoleSummary);
        else
            Debug.Log(consoleSummary);

        string details = "Profile: " + profile.name +
                         "\nPolicy: " + policy +
                         "\nScanned: " + summary.scannedMaterials +
                         "\nLinked: " + summary.linkedMaterials +
                         "\nUpdated: " + summary.updatedMaterials +
                         "\nFailed: " + summary.failedMaterials +
                         "\nBroken Links: " + summary.brokenLinks;
        if (summary.failedMaterials > 0)
            details += "\n\nSee Console for warnings/errors.";

        EditorUtility.DisplayDialog("Dither 3D Reapply Complete", details, "OK");
    }

    [MenuItem("Tools/Dither 3D/Reapply Linked Generated Materials", true)]
    static bool ValidateReapplyLinkedGeneratedMaterials()
    {
        return Selection.activeObject is DitherStyleProfile;
    }
}
