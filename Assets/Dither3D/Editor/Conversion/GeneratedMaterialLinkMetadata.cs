using System;
using System.Text;
using UnityEditor;
using UnityEngine;

[Serializable]
public class GeneratedMaterialLinkMetadata
{
    public int schemaVersion;
    public string sourceMaterialAssetPath;
    public string styleProfileAssetPath;
    public string styleProfileDependencyHash;
    public string adapterRegistryAssetPath;
    public string adapterRegistryDependencyHash;
    public string generatedAtUtc;
    public string lastReappliedAtUtc;
}

public static class GeneratedMaterialLinkMetadataUtility
{
    public const int CurrentSchemaVersion = 1;
    const string UserDataPrefix = "Dither3DGeneratedMaterialLink:";

    public static GeneratedMaterialLinkMetadata CreateFor(Material sourceMaterial, DitherStyleProfile styleProfile)
    {
        ShaderAdapterRegistry registry = styleProfile != null ? styleProfile.ShaderAdapterRegistry : null;
        return new GeneratedMaterialLinkMetadata
        {
            schemaVersion = CurrentSchemaVersion,
            sourceMaterialAssetPath = sourceMaterial != null ? AssetDatabase.GetAssetPath(sourceMaterial) : string.Empty,
            styleProfileAssetPath = styleProfile != null ? AssetDatabase.GetAssetPath(styleProfile) : string.Empty,
            styleProfileDependencyHash = ComputeAssetDependencyHash(styleProfile),
            adapterRegistryAssetPath = registry != null ? AssetDatabase.GetAssetPath(registry) : string.Empty,
            adapterRegistryDependencyHash = ComputeAssetDependencyHash(registry),
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            lastReappliedAtUtc = string.Empty
        };
    }

    public static bool TryRead(Material generatedMaterial, out GeneratedMaterialLinkMetadata metadata, out string error)
    {
        metadata = null;
        error = string.Empty;
        if (generatedMaterial == null)
        {
            error = "Generated material is null.";
            return false;
        }

        string path = AssetDatabase.GetAssetPath(generatedMaterial);
        if (string.IsNullOrEmpty(path))
        {
            error = "Generated material is not a persisted asset.";
            return false;
        }

        return TryReadAtPath(path, out metadata, out error);
    }

    public static bool TryReadAtPath(string materialAssetPath, out GeneratedMaterialLinkMetadata metadata, out string error)
    {
        metadata = null;
        error = string.Empty;

        AssetImporter importer = AssetImporter.GetAtPath(materialAssetPath);
        if (importer == null)
        {
            error = "No importer found for '" + materialAssetPath + "'.";
            return false;
        }

        string payload;
        if (!TryExtractPayload(importer.userData, out payload))
        {
            error = "No generated-material link metadata found.";
            return false;
        }

        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            metadata = JsonUtility.FromJson<GeneratedMaterialLinkMetadata>(json);
            if (metadata == null)
            {
                error = "Failed to deserialize generated-material link metadata.";
                return false;
            }
        }
        catch (Exception exception)
        {
            error = "Failed to read generated-material link metadata: " + exception.Message;
            return false;
        }

        return true;
    }

    public static bool Write(Material generatedMaterial, GeneratedMaterialLinkMetadata metadata, out string error)
    {
        error = string.Empty;
        if (generatedMaterial == null)
        {
            error = "Generated material is null.";
            return false;
        }

        string path = AssetDatabase.GetAssetPath(generatedMaterial);
        if (string.IsNullOrEmpty(path))
        {
            error = "Generated material is not a persisted asset.";
            return false;
        }

        return WriteAtPath(path, metadata, out error);
    }

    public static bool WriteAtPath(string materialAssetPath, GeneratedMaterialLinkMetadata metadata, out string error)
    {
        error = string.Empty;
        if (metadata == null)
        {
            error = "Metadata is null.";
            return false;
        }

        AssetImporter importer = AssetImporter.GetAtPath(materialAssetPath);
        if (importer == null)
        {
            error = "No importer found for '" + materialAssetPath + "'.";
            return false;
        }

        metadata.schemaVersion = CurrentSchemaVersion;
        string json = JsonUtility.ToJson(metadata);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        importer.userData = UpsertPayload(importer.userData, payload);
        EditorUtility.SetDirty(importer);
        AssetDatabase.WriteImportSettingsIfDirty(materialAssetPath);
        return true;
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

    static bool TryExtractPayload(string userData, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrEmpty(userData))
            return false;

        string[] lines = userData.Split(new[] { '\n' }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!line.StartsWith(UserDataPrefix, StringComparison.Ordinal))
                continue;

            payload = line.Substring(UserDataPrefix.Length).Trim();
            return !string.IsNullOrEmpty(payload);
        }

        return false;
    }

    static string UpsertPayload(string userData, string payload)
    {
        string serializedLine = UserDataPrefix + payload;
        if (string.IsNullOrEmpty(userData))
            return serializedLine;

        string[] lines = userData.Split(new[] { '\n' }, StringSplitOptions.None);
        bool replaced = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(UserDataPrefix, StringComparison.Ordinal))
                continue;

            lines[i] = serializedLine;
            replaced = true;
        }

        if (replaced)
            return string.Join("\n", lines);

        return userData + "\n" + serializedLine;
    }
}
