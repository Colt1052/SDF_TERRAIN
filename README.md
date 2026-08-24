# SDF_Terrain — 2D Destructible Planetoid Engine

Simulation-driven terrain system for Unity that represents planets as continuous Signed Distance Fields. Every planet is a circle whose terrain, caves, materials, and geology derive from a deterministic seed.

**Current State:** Phase 1-4 complete (foundation, terrain, editing, materials + geological layers). Geological layers are visible via vertex-color rendering. Next: ore generation (Task 20).

---

## Folder Layout

```
SDF_Terrain/
├── CLAUDE.md               # Agent rules (behavior, standards)
├── README.md               # This file
├── SCOPE.md                # Vision and design principles
├── ARCHITECTURE.md         # System layers, tech decisions, data flow
├── TASKS.md                # Task tracker (1-56 + subtasks)
├── docs/
│   └── task-archive.md     # Detailed notes for completed tasks
│
├── Runtime/
│   ├── SDF_Terrain.Runtime.asmdef
│   ├── Core/               # RadialMath, PlanetCoordinates, SeededRandom
│   ├── Planet/             # Planet, PlanetSettings, PlanetManager
│   ├── Terrain/            # SDF field, chunks, brushes, sampling, rendering
│   ├── Terrain/Brush/      # Brush behavior abstraction (ScriptableObjects)
│   ├── Meshing/            # MarchingSquaresMesher, ColliderContourBuilder, MeshData
│   ├── Materials/          # MaterialDefinition, MaterialDatabase, MaterialSampler
│   └── UI/                 # BrushToolbar, TerrainStats
│
├── Editor/                 # Asset creators, editor extensions
├── Scripts/Debug/          # Debug visualizations
└── Tests/                  # EditMode + PlayMode test assemblies
    └── EditMode/           # ~24 test files
```

---

## System Layers (bottom → top)

```
Core Math (RadialMath, PlanetCoordinates, SeededRandom)
  ↑
Planet / PlanetManager (identity, lifetime, lookup)
  ↑
PlanetGenerator (seeded, deterministic pipeline)
  ↑
TerrainField (SDF — authoritative geometry source)
  ↑
Chunk System (ChunkGrid, TerrainChunk — dirty tracking, indexing)
  ↑
Meshing (MarchingSquaresMesher → MeshData)
  ↑
Collision (ColliderContourBuilder → PolygonCollider2D)
  ↑
Rendering (TerrainRenderer, ChunkTerrainRenderer)
```

Each layer only depends on layers below it.

---

## Key Classes

| Class | File | Role |
|-------|------|------|
| `TerrainField` | `Runtime/Terrain/TerrainField.cs` | The SDF. Authoritative geometry. Sample/modify edits. |
| `TerrainEdit` | `Runtime/Terrain/TerrainEdit.cs` | One brush stroke: center, radius, additive/dig. |
| `TerrainChunk` | `Runtime/Terrain/TerrainChunk.cs` | Square region of the grid with bbox and dirty flag. |
| `ChunkGrid` | `Runtime/Terrain/ChunkGrid.cs` | 2D grid of chunks, indexing, dirty tracking. |
| `ChunkTerrainRenderer` | `Runtime/Terrain/ChunkTerrainRenderer.cs` | Per-chunk mesh/collider GameObject management. |
| `TerrainRenderer` | `Runtime/Terrain/TerrainRenderer.cs` | Whole-planet single-mesh renderer. |
| `CartesianChunkFieldSampler` | `Runtime/Terrain/CartesianChunkFieldSampler.cs` | Sample one chunk's bbox onto a lattice grid. |
| `MarchingSquaresMesher` | `Runtime/Meshing/MarchingSquaresMesher.cs` | Convert sample grid to MeshData (vertices/tris). |
| `ColliderContourBuilder` | `Runtime/Meshing/ColliderContourBuilder.cs` | Extract closed boundary loops from MeshData. |
| `TerrainBrush` | `Runtime/Terrain/TerrainBrush.cs` | Immutable struct: mode (Add/Remove/Smooth), radius. |
| `TerrainNoise` | `Runtime/Terrain/TerrainNoise.cs` | Sine-harmonic noise for terrain surface variation. |
| `PlanetGenerator` | `Runtime/Terrain/PlanetGenerator.cs` | Entry point: radius + seed + noise → TerrainField. |
| `Planet` | `Runtime/Planet/Planet.cs` | MonoBehaviour: radius, seed, gravity, settings ref. |
| `PlanetManager` | `Runtime/Planet/PlanetManager.cs` | Registration, lookup, multi-planet management. |
| `MaterialDatabase` | `Runtime/Materials/MaterialDatabase.cs` | Registry of MaterialDefinition ScriptableObjects. |
| `MaterialSampler` | `Runtime/Materials/MaterialSampler.cs` | Assign materials to SDF positions based on bands. |
| `BrushController` | `Runtime/Terrain/Brush/BrushController.cs` | Brush state, parameter management, events. |
| `BrushDefinition` | `Runtime/Terrain/Brush/BrushDefinition.cs` | ScriptableObject: name, icon, behavior, parameters. |
| `BrushBehavior` | `Runtime/Terrain/Brush/BrushBehavior.cs` | Abstract base for brush logic (Add/Remove/Smooth). |

---

## Key Design Decisions

- **Square Cartesian chunks** on a grid centered on planet origin. No wedge masks, no seam logic.
- **Brush edits are pure SDF cones**: `Radius - distanceFromBrush`, no strength parameter.
- **CSG composition**: digs use `Mathf.Max`, builds use `Mathf.Min` — idempotent, no melting.
- **Chunk-indexed spatial index**: each edit mapped to overlapping chunks at apply time. Sampling scans only local edits.
- **Packed `(col, row)` keys** for edit indexing — edits registered even when no chunk exists.
- **Empty chunks auto-removed**: collect indices during rebuild, remove after to avoid enumerator invalidation.
- **Marching Squares asymptotic decider**: bilinear center estimate resolves ambiguous saddle cases continuously.

---

## Data Flow

**Generation:**
```
Seed + Radius + NoiseSettings
  → PlanetGenerator.GenerateBaseShape()
    → TerrainField (sphere base + noise perturbation)
      → ChunkTerrainRenderer.RebuildDirtyChunks()
        → CartesianChunkFieldSampler → MarchingSquaresMesher → MeshData
          → MeshDataConverter.ToUnityMesh → MeshFilter.sharedMesh
          → ColliderContourBuilder → PolygonCollider2D
```

**Editing:**
```
Brush stroke → TerrainField.ApplyEdit(TerrainEdit)
  → TerrainField.IndexEdit (map to overlapping chunk grid cells)
  → ChunkTerrainRenderer.MarkDirtyRect
    → RebuildDirtyChunks
      → Only dirty chunks sample + mesh + collider
      → Empty chunks removed
```

---

## Testing

24+ EditMode test files, ~200+ test methods. Covers: math, coordinates, SDF sampling, meshing edge cases, chunk indexing, dirty propagation, brush behavior, collider contours, material database. Run via Unity Test Runner (EditMode).

---

## Unity Environment

Unity 6 (6000.0.45f1), URP 2D, Input System, Test Framework installed.
