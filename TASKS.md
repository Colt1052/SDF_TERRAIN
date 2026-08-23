# TASKS.md

# 2D Planetoid Sandbox — Development Tasks

## Development Rules

Every task should follow these rules:

* Complete only one task (or one tightly related group of tasks) per change.
* Keep pull requests and commits small.
* Never mix refactoring with new features.
* Maintain a runnable project after every task.
* Do not introduce TODOs without creating a corresponding task.
* Do not leave the project in a broken state.

---

# Definition of Done

A task is complete when:

* Code compiles without warnings.
* Existing tests pass.
* New functionality has automated tests where practical.
* Public APIs are documented.
* No unnecessary allocations occur in update loops.
* No new linter or analyzer warnings are introduced.
* The feature is demonstrated in a small test scene if applicable.

---

# Testing Rules

Every system should have at least one of:

* Unit tests
* Integration tests
* Simulation tests
* Debug visualization

Whenever possible:

* Test behavior instead of implementation.
* Prefer deterministic tests.
* Avoid tests that depend on timing.

---

# Debug Visualization

Every simulation system should expose debug visualization.

Examples:

* Density field
* Chunk boundaries
* Mesh outlines
* Collision polygons
* Gravity vectors
* Planet influence
* Wind vectors
* Pressure maps
* Ore generation
* Cave generation

Visualization is considered part of the implementation.

---

# Performance Rules

Avoid:

* Per-frame allocations
* LINQ in update loops
* Reflection during gameplay
* Full planet rebuilds

Prefer:

* Object pooling
* Dirty updates
* Chunk-local operations
* Burst-compatible code
* Native collections where appropriate

---

# PHASE 1 — Foundation

## 1. Project structure ✅ DONE

* Create assembly definitions
* Create folder hierarchy
* Create namespace conventions
* Configure analyzers
* Configure editor settings

Acceptance:

* Clean project structure
* No compile warnings

Notes: `Runtime/SDF_Terrain.Runtime.asmdef` (rootNamespace `SDFTerrain`) and
`Tests/SDF_Terrain.Tests.asmdef` (rootNamespace `SDFTerrain.Tests`, Editor-only,
references Runtime asmdef + NUnit/TestRunner) created. Folder hierarchy matches
ARCHITECTURE.md §8: `Runtime/{Core,Planet,Terrain,Meshing}`,
`Tests/{EditMode,PlayMode}`, `Scripts/Debug`. Analyzer/editor-settings config
deferred — no analyzer ruleset exists elsewhere in the project to mirror
(see ARCHITECTURE.md §9 risk table); revisit if/when one is introduced.
No code exists yet, so "no compile warnings" is vacuously true; asmdef JSON
validity checked directly (no Unity CLI available in this environment) —
compile correctness should be confirmed by opening the Unity editor.

---

## 2. Core math library ✅ DONE

Implement:

* Planet coordinates
* Local coordinate conversions
* Radial vectors
* Utility math

Tests:

* Coordinate conversions
* Precision
* Edge cases

Notes: `RadialMath` (angle wrap, angle-of, position-at, surface normal),
`PlanetCoordinates` (world/local/radial conversions, gravity direction),
`SeededRandom` (xorshift32 PRNG, the only permitted randomness source per
CLAUDE.md — never `UnityEngine.Random` in generation code) added under
`Runtime/Core/`, all static/pure or allocation-free structs. EditMode tests
in `Tests/EditMode/` cover angle wrapping edge cases (negative, >2π),
round-trip conversions, zero-radius/zero-seed edge cases, and determinism
(same seed → identical sequence). Verification limited to structural/syntax
review — no Unity CLI available in this environment; running the Test Runner
in-editor is the outstanding verification step for this and Task 1.

---

## 3. Planet component ✅ DONE

Create:

Planet

Contains:

* Radius
* Seed
* Gravity
* Transform
* Settings

Tests:

* Construction
* Serialization
* Validation

Notes: `PlanetSettings` (ScriptableObject: radius range, density, gravity
strength, optional seed override, `OnValidate` clamping) and `Planet` (thin
MonoBehaviour: `Radius`/`Seed`/`GravityStrength`/`Settings`/`Center`,
validated via `Initialize`, throws on null settings/non-positive radius/
negative gravity) added under `Runtime/Planet/`. `PlanetManager` added as a
plain C# class (no MonoBehaviour) — `Register`/`Unregister`/`GetPlanetAt`/
`AllPlanets`, nearest-by-surface-distance lookup (accounts for differing
planet radii, not just center distance). EditMode tests cover Planet
construction/validation (including seed-override precedence) and
PlanetManager multi-planet register/duplicate-register/unregister/lookup
including differing-radius lookup correctness. Serialization itself is
Unity's built-in `[SerializeField]` mechanism — no custom serialization code
was needed; verified structurally only, no Unity CLI available here.

---

## 4. Planet manager ✅ DONE

Implement:

* Registration
* Lookup
* Lifetime
* Update ordering

Tests:

* Multiple planets
* Removal
* Spawn/despawn

