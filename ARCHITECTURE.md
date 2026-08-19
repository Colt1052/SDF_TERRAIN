# SDF_Terrain — Architecture Document

**Version:** 1.0
**Status:** Initial — pre-implementation

---

## 1. Current Project State

A fresh directory exists containing only specification documents (`CLAUDE.md`, `SCOPE.md`, `TASKS.md`) and this architecture file. No assembly definitions, no runtime code, no tests. This lives inside the `EulerAtmo` Unity 6 (6000.0.45f1) URP 2D project, alongside a sibling module `Assets/EuroAtmoClaude` (GPU compute-shader atmosphere simulation) that establishes this project's documentation and directory conventions.

The Unity project already has 2D packages (`com.unity.feature.2d`), the Input System, URP, and the Test Framework installed — no new package dependencies are required for Phase 1–2 work.

---

## 2. System Architecture

### 2.1 Layered Model

```
┌─────────────────────────────────────────────────┐
│         Rendering (Mesh, Materials)              │  ← Consumes generated mesh/collider only
├─────────────────────────────────────────────────┤
│         Collision (PolygonCollider2D)            │  ← Derived, disposable
├─────────────────────────────────────────────────┤
│         Meshing (Marching Squares)               │  ← Derived, disposable
├─────────────────────────────────────────────────┤
│         Chunk System (dirty tracking, indexing)  │
├─────────────────────────────────────────────────┤
│         Terrain SDF (authoritative data)         │  ← Source of truth
├─────────────────────────────────────────────────┤
│         Planet Generator (seeded, deterministic) │
├─────────────────────────────────────────────────┤
│         Planet / PlanetManager (identity, lifetime)│
├─────────────────────────────────────────────────┤
│         Core Math (coordinates, radial vectors)  │
└─────────────────────────────────────────────────┘
```

Each layer only depends on layers below it. Rendering and collision never write back into the SDF; only brush/editing operations (Phase 3) mutate it.

### 2.2 Component Breakdown (planned)

#### Core (`Runtime/Core/`)
- **PlanetCoordinates** — conversion between world space, planet-local space, and radial (angle, radius) space.
- **RadialMath** — utility functions for radial vectors, surface normals, angular wrapping.
- **SeededRandom** — deterministic RNG wrapper seeded from planet DNA; the only permitted source of randomness (never `UnityEngine.Random` directly in generation code).

#### Planet (`Runtime/Planet/`)
- **Planet** — MonoBehaviour/data component. Holds radius, seed, gravity strength, and a reference to its `PlanetSettings` (ScriptableObject). Thin — no generation or rendering logic.
- **PlanetSettings** — ScriptableObject: radius range, density, seed override, archetype parameters.
- **PlanetManager** — Registration, lookup by ID/position, spawn/despawn, update ordering across multiple planets. Plain C# class (or a thin MonoBehaviour singleton) — no per-planet simulation logic lives here.

#### Terrain (`Runtime/Terrain/`) — Phase 2+
- **TerrainField** — the SDF data structure: sampling, modification, serialization interface. Authoritative geometry source.
- **TerrainChunk** — owns a rectangular region of density samples, its generated mesh, collider, and dirty flag.
- **ChunkGrid** — chunk creation, 2D grid indexing (col/row), 4-directional neighbor lookup, dirty propagation. Chunks are square cells on a grid centered on the planet's origin (`_gridMin = -(count * chunkSize) / 2`), covering the planet's bounding box symmetrically in all quadrants.
- **PlanetGenerator** — deterministic generation pipeline (sphere → noise → layers → caves → ore → materials), driven entirely by `Planet.Seed`.

#### Meshing / Collision (`Runtime/Meshing/`) — Phase 2+
- **MarchingSquaresMesher** — converts a chunk's density samples into a mesh (vertices, normals, UVs). Pure function of input samples — no side effects, fully testable in isolation.
- **ColliderGenerator** — builds `PolygonCollider2D` paths from the same chunk data used for meshing.

#### Debug (`Scripts/Debug/`)
- Visualizations for chunk borders, density field, normals — added per-system as each system lands (per TASKS.md "Debug Visualization" requirement), not built as one monolithic tool.

#### Tests (`Tests/EditMode/`, `Tests/PlayMode/`)
- EditMode unit tests for math, coordinates, SDF sampling, meshing edge cases.
- PlayMode/integration tests for chunk dirty propagation and multi-planet manager behavior.

