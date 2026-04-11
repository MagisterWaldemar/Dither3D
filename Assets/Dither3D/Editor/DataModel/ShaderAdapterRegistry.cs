using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asset containing source-to-target shader adapter mappings.
/// </summary>
[CreateAssetMenu(fileName = "ShaderAdapterRegistry", menuName = "Dither 3D/Conversion/Shader Adapter Registry")]
public class ShaderAdapterRegistry : ScriptableObject
{
    [SerializeField]
    List<ShaderAdapterMapping> shaderMappings = new List<ShaderAdapterMapping>();

    /// <summary>
    /// Shader mapping entries used by the conversion pipeline.
    /// </summary>
    public IReadOnlyList<ShaderAdapterMapping> ShaderMappings => shaderMappings;

    /// <summary>
    /// Creates prioritized non-URP adapter mappings for common Built-in RP shaders.
    /// </summary>
    public static ShaderAdapterRegistry CreatePrioritizedNonUrpRegistry()
    {
        var registry = CreateInstance<ShaderAdapterRegistry>();
        Shader cutoutShader = Shader.Find("Dither 3D/Cutout");
        Shader particleAddShader = Shader.Find("Dither 3D/Particles (Additive)");

        registry.shaderMappings = new List<ShaderAdapterMapping>
        {
            ShaderAdapterMapping.Create(
                "Nature/SpeedTree",
                cutoutShader,
                new[]
                {
                    PropertyRemapRule.Create("_Color", "_Color", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_MainTex", "_MainTex", PropertyRemapRuleKind.DirectCopy)
                },
                new[] { "_Color", "_MainTex" },
                new[] { "_Cutoff", "_BumpMap", "_HueVariation", "_WindQuality" },
                "Supports diffuse color/texture transfer for SpeedTree materials. Alpha cutoff, normal, and wind-specific controls are currently unsupported and reported as warnings."),
            ShaderAdapterMapping.Create(
                "Nature/SpeedTree8",
                cutoutShader,
                new[]
                {
                    PropertyRemapRule.Create("_Color", "_Color", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_MainTex", "_MainTex", PropertyRemapRuleKind.DirectCopy)
                },
                new[] { "_Color", "_MainTex" },
                new[] { "_Cutoff", "_BumpMap", "_HueVariation", "_WindQuality" },
                "Supports diffuse color/texture transfer for SpeedTree8 materials. Alpha cutoff, normal, and wind-specific controls are currently unsupported and reported as warnings."),
            ShaderAdapterMapping.Create(
                "Dither 3D/Particles (Alpha Blended)",
                particleAddShader,
                new[]
                {
                    PropertyRemapRule.Create("_TintColor", "_TintColor", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_MainTex", "_MainTex", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_InvFade", "_InvFade", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_InputExposure", "_InputExposure", PropertyRemapRuleKind.DirectCopy)
                },
                new[] { "_TintColor", "_MainTex", "_InvFade", "_InputExposure" },
                new[] { "_InputOffset", "_Scale", "_SizeVariability", "_Contrast", "_StretchSmoothness" },
                "Supports core particle color/texture/soft-particle/exposure transfer for the project alpha-blended particle shader. Additional tuning controls are explicitly unsupported and reported as warnings.")
        };

        return registry;
    }
}