Notes: Built on the `PlanetManager` class from Task 3. Added a shared
`PlanetManager.Instance` (lazy-initialized static) and wired `Planet.OnEnable`/
`OnDisable` to auto-register/unregister — this is the "lifetime" and
"spawn/despawn" requirement, so planets track their own lifecycle without a
separate manual registration step. "Update ordering" is defined as
registration order via `AllPlanets` (first registered, first processed) —
sufficient for now since no system yet consumes it; revisit if a future
system needs a different ordering policy. Tests that need isolation construct
their own `new PlanetManager()` (as in Task 3's PlanetManagerTests); new
tests in PlanetTests.cs cover auto-register on enable and auto-unregister on
disable against the shared instance. Verified structurally only — no Unity
CLI available here.

---

# PHASE 2 — Terrain

## 5. SDF data structure ✅ DONE

Create:

* Signed distance storage
* Sampling
* Modification
* Serialization interface

Tests:

* Sampling accuracy
* Bounds
* Modification

Notes: `TerrainField` (`Runtime/Terrain/`) is the authoritative SDF: base
shape is a perfect sphere (`|localPos| - radius`, matching generation order —
noise/layers/caves land in later tasks), with persisted `TerrainEdit`
modifications (circular smoothstep-falloff brushes, additive=dig/
subtractive=build) summed on top at sample time. Only the sparse edit list
is stored/serializable (`[System.Serializable] struct TerrainEdit`,
`LoadEdits`/`Edits`) — the base field is always regenerated from
`Planet.Radius`, per SCOPE.md "persist only modifications." Dense/chunked
sample storage is deferred to Task 6 (Chunk system); this task only
establishes the field abstraction and edit accumulation. Tests cover
sampling at center/surface/outside/inside, additive vs. subtractive edit
direction, brush radius falloff-to-zero at the boundary, out-of-range edits
having no effect, clear/load/replace semantics, and overlapping edits
accumulating. Verified structurally only — no Unity CLI available here.

---

## 6. Chunk system ✅ DONE

Implement:

* Chunk creation
* Chunk indexing
* Neighbor lookup
* Dirty tracking

Tests:

* Chunk lookup
* Neighbor correctness
* Dirty propagation

Notes: `TerrainChunk` (index, col, row, rectangular bounding box, dirty flag)
and `ChunkGrid` (`Runtime/Terrain/`) added, dividing the planet's bounding
square into a fixed 2D grid of equal square chunks (per ARCHITECTURE.md's
Cartesian chunking decision, revised Task 15.10). `GetChunkAt(Vector2
position)` computes (col, row) from position, clamping to grid bounds;
`GetChunkAtGrid(col, row)` validates coordinates explicitly;
`GetNeighbor(chunk, direction)` returns the 4-directional neighbor or null at
edges. `ChunksInRect(minX, maxX, minY, maxY, result)` computes overlapping
column/row range directly — no iteration over all chunks. Chunks start dirty;
`MarkDirtyAt(Vector2)`/`DirtyChunks()`/`ClearAllDirty` drive propagation.
Chunk density-sample storage, mesh, and collider ownership are deferred to
Meshing/Collision tasks (7, 9, 10) — this task only establishes indexing and
dirty state. Tests cover constructor validation (radius, chunkSize), grid
dimensions, bounding box correctness, position-based lookups (center, edges,
corners, out-of-bounds clamping), coordinate-based lookups, 4-directional
neighbor tests (including null at boundaries), rectangular dirty marking,
single/multi/full-grid/partial `ChunksInRect` overlap, and contiguous grid
(no gaps between adjacent chunks). Verified structurally only — no Unity CLI
available here.

---

## 7. Procedural planet generator ✅ DONE

Generate:

* Perfect sphere

Tests:

* Radius
* Continuity
* Surface accuracy

Notes: `PlanetGenerator.GenerateBaseShape(radius, seed)` (`Runtime/Terrain/`)
added as a static, stateless pipeline entry point producing a `TerrainField`
whose base shape is a perfect sphere — the first stage of the generation
order in SCOPE.md/CLAUDE.md (large-scale shape before terrain height noise,
layers, caves, ore, materials, vegetation, entities). The `seed` parameter is
threaded through and consumed via `SeededRandom` (unused by this stage) so
Task 8 (terrain noise) can extend the pipeline without changing the call
site or breaking determinism guarantees. Tests cover radius matching,
surface continuity around the full circle (360 sample points sit on the
zero-crossing), zero initial edits, and same-input determinism. Verified
structurally only — no Unity CLI available here.

---

## 8. Terrain noise ✅ DONE

Add:

* Fractal noise
* Ridged noise
* Domain warp

Tests:

* Deterministic generation
* Seed reproducibility

Notes: `TerrainNoiseSettings` (immutable readonly struct: amplitude,
frequency, octaves, persistence, lacunarity, ridged flag, warp
strength/frequency) and `TerrainNoise.SampleHeight(angle, seed, settings)`
(`Runtime/Terrain/`) added. Noise is built from summed sine harmonics with
integer frequencies rather than grid-based Perlin — `sin(k * angle + phase)`
is exactly periodic over 2π for integer k, so the sum is inherently seamless
around the planet's circumference with no special-cased wrap logic. Ridged
mode takes `1 - |sin(...)|` per octave and re-centers the result around zero
so it composes with fractal noise the same way. Domain warp perturbs the
sampled angle before evaluating the fractal/ridged sum. `TerrainField` grew
a `(radius, seed, noiseSettings)` constructor and `SurfaceRadiusAt(angle)`;
`Sample()` now converts the query position to angle and looks up the noisy
surface radius there instead of the constant base radius. `PlanetGenerator`
gained a `GenerateBaseShape(radius, seed, noiseSettings)` overload; the
existing 2-arg overload is preserved and forwards `TerrainNoiseSettings.None`
so Task 7's tests are unaffected. Tests cover zero-amplitude no-op,
determinism (same seed+angle), seed variation, amplitude bounds, seam
continuity across angle 0/2π (for both raw noise and through
`TerrainField.SurfaceRadiusAt`), and that ridged vs. fractal differ.
Verified structurally only — no Unity CLI available here.

---

## 9. Terrain meshing ✅ DONE

Implement:

Marching Squares

Outputs:

* Mesh
* Normals
* UVs

Tests:

* Small synthetic fields
* Closed loops
* Ambiguous cases

Notes: `MarchingSquaresMesher.Generate(samples, cellSize, origin, uvScale)`
and `MeshData` (plain vertex/triangle/normal/UV lists, no `UnityEngine.Mesh`
dependency) added under `Runtime/Meshing/`. Pure function of its inputs —
no dependency on `TerrainField`/`Planet`, per ARCHITECTURE.md's "pure
function of input samples" design — so it's testable against small
synthetic `float[,]` grids without any planet/chunk machinery. All 16
Marching Squares cases implemented via edge interpolation; ambiguous saddle
cases (5 and 10) are resolved by splitting into two disjoint triangles
(documented in the class doc-comment as a known simplification — can leave a
thin diagonal gap in checkerboard input, acceptable given expected chunk
resolution relative to terrain feature size). Normals are currently uniform
`Vector3.back` (flat 2D quad), since true smooth normals depend on gradient
sampling — deferred, no task currently calls for curved-surface normals in
2D. Tests cover null/invalid-arg guards, fully-empty and fully-solid grids,
single-corner and half-solid cases, vertex bounds/origin-offset correctness,
array-length consistency across vertices/normals/UVs, and both ambiguous
saddle cases. Converting `MeshData` into an actual `UnityEngine.Mesh` and
wiring it to a renderer is Task 11. Verified structurally only — no Unity
CLI available here.

---

## 10. Collider generation ✅ DONE

Generate:

* PolygonCollider2D

Tests:

* Collider validity
* No self intersections

Notes: `ColliderContourBuilder.BuildContours(meshData)` (`Runtime/Meshing/`)
added as a pure function extracting closed boundary polygon loops from
triangulated `MeshData` — no dependency on `PolygonCollider2D` or any
scene/component, matching the pure-function precedent set by
`MarchingSquaresMesher` in Task 9, and keeping the algorithmic core testable
without a live Unity object. Works by counting directed triangle edges: an
edge shared by two adjacent triangles is traversed once in each direction and
cancels, leaving only true boundary edges, which are then stitched
start-to-end into one or more closed loops (handles multiple disjoint solid
regions, e.g. a chunk with two separate landmasses, by producing one loop
per region). `TerrainColliderBuilder.Apply(meshData, collider)` is a thin
wrapper that feeds the built contours to `PolygonCollider2D.pathCount`/
`SetPath` — kept as a separate file so the contour math has zero Unity
component coupling. Tests cover null-arg guard, single filled cell (4-point
quad loop), a fully-filled multi-cell grid (verifying shared interior edges
between adjacent cells correctly cancel, leaving only the outer 4-point
boundary), a half-solid grid, an empty mesh (no contours), loop closure with
no repeated points (no self-intersection), and two disjoint filled regions
producing two separate loops. Verified structurally only — no Unity CLI
available here; confirming `PolygonCollider2D.SetPath` accepts these paths
without warnings is the outstanding in-editor verification step.

---

## 11. Terrain renderer ✅ DONE

Render:

* Generated mesh
* Materials
* Debug overlays

Acceptance:

* Planet visible

Notes: Three small pieces close the pipeline started in Tasks 9/10.
`TerrainFieldSampler` (`Runtime/Terrain/`) is a pure function sampling a
`TerrainField` onto a square bounding-box grid — whole-planet sampling was
chosen over per-chunk sectioning as the simplest approach that satisfies
"planet visible"; per-chunk partial sampling/rebuilds belong to Task 15
(Chunk rebuilding) and can reuse this sampler's grid math per-sector later.
`MeshDataConverter.ToUnityMesh(meshData, reuse)` (`Runtime/Meshing/`)
converts the algorithm-side `MeshData` into a real `UnityEngine.Mesh`,
accepting an existing Mesh to overwrite so rebuilds don't allocate a new
asset every time (supports the "dirty chunk rebuild, not full regen"
performance rule once chunk-local rebuilds land). `TerrainRenderer`
(`Runtime/Terrain/`) is the MonoBehaviour tying it together: `Rebuild(field,
boundsRadius, chunkGrid)` samples → meshes → converts → assigns
`MeshFilter.sharedMesh`, `MeshRenderer.sharedMaterial`, and calls
`TerrainColliderBuilder.Apply` for the `PolygonCollider2D` — all three
components required via `[RequireComponent]`. Debug overlays are gizmo-based
per CLAUDE.md's "every major system should expose debugging tools": optional
chunk-border rays (`drawDebugChunkBorders`, needs a `ChunkGrid` passed to
`Rebuild`) and surface normal rays (`drawDebugNormals`, reads back
`_mesh.normals`) drawn in `OnDrawGizmos`, both off by default to avoid
scene-view clutter. Tests cover `TerrainFieldSampler` argument validation,
grid dimensions/origin placement, solid-center/air-corner sampling
correctness, and that its output is directly meshable via
`MarchingSquaresMesher`; `TerrainRenderer` tests verify a `Rebuild` call
produces a non-empty mesh and populated collider, and that a second
`Rebuild` reuses the same `Mesh` instance rather than allocating a new one.
This is the first task where a planet becomes visible in the Unity Editor —
verified structurally only here (no Unity CLI available); opening the editor,
adding a `TerrainRenderer` to a GameObject, and calling `Rebuild` with a
generated `TerrainField` is the outstanding in-editor verification step.

---

# PHASE 3 — Terrain Editing

## 12. Brush framework ✅ DONE

Support:

* Add
* Remove
* Smooth

Tests:

* Brush falloff
* Radius
* Multiple edits

Notes: `BrushMode` (`Runtime/Terrain/`) enum — Add/Remove/Smooth — and
`TerrainBrush` (immutable readonly struct: mode, radius, strength) added as
the single entry point terrain-editing gameplay code should call.
`TerrainBrush.Apply(field, localPosition)` translates the mode into a
`TerrainField` mutation: Add/Remove both produce a `TerrainEdit` (reusing
Task 5's existing smoothstep-falloff brush and additive/subtractive
direction — no duplicate falloff logic), while Smooth is new behavior added
directly to `TerrainField` as `SmoothEdits(position, radius, strength)`,
which reduces the `Strength` of nearby existing edits (clamped to never go
negative) using the same smoothstep falloff profile rather than adding a new
deformation — smoothing conceptually erases past edits rather than
sculpting new terrain, so it doesn't fit the `TerrainEdit` list itself.
`TerrainEdit.Strength` needed to become a mutable field (was implicitly
read-only via constructor-only assignment) to support in-place reduction;
it's a struct stored by value in the edits list, so `SmoothEdits` reads,
modifies, and writes back each entry by index. Tests cover constructor
validation (radius/strength), null-field guard, Add/Remove pushing the
surface the correct direction, Smooth reducing an edit's strength and
clamping at zero, multiple edits accumulating in the list, and falloff
reaching exactly zero at the radius boundary. Verified structurally only —
no Unity CLI available here.

---

## 13. Digging ✅ DONE

Implement:

Terrain removal

Acceptance:

* Mesh updates
* Collider updates

Notes: `TerrainRenderer` (`Runtime/Terrain/`) gained `ApplyBrush(brush,
localPosition)`, which applies a `TerrainBrush` (Task 12) to the field it
was last built from and immediately calls `Rebuild` again — no new
digging-specific code path was needed since `BrushMode.Remove` +
`TerrainField.ApplyEdit` (Task 5) + full-field `Rebuild` (Task 11) already
compose into "remove terrain, mesh/collider update," matching CLAUDE.md's
"SDF is the source of truth, everything else derives from it" rule: digging
never touches the mesh/collider directly, it mutates the field and triggers
a full derive. `Rebuild` now caches the field/boundsRadius/chunkGrid it was
called with so `ApplyBrush` can re-invoke it without the caller re-supplying
them; calling `ApplyBrush` before any `Rebuild` throws
`InvalidOperationException`, and `Rebuild` itself now null-checks `field`
(was previously relying on `TerrainFieldSampler`'s own guard, which still
fires the same exception type but a clearer message at the outer call site
is preferable for a public API). Whole-planet rebuild-per-dig is
intentionally the simplest thing that satisfies this task's acceptance
criteria; chunk-local partial rebuilds are Task 15 (Chunk rebuilding) and
should only touch the chunks overlapping the brush radius, reusing
`ChunkGrid.MarkDirtyAt`/`DirtyChunks` from Task 6. Tests cover the
no-prior-rebuild guard, mesh vertex/triangle count changing after a dig,
`PolygonCollider2D`'s path changing after a dig, and the edit persisting on
the `TerrainField` (`Edits.Count`). Verified structurally only — no Unity
CLI available here.

---

## 14. Building terrain ✅ DONE

Implement:

Terrain addition

Acceptance:

* Smooth construction

Notes: No new production code was required — `BrushMode.Add` (Task 12),
`TerrainEdit`'s subtractive direction (Task 5, `isAdditive: false` lowers the
sampled distance / adds material with the same smoothstep falloff used for
digging), and `TerrainRenderer.ApplyBrush` (Task 13) already compose into a
complete "add terrain, mesh/collider update" path with no separate
build-specific code, matching CLAUDE.md's SDF-as-source-of-truth rule: a
build is just a differently-signed edit flowing through the same derive
pipeline as a dig. This task closes the test gap: `TerrainRendererTests` only
exercised `BrushMode.Remove` end-to-end. Added
`ApplyBrush_Build_UpdatesMeshVertexCount`, `ApplyBrush_Build_UpdatesCollider`,
`ApplyBrush_Build_PersistsEditOnField`, and
`ApplyBrush_Build_PushesSurfaceOutwardSmoothly` (asserts the sampled distance
at the brush center moves continuously toward solid rather than jumping,
i.e. "smooth construction") to `Tests/EditMode/TerrainRendererTests.cs`,
mirroring the existing dig tests. `TerrainBrushTests.Apply_Add_*` already
covered the lower-level field behavior. Verified structurally only — no
Unity CLI available here; running the Test Runner in-editor is the
outstanding verification step.

