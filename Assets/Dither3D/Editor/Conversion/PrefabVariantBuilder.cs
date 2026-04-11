using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds prefab variants with renderer slots replaced by converted dither materials.
/// </summary>
public class PrefabVariantBuilder
{
    readonly MaterialConverter materialConverter;

    public PrefabVariantBuilder(MaterialConverter materialConverter = null)
    {
        this.materialConverter = materialConverter ?? new MaterialConverter();
    }

    /// <summary>
    /// Builds a prefab variant from a source prefab path.
    /// </summary>
    public PrefabVariantBuildResult BuildVariant(
        string sourcePrefabPath,
        DitherStyleProfile styleProfile,
        string variantOutputDirectory = null,
        string materialOutputDirectory = null)
    {
        var result = new PrefabVariantBuildResult();
        if (string.IsNullOrEmpty(sourcePrefabPath))
        {
            result.AddError("Source prefab path is null or empty.");
            return result;
        }

        GameObject sourcePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
        if (sourcePrefabAsset == null)
        {
            result.AddError("No prefab asset found at path '" + sourcePrefabPath + "'.");
            return result;
        }

        return BuildVariant(sourcePrefabAsset, styleProfile, variantOutputDirectory, materialOutputDirectory);
    }

    /// <summary>
    /// Builds a prefab variant from a source prefab asset.
    /// </summary>
    public PrefabVariantBuildResult BuildVariant(
        GameObject sourcePrefabAsset,
        DitherStyleProfile styleProfile,
        string variantOutputDirectory = null,
        string materialOutputDirectory = null)
    {
        var result = new PrefabVariantBuildResult();
        if (sourcePrefabAsset == null)
        {
            result.AddError("Source prefab asset is null.");
            return result;
        }

        if (styleProfile == null)
        {
            result.AddError("Style profile is null.");
            return result;
        }

        string sourcePrefabPath = AssetDatabase.GetAssetPath(sourcePrefabAsset);
        if (string.IsNullOrEmpty(sourcePrefabPath))
        {
            result.AddError("Source prefab must be a persisted asset.");
            return result;
        }

        string resolvedVariantDirectory = ResolveVariantOutputDirectory(sourcePrefabPath, variantOutputDirectory);
        string resolvedMaterialDirectory = ResolveMaterialOutputDirectory(resolvedVariantDirectory, materialOutputDirectory);
        EnsureFolderExists(resolvedVariantDirectory);
        EnsureFolderExists(resolvedMaterialDirectory);

        string variantPath = BuildDeterministicVariantAssetPath(resolvedVariantDirectory, sourcePrefabAsset, styleProfile);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
            AssetDatabase.DeleteAsset(variantPath);

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject instanceRoot = null;

        try
        {
            instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefabAsset, previewScene);
            if (instanceRoot == null)
            {
                result.AddError("Failed to instantiate source prefab for variant generation.");
                return result;
            }

            ReplaceRendererMaterials(instanceRoot, styleProfile, resolvedMaterialDirectory, result);

            GameObject variantAsset = PrefabUtility.SaveAsPrefabAsset(instanceRoot, variantPath);
            if (variantAsset == null)
            {
                result.AddError("Failed to save prefab variant at path '" + variantPath + "'.");
                return result;
            }

            AssetDatabase.SaveAssets();
            result.VariantAssetPath = variantPath;
            return result;
        }
        finally
        {
            if (instanceRoot != null)
                UnityEngine.Object.DestroyImmediate(instanceRoot);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    /// <summary>
    /// Builds a stable generated variant path for deterministic conversion outputs.
    /// </summary>
    public static string BuildDeterministicVariantAssetPath(string outputDirectory, GameObject sourcePrefabAsset, DitherStyleProfile styleProfile)
    {
        string prefabName = sourcePrefabAsset != null ? sourcePrefabAsset.name : "SourcePrefab";
        string profileName = styleProfile != null && !string.IsNullOrEmpty(styleProfile.ProfileName)
            ? styleProfile.ProfileName
            : (styleProfile != null ? styleProfile.name : "Profile");
        string fileName = SanitizeName(prefabName) + "__" + SanitizeName(profileName) + "__DitherVariant.prefab";
        return NormalizePath(Path.Combine(outputDirectory, fileName));
    }

    void ReplaceRendererMaterials(
        GameObject instanceRoot,
        DitherStyleProfile styleProfile,
        string materialOutputDirectory,
        PrefabVariantBuildResult result)
    {
        var conversionCache = new Dictionary<Material, ConversionResult>();
        Renderer[] renderers = instanceRoot.GetComponentsInChildren<Renderer>(true);

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            string rendererPath = BuildRendererPath(instanceRoot.transform, renderer.transform);
            Material[] slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0)
                continue;

            bool hasChanges = false;
            bool isPartOfModelPrefab = PrefabUtility.IsPartOfModelPrefab(renderer.gameObject);
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                Material sourceMaterial = slots[slotIndex];
                if (sourceMaterial == null)
                {
                    result.AddSkippedSlot(new PrefabMaterialSkip(rendererPath, slotIndex, "Source slot has no material."));
                    continue;
                }

                if (isPartOfModelPrefab)
                {
                    result.AddSkippedSlot(new PrefabMaterialSkip(
                        rendererPath,
                        slotIndex,
                        "Renderer is part of an immutable nested prefab instance and cannot be overridden."));
                    continue;
                }

                if (!conversionCache.TryGetValue(sourceMaterial, out ConversionResult conversionResult))
                {
                    conversionResult = materialConverter.ConvertAndPersist(sourceMaterial, styleProfile, materialOutputDirectory);
                    conversionCache[sourceMaterial] = conversionResult;
                }

                if (conversionResult.Warnings.Count > 0)
                {
                    for (int warningIndex = 0; warningIndex < conversionResult.Warnings.Count; warningIndex++)
                    {
                        result.AddWarning("[" + rendererPath + " slot " + slotIndex + "] " + conversionResult.Warnings[warningIndex]);
                    }
                }

                if (!conversionResult.Success || conversionResult.ConvertedMaterial == null)
                {
                    string reason = BuildSkipReason(conversionResult);
                    result.AddSkippedSlot(new PrefabMaterialSkip(rendererPath, slotIndex, reason));
                    continue;
                }

                string sourcePath = AssetDatabase.GetAssetPath(sourceMaterial);
                string convertedPath = conversionResult.OutputAssetPath;
                slots[slotIndex] = conversionResult.ConvertedMaterial;
                hasChanges = true;
                result.AddReplacement(new PrefabMaterialReplacement(rendererPath, slotIndex, sourcePath, convertedPath));
            }

