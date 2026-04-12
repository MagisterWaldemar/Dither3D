using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PainterlyValidationReportTool
{
    const string ValidationSetAssetPath = "Assets/Dither3D/Editor/DataModel/Validation/PainterlyValidationSceneSet.asset";
    const string ReportFolderPath = "Assets/Dither3D/ValidationReports";
    const string CaptureFolderPath = "Assets/Dither3D/ValidationReports/Captures";
    const string JsonPath = "Assets/Dither3D/ValidationReports/PainterlyBaselineComparison.json";
    const string CsvPath = "Assets/Dither3D/ValidationReports/PainterlyBaselineComparison.csv";
    const string TxtPath = "Assets/Dither3D/ValidationReports/PainterlyBaselineComparison.txt";
    const string TuningOrder = "foundation/chroma -> accent envelope -> accent sparsity -> detail sensitivities";
    const string ToolVersion = "PainterlyValidationReportTool.v1";
    const float MinWeightSumThreshold = 0.0001f;
    const float SpecularHeavyGlossinessThreshold = 0.70f;
    const float SpecularHeavyMetallicThreshold = 0.60f;
    const float NoisyTextureDetailThreshold = 1.20f;
    const float FlatPaletteAccentSparsityThreshold = 0.60f;
    const float FlatPaletteHighlightThreshold = 0.25f;

    const string CompositionProperty = "_PointillismCompositionMode";
    const string BaseMutingProperty = "_PointillismBaseMuting";
    const string ChromaPushProperty = "_PointillismChromaPush";
    const string ComplementProperty = "_PointillismComplementaryAccentAmount";
    const string AccentSparsityProperty = "_PointillismAccentSparsity";
    const string DetailAlbedoProperty = "_PointillismDetailSensitivityAlbedo";
    const string DetailNormalProperty = "_PointillismDetailSensitivityNormal";
    const string HighlightProperty = "_PointillismHighlightAccentStrength";
    const string PhaseSpeedProperty = "_BlueNoisePhaseSpeed";
    const string HysteresisProperty = "_BlueNoiseHysteresis";
    const string MinDotProperty = "_BlueNoiseMinDot";
    const string GlossinessProperty = "_Glossiness";
    const string MetallicProperty = "_Metallic";

    const string ModeLegacy = "LegacyQuantized";
    const string ModeRoleComposed = "RoleComposed";
    const float LegacyModeValue = 0f;
    const float RoleModeValue = 1f;

    const string LabelProxy = "Proxy";

    [MenuItem("Tools/Dither 3D/Painterly Validation/Run Baseline Comparison + Capture")]
    static void RunBaselineComparisonAndCapture()
    {
        PainterlyValidationSceneSet sceneSet = GetOrCreateDefaultSceneSet();
        if (sceneSet == null)
        {
            Debug.LogError("Painterly validation aborted: scene set could not be loaded or created.");
            return;
        }

        EnsureFolderExists(ReportFolderPath);
        EnsureFolderExists(CaptureFolderPath);

        PainterlyValidationReport report = BuildReport(sceneSet);
        WriteDeterministicReports(report);
        AssetDatabase.Refresh();

        if (report.warnings.Count > 0)
            Debug.LogWarning("Painterly validation completed with warnings. See report + console for details.");
        else
            Debug.Log("Painterly validation completed successfully.");
    }

    [MenuItem("Tools/Dither 3D/Painterly Validation/Create or Refresh Default Scene Set")]
    static void CreateOrRefreshDefaultSceneSetMenu()
    {
        PainterlyValidationSceneSet sceneSet = GetOrCreateDefaultSceneSet();
        if (sceneSet != null)
            Debug.Log("Painterly validation scene set is ready at: " + ValidationSetAssetPath);
    }

    [MenuItem("Tools/Dither 3D/Painterly Validation/Apply Safety Fallback Profile (Selected Materials)")]
    static void ApplySafetyFallbackProfileToSelection()
    {
        Material[] selected = Selection.GetFiltered<Material>(SelectionMode.Assets);
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Painterly Fallback", "Select one or more materials in the Project window.", "OK");
            return;
        }

        int changed = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Material material = selected[i];
            if (material == null || !material.HasProperty(CompositionProperty))
                continue;

            ApplySafeRoleComposedFallback(material);
            EditorUtility.SetDirty(material);
            changed++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Painterly Fallback", $"Applied fallback profile to {changed} material(s).", "OK");
    }

    static void ApplySafeRoleComposedFallback(Material material)
    {
        SetIfHasFloat(material, CompositionProperty, RoleModeValue);
        SetIfHasFloat(material, BaseMutingProperty, 0.52f);
        SetIfHasFloat(material, ChromaPushProperty, 0.38f);
        SetIfHasFloat(material, ComplementProperty, 0.10f);
        SetIfHasFloat(material, AccentSparsityProperty, 0.90f);
        SetIfHasFloat(material, DetailAlbedoProperty, 0.75f);
        SetIfHasFloat(material, DetailNormalProperty, 0.70f);
        SetIfHasFloat(material, HighlightProperty, 0.15f);
        SetIfHasFloat(material, PhaseSpeedProperty, 0.08f);
        SetIfHasFloat(material, HysteresisProperty, 0.90f);
        SetIfHasFloat(material, MinDotProperty, 0.18f);
    }

    static void SetIfHasFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    static PainterlyValidationSceneSet GetOrCreateDefaultSceneSet()
    {
        PainterlyValidationSceneSet sceneSet = AssetDatabase.LoadAssetAtPath<PainterlyValidationSceneSet>(ValidationSetAssetPath);
        if (sceneSet != null)
            return sceneSet;

        EnsureFolderExists("Assets/Dither3D/Editor/DataModel/Validation");
        sceneSet = ScriptableObject.CreateInstance<PainterlyValidationSceneSet>();
        PopulateDefaultSceneSet(sceneSet);
        AssetDatabase.CreateAsset(sceneSet, ValidationSetAssetPath);
        EditorUtility.SetDirty(sceneSet);
        AssetDatabase.SaveAssets();
        return sceneSet;
    }

    static void PopulateDefaultSceneSet(PainterlyValidationSceneSet sceneSet)
    {
        sceneSet.scenes.Clear();
        sceneSet.sharedMaterialPaths.Clear();

        sceneSet.scenes.Add(new PainterlyValidationSceneEntry
        {
            label = "LowChromaGradients",
            category = "low-chroma gradients",
            scenePath = "Assets/Scenes/TestGradient/TestGradient.unity",
            cameraName = "Main Camera",
            notes = "Gradient-focused scene for foundation/chroma envelope checks.",
            materialPaths = new List<string>
            {
                "Assets/Scenes/TestGradient/TestGradientDither.mat",
                "Assets/Scenes/TestGradient/TestGradientRaw.mat"
            }
        });

        sceneSet.scenes.Add(new PainterlyValidationSceneEntry
        {
            label = "HighFrequencyAlbedo",
            category = "high-frequency albedo",
            scenePath = "Assets/Scenes/PerspectiveRow.unity",
            cameraName = "Main Camera",
            notes = "Tile detail scene for accent placement noise checks.",
            materialPaths = new List<string>
            {
                "Assets/Materials/Dither3DTiles.mat"
            }
        });

        sceneSet.scenes.Add(new PainterlyValidationSceneEntry
        {
            label = "StrongNormalLowAlbedo",
            category = "strong-normal low-albedo detail",
            scenePath = "Assets/Scenes/RotatingScene.unity",
            cameraName = "Main Camera",
            notes = "Normal-driven detail scene for role redistribution sensitivity.",
            materialPaths = new List<string>
            {
                "Assets/Materials/Dither3D.mat"
            }
        });

        sceneSet.scenes.Add(new PainterlyValidationSceneEntry
        {
            label = "MixedHighlights",
            category = "mixed highlights",
            scenePath = "Assets/Scenes/RotatingLight.unity",
            cameraName = "Main Camera",
            notes = "Highlight/specular stress scene for accent gating robustness.",
            materialPaths = new List<string>
            {
                "Assets/Materials/Dither3D_LargeDots.mat"
            }
        });

        sceneSet.sharedMaterialPaths.Add("Assets/Materials/Dither3D.mat");
        sceneSet.sharedMaterialPaths.Add("Assets/Materials/Dither3DTiles.mat");
        sceneSet.sharedMaterialPaths.Add("Assets/Materials/Dither3D_LargeDots.mat");
    }

    static PainterlyValidationReport BuildReport(PainterlyValidationSceneSet sceneSet)
    {
        PainterlyValidationReport report = new PainterlyValidationReport();
        report.generatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        report.toolVersion = ToolVersion;
        report.tuningOrder = TuningOrder;
        report.fallbackProfile = BuildFallbackProfileDescription();

        List<Material> sharedMaterials = LoadMaterials(sceneSet.sharedMaterialPaths, "Shared", report.warnings);

        for (int i = 0; i < sceneSet.scenes.Count; i++)
        {
            PainterlyValidationSceneEntry entry = sceneSet.scenes[i];
            PainterlySceneReport sceneReport = BuildSceneReport(entry, sharedMaterials, report.warnings);
            report.scenes.Add(sceneReport);
        }

        report.summary = BuildSummary(report.scenes);
        return report;
    }

    static PainterlySceneReport BuildSceneReport(PainterlyValidationSceneEntry entry, List<Material> sharedMaterials, List<string> warnings)
    {
        PainterlySceneReport sceneReport = new PainterlySceneReport();
        sceneReport.sceneLabel = string.IsNullOrEmpty(entry.label) ? "UnnamedScene" : entry.label;
        sceneReport.category = entry.category;
        sceneReport.scenePath = entry.scenePath;
        sceneReport.cameraName = string.IsNullOrEmpty(entry.cameraName) ? "Main Camera" : entry.cameraName;
        sceneReport.notes = entry.notes;
        sceneReport.tuningOrder = TuningOrder;

        bool sceneExists = !string.IsNullOrEmpty(entry.scenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.scenePath) != null;
        if (!sceneExists)
            warnings.Add($"Scene not found for validation entry '{sceneReport.sceneLabel}': {entry.scenePath}");
        if (string.IsNullOrEmpty(entry.cameraName))
            warnings.Add($"Validation entry '{sceneReport.sceneLabel}' has no camera name; capture placeholder will be emitted.");

        List<Material> materials = new List<Material>();
        List<Material> localMaterials = LoadMaterials(entry.materialPaths, sceneReport.sceneLabel, warnings);
        MergeMaterials(materials, sharedMaterials);
        MergeMaterials(materials, localMaterials);

        if (materials.Count == 0)
            warnings.Add($"Validation entry '{sceneReport.sceneLabel}' has no valid materials.");

        PainterlyModeMetrics legacyMetrics = EvaluateModeForScene(sceneReport.sceneLabel, materials, ModeLegacy, LegacyModeValue);
        PainterlyModeMetrics roleMetrics = EvaluateModeForScene(sceneReport.sceneLabel, materials, ModeRoleComposed, RoleModeValue);
        sceneReport.modeMetrics.Add(legacyMetrics);
        sceneReport.modeMetrics.Add(roleMetrics);
        sceneReport.metricDeltas = BuildMetricDeltas(legacyMetrics, roleMetrics);

        sceneReport.materialSnapshots = CaptureMaterialSnapshots(materials);
        sceneReport.captureArtifacts = WriteCapturePlaceholders(sceneReport.sceneLabel, entry.scenePath, sceneReport.cameraName, sceneExists);
        sceneReport.fallbackRecommendations = BuildFallbackRecommendations(materials);
        return sceneReport;
    }

    static List<PainterlyMetricDelta> BuildMetricDeltas(PainterlyModeMetrics legacyMetrics, PainterlyModeMetrics roleMetrics)
    {
        var deltas = new List<PainterlyMetricDelta>();
        Dictionary<string, PainterlyMetricValue> legacyByName = new Dictionary<string, PainterlyMetricValue>(StringComparer.Ordinal);
        for (int i = 0; i < legacyMetrics.metrics.Count; i++)
            legacyByName[legacyMetrics.metrics[i].metricName] = legacyMetrics.metrics[i];

        for (int i = 0; i < roleMetrics.metrics.Count; i++)
        {
            PainterlyMetricValue roleMetric = roleMetrics.metrics[i];
            if (!legacyByName.TryGetValue(roleMetric.metricName, out PainterlyMetricValue legacyMetric))
                continue;

            deltas.Add(new PainterlyMetricDelta
            {
                metricName = roleMetric.metricName,
                exactVsProxy = roleMetric.exactVsProxy,
                legacyValue = legacyMetric.value,
                roleComposedValue = roleMetric.value,
                deltaRoleMinusLegacy = roleMetric.value - legacyMetric.value
            });
        }

        return deltas;
    }

    static List<PainterlyMaterialSnapshot> CaptureMaterialSnapshots(List<Material> materials)
    {
        List<PainterlyMaterialSnapshot> snapshots = new List<PainterlyMaterialSnapshot>();
        for (int i = 0; i < materials.Count; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            PainterlyMaterialSnapshot snapshot = new PainterlyMaterialSnapshot();
            snapshot.materialName = material.name;
            snapshot.assetPath = AssetDatabase.GetAssetPath(material);
            snapshot.compositionMode = GetFloat(material, CompositionProperty, LegacyModeValue);
            snapshot.baseMuting = GetFloat(material, BaseMutingProperty, 0.35f);
            snapshot.chromaPush = GetFloat(material, ChromaPushProperty, 0.60f);
            snapshot.complementaryAccentAmount = GetFloat(material, ComplementProperty, 0.20f);
            snapshot.accentSparsity = GetFloat(material, AccentSparsityProperty, 0.75f);
            snapshot.detailSensitivityAlbedo = GetFloat(material, DetailAlbedoProperty, 1.00f);
            snapshot.detailSensitivityNormal = GetFloat(material, DetailNormalProperty, 1.00f);
            snapshot.highlightAccentStrength = GetFloat(material, HighlightProperty, 0.35f);
            snapshot.blueNoisePhaseSpeed = GetFloat(material, PhaseSpeedProperty, 0.15f);
            snapshot.blueNoiseHysteresis = GetFloat(material, HysteresisProperty, 0.80f);
            snapshot.blueNoiseMinDot = GetFloat(material, MinDotProperty, 0.12f);
            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    static List<PainterlyCaptureArtifact> WriteCapturePlaceholders(string sceneLabel, string scenePath, string cameraName, bool sceneExists)
    {
        List<PainterlyCaptureArtifact> captures = new List<PainterlyCaptureArtifact>();
        captures.Add(WriteCapturePlaceholder(sceneLabel, scenePath, cameraName, ModeLegacy, sceneExists));
        captures.Add(WriteCapturePlaceholder(sceneLabel, scenePath, cameraName, ModeRoleComposed, sceneExists));
        return captures;
    }

    static PainterlyCaptureArtifact WriteCapturePlaceholder(string sceneLabel, string scenePath, string cameraName, string modeName, bool sceneExists)
    {
        string safeScene = SanitizeToken(sceneLabel);
        string safeMode = SanitizeToken(modeName);
        string relativePath = $"{CaptureFolderPath}/{safeScene}_{safeMode}_CapturePlaceholder.txt";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Deterministic capture placeholder");
        builder.AppendLine("SceneLabel: " + sceneLabel);
        builder.AppendLine("ScenePath: " + scenePath);
        builder.AppendLine("CameraName: " + cameraName);
        builder.AppendLine("Mode: " + modeName);
        if (!sceneExists)
            builder.AppendLine("Status: Warning - scene missing; screenshot was not captured.");
        else if (string.IsNullOrEmpty(cameraName))
            builder.AppendLine("Status: Warning - camera missing; screenshot was not captured.");
        else
            builder.AppendLine("Status: Placeholder - deterministic screenshot capture hook.");

        File.WriteAllText(AssetPathToAbsolutePath(relativePath), builder.ToString(), Encoding.UTF8);
        AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);

        return new PainterlyCaptureArtifact
        {
            modeName = modeName,
            capturePath = relativePath,
            exactVsProxy = LabelProxy,
            notes = sceneExists ? "Placeholder capture emitted." : "Scene missing; warning emitted instead of hard fail."
        };
    }

    static List<PainterlyFallbackRecommendation> BuildFallbackRecommendations(List<Material> materials)
    {
        var recommendations = new List<PainterlyFallbackRecommendation>();
        for (int i = 0; i < materials.Count; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            float accentSparsity = GetFloat(material, AccentSparsityProperty, 0.75f);
            float highlight = GetFloat(material, HighlightProperty, 0.35f);
            float detailA = GetFloat(material, DetailAlbedoProperty, 1f);
            float detailN = GetFloat(material, DetailNormalProperty, 1f);
            float glossiness = GetFloat(material, GlossinessProperty, 0.5f);
            float metallic = GetFloat(material, MetallicProperty, 0f);

            bool specularHeavy = glossiness > SpecularHeavyGlossinessThreshold || metallic > SpecularHeavyMetallicThreshold;
            bool noisyTexture = detailA > NoisyTextureDetailThreshold || detailN > NoisyTextureDetailThreshold;
            bool flatPalette = accentSparsity < FlatPaletteAccentSparsityThreshold && highlight < FlatPaletteHighlightThreshold;

            string action = "Keep RoleComposed";
            string reason = "No fallback trigger detected.";

            if (specularHeavy)
            {
                action = "Prefer LegacyQuantized";
                reason = "Specular-heavy surface: reduce highlight instability risk by switching composition mode.";
            }
            else if (noisyTexture)
            {
                action = "Reduce accents";
                reason = "High detail sensitivity likely increases accent flicker on noisy textures.";
            }
            else if (flatPalette)
            {
                action = "Reduce accents";
                reason = "Flat palette content benefits from sparse accents to avoid blotchy patches.";
            }

            recommendations.Add(new PainterlyFallbackRecommendation
            {
                materialName = material.name,
                materialPath = AssetDatabase.GetAssetPath(material),
                action = action,
                reason = reason
            });
        }

        return recommendations;
    }

    static PainterlyModeMetrics EvaluateModeForScene(string sceneLabel, List<Material> materials, string modeName, float modeValue)
    {
        PainterlyModeMetrics result = new PainterlyModeMetrics();
        result.sceneLabel = sceneLabel;
        result.modeName = modeName;
        result.exactVsProxy = LabelProxy;

        if (materials.Count == 0)
            return result;

        float foundation = 0f;
        float chroma = 0f;
        float complement = 0f;
        float highlight = 0f;
        float accentPlacement = 0f;
        float meanColorError = 0f;
        float temporalStability = 0f;

        for (int i = 0; i < materials.Count; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            PainterlyProxyEvaluation eval = EvaluateMaterialProxy(material, modeValue);
            foundation += eval.foundationOccupancy;
            chroma += eval.chromaOccupancy;
            complement += eval.complementOccupancy;
            highlight += eval.highlightOccupancy;
            accentPlacement += eval.accentPlacementQuality;
            meanColorError += eval.meanColorErrorProxy;
            temporalStability += eval.temporalRoleFlipStability;
        }

        float invCount = 1f / Mathf.Max(1, materials.Count);
        foundation *= invCount;
        chroma *= invCount;
        complement *= invCount;
        highlight *= invCount;
        accentPlacement *= invCount;
        meanColorError *= invCount;
        temporalStability *= invCount;

        result.metrics.Add(CreateMetric("role_occupancy_foundation", foundation, LabelProxy, "role occupancy distribution (proxy)"));
        result.metrics.Add(CreateMetric("role_occupancy_chroma", chroma, LabelProxy, "role occupancy distribution (proxy)"));
        result.metrics.Add(CreateMetric("role_occupancy_complement", complement, LabelProxy, "role occupancy distribution (proxy)"));
        result.metrics.Add(CreateMetric("role_occupancy_highlight", highlight, LabelProxy, "role occupancy distribution (proxy)"));
        result.metrics.Add(CreateMetric("accent_sparsity_spatial_placement_quality", accentPlacement, LabelProxy, "accent sparsity/spatial placement quality (proxy)"));
        result.metrics.Add(CreateMetric("mean_color_error_vs_target_shading", meanColorError, LabelProxy, "mean color error vs target shading (proxy)"));
        result.metrics.Add(CreateMetric("temporal_role_flip_stability_frame_to_frame", temporalStability, LabelProxy, "temporal role-flip stability frame-to-frame (proxy)"));

        return result;
    }

    static PainterlyMetricValue CreateMetric(string name, float value, string exactVsProxy, string notes)
    {
        return new PainterlyMetricValue
        {
            metricName = name,
            value = Mathf.Clamp01(value),
            exactVsProxy = exactVsProxy,
            notes = notes
        };
    }

    static PainterlyProxyEvaluation EvaluateMaterialProxy(Material material, float modeValue)
    {
        float baseMuting = GetFloat(material, BaseMutingProperty, 0.35f);
        float chromaPush = GetFloat(material, ChromaPushProperty, 0.60f);
        float complementAmount = GetFloat(material, ComplementProperty, 0.20f);
        float accentSparsity = GetFloat(material, AccentSparsityProperty, 0.75f);
        float detailA = GetFloat(material, DetailAlbedoProperty, 1.00f);
        float detailN = GetFloat(material, DetailNormalProperty, 1.00f);
        float highlightStrength = GetFloat(material, HighlightProperty, 0.35f);
        float phaseSpeed = GetFloat(material, PhaseSpeedProperty, 0.15f);
        float hysteresis = GetFloat(material, HysteresisProperty, 0.80f);
        float minDot = GetFloat(material, MinDotProperty, 0.12f);

        if (modeValue < 0.5f)
        {
            return new PainterlyProxyEvaluation
            {
                foundationOccupancy = 0.82f,
                chromaOccupancy = 0.15f,
                complementOccupancy = 0.02f,
                highlightOccupancy = 0.01f,
                accentPlacementQuality = Mathf.Clamp01(0.75f + (hysteresis * 0.15f) + ((1f - phaseSpeed) * 0.10f)),
                meanColorErrorProxy = Mathf.Clamp01(0.25f + Mathf.Abs(0.5f - baseMuting) * 0.15f),
                temporalRoleFlipStability = Mathf.Clamp01((hysteresis * 0.6f) + ((1f - phaseSpeed) * 0.25f) + (minDot * 0.15f))
            };
        }

        float detailInfluence = Mathf.Clamp01((detailA + detailN) * 0.25f);
        float accentEnvelope = Mathf.Clamp01((complementAmount * 0.55f) + (highlightStrength * 0.45f));
        float sparseTarget = Mathf.Clamp01(1f - accentSparsity);
        float accentOccupancy = accentEnvelope * sparseTarget;

        float highlightOccupancy = Mathf.Clamp01(accentOccupancy * (0.35f + highlightStrength * 0.65f));
        float complementOccupancy = Mathf.Clamp01(accentOccupancy * (0.65f + detailInfluence * 0.35f));
        float remaining = Mathf.Clamp01(1f - highlightOccupancy - complementOccupancy);
        float chromaOccupancy = Mathf.Clamp01(remaining * (0.35f + Mathf.Clamp01(chromaPush * 0.5f) * 0.65f));
        float foundationOccupancy = Mathf.Clamp01(1f - chromaOccupancy - complementOccupancy - highlightOccupancy);

        float sum = foundationOccupancy + chromaOccupancy + complementOccupancy + highlightOccupancy;
        if (sum > MinWeightSumThreshold)
        {
            float inv = 1f / sum;
            foundationOccupancy *= inv;
            chromaOccupancy *= inv;
            complementOccupancy *= inv;
            highlightOccupancy *= inv;
        }

        float accentPlacementQuality = Mathf.Clamp01((accentSparsity * 0.60f) + (hysteresis * 0.25f) + ((1f - phaseSpeed) * 0.15f));
        // Proxy expectation baselines:
        // - Foundation generally carries around half the occupancy in balanced painterly setups.
        // - Chroma role tends to carry a slightly higher share than foundation.
        // - Base muting / complement amounts nudge those baselines with a small gain (0.10).
        float expectedBase = 0.45f + (baseMuting * 0.10f);
        float expectedChroma = 0.55f + (complementAmount * 0.10f);
        float meanColorError = Mathf.Clamp01(
            (Mathf.Abs(expectedBase - foundationOccupancy) * 0.45f) +
            (Mathf.Abs(expectedChroma - chromaOccupancy) * 0.35f) +
            ((1f - accentPlacementQuality) * 0.20f));
        float temporalStability = Mathf.Clamp01((hysteresis * 0.55f) + ((1f - phaseSpeed) * 0.30f) + (minDot * 0.15f));

        return new PainterlyProxyEvaluation
        {
            foundationOccupancy = foundationOccupancy,
            chromaOccupancy = chromaOccupancy,
            complementOccupancy = complementOccupancy,
            highlightOccupancy = highlightOccupancy,
            accentPlacementQuality = accentPlacementQuality,
            meanColorErrorProxy = meanColorError,
            temporalRoleFlipStability = temporalStability
        };
    }

    static List<Material> LoadMaterials(List<string> materialPaths, string contextLabel, List<string> warnings)
    {
        List<Material> materials = new List<Material>();
        if (materialPaths == null)
            return materials;

        for (int i = 0; i < materialPaths.Count; i++)
        {
            string path = materialPaths[i];
            if (string.IsNullOrEmpty(path))
                continue;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                warnings.Add($"Material not found ({contextLabel}): {path}");
                continue;
            }

            materials.Add(material);
        }

        return materials;
    }

    static void MergeMaterials(List<Material> destination, List<Material> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Material material = source[i];
            if (material == null || destination.Contains(material))
                continue;
            destination.Add(material);
        }
    }

    static string BuildFallbackProfileDescription()
    {
        return
            "Safety fallback profile (RoleComposed-safe): BaseMuting=0.52, ChromaPush=0.38, ComplementaryAccentAmount=0.10, AccentSparsity=0.90, DetailSensitivityA=0.75, DetailSensitivityN=0.70, HighlightAccent=0.15, PhaseSpeed=0.08, Hysteresis=0.90, MinDot=0.18. " +
            "Criteria: flat palette or noisy textures => reduce accents first; specular-heavy content => prefer LegacyQuantized.";
    }

    static PainterlySummary BuildSummary(List<PainterlySceneReport> scenes)
    {
        PainterlySummary summary = new PainterlySummary();
        summary.sceneCount = scenes.Count;

        float totalRoleStability = 0f;
        float totalLegacyStability = 0f;
        int count = 0;

        for (int i = 0; i < scenes.Count; i++)
        {
            PainterlySceneReport scene = scenes[i];
            PainterlyModeMetrics role = scene.modeMetrics.Find(m => string.Equals(m.modeName, ModeRoleComposed, StringComparison.Ordinal));
            PainterlyModeMetrics legacy = scene.modeMetrics.Find(m => string.Equals(m.modeName, ModeLegacy, StringComparison.Ordinal));
            if (role == null || legacy == null)
                continue;

            totalRoleStability += GetMetricValue(role, "temporal_role_flip_stability_frame_to_frame");
            totalLegacyStability += GetMetricValue(legacy, "temporal_role_flip_stability_frame_to_frame");
            count++;
        }

        if (count > 0)
        {
            summary.averageTemporalStabilityRoleComposed = totalRoleStability / count;
            summary.averageTemporalStabilityLegacyQuantized = totalLegacyStability / count;
            summary.temporalStabilityDeltaRoleMinusLegacy = summary.averageTemporalStabilityRoleComposed - summary.averageTemporalStabilityLegacyQuantized;
        }

        return summary;
    }

    static float GetMetricValue(PainterlyModeMetrics modeMetrics, string metricName)
    {
        for (int i = 0; i < modeMetrics.metrics.Count; i++)
        {
            PainterlyMetricValue metric = modeMetrics.metrics[i];
            if (string.Equals(metric.metricName, metricName, StringComparison.Ordinal))
                return metric.value;
        }

        return 0f;
    }

    static void WriteDeterministicReports(PainterlyValidationReport report)
    {
        File.WriteAllText(AssetPathToAbsolutePath(JsonPath), JsonUtility.ToJson(report, true), Encoding.UTF8);
        File.WriteAllText(AssetPathToAbsolutePath(CsvPath), BuildCsv(report), Encoding.UTF8);
        File.WriteAllText(AssetPathToAbsolutePath(TxtPath), BuildTextSummary(report), Encoding.UTF8);

        AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(CsvPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(TxtPath, ImportAssetOptions.ForceUpdate);
    }

    static string BuildCsv(PainterlyValidationReport report)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Scene,Category,Mode,Metric,Value,ExactVsProxy,Notes");

        for (int s = 0; s < report.scenes.Count; s++)
        {
            PainterlySceneReport scene = report.scenes[s];
            for (int m = 0; m < scene.modeMetrics.Count; m++)
            {
                PainterlyModeMetrics mode = scene.modeMetrics[m];
                for (int i = 0; i < mode.metrics.Count; i++)
                {
                    PainterlyMetricValue metric = mode.metrics[i];
                    builder.Append(CsvEscape(scene.sceneLabel)).Append(',');
                    builder.Append(CsvEscape(scene.category)).Append(',');
                    builder.Append(CsvEscape(mode.modeName)).Append(',');
                    builder.Append(CsvEscape(metric.metricName)).Append(',');
                    builder.Append(metric.value.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',');
                    builder.Append(CsvEscape(metric.exactVsProxy)).Append(',');
                    builder.Append(CsvEscape(metric.notes)).AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    static string BuildTextSummary(PainterlyValidationReport report)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Painterly Baseline Comparison Report");
        builder.AppendLine("GeneratedUtc: " + report.generatedUtc);
        builder.AppendLine("TuningOrder: " + report.tuningOrder);
        builder.AppendLine("FallbackProfile: " + report.fallbackProfile);
        builder.AppendLine();
        builder.AppendLine("Metric labels: ExactVsProxy is explicit per metric. Current workflow uses proxies.");
        builder.AppendLine();

        for (int s = 0; s < report.scenes.Count; s++)
        {
            PainterlySceneReport scene = report.scenes[s];
            builder.AppendLine($"Scene: {scene.sceneLabel} ({scene.category})");
            builder.AppendLine($"  Path: {scene.scenePath}");
            builder.AppendLine($"  Camera: {scene.cameraName}");
            for (int m = 0; m < scene.modeMetrics.Count; m++)
            {
                PainterlyModeMetrics mode = scene.modeMetrics[m];
                builder.AppendLine($"  Mode: {mode.modeName}");
                for (int i = 0; i < mode.metrics.Count; i++)
                {
                    PainterlyMetricValue metric = mode.metrics[i];
                    builder.AppendLine($"    {metric.metricName}: {metric.value.ToString("0.000", CultureInfo.InvariantCulture)} [{metric.exactVsProxy}]");
                }
            }

            for (int r = 0; r < scene.fallbackRecommendations.Count; r++)
            {
                PainterlyFallbackRecommendation recommendation = scene.fallbackRecommendations[r];
                builder.AppendLine($"  Fallback: {recommendation.materialName} -> {recommendation.action} ({recommendation.reason})");
            }

            builder.AppendLine();
        }

        if (report.warnings.Count > 0)
        {
            builder.AppendLine("Warnings:");
            for (int i = 0; i < report.warnings.Count; i++)
                builder.AppendLine("- " + report.warnings[i]);
        }

        return builder.ToString();
    }

    static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool shouldQuote = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        if (!shouldQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static float GetFloat(Material material, string propertyName, float fallback)
    {
        if (material == null || !material.HasProperty(propertyName))
            return fallback;
        return material.GetFloat(propertyName);
    }

    static void EnsureFolderExists(string folderPath)
    {
        folderPath = NormalizeAssetPath(folderPath).TrimEnd('/');
        if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
            return;

        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = NormalizeAssetPath(Path.GetDirectoryName(folderPath) ?? string.Empty);
        if (string.IsNullOrEmpty(parent))
            parent = "Assets";

        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
    }

    static string NormalizeAssetPath(string path)
    {
        return path.Replace('\\', '/');
    }

    static string AssetPathToAbsolutePath(string assetPath)
    {
        assetPath = NormalizeAssetPath(assetPath);
        if (assetPath == "Assets")
            return Application.dataPath;
        if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, assetPath);
    }

    static string SanitizeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Unnamed";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                builder.Append(c);
            else
                builder.Append('_');
        }

        return builder.ToString();
    }
}

