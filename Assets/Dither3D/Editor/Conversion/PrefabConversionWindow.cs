using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class PrefabConversionWindow : EditorWindow
{
    [SerializeField] List<GameObject> selectedPrefabs = new List<GameObject>();
    [SerializeField] DitherStyleProfile styleProfile;
    [SerializeField] ShaderAdapterRegistry adapterRegistry;
    [SerializeField] string manifestOutputDirectory = "Assets/Dither3D/GeneratedConversionReports";
    [SerializeField] Vector2 prefabListScroll;
    [SerializeField] Vector2 detailsScroll;

    RunSummary lastSummary;
    string lastManifestAssetPath;
    string lastManifestJson;

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

        styleProfile = (DitherStyleProfile)EditorGUILayout.ObjectField("Style Profile", styleProfile, typeof(DitherStyleProfile), false);
        adapterRegistry = (ShaderAdapterRegistry)EditorGUILayout.ObjectField("Adapter Registry", adapterRegistry, typeof(ShaderAdapterRegistry), false);
        manifestOutputDirectory = EditorGUILayout.TextField(
            new GUIContent("Manifest Output", "Asset folder used to save conversion manifests on real convert."),
            manifestOutputDirectory);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Dry Run", GUILayout.Height(26f)))
                RunConversion(true);

            if (GUILayout.Button("Convert", GUILayout.Height(26f)))
                RunConversion(false);
        }

        EditorGUILayout.Space();
        DrawSummaryPanel();
    }

    void DrawSelectionSection()
    {
        EditorGUILayout.LabelField("Selected Prefabs", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected Prefabs"))
                AddSelectedPrefabs();

            if (GUILayout.Button("Clear"))
                selectedPrefabs.Clear();
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
                    selectedPrefabs[i] = (GameObject)EditorGUILayout.ObjectField(
                        "Prefab " + (i + 1),
                        selectedPrefabs[i],
                        typeof(GameObject),
                        false);

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        selectedPrefabs.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
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
        manifest.dryRun = dryRun;
        manifest.generatedAtUtc = DateTime.UtcNow.ToString("o");
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
            AppendManifestEntries(prefab, result, manifest);
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
            string fileName = "ConversionManifest_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
            lastManifestAssetPath = NormalizePath(Path.Combine(folder, fileName));
            File.WriteAllText(AssetPathToAbsolutePath(lastManifestAssetPath), lastManifestJson);
            AssetDatabase.ImportAsset(lastManifestAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        lastSummary = new RunSummary(prefabs.Count, successfulPrefabs, warningCount, errorCount, messages);
    }

    void AppendManifestEntries(GameObject sourcePrefab, PrefabVariantBuildResult result, ConversionManifest manifest)
    {
        string now = DateTime.UtcNow.ToString("o");
        string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
        var prefabEntry = new ConversionManifestEntry();
        prefabEntry.entryType = "prefab";
        prefabEntry.source = sourcePath;
        prefabEntry.output = result.VariantAssetPath;
        prefabEntry.adapterUsed = "StyleProfile:" + manifest.styleProfile;
        prefabEntry.context = sourcePrefab.name;
        prefabEntry.timestampUtc = now;
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
            entry.timestampUtc = now;
            manifest.entries.Add(entry);
        }

        for (int i = 0; i < result.SkippedSlots.Count; i++)
        {
            PrefabMaterialSkip skip = result.SkippedSlots[i];
            var entry = new ConversionManifestEntry();
            entry.entryType = "skipped";
            entry.source = skip.SourceMaterialPath;
            entry.output = string.Empty;
            entry.adapterUsed = "N/A";
            entry.context = sourcePath + "::" + skip.RendererPath + " [slot " + skip.SlotIndex + "]";
            entry.timestampUtc = now;
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

        string profileName = styleProfile != null ? styleProfile.ProfileName : "AdHocProfile";
        string notes = styleProfile != null ? styleProfile.Notes : string.Empty;
        DitherStyleProfile runtime = DitherStyleProfile.CreateRuntimeProfile(profileName, adapterRegistry, notes);
        runtime.name = "Runtime_" + profileName;
        return runtime;
    }

    void AddSelectedPrefabs()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
        for (int i = 0; i < selected.Length; i++)
        {
            GameObject go = selected[i] as GameObject;
            if (go == null || PrefabUtility.GetPrefabAssetType(go) == PrefabAssetType.NotAPrefab)
                continue;

            if (!selectedPrefabs.Contains(go))
                selectedPrefabs.Add(go);
        }
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
