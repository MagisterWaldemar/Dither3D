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
}
