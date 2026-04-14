/*
 * PainterlyCore.hlsl — Multi-layer pointillist rendering core.
 *
 * Uses procedural hexagonal-grid circular dots for a true pointillist look.
 * Four paint layers are composited with soft blending:
 *   Layer 0  Shadow    — dark tones, warm/cool shifted
 *   Layer 1  Body      — source color, chroma boosted
 *   Layer 2  Highlight — bright tones, desaturated
 *   Layer 3  Accent    — complementary hue, sparse
 *
 * Each layer uses a rotated UV so dots from different layers sit side by side,
 * producing the optical color mix characteristic of pointillism.
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
//  TEXTURE DECLARATIONS  (kept for material property compatibility)
// ═══════════════════════════════════════════════════════════════════

TEXTURE3D(_DitherTex);
SAMPLER(sampler_DitherTex);
TEXTURE2D(_DitherRampTex);
SAMPLER(sampler_DitherRampTex);
float4 _DitherTex_TexelSize;

// ═══════════════════════════════════════════════════════════════════
//  CONSTANTS
// ═══════════════════════════════════════════════════════════════════

static const float PAINTERLY_EPS = 0.0001;
static const float PAINTERLY_SQ3 = 1.7320508;

// Per-layer UV rotation (sin, cos).  Different angles prevent dot overlap.
static const float2 LAYER_ROT_SHADOW    = float2( 0.50000, 0.86603);  // 30 deg
static const float2 LAYER_ROT_BODY      = float2( 0.00000, 1.00000);  //  0 deg
static const float2 LAYER_ROT_HIGHLIGHT = float2( 0.86603, 0.50000);  // 60 deg
static const float2 LAYER_ROT_ACCENT    = float2( 0.70711, 0.70711);  // 45 deg

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

float3 PainterlyOKLabToRGBSafe(float3 lab)
{
    float3 rgb = PainterlyOKLabToRGB(lab);

    float lo = min(rgb.r, min(rgb.g, rgb.b));
    float hi = max(rgb.r, max(rgb.g, rgb.b));
    if (lo >= 0.0 && hi <= 1.0)
        return rgb;

    float tLo = 0.0;
    float tHi = 1.0;
    float3 grey = float3(lab.x, 0.0, 0.0);

    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float tMid   = (tLo + tHi) * 0.5;
        float3 trial = lerp(lab, grey, tMid);
        float3 c     = PainterlyOKLabToRGB(trial);
        float cLo    = min(c.r, min(c.g, c.b));
        float cHi    = max(c.r, max(c.g, c.b));
        if (cLo < 0.0 || cHi > 1.0)
            tLo = tMid;
        else
            tHi = tMid;
    }

    return saturate(PainterlyOKLabToRGB(lerp(lab, grey, tHi)));
}

// ═══════════════════════════════════════════════════════════════════
//  UV ROTATION HELPER
// ═══════════════════════════════════════════════════════════════════

float2 PainterlyRotateUV(float2 uv, float2 sc)
{
    return float2(uv.x * sc.y - uv.y * sc.x,
                  uv.x * sc.x + uv.y * sc.y);
}

// ═══════════════════════════════════════════════════════════════════
//  HASH FUNCTIONS  (for per-dot variation & jitter)
// ═══════════════════════════════════════════════════════════════════

float PainterlyHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 PainterlyHash22(float2 p)
{
    return float2(PainterlyHash21(p),
                  PainterlyHash21(p + 37.0));
}

// ═══════════════════════════════════════════════════════════════════
//  HEX-GRID CIRCULAR DOT LAYER
//
//  Returns a soft mask in [0,1].  1 = inside dot, 0 = canvas gap.
//  Uses a hexagonal grid for natural dot packing and antialiased
//  distance-field circles for proper round dots.
// ═══════════════════════════════════════════════════════════════════

float SampleDotLayer(float2 uv, float cellSize, float density,
                     float aaPixelSize, float jitterAmt)
{
    if (density < 0.005)
        return 0.0;
    density = saturate(density);

    // Scale UV into cell-normalised coordinates.
    float2 scaled = uv / cellSize;

    // Hexagonal grid: two staggered rectangular sublattices.
    float2 cellDim = float2(1.0, PAINTERLY_SQ3);

    float2 idA     = floor(scaled / cellDim);
    float2 centerA = (idA + 0.5) * cellDim;
    float2 offA    = scaled - centerA;

    float2 shifted = scaled - float2(0.5, PAINTERLY_SQ3 * 0.5);
    float2 idB     = floor(shifted / cellDim);
    float2 centerB = (idB + 0.5) * cellDim + float2(0.5, PAINTERLY_SQ3 * 0.5);
    float2 offB    = scaled - centerB;

    // Pick the nearest hex center.
    bool   useA   = dot(offA, offA) < dot(offB, offB);
    float2 off    = useA ? offA : offB;
    float2 cellId = useA ? idA : (idB + float2(1000.5, 1000.5));

    // Per-dot position jitter for a hand-painted feel.
    float2 jitter = (PainterlyHash22(cellId) - 0.5) * jitterAmt;
    off -= jitter;

    // Per-dot size variation (+-15%).
    float sizeVar = lerp(0.85, 1.15, PainterlyHash21(cellId + 7.0));

    // Dot radius — area proportional to density (sqrt for radius).
    float maxR = 0.52;   // slightly > 0.5 so full-density dots touch
    float r    = maxR * sqrt(density) * sizeVar;

    float dist = length(off);

    // Anti-aliasing width: thinner at higher _DotSharpness.
    float aa = aaPixelSize / max(_DotSharpness, 0.2);
    aa = max(aa, 0.015);  // minimum softness for smooth edges

    return 1.0 - smoothstep(r - aa, r + aa, dist);
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

    // -- Shadow -------------------------------------------------------
    float sL = max(0.0, L - _ValueSpread * 0.4);
    float sH = hue + _WarmCool * 0.4;
    float sC = C * max(0.2, 1.0 - _ValueSpread * 0.3);
    p.shadow = half3(PainterlyOKLabToRGBSafe(
                   float3(sL, sC * cos(sH), sC * sin(sH))));

    // -- Body ---------------------------------------------------------
    float bC = C * _Chroma;
    p.body = half3(PainterlyOKLabToRGBSafe(
                   float3(L, bC * cos(hue), bC * sin(hue))));

    // -- Highlight ----------------------------------------------------
    float hL = min(1.0, L + _ValueSpread * 0.3);
    float hH = hue - _WarmCool * 0.2;
    float hC = C * max(0.1, 1.0 - _ValueSpread * 0.5);
    p.highlight = half3(PainterlyOKLabToRGBSafe(
                       float3(hL, hC * cos(hH), hC * sin(hH))));

    // -- Accent -------------------------------------------------------
    float aH = hue + 3.14159265 + _HueShift * 6.28318;
    float aC = C * 0.6;
    float aL = clamp(L * 0.85 + 0.1, 0.0, 1.0);
    p.accent = half3(PainterlyOKLabToRGBSafe(
                   float3(aL, aC * cos(aH), aC * sin(aH))));

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
    // -- Exposure -----------------------------------------------------
    litColor = saturate(litColor * _Exposure);

    // Perceptual lightness drives per-layer density.
    float brightness = PainterlyRGBtoOKLab(litColor).x;

    // -- Palette ------------------------------------------------------
    PaintPalette pal = GeneratePalette(litColor);

    // -- Dot sizing ---------------------------------------------------
    // Cell size in UV space — dots are surface-stable.
    float cellSize = _DotScale * 0.004;

    // AA width: size of one pixel in cell-normalised coordinates.
    float pixelUV    = (length(dx) + length(dy)) * 0.5;
    float aaPixelSize = pixelUV / max(cellSize, PAINTERLY_EPS);

    float jitter = 0.15;

    // -- Per-layer rotated UVs ----------------------------------------
    float2 uvShadow    = PainterlyRotateUV(uv, LAYER_ROT_SHADOW);
    float2 uvBody      = uv;
    float2 uvHighlight = PainterlyRotateUV(uv, LAYER_ROT_HIGHLIGHT);
    float2 uvAccent    = PainterlyRotateUV(uv, LAYER_ROT_ACCENT);

    // -- Layer densities (smoothstep for soft transitions) ------------
    float shadowDen    = smoothstep(0.6, 0.05, brightness) * 0.75;
    float bodyDen      = smoothstep(0.0,  0.25, brightness)
                       * smoothstep(1.0,  0.50, brightness) * 0.65;
    float highlightDen = smoothstep(0.35, 0.80, brightness) * 0.60;
    float accentDen    = _AccentAmount
                       * smoothstep(0.15, 0.40, brightness)
                       * smoothstep(0.85, 0.50, brightness);

    // Canvas visibility — reduce ink in bright areas to show canvas.
    float inkScale = 1.0 - _CanvasShow * brightness * 0.5;
    shadowDen    *= inkScale;
    bodyDen      *= inkScale;
    highlightDen *= inkScale;

    // -- Sample each dot layer ----------------------------------------
    float shadowDot    = SampleDotLayer(uvShadow,    cellSize, shadowDen,
                                        aaPixelSize, jitter);
    float bodyDot      = SampleDotLayer(uvBody,      cellSize, bodyDen,
                                        aaPixelSize, jitter);
    float highlightDot = SampleDotLayer(uvHighlight, cellSize, highlightDen,
                                        aaPixelSize, jitter);
    float accentDot    = SampleDotLayer(uvAccent,    cellSize, accentDen,
                                        aaPixelSize, jitter);

    // -- Painter's algorithm (soft blend, last layer on top) ----------
    half3 result = _CanvasColor.rgb;
    result = lerp(result, pal.shadow,    shadowDot);
    result = lerp(result, pal.body,      bodyDot);
    result = lerp(result, pal.highlight, highlightDot);
    result = lerp(result, pal.accent,    accentDot);

    // -- Impasto (simulated paint thickness) --------------------------
    float anyDot  = saturate(shadowDot + bodyDot + highlightDot + accentDot);
    float topDot  = max(max(shadowDot, bodyDot),
                        max(highlightDot, accentDot));
    float center  = topDot;   // 0 at edge, 1 at dot centre
    float impastoFx = 1.0 + _Impasto
                    * (center * 0.15 - (1.0 - center) * 0.08) * anyDot;
    result *= impastoFx;

    return saturate(result);
}

#endif // PAINTERLY_CORE_INCLUDED
