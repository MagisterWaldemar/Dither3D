# Surface-Stable Fractal Dithering

Surface-Stable Fractal Dithering is a novel form of dithering invented by Rune Skovbo Johansen for use on surfaces in 3D scenes.

What's unique about it is that the dots in the dither patterns stick to surfaces, and yet the dot sizes and spacing remain approximately constant on the screen, even as surfaces move closer by or further away. This is achieved by dynamically adding or removing dots as needed.

Here's a video explaining how it works:

[![Surface-Stable Fractal Dithering video on YouTube](https://img.youtube.com/vi/HPqGaIMVuLs/0.jpg)](https://www.youtube.com/watch?v=HPqGaIMVuLs)

And here's a feature demo video (with music!) showing color RGB dithering, CMYK halftone, true 1-bit low-res effects, and much more:

[![Surface-Stable Fractal Dithering Demo video on YouTube](https://img.youtube.com/vi/EzjWBmhO_1E/0.jpg)](https://www.youtube.com/watch?v=EzjWBmhO_1E)

This repository contains the shader and texture source files, and a Unity example project demonstrating their use. The example project is made with Unity 2019.4 and is also tested in Unity 2022.3 and Unity 6. It's based on the Forward rendering path in the Built-in Render Pipeline.

The core implementation is located in the folder `Assets/Dither3D`. The remaining files relate to the Unity example project.

The original version of this repository can be found at:  
[https://github.com/runevision/Dither3D](https://github.com/runevision/Dither3D)

## Installation via Unity Package Manager

You can install this package directly from Git using Unity Package Manager.

1. Open **Unity Package Manager** (Window → Package Manager).
2. Click the **+** button → **Add package from git URL…**
3. Paste the following URL and click **Add**:

```
https://github.com/MagisterWaldemar/Dither3D.git?path=/Assets/Dither3D
```

The `?path=/Assets/Dither3D` suffix tells Unity where the `package.json` manifest lives inside the repository. Unity 2019.4 or later is required.

> **Note:** Git must be installed on your system. Download from [https://git-scm.com](https://git-scm.com) and restart Unity if needed.

To install a specific version tag (once tags are published), append `#<tag>`, e.g.:
```
https://github.com/MagisterWaldemar/Dither3D.git?path=/Assets/Dither3D#1.0.0
```

## Dither Properties

Each material that uses the dithering has the following dither-specific number properties:

**Dither Input Brightness**

- `Exposure`  
Exposure to apply to input brightness (default 1).
- `Offset`  
Offset to apply to input brightness (default 0).

**Dither Settings**

- `Dot Scale`  
Value that exponentially scales the dots.
- `Pattern Source`  
`Bayer` (default, previous behavior) or `BlueNoiseFractal` (optional).
- `Dot Size Variability`  
0 = shading controls dot count "Bayer style" (default);  
1 = shading controls dot sizes "half-tone style".
- `Dot Contrast`  
A value of 1 produces perfect anti-aliasing (default 1).
- `Stretch Smoothness`  
How much to smooth anisotropic dots (default 1).
- `Blue Noise Rank Texture`  
Optional tileable rank texture (0..1) used by `BlueNoiseFractal`.
- `Blue Noise Phase Texture (Optional)`  
Optional texture for extra temporal phase decorrelation.
- `Blue Noise Phase Speed`  
Rate of deterministic temporal phase evolution.
- `Blue Noise Hysteresis`  
Stickiness of the current phase before blending to the next.
- `Blue Noise Min Dot`  
Minimum-dot equivalent for maintaining small dots during motion.
- `Enable Pointillism`
Enables pointillist post-dither color stylization (default off, backwards compatible).
- `Stroke Directionality`
How strongly channel sampling is offset along an oriented stroke direction.
- `Stroke Length`
Length of directional stroke offsets used in pointillism ranking.
- `Color Steps`
Number of quantized color levels used for color dithering.
- `Pointillism Object Scale`
Scale multiplier for object-space/triplanar pointillism coordinates.
- `Pointillism Triplanar Sharpness`
Blend sharpness for triplanar object-space coordinate mixing.
- `Pointillism Coord Source`
`UV` by default, with optional `AltUVHook`, `ObjectSpace`, and `TriplanarObjectSpace`.
- `Clamp Min Color` / `Clamp Max Color`
Per-channel output clamp range before color quantization.
- `Pointillism LUT (Optional)` / `Pointillism LUT Blend`
Optional LUT-driven color skew blended with pointillism output.

**Global Options**

Furthermore, the following global toggle properties can be set via the `Dither3DGlobalProperties` component:

- `Color Mode`  
Can be set to Grayscale, RGB or CMYK. Grayscale converts the color to grayscale and runs the dithering once on that. RGB runs the dithering separately for the red, green and blue color channel. CMYK converts the color to CMYK, runs the dithering on each of those with traditional halftone rotations applied, and converts back to RGB.
- `Inverse Dots`  
For Grayscale and RGB, disabled produces bright dots on dark background (recommended) while enabled produces dark dots on light background. For CMYK, disabled produces dark dots on light background (like ink) while enabled produces light dots on dark background. Here, disabled works best if the shapes of the individual dots are clearly visible, but it can produce significant banding. For smaller dot sizes, enabled is recommended.
- `Radial Compensation`  
When using a perspective camera, dots must be larger towards the edge of the screen in order to be stable under camera rotation. The Radial Compensation feature can be enabled to achieve this.
- `Quantize Layers`  
When disabled, dots may grow or shrink in size when they appear or disappear, respectively. Even when enabled, dots may still be partially cut off, but that's a separate and unavoidable effect.
- `Debug Fractal`  
Displays debug overlays when enabled.  
In grayscale mode, this shows fractal debug channels (U, V, and layer index) over the dither output.  
When pointillism is enabled, it additionally shows coordinate-source debugging (UV/AltUV/ObjectSpace source colors, or triplanar XYZ blend weights as RGB).

`Dither3DGlobalProperties` now also includes a temporal preset override for blue-noise mode:

- `Conservative`  
Slow phase evolution and high stickiness (best temporal stability for heavy motion/upscalers).
- `Balanced` (default)  
Middle-ground speed and stickiness.
- `Aggressive`  
Faster phase evolution and reduced stickiness (better for close/static shots).

The `Dither3DGlobalProperties` component can also be used to override the non-global properties of all dither materials at once.

`Dither3DGlobalProperties` also provides a dedicated pointillism preset override that groups stroke + palette + temporal tuning:

- `Conservative`
Lower stroke directionality/length, reduced color steps, slightly clamped palette, and high temporal stickiness.
- `Balanced` (default)
Middle-ground values across stroke, palette, and temporal controls.
- `Aggressive`
Higher stroke directionality/length, more color steps, and faster/lower-stickiness temporal response.

## BlueNoiseFractal setup

1. Keep existing Bayer workflow as-is (default behavior).  
2. Generate blue-noise textures via **Tools → Dither 3D → Blue Noise Fractal Generator**.  
3. Assign `Blue Noise Rank Texture` to the material.  
4. (Optional) Generate and assign a `Blue Noise Phase Texture`.  
5. Switch `Pattern Source` to `BlueNoiseFractal`.  
6. Tune `Phase Speed`, `Hysteresis`, `Min Dot`, or use global temporal preset overrides.

Generated blue-noise textures are imported with:
- Linear (sRGB off)
- No mipmaps
- Uncompressed
- Repeat wrap
- Point filtering

If a blue-noise rank texture is missing, shaders safely fallback to the Bayer path.

## Pointillism setup

1. Keep your existing material workflow unchanged (pointillism is opt-in and off by default).
2. (Recommended) Assign a tileable `Blue Noise Rank Texture` and optional phase texture.
3. Enable `Pointillism` on the material.
4. Tune `Stroke Directionality`, `Stroke Length`, and `Color Steps`.
5. Choose `Pointillism Coord Source`:
   - `UV`: standard UV-anchored mode.
   - `AltUVHook`: alternate UVs from `GetDither3DColorAltUV(...)`.
   - `ObjectSpace`: uses object-space XZ projection.
   - `TriplanarObjectSpace`: object-space triplanar blend for stretched/poor UVs.
6. Set `Clamp Min/Max Color` to restrict palette range.
7. (Optional) Assign `Pointillism LUT` and increase `Pointillism LUT Blend`.
8. (Optional) Use **Tools → Dither 3D → Configure Pointillism LUT Import (Selected)** on LUT textures.
9. For coordinate tuning, enable global `Debug Fractal`:
   - UV source: light blue (0.1, 0.7, 1.0)
   - AltUVHook source: golden yellow (1.0, 0.85, 0.1)
   - ObjectSpace source: orange (1.0, 0.4, 0.1)
   - TriplanarObjectSpace source: RGB = X/Y/Z blend weights

Pointillism uses the same deterministic temporal phase/hysteresis controls as blue-noise fractal mode, so conservative/balanced temporal presets also apply to pointillism stability behavior.
You can also use the dedicated pointillism preset override in `Dither3DGlobalProperties` to set stroke/palette/temporal as one grouped choice.

## Files

A brief overview of the files in the `Assets/Dither3D` folder:

The central shader include file with the dithering implementation:

- `Dither3DInclude.cginc`

Included shader files that use the dithering implementation:

- `Dither3DOpaque.shader`
- `Dither3DCutout.shader`
- `Dither3DParticleAdd.shader`
- `Dither3DSkybox.shader`

The dither shaders rely on a 3D texture with dither patterns. These come in several versions with different amounts of dots. In the materials using the dither shaders, you can freely switch between these 3D textures.

- `Dither3D_1x1.asset`
- `Dither3D_2x2.asset`
- `Dither3D_4x4.asset`
- `Dither3D_8x8.asset`

Although the 3D textures are available in the repository, a script is also included which can generate them from scratch. You can do this by using the menu items under the grouping `Assets/Create/Dither 3D Texture/...`. 

- `Dither3DTextureMaker.cs`

The script also generates PNG image files, where the different layers are laid out bottom to top. These PNG files are not used for anything and can be safely deleted, but they are easier to inspect and study than the native 3D textures. Note that later versions of Unity can in principle import 3D textures from such 2D images, but due to an inconsistency between Unity's 3D texture API and their 3D texture importer, the layers will appear in reverse order if this is attempted, and this will cause the fractal dithering effect to not work.

- `Dither3D_1x1.png`
- `Dither3D_2x2.png`
- `Dither3D_4x4.png`
- `Dither3D_8x8.png`

Additional editor tooling for optional blue-noise rank/phase textures:

- `Tools/Dither 3D/Blue Noise Fractal Generator`

## BlueNoiseFractal parameter quick table

| Property | Suggested start (Balanced) | Notes |
|---|---:|---|
| Phase Speed | 0.15 | Lower for more temporal stability |
| Hysteresis | 0.80 | Higher = more stickiness, less popping |
| Min Dot | 0.12 | Higher helps preserve tiny dots in motion |

## Pointillism parameter quick table

| Property | Suggested start (Balanced) | Notes |
|---|---:|---|
| Enable Pointillism | On (per material) | Off by default for backward compatibility |
| Stroke Directionality | 0.5 | Higher increases directional "stroke" feel |
| Stroke Length | 0.4 | Higher extends channel offsets |
| Color Steps | 8 | Lower = flatter posterization, higher = smoother |
| Pointillism Coord Source | UV | UV / AltUVHook / ObjectSpace / TriplanarObjectSpace |
| Object Scale | 1.0 | Scales object-space coordinate density |
| Triplanar Sharpness | 4.0 | Higher = harder blend transitions between planes |
| Clamp Min/Max Color | (0,0,0) / (1,1,1) | Narrow for palette-limited looks |
| LUT Blend | 0.25 | Set 0 if no LUT is assigned |

## Pointillism grouped preset quick table

| Preset | Stroke Directionality | Stroke Length | Color Steps | Clamp Range | Phase Speed / Hysteresis / Min Dot |
|---|---:|---:|---:|---|---|
| Conservative | 0.35 | 0.25 | 6 | (0.05..0.95) | 0.08 / 0.90 / 0.18 |
| Balanced | 0.50 | 0.40 | 8 | (0..1) | 0.15 / 0.80 / 0.12 |
| Aggressive | 0.80 | 0.70 | 12 | (0..1) | 0.45 / 0.45 / 0.04 |

## Validation scenarios

Use these when tuning temporal settings:

1. Static object + whipping camera
2. Moving/rotating object + static camera
3. Thin geometry / high-frequency albedo
4. UV-stretched mesh

Expected result: Bayer mode remains unchanged; BlueNoiseFractal has reduced shimmer/pop when rank texture + conservative/balanced settings are used.
With pointillism enabled, dot identity remains surface-anchored while color quantization/stroke directionality remains temporally stable under motion.

## Risk and compatibility

- Existing materials remain backward compatible because `Pattern Source` defaults to Bayer.
- Existing `_DitherMode`, `_DitherTex`, and `_DitherRampTex` semantics are unchanged.
- Blue-noise path is opt-in and has runtime fallback to Bayer when rank texture is not available.
- Pointillism is opt-in (`Enable Pointillism` default = off) and safely degrades when optional LUT is missing (`LUT Blend` default = 0).

## Known limitations

- BlueNoiseFractal relies on generated rank textures and does not enforce strict mathematical fractal guarantees equivalent to Bayer matrices.
- Optional phase texture set is generated as separate textures and must be assigned manually.
- Object/triplanar pointillism coordinates are currently available on opaque/cutout surface shaders; other shader paths may still rely on UV/AltUV behavior.
- Pointillism LUT expects a simple horizontal 1D-style mapping texture and currently applies per-channel remapping only.

## Next improvements

- Add direct support for phase texture arrays/atlases.
- Extend runtime visual debug overlays to include phase blend and hysteresis response.

## Discussion of surface-stable trait

Here is how I define surface-stable:

- A specific dot "sticks" to the surface whose shading it is part of, both under object movement and camera movement.
- When a pattern is enlarged on the screen, for example due to zooming in on a surface, additional dots may be added to maintain the desired dot density, but no dots may be removed. Likewise, when a pattern shrinks on the screen, dots may be removed, but no dots may be added.

Conforming to the second constraint is in particular what is new in Surface-Stable Fractal Dithering. Approaches that fade between different scales of a pattern, which is not self-similar in the required way, will typically see dots both appearing and disappearing when zooming in.

### Bayer matrices

My implementation conforms to the second constraint by exploiting a certain "fractal" property of Bayer matrices, as explained in the video above.

### Other regular patterns

Some people have suggested using other regular patterns than Bayer for the dithering, based on triangles, hexes, square root 2 ratio rectangles, or similar. This should be relatively straightforward to implement for someone who so desires.

### Blue noise patterns

Some people have suggested using blue noise for the pattern. But it is not straightforward to construct a blue noise pattern which is self-similar in a way that conforms to the second surface-stable constraint.

If we use only a single tiled square of blue noise pattern, it would require the dots in each quadrant of the pattern, and each recursive quadrant of the pattern, to perfectly line up with dots in the full pattern, when scaled up to cover the same area as the full pattern.

Some people have pointed to the paper "Recursive Wang Tiles for Real-Time Blue Noise" by Johannes Kopf et al ([paper](https://johanneskopf.de/publications/blue_noise/paper/Recursive_Wang_Tiles_For_Real-Time_Blue_Noise.pdf) and [video](https://www.youtube.com/watch?v=ykACzjtR6rc)). While the paper describes their technique making use of self-similar blue noise patterns, the practical demonstration in the video shows dots fading both in and out while zooming in, so while I do not fully understand their technique in detail, it does not seem to conform to the second surface-stable constraint.

I hope others will manage to construct a tiled blue-noise pattern with the required properties.

## License

This Surface-Stable Fractal Dithering implementation is licensed under the [Mozilla Public License, v. 2.0](https://mozilla.org/MPL/2.0/).

You can read a summary [here](https://choosealicense.com/licenses/mpl-2.0/). In short: If you make changes/improvements to this Surface-Stable Fractal Dithering implementation, you must share those for free with the community. But the rest of the source code for your game or application is not subject to this license, so there's nothing preventing you from creating proprietary and commercial games that use this Surface-Stable Fractal Dithering implementation.
