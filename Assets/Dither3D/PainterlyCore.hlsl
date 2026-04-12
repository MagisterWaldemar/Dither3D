/*
 * PainterlyCore.hlsl — Multi-layer pointillist rendering core.
 *
 * Uses the existing Dither3D Bayer fractal textures for surface-stable dot
 * placement, but replaces the single-threshold dithering with a 4-layer
 * paint simulation:
 *   Layer 0  Shadow    — dark tones, warm/cool shifted
 *   Layer 1  Body      — source color, chroma boosted
 *   Layer 2  Highlight — bright tones, desaturated
 *   Layer 3  Accent    — complementary hue, sparse
 *
 * Each layer uses a rotated UV so dots from different layers do not overlap,
 * producing the side-by-side color mix characteristic of pointillism.
 * Layers are composited with painter's algorithm (last on top).
 *
 * Required material properties (must be declared in CBUFFER_START before
 * this file is included):
 *   float _DotScale, _DotSharpness, _Exposure,
 *         _HueShift, _Chroma, _ValueSpread, _WarmCool,
 *         _AccentAmount, _CanvasShow, _Impasto
 *   float4 _CanvasColor
 */

#ifndef PAINTERLY_CORE_INCLUDED
#define PAINTERLY_CORE_INCLUDED

// ═══════════════════════════════════════════════════════════════════
//  TEXTURE DECLARATIONS
// ═══════════════════════════════════════════════════════════════════

TEXTURE3D(_DitherTex);
SAMPLER(sampler_DitherTex);
TEXTURE2D(_DitherRampTex);
SAMPLER(sampler_DitherRampTex);

// Engine-provided — not in UnityPerMaterial CBUFFER.
float4 _DitherTex_TexelSize;

// ═══════════════════════════════════════════════════════════════════
//  CONSTANTS
// ═══════════════════════════════════════════════════════════════════

static const float PAINTERLY_EPS = 0.0001;

// Per-layer UV rotation (sin, cos).  Different angles prevent dot overlap.
static const float2 LAYER_ROT_SHADOW    = float2( 0.50000, 0.86603);  // 30 deg
static const float2 LAYER_ROT_BODY      = float2( 0.00000, 1.00000);  //  0 deg
static const float2 LAYER_ROT_HIGHLIGHT = float2( 0.86603, 0.50000);  // 60 deg
static const float2 LAYER_ROT_ACCENT    = float2( 0.70711, 0.70711);  // 45 deg

// Hard-coded size-variability — mostly density-driven, tiny size variation
// for a natural painterly feel.  0 = pure density, 1 = pure size.
static const float PAINTERLY_SIZE_VAR = 0.15;

// ═══════════════════════════════════════════════════════════════════
//  OKLab COLOUR SPACE
// ═══════════════════════════════════════════════════════════════════

float3 PainterlyRGBtoOKLab(float3 rgb)
{
    float3 lms = mul(float3x3(
        0.4122214708, 0.5363325363, 0.0514459929,
        0.2119034982, 0.6806995451, 0.1073969566,
        0.0883024619, 0.2817188376, 0.6299787005), rgb);
    float3 r = pow(max(lms, 0.0), 1.0 / 3.0);
    return mul(float3x3(
        0.2104542553,  0.7936177850, -0.0040720468,
        1.9779984951, -2.4285922050,  0.4505937099,
        0.0259040371,  0.7827717662, -0.8086757660), r);
}

float3 PainterlyOKLabToRGB(float3 lab)
{
    float3 r = mul(float3x3(
        1.0,  0.3963377774,  0.2158037573,
        1.0, -0.1055613458, -0.0638541728,
        1.0, -0.0894841775, -1.2914855480), lab);
    float3 lms = r * r * r;
    return mul(float3x3(
         4.0767416621, -3.3077115913,  0.2309699292,
        -1.2684380046,  2.6097574011, -0.3413193965,
        -0.0041960863, -0.7034186147,  1.7076147010), lms);
}

// ═══════════════════════════════════════════════════════════════════
//  UV ROTATION HELPER
// ═══════════════════════════════════════════════════════════════════

float2 PainterlyRotateUV(float2 uv, float2 sc)
{
    // sc = (sin(angle), cos(angle))
    return float2(uv.x * sc.y - uv.y * sc.x,
                  uv.x * sc.x + uv.y * sc.y);
}

// ═══════════════════════════════════════════════════════════════════
//  SURFACE-STABLE DOT FIELD  (shared SVD result)
// ═══════════════════════════════════════════════════════════════════

struct DotField
{
    float baseSpacing;
    float baseContrast;
    float dotsTotal;
    float invXres;
    float invZres;
};