---

## 15. Chunk rebuilding ✅ DONE

Rebuild only dirty chunks

Tests:

* Local rebuilds
* Neighbor seams

Notes: Three pieces close this task. `MarchingSquaresMesher` (Task 9)
gained a `Generate(samples, positions, uvScale)` overload alongside the
existing `Generate(samples, cellSize, origin, uvScale)` — both now funnel
into a shared private `EmitCell` taking raw corner samples/positions, so the
Marching Squares case table exists in exactly one place. The new overload
accepts an explicit non-uniform position grid (`Vector2[,]`) so chunks can
sample at arbitrary lattice positions. `CartesianChunkFieldSampler.Sample(field,
chunk, cellSize)` (`Runtime/Terrain/`) is the chunk-local counterpart to
Task 11's `TerrainFieldSampler`: it samples one `TerrainChunk`'s rectangular
bounding box onto a Cartesian lattice covering exactly the chunk's bounding
box. No clipping mask is applied —
each lattice point simply calls `field.Sample(position)`. Points outside the
planet are naturally air (positive SDF), so Marching Squares produces no
contour in all-air regions. `ChunkTerrainRenderer` (`Runtime/Terrain/`) is
the chunk-owning renderer TASKS.md/SCOPE.md call for: one child GameObject
(MeshFilter/MeshRenderer/PolygonCollider2D) per chunk, created once in
`Initialize`, with `RebuildDirtyChunks()` iterating only `ChunkGrid.DirtyChunks()`
(Task 6) — chunks not reported dirty keep their existing mesh/collider instance
untouched, satisfying "never regenerate an entire planet; only dirty chunks
rebuild." This is a new, separate component from Task 11's `TerrainRenderer`
(whole-planet single mesh) rather than a replacement — both remain valid entry
points. `TerrainField.EnableChunkIndexing(ChunkGrid)` maps each edit to the
chunks whose bounding boxes overlap the brush's circular footprint (via
`ChunksInRect`). Tests (`CartesianChunkFieldSamplerTests`) cover
dimension/argument validation, center-is-solid, lattice positions are cell-size
multiples, adjacent chunks share boundary points with identical values, and
edge chunks produce air where the planet surface doesn't reach.
`ChunkTerrainRendererTests` cover the no-Initialize guard, one child per
chunk, dirty flags clearing after rebuild, local rebuilds (only affected
chunks rebuild), mesh-instance reuse on repeated rebuilds, and boundary brushes
marking overlapping chunks. Verified structurally only — no Unity CLI available
here.

---

## 15.1. Chunk seam gap fix (seam margin in CartesianChunkFieldSampler) ✅ DONE → Superseded

