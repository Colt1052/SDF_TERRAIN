# TASKS.md — Task Tracker

## Development Rules

- Complete only one task (or one tightly related group) per change.
- Keep commits small. Never mix refactoring with new features.
- Maintain a runnable project after every task.
- Do not introduce TODOs without creating a corresponding task.
- Add tests alongside implementation. Add debug visualizations for simulation features.

---

## Definition of Done

Code compiles without warnings, existing tests pass, new functionality has tests, public APIs are documented, no unnecessary allocations in update loops, no new linter warnings.

---

# PHASE 1 — Foundation

## 1. Project structure ✅
Asmdefs, folder hierarchy, namespaces created.

## 2. Core math library ✅
`RadialMath`, `PlanetCoordinates`, `SeededRandom` (xorshift32) under `Runtime/Core/`.

## 3. Planet component ✅
`Planet` (MonoBehaviour), `PlanetSettings` (ScriptableObject) under `Runtime/Planet/`.

## 4. Planet manager ✅
`PlanetManager` singleton, auto-register/unregister via `Planet.OnEnable`/`OnDisable`.

---

# PHASE 2 — Terrain

## 5. SDF data structure ✅
`TerrainField` + `TerrainEdit` — sphere base, brush edits, CSG composition.

## 6. Chunk system ✅
`TerrainChunk` + `ChunkGrid` — square Cartesian grid, dirty tracking, neighbor lookup.

## 7. Procedural planet generator ✅
`PlanetGenerator.GenerateBaseShape(radius, seed)` → `TerrainField`.

## 8. Terrain noise ✅
`TerrainNoise` — seamless sine-harmonic noise, ridged mode, domain warp.

## 9. Terrain meshing ✅
`MarchingSquaresMesher` — all 16 cases, asymptotic decider for saddles.

## 10. Collider generation ✅
`ColliderContourBuilder` — boundary loops from `MeshData` → `PolygonCollider2D`.

## 11. Terrain renderer ✅
`TerrainFieldSampler`, `MeshDataConverter`, `TerrainRenderer`, `ChunkTerrainRenderer`.

---

# PHASE 3 — Terrain Editing

## 12. Brush framework ✅
`BrushMode` (Add/Remove/Smooth), `TerrainBrush` struct.

## 13. Digging ✅
`TerrainRenderer.ApplyBrush` → mutate SDF → rebuild.

## 14. Building terrain ✅
`BrushMode.Add` path verified end-to-end.

## 15. Chunk rebuilding ✅
Per-chunk sampling, dirty-only rebuilds, `ChunkTerrainRenderer`.

### Brush refinements ✅
- **15.1** Chunk seam gap fix (superseded by 15.10)
- **15.5** Solid brush + idempotent overlapping edits (CSG Max/Min)
- **15.6** Exact circular brush shape (linear SDF cone)
- **15.7** Asymptotic decider for ambiguous Marching Squares saddles
- **15.8** Remove Strength plateau clamp
- **15.9** Remove Strength — brush edits are pure SDF (radius only)
- **15.10** Convert chunks from wedges to square Cartesian grid
- **15.11** Chunk-indexed spatial index + dead edit pruning
- **15.12** Prevent chunk creation for delete brushes in empty space
- **15.13** Automatic removal of empty chunks
- **15.14** In-game brush selection & property editor (ScriptableObject brush system)

## 16. Undo system

Store terrain edit history. Support undo/redo.

---

# PHASE 4 — Materials

## 17. Material database ✅
`MaterialDefinition` (ScriptableObject), `MaterialDatabase` registry, 12 default assets.

## 18. Material sampling ✅
`MaterialSampler`, `MaterialBand`, `MaterialSampleSettings` — assign materials by depth/position.

## 19. Geological layers ✅

Generate layered composition (soil → stone → mantle) based on depth, heat, pressure, noise.
Wire geological profile to ChunkTerrainRenderer for vertex-color rendering. Create playable demo scene.

### Shader fixes (2026-08-24)
- **LightMode mismatch**: `TerrainVertexColor.shader` declared `LightMode = "UniversalForward"` (URP 3D).
  The project uses URP 2D, so the draw call was never dispatched. Fixed → `"Universal2D"`.
- **Per-vertex color loss**: `MarchingSquaresMesher` helpers (`AddTriangle`, `AddQuad`, etc.) computed
  per-corner colors but passed only the first color to `MeshData.AddTriangle()`, so the whole shape
  rendered a single color. Added per-vertex overloads to `MeshData` and wired helpers through them.
  (Vertex-color approach abandoned in favor of fragment shader — see below.)
