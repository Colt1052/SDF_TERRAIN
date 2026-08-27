# Task Archive — Completed Implementation Details

Detailed notes for every completed task. Read only when deep historical context is needed for a subsystem.

---

## Phase 1 — Foundation

### Task 1. Project Structure
`Runtime/SDF_Terrain.Runtime.asmdef` (rootNamespace `SDFTerrain`) and `Tests/SDF_Terrain.Tests.asmdef` (rootNamespace `SDFTerrain.Tests`, Editor-only, references Runtime asmdef + NUnit/TestRunner) created. Folder hierarchy matches ARCHITECTURE.md §8: `Runtime/{Core,Planet,Terrain,Meshing}`, `Tests/{EditMode,PlayMode}`, `Scripts/Debug`. Analyzer/editor-settings config deferred — no analyzer ruleset exists elsewhere in the project to mirror. No code existed; compile correctness should be confirmed by opening Unity editor.

### Task 2. Core Math Library
`RadialMath` (angle wrap, angle-of, position-at, surface normal), `PlanetCoordinates` (world/local/radial conversions, gravity direction), `SeededRandom` (xorshift32 PRNG — the only permitted randomness source per CLAUDE.md, never `UnityEngine.Random` in generation code) added under `Runtime/Core/`, all static/pure or allocation-free structs. EditMode tests cover angle wrapping edge cases (negative, >2π), round-trip conversions, zero-radius/zero-seed edge cases, and determinism (same seed → identical sequence).

### Task 3. Planet Component
`PlanetSettings` (ScriptableObject: radius range, density, gravity strength, optional seed override, `OnValidate` clamping) and `Planet` (thin MonoBehaviour: `Radius`/`Seed`/`GravityStrength`/`Settings`/`Center`, validated via `Initialize`, throws on null settings/non-positive radius/negative gravity) added under `Runtime/Planet/`. `PlanetManager` added as plain C# class — `Register`/`Unregister`/`GetPlanetAt`/`AllPlanets`, nearest-by-surface-distance lookup (accounts for differing planet radii, not just center distance). Tests cover Planet construction/validation (including seed-override precedence) and PlanetManager multi-planet register/duplicate-register/unregister/lookup.

### Task 4. Planet Manager
Built on `PlanetManager` from Task 3. Added `PlanetManager.Instance` (lazy-initialized static) and wired `Planet.OnEnable`/`OnDisable` to auto-register/unregister — planets track their own lifecycle. Update ordering = registration order via `AllPlanets`. Tests that need isolation construct `new PlanetManager()`; new tests in PlanetTests.cs cover auto-register/unregister against shared instance.

---

## Phase 2 — Terrain

### Task 5. SDF Data Structure
`TerrainField` (`Runtime/Terrain/`) is the authoritative SDF: base shape is a perfect sphere (`|localPos| - radius`), with persisted `TerrainEdit` modifications (circular smoothstep-falloff brushes, additive=dig/subtractive=build) summed on top at sample time. Only the sparse edit list is stored/serializable (`[System.Serializable] struct TerrainEdit`, `LoadEdits`/`Edits`) — the base field is always regenerated from `Planet.Radius`. Dense/chunked sample storage deferred to Task 6. Tests cover sampling at center/surface/outside/inside, additive vs. subtractive direction, brush radius falloff-to-zero, out-of-range edits, clear/load/replace, overlapping edits accumulating.

### Task 6. Chunk System
`TerrainChunk` (index, col, row, rectangular bounding box, dirty flag) and `ChunkGrid` (`Runtime/Terrain/`) added, dividing planet's bounding square into a fixed 2D grid of equal square chunks. `GetChunkAt(Vector2)` computes (col, row) from position, clamping to grid bounds; `GetChunkAtGrid(col, row)` validates coordinates; `GetNeighbor(chunk, direction)` returns 4-directional neighbor or null at edges. `ChunksInRect(minX, maxX, minY, maxY, result)` computes overlapping range directly — no iteration over all chunks. Chunks start dirty; `MarkDirtyAt(Vector2)`/`DirtyChunks()`/`ClearAllDirty` drive propagation.

