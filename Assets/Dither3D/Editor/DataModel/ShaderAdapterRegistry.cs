using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Asset containing source-to-target shader adapter mappings.
/// </summary>
[CreateAssetMenu(fileName = "ShaderAdapterRegistry", menuName = "Dither 3D/Conversion/Shader Adapter Registry")]
public class ShaderAdapterRegistry : ScriptableObject
{
    const string BuiltInOpaqueShaderName = "Dither 3D/Opaque";
    const string UrpOpaqueShaderName = "Dither 3D/URP/Opaque";

    [SerializeField]
    List<ShaderAdapterMapping> shaderMappings = new List<ShaderAdapterMapping>();

    /// <summary>
    /// Shader mapping entries used by the conversion pipeline.
    /// </summary>
    public IReadOnlyList<ShaderAdapterMapping> ShaderMappings => shaderMappings;

    /// <summary>
    /// Creates prioritized adapter mappings for the currently active render pipeline.
    /// </summary>
    public static ShaderAdapterRegistry CreatePrioritizedRegistryForActivePipeline()
    {
        return CreatePrioritizedRegistry(IsUrpPipelineActive());
    }

    /// <summary>
    /// Creates prioritized adapter mappings and optionally prefers URP targets.
    /// </summary>
    public static ShaderAdapterRegistry CreatePrioritizedRegistry(bool preferUrpTargets)
    {
        ShaderAdapterRegistry registry = CreatePrioritizedNonUrpRegistry();
        if (!preferUrpTargets)
            return registry;

        Shader preferredOpaque = FindPreferredOpaqueTargetShader(true);
        if (preferredOpaque == null)
            return registry;

        registry.shaderMappings.Insert(
            0,
            ShaderAdapterMapping.Create(
                "Universal Render Pipeline/Lit",
                preferredOpaque,
                new[]
                {
                    PropertyRemapRule.Create("_BaseColor", "_Color", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_BaseMap", "_MainTex", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_BumpMap", "_BumpMap", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_EmissionMap", "_EmissionMap", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_EmissionColor", "_EmissionColor", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_Smoothness", "_Glossiness", PropertyRemapRuleKind.DirectCopy),
                    PropertyRemapRule.Create("_Metallic", "_Metallic", PropertyRemapRuleKind.DirectCopy)
                },
                new[] { "_BaseColor", "_BaseMap", "_BumpMap", "_EmissionMap", "_EmissionColor", "_Smoothness", "_Metallic" },
                new[] { "_WorkflowMode", "_Cull", "_Surface", "_Blend", "_AlphaClip", "_Cutoff", "_ReceiveShadows", "_SpecColor", "_SpecGlossMap", "_OcclusionMap", "_OcclusionStrength" },
                "Supports core Lit PBR + texture transfer for URP Lit materials. Surface/blend workflow controls and selected advanced maps remain unsupported and are reported as warnings."));

        return registry;
    }

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

    static Shader FindPreferredOpaqueTargetShader(bool preferUrpTarget)
    {
        if (preferUrpTarget)
        {
            Shader urpShader = Shader.Find(UrpOpaqueShaderName);
            if (urpShader != null)
                return urpShader;
        }

        return Shader.Find(BuiltInOpaqueShaderName);
    }

    static bool IsUrpPipelineActive()
    {
        RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
        if (current == null)
            return false;

        string typeName = current.GetType().FullName;
        return string.Equals(typeName, "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset", System.StringComparison.Ordinal);
    }
}