- **Fragment-shader geological rendering**: Vertex colors can't show interior layers because the
  mesher fills the solid region with triangles whose vertices are at grid-point positions — the
  interior vertices all have the same layer color as their grid corner. Replaced with fragment-shader
  approach: pass `posOS` to fragment, compute `depth = radius - length(posOS)`, select layer color
  at each pixel. This produces correct concentric shells visible in 2D cross-section.
- **Noise-based layer boundaries**: Added 3-octave FBM (2D value noise via sin hash + Hermite interp)
  to perturb depth in the fragment shader. Layer boundaries are now organic/wavy instead of circular.
  Shader properties: `_PlanetRadius`, `_NoiseAmplitude` (2.0), `_NoiseFrequency` (0.15), plus layer
  colors and tint. Thresholds match `GeologicalProfile.EarthLike()` (dirt: 0-3, stone: 3-15, deep
  stone: 15-30, mantle: 30+).

### Lessons learned
- **URP 2D requires `LightMode = "Universal2D"`** — `"UniversalForward"` silently renders nothing.
- **Vertex colors on filled meshes only color what's at the vertices** — can't show gradients through
  interior geometry that the mesher didn't place vertices at. Fragment-space computation from position
  is the correct approach for depth-based coloring of filled regions.
- **Chunk GameObjects share the planet's transform** — `posOS` in the shader is planet-relative, not
  chunk-relative. Fragment `length(posOS)` works for concentric shells.
- **HLSL hash portability**: `sin(dot(p, constant))` formula works; integer-based hash (multiply +
  dot) can silently break on some DX11 configurations.

---

## 20. Ore generation

Generate ore deposits (iron, copper, gold) procedurally from geological layers, pressure, temperature, noise.

Tests: deterministic output, distribution patterns.

---

# PHASE 5 — Gravity

## 21. Gravity system

Implement radial gravity. Tests: correct direction, magnitude.

---

## 22. Player orientation

Rotate player so feet point toward planet center.

---

## 23. Multi-planet gravity

Nearest-influence gravity switching. Tests: planet switching correctness.

---

# PHASE 6 — World Generation

## 24. Cave generation

Noise-based cave systems (lava tubes, caverns, tunnels, underground lakes).

Tests: connectivity, density.

---

## 25. Biome framework

Support temperature, moisture, surface material variation.

---

## 26. Planet DNA

Random planet parameter generation from seed. Tests: repeatability.

---

# PHASE 7 — Gameplay

## 27. Player controller

Movement, jump, mining, building.

---

## 28. Inventory

Items, resources, storage.

---

## 29. Resource drops

Spawn mined resources. Tests: material correctness.

---

## 30. Building placement

Foundations, validation, terrain integration.

---

# PHASE 8 — Physics

## 31. Planet collision detection

Detect planet overlap.

---

## 32. Terrain deformation (temporary)

Visible squish on impact.

---

## 33. Terrain deformation (permanent)

Persistent craters baked into SDF.

---

# PHASE 9 — Atmosphere

## 34. Atmospheric grid

Pressure field, temperature field.

---

## 35. Fluid solver

Euler simulation. Tests: stable timestep.

---

## 36. Terrain interaction

Wind flow around mountains. Tests: obstacle flow.

---

## 37. Weather

Clouds, rain, storms.

---

# PHASE 10 — Water

## 38. Water simulation

Surface water.

---

## 39. Water terrain interaction

Flood caves, fill craters.

---

# PHASE 11 — Rendering

## 40. Material blending

Surface texture blending.

---

## 41. Lighting

Dynamic lighting, smooth normals.

---

## 42. Shadows

Terrain shadows.

---

# PHASE 12 — Optimization

## 43. Burst compatibility

Refactor for Burst-safe code.

---

## 44. Job System

Move generation, meshing, sampling to Job System.

---

## 45. Memory optimization

Reduce allocations, pool objects.

---

## 46. LOD

Planet detail levels.

---

## 47. Chunk streaming

Load/unload chunks dynamically.

---

# PHASE 13 — Saving

## 48. Save format

Store: planet seed, terrain edits, entities.

---

## 49. Terrain edit replay

Regenerate from seed, replay edit list.

---

# PHASE 14 — Debugging

## 50. Terrain debugger

Display SDF, chunks, normals.

---

## 51. Planet debugger

Display gravity, radius, influence zones.

---

## 52. Generator debugger

Display noise, layers, ore, caves.

---

# PHASE 15 — Polish

## 53. Profiling

Profile CPU, memory, GC for every major system.

---

## 54. Stress testing

100 planets, continuous digging/building, planet collisions.

---

## 55. Determinism testing

Verify same seed → same output across runs.

---

## 56. Documentation

Document architecture, algorithms, data flow, extension points.