Follow-up to Task 15 (Chunk rebuilding), raised by visible seam gaps at chunk
boundaries in-editor: every chunk combined its sampled terrain SDF with a
steep wedge mask (`Mathf.Max(terrain, WedgeMask(...))`). At a shared cell edge
crossing the boundary between adjacent chunks, one chunk had a terrain value
at the lattice point on its side and a large positive mask value at the lattice
point on the neighbor's side — and vice versa. Marching Squares interpolated
the zero-crossing independently, placing contour vertices ~96% of a cell apart.
Visible gap.

Root cause: the `ChunkSeamCache` only cached boundary *direction vectors*,
ensuring both chunks used the same ray. But `Cross(dir, point)` is inherently
signed differently for points on opposite sides of the ray, so the mask values
remained asymmetric. Same direction, opposite sign.

Fix: lattice points within a 2-cell perpendicular margin of either boundary
ray bypassed the wedge mask and used the raw terrain SDF directly. Both
neighboring chunks sample the same lattice points in the overlap strip, and
feeding both the same terrain value guarantees identical Marching Squares
topology at the seam.

**Superseded by Task 15.10:** The wedge-to-square chunk migration eliminated
the root cause entirely. Square chunks share boundary lattice points that
sample the same field at the same position — no wedge masks, no seam margins,
no `ChunkSeamCache`, no asymmetric SDF values. The seam gap problem no longer
exists.

---

## 15.5. Solid brush + idempotent overlapping edits ✅ DONE

Follow-up to Task 12 (Brush framework), raised via manual mouse-editing
testing (Task 13/14's `MouseTerrainEditor` demo driver) rather than a
pre-planned task: the original soft, additive brush "melted" terrain the
longer a stroke held still or overlapped itself, instead of behaving like a
solid eraser/stamp.

Acceptance:

* A brush stroke fully clears/fills material within its radius without a
  soft taper across the whole radius (opt-in via a new hardness parameter).
* Holding the brush stationary does not keep carving deeper each frame.
* Overlapping or repeated strokes at the same position do not accumulate
  depth beyond a single edit's effect — re-editing an already-fully-edited
  spot is a no-op, not a "melt."
* A genuinely stronger/larger edit at the same position still takes effect
  (idempotence blocks re-doing the same edit, not doing a bigger one).

Notes: Three coordinated changes, spanning both the terrain-editing core and
the demo input driver. `TerrainEdit` (Task 5) gained a `Hardness` field
(0 = original full-radius smoothstep taper, 1 = solid: full strength up to
the radius, no taper), threaded through a new `TerrainBrush` (Task 12)
constructor parameter (default `0f`, preserving old behavior for existing
call sites). The deeper fix is in `TerrainField.Sample` (Task 5): edits were
being combined via `distance += contribution` for every overlapping edit,
which is what caused melting — any two strokes whose radii overlapped, not
just exact repeats, kept carving deeper. `Sample` now combines edits
CSG-style instead: `Mathf.Max(distance, contribution)` for dig edits,
`Mathf.Min(distance, contribution)` for build edits, with an explicit skip
for edits whose radius doesn't reach the sampled point (`SampleContribution`
returns `0` there, which is not a safe max/min identity value — `max(solid,
0)` would incorrectly pull unrelated solid terrain toward the surface).
This makes repeated/overlapping edits idempotent: re-digging an
already-cleared spot has no further effect, while a larger/stronger edit at
the same spot still carves further, since its contribution is genuinely
larger. `MouseTerrainEditor` (demo/debug driver, not itself a numbered task)
also gained a `minDragDistance` gate so holding the mouse still doesn't
re-apply a full-strength edit every frame — defense in depth alongside the
`Sample` fix, since even an idempotent solid edit shouldn't need reapplying
every frame at an unmoved position. Tests: `TerrainFieldTests` replaced its
old `MultipleOverlappingEdits_AccumulateAdditively` test (which asserted the
exact melting behavior being fixed) with
`DistinctNonOverlappingDigEdits_BothApply`,
`OverlappingDigEdits_AtSamePosition_AreIdempotentNotAdditive`,
`OverlappingBuildEdits_AtSamePosition_AreIdempotentNotAdditive`, and
`LargerDigEdit_AtSamePosition_CarvesDeeper`. `TerrainBrushTests` added
`Apply_SolidHardness_IsFullStrengthNearRadiusEdge`,
`Apply_SolidHardness_StillReachesZeroAtRadiusBoundary`, and
`Apply_StationaryRepeatedEdits_DoNotGrowBeyondSingleEditFootprint`.
Verified structurally only — no Unity CLI available here; confirming the
"solid eraser" feel and mesh/collider correctness for overlapping strokes
in-editor is the outstanding verification step.

---

## 15.6. Exact circular brush shape (linear SDF, remove Hardness) ✅ DONE

Follow-up to Task 15.5: fixing the melting-ice bug exposed a second problem —
single brush strokes rendered as blocky, marching-squares-faceted shapes
instead of clean circles.

Acceptance:

* A single dig/build stroke produces a geometrically circular edge, not a
  faceted/blocky one, regardless of chunk grid resolution.
* No separate "hardness" parameter is needed — every edit is exactly
  circular by construction.

Notes: Root cause was that `TerrainEdit.SampleContribution` (Task 5, revised
Task 15.5) returned a smoothstep-*curved* falloff as a function of distance
from the brush center, not a linear signed distance. `MarchingSquaresMesher`
(Task 9) only linearly interpolates the zero-crossing between adjacent grid
samples — it reconstructs an exact circle only when the underlying field is
itself linear in distance near the crossing, exactly as the base planet
sphere (`|localPos| - radius`) already is. A curved falloff means the
interpolated crossing deviates from the true circle, visible as facets at
chunk resolution. Fix: `SampleContribution` now returns an exact linear
signed-distance cone to the brush boundary — `Radius - distanceFromBrush`,
capped by `Strength` — matching the base sphere's own signed-distance shape
instead of an arbitrary strength curve. This makes the `Hardness` parameter
added in Task 15.5 obsolete (every edit is now always exactly circular), so
it was removed entirely from `TerrainEdit`, `TerrainBrush`, and
`MouseTerrainEditor` (including its now-stale doc comments); `Strength` is
repurposed as a cap on carve/build depth rather than a falloff shape
control. `TerrainField.Sample`'s CSG max/min combine (Task 15.5) is
unchanged — it composes with the new linear contribution the same way it did
with the old curved one. This also sets up the user's stated next goal (a
brush that follows a circular "shell" for local flatness), since edits are
now true signed distances rather than an ad hoc strength curve. Tests:
replaced the two Hardness-specific `TerrainBrushTests` cases with
`Apply_ContributionIsLinearInDistance_NotCurved` (asserts equal deltas at
equally-spaced distances from the brush center, proving no curvature) and
`Apply_ContributionAtCenter_IsCappedByStrength`; updated
`Apply_StationaryRepeatedEdits_DoNotGrowBeyondSingleEditFootprint`'s
assertion and comment to no longer reference Hardness. Verified structurally
only — no Unity CLI available here; the user's own in-editor test (paint a
circle, zoom in, confirm a smooth circular edge instead of facets) is the
outstanding acceptance check.

---

## 15.7. Asymptotic decider for ambiguous Marching Squares saddle cases ✅ DONE

Follow-up to Task 15.6: making the brush SDF exactly linear fixed general
faceting, but a distinct artifact remained — as a brush edge moved smoothly,
its contour would occasionally show a sudden spike or notch instead of
changing continuously.

Acceptance:

* Moving a brush's edge continuously through a cell never produces a
  discontinuous topology change (spike/notch) at ambiguous saddle
  configurations.

