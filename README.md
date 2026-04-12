# Surface-Stable Fractal Dithering

Surface-Stable Fractal Dithering is a novel dithering technique invented by Rune Skovbo Johansen for use on surfaces in 3D scenes. Dots stick to surfaces and maintain approximately constant screen-space size regardless of camera distance, achieved by dynamically adding or removing dots as the surface scales.

Here's a video explaining how it works:

[![Surface-Stable Fractal Dithering video on YouTube](https://img.youtube.com/vi/HPqGaIMVuLs/0.jpg)](https://www.youtube.com/watch?v=HPqGaIMVuLs)

Feature demo (RGB dithering, CMYK halftone, 1-bit low-res, pointillism):

[![Surface-Stable Fractal Dithering Demo video on YouTube](https://img.youtube.com/vi/EzjWBmhO_1E/0.jpg)](https://www.youtube.com/watch?v=EzjWBmhO_1E)

This repository contains shader and texture source files plus a Unity example project (Unity 2019.4, tested on 2022.3 and Unity 6). Includes Built-in RP shaders and a URP opaque variant.

Core implementation is in `Assets/Dither3D`. The original repository: [https://github.com/runevision/Dither3D](https://github.com/runevision/Dither3D)

---

## Installation

### Via Unity Package Manager (recommended)

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL…**
3. Paste and click **Add**:
   ```
   https://github.com/MagisterWaldemar/Dither3D.git?path=/Assets/Dither3D
   ```

Requires Unity 2019.4 or later. Git must be installed on your system ([git-scm.com](https://git-scm.com)).

To pin a specific version tag once published:
```
https://github.com/MagisterWaldemar/Dither3D.git?path=/Assets/Dither3D#1.0.0
```

### Manual

Copy the `Assets/Dither3D` folder into your Unity project.

---

## Available Shaders

| Shader | Use case |
|---|---|
| `Dither 3D/Opaque` | Standard (Built-in RP) opaque surfaces |
| `Dither 3D/URP/Opaque` | URP opaque surfaces |
| `Dither 3D/Cutout` | Alpha-tested / cutout surfaces |
| `Dither 3D/Particle Add` | Additive particle effects |
| `Dither 3D/Skybox` | Skybox |

---

## Converting an Existing Material to Dither 3D

### From Standard (Built-in RP)

This is the most common starting point. The Dither 3D/Opaque shader is intentionally designed to mirror Standard shader properties.

1. Select the material in the **Project** window.
2. In the **Inspector**, click the **Shader** dropdown → **Dither 3D → Opaque**.
3. Re-assign your textures. These properties carry over with the same names:
   - `_MainTex` (Albedo)
   - `_BumpMap` (Normal Map)
   - `_EmissionMap` / `_EmissionColor`
   - `_Metallic` / `_Glossiness`
4. Under **Dither Input Brightness**, adjust `Exposure` and `Offset` to match the material's previous perceived brightness. Start with `Exposure = 1`, `Offset = 0` and tweak from there.
5. Set a **Pattern** size in the material. `4×4` is a good general-purpose starting point. Larger patterns produce finer detail.

> **If the material turns magenta**: you're using the URP shader on a Built-in RP project or vice versa. Switch to the pipeline-correct variant.

### Batch Conversion via Prefab Tool

For converting many materials at once, use **Tools → Dither 3D → Prefab Conversion**:

- **Dry Run** — previews the conversion with no file writes and logs unmapped properties.
- **Convert** — writes new materials, prefab variants, and a JSON manifest report.
- **Prefab Preview panel** — side-by-side Source vs Converted view (in-memory, no writes) before committing.

The converter uses `ShaderAdapterRegistry` + `DitherStyleProfile` rules. It only applies explicitly configured mappings — no implicit property guessing. Unmapped properties are logged with warnings.

**Pipeline-aware adapter bootstrap:**
- `ShaderAdapterRegistry.CreatePrioritizedRegistryForActivePipeline()` — auto-selects URP or Built-in adapters based on the active pipeline.
- `ShaderAdapterRegistry.CreatePrioritizedNonUrpRegistry()` — forces Built-in-only mappings.

Built-in adapters include:
- `Standard` → `Dither 3D/Opaque`
- `Nature/SpeedTree` / `Nature/SpeedTree8` → `Dither 3D/Cutout`
- URP adapter: `Universal Render Pipeline/Lit` → `Dither 3D/URP/Opaque` (falls back to `Dither 3D/Opaque` if unavailable)

### From URP Lit

Same steps as above, but use **Dither 3D → URP/Opaque**. The URP adapter maps `_Smoothness` (URP name) to `_Glossiness` (Dither 3D name) automatically.

---

## Scene Setup: Dither3DGlobalProperties Component

Before the dithering renders correctly, you need one `Dither3DGlobalProperties` component in the scene. Without it, global shader keywords won't be set and results will look wrong.

1. Select any persistent GameObject (dedicated manager object or main camera).
2. **Add Component** → search **Dither 3D Global Properties**.
3. Set **Color Mode**:
   - `Grayscale` — converts to grayscale, dithers once.
   - `RGB` — dithers each channel separately (default for most use cases).
   - `CMYK` — converts to CMYK, applies halftone rotations per channel, converts back. Produces a classic halftone print look.
4. Leave other settings at defaults initially.

The component automatically finds all materials using a Dither 3D shader (identified by the `_DitherTex` property) and propagates global keywords at runtime.

---

## Pointillism: Step-by-Step Setup

Pointillism is an opt-in feature that replaces flat dither output with multi-ink painterly color composition. It's disabled by default and fully backward-compatible.

### Step 1 — Enable Pointillism on the Material

On your material's Inspector, check **Enable Pointillism**. You'll immediately see color quantization applied on top of the dither.

### Step 2 — Choose a Color Model

| Model | What it does |
|---|---|
| **OKLab** (recommended) | Quantizes perceptual lightness only. Hue and chroma are preserved. Uses a perceptually uniform color space so tonal steps feel even. |
| **Legacy / Luminance** | Quantizes by standard luminance weights (0.299 R, 0.587 G, 0.114 B). Scales the original color to preserve hue. Simpler but less perceptually accurate. |
| **Legacy / Perceptual HSL** | Quantizes hue and lightness independently. Controls hue via `Hue Steps` and lightness via `Color Steps`. |

OKLab is recommended unless you specifically want the legacy look.

With OKLab, also set **Pointillism Max Chroma**: lower values (e.g. `0.24`) reduce saturation clipping and color-flip artifacts in highly saturated areas. The default `0.32` is safe for most content.

### Step 3 — Set Color Steps

`Color Steps` controls how many discrete lightness/luminance levels exist in the quantized palette. More steps = more tones, less stylized, less visible banding.

- `4–6`: Strong posterization, graphic look
- `8` (default): Good balance
- `12–16`: Subtle, closer to source

The shader uses **trinary dithering** internally — it can blend between three adjacent tonal slots per pixel rather than just two, producing richer color transitions even at low step counts.

### Step 4 — Choose a Coord Source

The pointillism coordinate source determines what UV space is used for stroke direction sampling. Wrong coord source = smeared or swimming strokes.

| Source | When to use |
|---|---|
| **UV** (default) | Well-UV-mapped surfaces. Strokes anchor to surface UV space. |
| **ObjectSpace** | Surfaces with stretched or tiled UVs (e.g. terrain, rocks). Projects onto the XZ plane in object space. |
| **TriplanarObjectSpace** | Heavily UV-distorted surfaces or any mesh without good UVs. Blends three planar projections weighted by surface normal. |
| **AltUVHook** | Custom UV from code via `GetDither3DColorAltUV(...)`. |

For `ObjectSpace` and `TriplanarObjectSpace`, adjust **Pointillism Object Scale** to control coordinate density (higher = more patterns per unit). For triplanar, **Triplanar Sharpness** controls how hard the transitions are between planes (default `4.0`; higher = sharper seams).

### Step 5 — Tune Stroke Directionality

Strokes are computed from screen-space UV derivatives. These two parameters control the "brushstroke" feel:

- **Stroke Directionality** (`0–1`): How much the dither threshold samples are offset along the dominant UV derivative direction. `0` = circular dots, `1` = highly elongated strokes.
- **Stroke Length** (`0–1`): How far the channel offset extends. Scales the spread of sample points.
- **Blue Noise Stroke Mix** (`0–1`): Blends the derivative-based stroke direction with a random blue-noise angle. `0` = fully geometry-driven direction, `1` = fully random. Adds organic variation.

Start with `Stroke Directionality = 0.5`, `Stroke Length = 0.4`, `Blue Noise Stroke Mix = 0.3`.

### Step 6 — Choose a Composition Mode

This is the most important decision for the final look:

#### LegacyQuantized (default, simpler)
Each pixel samples a single quantized color from the tone palette. No role system. Straightforward output, minimal tuning needed. Good starting point and safe fallback.

#### RoleComposed (painterly)
Each pixel is probabilistically assigned one of four "ink roles":

| Role | What it looks like |
|---|---|
| **Foundation** | Desaturated base. `Base Muting` controls how much chroma is compressed toward grey. |
| **Chroma** | Saturated base color. `Chroma Push` controls how much saturation is boosted above the source color. |
| **Complement** | Opposite hue accent. Sparse. Requires sufficient chroma (> 0.05) and surface detail to activate. |
| **Highlight** | Bright desaturated accent. Activates in bright areas and on high-detail surfaces. |

The shader calculates weights for each role from the target color's OKLab representation, detail signals (screen-space albedo and normal gradients), and your tuning parameters. It then probabilistically selects one ink per pixel using the rank threshold, keeping expected output color close to the target shading.

**Switching from LegacyQuantized to RoleComposed:**
Apply the **Safety Fallback Profile** first as a starting point:
- **Tools → Dither 3D → Painterly Validation → Apply Safety Fallback Profile (Selected Materials)**

This sets conservative values that are stable across most content types. Tune from there.

### Step 7 — Tune RoleComposed Parameters (in order)

Tune in this sequence to avoid chasing artifacts:

**1. Foundation/Chroma balance first:**
- `Base Muting` (`0–1`): How much the foundation ink desaturates toward grey. `0.35` is balanced; `0.5+` makes the look more muted/earthy.
- `Chroma Push` (`0–2`): How much the chroma ink boosts saturation above the source. `0.6` is balanced; above `1.0` produces highly saturated accent strokes.

**2. Accent envelope:**
- `Complementary Accent Amount` (`0–1`): Maximum contribution from the opposite-hue accent. `0` disables complements entirely. Keep low (`0.10–0.20`) to start.
- `Highlight Accent Strength` (`0–1`): Controls how often and how strongly highlight inks appear in bright/specular regions. Lower for stability.

**3. Accent sparsity:**
- `Accent Sparsity` (`0–1`): Higher values = fewer accent pixels scattered across the surface. Start at `0.75`; raise to `0.85–0.92` if accents look noisy.

**4. Detail sensitivities last:**
- `Detail Sensitivity (Albedo)` (`0–2`): How strongly albedo gradients (detected via screen-space derivatives) redistribute weight toward accent roles. Lower this if noisy/high-frequency textures produce too much accent noise.
- `Detail Sensitivity (Normal)` (`0–2`): Same but driven by normal map gradients. Useful for surfaces where shape detail should drive accent placement.

### Step 8 — Color Clamping and LUT (Optional)

- **Clamp Min/Max Color**: Restricts the output color range before composition. Useful for palette-limited looks (e.g. clamp max to `(0.9, 0.9, 0.9)` for a faded-print feel).
- **Pointillism LUT**: A 1D lookup texture applied per-channel after composition. Lets you apply a color grade. Use **Tools → Dither 3D → Configure Pointillism LUT Import (Selected)** to set correct import settings on the texture. Set `LUT Blend = 0` if no LUT is assigned.

---

## Pointillism Debug Mode

Enable **Debug Fractal** on the `Dither3DGlobalProperties` component to visualize coordinate sources:

| Coord source | Debug color |
|---|---|
| UV | Light blue `(0.1, 0.7, 1.0)` |
| AltUVHook | Golden yellow `(1.0, 0.85, 0.1)` |
| ObjectSpace | Orange `(1.0, 0.4, 0.1)` |
| TriplanarObjectSpace | RGB = X/Y/Z blend weights |

Use this to confirm the right source is active and to diagnose swimming/smearing artifacts.

---

## Pointillism Preset Quick Reference

Use these as starting points via the **Pointillism Preset Override** in `Dither3DGlobalProperties`, or apply them per-material manually.

| Preset | Use case |
|---|---|
| **Conservative** | Heavy motion, TAA/upscaler scenes, specular-heavy content. Low stroke, reduced accents, high temporal stickiness. |
| **Balanced** (default) | General gameplay, mixed lighting. Good middle-ground across all parameters. |
| **Aggressive** | Close/static shots where strong painterly accents are intentional. High stroke, more color steps, faster temporal response. |

Full parameter values:

| Property | Conservative | Balanced | Aggressive |
|---|---:|---:|---:|
| Stroke Directionality | 0.35 | 0.50 | 0.80 |
| Stroke Length | 0.25 | 0.40 | 0.70 |
| Blue Noise Stroke Mix | 0.15 | 0.30 | 0.60 |
| Color Steps | 6 | 8 | 12 |
| Color Model | OKLab | OKLab | OKLab |
| Max Chroma | 0.24 | 0.32 | 0.40 |
| Clamp Range | 0.05–0.95 | 0–1 | 0–1 |
| Composition Mode | RoleComposed | RoleComposed | RoleComposed |
| Base Muting | 0.50 | 0.35 | 0.22 |
| Chroma Push | 0.40 | 0.60 | 1.20 |
| Complement Accent Amount | 0.12 | 0.20 | 0.45 |
| Accent Sparsity | 0.88 | 0.75 | 0.55 |
| Detail Sensitivity (Albedo/Normal) | 0.80/0.75 | 1.00/1.00 | 1.35/1.40 |
| Highlight Accent Strength | 0.20 | 0.35 | 0.70 |
| Phase Speed / Hysteresis / Min Dot | 0.08/0.90/0.18 | 0.15/0.80/0.12 | 0.45/0.45/0.04 |

---

## Pointillism Tuning by Content Type

| Content | Starting preset | Key adjustments |
|---|---|---|
| Low-chroma gradients | Conservative | High `Base Muting` (0.45–0.60), low `Chroma Push` (0.30–0.55), `Accent Sparsity >= 0.85` |
| High-frequency albedo (detailed textures) | Balanced | Raise `Accent Sparsity` to 0.80–0.92 first; lower `Detail Sensitivity (Albedo)` if accent noise persists |
| Strong normals, low albedo (e.g. sculpted rock) | Balanced or Conservative | Keep `Detail Sensitivity (Normal)` at 0.70–1.00; lower `Highlight Accent Strength` for stability |
| Specular-heavy / shiny materials | Conservative | Lower `Highlight Accent Strength`; increase `Hysteresis`; switch to `LegacyQuantized` if popping persists |
| Flat stylized content | Conservative | High `Base Muting`, high `Accent Sparsity`; fall back to `LegacyQuantized` if over-stylized |

---

## Troubleshooting

| Symptom | First control | Second control | Fallback |
|---|---|---|---|
| Accent flicker/pop in motion | Increase `Accent Sparsity` | Increase `Hysteresis`, lower `Phase Speed` | `Conservative` preset |
| Over-saturated / blotchy accents | Lower `Chroma Push` | Lower `Complementary Accent Amount` | Safety fallback profile |
| Highlight sparkle instability | Lower `Highlight Accent Strength` | Increase `Blue Noise Min Dot` | Switch to `LegacyQuantized` |
| Detail-heavy surfaces look noisy | Lower `Detail Sensitivity (Albedo/Normal)` | Increase `Base Muting` | Reduce accents |
| Flat content looks over-stylized | Increase `Base Muting` | Increase `Accent Sparsity` | `LegacyQuantized` |
| Strokes swim / don't anchor to surface | Check `Coord Source` matches UV layout | Use `ObjectSpace` or `TriplanarObjectSpace` | Enable Debug Fractal to diagnose |
| Material turns magenta | Use pipeline-correct shader (`URP/Opaque` vs `Opaque`) | Re-import shader/material | — |

---

## Basic Dithering (No Pointillism)

The core dithering properties available on every Dither 3D material:

**Dither Input Brightness**
- `Exposure` — Multiplier on input brightness (default 1).
- `Offset` — Additive offset on input brightness (default 0).

**Dither Settings**
- `Dot Scale` — Exponentially scales dot size.
- `Pattern` — `1×1`, `2×2`, `4×4`, `8×8`. Larger = finer dot detail.
- `Pattern Source` — `Bayer` (default) or `BlueNoiseFractal` (requires generated rank texture).
- `Dot Size Variability` — `0` = Bayer-style (shading controls dot count); `1` = halftone-style (shading controls dot size).
- `Dot Contrast` — Anti-aliasing quality. Default `1` = perfect anti-aliasing.
- `Stretch Smoothness` — Smoothing for anisotropic dots (default 1).

**Global Options (via Dither3DGlobalProperties)**
- `Color Mode` — Grayscale / RGB / CMYK.
- `Inverse Dots` — Flips dot polarity. For CMYK: disabled = dark ink on light (recommended). For RGB/Grayscale: disabled = bright dots on dark.
- `Radial Compensation` — Enlarges dots toward screen edges to maintain stability under perspective camera rotation.
- `Quantize Layers` — When enabled, prevents dots from smoothly growing/shrinking as they appear/disappear.
- `Debug Fractal` — Enables debug overlays (grayscale: fractal channels; pointillism: coord source colors).

---

## BlueNoiseFractal Setup (Optional)

Blue noise pattern source reduces structured Bayer artifacts. Opt-in.

1. Generate textures via **Tools → Dither 3D → Blue Noise Fractal Generator**.
2. Assign the generated texture to `Blue Noise Rank Texture` on the material.
3. Switch `Pattern Source` to `BlueNoiseFractal`.
4. Optionally generate and assign a `Blue Noise Phase Texture` for extra temporal decorrelation.
5. Tune `Phase Speed`, `Hysteresis`, `Min Dot` — or use the global **Temporal Preset Override** in `Dither3DGlobalProperties` (Conservative / Balanced / Aggressive).

Generated textures must be imported as: Linear (sRGB off), No mipmaps, Uncompressed, Repeat wrap, Point filtering. The generator applies these settings automatically.

If rank texture is missing, the shader silently falls back to Bayer.

| Property | Balanced start | Notes |
|---|---:|---|
| Phase Speed | 0.15 | Lower = more stable, less variation |
| Hysteresis | 0.80 | Higher = more stickiness, less popping |
| Min Dot | 0.12 | Higher preserves small dots during motion |

---

## Painterly Validation Tooling

Use **Tools → Dither 3D → Painterly Validation → Run Baseline Comparison + Capture** to compare `LegacyQuantized` vs `RoleComposed` across reference scenes.

Outputs to `Assets/Dither3D/ValidationReports`:
- `PainterlyBaselineComparison.json` / `.csv` / `.txt`
- Capture placeholders under `ValidationReports/Captures`

Default validation scene-set (`PainterlyValidationSceneSet.asset`) includes: low-chroma gradients, high-frequency albedo, strong-normal/low-albedo detail, mixed highlights.

**Tuning sequence:**
1. Foundation/Chroma: `Base Muting`, `Chroma Push`
2. Accent Envelope: `Complementary Accent Amount`, `Highlight Accent Strength`
3. Accent Sparsity: `Accent Sparsity`
4. Detail Sensitivities: `Detail Sensitivity (Albedo/Normal)`

**Apply Safety Fallback Profile** (**Tools → Dither 3D → Painterly Validation → Apply Safety Fallback Profile (Selected Materials)**):
```
Composition Mode = RoleComposed
Base Muting = 0.52 | Chroma Push = 0.38
Complementary Accent Amount = 0.10 | Accent Sparsity = 0.90
Detail Sensitivity (Albedo) = 0.75 | Detail Sensitivity (Normal) = 0.70
Highlight Accent Strength = 0.15
Phase Speed = 0.08 | Hysteresis = 0.90 | Min Dot = 0.18
```

---

## Files Reference

| File | Purpose |
|---|---|
| `Dither3DInclude.cginc` | Core shader include — all dithering and pointillism logic |
| `Dither3DOpaque.shader` | Built-in RP opaque shader |
| `Dither3DOpaqueURP.shader` | URP opaque shader |
| `Dither3DCutout.shader` | Alpha-tested shader |
| `Dither3DParticleAdd.shader` | Additive particle shader |
| `Dither3DSkybox.shader` | Skybox shader |
| `Dither3D_1x1/2x2/4x4/8x8.asset` | Pre-generated 3D dither pattern textures |
| `Dither3D_1x1/2x2/4x4/8x8.png` | PNG previews of patterns (for inspection; not used by shaders) |
| `Dither3DTextureMaker.cs` | Editor script to regenerate pattern textures from scratch |

Regenerate 3D textures via **Assets → Create → Dither 3D Texture → …**

> Do not import the PNG files as 3D textures in Unity — a Unity importer inconsistency reverses the layer order, which breaks the fractal effect.

---

## Known Limitations

- `BlueNoiseFractal` uses generated rank textures and does not guarantee strict mathematical fractal self-similarity equivalent to Bayer matrices.
- Object-space and triplanar pointillism coord sources are only available on opaque/cutout surface shaders.
- Pointillism LUT expects a simple horizontal 1D-style texture and applies per-channel remapping only (no 3D LUT).
- Prefab preview conversion does not run post-processing, lighting, or animation — use it as a fast preflight check only.
- Painterly validation capture emits deterministic placeholders by default; real screenshot capture requires per-project camera rig integration.

---

## Discussion: What "Surface-Stable" Means

A dither pattern is surface-stable when:
1. Each dot sticks to the surface it belongs to — under both object and camera movement.
2. When the surface scales larger on screen (zoom in), dots may be *added* but never removed. When it scales smaller, dots may be *removed* but never added.

The second constraint is what's new here. Most scale-fading dither approaches violate it by having dots both appear and disappear during zoom. This implementation satisfies it by exploiting a fractal property of Bayer matrices — each quadrant of a Bayer matrix contains a valid sub-set of the full matrix, enabling dots to appear hierarchically.

The `BlueNoiseFractal` mode approximates this using generated rank textures, but does not guarantee strict mathematical fractal self-similarity.

### Other Pattern Types

- **Regular non-Bayer patterns** (triangles, hexes, √2 rectangles): straightforward to implement, no fundamental obstacles.
- **True blue-noise patterns**: not straightforward. Requires a self-similar blue-noise pattern where dots in each recursive quadrant align with dots in the full pattern. The "Recursive Wang Tiles" paper approximates this but does not appear to fully satisfy the second constraint.

---

## License

Licensed under the [Mozilla Public License, v. 2.0](https://mozilla.org/MPL/2.0/).

In short: changes/improvements to this implementation must be shared freely. The rest of your game or application source code is not subject to this license. Commercial and proprietary games using this implementation are permitted.
