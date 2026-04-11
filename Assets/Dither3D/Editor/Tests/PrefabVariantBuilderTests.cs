using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PrefabVariantBuilderTests
{
    const string TempRoot = "Assets/Dither3D/Editor/Tests/__GeneratedPrefabVariants";
    const string TempMaterialDirectory = TempRoot + "/ConvertedMaterials";
    const string TempVariantDirectory = TempRoot + "/Variants";

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TempRoot);
    }

    [Test]
    public void BuildVariant_ReplacesSlotsAndReportsSkippedSlots_WithoutChangingSourcePrefab()
    {
        CreateFolder(TempRoot);
        Material sourceA = CreateMaterialAsset(TempRoot + "/Source_A.mat", "Standard");
        Material sourceB = CreateMaterialAsset(TempRoot + "/Source_B.mat", "Standard");
        Material unsupported = CreateMaterialAsset(TempRoot + "/Source_Unlit.mat", "Unlit/Color");
        string sourcePrefabPath = TempRoot + "/Source.prefab";
        CreateSourcePrefab(sourcePrefabPath, sourceA, sourceB, unsupported);

        DitherStyleProfile profile = CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));
        PrefabVariantBuilder builder = new PrefabVariantBuilder();
        PrefabVariantBuildResult result = builder.BuildVariant(sourcePrefabPath, profile, TempVariantDirectory, TempMaterialDirectory);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Replacements.Count, Is.EqualTo(2));
        Assert.That(result.SkippedSlots.Count, Is.EqualTo(2));
        Assert.That(result.Warnings.Count, Is.GreaterThan(0));
        Assert.That(result.VariantAssetPath, Is.EqualTo(
            PrefabVariantBuilder.BuildDeterministicVariantAssetPath(
                TempVariantDirectory,
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath),
                profile)));

        GameObject sourceContents = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
        try
        {
            AssertSourceMaterialsUnchanged(sourceContents, sourceA, sourceB, unsupported);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceContents);
        }

        GameObject variantContents = PrefabUtility.LoadPrefabContents(result.VariantAssetPath);
        try
        {
            Renderer rendererA = variantContents.transform.Find("RendererA").GetComponent<Renderer>();
            Renderer rendererB = variantContents.transform.Find("RendererB").GetComponent<Renderer>();
            Material[] slotsA = rendererA.sharedMaterials;
            Material[] slotsB = rendererB.sharedMaterials;

            Assert.That(slotsA[0], Is.Not.EqualTo(sourceA));
            Assert.That(slotsA[1], Is.Null);
            Assert.That(slotsB[0], Is.EqualTo(unsupported));
            Assert.That(slotsB[1], Is.Not.EqualTo(sourceB));
            Assert.That(AssetDatabase.GetAssetPath(slotsA[0]).StartsWith(TempMaterialDirectory + "/"), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(slotsB[1]).StartsWith(TempMaterialDirectory + "/"), Is.True);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(variantContents);
        }
    }

    [Test]
    public void BuildVariant_ConvertsRenderersInsideNestedPrefabInstances()
    {
        CreateFolder(TempRoot);
        Material nestedSource = CreateMaterialAsset(TempRoot + "/Nested_Source.mat", "Standard");
        Material rootSource = CreateMaterialAsset(TempRoot + "/Root_Source.mat", "Standard");

        string nestedPrefabPath = TempRoot + "/Nested.prefab";
        string rootPrefabPath = TempRoot + "/Root.prefab";
        CreateNestedPrefab(nestedPrefabPath, nestedSource);
        CreateRootPrefabWithNestedInstance(rootPrefabPath, rootSource, nestedPrefabPath);

        DitherStyleProfile profile = CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));
        PrefabVariantBuilder builder = new PrefabVariantBuilder();
        PrefabVariantBuildResult result = builder.BuildVariant(rootPrefabPath, profile, TempVariantDirectory, TempMaterialDirectory);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Replacements.Count, Is.EqualTo(2));

        GameObject nestedContents = PrefabUtility.LoadPrefabContents(nestedPrefabPath);
        try
        {
            Renderer nestedRenderer = nestedContents.transform.Find("NestedRenderer").GetComponent<Renderer>();
            Assert.That(nestedRenderer.sharedMaterials[0], Is.EqualTo(nestedSource));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(nestedContents);
        }

        GameObject variantContents = PrefabUtility.LoadPrefabContents(result.VariantAssetPath);
        try
        {
            Renderer rootRenderer = variantContents.transform.Find("RootRenderer").GetComponent<Renderer>();
            Renderer nestedRenderer = variantContents.transform.Find("NestedInstance/NestedRenderer").GetComponent<Renderer>();
            Assert.That(rootRenderer.sharedMaterials[0], Is.Not.EqualTo(rootSource));
            Assert.That(nestedRenderer.sharedMaterials[0], Is.Not.EqualTo(nestedSource));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(variantContents);
        }
    }

    [Test]
    public void BuildVariantDryRun_PerformsNoWritesAndReportsDeterministicOutputs()
    {
        CreateFolder(TempRoot);
        Material source = CreateMaterialAsset(TempRoot + "/DryRun_Source.mat", "Standard");
        string sourcePrefabPath = TempRoot + "/DryRun_Source.prefab";
        CreateSourcePrefab(sourcePrefabPath, source, source, source);

        DitherStyleProfile profile = CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));
        string expectedVariantPath = PrefabVariantBuilder.BuildDeterministicVariantAssetPath(
            TempVariantDirectory,
            AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath),
            profile);

        string[] guidsBefore = AssetDatabase.FindAssets(string.Empty, new[] { TempRoot });
        PrefabVariantBuilder builder = new PrefabVariantBuilder();
        PrefabVariantBuildResult result = builder.BuildVariantDryRun(sourcePrefabPath, profile, TempVariantDirectory, TempMaterialDirectory);
        string[] guidsAfter = AssetDatabase.FindAssets(string.Empty, new[] { TempRoot });

        Assert.That(result.Success, Is.True);
        Assert.That(result.VariantAssetPath, Is.EqualTo(expectedVariantPath));
        Assert.That(result.Replacements.Count, Is.GreaterThan(0));
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(result.VariantAssetPath), Is.Null);
        Assert.That(AssetDatabase.IsValidFolder(TempVariantDirectory), Is.False);
        Assert.That(AssetDatabase.IsValidFolder(TempMaterialDirectory), Is.False);
        Assert.That(guidsAfter.Length, Is.EqualTo(guidsBefore.Length));

        for (int i = 0; i < result.Replacements.Count; i++)
        {
            PrefabMaterialReplacement replacement = result.Replacements[i];
            Assert.That(replacement.ConvertedMaterialPath.StartsWith(TempMaterialDirectory + "/"), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(replacement.ConvertedMaterialPath), Is.Null);
            Assert.That(string.IsNullOrEmpty(replacement.AdapterUsed), Is.False);
        }
    }

    [Test]
    public void BuildVariantPreview_PerformsInMemoryConversionWithoutAssetWrites()
    {
        CreateFolder(TempRoot);
        Material source = CreateMaterialAsset(TempRoot + "/Preview_Source.mat", "Standard");
        string sourcePrefabPath = TempRoot + "/Preview_Source.prefab";
        CreateSourcePrefab(sourcePrefabPath, source, source, source);

        DitherStyleProfile profile = CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
        Assert.That(sourcePrefab, Is.Not.Null);

        string[] guidsBefore = AssetDatabase.FindAssets(string.Empty, new[] { TempRoot });
        PrefabVariantBuilder builder = new PrefabVariantBuilder();
        PrefabVariantBuildResult result = builder.BuildVariantPreview(sourcePrefab, profile, out GameObject previewInstance);
        string[] guidsAfter = AssetDatabase.FindAssets(string.Empty, new[] { TempRoot });

        try
        {
            Assert.That(result.Success, Is.True);
            Assert.That(previewInstance, Is.Not.Null);
            Assert.That(result.Replacements.Count, Is.GreaterThan(0));
            Assert.That(result.TemporaryMaterials.Count, Is.GreaterThan(0));
            Assert.That(guidsAfter.Length, Is.EqualTo(guidsBefore.Length));
            Assert.That(EditorUtility.IsPersistent(previewInstance), Is.False);

            Renderer renderer = previewInstance.transform.Find("RendererA").GetComponent<Renderer>();
            Material converted = renderer.sharedMaterials[0];
            Assert.That(converted, Is.Not.Null);
            Assert.That(converted, Is.Not.EqualTo(source));
            Assert.That(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(converted)), Is.True);
        }
        finally
        {
            if (result != null)
            {
                for (int i = 0; i < result.TemporaryMaterials.Count; i++)
                {
                    Material material = result.TemporaryMaterials[i];
                    if (material != null)
                        Object.DestroyImmediate(material);
                }
            }

            if (previewInstance != null)
                Object.DestroyImmediate(previewInstance);
        }
    }

    static void AssertSourceMaterialsUnchanged(GameObject sourceContents, Material sourceA, Material sourceB, Material unsupported)
    {
        Renderer rendererA = sourceContents.transform.Find("RendererA").GetComponent<Renderer>();
        Renderer rendererB = sourceContents.transform.Find("RendererB").GetComponent<Renderer>();
        Material[] slotsA = rendererA.sharedMaterials;
        Material[] slotsB = rendererB.sharedMaterials;
        Assert.That(slotsA[0], Is.EqualTo(sourceA));
        Assert.That(slotsA[1], Is.Null);
        Assert.That(slotsB[0], Is.EqualTo(unsupported));
        Assert.That(slotsB[1], Is.EqualTo(sourceB));
    }

    static void CreateSourcePrefab(string prefabPath, Material sourceA, Material sourceB, Material unsupported)
    {
        GameObject root = new GameObject("SourceRoot");
        try
        {
            GameObject childA = new GameObject("RendererA");
            childA.transform.SetParent(root.transform, false);
            MeshRenderer rendererA = childA.AddComponent<MeshRenderer>();
            rendererA.sharedMaterials = new[] { sourceA, null };

            GameObject childB = new GameObject("RendererB");
            childB.transform.SetParent(root.transform, false);
            MeshRenderer rendererB = childB.AddComponent<MeshRenderer>();
            rendererB.sharedMaterials = new[] { unsupported, sourceB };

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void CreateNestedPrefab(string nestedPrefabPath, Material nestedSource)
    {
        GameObject nestedRoot = new GameObject("NestedRoot");
        try
        {
            GameObject nestedRendererGo = new GameObject("NestedRenderer");
            nestedRendererGo.transform.SetParent(nestedRoot.transform, false);
            MeshRenderer renderer = nestedRendererGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = nestedSource;
            PrefabUtility.SaveAsPrefabAsset(nestedRoot, nestedPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(nestedRoot);
        }
    }

    static void CreateRootPrefabWithNestedInstance(string rootPrefabPath, Material rootSource, string nestedPrefabPath)
    {
        GameObject nestedPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(nestedPrefabPath);
        Assert.That(nestedPrefabAsset, Is.Not.Null);

        GameObject root = new GameObject("Root");
        try
        {
            GameObject rootRendererGo = new GameObject("RootRenderer");
            rootRendererGo.transform.SetParent(root.transform, false);
            MeshRenderer rootRenderer = rootRendererGo.AddComponent<MeshRenderer>();
            rootRenderer.sharedMaterial = rootSource;

            GameObject nestedInstance = (GameObject)PrefabUtility.InstantiatePrefab(nestedPrefabAsset);
            nestedInstance.name = "NestedInstance";
            nestedInstance.transform.SetParent(root.transform, false);

            PrefabUtility.SaveAsPrefabAsset(root, rootPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Material CreateMaterialAsset(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Assert.That(shader, Is.Not.Null, "Shader '" + shaderName + "' must exist.");

        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
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

    static DitherStyleProfile CreateStyleProfile(params PropertyRemapRule[] rules)
    {
        ShaderAdapterMapping mapping = new ShaderAdapterMapping();
        SetPrivateField(mapping, "sourceShaderName", "Standard");
        SetPrivateField(mapping, "targetShader", FindTargetShader());
        SetPrivateField(mapping, "propertyRemapRules", new System.Collections.Generic.List<PropertyRemapRule>(rules));

        ShaderAdapterRegistry registry = ScriptableObject.CreateInstance<ShaderAdapterRegistry>();
        SetPrivateField(registry, "shaderMappings", new System.Collections.Generic.List<ShaderAdapterMapping> { mapping });

        DitherStyleProfile profile = ScriptableObject.CreateInstance<DitherStyleProfile>();
        SetPrivateField(profile, "profileName", "VariantTestStyle");
        SetPrivateField(profile, "shaderAdapterRegistry", registry);
        return profile;
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

    static void SetPrivateField<TObject, TValue>(TObject instance, string fieldName, TValue value)
    {
        FieldInfo field = typeof(TObject).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field " + fieldName + " on " + typeof(TObject).Name);
        field.SetValue(instance, value);
    }
}