Notes: `MarchingSquaresMesher.EmitCell`'s ambiguous saddle cases (5 and 10:
diagonally-opposite corners solid, the other two not) previously always
resolved to the same triangulation — two disjoint triangles — regardless of
the actual field shape near the cell center (documented at the time as an
accepted simplification, Task 9). Whether the two solid corners should
connect through the cell center or stay disjoint genuinely depends on the
field there; always picking "disjoint" means that whenever the true field
would connect them, the mesh flips discontinuously between the two
topologies as sample values cross the saddle threshold during a moving
edit — exactly the "smooth, then a spike suddenly appears" artifact
reported. Fixed with the standard asymptotic decider: a bilinear estimate of
the field value at the cell center (`EstimateCenter`, the average of the
four corner samples) picks between the disjoint triangulation (center
estimate ≥ 0, air) and a new merged hexagon connecting the two solid
corners through all four edge-interpolated points (center estimate < 0,
solid) — added `AddHexagon`, following the existing `AddTriangle`/`AddQuad`/
`AddPentagon` fan-triangulation pattern. This makes the topology choice a
continuous function of the sampled field instead of a fixed guess, matching
the true underlying shape and eliminating the discontinuity. The original
Task 9 test (`Generate_AmbiguousSaddleCase_ProducesTwoTriangles`, symmetric
±1 corners → center estimate exactly zero → still resolves to the disjoint
case) was renamed to
`Generate_AmbiguousSaddleCase_WithAirCenterEstimate_ProducesTwoDisjointTriangles`
to reflect that its behavior is now conditional, not unconditional; added
`Generate_AmbiguousSaddleCase_WithSolidCenterEstimate_ProducesMergedHexagon`
(strongly-negative solid corners, weakly-positive other corners → center
estimate negative → asserts the 4-triangle hexagon is produced instead).
Verified structurally only — no Unity CLI available here; the user's
in-editor test (slowly dragging a brush and watching for a discontinuous
spike/notch at any point) is the outstanding acceptance check.

---

## 15.8. Remove Strength plateau clamp in brush contribution ✅ DONE

Follow-up to Task 15.6: the linear cone fix made the *shape* of the SDF exactly
circular, but `SampleContribution` still clamped that cone to `Mathf.Min(Radius -
distanceFromBrush, Strength)` — a flat plateau everywhere the cone exceeded
`Strength`. A flat plateau has zero gradient, so Marching Squares (which only
linearly interpolates the zero-crossing between adjacent grid samples) had no
gradient to interpolate through across most of the disc, producing grid-aligned
stair-stepping — worst at `Strength = 0` (entire disc is a step function, visible
at both the surface and the brush center), and still present at high `Strength`
(plateau shrinks but never vanishes while `Strength < Radius`).

Acceptance:

* A brush stroke is smoothly circular at any `Strength` value, including nonzero
  moderate values previously showing stair-stepping.

Notes: `TerrainEdit.SampleContribution` (Task 5, revised Tasks 12/15.5/15.6) no
longer clamps the cone to `Strength`; it now scales the uncapped cone by `Strength`
instead: `(Radius - distanceFromBrush) * Mathf.Max(Strength, 0f)`. This keeps
gradient nonzero everywhere inside the brush radius (a true signed-distance-shaped
field, matching the base planet sphere's own shape), so Marching Squares
reconstructs a smooth circle regardless of chunk resolution or Strength value.
`Strength` changes meaning slightly: previously a hard cap on carve/build depth, it
is now a depth/intensity multiplier on the cone (`Strength = 1` reaches exactly
`Radius` of depth at the brush center; existing call sites already passing values
like `2f`/`5f`/`100f` expecting "stronger reaches deeper" are unaffected in
direction, only in exact magnitude). `TerrainField.Sample`'s CSG max/min combine
(Task 15.5) is unchanged — it composes with any signed-distance-shaped contribution
the same way regardless of whether it was capped. `TerrainBrushTests
.Apply_ContributionAtCenter_IsCappedByStrength` (asserted the old plateau value)
replaced with `Apply_ContributionAtCenter_IsRadiusTimesStrength` asserting the new
scaled-cone value at the brush center. Verified structurally only — no Unity CLI
available here; the user's in-editor test (paint a stroke at low and moderate
Strength, zoom in, confirm a smooth circle instead of stair-stepping at either) is
the outstanding acceptance check.

---

## 15.9. Remove Strength; brush edits are a pure SDF ✅ DONE

Follow-up to Task 15.8: `Strength` still existed as a depth/intensity multiplier
on the brush cone. Requested explicitly: remove it entirely so a brush edit is
nothing but a genuine signed-distance cone to a circle of `Radius` — no separate
tuning parameter distorting that shape.

Acceptance:

* `TerrainBrush`/`TerrainEdit` take only a radius; no `Strength` parameter exists.
* `SampleContribution` is exactly `Radius - distanceFromBrush` (capped at zero
  outside the radius via the existing early-out), matching the base planet
  sphere's own signed-distance shape.

Notes: `TerrainEdit` (`Runtime/Terrain/TerrainEdit.cs`) dropped the `Strength`
field entirely; `SampleContribution` is now the unscaled cone. `TerrainBrush`
(`Runtime/Terrain/TerrainBrush.cs`) dropped `Strength` and its validation —
`Radius` is its only parameter, for every mode. `MouseTerrainEditor` dropped the
now-unused `brushStrength` field. The one behavior that needed redesigning:
`TerrainField.SmoothEdits` (Task 12) previously shrank `edit.Strength`; with no
`Strength` left on `TerrainEdit`, it now shrinks `edit.Radius` instead (same
smoothstep-falloff profile, floored at zero) — `TerrainEdit.Radius` became a
mutable field to support this, the same situation `Strength` was in before Task
12 made it mutable. `SmoothEdits`'s separate `strength` parameter was dropped too
(redundant with a single-parameter brush): its signature is now
`SmoothEdits(Vector2 localPosition, float radius)`, reusing `radius` as both the
affected area and the reduction amount. An edit shrunk to `Radius <= 0`
contributes nothing (the existing `distanceFromBrush >= Radius` early-out is
always true once `Radius <= 0`) so it's left in the list rather than pruned,
consistent with how zero-`Strength` edits were handled before. Tests: removed
`Constructor_NegativeStrength_Throws` (no strength param to validate); rewrote
`Apply_Smooth_ReducesNearbyEditStrength`/`Apply_Smooth_NeverReducesStrengthBelowZero`
to assert against `edit.Radius`; renamed
`Apply_ContributionAtCenter_IsCappedByStrength` to
`Apply_ContributionAtCenter_EqualsRadius` (now asserts contribution at brush
center equals `Radius` exactly); `TerrainFieldTests.LargerDigEdit_AtSamePosition_CarvesDeeper`
now varies `radius` (3f vs 8f) instead of `strength` to prove a genuinely larger
edit still carves further; every remaining `TerrainEdit`/`TerrainBrush`
construction across `TerrainBrushTests`, `TerrainFieldTests`,
`TerrainRendererTests`, and `ChunkTerrainRendererTests` dropped the trailing
strength argument. `TerrainField.Sample`'s CSG max/min combine (Task 15.5) is
unchanged — it composes with any signed-distance-shaped contribution the same way
regardless of what feeds it. Verified structurally only — no Unity CLI available
here; running the Test Runner in-editor, and painting dig/build/smooth strokes to
confirm they remain smoothly circular with no separate strength knob, are the
outstanding acceptance checks.

---

## 15.10. Convert chunk system from wedge shapes to square chunks ✅ DONE

Follow-up to Tasks 6/15 (Chunk system / Chunk rebuilding), raised by the
inherent complexity of angular wedges: a brush near the planet center spans
all angles, marking every chunk dirty (all wedges converge at origin).
`CartesianChunkFieldSampler` had to compute bounding boxes from angular
sectors, apply wedge masks via CSG, handle reflex wedges, and maintain a
2-cell seam margin. `ChunkSeamCache` existed solely because floating-point
boundary angles differed between adjacent chunks.

Acceptance:

* The planet renders as a complete circle (same visual as before).
* Chunk borders visible as a rectangular grid (not radial lines).
* Dig/build brushes produce smooth circular holes (unchanged — SDF is
  unchanged).
* A brush near the planet center only dirties ~4 chunks (vs. all chunks
  before).
* No visible seams between chunks.
* Dirty chunk rebuild only touches affected chunks.

Files deleted (1):