DotField ComputeDotField(float2 dx, float2 dy)
{
    DotField df;

    float xRes     = _DitherTex_TexelSize.z;
    df.invXres     = _DitherTex_TexelSize.x;
    float dps      = xRes / 16.0;            // dots-per-side
    df.dotsTotal   = dps * dps;
    df.invZres     = 1.0 / df.dotsTotal;

    // SVD of the 2×2 Jacobian [dx ; dy].
    float4 v       = float4(dx, dy);
    float  Q       = dot(v, v);
    float  R       = dx.x * dy.y - dx.y * dy.x;
    float  disc    = sqrt(max(0.0, Q * Q - 4.0 * R * R));
    float2 freq    = sqrt(max(PAINTERLY_EPS, float2(Q + disc, Q - disc)) * 0.5);

    float scaleExp = exp2(_DotScale);
    df.baseSpacing = freq.y * scaleExp * dps * 0.125;

    df.baseContrast = _DotSharpness * scaleExp * 0.1;
    // Reduce contrast when the surface is highly stretched to prevent aliasing.
    df.baseContrast *= pow(max(PAINTERLY_EPS, freq.y) /
                           max(PAINTERLY_EPS, freq.x), 1.0);

    return df;
}

// ═══════════════════════════════════════════════════════════════════
//  SINGLE DOT-LAYER SAMPLE
//
//  Returns a value in [0,1].   > 0.5 → dot present (ink)
//                                < 0.5 → gap    (canvas)
//  Soft edges around 0.5 provide natural anti-aliasing.
// ═══════════════════════════════════════════════════════════════════

float SampleDotLayer(float2 layerUV, DotField df, float density)
{
    // Early-out: nothing to draw.
    if (density < 0.005)
        return 0.0;
    density = saturate(density);

    // Brightness-ramp linearises the threshold→coverage relationship.
    float2 rampCoord = float2(
        0.5 * df.invXres + (1.0 - df.invXres) * density, 0.5);
    float bCurve = SAMPLE_TEXTURE2D(_DitherRampTex, sampler_DitherRampTex,
                                     rampCoord).r;

    // Spacing adjustment (size-variability ≈ 0.15).
    float spMul    = pow(max(0.001, bCurve * 2.0 + 0.001),
                         -(1.0 - PAINTERLY_SIZE_VAR));
    float spacing  = df.baseSpacing * spMul;

    // Fractal level.
    float sLog     = log2(max(PAINTERLY_EPS, spacing));
    int   level    = (int)floor(sLog);
    float fracPart = sLog - (float)level;

    float2 scaledUV = layerUV / exp2((float)level);

    // Sub-layer (z in 3-D texture).
    float subLayer = lerp(0.25 * df.dotsTotal, df.dotsTotal, 1.0 - fracPart);
    subLayer       = (subLayer - 0.5) * df.invZres;

    // Bayer fractal sample.
    float pattern  = SAMPLE_TEXTURE3D(_DitherTex, sampler_DitherTex,
                                       float3(scaledUV, subLayer)).r;

    // Threshold & contrast → soft binary mask.
    float threshold = 1.0 - bCurve;
    float contrast  = df.baseContrast * spMul;
    float baseVal   = lerp(0.5, density, saturate(1.05 / (1.0 + contrast)));

    return saturate((pattern - threshold) * contrast + baseVal);
}

// ═══════════════════════════════════════════════════════════════════
//  PALETTE GENERATION
//
//  Four paint colours derived from the lit surface colour in OKLab.
// ═══════════════════════════════════════════════════════════════════

struct PaintPalette
{
    half3 shadow;
    half3 body;
    half3 highlight;
    half3 accent;
};

PaintPalette GeneratePalette(half3 color)
{
    float3 lab = PainterlyRGBtoOKLab(color);
    float  L   = lab.x;
    float2 ab  = lab.yz;
    float  C   = length(ab);
    float  hue = C > 0.001 ? atan2(ab.y, ab.x) : 0.0;

    PaintPalette p;

    // ── Shadow ───────────────────────────────────────────────────
    float sL = max(0.0, L - _ValueSpread * 0.4);
    float sH = hue + _WarmCool * 0.4;
    float sC = C * max(0.2, 1.0 - _ValueSpread * 0.3);
    p.shadow = half3(saturate(PainterlyOKLabToRGB(
                   float3(sL, sC * cos(sH), sC * sin(sH)))));

    // ── Body ─────────────────────────────────────────────────────
    float bC = C * _Chroma;
    p.body = half3(saturate(PainterlyOKLabToRGB(
                   float3(L, bC * cos(hue), bC * sin(hue)))));

    // ── Highlight ────────────────────────────────────────────────
    float hL = min(1.0, L + _ValueSpread * 0.3);
    float hH = hue - _WarmCool * 0.2;
    float hC = C * max(0.1, 1.0 - _ValueSpread * 0.5);
    p.highlight = half3(saturate(PainterlyOKLabToRGB(
                       float3(hL, hC * cos(hH), hC * sin(hH)))));

    // ── Accent ───────────────────────────────────────────────────
    float aH = hue + 3.14159265 + _HueShift * 6.28318;
    float aC = C * 0.45;
    float aL = clamp(L * 0.8 + 0.1, 0.0, 1.0);
    p.accent = half3(saturate(PainterlyOKLabToRGB(
                   float3(aL, aC * cos(aH), aC * sin(aH)))));

    return p;
}

