using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines how a source material property is remapped to a target property.
/// </summary>
[Serializable]
public class PropertyRemapRule
{
    [SerializeField]
    string sourcePropertyName;

    [SerializeField]
    string targetPropertyName;

    [SerializeField]
    PropertyRemapRuleKind ruleKind = PropertyRemapRuleKind.DirectCopy;

    [SerializeField]
    float scale = 1.0f;

    [SerializeField]
    float bias = 0.0f;

    [SerializeField]
    float constantFallback = 0.0f;

    /// <summary>
    /// Source shader property name.
    /// </summary>
    public string SourcePropertyName => sourcePropertyName;

    /// <summary>
    /// Target shader property name.
    /// </summary>
    public string TargetPropertyName => targetPropertyName;

    /// <summary>
    /// Rule behavior used while remapping this property.
    /// </summary>
    public PropertyRemapRuleKind RuleKind => ruleKind;

    /// <summary>
    /// Optional scaling multiplier for scale/bias remapping.
    /// </summary>
    public float Scale => scale;

    /// <summary>
    /// Optional additive bias for scale/bias remapping.
    /// </summary>
    public float Bias => bias;

    /// <summary>
    /// Constant value used by fallback-oriented mapping strategies.
    /// </summary>
    public float ConstantFallback => constantFallback;

    /// <summary>
    /// Creates a remap rule for programmatic adapter setup.
    /// </summary>
    public static PropertyRemapRule Create(
        string sourcePropertyNameValue,
        string targetPropertyNameValue,
        PropertyRemapRuleKind ruleKindValue,
        float scaleValue = 1.0f,
        float biasValue = 0.0f,
        float constantFallbackValue = 0.0f)
    {
        var rule = new PropertyRemapRule();
        rule.sourcePropertyName = sourcePropertyNameValue ?? string.Empty;
        rule.targetPropertyName = targetPropertyNameValue ?? string.Empty;
        rule.ruleKind = ruleKindValue;
        rule.scale = scaleValue;
        rule.bias = biasValue;
        rule.constantFallback = constantFallbackValue;
        return rule;
    }
}

/// <summary>
/// Enumerates available remapping rule behaviors for property migration.
/// </summary>
public enum PropertyRemapRuleKind
{
    DirectCopy,
    ScaleBias,
    ConstantFallback,
    SkipWithWarning
}

/// <summary>
/// Maps one source shader to one target shader and its property remapping rules.
/// </summary>
[Serializable]
public class ShaderAdapterMapping
{
    [SerializeField]
    string sourceShaderName;

    [SerializeField]
    Shader targetShader;

    [SerializeField]
    List<PropertyRemapRule> propertyRemapRules = new List<PropertyRemapRule>();

    [SerializeField, TextArea(2, 6)]
    string adapterDocumentation;

    [SerializeField]
    List<string> supportedSourceProperties = new List<string>();

    [SerializeField]
    List<string> unsupportedSourceProperties = new List<string>();

    /// <summary>
    /// Source shader name used to match materials for conversion.
    /// </summary>
    public string SourceShaderName => sourceShaderName;

    /// <summary>
    /// Destination shader used for converted materials.
    /// </summary>
    public Shader TargetShader => targetShader;

    /// <summary>
    /// Property remapping rules for this shader pairing.
    /// </summary>
    public IReadOnlyList<PropertyRemapRule> PropertyRemapRules => propertyRemapRules;

    /// <summary>
    /// Human-readable notes describing support level for this adapter.
    /// </summary>
    public string AdapterDocumentation => adapterDocumentation;

    /// <summary>
    /// Source shader properties explicitly supported by this adapter.
    /// </summary>
    public IReadOnlyList<string> SupportedSourceProperties => supportedSourceProperties;

    /// <summary>
    /// Source shader properties intentionally unsupported by this adapter.
    /// </summary>
    public IReadOnlyList<string> UnsupportedSourceProperties => unsupportedSourceProperties;

    /// <summary>
    /// Creates a shader adapter mapping for programmatic registry setup.
    /// </summary>
    public static ShaderAdapterMapping Create(
        string sourceShaderNameValue,
        Shader targetShaderValue,
        IEnumerable<PropertyRemapRule> propertyRules,
        IEnumerable<string> supportedProperties,
        IEnumerable<string> unsupportedProperties,
        string documentation)
    {
        var mapping = new ShaderAdapterMapping();
        mapping.sourceShaderName = sourceShaderNameValue ?? string.Empty;
        mapping.targetShader = targetShaderValue;
        mapping.propertyRemapRules = propertyRules != null
            ? new List<PropertyRemapRule>(propertyRules)
            : new List<PropertyRemapRule>();
        mapping.supportedSourceProperties = supportedProperties != null
            ? new List<string>(supportedProperties)
            : new List<string>();
        mapping.unsupportedSourceProperties = unsupportedProperties != null
            ? new List<string>(unsupportedProperties)
            : new List<string>();
        mapping.adapterDocumentation = documentation ?? string.Empty;
        return mapping;
    }
}
