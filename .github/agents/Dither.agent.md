# AGENTS.md — Dither3D Blue-Noise / Surface-Stable Pointillism Workflows

## Mission
Implement and refine **surface-stable fractal/pointillist dithering** in Unity (ShaderLab/HLSL/C#), with emphasis on:
- tileable/progressive blue-noise usage,
- temporal stability (camera + object motion),
- practical editor tooling and non-breaking integration.

## Success Criteria
1. Visual output remains stable on moving camera/object (minimal shimmer/pop).
2. Dot distribution is naturalistic (blue-noise-like, low clumping).
3. Integration is incremental and backward compatible.
4. Changes are testable in-scene with clear toggles/presets.
5. Docs explain how to generate assets and tune quality/performance.

---

## Repository Assumptions
- Unity project with ShaderLab/HLSL + C# scripts.
- Existing dithering pipeline should be preserved unless explicitly replaced.
- Avoid introducing SRP-specific hard dependencies unless repo already uses them.

---

## Guardrails (Must Follow)
1. **Do not break existing materials/shaders**:
   - Add properties with safe defaults.
   - Preserve old property names when possible.
   - If renaming is required, add compatibility mapping and migration notes.
2. **Prefer additive changes** over rewrites.
3. **Editor code stays in `Editor/`** and guarded if needed.
4. **No hidden magic constants**:
   - Expose key controls in material inspector or script presets.
5. **Do not assume UV quality**:
   - support UV path + fallback option (object/triplanar hook).
6. **Upscaler/TAA-aware defaults**:
   - default preset should favor temporal coherence.

---

## Required Work Pattern
For each task, follow this exact sequence:

1. **Discover**
   - Identify current shader entry points and property names.
   - Locate existing C# controllers/material-updater scripts.
   - Find docs/README sections for usage.

2. **Design Minimal Diff**
   - Propose smallest set of file edits needed.
   - List compatibility impacts before coding.

3. **Implement**
   - Make focused commits by concern:
     - shader include/utilities
     - material/runtime controls
     - editor generator/import pipeline
     - docs

4. **Validate**
   - Confirm compile for C# + shaders.
   - Confirm materials still render if new textures are missing.
   - Confirm default preset behaves stably under camera pan.

5. **Document**
   - Add/modify docs with exact setup steps and property table.
   - Add “Known limitations” and “Next improvements”.

---

## Preferred Architecture

### A. Blue-Noise Data
- Use **periodic/tileable rank textures** (0..1).
- Optional multi-phase texture set for temporal decorrelation.
- Deterministic generation from seed.

### B. Sampling
- Primary: surface-stable coordinates (UV/object anchored).
- Fallback hook for triplanar/object-space for poor UV assets.
- Avoid screen-space anchoring for final dot identity.

### C. Thresholding
- Convert tone to density and compare against rank.
- Keep equations simple and branch-light.

### D. Temporal Stability
- Support three presets:
  - Conservative
  - Balanced (default)
  - Aggressive
- Parameters include:
  - phase speed,
  - hysteresis/stickiness,
  - minimum dot size (or closest equivalent).

### E. Editor Tooling
- Add generator window for rank textures and optional phase sets.
- Enforce import settings for rank data:
  - linear,
  - no mipmaps,
  - uncompressed,
  - repeat wrap,
  - point (or justified alternative).

---

## PR Quality Bar
Every PR must include:

1. **What changed**
   - concise summary by file.
2. **Why**
   - tie each change to stability/quality goals.
3. **How to test**
   - concrete scene/material steps.
4. **Risk and compatibility**
   - list defaults and fallback behavior.
5. **Known limitations**
   - what is intentionally deferred.

---

## Coding Conventions
- Match existing style and naming conventions.
- Keep shader property names consistent with existing prefixes.
- Use clear comments only where non-obvious.
- Avoid large utility abstractions unless reused in multiple files.

---

## Non-Goals (for this phase)
- Full geodesic optimizer implementation in runtime.
- Large render-pipeline refactors.
- Replacing entire artistic look pipeline.
- Overfitting to one scene or one mesh.

---

## Investigation Checklist (Before Editing)
- [ ] Main shaders and includes identified
- [ ] Existing dither/noise properties mapped
- [ ] Material inspector/custom editor behavior understood
- [ ] Runtime scripts setting material params identified
- [ ] Current docs location identified

---

## Implementation Checklist
- [ ] Add or integrate blue-noise include/utilities
- [ ] Add property hooks for rank texture(s), phase controls
- [ ] Add/extend runtime preset controller
- [ ] Add editor generator + import enforcement
- [ ] Add docs + parameter table
- [ ] Verify old scenes/materials still render

---

## Validation Scenes (Create/Use)
Agent should validate on at least:
1. **Static object + whipping camera**
2. **Moving/rotating object + static camera**
3. **Thin geometry / high-frequency albedo**
4. **UV-stretched mesh**

Record observed shimmer/pop and preset behavior.

---

## Parameter Guidance (Initial Defaults)
- Balanced preset is default.
- Conservative for heavy motion/upscaler-heavy projects.
- Aggressive only for close/static shots.

If project has TAA/upscaler enabled, prioritize hysteresis and slower phase changes.

---

## Commit Strategy
Prefer multiple small commits:
1. shader plumbing
2. runtime preset plumbing
3. editor generation/import
4. docs

Avoid one giant mixed commit.

---

## If Unsure
When encountering ambiguity:
1. Choose backward-compatible option.
2. Add TODO with concrete future upgrade path.
3. Document decision in PR notes.

---

## Deliverable Format for Agent Responses
When reporting back:
1. Start with direct status.
2. List changed files.
3. Provide minimal test steps.
4. Mention risks/limitations.
5. Propose next incremental improvement.