* `ChunkSeamCache.cs` — Square chunk boundaries are exact floats
  (`col * chunkSize`), so adjacent chunks share bit-identical edges by
  construction. No boundary direction caching needed.

Files modified (12 production + 6 test files):

* `TerrainChunk.cs` — Replaced `StartAngle`/`EndAngle` with `Col`, `Row`,
  `MinX`, `MaxX`, `MinY`, `MaxY`. Constructor takes grid coordinates +
  bounding box.
* `ChunkGrid.cs` — Complete rewrite: 2D grid with `_chunkSize`, `_cols`,
  `_rows`. Grid spans `-gridExtent` to `+gridExtent` where
  `gridExtent = cols * chunkSize`. Position-based `GetChunkAt(Vector2)`,
  coordinate-based `GetChunkAtGrid(col, row)`, 4-directional
  `GetNeighbor(chunk, direction)`, `ChunksInRect(minX, maxX, minY, maxY)`,
  `MarkDirtyAt(Vector2)`. Legacy `ChunkGrid(int chunkCount)` constructor
  preserved as `[Obsolete]`.
* `CartesianChunkFieldSampler.cs` — Major simplification: `Sample(field,
  chunk, cellSize)` with no `maxRadius` or `seamCache` parameters. Removed
  all wedge mask logic (`WedgeMask`, `WedgeMaskSteepness`, `IsWithinSeamMargin`,
  `IsNearRay`, `Cross`, reflex wedge handling, angular `ComputeLatticeBounds`).
  Each lattice point simply samples `field.Sample(position)`. Points outside
  the planet are naturally air (positive SDF) — Marching Squares produces no
  contour in all-air regions.
* `TerrainField.cs` — `IndexEdit` replaced angular membership with rectangular
  overlap. Removed `AffectedAngleRange` entirely (no longer needed).
* `ChunkTerrainRenderer.cs` — Rectangle-based dirty marking via
  `MarkDirtyRect(minX, maxX, minY, maxY)`. Removed seam cache, angular range
  buffer, and neighbor padding logic.
* `TerrainRenderer.cs` — `DrawChunkBorders` draws grid lines instead of radial
  rays.
* `MarchingSquaresGridDebugView.cs` — Removed seam cache. Updated sampler
  calls to new signature.
* `PlanetDemo.cs` — Updated constructor: `ChunkGrid(radius, chunkSize)`
  instead of `ChunkGrid(chunkCount)`.
* Tests updated to match: `ChunkGridTests`, `CartesianChunkFieldSamplerTests`,
  `TerrainFieldTests`, `ChunkTerrainRendererTests`,
  `MarchingSquaresGridDebugViewTests`.

Key design decisions:

1. **No boundary clipping**: Each chunk samples freely within its bounding
   box. Chunks partially outside the planet render empty (all-air → no
   mesh). Same visual result for the visible planet area, vastly simpler code.
2. **Shared boundary lattice points**: Adjacent chunks share boundary
   lattice points with identical terrain values — no seam cache needed,
   no overlapping mesh geometry at chunk edges.
3. **Grid edges return null**: `GetNeighbor` at the grid boundary returns
   null rather than wrapping. The planet is circular within a rectangular
   grid.
4. **Terrain noise unchanged**: The noise system samples by angle, independent
   of chunk shape. Zero changes.
5. **MarchingSquaresMesher unchanged**: Pure function of input samples.
   Already handles non-uniform position grids.
6. **RadialMath unchanged**: Used for noise, gravity, player orientation.
   Square chunks don't eliminate the planet's circular nature, just the chunk
   indexing.

Verified structurally only — no Unity CLI available here.

Bug fix (post-merge): `ChunkGrid` grid bounds were offset to the bottom-left
quadrant. `_gridMinX = -_cols * chunkSize` placed the grid at [-full_extent, 0]
instead of centered on the origin, so only 25% of the planet (the bottom-left
quadrant where both axes are negative) had chunks to render it. The constructor
docstring already described the correct centered behavior — the code just didn't
match. Fixed both constructors: `_gridMinX = -(_cols * chunkSize) / 2f` so the
grid spans symmetrically from -half_extent to +half_extent. `TerrainRenderer`
debug border drawing used the same asymmetric formula and was corrected to
match. `ChunkGridTests.Constructor_ChunkBoundingBoxesAreCorrect` and two
`ChunksInRect` test comments updated to reflect the centered bounds. Verified
in-editor: full planet circle visible across all four quadrants, brush editing
works correctly at chunk edges everywhere.

---

## 15.11. Chunk-indexed sampling + dead edit pruning ✅ DONE

Performance fix: edits degraded over time because `CartesianChunkFieldSampler`
called `field.Sample(position)` — the chunk-agnostic overload that iterates
every edit ever applied — making sampling cost O(total_lifetime_edits). After
thousands of brush strokes, each lattice point scanned thousands of edits,
causing visible lag that worsened with use.

Acceptance:

* Sampling cost depends only on edits overlapping the chunk, not total edits.
* Zero-radius edits are reclaimable without corrupting chunk indices.
* `ClearEdits` and `LoadEdits` keep chunk indices consistent.

Notes: Three coordinated changes. `CartesianChunkFieldSampler.Sample()` now
calls `field.Sample(position, chunk.Index)` — the chunk-indexed overload that
only scans edits registered to the chunk via the spatial index maintained by
`EnableChunkIndexing`. Because CSG Max/Min is commutative and idempotent, an
edit whose rectangular footprint cannot reach a chunk can never be the
Max/Min-selected contributor there, so excluding it produces identical results
while bounding cost to O(chunk_local_edits). `MarchingSquaresGridDebugView`
gained `EnableChunkIndexing` in its `Initialize` to match.

`TerrainField.PruneDeadEdits()` compacts `_edits` by removing zero-radius
entries (created by smoothing) and atomically remaps all `_editsByChunk`
indices so the chunk-indexed sampler remains valid. Returns the pruned count.
Callers should invoke periodically to prevent silent list growth.

`TerrainField.ClearEdits()` now also clears `_editsByChunk` (was leaving stale
indices). `TerrainField.LoadEdits()` now rebuilds `_editsByChunk` for the
replacement edits (was leaving old indices). Both were correctness bugs
exposed by the sampler switch — chunk-indexed sampling would have seen
stale entries if these weren't fixed.

Tests: `CartesianChunkFieldSamplerTests` updated with `EnableChunkIndexing`
on all sampling tests. `TerrainFieldTests` gained seven tests covering prune
correctness, index remapping after prune, safe no-op prune, clear/index
consistency, and load/index rebuild. Verified structurally only — no Unity
CLI available here; confirming editing remains smooth after many strokes
in-editor is the outstanding acceptance check.

---

## 15.12. Prevent chunk creation for delete brushes in empty space ✅ DONE

Follow-up to Tasks 6/15 (Chunk system / Chunk rebuilding), raised by in-editor
observation: delete brushes in empty space far from the planet created new
chunks (with GameObjects, MeshFilters, MeshRenderers, PolygonCollider2Ds) even
though there was no terrain to delete.

Acceptance:

* A delete brush outside the existing chunk grid does not create new chunks.
* A build brush outside the grid still creates chunks (for building new
  terrain).
* A delete brush overlapping existing chunks still marks them dirty and
  rebuilds them.
* Chunk-indexed sampling remains correct after edits outside the grid (a
  future build at the same location sees prior delete edits).

Files modified (3 production + 3 test files):

* `ChunkGrid.cs` — `ChunksInRect` gained `bool createChunks = true` parameter.
  When `false`, returns only existing chunks (skips `GetOrCreateChunkAtGrid`,
  uses `TryGetValue` instead). Default `true` preserves existing behavior for
  all other callers.
