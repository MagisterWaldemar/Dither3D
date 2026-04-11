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

    static bool ContainsWarningPrefix(ConversionResult result, string prefix)
    {
        for (int i = 0; i < result.Warnings.Count; i++)
        {
            if (result.Warnings[i].StartsWith(prefix))
                return true;
        }

        return false;
    }

    static Material CreateSourceMaterial()
    {
        Shader sourceShader = Shader.Find("Standard");
        Assert.That(sourceShader, Is.Not.Null, "Standard shader must exist.");
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

        DitherStyleProfile profile = ScriptableObject.CreateInstance<DitherStyleProfile>();
        SetPrivateField(profile, "profileName", "TestStyle");
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
