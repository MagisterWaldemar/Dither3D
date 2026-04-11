using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class PrefabConversionWindow : EditorWindow
{
    const string UnmappedPropertyWarningToken = "Unmapped source property";
    const float PreviewPrimaryLightIntensity = 1.2f;
    const float PreviewSecondaryLightIntensity = 1f;
    static readonly Vector3 PreviewPrimaryLightEuler = new Vector3(40f, 40f, 0f);
    const float PreviewMinRadius = 0.5f;
    const float PreviewDistanceMultiplier = 2.8f;
    const float PreviewHeightOffsetMultiplier = 0.35f;
    static readonly Color PreviewPanelBackgroundColor = new Color(0.11f, 0.11f, 0.11f, 1f);
    static readonly Color PreviewCameraBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [SerializeField] List<GameObject> selectedPrefabs = new List<GameObject>();
    [SerializeField] DitherStyleProfile styleProfile;
    [SerializeField] ShaderAdapterRegistry adapterRegistry;
    [SerializeField] string manifestOutputDirectory = "Assets/Dither3D/GeneratedConversionReports";
    [SerializeField] Vector2 prefabListScroll;
    [SerializeField] Vector2 detailsScroll;
    [SerializeField] int previewPrefabIndex;
    [SerializeField] PreviewDisplayMode previewDisplayMode = PreviewDisplayMode.SideBySide;
    [SerializeField] bool autoRefreshPreview = true;
    [SerializeField] Vector2 previewWarningScroll;

    RunSummary lastSummary;
    string lastManifestAssetPath;
    string lastManifestJson;
    PreviewRenderUtility sourcePreviewUtility;
    PreviewRenderUtility convertedPreviewUtility;
    GameObject sourcePreviewInstance;
    GameObject convertedPreviewInstance;
    PrefabVariantBuildResult previewResult;
    bool previewDirty = true;
    string previewError;
    Hash128 styleProfileDependencyHash;
    Hash128 adapterRegistryDependencyHash;

    enum PreviewDisplayMode
    {
        SideBySide,
        SourceOnly,
        ConvertedOnly
    }

    [MenuItem("Tools/Dither 3D/Prefab Conversion")]
    static void OpenWindow()
    {
        var window = GetWindow<PrefabConversionWindow>("Prefab Conversion");
        window.minSize = new Vector2(500f, 520f);
        window.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab Conversion", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawSelectionSection();
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        styleProfile = (DitherStyleProfile)EditorGUILayout.ObjectField("Style Profile", styleProfile, typeof(DitherStyleProfile), false);
        adapterRegistry = (ShaderAdapterRegistry)EditorGUILayout.ObjectField("Adapter Registry", adapterRegistry, typeof(ShaderAdapterRegistry), false);
        manifestOutputDirectory = EditorGUILayout.TextField(
            new GUIContent("Manifest Output", "Asset folder used to save conversion manifests on real convert."),
            manifestOutputDirectory);
        if (EditorGUI.EndChangeCheck())
            MarkPreviewDirty();

        MonitorPreviewDependencyChanges();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Dry Run", GUILayout.Height(26f)))
                RunConversion(true);

            if (GUILayout.Button("Convert", GUILayout.Height(26f)))
                RunConversion(false);
        }

        EditorGUILayout.Space();
        DrawPreviewPanel();
        EditorGUILayout.Space();
        DrawSummaryPanel();
    }

    void OnDisable()
    {
        DisposePreviewResources();
    }

    void DrawSelectionSection()
    {
        EditorGUILayout.LabelField("Selected Prefabs", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected Prefabs"))
                AddSelectedPrefabs();

            if (GUILayout.Button("Clear"))
            {
                selectedPrefabs.Clear();
                previewPrefabIndex = 0;
                MarkPreviewDirty();
            }
        }

        prefabListScroll = EditorGUILayout.BeginScrollView(prefabListScroll, GUILayout.Height(170f));
        if (selectedPrefabs.Count == 0)
        {
            EditorGUILayout.HelpBox("Add one or more prefab assets to convert.", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < selectedPrefabs.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    selectedPrefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                        "Prefab " + (i + 1),
                        selectedPrefabs[i],
                        typeof(GameObject),
                        false);
                    if (EditorGUI.EndChangeCheck())
                        MarkPreviewDirty();

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        selectedPrefabs.RemoveAt(i);
                        if (previewPrefabIndex >= selectedPrefabs.Count)
                            previewPrefabIndex = Mathf.Max(0, selectedPrefabs.Count - 1);
                        MarkPreviewDirty();
                        i--;
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawPreviewPanel()
    {
        EditorGUILayout.LabelField("Prefab Preview", EditorStyles.boldLabel);

        List<GameObject> prefabs = CollectPrefabInputs();
        if (prefabs.Count == 0)
        {
            DisposePreviewResources();
            EditorGUILayout.HelpBox("Select at least one prefab to enable side-by-side preview.", MessageType.Info);
            return;
        }

        previewPrefabIndex = Mathf.Clamp(previewPrefabIndex, 0, prefabs.Count - 1);
        EditorGUI.BeginChangeCheck();
        string[] names = new string[prefabs.Count];
        for (int i = 0; i < prefabs.Count; i++)
            names[i] = prefabs[i] != null ? prefabs[i].name : "(Missing)";
        previewPrefabIndex = EditorGUILayout.Popup("Preview Prefab", previewPrefabIndex, names);
        previewDisplayMode = (PreviewDisplayMode)EditorGUILayout.EnumPopup("Display", previewDisplayMode);
        autoRefreshPreview = EditorGUILayout.Toggle(new GUIContent("Auto Refresh", "Rebuild converted preview when profile/registry changes."), autoRefreshPreview);
        if (EditorGUI.EndChangeCheck())
            MarkPreviewDirty();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Preview", GUILayout.Width(140f)))
                MarkPreviewDirty();
        }

        EnsurePreviewBuilt(prefabs[previewPrefabIndex]);

        if (!string.IsNullOrEmpty(previewError))
        {
            EditorGUILayout.HelpBox(previewError, MessageType.Error);
            return;
        }

        if (previewResult != null && previewResult.Warnings.Count > 0)
        {
            var unmappedWarnings = new List<string>();
            for (int i = 0; i < previewResult.Warnings.Count; i++)
            {
                string warning = previewResult.Warnings[i];
                if (!string.IsNullOrEmpty(warning) && warning.Contains(UnmappedPropertyWarningToken))
                    unmappedWarnings.Add(warning);
            }

            if (unmappedWarnings.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Unmapped properties detected (" + unmappedWarnings.Count + "). Converted preview may differ from source where no remap rule exists.",
                    MessageType.Warning);
                previewWarningScroll = EditorGUILayout.BeginScrollView(previewWarningScroll, GUILayout.Height(70f));
                for (int i = 0; i < unmappedWarnings.Count; i++)
                    EditorGUILayout.LabelField("- " + unmappedWarnings[i], EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        Rect previewRect = GUILayoutUtility.GetRect(10f, 210f, GUILayout.ExpandWidth(true));
        if (previewDisplayMode == PreviewDisplayMode.SideBySide)
        {
            Rect left = new Rect(previewRect.x, previewRect.y, (previewRect.width - 6f) * 0.5f, previewRect.height);
            Rect right = new Rect(left.xMax + 6f, previewRect.y, left.width, previewRect.height);
            DrawPreviewCell(left, sourcePreviewUtility, sourcePreviewInstance, "Source");
            DrawPreviewCell(right, convertedPreviewUtility, convertedPreviewInstance, "Converted");
        }
        else if (previewDisplayMode == PreviewDisplayMode.SourceOnly)
        {
            DrawPreviewCell(previewRect, sourcePreviewUtility, sourcePreviewInstance, "Source");
        }
        else
        {
            DrawPreviewCell(previewRect, convertedPreviewUtility, convertedPreviewInstance, "Converted");
        }
    }

    void DrawSummaryPanel()
    {
        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        if (lastSummary == null)
        {
            EditorGUILayout.HelpBox("Run Dry Run or Convert to generate a summary.", MessageType.None);
            return;
        }

        MessageType summaryType = lastSummary.errorCount > 0 ? MessageType.Error : (lastSummary.warningCount > 0 ? MessageType.Warning : MessageType.Info);
        EditorGUILayout.HelpBox(
            "Prefabs: " + lastSummary.totalPrefabs +
            " | Success: " + lastSummary.successCount +
            " | Warnings: " + lastSummary.warningCount +
            " | Errors: " + lastSummary.errorCount,
            summaryType);

        if (!string.IsNullOrEmpty(lastManifestAssetPath))
        {
            EditorGUILayout.LabelField("Manifest Asset", lastManifestAssetPath);
            if (GUILayout.Button("Ping Manifest Asset", GUILayout.Width(140f)))
            {
                UnityEngine.Object manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(lastManifestAssetPath);
                if (manifestAsset != null)
                    EditorGUIUtility.PingObject(manifestAsset);
            }
        }
        else if (!string.IsNullOrEmpty(lastManifestJson))
        {
            EditorGUILayout.HelpBox("Dry Run generated in-memory JSON manifest preview (not written to disk).", MessageType.Info);
        }

        detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll, GUILayout.Height(190f));
        if (lastSummary.messages.Count > 0)
        {
            for (int i = 0; i < lastSummary.messages.Count; i++)
                EditorGUILayout.LabelField("- " + lastSummary.messages[i], EditorStyles.wordWrappedLabel);
        }
        else
        {
            EditorGUILayout.LabelField("No warnings or errors.");
        }

        EditorGUILayout.EndScrollView();
    }

    void RunConversion(bool dryRun)
    {
        List<GameObject> prefabs = CollectPrefabInputs();
        if (prefabs.Count == 0)
        {
            lastSummary = RunSummary.SingleError("No prefab assets selected.");
            return;
        }

        DitherStyleProfile effectiveProfile = ResolveEffectiveProfile();
        if (effectiveProfile == null)
        {
            lastSummary = RunSummary.SingleError("Provide a style profile and/or adapter registry.");
            return;
        }

        List<string> validationMessages = ShaderAdapterRegistryValidationUtility.Validate(effectiveProfile);
        if (validationMessages.Count > 0)
        {
            lastSummary = RunSummary.FromMessages(prefabs.Count, 0, validationMessages, true);
            return;
        }

        var builder = new PrefabVariantBuilder();
        var manifest = new ConversionManifest();
        DateTime runTimestampUtc = DateTime.UtcNow;
        manifest.dryRun = dryRun;
        manifest.generatedAtUtc = runTimestampUtc.ToString("o");
        manifest.styleProfile = effectiveProfile.name;
        manifest.styleProfileAssetPath = AssetDatabase.GetAssetPath(styleProfile);
        ShaderAdapterRegistry effectiveRegistry = effectiveProfile.ShaderAdapterRegistry;
        manifest.adapterRegistry = effectiveRegistry != null ? effectiveRegistry.name : string.Empty;
        manifest.adapterRegistryAssetPath = AssetDatabase.GetAssetPath(effectiveRegistry);

        var messages = new List<string>();
        int successfulPrefabs = 0;
        int warningCount = 0;
        int errorCount = 0;

        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            string sourcePath = AssetDatabase.GetAssetPath(prefab);
            PrefabVariantBuildResult result = dryRun
                ? builder.BuildVariantDryRun(sourcePath, effectiveProfile)
                : builder.BuildVariant(sourcePath, effectiveProfile);

            if (result.Success)
                successfulPrefabs++;

            if (result.Warnings.Count > 0)
                manifest.summary.prefabsWithWarnings++;

            if (result.Errors.Count > 0)
                manifest.summary.prefabsWithErrors++;

            warningCount += result.Warnings.Count;
            errorCount += result.Errors.Count;
            manifest.summary.totalReplacements += result.Replacements.Count;
            manifest.summary.totalSkippedSlots += result.SkippedSlots.Count;

            AppendResultMessages(prefab.name, result, messages);
            AppendManifestEntries(prefab, result, manifest, manifest.generatedAtUtc);
        }

        manifest.summary.totalPrefabs = prefabs.Count;
        manifest.summary.successfulPrefabs = successfulPrefabs;
        lastManifestJson = JsonUtility.ToJson(manifest, true);

        if (dryRun)
        {
            lastManifestAssetPath = string.Empty;
        }
        else
        {
            string folder = NormalizePath(manifestOutputDirectory);
            if (string.IsNullOrEmpty(folder))
                folder = "Assets";

            EnsureFolderExists(folder);
            string fileName = "ConversionManifest_" + runTimestampUtc.ToString("yyyyMMdd_HHmmss") + ".json";
            lastManifestAssetPath = NormalizePath(Path.Combine(folder, fileName));
            File.WriteAllText(AssetPathToAbsolutePath(lastManifestAssetPath), lastManifestJson);
            AssetDatabase.ImportAsset(lastManifestAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        lastSummary = new RunSummary(prefabs.Count, successfulPrefabs, warningCount, errorCount, messages);
    }

    void EnsurePreviewBuilt(GameObject previewPrefab)
    {
        if (previewPrefab == null)
        {
            previewError = "Preview prefab is null.";
            return;
        }

        if (!previewDirty)
            return;

        previewDirty = false;
        previewError = string.Empty;
        DisposePreviewResources();

        DitherStyleProfile effectiveProfile = ResolveEffectiveProfile();
        if (effectiveProfile == null)
        {
            previewError = "Preview unavailable: assign a Style Profile and/or Adapter Registry.";
            return;
        }

        List<string> validationMessages = ShaderAdapterRegistryValidationUtility.Validate(effectiveProfile);
        if (validationMessages.Count > 0)
        {
            previewError = "Preview unavailable: adapter validation failed. " + string.Join(" | ", validationMessages);
            return;
        }

        try
        {
            var builder = new PrefabVariantBuilder();
            previewResult = builder.BuildVariantPreview(previewPrefab, effectiveProfile, out convertedPreviewInstance);
            if (previewResult == null || !previewResult.Success || convertedPreviewInstance == null)
            {
                previewError = BuildActionablePreviewErrorMessage(previewResult);
                DisposePreviewResources();
                return;
            }

            sourcePreviewInstance = PrefabUtility.InstantiatePrefab(previewPrefab) as GameObject;
            if (sourcePreviewInstance == null)
                sourcePreviewInstance = Instantiate(previewPrefab);

            if (sourcePreviewInstance == null)
            {
                previewError = "Preview unavailable: failed to instantiate source prefab. Try reimporting the prefab and refresh preview.";
                DisposePreviewResources();
                return;
            }

            sourcePreviewInstance.hideFlags = HideFlags.HideAndDontSave;
            PrefabVariantBuilder.ApplyHideFlagsRecursively(sourcePreviewInstance.transform, HideFlags.HideAndDontSave);
            sourcePreviewUtility = new PreviewRenderUtility();
            sourcePreviewUtility.AddSingleGO(sourcePreviewInstance);

            convertedPreviewUtility = new PreviewRenderUtility();
            convertedPreviewUtility.AddSingleGO(convertedPreviewInstance);
        }
        catch (Exception exception)
        {
            previewError = "Preview conversion failed: " + exception.Message + " (check adapter mappings and target shader properties, then click Refresh Preview).";
            DisposePreviewResources();
        }
    }

    void AppendManifestEntries(GameObject sourcePrefab, PrefabVariantBuildResult result, ConversionManifest manifest, string entryTimestampUtc)
    {
        string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
        var prefabEntry = new ConversionManifestEntry();
        prefabEntry.entryType = "prefab";
        prefabEntry.source = sourcePath;
        prefabEntry.output = result.VariantAssetPath;
        prefabEntry.adapterUsed = "StyleProfile:" + manifest.styleProfile;
        prefabEntry.context = sourcePrefab.name;
        prefabEntry.timestampUtc = entryTimestampUtc;
        prefabEntry.warnings.AddRange(result.Warnings);
        prefabEntry.errors.AddRange(result.Errors);
        manifest.entries.Add(prefabEntry);

        for (int i = 0; i < result.Replacements.Count; i++)
        {
            PrefabMaterialReplacement replacement = result.Replacements[i];
            var entry = new ConversionManifestEntry();
            entry.entryType = "replacement";
            entry.source = replacement.SourceMaterialPath;
            entry.output = replacement.ConvertedMaterialPath;
            entry.adapterUsed = string.IsNullOrEmpty(replacement.AdapterUsed) ? "N/A" : replacement.AdapterUsed;
            entry.context = sourcePath + "::" + replacement.RendererPath + " [slot " + replacement.SlotIndex + "]";
            entry.timestampUtc = entryTimestampUtc;
            manifest.entries.Add(entry);
        }

        for (int i = 0; i < result.SkippedSlots.Count; i++)
        {
            PrefabMaterialSkip skip = result.SkippedSlots[i];
            var entry = new ConversionManifestEntry();
            entry.entryType = "skipped";
            entry.source = string.IsNullOrEmpty(skip.SourceMaterialPath) ? "N/A" : skip.SourceMaterialPath;
            entry.output = string.Empty;
            entry.adapterUsed = "N/A";
            entry.context = sourcePath + "::" + skip.RendererPath + " [slot " + skip.SlotIndex + "]";
            entry.timestampUtc = entryTimestampUtc;
            entry.warnings.Add(skip.Reason);
            manifest.entries.Add(entry);
        }
    }

    static void AppendResultMessages(string prefabName, PrefabVariantBuildResult result, List<string> messages)
    {
        for (int i = 0; i < result.Warnings.Count; i++)
            messages.Add(prefabName + " warning: " + result.Warnings[i]);

        for (int i = 0; i < result.Errors.Count; i++)
            messages.Add(prefabName + " error: " + result.Errors[i]);

        for (int i = 0; i < result.SkippedSlots.Count; i++)
            messages.Add(prefabName + " skipped slot: " + result.SkippedSlots[i].Reason);
    }

    List<GameObject> CollectPrefabInputs()
    {
        var prefabs = new List<GameObject>();
        for (int i = 0; i < selectedPrefabs.Count; i++)
        {
            GameObject prefab = selectedPrefabs[i];
            if (prefab == null)
                continue;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
                continue;

            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
                continue;

            if (!prefabs.Contains(prefab))
                prefabs.Add(prefab);
        }

        return prefabs;
    }

    DitherStyleProfile ResolveEffectiveProfile()
    {
        if (styleProfile == null && adapterRegistry == null)
            return null;

        if (styleProfile != null && adapterRegistry == null)
            return styleProfile;

        string runtimeProfileName = styleProfile != null ? styleProfile.ProfileName : "AdHocProfile";
        string notes = styleProfile != null ? styleProfile.Notes : string.Empty;
        DitherStyleProfile runtime = DitherStyleProfile.CreateRuntimeProfile(runtimeProfileName, adapterRegistry, notes);
        runtime.name = "Runtime_" + runtimeProfileName;
        return runtime;
    }

    void AddSelectedPrefabs()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
        bool changed = false;
        for (int i = 0; i < selected.Length; i++)
        {
            GameObject go = selected[i] as GameObject;
            if (go == null || PrefabUtility.GetPrefabAssetType(go) == PrefabAssetType.NotAPrefab)
                continue;

            if (!selectedPrefabs.Contains(go))
            {
                selectedPrefabs.Add(go);
                changed = true;
            }
        }

        if (changed)
            MarkPreviewDirty();
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

    static string AssetPathToAbsolutePath(string assetPath)
    {
        assetPath = NormalizePath(assetPath);
        if (assetPath == "Assets")
            return Application.dataPath;

        if (assetPath.StartsWith("Assets/"))
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, assetPath);
    }

    void MonitorPreviewDependencyChanges()
    {
        Hash128 styleHash = ComputeAssetDependencyHash(styleProfile);
        ShaderAdapterRegistry activeRegistry = adapterRegistry;
        if (activeRegistry == null && styleProfile != null)
            activeRegistry = styleProfile.ShaderAdapterRegistry;

        Hash128 registryHash = ComputeAssetDependencyHash(activeRegistry);

        if (styleHash != styleProfileDependencyHash || registryHash != adapterRegistryDependencyHash)
        {
            styleProfileDependencyHash = styleHash;
            adapterRegistryDependencyHash = registryHash;
            if (autoRefreshPreview)
                MarkPreviewDirty();
        }
    }

    static Hash128 ComputeAssetDependencyHash(UnityEngine.Object asset)
    {
        if (asset == null)
            return default(Hash128);

        string path = AssetDatabase.GetAssetPath(asset);
        return string.IsNullOrEmpty(path) ? default(Hash128) : AssetDatabase.GetAssetDependencyHash(path);
    }

    void DrawPreviewCell(Rect rect, PreviewRenderUtility previewUtility, GameObject previewInstance, string label)
    {
        EditorGUI.DrawRect(rect, PreviewPanelBackgroundColor);
        GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 18f), label, EditorStyles.miniBoldLabel);

        if (previewUtility == null || previewInstance == null)
            return;

        Rect renderRect = new Rect(rect.x + 2f, rect.y + 22f, rect.width - 4f, rect.height - 24f);
        Bounds bounds = CalculateBounds(previewInstance);
        Camera camera = previewUtility.camera;
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = PreviewCameraBackgroundColor;
        ConfigurePreviewCamera(camera, bounds);

        previewUtility.lights[0].intensity = PreviewPrimaryLightIntensity;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(PreviewPrimaryLightEuler);
        previewUtility.lights[1].intensity = PreviewSecondaryLightIntensity;

        previewUtility.BeginPreview(renderRect, GUIStyle.none);
        camera.Render();
        Texture texture = previewUtility.EndPreview();
        GUI.DrawTexture(renderRect, texture, ScaleMode.StretchToFill, false);
    }

    static void ConfigurePreviewCamera(Camera camera, Bounds bounds)
    {
        Vector3 center = bounds.center;
        float radius = Mathf.Max(PreviewMinRadius, bounds.extents.magnitude);
        float distance = radius * PreviewDistanceMultiplier;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
        camera.transform.position = center + new Vector3(0f, radius * PreviewHeightOffsetMultiplier, -distance);
        camera.transform.LookAt(center);
    }

    static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void MarkPreviewDirty()
    {
        previewDirty = true;
    }

    void DisposePreviewResources()
    {
        DisposePreviewUtility(ref sourcePreviewUtility);
        DisposePreviewUtility(ref convertedPreviewUtility);
        DisposePreviewObject(ref sourcePreviewInstance);
        DisposePreviewObject(ref convertedPreviewInstance);

        if (previewResult != null && previewResult.TemporaryMaterials.Count > 0)
        {
            for (int i = 0; i < previewResult.TemporaryMaterials.Count; i++)
            {
                Material material = previewResult.TemporaryMaterials[i];
                if (material != null)
                    DestroyImmediate(material);
            }
        }

        previewResult = null;
    }

    static void DisposePreviewUtility(ref PreviewRenderUtility previewUtility)
    {
        if (previewUtility == null)
            return;

        previewUtility.Cleanup();
        previewUtility = null;
    }

    static void DisposePreviewObject(ref GameObject instance)
    {
        if (instance == null)
            return;

        DestroyImmediate(instance);
        instance = null;
    }

    static string BuildActionablePreviewErrorMessage(PrefabVariantBuildResult result)
    {
        if (result == null)
            return "Preview conversion failed: no result returned. Verify style profile and adapter registry, then refresh preview.";

        if (result.Errors.Count == 0)
            return "Preview conversion failed without explicit errors. Verify shader adapter mappings and target shader property names, then refresh preview.";

        return "Preview conversion failed: " + string.Join(" | ", result.Errors) +
               " (Check that the source shader has a mapping in the active adapter registry and required target properties exist.)";
    }

    class RunSummary
    {
        public readonly int totalPrefabs;
        public readonly int successCount;
        public readonly int warningCount;
        public readonly int errorCount;
        public readonly List<string> messages;

        public RunSummary(int totalPrefabs, int successCount, int warningCount, int errorCount, List<string> messages)
        {
            this.totalPrefabs = totalPrefabs;
            this.successCount = successCount;
            this.warningCount = warningCount;
            this.errorCount = errorCount;
            this.messages = messages ?? new List<string>();
        }

        public static RunSummary SingleError(string message)
        {
            return new RunSummary(0, 0, 0, 1, new List<string> { message });
        }

        public static RunSummary FromMessages(int totalPrefabs, int successfulPrefabs, List<string> sourceMessages, bool asError)
        {
            return new RunSummary(
                totalPrefabs,
                successfulPrefabs,
                asError ? 0 : sourceMessages.Count,
                asError ? sourceMessages.Count : 0,
                sourceMessages);
        }
    }
}
