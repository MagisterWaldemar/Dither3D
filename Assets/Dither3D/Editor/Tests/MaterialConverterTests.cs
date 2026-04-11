using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MaterialConverterTests
{
    const string TempOutputDirectory = "Assets/Dither3D/Editor/Tests/__Generated";

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(TempOutputDirectory);
    }

    [Test]
    public void DirectCopy_CopiesTextureScaleOffset()
    {
        Material source = CreateSourceMaterial();
        source.SetTextureScale("_MainTex", new Vector2(2f, 3f));
        source.SetTextureOffset("_MainTex", new Vector2(0.25f, 0.5f));

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, CreateStyleProfile(CreateRule("_MainTex", "_MainTex", PropertyRemapRuleKind.DirectCopy)));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ConvertedMaterial.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2f, 3f)));
        Assert.That(result.ConvertedMaterial.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
    }

    [Test]
    public void ScaleBias_AppliesScaleAndBias()
    {
        Material source = CreateSourceMaterial();
        source.SetFloat("_Glossiness", 0.4f);

        PropertyRemapRule rule = CreateRule("_Glossiness", "_InputExposure", PropertyRemapRuleKind.ScaleBias);
        SetPrivateField(rule, "scale", 2.0f);
        SetPrivateField(rule, "bias", 0.1f);

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, CreateStyleProfile(rule));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ConvertedMaterial.GetFloat("_InputExposure"), Is.EqualTo(0.9f).Within(0.0001f));
    }

    [Test]
    public void ConstantFallback_UsesFallbackWhenSourceMissing()
    {
        Material source = CreateSourceMaterial();
        PropertyRemapRule rule = CreateRule("_MissingProp", "_InputExposure", PropertyRemapRuleKind.ConstantFallback);
        SetPrivateField(rule, "constantFallback", 1.75f);

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, CreateStyleProfile(rule));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ConvertedMaterial.GetFloat("_InputExposure"), Is.EqualTo(1.75f).Within(0.0001f));
    }

    [Test]
    public void SkipWithWarning_DoesNotSetTargetProperty()
    {
        Material source = CreateSourceMaterial();
        source.SetFloat("_Glossiness", 0.9f);
        float originalTarget = new Material(FindTargetShader()).GetFloat("_InputExposure");

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, CreateStyleProfile(CreateRule("_Glossiness", "_InputExposure", PropertyRemapRuleKind.SkipWithWarning)));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Warnings.Count, Is.GreaterThan(0));
        Assert.That(result.ConvertedMaterial.GetFloat("_InputExposure"), Is.EqualTo(originalTarget).Within(0.0001f));
    }

    [Test]
    public void Conversion_IsIdempotentAndDoesNotModifySource()
    {
        Material source = CreateSourceMaterial();
        source.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1f));
        source.SetFloat("_Glossiness", 0.3f);
        Color sourceColorBefore = source.GetColor("_Color");
        float sourceGlossBefore = source.GetFloat("_Glossiness");

        DitherStyleProfile profile = CreateStyleProfile(
            CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy),
            CreateRule("_Glossiness", "_InputExposure", PropertyRemapRuleKind.ScaleBias, 1.5f, 0.2f));

        MaterialConverter converter = new MaterialConverter();
        ConversionResult first = converter.ConvertAndPersist(source, profile, TempOutputDirectory);
        Color firstColor = first.ConvertedMaterial.GetColor("_Color");
        float firstExposure = first.ConvertedMaterial.GetFloat("_InputExposure");
        ConversionResult second = converter.ConvertAndPersist(source, profile, TempOutputDirectory);

        Assert.That(first.Success, Is.True);
        Assert.That(second.Success, Is.True);
        Assert.That(first.OutputAssetPath, Is.EqualTo(second.OutputAssetPath));
        Assert.That(firstColor, Is.EqualTo(second.ConvertedMaterial.GetColor("_Color")));
        Assert.That(firstExposure, Is.EqualTo(second.ConvertedMaterial.GetFloat("_InputExposure")).Within(0.0001f));
        Assert.That(source.GetColor("_Color"), Is.EqualTo(sourceColorBefore));
        Assert.That(source.GetFloat("_Glossiness"), Is.EqualTo(sourceGlossBefore).Within(0.0001f));
    }

    [Test]
    public void UnmappedSourceProperties_EmitWarnings()
    {
        Material source = CreateSourceMaterial();
        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy)));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Warnings.Count, Is.GreaterThan(0));
        Assert.That(ContainsWarningPrefix(result, "Unmapped source property"), Is.True);
    }

    [Test]
    public void DryRunConvert_DoesNotWriteAsset_AndReportsDeterministicPath()
    {
        Material source = CreateSourceMaterial();
        source.name = "DryRunSource";
        DitherStyleProfile profile = CreateStyleProfile(CreateRule("_Color", "_Color", PropertyRemapRuleKind.DirectCopy));

        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.DryRunConvert(source, profile, TempOutputDirectory);

        Assert.That(result.Success, Is.True);
        Assert.That(string.IsNullOrEmpty(result.OutputAssetPath), Is.False);
        Assert.That(result.OutputAssetPath.StartsWith(TempOutputDirectory + "/"), Is.True);
        Assert.That(AssetDatabase.LoadAssetAtPath<Material>(result.OutputAssetPath), Is.Null);
        Assert.That(string.IsNullOrEmpty(result.AdapterUsed), Is.False);
    }

    [Test]
    public void PrioritizedNonUrpAdapters_ConvertRepresentativeMaterial_AndReportUnsupportedFields()
    {
        string[] sourceShaderNames =
        {
            "Nature/SpeedTree",
            "Nature/SpeedTree8",
            "Dither 3D/Particles (Alpha Blended)"
        };

        DitherStyleProfile profile = CreateProfileWithPrioritizedNonUrpAdapters();
        MaterialConverter converter = new MaterialConverter();

        for (int i = 0; i < sourceShaderNames.Length; i++)
        {
            string shaderName = sourceShaderNames[i];
            Material source = CreateSourceMaterial(shaderName);
            if (source == null)
                Assert.Inconclusive("Expected source shader '" + shaderName + "' to exist for adapter coverage.");

            ConversionResult result = converter.Convert(source, profile);
            Assert.That(result.Success, Is.True, "Expected conversion success for " + shaderName);
            Assert.That(result.ConvertedMaterial, Is.Not.Null, "Expected converted material for " + shaderName);
            Assert.That(result.Warnings.Count, Is.GreaterThan(0), "Expected warnings for partial mapping on " + shaderName);
            Assert.That(ContainsWarningPrefix(result, "Explicitly unsupported source property"), Is.True, "Expected explicit unsupported warning for " + shaderName);

            ShaderAdapterMapping mapping = FindMapping(profile.ShaderAdapterRegistry, shaderName);
            Assert.That(mapping, Is.Not.Null, "Missing mapping for " + shaderName);
            for (int unsupportedIndex = 0; unsupportedIndex < mapping.UnsupportedSourceProperties.Count; unsupportedIndex++)
            {
                string unsupportedProperty = mapping.UnsupportedSourceProperties[unsupportedIndex];
                Assert.That(ContainsWarningText(result, unsupportedProperty), Is.True,
                    $"Expected warning mentioning unsupported property '{unsupportedProperty}' for shader '{shaderName}'.");
            }
        }
    }

    [Test]
    public void PrioritizedNonUrpAdapters_DefinitionsOnlyMapVerifiedProperties()
    {
        ShaderAdapterRegistry registry = ShaderAdapterRegistry.CreatePrioritizedNonUrpRegistry();
        Assert.That(registry, Is.Not.Null);
        Assert.That(registry.ShaderMappings.Count, Is.GreaterThanOrEqualTo(3));

        for (int i = 0; i < registry.ShaderMappings.Count; i++)
        {
            ShaderAdapterMapping mapping = registry.ShaderMappings[i];
            Assert.That(mapping, Is.Not.Null);
            Assert.That(mapping.TargetShader, Is.Not.Null, "Missing target shader for mapping " + mapping.SourceShaderName);
            Assert.That(string.IsNullOrEmpty(mapping.AdapterDocumentation), Is.False, "Missing adapter documentation for " + mapping.SourceShaderName);
            Assert.That(mapping.SupportedSourceProperties.Count, Is.GreaterThan(0), "Missing supported property documentation for " + mapping.SourceShaderName);
            Assert.That(mapping.UnsupportedSourceProperties.Count, Is.GreaterThan(0), "Missing unsupported property documentation for " + mapping.SourceShaderName);

            Shader sourceShader = Shader.Find(mapping.SourceShaderName);
            if (sourceShader == null)
                Assert.Inconclusive("Expected source shader '" + mapping.SourceShaderName + "' to exist for mapping verification.");

            IReadOnlyList<PropertyRemapRule> rules = mapping.PropertyRemapRules;
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                PropertyRemapRule rule = rules[ruleIndex];
                Assert.That(ShaderHasProperty(sourceShader, rule.SourcePropertyName), Is.True,
                    $"Source shader '{mapping.SourceShaderName}' is missing mapped source property '{rule.SourcePropertyName}'.");
                Assert.That(ShaderHasProperty(mapping.TargetShader, rule.TargetPropertyName), Is.True,
                    $"Target shader '{mapping.TargetShader.name}' is missing mapped target property '{rule.TargetPropertyName}'.");
            }
        }
    }

    [Test]
    public void UrpOpaqueShader_Resolves_WhenUrpLitShaderIsAvailable()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
            Assert.Inconclusive("URP Lit shader is not available in this test environment.");

        Shader urpOpaque = Shader.Find("Dither 3D/URP/Opaque");
        Assert.That(urpOpaque, Is.Not.Null, "Dither 3D/URP/Opaque shader must resolve in URP projects.");
    }

    [Test]
    public void PrioritizedRegistry_WithUrpPreference_ConvertsUrpLitToPreferredOpaqueTarget()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
            Assert.Inconclusive("URP Lit shader is not available in this test environment.");

        Material source = CreateSourceMaterial("Universal Render Pipeline/Lit");
        Assert.That(source, Is.Not.Null, "URP Lit shader must be available.");
        source.SetColor("_BaseColor", new Color(0.3f, 0.4f, 0.5f, 1f));

        ShaderAdapterRegistry registry = ShaderAdapterRegistry.CreatePrioritizedRegistry(true);
        DitherStyleProfile profile = CreateStyleProfile(registry, "UrpPreferred");
        MaterialConverter converter = new MaterialConverter();
        ConversionResult result = converter.Convert(source, profile);

        Shader preferredUrpTarget = Shader.Find("Dither 3D/URP/Opaque");
        Shader fallbackTarget = Shader.Find("Dither 3D/Opaque");
        Shader expectedTarget = preferredUrpTarget != null ? preferredUrpTarget : fallbackTarget;

        Assert.That(expectedTarget, Is.Not.Null, "Expected at least one opaque dither target shader.");
        Assert.That(result.Success, Is.True);
        Assert.That(result.ConvertedMaterial, Is.Not.Null);
        Assert.That(result.ConvertedMaterial.shader, Is.EqualTo(expectedTarget));
        Assert.That(result.ConvertedMaterial.GetColor("_Color"), Is.EqualTo(source.GetColor("_BaseColor")));
    }

    [Test]
    public void PrioritizedRegistry_WithoutUrpPreference_DoesNotAddUrpLitMapping()
    {
        ShaderAdapterRegistry registry = ShaderAdapterRegistry.CreatePrioritizedRegistry(false);
        ShaderAdapterMapping mapping = FindMapping(registry, "Universal Render Pipeline/Lit");
        Assert.That(mapping, Is.Null);
    }

    [Test]
    public void PrioritizedRegistry_WithUrpPreference_FallsBackToBuiltInTarget_WhenUrpTargetIsUnavailable()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
            Assert.Inconclusive("URP Lit shader is not available in this test environment.");

        Shader urpTarget = Shader.Find("Dither 3D/URP/Opaque");
        if (urpTarget != null)
            Assert.Inconclusive("URP target shader is available; fallback path is not applicable in this environment.");

        Shader fallbackTarget = Shader.Find("Dither 3D/Opaque");
        Assert.That(fallbackTarget, Is.Not.Null);

        ShaderAdapterRegistry registry = ShaderAdapterRegistry.CreatePrioritizedRegistry(true);
        ShaderAdapterMapping mapping = FindMapping(registry, "Universal Render Pipeline/Lit");
        Assert.That(mapping, Is.Not.Null);
        Assert.That(mapping.TargetShader, Is.EqualTo(fallbackTarget));
    }

    static bool ShaderHasProperty(Shader shader, string propertyName)
    {
        if (shader == null || string.IsNullOrEmpty(propertyName))
            return false;

        int propertyCount = shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            if (shader.GetPropertyName(i) == propertyName)
                return true;
        }

        return false;
    }

    static bool ContainsWarningPrefix(ConversionResult result, string prefix)
    {
        for (int i = 0; i < result.Warnings.Count; i++)
        {
            if (result.Warnings[i].StartsWith(prefix))
                return true;
        }

        return false;
    }

    static bool ContainsWarningText(ConversionResult result, string text)
    {
        for (int i = 0; i < result.Warnings.Count; i++)
        {
            if (result.Warnings[i].Contains(text))
                return true;
        }

        return false;
    }

    static ShaderAdapterMapping FindMapping(ShaderAdapterRegistry registry, string sourceShaderName)
    {
        for (int i = 0; i < registry.ShaderMappings.Count; i++)
        {
            ShaderAdapterMapping mapping = registry.ShaderMappings[i];
            if (mapping != null && mapping.SourceShaderName == sourceShaderName)
                return mapping;
        }

        return null;
    }

    static Material CreateSourceMaterial()
    {
        Material material = CreateSourceMaterial("Standard");
        Assert.That(material, Is.Not.Null, "Standard shader must exist.");
        return material;
    }

    static Material CreateSourceMaterial(string shaderName)
    {
        Shader sourceShader = Shader.Find(shaderName);
        if (sourceShader == null)
            return null;
        return new Material(sourceShader);
    }

    static Shader FindTargetShader()
    {
        Shader shader = Shader.Find("Dither 3D/Opaque");
        Assert.That(shader, Is.Not.Null, "Dither 3D/Opaque shader must exist.");
        return shader;
    }

    static DitherStyleProfile CreateStyleProfile(params PropertyRemapRule[] rules)
    {
        ShaderAdapterMapping mapping = new ShaderAdapterMapping();
        SetPrivateField(mapping, "sourceShaderName", "Standard");
        SetPrivateField(mapping, "targetShader", FindTargetShader());
        SetPrivateField(mapping, "propertyRemapRules", new System.Collections.Generic.List<PropertyRemapRule>(rules));

        ShaderAdapterRegistry registry = ScriptableObject.CreateInstance<ShaderAdapterRegistry>();
        SetPrivateField(registry, "shaderMappings", new System.Collections.Generic.List<ShaderAdapterMapping> { mapping });
        return CreateStyleProfile(registry, "TestStyle");
    }

    static DitherStyleProfile CreateStyleProfile(ShaderAdapterRegistry registry, string profileName)
    {
        DitherStyleProfile profile = ScriptableObject.CreateInstance<DitherStyleProfile>();
        SetPrivateField(profile, "profileName", profileName);
        SetPrivateField(profile, "shaderAdapterRegistry", registry);
        return profile;
    }

    static DitherStyleProfile CreateProfileWithPrioritizedNonUrpAdapters()
    {
        ShaderAdapterRegistry registry = ShaderAdapterRegistry.CreatePrioritizedNonUrpRegistry();
        DitherStyleProfile profile = ScriptableObject.CreateInstance<DitherStyleProfile>();
        SetPrivateField(profile, "profileName", "PrioritizedNonUrp");
        SetPrivateField(profile, "shaderAdapterRegistry", registry);
        return profile;
    }

    static PropertyRemapRule CreateRule(string source, string target, PropertyRemapRuleKind kind, float scale = 1f, float bias = 0f)
    {
        PropertyRemapRule rule = new PropertyRemapRule();
        SetPrivateField(rule, "sourcePropertyName", source);
        SetPrivateField(rule, "targetPropertyName", target);
        SetPrivateField(rule, "ruleKind", kind);
        SetPrivateField(rule, "scale", scale);
        SetPrivateField(rule, "bias", bias);
        return rule;
    }

    static void SetPrivateField<TObject, TValue>(TObject instance, string fieldName, TValue value)
    {
        FieldInfo field = typeof(TObject).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field " + fieldName + " on " + typeof(TObject).Name);
        field.SetValue(instance, value);
    }
}