[Serializable]
public class PainterlyValidationReport
{
    public string generatedUtc;
    public string toolVersion;
    public string tuningOrder;
    public string fallbackProfile;
    public PainterlySummary summary = new PainterlySummary();
    public List<PainterlySceneReport> scenes = new List<PainterlySceneReport>();
    public List<string> warnings = new List<string>();
}

[Serializable]
public class PainterlySummary
{
    public int sceneCount;
    public float averageTemporalStabilityLegacyQuantized;
    public float averageTemporalStabilityRoleComposed;
    public float temporalStabilityDeltaRoleMinusLegacy;
}

[Serializable]
public class PainterlySceneReport
{
    public string sceneLabel;
    public string category;
    public string scenePath;
    public string cameraName;
    public string notes;
    public string tuningOrder;
    public List<PainterlyModeMetrics> modeMetrics = new List<PainterlyModeMetrics>();
    public List<PainterlyMetricDelta> metricDeltas = new List<PainterlyMetricDelta>();
    public List<PainterlyMaterialSnapshot> materialSnapshots = new List<PainterlyMaterialSnapshot>();
    public List<PainterlyCaptureArtifact> captureArtifacts = new List<PainterlyCaptureArtifact>();
    public List<PainterlyFallbackRecommendation> fallbackRecommendations = new List<PainterlyFallbackRecommendation>();
}

