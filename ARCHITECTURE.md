# SDF_Terrain — Architecture Document

**Version:** 2.0
**Status:** Phase 1-3 implemented, Phase 4 in progress

---

## 1. Current State

Phase 1 (Foundation), Phase 2 (Terrain), and Phase 3 (Terrain Editing) are implemented and tested. Phase 4 (Materials) has database + sampling; geological layers are next. See `TASKS.md` for the full list and `README.md` for the class directory.

Unity 6 (6000.0.45f1), URP 2D, Input System, Test Framework installed.

---

## 2. System Layers

```
┌─────────────────────────────────────────────────┐
│   Brush UI / Input (BrushUI, BrushInputHandler)  │
├─────────────────────────────────────────────────┤
│   Rendering (Mesh, Materials, Gizmos)            │
├─────────────────────────────────────────────────┤
│   Collision (PolygonCollider2D)                  │
├─────────────────────────────────────────────────┤
│   Meshing (Marching Squares → MeshData)          │
├─────────────────────────────────────────────────┤
│   Chunk System (dirty tracking, indexing)        │
├─────────────────────────────────────────────────┤
│   Terrain SDF (authoritative data)               │
├─────────────────────────────────────────────────┤
│   Planet Generator (seeded, deterministic)       │
├─────────────────────────────────────────────────┤
│   Planet / PlanetManager (identity, lifetime)    │
├─────────────────────────────────────────────────┤
│   Core Math (coordinates, radial vectors, RNG)   │
└─────────────────────────────────────────────────┘
```

Each layer only depends on layers below it. Only brush/edit operations mutate the SDF.

---

## 3. Component Map

### Core (`Runtime/Core/`)
- **RadialMath** — angle wrap, angle-of, position-at, surface normals.
- **PlanetCoordinates** — world/local/radial conversions, gravity direction.
- **SeededRandom** — xorshift32 PRNG. Only permitted randomness source.

### Planet (`Runtime/Planet/`)
- **Planet** — thin MonoBehaviour: radius, seed, gravity, settings ref. Auto-registers with PlanetManager.
- **PlanetSettings** — ScriptableObject config.
- **PlanetManager** — static singleton. Registration, lookup by position, multi-planet list.

### Terrain (`Runtime/Terrain/`)
- **TerrainField** — the SDF. Sphere base + brush edits. CSG composition. Chunk-indexed sampling.
- **TerrainEdit** — one brush stroke: position, radius, additive flag. Pure SDF cone.
- **TerrainChunk** — square grid cell with bbox (col/row) and dirty flag.
- **ChunkGrid** — 2D grid of chunks, centered on origin. Position/coordinate lookup, dirty tracking.
- **CartesianChunkFieldSampler** — sample one chunk's bbox onto a lattice grid.
- **TerrainRenderer** — whole-planet single-mesh renderer.
- **ChunkTerrainRenderer** — per-chunk GameObject management, dirty-only rebuilds.
- **TerrainBrush** — immutable struct: mode (Add/Remove/Smooth), radius.
- **TerrainNoise** — seamless sine-harmonic noise. Configured by `TerrainNoiseSettings`.
- **PlanetGenerator** — `GenerateBaseShape(radius, seed, noiseSettings)` → `TerrainField`.

### Brush (`Runtime/Terrain/Brush/`)
- **BrushBehavior** — abstract ScriptableObject base for brush logic.
- **BrushDefinition** — ScriptableObject: name, icon, behavior, parameter descriptors.
- **BrushParameterDescriptor** — ScriptableObject: float parameter config (name, range, step).
- **BrushController** — MonoBehaviour: active brush, parameters, events.
- **StandardBrushBehavior** — Add/Remove implementation.
- **SmoothBrushBehavior** — Smoothing implementation.

### Meshing (`Runtime/Meshing/`)
- **MarchingSquaresMesher** — sample grid → `MeshData`. Pure function. Asymptotic decider for saddles.
- **MeshData** — vertex/triangle/normal/UV lists (no UnityEngine.Mesh dependency).
- **MeshDataConverter** — `MeshData` → `UnityEngine.Mesh` (with reuse).
- **ColliderContourBuilder** — extract closed boundary loops from `MeshData`.
- **TerrainColliderBuilder** — apply contours to `PolygonCollider2D`.