### Task 7. Procedural Planet Generator
`PlanetGenerator.GenerateBaseShape(radius, seed)` (`Runtime/Terrain/`) added as static, stateless pipeline entry point producing a `TerrainField` whose base shape is a perfect sphere — first stage of generation order in SCOPE.md/CLAUDE.md. The `seed` parameter is threaded through and consumed via `SeededRandom` (unused by this stage) so Task 8 can extend the pipeline without changing the call site. Tests cover radius matching, surface continuity (360 sample points on zero-crossing), zero initial edits, determinism.

### Task 8. Terrain Noise
`TerrainNoiseSettings` (immutable readonly struct: amplitude, frequency, octaves, persistence, lacunarity, ridged flag, warp strength/frequency) and `TerrainNoise.SampleHeight(angle, seed, settings)` added. Noise built from summed sine harmonics with integer frequencies — `sin(k * angle + phase)` is exactly periodic over 2π for integer k, so the sum is inherently seamless with no special-cased wrap logic. Ridged mode takes `1 - |sin(...)|` per octave. Domain warp perturbs sampled angle before evaluating the sum. `TerrainField` grew a `(radius, seed, noiseSettings)` constructor and `SurfaceRadiusAt(angle)`. `PlanetGenerator` gained 3-arg overload; 2-arg overload preserved, forwarding `TerrainNoiseSettings.None`.

### Task 9. Terrain Meshing
`MarchingSquaresMesher.Generate(samples, cellSize, origin, uvScale)` and `MeshData` (plain vertex/triangle/normal/UV lists, no `UnityEngine.Mesh` dependency) added under `Runtime/Meshing/`. Pure function of inputs — no dependency on `TerrainField`/`Planet`. All 16 Marching Squares cases via edge interpolation; ambiguous saddle cases (5 and 10) resolved by asymptotic decider (Task 15.7). Normals are uniform `Vector3.back` (flat 2D quad) — deferred. Converting `MeshData` to `UnityEngine.Mesh` is Task 11.

### Task 10. Collider Generation
`ColliderContourBuilder.BuildContours(meshData)` (`Runtime/Meshing/`) — pure function extracting closed boundary polygon loops from triangulated `MeshData`. Counts directed triangle edges: shared edges cancel, leaving only boundary edges, stitched into closed loops (handles multiple disjoint regions). `TerrainColliderBuilder.Apply(meshData, collider)` is a thin wrapper for `PolygonCollider2D.pathCount`/`SetPath`.

### Task 11. Terrain Renderer
`TerrainFieldSampler` — pure function sampling a `TerrainField` onto a square bounding-box grid (whole-planet sampling). `MeshDataConverter.ToUnityMesh(meshData, reuse)` — converts `MeshData` into `UnityEngine.Mesh`, accepting existing Mesh to overwrite (no per-rebuild allocation). `TerrainRenderer` MonoBehaviour ties it together: `Rebuild(field, boundsRadius, chunkGrid)` samples → meshes → converts → assigns components. Debug overlays via gizmos (chunk borders, normals), off by default. `ChunkTerrainRenderer` handles per-chunk rendering (Task 15).

---

## Phase 3 — Terrain Editing

### Task 12. Brush Framework
`BrushMode` (Add/Remove/Smooth) enum and `TerrainBrush` (immutable readonly struct: mode, radius) added. `TerrainBrush.Apply(field, localPosition)`: Add/Remove produce `TerrainEdit`, Smooth calls `TerrainField.SmoothEdits(position, radius)` which reduces the `Radius` of nearby existing edits using smoothstep falloff.

### Task 13. Digging
`TerrainRenderer.ApplyBrush(brush, localPosition)` applies a brush to the field and immediately calls `Rebuild`. Rebuild caches field/boundsRadius/chunkGrid so `ApplyBrush` can re-invoke without caller re-supplying. Calling `ApplyBrush` before `Rebuild` throws `InvalidOperationException`. Whole-planet rebuild-per-dig — chunk-local rebuilds are Task 15.