[Serializable]
public class PainterlyModeMetrics
{
    public string sceneLabel;
    public string modeName;
    public string exactVsProxy;
    public List<PainterlyMetricValue> metrics = new List<PainterlyMetricValue>();
}

[Serializable]
public class PainterlyMetricValue
{
    public string metricName;
    public float value;
    public string exactVsProxy;
    public string notes;
}

[Serializable]
public class PainterlyMetricDelta
{
    public string metricName;
    public string exactVsProxy;
    public float legacyValue;
    public float roleComposedValue;
    public float deltaRoleMinusLegacy;
}

[Serializable]
public class PainterlyMaterialSnapshot
{
    public string materialName;
    public string assetPath;
    public float compositionMode;
    public float baseMuting;
    public float chromaPush;
    public float complementaryAccentAmount;
    public float accentSparsity;
    public float detailSensitivityAlbedo;
    public float detailSensitivityNormal;
    public float highlightAccentStrength;
    public float blueNoisePhaseSpeed;
    public float blueNoiseHysteresis;
    public float blueNoiseMinDot;
}

[Serializable]
public class PainterlyCaptureArtifact
{
    public string modeName;
    public string capturePath;
    public string exactVsProxy;
    public string notes;
}

[Serializable]
public class PainterlyFallbackRecommendation
{
    public string materialName;
    public string materialPath;
    public string action;
    public string reason;
}

public struct PainterlyProxyEvaluation
{
    public float foundationOccupancy;
    public float chromaOccupancy;
    public float complementOccupancy;
    public float highlightOccupancy;
    public float accentPlacementQuality;
    public float meanColorErrorProxy;
    public float temporalRoleFlipStability;
}