### Materials (`Runtime/Materials/`)
- **MaterialDefinition** — ScriptableObject: 9 physical properties (density, hardness, friction, etc.).
- **MaterialDatabase** — static registry singleton, loads from Resources/Materials/.
- **MaterialSampler** — assign materials to SDF positions based on band configuration.
- **MaterialBand** — depth/position-based material assignment rule.

### UI (`Runtime/UI/` + `Runtime/Terrain/`)
- **BrushUI** — dynamic canvas: style selector buttons + parameter sliders.
- **BrushInputHandler** — screen→world→planet-local input adapter.
- **BrushToolbar** — toolbar-style brush selector.
- **TerrainStats** — runtime statistics display.
- **SDFDebugView** / **SDFDebugTexture** — SDF visualization.

---

## 4. Data Flow

### Generation
```
Seed + Radius + NoiseSettings
  → PlanetGenerator.GenerateBaseShape()
    → TerrainField (sphere + noise)
      → ChunkTerrainRenderer.RebuildDirtyChunks()
        → CartesianChunkFieldSampler → MarchingSquaresMesher → MeshData
          → MeshDataConverter → MeshFilter.sharedMesh
          → ColliderContourBuilder → PolygonCollider2D
```

### Editing
```
Brush stroke → TerrainField.ApplyEdit(TerrainEdit)
  → TerrainField.IndexEdit (packed col/row key)
  → ChunkTerrainRenderer.MarkDirtyRect
    → RebuildDirtyChunks (only dirty chunks)
      → Empty chunks removed, new chunks created (Add mode only)
```

### Chunk Seam Strategy
Square Cartesian chunks share boundary lattice points that sample the same field at the same position. Identical values → identical Marching Squares output at every seam. No seam logic needed.

### Edit Sampling
`EnableChunkIndexing()` maps each edit to overlapping grid cells (packed col/row key). `Sample(position, chunkIndex)` scans only local edits — O(chunk_local_edits), not O(total_lifetime_edits). `PruneDeadEdits()` reclaims zero-radius entries. CSG Max/Min commutativity guarantees excluding distant edits produces identical results.

---

## 5. Coordinate System

- **World space** — Unity world coordinates. Planet center = `Transform.position`.
- **Planet-local space** — `worldPos - planetCenter`. Used for all terrain operations.
- **Radial space** — `(angle, radius)` around planet-local origin. Used for noise and surface sampling.
- Gravity direction: `-normalize(worldPos - planetCenter)`. No global "up".

---

## 6. Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Terrain | Continuous SDF (CPU) | Smooth deformation, marching squares |
| Chunks | Square Cartesian grid | Simple indexing, 4 neighbors, no seams |
| Meshing | Marching Squares (2D) | Standard, testable, well-understood |
| Randomness | SeededRandom (xorshift32) | Determinism — never `UnityEngine.Random` |
| Config | ScriptableObjects | Data-driven, no scattered constants |
| Persistence | Seed + edits only | Regenerate everything else |
| Collision | `PolygonCollider2D` from mesh data | 2D project, derives from SDF |
| Brush edits | Pure SDF cone (radius only) | Exact circles, idempotent CSG |
| Edit indexing | Packed (col,row) → List<int> | Edits indexed even when no chunk exists |
| Empty chunks | Auto-removed | No wasted GameObjects/meshes |

---

## 7. Key Assumptions

1. **Unity 6 (6000.0.45f1)**, URP 2D.
2. **2D only** — planets are circles. "Radial" = 2D polar coordinates.
3. **Single assembly** — `SDF_Terrain.Runtime` asmdef + `SDF_Terrain.Tests` asmdef.
4. **CPU-first** — plain C#, designed for later Jobs/Burst migration.
5. **No stored geometry** — everything regenerates from seed + edits.

---

## 8. Risks

| Risk | Status | Mitigation |
|------|--------|------------|
| Seam gaps at chunk boundaries | **Resolved** (15.10) | Square chunks share lattice points |
| Brush melting / non-idempotent edits | **Resolved** (15.5) | CSG Max/Min composition |
| Performance degradation over time | **Resolved** (15.11) | Chunk-indexed sampling + pruning |
| Determinism regressions | Active risk | Code review; SeededRandom is only source |
| Overlap with EuroAtmoClaude module | Monitoring | Reuse `PlanetCoordinates`/radial math where possible |