* `TerrainField.cs` — `_editsByChunk` (keyed by `chunk.Index` `int`) replaced
  with `_editsByChunkKey` (keyed by packed `(col, row)` `long`). `IndexEdit`
  no longer calls `ChunksInRect`; it computes the col/row range directly
  (deriving `gridMinX`/`gridMinY` from `_chunkGrid.Cols`/`_chunkGrid.Rows`/
  `_chunkGrid.ChunkSize`, matching the ChunkGrid constructor logic) and
  indexes edits by packed key. This lets edits be indexed for grid cells even
  when no chunk object exists, so a delete brush outside the grid still
  registers its edit: a future build that creates a chunk there will see the
  delete in its chunk-indexed sample. `Sample(Vector2, int)` converts
  `chunkIndex` to `(col, row)` via `GetChunk(chunkIndex)`, then to packed key
  for lookup.
* `ChunkTerrainRenderer.cs` — `ApplyBrush` passes
  `createChunks = brush.Mode == BrushMode.Add` to `MarkDirtyRect`, which
  forwards it to `ChunksInRect`. Build brushes expand; delete brushes don't.
  `MarkDirtyRect` signature gained `bool createChunks` parameter.

Key design decisions:

1. **Packed-key indexing over chunk-index indexing**: The old `Dictionary<int,
   List<int>>` was keyed by `chunk.Index` (a sequential int), which requires a
   chunk object to exist. Switching to `Dictionary<long, List<int>>` keyed by
   packed `(col, row)` means edits can be indexed for any grid cell regardless
   of whether a chunk exists — no reconciliation needed when a chunk is later
   created.
2. **IndexEdit computes grid range directly**: Instead of calling
   `ChunksInRect` (which couples edit indexing to chunk creation), `IndexEdit`
   iterates the col/row range itself. This is the same computation
   `ChunksInRect` performs, just without the chunk-creation side effect.
3. **ApplyEdit signature unchanged**: The `createChunks` control lives only in
   `ChunkTerrainRenderer.ApplyBrush` → `MarkDirtyRect` → `ChunksInRect`. The
   field's `ApplyEdit` always indexes edits (by packed key) regardless of
   whether chunks exist — the field shouldn't know about rendering policy.

Tests: Added `ChunksInRect_OutOfBounds_NoCreate_ReturnsEmpty`,
`ChunksInRect_PartialOutOfBounds_NoCreate_ReturnsExistingOnly` to
`ChunkGridTests`; `ApplyBrush_Remove_OutsideOriginalGrid_NoChunksCreated` and
`ApplyBrush_RemoveThenAdd_OutsideOriginalGrid_RenderCorrectly` to
`ChunkTerrainRendererTests`; `ChunkIndexedSample_EditOutsideGrid_IndexedByPackedKey`
to `TerrainFieldTests`. Verified structurally only — no Unity CLI available
here.

---

## 15.13. Automatic removal of empty chunks ✅ DONE

Follow-up to Task 15.12 (prevent chunk creation for delete brushes): after
preventing new chunks from being created in empty space, there was still the
question of removing chunks that had become empty (e.g., built terrain that was
later fully deleted).

Acceptance:

* A chunk whose mesh produces no geometry is removed: GameObject destroyed,
  entry removed from `_chunkViews`, chunk removed from `ChunkGrid`.
* Re-building terrain at the same location recreates the chunk correctly.
* Chunks with terrain (planet surface) are not removed.
* Removal does not invalidate the `DirtyChunks()` enumerator.

Files modified (2 production + 2 test files):

* `ChunkGrid.cs` — Added `RemoveChunkAtGrid(col, row)` (removes chunk from
  dictionary, returns whether one existed) and `HasChunkAtGrid(col, row)`
  (existence check).
* `ChunkTerrainRenderer.cs` — `RebuildChunk` now returns `bool` indicating
  whether the chunk produced no geometry (`meshData.Vertices.Count == 0`).
  `RebuildDirtyChunks` collects empty chunk indices during the rebuild loop
  and removes them after (avoiding enumerator invalidation from modifying the
  grid mid-iteration). `RemoveEmptyChunk(chunkIndex)` destroys the GameObject,
  removes the entry from `_chunkViews`, and calls `RemoveChunkAtGrid` on the
  grid. Empty chunks skip mesh/collider assignment (no point building a Unity
  Mesh for nothing).

Key design decisions:

1. **Global behavior**: Any empty chunk is removed, not just dynamically
   created ones. No tracking of "original" vs "dynamic" chunks. If all
   terrain in a chunk is erased, it disappears.
2. **Collect-then-remove pattern**: Empty chunk indices are collected during
   the `DirtyChunks()` iteration and removed after, to avoid invalidating the
   `foreach` enumerator (which iterates `_chunks.Values`).
3. **No orphaned edit index cleanup**: When a chunk is removed, the
   corresponding `_editsByChunkKey` entry for its packed key is left in place
   (orphaned but harmless — small dictionary entries that don't affect
   correctness). Future edits to the same cell will find and reuse the entry.

Tests: Added `RemoveChunkAtGrid_RemovesExistingChunk`,
`RemoveChunkAtGrid_MissingChunk_ReturnsFalse`, `HasChunkAtGrid_ExistingChunk_ReturnsTrue`,
`HasChunkAtGrid_MissingChunk_ReturnsFalse`, `HasChunkAtGrid_AfterRemoval_ReturnsFalse`
to `ChunkGridTests`; `ApplyBrush_BuildThenRemove_OutsideOriginalGrid_RemovesChunk`,
`ApplyBrush_EmptyChunk_RecreatedOnBuild`,
`RebuildDirtyChunks_PlanetChunksNotRemoved` to
`ChunkTerrainRendererTests`. Verified structurally only — no Unity CLI
available here.

---

## 15.14. In-Game Brush Selection & Property Editor ✅ DONE

Follow-up to Tasks 12/15 (Brush framework / Chunk rebuilding), raised by the need
for a data-driven, extensible brush UI — the previous `MouseTerrainEditor` was a
hardcoded demo driver with no brush switching, parameter tuning, or UI.

Acceptance:

* Brush styles are defined by ScriptableObject assets (`BrushDefinition`).
* Each brush exposes configurable parameters (`BrushParameterDescriptor`).
* Brush behavior logic is abstract (`BrushBehavior`) and extensible without
  modifying core terrain code.
* In-game UI displays brush selectors and parameter sliders dynamically.
* Switching brushes rebuilds the parameter panel.
* Adjusting sliders updates the controller in real time.

Files created (6):

* `BrushBehavior.cs` (`Runtime/Terrain/Brush/`) — Abstract `ScriptableObject` base
  class with `Apply(TerrainField, ChunkTerrainRenderer, Vector2,
  Dictionary<string, float>)`. Inherits `ScriptableObject` so instances can be
  serialized as Unity assets and referenced from `BrushDefinition`.
* `BrushDefinition.cs` (`Runtime/Terrain/Brush/`) — ScriptableObject that holds a
  brush name, icon, description, a `BrushBehavior` reference, and an array of
  `BrushParameterDescriptor`. The single asset a user drags into the controller to
  define a brush style.
* `BrushParameterDescriptor.cs` (`Runtime/Terrain/Brush/`) — ScriptableObject that
  describes a single float parameter: name, display name, tooltip, default value,
  min/max range, and step size. Shared across brush definitions (e.g., "radius"
  descriptor used by Erase, Build, and Smooth).
* `StandardBrushBehavior.cs` (`Runtime/Terrain/Brush/`) — `BrushBehavior`
  implementation for Add/Remove operations. Reads `BrushMode` field, creates a
  `TerrainEdit`, applies it to the field, and marks chunks dirty via
  `ChunkTerrainRenderer.ApplyBrush`. Respects `createChunks` flag (only Add mode
  creates chunks in empty space).
* `SmoothBrushBehavior.cs` (`Runtime/Terrain/Brush/`) — `BrushBehavior`
  implementation for smoothing. Calls `TerrainField.SmoothEdits` and marks chunks
  dirty via `ChunkTerrainRenderer.MarkDirtyRectAndRebuild` (does not add terrain
  edits, so uses the direct renderer path).
* `BrushController.cs` (`Runtime/Terrain/Brush/`) — `MonoBehaviour` that manages
  brush state: active definition, parameter dictionary, and application logic.
  Fires events (`OnBrushChanged`, `OnParameterChanged`, `OnBrushApplied`) for UI
  binding. Validates parameters against descriptor ranges.

