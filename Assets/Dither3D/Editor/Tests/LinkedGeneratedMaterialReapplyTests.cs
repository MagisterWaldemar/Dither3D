using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class LinkedGeneratedMaterialReapplyTests
{
    const string TempRoot = "Assets/Dither3D/Editor/Tests/__GeneratedReapply";
    const string TempMaterialDirectory = TempRoot + "/GeneratedMaterials";

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TempRoot);
    }

    [Test]
    public void ConvertAndPersist_WritesGeneratedMaterialLinkMetadata()
    {
        CreateFolder(TempRoot);
        Material source = CreateMaterialAsset(TempRoot + "/Source.mat", "Standard");
        DitherStyleProfile profile = CreatePersistedStyleProfile(TempRoot + "/Profile.asset", CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.ConvertAndPersist(source, profile, TempMaterialDirectory);

        Assert.That(result.Success, Is.True);
        Material generated = AssetDatabase.LoadAssetAtPath<Material>(result.OutputAssetPath);
        Assert.That(generated, Is.Not.Null);

        GeneratedMaterialLinkMetadata metadata;
        string error;
        bool linked = GeneratedMaterialLinkMetadataUtility.TryRead(generated, out metadata, out error);
        Assert.That(linked, Is.True, error);
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.sourceMaterialAssetPath, Is.EqualTo(AssetDatabase.GetAssetPath(source)));
        Assert.That(metadata.styleProfileAssetPath, Is.EqualTo(AssetDatabase.GetAssetPath(profile)));
        Assert.That(string.IsNullOrEmpty(metadata.styleProfileDependencyHash), Is.False);
    }

    [Test]
    public void Reapply_RespectsPolicy_ForMappedProperties()
    {
        CreateFolder(TempRoot);
        Material source = CreateMaterialAsset(TempRoot + "/SourceMapped.mat", "Standard");
        source.SetFloat("_Glossiness", 0.2f);

        DitherStyleProfile profile = CreatePersistedStyleProfile(
            TempRoot + "/ProfileMapped.asset",
            CreateRule("_Glossiness", "_InputExposure", PropertyRemapRuleKind.DirectCopy));

        MaterialConverter converter = new MaterialConverter();
        ConversionResult initial = converter.ConvertAndPersist(source, profile, TempMaterialDirectory);
        Assert.That(initial.Success, Is.True);

        Material generated = AssetDatabase.LoadAssetAtPath<Material>(initial.OutputAssetPath);
        Assert.That(generated, Is.Not.Null);
        generated.SetFloat("_InputExposure", 0.95f);
        EditorUtility.SetDirty(generated);
        AssetDatabase.SaveAssets();

        source.SetFloat("_Glossiness", 0.1f);
        EditorUtility.SetDirty(source);
        AssetDatabase.SaveAssets();

        LinkedGeneratedMaterialReapplySummary styleOnlySummary =
            LinkedGeneratedMaterialReapplyUtility.Reapply(profile, LinkedGeneratedMaterialUpdatePolicy.StyleParametersOnly);
        Assert.That(styleOnlySummary.failedMaterials, Is.EqualTo(0));

        generated = AssetDatabase.LoadAssetAtPath<Material>(initial.OutputAssetPath);
        Assert.That(generated.GetFloat("_InputExposure"), Is.EqualTo(0.95f).Within(0.0001f));

        LinkedGeneratedMaterialReapplySummary allMappedSummary =
            LinkedGeneratedMaterialReapplyUtility.Reapply(profile, LinkedGeneratedMaterialUpdatePolicy.AllMappedParameters);
        Assert.That(allMappedSummary.failedMaterials, Is.EqualTo(0));
        Assert.That(allMappedSummary.updatedMaterials, Is.GreaterThan(0));

        generated = AssetDatabase.LoadAssetAtPath<Material>(initial.OutputAssetPath);
        Assert.That(generated.GetFloat("_InputExposure"), Is.EqualTo(0.1f).Within(0.0001f));
    }

    static Material CreateMaterialAsset(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Assert.That(shader, Is.Not.Null, "Shader '" + shaderName + "' must exist.");
        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static DitherStyleProfile CreatePersistedStyleProfile(string profilePath, params PropertyRemapRule[] rules)
    {
        ShaderAdapterMapping mapping = new ShaderAdapterMapping();
        SetPrivateField(mapping, "sourceShaderName", "Standard");
        SetPrivateField(mapping, "targetShader", FindTargetShader());
        SetPrivateField(mapping, "propertyRemapRules", new System.Collections.Generic.List<PropertyRemapRule>(rules));

        ShaderAdapterRegistry registry = ScriptableObject.CreateInstance<ShaderAdapterRegistry>();
        SetPrivateField(registry, "shaderMappings", new System.Collections.Generic.List<ShaderAdapterMapping> { mapping });
        string registryPath = profilePath.Replace(".asset", "_Registry.asset");
        AssetDatabase.CreateAsset(registry, registryPath);
        registry = AssetDatabase.LoadAssetAtPath<ShaderAdapterRegistry>(registryPath);

        DitherStyleProfile profile = ScriptableObject.CreateInstance<DitherStyleProfile>();
        SetPrivateField(profile, "profileName", "ReapplyTestStyle");
        SetPrivateField(profile, "shaderAdapterRegistry", registry);
        AssetDatabase.CreateAsset(profile, profilePath);
        return AssetDatabase.LoadAssetAtPath<DitherStyleProfile>(profilePath);
    }

    static Shader FindTargetShader()
    {
        Shader shader = Shader.Find("Dither 3D/Opaque");
        Assert.That(shader, Is.Not.Null, "Dither 3D/Opaque shader must exist.");
        return shader;
    }

    static PropertyRemapRule CreateRule(string source, string target, PropertyRemapRuleKind kind)
    {
        PropertyRemapRule rule = new PropertyRemapRule();
        SetPrivateField(rule, "sourcePropertyName", source);
        SetPrivateField(rule, "targetPropertyName", target);
        SetPrivateField(rule, "ruleKind", kind);
        return rule;
    }

    static void CreateFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static void SetPrivateField<TObject, TValue>(TObject instance, string fieldName, TValue value)
    {
        FieldInfo field = typeof(TObject).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field " + fieldName + " on " + typeof(TObject).Name);
        field.SetValue(instance, value);
    }
}