---

## 3. Data Flow

### 3.1 Planet Generation Order (per SCOPE.md, must not be reordered)

```
Planet DNA (seed)
  → Large-scale shape (sphere)
  → Terrain height (noise)
  → Geological layers
  → Caves
  → Ore
  → Materials
  → Vegetation
  → Entities
```

### 3.2 Terrain Edit Flow (Phase 3+)

```
Brush input → TerrainField.ApplyEdit (persists TerrainEdit)
            → edit indexed into chunks whose bbox overlaps brush (EnableChunkIndexing)
            → mark affected chunks dirty
            → dirty chunks sample field via chunk-indexed Sample(position, chunkIndex)
              (only scans edits registered to that chunk — O(local edits), not O(total))
            → dirty chunks re-mesh (Marching Squares)
            → dirty chunks regenerate collider
            → renderer picks up updated mesh
```

No step here ever touches a mesh or collider directly; edits only ever target the SDF.

`PruneDeadEdits()` compacts zero-radius edits and remaps chunk indices atomically,
called periodically to prevent unbounded list growth.

### 3.3 Chunk Seam Strategy

Chunks are square cells on a regular Cartesian grid. Each chunk's lattice
covers exactly the chunk's bounding box. Because every chunk samples the
*same* global Cartesian lattice, adjacent chunks share boundary lattice points
with identical terrain values — Marching Squares produces contiguous mesh
edges at every seam. No seam cache, wedge masks, or margin logic is needed.
The SDF is sampled freely within each chunk's bounding box; lattice points
outside the planet's surface read as air (positive SDF), so Marching Squares
produces no contour in all-air regions.

---

## 4. Coordinate System

- **World space** — standard Unity world coordinates; a planet's `Transform.position` is its center.
- **Planet-local space** — world position minus planet center, used for all radial math so generation is independent of where the planet sits in the world.
- **Radial space** — `(angle, radius)` around planet-local origin; used for surface sampling and generation since planets are always circular/spherical in this 2D sandbox (per SCOPE.md "Planet-Centric Design").
- Gravity direction is always `-normalize(worldPos - planetCenter)`; there is no global "up".

---

## 5. Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Terrain representation | Continuous SDF (CPU-side arrays initially) | SCOPE.md mandates continuous, non-grid/voxel terrain; SDF supports smooth deformation and marching-squares meshing |
| Chunking shape | Square Cartesian grid covering planet bounding box | Simple position-based indexing, exactly 4 neighbors, no wedge masks, no seam cache. Chunks partially outside the planet render empty (all-air → no mesh). |
| Meshing algorithm | Marching Squares (2D) | Explicitly named in TASKS.md #9; standard, testable, well-understood |
| Randomness | Seeded RNG only, sourced from planet DNA | CLAUDE.md determinism requirement; never `UnityEngine.Random` in generation |
| Configuration | ScriptableObjects (`PlanetSettings`) | CLAUDE.md data-driven requirement; avoids scattered constants |
| Planet component split | `Planet` (data) / `PlanetGenerator` / `PlanetManager` separate classes | CLAUDE.md single-responsibility + composition-over-inheritance rules |
| Persistence | Store seed + edits only; regenerate the rest | SCOPE.md "Saving" section — never serialize reconstructable terrain |
| Collision | `PolygonCollider2D` derived from mesh/chunk data | 2D project; matches TASKS.md #10 |
| Assembly structure | Dedicated `.asmdef` for Runtime code, separate for Tests | CLAUDE.md Task 1.1 ("Create assembly definitions"); keeps compile times low and enforces the layering above |
| Seam handling | No seam logic needed: shared lattice points | Adjacent square chunks sample the same global Cartesian lattice. Shared boundary lattice points produce identical terrain values by construction (same field, same position), so Marching Squares generates contiguous mesh edges automatically. |
| Edit sampling | Chunk-indexed spatial index | `EnableChunkIndexing()` maps each edit to chunks whose bounding box overlaps the brush footprint. `CartesianChunkFieldSampler` calls `Sample(position, chunkIndex)` to scan only local edits — O(chunk_local_edits) instead of O(total_lifetime_edits). `PruneDeadEdits()` reclaims zero-radius entries. CSG Max/Min commutativity guarantees excluding distant edits produces identical results. |