### Task 14. Building Terrain
No new code — `BrushMode.Add`, `TerrainEdit` subtractive direction, and `TerrainRenderer.ApplyBrush` already compose into "add terrain" path. Tests added for build-specific end-to-end verification.

### Task 15. Chunk Rebuilding
`MarchingSquaresMesher` gained `Generate(samples, positions, uvScale)` overload for non-uniform position grids. `CartesianChunkFieldSampler.Sample(field, chunk, cellSize)` — chunk-local sampler for one chunk's bounding box. `ChunkTerrainRenderer` — one child GameObject per chunk, created once in `Initialize`, `RebuildDirtyChunks()` iterates only `ChunkGrid.DirtyChunks()`. `TerrainField.EnableChunkIndexing(ChunkGrid)` maps edits to overlapping chunks.

---

## Phase 3 — Brush Refinements

### Task 15.1. Chunk Seam Gap Fix (Superseded by 15.10)
Every chunk combined terrain SDF with a steep wedge mask, causing asymmetric values at shared edges. Fix was a 2-cell seam margin that bypassed wedge masks. **Superseded by 15.10:** square chunk migration eliminated root cause entirely.

### Task 15.5. Solid Brush + Idempotent Overlapping Edits
`TerrainEdit` gained `Hardness` field (later removed in 15.6). `TerrainField.Sample` combined edits CSG-style instead of additively: `Mathf.Max(distance, contribution)` for digs, `Mathf.Min` for builds — idempotent, no melting. `MouseTerrainEditor` gained `minDragDistance` gate.

### Task 15.6. Exact Circular Brush Shape
`TerrainEdit.SampleContribution` changed from smoothstep-curved falloff to exact linear signed-distance cone (`Radius - distanceFromBrush`, capped by `Strength`). The `Hardness` parameter became obsolete and was removed. `Strength` repurposed as depth cap.

### Task 15.7. Asymptotic Decider for Ambiguous Saddles
Ambiguous saddle cases (5, 10) previously always resolved to disjoint triangles. Fixed with bilinear center estimate: `EstimateCenter` picks between disjoint triangulation (center >= 0, air) and merged hexagon (center < 0, solid). Made topology choice continuous.

### Task 15.8. Remove Strength Plateau Clamp
`SampleContribution` no longer clamps cone to `Mathf.Min(Radius - distanceFromBrush, Strength)`. Scales uncapped cone by `Strength`: `(Radius - distanceFromBrush) * Strength`. Keeps gradient nonzero everywhere — Marching Squares reconstructs smooth circle at any Strength.

### Task 15.9. Remove Strength — Pure SDF Brush
`Strength` removed from `TerrainEdit` and `TerrainBrush` entirely. `SampleContribution` is exactly `Radius - distanceFromBrush`. `TerrainField.SmoothEdits` shrinks `edit.Radius` instead of `edit.Strength`. One parameter: radius.

### Task 15.10. Convert Chunks from Wedges to Squares
`ChunkSeamCache.cs` deleted. `TerrainChunk` replaced `StartAngle`/`EndAngle` with `Col`/`Row`/bounding box coords. `ChunkGrid` rewritten as 2D grid centered on origin. `CartesianChunkFieldSampler` simplified — no wedge masks, no seam margins, no angular math. Each lattice point samples `field.Sample(position)`. Key design: no boundary clipping, shared boundary lattice points, grid edges return null for neighbors, terrain noise and RadialMath unchanged. Bug fix: grid bounds were offset to bottom-left quadrant — corrected to center symmetrically.

### Task 15.11. Chunk-Indexed Sampling + Dead Edit Pruning
`CartesianChunkFieldSampler` calls `field.Sample(position, chunk.Index)` — only scans edits registered to the chunk via spatial index. Cost bounded to O(chunk_local_edits). `TerrainField.PruneDeadEdits()` compacts zero-radius entries and atomically remaps all `_editsByChunk` indices. `ClearEdits` and `LoadEdits` now maintain index consistency.