// ═══════════════════════════════════════════════════════════════════
//  MULTI-LAYER PAINTERLY COMPOSITION
//
//  Entry point called from the fragment shader.
//  Expects ddx/ddy of the dither UV passed in as (dx, dy).
// ═══════════════════════════════════════════════════════════════════

half3 PainterlyComposite(float2 uv, float2 dx, float2 dy, half3 litColor)
{
    // ── Exposure ─────────────────────────────────────────────────
    litColor = saturate(litColor * _Exposure);

    // Perceptual lightness drives per-layer density.
    float brightness = PainterlyRGBtoOKLab(litColor).x;

    // ── Palette ──────────────────────────────────────────────────
    PaintPalette pal = GeneratePalette(litColor);

    // ── Shared dot-field (one SVD for all layers) ────────────────
    DotField df = ComputeDotField(dx, dy);

    // ── Per-layer rotated UVs ────────────────────────────────────
    float2 uvShadow    = PainterlyRotateUV(uv, LAYER_ROT_SHADOW);
    float2 uvBody      = uv;   // no rotation
    float2 uvHighlight = PainterlyRotateUV(uv, LAYER_ROT_HIGHLIGHT);
    float2 uvAccent    = PainterlyRotateUV(uv, LAYER_ROT_ACCENT);

    // ── Layer densities (smoothstep for soft transitions) ────────
    float shadowDen    = smoothstep(0.65, 0.0,  brightness) * 0.85;
    float bodyDen      = smoothstep(0.0,  0.35, brightness)
                       * smoothstep(1.0,  0.55, brightness) * 0.75;
    float highlightDen = smoothstep(0.3,  0.85, brightness) * 0.70;
    float accentDen    = _AccentAmount * 0.18
                       * smoothstep(0.15, 0.45, brightness)
                       * smoothstep(0.85, 0.55, brightness);

    // Canvas visibility scales total ink down in bright areas.
    float inkScale = 1.0 - _CanvasShow * brightness * 0.7;
    shadowDen    *= inkScale;
    bodyDen      *= inkScale;
    highlightDen *= inkScale;

    // ── Sample each layer ────────────────────────────────────────
    float shadowDot    = SampleDotLayer(uvShadow,    df, shadowDen);
    float bodyDot      = SampleDotLayer(uvBody,      df, bodyDen);
    float highlightDot = SampleDotLayer(uvHighlight, df, highlightDen);
    float accentDot    = SampleDotLayer(uvAccent,    df, accentDen);

    // ── Painter's algorithm (last layer on top) ──────────────────
    half3 result = _CanvasColor.rgb;
    float topDot = 0.0;

    // Shadow (bottom)
    float sMask = step(0.5, shadowDot);
    result = lerp(result, pal.shadow, sMask);
    topDot = lerp(topDot, shadowDot, sMask);

    // Body
    float bMask = step(0.5, bodyDot);
    result = lerp(result, pal.body, bMask);
    topDot = lerp(topDot, bodyDot, bMask);

    // Highlight
    float hMask = step(0.5, highlightDot);
    result = lerp(result, pal.highlight, hMask);
    topDot = lerp(topDot, highlightDot, hMask);

    // Accent (top)
    float aMask = step(0.5, accentDot);
    result = lerp(result, pal.accent, aMask);
    topDot = lerp(topDot, accentDot, aMask);

    // ── Impasto (simulated paint thickness) ──────────────────────
    float anyDot    = saturate(sMask + bMask + hMask + aMask);
    float center    = saturate((topDot - 0.5) * 3.0); // 0 at edge, 1 at centre
    float impastoFx = 1.0 + _Impasto
                    * (center * 0.15 - (1.0 - center) * 0.10) * anyDot;
    result *= impastoFx;

    return saturate(result);
}

#endif // PAINTERLY_CORE_INCLUDED
