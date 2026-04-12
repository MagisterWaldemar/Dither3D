/*
 * Copyright (c) 2025 Rune Skovbo Johansen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

sampler3D _DitherTex;
sampler2D _DitherRampTex;
sampler2D _BlueNoiseRankTex;
sampler2D _BlueNoisePhaseTex;
sampler2D _PointillismLUTTex;

#ifndef fixed
#define fixed half
#endif
#ifndef fixed2
#define fixed2 half2
#endif
#ifndef fixed3
#define fixed3 half3
#endif
#ifndef fixed4
#define fixed4 half4
#endif

float4 _DitherTex_TexelSize;
float4 _BlueNoiseRankTex_TexelSize;
float4 _BlueNoisePhaseTex_TexelSize;
float4 _PointillismLUTTex_TexelSize;
float _Scale;
float _SizeVariability;
float _Contrast;
float _StretchSmoothness;
float _InputExposure;
float _InputOffset;
float _DitherPatternSource;
float _BlueNoisePhaseSpeed;
float _BlueNoiseHysteresis;
float _BlueNoiseMinDot;
float _PointillismEnable;
float _PointillismDirectionality;
float _PointillismStrokeLength;
float _PointillismBlueNoiseStrokeMix;
float _PointillismColorSteps;
float _PointillismPerceptualMode;
float _PointillismHueSteps;
float _PointillismColorModel;
float _PointillismMaxChroma;
float4 _PointillismClampMinColor;
float4 _PointillismClampMaxColor;
float _PointillismLUTBlend;
float _PointillismCoordSource;
float _PointillismObjectScale;
float _PointillismTriplanarSharpness;

float2 HashBlueNoiseOffset(float n)
{
    float2 sn = sin(float2(n * 12.9898 + 78.233, n * 39.3467 + 11.1351));
    return frac(sn * 43758.5453);
}

fixed SampleBlueNoiseRank(float2 uv, float phaseIndex, float phaseTexBlend)
{
    float2 phaseOffset = HashBlueNoiseOffset(phaseIndex);
    float phaseTex = tex2D(_BlueNoisePhaseTex, frac(uv + phaseOffset)).r;
    phaseOffset = frac(phaseOffset + (phaseTex - 0.5) * phaseTexBlend);
    return tex2D(_BlueNoiseRankTex, frac(uv + phaseOffset)).r;
}

static const float MIN_CONTRAST_EPSILON = 0.0001;
static const float MIN_DOT_CONTRAST_REDUCTION_FACTOR = 0.75;
static const float MIN_VALID_TEXTURE_SIZE = 1.5;
static const float MIN_POINTILLISM_RANGE = 0.0001;
static const float POINTILLISM_MIN_CHROMA_PRESERVE_THRESHOLD = 0.04;
static const float POINTILLISM_CHROMA_PRESERVE_RATIO = 0.35;
static const float2 POINTILLISM_RANK_UV_OFFSET = float2(0.37, 0.61);
static const float POINTILLISM_RANK_SPREAD_SCALE = 1.73;
static const float POINTILLISM_RANK_SPREAD_OFFSET = 0.19;
static const float POINTILLISM_RANK_PRIMARY_WEIGHT = 0.67;
static const float POINTILLISM_RANK_SECONDARY_WEIGHT = 0.33;
static const float POINTILLISM_TRI_MIX_RICHNESS_STEP_RANGE = 10.0;
static const float POINTILLISM_TRI_MIX_TRANSFER_SCALE = 0.5;
static const float PATTERN_SOURCE_BLUENOISE_THRESHOLD = 0.5;
static const float POINTILLISM_COLORMODE_OKLAB_THRESHOLD = 0.5;
static const fixed3 POINTILLISM_SOURCE_VIS_UV = fixed3(0.1, 0.7, 1.0);
static const fixed3 POINTILLISM_SOURCE_VIS_ALT_UV = fixed3(1.0, 0.85, 0.1);
static const fixed3 POINTILLISM_SOURCE_VIS_OBJECT = fixed3(1.0, 0.4, 0.1);
static const float POINTILLISM_DEBUG_BLEND = 0.7;

fixed SampleTemporalRankWithFallback(float2 uvBlue, float phaseOffset)
{
    float phaseTime = max(0.0, _Time.y * _BlueNoisePhaseSpeed);
    float phaseIdx = floor(phaseTime);
    float phaseFrac = frac(phaseTime);
    float stickiness = saturate(_BlueNoiseHysteresis);
    float phaseBlend = saturate((phaseFrac - stickiness) / max(MIN_CONTRAST_EPSILON, 1.0 - stickiness));
    float hasPhaseTex = step(MIN_VALID_TEXTURE_SIZE, _BlueNoisePhaseTex_TexelSize.z);
    float hasRankTex = step(MIN_VALID_TEXTURE_SIZE, _BlueNoiseRankTex_TexelSize.z);

    fixed rankA = SampleBlueNoiseRank(uvBlue, phaseIdx + phaseOffset, hasPhaseTex);
    fixed rankB = SampleBlueNoiseRank(uvBlue, phaseIdx + 1.0 + phaseOffset, hasPhaseTex);
    fixed rank = lerp(rankA, rankB, phaseBlend);

    fixed hashRank = frac(sin(dot(uvBlue + phaseOffset, float2(12.9898, 78.233))) * 43758.5453);
    return lerp(hashRank, rank, hasRankTex);
}

fixed SamplePointillismRank(float2 uvPointillism)
{
    // Pointillism defaults to a stable Bayer-derived rank.
    // Blue-noise temporal rank is only used when explicitly selected and a rank texture is available.
    float2 uvRank = frac(uvPointillism);
    float useBlueNoiseRank = step(PATTERN_SOURCE_BLUENOISE_THRESHOLD, _DitherPatternSource) * step(MIN_VALID_TEXTURE_SIZE, _BlueNoiseRankTex_TexelSize.z);
    fixed bayerRank = tex3D(_DitherTex, float3(uvRank, 0.5)).r;
    fixed blueNoiseRank = SampleTemporalRankWithFallback(uvRank, 0.0);
    return lerp(bayerRank, blueNoiseRank, useBlueNoiseRank);
}

fixed3 RGBtoHSL(fixed3 rgb);
fixed HueToRGB(fixed p, fixed q, fixed t);
fixed3 HSLtoRGB(fixed3 hsl);

void SelectLowerMagnitudeDerivativeSet(float2 uvA, float2 uvB, out float2 dx, out float2 dy)
{
    // Get the rates of change of two sets of UV coordinates and use the set with lower magnitude.
    // "Lower magnitude" here means lower squared length (dot(v, v)), not per-component comparison.
    // This can remove seams caused by discontinuities in the UV coordinates,
    // as long as the alternative coordinates don't have seams in the same place.
    // Lower-magnitude derivatives reduce over-aggressive filtering and visual popping near seams.
    float2 dxA = ddx(uvA);
    float2 dyA = ddy(uvA);
    float2 dxB = ddx(uvB);
    float2 dyB = ddy(uvB);
    dx = dot(dxA, dxA) < dot(dxB, dxB) ? dxA : dxB;
    dy = dot(dyA, dyA) < dot(dyB, dyB) ? dyA : dyB;
}

float3 RGBtoOKLab(float3 rgb)
{
    float3 lms = mul(float3x3(
        0.4122214708, 0.5363325363, 0.0514459929,
        0.2119034982, 0.6806995451, 0.1073969566,
        0.0883024619, 0.2817188376, 0.6299787005), rgb);
    float3 lmsRoot = pow(max(lms, 0.0), 1.0 / 3.0);
    return mul(float3x3(
        0.2104542553, 0.7936177850, -0.0040720468,
        1.9779984951, -2.4285922050, 0.4505937099,
        0.0259040371, 0.7827717662, -0.8086757660), lmsRoot);
}

float3 OKLabToRGB(float3 lab)
{
    float3 lmsRoot = mul(float3x3(
        1.0, 0.3963377774, 0.2158037573,
        1.0, -0.1055613458, -0.0638541728,
        1.0, -0.0894841775, -1.2914855480), lab);
    float3 lms = lmsRoot * lmsRoot * lmsRoot;
    return mul(float3x3(
        4.0767416621, -3.3077115913, 0.2309699292,
        -1.2684380046, 2.6097574011, -0.3413193965,
        -0.0041960863, -0.7034186147, 1.7076147010), lms);
}

float2 ResolvePointillismStrokeDirection(float2 dx, float2 dy, float3 worldNormal, float hasWorldData)
{
    float2 dominantDerivative = dot(dx, dx) > dot(dy, dy) ? dx : dy;
    float2 mainDir = dot(dominantDerivative, dominantDerivative) > MIN_CONTRAST_EPSILON ? normalize(dominantDerivative) : float2(1.0, 0.0);
    float2 derivOrthoDir = float2(-mainDir.y, mainDir.x);

    float source = floor(_PointillismCoordSource + 0.5);
    float useWorldNormalDirection = hasWorldData * step(1.5, source);
    if (useWorldNormalDirection > 0.5)
    {
        float3 objectNormal = mul((float3x3)unity_WorldToObject, worldNormal);
        float2 objectDir = float2(objectNormal.x, objectNormal.z);
        float objectDirLen = length(objectDir);
        if (objectDirLen > MIN_CONTRAST_EPSILON)
        {
            float2 objectOrthoDir = float2(-objectDir.y, objectDir.x) / objectDirLen;
            return normalize(lerp(derivOrthoDir, objectOrthoDir, 0.75));
        }
    }

    return derivOrthoDir;
}

float ResolvePointillismTrinaryQuantizedValue(float normalizedValue, float steps, fixed ditherThreshold)
{
    float maxStepIndex = max(1.0, steps - 1.0);
    float scaledValue = saturate(normalizedValue) * maxStepIndex;
    float midIndex = floor(scaledValue);
    float fracValue = frac(scaledValue);
    float lowIndex = max(0.0, midIndex - 1.0);
    float highIndex = min(maxStepIndex, midIndex + 1.0);

    // Base two-tone dithering is (mid=1-frac, high=frac).
    // We inject a symmetric transfer term to introduce a third tone while preserving expected brightness.
    // Richness scales third-tone strength from 0 (near 2 steps) toward 1 (higher step counts).
    float richness = saturate((steps - 2.0) / POINTILLISM_TRI_MIX_RICHNESS_STEP_RANGE);
    // Symmetric transfer peaks at mid-bin values and fades near bin edges.
    // Expected brightness is preserved because transfer is added equally to low/high while mid is reduced by their sum.
    float lowTransfer = POINTILLISM_TRI_MIX_TRANSFER_SCALE * fracValue * (1.0 - fracValue) * richness;
    float lowProb = lowTransfer;
    float highProb = fracValue + lowTransfer;
    float midProb = max(0.0, 1.0 - lowProb - highProb);

    if (lowIndex >= midIndex && highIndex <= midIndex)
    {
        lowProb = 0.0;
        highProb = 0.0;
        midProb = 1.0;
    }
    else if (lowIndex >= midIndex)
    {
        midProb += lowProb;
        lowProb = 0.0;
    }
    else if (highIndex <= midIndex)
    {
        midProb += highProb;
        highProb = 0.0;
    }
    float probSum = max(MIN_POINTILLISM_RANGE, lowProb + midProb + highProb);
    lowProb /= probSum;
    midProb /= probSum;

    float lowThreshold = lowProb;
    float midThreshold = lowProb + midProb;
    float chosenIndex = highIndex;
    if (ditherThreshold < lowThreshold)
        chosenIndex = lowIndex;
    else if (ditherThreshold < midThreshold)
        chosenIndex = midIndex;
    return chosenIndex / maxStepIndex;
}

fixed3 ApplyPointillismColor(float2 uvPointillism, float2 dx, float2 dy, float3 worldNormal, float hasWorldData, fixed3 color)
{
    fixed3 clampMin = saturate(_PointillismClampMinColor.rgb);
    fixed3 clampMax = max(clampMin, saturate(_PointillismClampMaxColor.rgb));
    fixed3 clampRange = max(fixed3(MIN_POINTILLISM_RANGE, MIN_POINTILLISM_RANGE, MIN_POINTILLISM_RANGE), clampMax - clampMin);
    fixed3 clamped = clamp(color, clampMin, clampMax);

    float2 orthoDir = ResolvePointillismStrokeDirection(dx, dy, worldNormal, hasWorldData);
    fixed blueAngleRaw = SampleTemporalRankWithFallback(frac(uvPointillism), 0.5);
    float blueAngle = blueAngleRaw * 6.28318530718;
    float2 blueDir = float2(cos(blueAngle), sin(blueAngle));
    float2 blendedDir = normalize(lerp(orthoDir, blueDir, saturate(_PointillismBlueNoiseStrokeMix)));
    // Keep a small base spread so pointillism variation remains visible even with low stroke length.
    // The remaining spread range is controlled by stroke length for directional stroke tuning.
    float spread = saturate(_PointillismDirectionality) * (0.15 + 0.85 * saturate(_PointillismStrokeLength));
    float derivMag = max(MIN_CONTRAST_EPSILON, length(dx) + length(dy));
    float scaleAwareSpread = spread / max(MIN_CONTRAST_EPSILON, derivMag);
    scaleAwareSpread = clamp(scaleAwareSpread, 0.01, 2.0);

    float2 uvBase = frac(uvPointillism);
    float2 rankUvA = frac(uvBase + blendedDir * scaleAwareSpread * spread);
    float secondarySpread = spread * POINTILLISM_RANK_SPREAD_SCALE + POINTILLISM_RANK_SPREAD_OFFSET;
    float2 rankUvB = frac(uvBase + POINTILLISM_RANK_UV_OFFSET + blendedDir * scaleAwareSpread * secondarySpread);
    fixed rank = SamplePointillismRank(rankUvA);
    fixed rankSecondary = SamplePointillismRank(rankUvB);
    // Blend two decorrelated rank samples to reduce repeated two-tone pairings.
    fixed triRank = frac(rank * POINTILLISM_RANK_PRIMARY_WEIGHT + rankSecondary * POINTILLISM_RANK_SECONDARY_WEIGHT);
    fixed3 remapped = clamped;

    if (_PointillismColorModel > POINTILLISM_COLORMODE_OKLAB_THRESHOLD)
    {
        float3 lab = RGBtoOKLab(clamped);
        float maxChroma = max(MIN_POINTILLISM_RANGE, _PointillismMaxChroma);
        float chroma = length(lab.yz);
        if (chroma > maxChroma)
            lab.yz *= maxChroma / chroma;

        float steps = max(2.0, _PointillismColorSteps);
        lab.x = ResolvePointillismTrinaryQuantizedValue(lab.x, steps, triRank);

        remapped = clamp(saturate(OKLabToRGB(lab)), clampMin, clampMax);
    }
    else if (_PointillismPerceptualMode > 0.5)
    {
        fixed3 hsl = RGBtoHSL(clamped);
        float hueSteps = max(2.0, _PointillismHueSteps);
        fixed scaledHue = hsl.x * hueSteps;
        fixed quantizedHue = floor(scaledHue + 0.5) / hueSteps;

        float lumSteps = max(2.0, _PointillismColorSteps);
        fixed ditheredLum = ResolvePointillismTrinaryQuantizedValue(hsl.z, lumSteps, triRank);

        remapped = HSLtoRGB(fixed3(quantizedHue, hsl.y, ditheredLum));
        remapped = clamp(remapped, clampMin, clampMax);
    }
    else
    {
        fixed3 luminanceWeights = fixed3(0.299, 0.587, 0.114);
        fixed minLum = dot(clampMin, luminanceWeights);
        fixed maxLum = dot(clampMax, luminanceWeights);
        fixed lumRange = max(MIN_POINTILLISM_RANGE, dot(clampRange, luminanceWeights));
        fixed inputLum = clamp(dot(clamped, luminanceWeights), minLum, maxLum);
        fixed normalizedLum = saturate((inputLum - minLum) / lumRange);

        float steps = max(2.0, _PointillismColorSteps);
        fixed ditheredLum = ResolvePointillismTrinaryQuantizedValue(normalizedLum, steps, triRank);
        remapped = clamp(clamped * (ditheredLum / max(MIN_POINTILLISM_RANGE, inputLum)), clampMin, clampMax);
    }

    // If quantization collapses saturation too far for a clearly chromatic source,
    // blend back toward source color to avoid unintended grayscale-looking output.
    fixed sourceMax = max(clamped.r, max(clamped.g, clamped.b));
    fixed sourceMin = min(clamped.r, min(clamped.g, clamped.b));
    fixed sourceSaturation = sourceMax - sourceMin;
    fixed remappedMax = max(remapped.r, max(remapped.g, remapped.b));
    fixed remappedMin = min(remapped.r, min(remapped.g, remapped.b));
    fixed remappedSaturation = remappedMax - remappedMin;
    fixed desiredMinSaturation = sourceSaturation * POINTILLISM_CHROMA_PRESERVE_RATIO;
    fixed preserveAmount = step(POINTILLISM_MIN_CHROMA_PRESERVE_THRESHOLD, sourceSaturation) *
                           saturate(max(0.0, desiredMinSaturation - remappedSaturation) / max(MIN_POINTILLISM_RANGE, desiredMinSaturation));
    remapped = lerp(remapped, clamped, preserveAmount);

    float hasLut = step(MIN_VALID_TEXTURE_SIZE, _PointillismLUTTex_TexelSize.z);
    float lutBlend = saturate(_PointillismLUTBlend) * hasLut;
    fixed3 lutColor = fixed3(
        tex2D(_PointillismLUTTex, float2(remapped.r, 0.5)).r,
        tex2D(_PointillismLUTTex, float2(remapped.g, 0.5)).g,
        tex2D(_PointillismLUTTex, float2(remapped.b, 0.5)).b
    );
    return saturate(lerp(remapped, lutColor, lutBlend));
}

void ResolvePointillismUVAndDebugData(float2 uvDither, float2 uvAlt, float3 worldPos, float3 worldNormal, float hasWorldData, out float2 uvPointillism, out fixed3 sourceVis, out fixed3 triplanarWeights)
{
    float source = floor(_PointillismCoordSource + 0.5);
    sourceVis = POINTILLISM_SOURCE_VIS_UV;
    triplanarWeights = fixed3(0.0, 0.0, 0.0);

    if (source < 0.5)
    {
        uvPointillism = uvDither;
        return;
    }

    if (source < 1.5)
    {
        sourceVis = POINTILLISM_SOURCE_VIS_ALT_UV;
        uvPointillism = uvAlt;
        return;
    }

    if (hasWorldData < 0.5)
    {
        uvPointillism = uvDither;
        return;
    }

    float3 objectPos = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
    float objectScale = max(MIN_CONTRAST_EPSILON, _PointillismObjectScale);
    objectPos *= objectScale;

    if (source < 2.5)
    {
        sourceVis = POINTILLISM_SOURCE_VIS_OBJECT;
        uvPointillism = objectPos.xz;
        return;
    }

    float3 objectNormal = mul((float3x3)unity_WorldToObject, worldNormal);
    float normalLen = length(objectNormal);
    if (normalLen <= MIN_CONTRAST_EPSILON)
    {
        sourceVis = POINTILLISM_SOURCE_VIS_OBJECT;
        uvPointillism = objectPos.xz;
        return;
    }

    float3 normalAbs = abs(objectNormal / normalLen);
    float sharpness = max(1.0, _PointillismTriplanarSharpness);
    float3 weights = pow(normalAbs, sharpness);
    float weightSum = weights.x + weights.y + weights.z;
    weights /= max(MIN_CONTRAST_EPSILON, weightSum);
    triplanarWeights = weights;
    sourceVis = weights;

    float2 uvX = objectPos.yz;
    float2 uvY = objectPos.xz;
    float2 uvZ = objectPos.xy;
    uvPointillism = uvX * weights.x + uvY * weights.y + uvZ * weights.z;
}

float2 ResolvePointillismUV(float2 uvDither, float2 uvAlt, float3 worldPos, float3 worldNormal, float hasWorldData)
{
    float2 uvPointillism;
    fixed3 sourceVis;
    fixed3 triplanarWeights;
    ResolvePointillismUVAndDebugData(uvDither, uvAlt, worldPos, worldNormal, hasWorldData, uvPointillism, sourceVis, triplanarWeights);
    return uvPointillism;
}

// dx is the delta in u and v coordinates along the screen X axis.
// dy is the delta in u and v coordinates along the screen Y axis.
fixed4 GetDither3D_(float2 uv_DitherTex, float4 screenPos, float2 dx, float2 dy, fixed brightness)
{
    #if (INVERSE_DOTS)
        brightness = 1.0 - brightness;
    #endif

    // Get texture X resolution (width) based on Unity builtin data.
    // We assume the Y resolution is the same.
    float xRes = _DitherTex_TexelSize.z;
    float invXres = _DitherTex_TexelSize.x;

    // The relationship between X resolution, dots per side, and total number of
    // dots - which is also the Z resolution - is hardcoded in the script that
    // creates the 3D texture. Unity has no way to query the Z resolution
    // of a 3D texture in a shader.
    float dotsPerSide = xRes / 16.0;
    float dotsTotal = pow(dotsPerSide, 2); // Could also have been named zRes
    float invZres = 1.0 / dotsTotal;

    // Lookup brightness to make dither output have correct output
    // brightness at different input brightness values.
    float2 lookup = float2((0.5 * invXres + (1 - invXres) * brightness), 0.5);
    fixed brightnessCurve = tex2D(_DitherRampTex, lookup).r;

    #if (RADIAL_COMPENSATION)
        // Make screenPos have 0,0 in the center of the screen.
        float2 screenP = (screenPos.xy / screenPos.w - 0.5) * 2.0;
        // Calculate view direction projected onto camera plane.
        float2 viewDirProj = float2(
            screenP.x /  UNITY_MATRIX_P[0][0],
            screenP.y / -UNITY_MATRIX_P[1][1]);
        // Calculate how much dots should be larger towards the edges of the screen.
        // This is meant to keep dots completely stable under camera rotation.
        // Currently it doesn't entirely work but is more stable than no compensation.
        float radialCompensation = dot(viewDirProj, viewDirProj) + 1;
        dx *= radialCompensation;
        dy *= radialCompensation;
    #endif

    // Get frequency based on singular value decomposition.
    // A simpler approach would have been to use fwidth(uv_DitherTex).
    // However:
    //  1) fwidth is not accurate and produces axis-aligned biases/artefacts.
    //  2) We need both the minimum and maximum rate of change.
    //     These can be along any directions (orthogonal to each other),
    //     not necessarily aligned with x, y, u or v.
    //     So we use (a subset of) singular value decomposition to get these.
    float2x2 matr = { dx, dy };
    float4 vectorized = float4(dx, dy);
    float Q = dot(vectorized, vectorized);
    float R = determinant(matr); //ad-bc
    float discriminantSqr = max(0, Q*Q-4*R*R);
    float discriminant = sqrt(discriminantSqr);

    // "freq" here means rate of change of the UV coordinates on the screen.
    // Something smaller on the screen has a larger rate of change of its
    // UV coordinates from one pixel to the next.
    //
    // The freq variable: (max-freq, min-freq)
    //
    // If a surface has non-uniform scaling, or is seen at an angle,
    // or has UVs that are stretched more in one direction than the other,
    // the min and max frequency won't be the same.
    float2 freq = sqrt(float2(Q + discriminant, Q - discriminant) / 2);

    // We define a spacing variable which linearly correlates with
    // the average distance between dots.
    // For this dot spacing, we use the smaller frequency, which
    // corresponds to the largest amount of stretching.
    // This for example means that dots seen at an angle will be
    // compressed in one direction rather than enlarged in the other.
    float spacing = freq.y;

    // Scale the spacing by the specified input (power of two) scale.
    float scaleExp = exp2(_Scale);
    spacing *= scaleExp;

    // We keep the spacing the same regardless of whether we're using
    // a pattern with more or less dots in it.
    spacing *= dotsPerSide * 0.125;

    // We produce higher brightness by having the dots be larger
    // compared to the pattern size (based on a contrast threshold
    // further down), and lower brightness by having them be smaller.
    //
    // If we don't want variable dot sizes, we can keep the dot sizes
    // approximately constant regardless of brightness by dividing
    // the spacing by the brightness. This makes both the dots and
    // the spacing between them larger, the lower the brightness is.
    // In this case, the two adjustments of dot size cancel out each
    // other, leaving only the effect on the spacing between the dots.
    //
    // Any behavior in between these two is also possible, controlled by
    // the _SizeVariability input.
    //
    // A*pow(B,-1) is the same as A/B, so when _SizeVariability is 0,
    // we divide the spacing by the brightness.
    //
    // A*pow(B,0) is the same as A, so when _SizeVariability is 1,
    // we leave the spacing alone.
    //
    // The "* 2" is there so the mid-size dots keeps constant throughout
    // the spectrum, rather than the largest-sized dots.
    // The "+ 0.001" is there to avoid dividing by zero.
    float brightnessSpacingMultiplier =
        pow(brightnessCurve * 2 + 0.001, -(1 - _SizeVariability));
    spacing *= brightnessSpacingMultiplier;

    // Find the power-of-two level that corresponds to the dot spacing.
    float spacingLog = log2(spacing);
    int patternScaleLevel = floor(spacingLog); // Fractal level.
    float f = spacingLog - patternScaleLevel; // Fractional part.

    // Get the UV coordinates in the current fractal level.
    float2 uv = uv_DitherTex / exp2(patternScaleLevel);

    // Get the third coordinate for the 3D texture lookup.
    // Each layer along the 3rd dimension in the 3D texture has one more dot.
    // The first layer we use is the one that has 1/4 of the dots.
    // The last layer we use is the one with all the dots.
    float subLayer = lerp(0.25 * dotsTotal, dotsTotal, 1 - f);

    // If we don't want to interpolate between different layers, we can
    // restrict the sampled values so they correspond exactly to one layer.
    #if (QUANTIZE_LAYERS)
        float origSubLayer = subLayer;
        subLayer = floor(subLayer + 0.5);

        // When we quantize the layers, we can't rely on pattern interpolation
        // to keep the dot size constant within each sub-layer, so we have to
        // tweak the threshold values to compensate instead.
        float thresholdTweak = sqrt(subLayer / origSubLayer);
    #endif

    // Texels are half a texel offset from the texture border, so we
    // need to subtract half a texel. We also normalize to the 0-1 range.
    subLayer = (subLayer - 0.5) * invZres;

    // Sample the 3D texture (Bayer fractal path).
    fixed pattern = tex3D(_DitherTex, float3(uv, subLayer)).r;

    // The dots in the pattern are radial gradients.
    // We create sharp dots from them by increasing the contrast.
    // The desired amount of contrast can be set in the material,
    // for example such that there is 1 pixel of blurring around dots,
    // which looks equivalent to anti-aliasing.
    float contrast = _Contrast * scaleExp * brightnessSpacingMultiplier * 0.1;

    // The spacing is derived from the lowest frequency, but the
    // contrast must be based on the highest frequency to avoid aliasing.
    // Hence we multiply the contrast by the factor of the smallest
    // frequency (freq.y) relative to the highest frequency (freq.x).
    // This compensation can be increased or decreased by using exponents
    // other than 1, as provided by the _StretchSmoothness input.
    contrast *= pow(freq.y / freq.x, _StretchSmoothness);

    // The base brightness value that we scale the contrast around
    // should normally be 0.5, but if the pattern is very blurred,
    // that would just make the brightness everywhere close to 0.5.
    // To avoid this, we lerp towards a base value of the original
    // brightness the lower the contrast is.
    // The specific formula is arrived at experimentally to maintain
    // brightness levels across various contrast and scale values.
    fixed baseVal = lerp(0.5, brightness, saturate(1.05 / (1 + contrast)));

    // The brighter output we want, the lower threshold we need to use,
    // which makes the resulting dots larger relative to the pattern.
    #if (QUANTIZE_LAYERS)
        fixed threshold = 1 - brightnessCurve * thresholdTweak;
    #else
        fixed threshold = 1 - brightnessCurve;
    #endif

    float blueNoiseBlendFactor = step(PATTERN_SOURCE_BLUENOISE_THRESHOLD, _DitherPatternSource) * step(MIN_VALID_TEXTURE_SIZE, _BlueNoiseRankTex_TexelSize.z);
    float2 uvBlue = frac(uv);
    fixed rank = SampleTemporalRankWithFallback(uvBlue, 0.0);
    // Center rank around zero so it can shift threshold up or down.
    // Attenuate the shift for coarser fractal levels (larger spacing),
    // so large dots do not get over-perturbed by the blue-noise rank.
    fixed blueNoiseScaleFactor = saturate(1.0 / max(MIN_CONTRAST_EPSILON, spacing));
    fixed blueNoiseOffset = (rank - 0.5) * blueNoiseBlendFactor * blueNoiseScaleFactor;

    // Get the pattern value relative to the threshold (with optional
    // blue-noise perturbation), scale it according to the contrast,
    // and add the base value.
    fixed bw = saturate((pattern - (threshold + blueNoiseOffset)) * contrast + baseVal);

    #if (INVERSE_DOTS)
        bw = 1.0 - bw;
    #endif

    return fixed4(bw, frac(uv.x), frac(uv.y), subLayer);
}

fixed GetDither3D(float2 uv_DitherTex, float4 screenPos, fixed brightness)
{
    // Get the rates of change of the UV coordinates.
    float2 dx = ddx(uv_DitherTex);
    float2 dy = ddy(uv_DitherTex);
    return GetDither3D_(uv_DitherTex, screenPos, dx, dy, brightness);
}

fixed GetDither3DAltUV(float2 uv_DitherTex, float2 uv_DitherTexAlt, float4 screenPos, fixed brightness)
{
    float2 dx;
    float2 dy;
    SelectLowerMagnitudeDerivativeSet(uv_DitherTex, uv_DitherTexAlt, dx, dy);
    return GetDither3D_(uv_DitherTex, screenPos, dx, dy, brightness);
}

// COLOR HANDLING

fixed GetGrayscale(fixed4 color)
{
    return saturate(0.299 * color.r + 0.587 * color.g + 0.114 * color.b);
}

fixed3 CMYKtoRGB(fixed4 cmyk) {
    fixed c = cmyk.x;
    fixed m = cmyk.y;
    fixed y = cmyk.z;
    fixed k = cmyk.w;

    fixed invK = 1.0 - k;
    fixed r = 1.0 - min(1.0, c * invK + k);
    fixed g = 1.0 - min(1.0, m * invK + k);
    fixed b = 1.0 - min(1.0, y * invK + k);
    return saturate(fixed3(r, g, b));
}

fixed4 RGBtoCMYK(fixed3 rgb) {
    fixed r = rgb.r;
    fixed g = rgb.g;
    fixed b = rgb.b;
    fixed k = min(1.0 - r, min(1.0 - g, 1.0 - b));
    fixed3 cmy = 0.0;
    fixed invK = 1.0 - k;
    if (invK != 0.0) {
        cmy.x = (1.0 - r - k) / invK;
        cmy.y = (1.0 - g - k) / invK;
        cmy.z = (1.0 - b - k) / invK;
    }
    return saturate(fixed4(cmy, k));
}

fixed3 RGBtoHSL(fixed3 rgb)
{
    fixed maxC = max(rgb.r, max(rgb.g, rgb.b));
    fixed minC = min(rgb.r, min(rgb.g, rgb.b));
    fixed l = (maxC + minC) * 0.5;
    fixed d = maxC - minC;
    fixed s = 0.0;
    fixed h = 0.0;
    if (d > MIN_POINTILLISM_RANGE)
    {
        s = (l > 0.5) ? d / max(MIN_POINTILLISM_RANGE, 2.0 - maxC - minC)
                      : d / max(MIN_POINTILLISM_RANGE, maxC + minC);
        if (maxC == rgb.r)
            h = (rgb.g - rgb.b) / d + (rgb.g < rgb.b ? 6.0 : 0.0);
        else if (maxC == rgb.g)
            h = (rgb.b - rgb.r) / d + 2.0;
        else
            h = (rgb.r - rgb.g) / d + 4.0;
        h /= 6.0;
    }
    return fixed3(h, s, l);
}

fixed HueToRGB(fixed p, fixed q, fixed t)
{
    if (t < 0.0) t += 1.0;
    if (t > 1.0) t -= 1.0;
    if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
    if (t < 0.5) return q;
    if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
    return p;
}

fixed3 HSLtoRGB(fixed3 hsl)
{
    fixed h = hsl.x;
    fixed s = hsl.y;
    fixed l = hsl.z;
    if (s < MIN_POINTILLISM_RANGE)
        return fixed3(l, l, l);
    fixed q = (l < 0.5) ? l * (1.0 + s) : l + s - l * s;
    fixed p = 2.0 * l - q;
    return fixed3(
        HueToRGB(p, q, h + 1.0 / 3.0),
        HueToRGB(p, q, h),
        HueToRGB(p, q, h - 1.0 / 3.0)
    );
}

float2 RotateUV(float2 uv, float2 xUnitDir)
{
    return uv.x * xUnitDir + uv.y * float2(-xUnitDir.y, xUnitDir.x);
}

fixed4 GetDither3DColor_(float2 uv_DitherTex, float2 uvPointillism, float3 worldNormal, float hasWorldData, float4 screenPos, float2 dx, float2 dy, fixed4 color, fixed3 pointillismSourceVis, fixed3 pointillismTriplanarWeights)
{
    // Adjust brightness according to shader exposure and offset properties.
    color.rgb = saturate(color.rgb * _InputExposure + _InputOffset);

    if (_PointillismEnable > 0.5)
    {
        color.rgb = ApplyPointillismColor(uvPointillism, dx, dy, worldNormal, hasWorldData, color.rgb);

        #if (DEBUG_FRACTAL)
            float hasTriplanarWeights = step(MIN_CONTRAST_EPSILON, pointillismTriplanarWeights.x + pointillismTriplanarWeights.y + pointillismTriplanarWeights.z);
            fixed3 pointillismVis = lerp(pointillismSourceVis, pointillismTriplanarWeights, hasTriplanarWeights);
            color.rgb = lerp(color.rgb, pointillismVis, POINTILLISM_DEBUG_BLEND);
        #endif
    }
    else
    {
        #ifdef DITHERCOL_GRAYSCALE
            fixed4 dither = GetDither3D_(uv_DitherTex, screenPos, dx, dy, GetGrayscale(color));
            color.rgb = dither.x;
            #if (DEBUG_FRACTAL)
                fixed3 uvVis = dither.yzw;
                color.rgb = lerp(color.rgb, uvVis, 0.7);
            #endif
        #elif DITHERCOL_RGB
            color.r = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.r).x;
            color.g = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.g).x;
            color.b = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.b).x;
        #elif DITHERCOL_CMYK
            fixed4 cmyk = RGBtoCMYK(color.rgb);
            // Get dither pattern for C, M, Y, K and angles 15, 75, 0, 45.
            cmyk.x = GetDither3D_(RotateUV(uv_DitherTex, float2(0.966, 0.259)), screenPos, dx, dy, cmyk.x).x;
            cmyk.y = GetDither3D_(RotateUV(uv_DitherTex, float2(0.259, 0.966)), screenPos, dx, dy, cmyk.y).x;
            cmyk.z = GetDither3D_(RotateUV(uv_DitherTex, float2(1.000, 0.000)), screenPos, dx, dy, cmyk.z).x;
            cmyk.w = GetDither3D_(RotateUV(uv_DitherTex, float2(0.707, 0.707)), screenPos, dx, dy, cmyk.w).x;
            color.rgb = CMYKtoRGB(cmyk);
        #else
            // Fallback for pipelines or materials where no DITHERCOL_* keyword is enabled.
            // Preserve source color by default by dithering each RGB channel independently.
            // Separate calls are required because each channel has different brightness thresholds.
            color.r = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.r).x;
            color.g = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.g).x;
            color.b = GetDither3D_(uv_DitherTex, screenPos, dx, dy, color.b).x;
        #endif
    }

    return color;
}

fixed4 GetDither3DColorWorld(float2 uv_DitherTex, float2 uv_DitherTexAlt, float3 worldPos, float3 worldNormal, float4 screenPos, fixed4 color)
{
    float2 dx;
    float2 dy;
    SelectLowerMagnitudeDerivativeSet(uv_DitherTex, uv_DitherTexAlt, dx, dy);
    float2 uvPointillism;
    fixed3 pointillismSourceVis;
    fixed3 pointillismTriplanarWeights;
    ResolvePointillismUVAndDebugData(uv_DitherTex, uv_DitherTexAlt, worldPos, worldNormal, 1.0, uvPointillism, pointillismSourceVis, pointillismTriplanarWeights);
    return GetDither3DColor_(uv_DitherTex, uvPointillism, worldNormal, 1.0, screenPos, dx, dy, color, pointillismSourceVis, pointillismTriplanarWeights);
}

fixed4 GetDither3DColor(float2 uv_DitherTex, float4 screenPos, fixed4 color)
{
    // Get the rates of change of the UV coordinates.
    float2 dx = ddx(uv_DitherTex);
    float2 dy = ddy(uv_DitherTex);
    float2 uvPointillism;
    fixed3 pointillismSourceVis;
    fixed3 pointillismTriplanarWeights;
    ResolvePointillismUVAndDebugData(uv_DitherTex, uv_DitherTex, float3(0, 0, 0), float3(0, 1, 0), 0.0, uvPointillism, pointillismSourceVis, pointillismTriplanarWeights);
    return GetDither3DColor_(uv_DitherTex, uvPointillism, float3(0, 1, 0), 0.0, screenPos, dx, dy, color, pointillismSourceVis, pointillismTriplanarWeights);
}

fixed4 GetDither3DColorAltUV(float2 uv_DitherTex, float2 uv_DitherTexAlt, float4 screenPos, fixed4 color)
{
    float2 dx;
    float2 dy;
    SelectLowerMagnitudeDerivativeSet(uv_DitherTex, uv_DitherTexAlt, dx, dy);
    float2 uvPointillism;
    fixed3 pointillismSourceVis;
    fixed3 pointillismTriplanarWeights;
    ResolvePointillismUVAndDebugData(uv_DitherTex, uv_DitherTexAlt, float3(0, 0, 0), float3(0, 1, 0), 0.0, uvPointillism, pointillismSourceVis, pointillismTriplanarWeights);
    return GetDither3DColor_(uv_DitherTex, uvPointillism, float3(0, 1, 0), 0.0, screenPos, dx, dy, color, pointillismSourceVis, pointillismTriplanarWeights);
}