            if (hasChanges)
                renderer.sharedMaterials = slots;
        }
    }

    static string BuildSkipReason(ConversionResult conversionResult)
    {
        if (conversionResult == null)
            return "Material conversion returned no result.";

        if (conversionResult.Errors.Count == 0)
            return "Material conversion did not produce a converted material.";

        return string.Join(" | ", conversionResult.Errors);
    }

    static string BuildRendererPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;

        if (target == root)
            return root.name;

        var segments = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        segments.Add(root.name);
        segments.Reverse();
        return string.Join("/", segments);
    }

    static string ResolveVariantOutputDirectory(string sourcePrefabPath, string variantOutputDirectory)
    {
        if (!string.IsNullOrEmpty(variantOutputDirectory))
            return NormalizePath(variantOutputDirectory);

        string sourceDirectory = Path.GetDirectoryName(sourcePrefabPath);
        return NormalizePath(Path.Combine(sourceDirectory ?? "Assets", "GeneratedPrefabVariants"));
    }

    static string ResolveMaterialOutputDirectory(string variantOutputDirectory, string materialOutputDirectory)
    {
        if (!string.IsNullOrEmpty(materialOutputDirectory))
            return NormalizePath(materialOutputDirectory);

        return NormalizePath(Path.Combine(variantOutputDirectory, "ConvertedMaterials"));
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