### Task 15.12. Prevent Chunk Creation in Empty Space
`ChunkGrid.ChunksInRect` gained `bool createChunks = true`. `TerrainField._editsByChunk` (keyed by `chunk.Index` `int`) replaced with `_editsByChunkKey` (keyed by packed `(col, row)` `long`) — edits can be indexed for grid cells even when no chunk object exists. `ChunkTerrainRenderer.ApplyBrush` passes `createChunks = brush.Mode == BrushMode.Add`.

### Task 15.13. Automatic Removal of Empty Chunks
`ChunkGrid` gained `RemoveChunkAtGrid(col, row)` and `HasChunkAtGrid(col, row)`. `ChunkTerrainRenderer.RebuildChunk` returns `bool` (true = no geometry). `RebuildDirtyChunks` collects empty indices and removes them after iteration (avoids enumerator invalidation). `RemoveEmptyChunk` destroys GameObject, removes from `_chunkViews`, removes from grid. Global behavior — any empty chunk is removed.

### Task 15.14. In-Game Brush Selection & Property Editor
Data-driven brush system: `BrushBehavior` (abstract ScriptableObject), `BrushDefinition` (name, icon, behavior, parameters), `BrushParameterDescriptor` (float parameter config), `StandardBrushBehavior` (Add/Remove), `SmoothBrushBehavior` (smoothing), `BrushController` (state management, events), `BrushInputHandler` (input adapter), `BrushUI` (dynamic canvas UI). `BrushDefaultAssetsCreator` editor menu generates default ScriptableObject assets. Event-driven UI architecture — `BrushController` fires events, `BrushUI` subscribes. `ChunkTerrainRenderer` gained `MarkDirtyRectAndRebuild` for non-standard behaviors.

### Task 15.15. Edit-Centric Chunk Index + Isolation Pruning
`TerrainField` gained `_editChunkKeys: Dictionary<int, HashSet<long>>` — a reverse index mapping edit index to the packed `(col, row)` chunk keys the edit's bounding box overlaps. Populated during `IndexEdit` (zero extra loops — collects keys in the same grid iteration that builds `_editsByChunkKey`). All existing pruning methods (`PruneDeadEdits`, `PruneRedundantEdits`) and lifecycle methods (`ClearEdits`, `LoadEdits`) rebuild `_editChunkKeys` alongside `_editsByChunkKey` using the same `oldToNew` remapping pattern. `TerrainField.PruneIsolatedEdits(Func<long, bool> isChunkActive)` removes edits whose affected chunk keys all fail the caller's predicate — enables callers to define "active" however they need (loaded in memory, has geometry, within render distance). `ChunkTerrainRenderer.PruneIsolatedEdits()` wraps the field method with a predicate that checks: chunk exists in grid AND has an active `ChunkView`. Edit struct unchanged — `_editChunkKeys` is a parallel collection (chunk keys are derived from bounding box + grid params, not intrinsic edit data, so serialization is unaffected). 13 new EditMode tests cover population, multi-chunk capsules, lifecycle consistency, and selective removal via predicate.

---

## Phase 4 — Materials

### Task 17. Material Database
`MaterialDefinition` (ScriptableObject with `[CreateAssetMenu]`) carries 9 properties: `Id`, `DisplayName`, `Color`, `Density`, `Hardness`, `Friction`, `ThermalConductivity`, `MeltingPoint`, `StructuralStrength`. All `[SerializeField] private` with readonly public getters. `OnValidate()` clamps values. `MaterialDatabase` is a plain C# static registry with lazy `Instance` singleton, loads from `Resources/Materials/`. `MaterialAssetsCreator` editor menu generates 12 default material assets. Tests cover registration, retrieval, validation, determinism, clamping, duplicate-ID semantics.

### Task 18. Material Sampling
`MaterialSampler` and `MaterialBand` added for assigning materials (Dirt, Stone, Ice) based on depth/position within the SDF. `MaterialSampleSettings` drives configuration.
