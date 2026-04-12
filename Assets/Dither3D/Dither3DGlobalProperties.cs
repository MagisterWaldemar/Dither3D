/*
 * Copyright (c) 2025 Rune Skovbo Johansen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Collections.Generic;
using UnityEngine;

public class OverridePropertyAttribute : PropertyAttribute { }

[ExecuteAlways]
public class Dither3DGlobalProperties : MonoBehaviour
{
    static List<Material> ditherMaterials = new List<Material>();

    public enum DitherColorMode { Grayscale, RGB, CMYK }
    public enum DitherTemporalPreset { Conservative, Balanced, Aggressive }
    public enum PointillismPreset { Conservative, Balanced, Aggressive }

    [Header("Global Options")]
    public DitherColorMode colorMode;

    public bool inverseDots;
    public bool radialCompensation;
    public bool quantizeLayers;
    public bool debugFractal;

    [Header("Global Overrides")]
    public bool setOnEnable = true;
    public bool saveInMaterials;

    [Space]
    
    [OverrideProperty] public float inputExposure = 1;
    [HideInInspector] public bool inputExposureOverride;
    
    [OverrideProperty] public float inputOffset = 0;
    [HideInInspector] public bool inputOffsetOverride;

    [Space]

    [OverrideProperty] public float dotScale = 5;
    [HideInInspector] public bool dotScaleOverride;
    
    [OverrideProperty] public float dotSizeVariability = 0;
    [HideInInspector] public bool dotSizeVariabilityOverride;
    
    [OverrideProperty] public float dotContrast = 1;
    [HideInInspector] public bool dotContrastOverride;
    
    [OverrideProperty] public float stretchSmoothness = 1;
    [HideInInspector] public bool stretchSmoothnessOverride;

    [Space]

    [OverrideProperty] public DitherTemporalPreset temporalPreset = DitherTemporalPreset.Balanced;
    [HideInInspector] public bool temporalPresetOverride;

    [OverrideProperty] public float blueNoisePhaseSpeed = 0.15f;
    [HideInInspector] public bool blueNoisePhaseSpeedOverride;

    [OverrideProperty] public float blueNoiseHysteresis = 0.8f;
    [HideInInspector] public bool blueNoiseHysteresisOverride;

    [OverrideProperty] public float blueNoiseMinDot = 0.12f;
    [HideInInspector] public bool blueNoiseMinDotOverride;

    [Header("Dots Scaling Behavior")]
    public bool scaleWithScreen = true;
    public int referenceRes = 1080;

    [Space]

    [Header("Pointillism Overrides")]
    [OverrideProperty] public bool pointillismEnable;
    [HideInInspector] public bool pointillismEnableOverride;

    [OverrideProperty] public PointillismPreset pointillismPreset = PointillismPreset.Balanced;
    [HideInInspector] public bool pointillismPresetOverride;

    [OverrideProperty] public float pointillismDirectionality = 0.5f;
    [HideInInspector] public bool pointillismDirectionalityOverride;

    [OverrideProperty] public float pointillismStrokeLength = 0.4f;
    [HideInInspector] public bool pointillismStrokeLengthOverride;

    [OverrideProperty] public float pointillismBlueNoiseStrokeMix = 0.3f;
    [HideInInspector] public bool pointillismBlueNoiseStrokeMixOverride;

    [OverrideProperty] public float pointillismColorSteps = 8f;
    [HideInInspector] public bool pointillismColorStepsOverride;

    [OverrideProperty] public float pointillismColorModel = 1f;
    [HideInInspector] public bool pointillismColorModelOverride;

    [OverrideProperty] public float pointillismMaxChroma = 0.32f;
    [HideInInspector] public bool pointillismMaxChromaOverride;

    [OverrideProperty] public bool pointillismPerceptualMode;
    [HideInInspector] public bool pointillismPerceptualModeOverride;

    [OverrideProperty] public float pointillismHueSteps = 8f;
    [HideInInspector] public bool pointillismHueStepsOverride;

    [OverrideProperty] public float pointillismCoordSource;
    [HideInInspector] public bool pointillismCoordSourceOverride;

    [OverrideProperty] public float pointillismObjectScale = 1f;
    [HideInInspector] public bool pointillismObjectScaleOverride;

    [OverrideProperty] public float pointillismTriplanarSharpness = 4f;
    [HideInInspector] public bool pointillismTriplanarSharpnessOverride;

    [OverrideProperty] public Color pointillismClampMinColor = Color.black;
    [HideInInspector] public bool pointillismClampMinColorOverride;

    [OverrideProperty] public Color pointillismClampMaxColor = Color.white;
    [HideInInspector] public bool pointillismClampMaxColorOverride;

    [OverrideProperty] public float pointillismLUTBlend = 0f;
    [HideInInspector] public bool pointillismLUTBlendOverride;

    [OverrideProperty] public float pointillismCompositionMode = 0f;
    [HideInInspector] public bool pointillismCompositionModeOverride;

    [OverrideProperty] public float pointillismBaseMuting = 0.35f;
    [HideInInspector] public bool pointillismBaseMutingOverride;

    [OverrideProperty] public float pointillismChromaPush = 0.6f;
    [HideInInspector] public bool pointillismChromaPushOverride;

    [OverrideProperty] public float pointillismComplementaryAccentAmount = 0.2f;
    [HideInInspector] public bool pointillismComplementaryAccentAmountOverride;

    [OverrideProperty] public float pointillismAccentSparsity = 0.75f;
    [HideInInspector] public bool pointillismAccentSparsityOverride;

    [OverrideProperty] public float pointillismDetailSensitivityAlbedo = 1f;
    [HideInInspector] public bool pointillismDetailSensitivityAlbedoOverride;

    [OverrideProperty] public float pointillismDetailSensitivityNormal = 1f;
    [HideInInspector] public bool pointillismDetailSensitivityNormalOverride;

    [OverrideProperty] public float pointillismHighlightAccentStrength = 0.35f;
    [HideInInspector] public bool pointillismHighlightAccentStrengthOverride;

    void OnEnable()
    {
        CollectMaterials();
        UpdateGlobalOptions();
        if (setOnEnable)
            UpdateGlobalOverrides();
    }

    void OnDisable()
    {
    }

    void OnValidate()
    {
        if (ditherMaterials != null && ditherMaterials.Count > 0 && ditherMaterials[0] == null)
            CollectMaterials();

        UpdateGlobalOptions();
        UpdateGlobalOverrides();
    }

    void OnDidApplyAnimationProperties()
    {
        UpdateGlobalOptions();
        UpdateGlobalOverrides();
    }

    void CollectMaterials()
    {
        ditherMaterials.Clear();
        Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
        foreach (var mat in materials)
        {
            if (mat.HasProperty("_DitherTex"))
            {
                ditherMaterials.Add(mat);
            }
        }
    }

    void UpdateGlobalOptions()
    {
        EnableKeyword("DITHERCOL_GRAYSCALE", colorMode == DitherColorMode.Grayscale);
        EnableKeyword("DITHERCOL_RGB", colorMode == DitherColorMode.RGB);
        EnableKeyword("DITHERCOL_CMYK", colorMode == DitherColorMode.CMYK);
        EnableKeyword("INVERSE_DOTS", inverseDots);
        EnableKeyword("RADIAL_COMPENSATION", radialCompensation);
        EnableKeyword("QUANTIZE_LAYERS", quantizeLayers);
        EnableKeyword("DEBUG_FRACTAL", debugFractal);
    }

    void UpdateGlobalOverrides()
    {
        bool changed = false;
        if (inputExposureOverride)
            SetShaderOverride("_InputExposure", inputExposure, ref changed);
        if (inputOffsetOverride)
            SetShaderOverride("_InputOffset", inputOffset, ref changed);
        if (dotScaleOverride)
        {
            float shaderDotScale = dotScale;
            if (scaleWithScreen)
            {
                float multiplier = Screen.height / referenceRes;
                if (multiplier > 0f)
                {
                    float logDelta = Mathf.Log(multiplier, 2f);
                    shaderDotScale += logDelta;
                }
            }
            SetShaderOverride("_Scale", shaderDotScale, ref changed);
        }
        if (dotSizeVariabilityOverride)
            SetShaderOverride("_SizeVariability", dotSizeVariability, ref changed);
        if (dotContrastOverride)
            SetShaderOverride("_Contrast", dotContrast, ref changed);
        if (stretchSmoothnessOverride)
            SetShaderOverride("_StretchSmoothness", stretchSmoothness, ref changed);
        if (temporalPresetOverride)
        {
            GetTemporalPresetValues(temporalPreset, out float presetPhaseSpeed, out float presetHysteresis, out float presetMinDot);
            SetShaderOverride("_BlueNoisePhaseSpeed", presetPhaseSpeed, ref changed);
            SetShaderOverride("_BlueNoiseHysteresis", presetHysteresis, ref changed);
            SetShaderOverride("_BlueNoiseMinDot", presetMinDot, ref changed);
        }
        if (blueNoisePhaseSpeedOverride)
            SetShaderOverride("_BlueNoisePhaseSpeed", blueNoisePhaseSpeed, ref changed);
        if (blueNoiseHysteresisOverride)
            SetShaderOverride("_BlueNoiseHysteresis", blueNoiseHysteresis, ref changed);
        if (blueNoiseMinDotOverride)
            SetShaderOverride("_BlueNoiseMinDot", blueNoiseMinDot, ref changed);
        if (pointillismEnableOverride)
            SetShaderOverride("_PointillismEnable", pointillismEnable ? 1f : 0f, ref changed);
        if (pointillismPresetOverride)
        {
            GetPointillismPresetValues(
                pointillismPreset,
                out float presetDirectionality,
                out float presetStrokeLength,
                out float presetBlueNoiseStrokeMix,
                out float presetColorSteps,
                out float presetColorModel,
                out float presetMaxChroma,
                out float presetPerceptualMode,
                out float presetHueSteps,
                out Color presetClampMin,
                out Color presetClampMax,
                out float presetCompositionMode,
                out float presetBaseMuting,
                out float presetChromaPush,
                out float presetComplementaryAccentAmount,
                out float presetAccentSparsity,
                out float presetDetailSensitivityAlbedo,
                out float presetDetailSensitivityNormal,
                out float presetHighlightAccentStrength,
                out float presetPhaseSpeed,
                out float presetHysteresis,
                out float presetMinDot);
            SetShaderOverride("_PointillismDirectionality", presetDirectionality, ref changed);
            SetShaderOverride("_PointillismStrokeLength", presetStrokeLength, ref changed);
            SetShaderOverride("_PointillismBlueNoiseStrokeMix", presetBlueNoiseStrokeMix, ref changed);
            SetShaderOverride("_PointillismColorSteps", presetColorSteps, ref changed);
            SetShaderOverride("_PointillismColorModel", presetColorModel, ref changed);
            SetShaderOverride("_PointillismMaxChroma", presetMaxChroma, ref changed);
            SetShaderOverride("_PointillismPerceptualMode", presetPerceptualMode, ref changed);
            SetShaderOverride("_PointillismHueSteps", presetHueSteps, ref changed);
            SetShaderColorOverride("_PointillismClampMinColor", presetClampMin, ref changed);
            SetShaderColorOverride("_PointillismClampMaxColor", presetClampMax, ref changed);
            SetShaderOverride("_PointillismCompositionMode", presetCompositionMode, ref changed);
            SetShaderOverride("_PointillismBaseMuting", presetBaseMuting, ref changed);
            SetShaderOverride("_PointillismChromaPush", presetChromaPush, ref changed);
            SetShaderOverride("_PointillismComplementaryAccentAmount", presetComplementaryAccentAmount, ref changed);
            SetShaderOverride("_PointillismAccentSparsity", presetAccentSparsity, ref changed);
            SetShaderOverride("_PointillismDetailSensitivityAlbedo", presetDetailSensitivityAlbedo, ref changed);
            SetShaderOverride("_PointillismDetailSensitivityNormal", presetDetailSensitivityNormal, ref changed);
            SetShaderOverride("_PointillismHighlightAccentStrength", presetHighlightAccentStrength, ref changed);
            SetShaderOverride("_BlueNoisePhaseSpeed", presetPhaseSpeed, ref changed);
            SetShaderOverride("_BlueNoiseHysteresis", presetHysteresis, ref changed);
            SetShaderOverride("_BlueNoiseMinDot", presetMinDot, ref changed);
        }
        if (pointillismDirectionalityOverride)
            SetShaderOverride("_PointillismDirectionality", pointillismDirectionality, ref changed);
        if (pointillismStrokeLengthOverride)
            SetShaderOverride("_PointillismStrokeLength", pointillismStrokeLength, ref changed);
        if (pointillismBlueNoiseStrokeMixOverride)
            SetShaderOverride("_PointillismBlueNoiseStrokeMix", pointillismBlueNoiseStrokeMix, ref changed);
        if (pointillismColorStepsOverride)
            SetShaderOverride("_PointillismColorSteps", pointillismColorSteps, ref changed);
        if (pointillismColorModelOverride)
            SetShaderOverride("_PointillismColorModel", pointillismColorModel, ref changed);
        if (pointillismMaxChromaOverride)
            SetShaderOverride("_PointillismMaxChroma", pointillismMaxChroma, ref changed);
        if (pointillismPerceptualModeOverride)
            SetShaderOverride("_PointillismPerceptualMode", pointillismPerceptualMode ? 1f : 0f, ref changed);
        if (pointillismHueStepsOverride)
            SetShaderOverride("_PointillismHueSteps", pointillismHueSteps, ref changed);
        if (pointillismCoordSourceOverride)
            SetShaderOverride("_PointillismCoordSource", pointillismCoordSource, ref changed);
        if (pointillismObjectScaleOverride)
            SetShaderOverride("_PointillismObjectScale", pointillismObjectScale, ref changed);
        if (pointillismTriplanarSharpnessOverride)
            SetShaderOverride("_PointillismTriplanarSharpness", pointillismTriplanarSharpness, ref changed);
        if (pointillismClampMinColorOverride)
            SetShaderColorOverride("_PointillismClampMinColor", pointillismClampMinColor, ref changed);
        if (pointillismClampMaxColorOverride)
            SetShaderColorOverride("_PointillismClampMaxColor", pointillismClampMaxColor, ref changed);
        if (pointillismLUTBlendOverride)
            SetShaderOverride("_PointillismLUTBlend", pointillismLUTBlend, ref changed);
        if (pointillismCompositionModeOverride)
            SetShaderOverride("_PointillismCompositionMode", pointillismCompositionMode, ref changed);
        if (pointillismBaseMutingOverride)
            SetShaderOverride("_PointillismBaseMuting", pointillismBaseMuting, ref changed);
        if (pointillismChromaPushOverride)
            SetShaderOverride("_PointillismChromaPush", pointillismChromaPush, ref changed);
        if (pointillismComplementaryAccentAmountOverride)
            SetShaderOverride("_PointillismComplementaryAccentAmount", pointillismComplementaryAccentAmount, ref changed);
        if (pointillismAccentSparsityOverride)
            SetShaderOverride("_PointillismAccentSparsity", pointillismAccentSparsity, ref changed);
        if (pointillismDetailSensitivityAlbedoOverride)
            SetShaderOverride("_PointillismDetailSensitivityAlbedo", pointillismDetailSensitivityAlbedo, ref changed);
        if (pointillismDetailSensitivityNormalOverride)
            SetShaderOverride("_PointillismDetailSensitivityNormal", pointillismDetailSensitivityNormal, ref changed);
        if (pointillismHighlightAccentStrengthOverride)
            SetShaderOverride("_PointillismHighlightAccentStrength", pointillismHighlightAccentStrength, ref changed);

        #if UNITY_EDITOR
        if (changed && saveInMaterials)
        {
            foreach (var mat in ditherMaterials)
            {
                UnityEditor.EditorUtility.SetDirty(mat);
            }
        }
        #endif
    }

    void EnableKeyword(string keyword, bool enable)
    {
        if (enable)
            Shader.EnableKeyword(keyword);
        else
            Shader.DisableKeyword(keyword);
    }

    void SetShaderOverride(string property, float value, ref bool changed)
    {
        foreach (var mat in ditherMaterials)
        {
            if (!mat.HasProperty(property))
                continue;

            if (mat.GetFloat(property) != value)
            {
                mat.SetFloat(property, value);
                changed = true;
            }
        }
    }

    void SetShaderColorOverride(string property, Color value, ref bool changed)
    {
        foreach (var mat in ditherMaterials)
        {
            if (!mat.HasProperty(property))
                continue;

            if (mat.GetColor(property) != value)
            {
                mat.SetColor(property, value);
                changed = true;
            }
        }
    }

    static void GetTemporalPresetValues(DitherTemporalPreset preset, out float phaseSpeed, out float hysteresis, out float minDot)
    {
        switch (preset)
        {
            case DitherTemporalPreset.Conservative:
                phaseSpeed = 0.08f;
                hysteresis = 0.90f;
                minDot = 0.18f;
                break;
            case DitherTemporalPreset.Aggressive:
                phaseSpeed = 0.45f;
                hysteresis = 0.45f;
                minDot = 0.04f;
                break;
            default:
                phaseSpeed = 0.15f;
                hysteresis = 0.80f;
                minDot = 0.12f;
                break;
        }
    }

    static void GetPointillismPresetValues(
        PointillismPreset preset,
        out float directionality,
        out float strokeLength,
        out float blueNoiseStrokeMix,
        out float colorSteps,
        out float colorModel,
        out float maxChroma,
        out float perceptualMode,
        out float hueSteps,
        out Color clampMin,
        out Color clampMax,
        out float compositionMode,
        out float baseMuting,
        out float chromaPush,
        out float complementaryAccentAmount,
        out float accentSparsity,
        out float detailSensitivityAlbedo,
        out float detailSensitivityNormal,
        out float highlightAccentStrength,
        out float phaseSpeed,
        out float hysteresis,
        out float minDot)
    {
        switch (preset)
        {
            case PointillismPreset.Conservative:
                directionality = 0.35f;
                strokeLength = 0.25f;
                blueNoiseStrokeMix = 0.15f;
                colorSteps = 6f;
                colorModel = 1f;
                maxChroma = 0.24f;
                perceptualMode = 0f;
                hueSteps = 6f;
                clampMin = new Color(0.05f, 0.05f, 0.05f, 1f);
                clampMax = new Color(0.95f, 0.95f, 0.95f, 1f);
                compositionMode = 1f;
                baseMuting = 0.50f;
                chromaPush = 0.40f;
                complementaryAccentAmount = 0.12f;
                accentSparsity = 0.88f;
                detailSensitivityAlbedo = 0.80f;
                detailSensitivityNormal = 0.75f;
                highlightAccentStrength = 0.20f;
                phaseSpeed = 0.08f;
                hysteresis = 0.90f;
                minDot = 0.18f;
                break;
            case PointillismPreset.Aggressive:
                directionality = 0.80f;
                strokeLength = 0.70f;
                blueNoiseStrokeMix = 0.60f;
                colorSteps = 12f;
                colorModel = 1f;
                maxChroma = 0.40f;
                perceptualMode = 0f;
                hueSteps = 12f;
                clampMin = Color.black;
                clampMax = Color.white;
                compositionMode = 1f;
                baseMuting = 0.22f;
                chromaPush = 1.20f;
                complementaryAccentAmount = 0.45f;
                accentSparsity = 0.55f;
                detailSensitivityAlbedo = 1.35f;
                detailSensitivityNormal = 1.40f;
                highlightAccentStrength = 0.70f;
                phaseSpeed = 0.45f;
                hysteresis = 0.45f;
                minDot = 0.04f;
                break;
            default:
                directionality = 0.50f;
                strokeLength = 0.40f;
                blueNoiseStrokeMix = 0.30f;
                colorSteps = 8f;
                colorModel = 1f;
                maxChroma = 0.32f;
                perceptualMode = 0f;
                hueSteps = 8f;
                clampMin = Color.black;
                clampMax = Color.white;
                compositionMode = 1f;
                baseMuting = 0.35f;
                chromaPush = 0.60f;
                complementaryAccentAmount = 0.20f;
                accentSparsity = 0.75f;
                detailSensitivityAlbedo = 1.00f;
                detailSensitivityNormal = 1.00f;
                highlightAccentStrength = 0.35f;
                phaseSpeed = 0.15f;
                hysteresis = 0.80f;
                minDot = 0.12f;
                break;
        }
    }
}