---

## 6. Public API Surface (planned, Phase 1 scope)

- `Planet` — `Radius`, `Seed`, `GravityStrength`, `Settings` (read-only accessors; construction/validation via constructor or `Initialize`).
- `PlanetManager` — `Register(Planet)`, `Unregister(Planet)`, `GetPlanetAt(Vector2 worldPos)`, `AllPlanets` (read-only enumerable).
- `PlanetCoordinates` — `WorldToLocal`, `LocalToRadial`, `RadialToLocal`, `SurfaceNormal(angle)`.

Terrain/meshing/collision APIs are deferred to Phase 2 design and not finalized here.

---

## 7. Key Assumptions

1. **Unity Version** — 6000.0.45f1 (Unity 6), URP 2D, as found in `ProjectSettings/ProjectVersion.txt`.
2. **2D only** — planets are circles, not spheres; "radial" means 2D polar coordinates throughout.
3. **No git repository detected** in this working directory context — version control status should be confirmed with the user before work that benefits from commits/branches.
4. **Single assembly initially** — one `SDF_Terrain.Runtime` asmdef plus one `SDF_Terrain.Tests` asmdef is sufficient for Phase 1; further splitting (e.g. per-module asmdefs) is deferred until a concrete compile-time or dependency-boundary problem appears.
5. **CPU-first terrain** — the SDF and meshing start as plain C# (NativeArray-ready but not Burst/Jobs yet), per CLAUDE.md "Future Compatibility" (design for later migration, don't front-load it).
6. **No existing code to reuse** — confirmed empty directory; Phase 1 Task 1 (project structure) is genuinely the first actionable task.

---

## 8. Directory Structure (Planned)

```
Assets/SDF_Terrain/
├── CLAUDE.md
├── SCOPE.md
├── TASKS.md
├── ARCHITECTURE.md
│
├── Runtime/
│   ├── SDF_Terrain.Runtime.asmdef
│   ├── Core/
│   │   ├── PlanetCoordinates.cs
│   │   ├── RadialMath.cs
│   │   └── SeededRandom.cs
│   ├── Planet/
│   │   ├── Planet.cs
│   │   ├── PlanetSettings.cs
│   │   └── PlanetManager.cs
│   ├── Terrain/          # Phase 2+
│   └── Meshing/          # Phase 2+
│
├── Scripts/
│   └── Debug/            # per-system debug visualizations, added incrementally
│
└── Tests/
    ├── SDF_Terrain.Tests.asmdef
    ├── EditMode/
    │   ├── PlanetCoordinatesTests.cs
    │   ├── PlanetTests.cs
    │   └── PlanetManagerTests.cs
    └── PlayMode/
```

---

## 9. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| No git repository | No version control, work loss risk | Confirm with user before beginning implementation; recommend init if absent |
| Chunk shape (angular vs. Cartesian) not yet validated against meshing algorithm | Rework needed once Marching Squares (Task 9) lands | Keep chunk indexing behind an interface; defer final shape decision to Task 6 design step, informed by Task 9 |
| Visible seam gaps at chunk boundaries | Mesh contour vertices placed at different positions on shared cell edges | **Resolved** (Task 15.10): Square Cartesian chunks share boundary lattice points that sample the same field at the same position. Identical terrain values guarantee identical Marching Squares output at every seam — no runtime seam logic needed. |
| Determinism regressions from incidental `UnityEngine.Random` use | Non-reproducible planets | Enforce via code review / analyzer; seeded RNG is the only entry point exposed to generation code |
| Overlap with sibling `EuroAtmoClaude` module (atmosphere) | Duplicate coordinate/math utilities | `PlanetCoordinates`/radial math here should be reused by atmosphere module later rather than reimplemented; flag if duplication appears |
| Task 1 (assembly definitions, analyzers) has no prior art in this folder | Ambiguity on analyzer ruleset | Mirror whatever ruleset (if any) `EuroAtmoClaude` or project root uses; otherwise use Unity defaults and note the gap |

---

## 10. Dependencies

No new package dependencies required for Phase 1–2. Uses only:

- Unity 2D packages (already installed)
- Unity Test Framework (already installed)
- Unity ScriptableObjects, MonoBehaviours (built-in)
- `PolygonCollider2D` / Physics2D module (already installed, needed from Task 10 onward)