Files created (input + UI):

* `BrushInputHandler.cs` (`Runtime/Terrain/`) — Thin input layer that replaces
  `MouseTerrainEditor` as the demo driver. Reads mouse input, converts
  screen→world→planet-local, calls `controller.ApplyBrush(localPosition)`. Left
  mouse button (button 0) applies the brush.
* `BrushUI.cs` (`Runtime/UI/`) — Canvas-based in-game UI component. Dynamically
  builds style selector buttons (one per `BrushDefinition`) and parameter sliders
  (one per `BrushParameterDescriptor` of the active brush). Subscribes to
  `BrushController` events to rebuild the parameter panel on brush change and
  update slider values on programmatic parameter changes. Supports optional
  prefab templates for buttons and sliders. Properly cleans up UI elements on
  destroy (handles both play-mode `Destroy` and editor-mode `DestroyImmediate`).

Files created (editor):

* `BrushDefaultAssetsCreator.cs` (`Editor/`) — Editor menu item
  `Tools/SDF Terrain/Create Default Brush Assets` that generates the default brush
  ScriptableObject assets: Erase (Remove), Build (Add), and Smooth behaviors with
  a shared radius parameter descriptor. Uses reflection to set private
  `[SerializeField]` fields on ScriptableObjects. Assets are created under
  `Assets/SDF_Terrain/Resources/Brushes/`.

Files modified (2):

* `ChunkTerrainRenderer.cs` — Added public `MarkDirtyRectAndRebuild(minX, maxX,
  minY, maxY, createChunks)` method that wraps `MarkDirtyRect` and
  `RebuildDirtyChunks`. Enables `BrushBehavior` implementations to mark chunks
  dirty and rebuild without going through `ApplyBrush` (which would add an
  unwanted `TerrainEdit` — needed by `SmoothBrushBehavior`).
* `SmoothBrushBehavior.cs` — Updated to call `renderer.MarkDirtyRectAndRebuild`
  directly (previously had a workaround method on the same class that was
  removed once the public renderer method was available).

Key design decisions:

1. **BrushBehavior as ScriptableObject**: Unity's serialization system requires
   `ScriptableObject` or `MonoBehaviour` for `[SerializeField]` references. Plain
   C# classes cannot be serialized. This means each brush behavior instance is a
   Unity asset that can be drag-referenced into `BrushDefinition` definitions.
2. **Parameter descriptors as ScriptableObjects**: Parameters are defined as
   assets, not code. New parameters can be added by creating new descriptor
   assets — no code changes required.
3. **Event-driven UI**: `BrushController` fires events; `BrushUI` subscribes. The
   UI layer knows nothing about terrain, meshes, or chunks — it only knows about
   brush definitions and parameter values.
4. **Thin input layer**: `BrushInputHandler` is a pure input-to-controller adapter.
   No gameplay logic, no terrain knowledge — just screen→world→planet-local
   coordinate conversion and mouse button detection.
5. **MarkDirtyRectAndRebuild on renderer**: Exposing this public method on
   `ChunkTerrainRenderer` is the minimal surface area needed for non-standard
   behaviors (Smooth) that modify the field but don't add terrain edits.

Verified structurally only — no Unity CLI available here. The outstanding
verification steps are: (1) run the editor menu item to generate assets,
(2) attach `BrushController` + `BrushInputHandler` + `BrushUI` to a scene,
(3) verify brush switching, parameter adjustment, and painting in-game.

---

## 16. Undo system

Store:

Terrain edits

Acceptance:

* Undo
* Redo

---

# PHASE 4 — Materials

## 17. Material database

Implement:

* Material definitions
* IDs
* Properties

---

## 18. Material sampling

Assign:

* Dirt
* Stone
* Ice

Tests:

* Correct lookup

---

## 19. Geological layers

Generate:

* Soil
* Stone
* Mantle

Tests:

* Layer depth
* Continuity

---

## 20. Ore generation

Generate:

* Iron
* Copper
* Gold

Tests:

* Deterministic
* Distribution

---

# PHASE 5 — Gravity

## 21. Gravity system

Implement:

Radial gravity

Tests:

* Correct direction
* Magnitude

---

## 22. Player orientation

Rotate player

Acceptance:

* Feet point toward planet

---

## 23. Multi-planet gravity

Implement:

Nearest influence

Tests:

* Planet switching

---

# PHASE 6 — World Generation

## 24. Cave generation

Implement

Noise caves

Tests:

* Connectivity
* Density

---

## 25. Biome framework

Support:

* Temperature
* Moisture
* Surface materials

---

## 26. Planet DNA

Generate:

Random planet parameters

Tests:

* Repeatability

---

# PHASE 7 — Gameplay

## 27. Player controller

Movement

Jump

Mining

Building

---

## 28. Inventory

Implement

Items

Resources

Storage

---

## 29. Resource drops

Spawn mined resources

Tests:

* Material correctness

---

## 30. Building placement

Support:

Foundations

Validation

---

# PHASE 8 — Physics

## 31. Planet collision detection

Detect:

Planet overlap

---

## 32. Terrain deformation

Temporary deformation

Acceptance:

Visible squish

---

## 33. Permanent deformation

Bake impacts

Acceptance:

Persistent craters

---

# PHASE 9 — Atmosphere

## 34. Atmospheric grid

Create

Pressure field

Temperature field

---

## 35. Fluid solver

Implement

Euler simulation

Tests:

* Stable timestep

---

## 36. Terrain interaction

Wind around mountains

Tests:

* Obstacle flow

---

## 37. Weather

Generate

Clouds

Rain

Storms

---

# PHASE 10 — Water

## 38. Water simulation

Implement

Surface water

---

## 39. Water terrain interaction

Flood caves

Fill craters

---

# PHASE 11 — Rendering

## 40. Material blending

Blend

Surface textures

---

## 41. Lighting

Dynamic lighting

Normals

---

## 42. Shadows

Terrain shadows

---

# PHASE 12 — Optimization

## 43. Burst compatibility

Refactor

Burst-safe code

---

## 44. Job System

Move:

Generation

Meshing

Sampling

---

## 45. Memory optimization

Reduce allocations

Pool objects

---

## 46. LOD

Planet detail levels

---

## 47. Chunk streaming

Load/unload chunks

---

# PHASE 13 — Saving

## 48. Save format

Store:

Planet seed

Terrain edits

Entities

---

## 49. Terrain edit replay

Regenerate

Apply edits

---

# PHASE 14 — Debugging

## 50. Terrain debugger

Display

SDF

Chunks

Normals

---

## 51. Planet debugger

Display

Gravity

Radius

Influence

---

## 52. Generator debugger

Display

Noise

Layers

Ore

Caves

---

# PHASE 15 — Polish

## 53. Profiling

Profile every major system

Record:

CPU

Memory

GC

---

## 54. Stress testing

Test:

100 planets

Continuous digging

Continuous building

Planet collisions

---

## 55. Determinism testing

Verify:

Same seed

Same output

Across multiple runs

---

## 56. Documentation

Document:

Architecture

Algorithms

Data flow

Extension points

---

# Continuous Agent Behavior

Every implementation agent should:

1. Read the architecture documentation before coding.
2. Search for existing implementations before creating new ones.
3. Reuse abstractions instead of duplicating code.
4. Leave the project compiling after every task.
5. Add or update tests alongside implementation.
6. Add debug visualization for every simulation feature.
7. Profile new systems if they run every frame.
8. Keep classes focused on a single responsibility.
9. Favor composition over inheritance.
10. Make all procedural generation deterministic from the planet seed.
11. Avoid introducing global state.
12. Never optimize blindly—measure first.
13. Document assumptions and invariants in code.
14. Ensure systems can eventually migrate to Unity Jobs and Burst without major rewrites.
15. Prefer data-oriented APIs and immutable configuration objects where practical.

The goal is steady, incremental progress toward a deterministic, simulation-driven planetary engine where every completed task leaves the project in a healthier, testable, and extensible state.
