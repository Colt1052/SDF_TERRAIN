# RESOURCETASKS.md — Material & Resource System Implementation

---

## Completed Implementation

### 1. Material Data Model ✅

**Files:** `Runtime/Materials/MaterialId.cs`, `Runtime/Materials/MaterialSample.cs`, `Runtime/Materials/MaterialEdit.cs`

- `MaterialId` — compact numeric struct (Air = 0, Unknown = -1). Internal constructor prevents ad-hoc creation.
- `MaterialSample` — readonly struct with MaterialId, Concentration (clamped 0–1), IsSolid.
- `MaterialEdit` — serializable struct: Vector2 LocalPosition, float Radius, MaterialId, int Order. Implements `Contains(Vector2)` and `SampleDistance(Vector2)`.
- Materials tracked: Air (built-in), Dirt, Stone, Sand, Iron Ore, Copper Ore, Concrete (via ScriptableObject assets).

### 2. MaterialDatabase & Registry ✅

**File:** `Runtime/Materials/MaterialDatabase.cs`

- Static singleton with O(1) lookup by string ID or numeric MaterialId.
- `Initialize()` loads from `Resources/Materials/`. `AddMaterial()` for tests/runtime registration.
- Air always MaterialId 0. Subsequent materials receive incrementing IDs.
- `GetMaterialId(string)` → MaterialId. `GetName(MaterialId)` → human-readable string.

### 3. MaterialLayer (Authoritative State) ✅

**File:** `Runtime/Materials/MaterialLayer.cs`

- Owns the material edit history alongside a TerrainField. Not merely a rendering layer.
- `Sample(TerrainField, Vector2, int chunkIndex)` → MaterialSample. Returns Air if SDF > 0, otherwise scans edits last-first, falls back to geological profile.
- `EnableChunkIndexing(ChunkGrid)` — spatially indexes edits by (col, row) so sampling is O(local_edits) not O(total_history).
- `ApplyEdit(Vector2, float, MaterialId)` — adds material override circular region.
- `LoadEdits(IEnumerable<MaterialEdit>)` — restores from serialization.
- `PruneEditsOutsideTerrain(TerrainField)` — removes orphaned edits after terrain is mined away.

### 4. Geological Material Generation ✅

**Files:** `Runtime/Materials/GeologicalLayerGenerator.cs`, `Runtime/Materials/GeologicalProfile.cs`, `Runtime/Materials/GeologicalLayerNoise.cs`

- Pure-function utility: given (world position, terrain seed, geological profile) → GeologicalSampleResult.
- Deterministic sine-harmonic noise perturbs layer boundaries. Temperature/pressure computed from profile gradients.
- `MaterialLayer.Sample()` falls back to this when no edit overrides.

### 5. MaterialVolumeResult ✅

**File:** `Runtime/Materials/MaterialVolumeResult.cs`

- Maps MaterialId → removed volume (area in 2D). Independent of inventory.
- `Add(MaterialId, float)`, `GetVolume(MaterialId)`, `HasMaterial(MaterialId)`, `ForEach(Action)`.

### 6. ExcavationCalculator ✅

**File:** `Runtime/Resources/ExcavationCalculator.cs`

- `CalculateRemoval(MaterialLayer, TerrainField, center, radius, chunkIndex, sampleResolution)` — grid-based sampling within the circular region. Only counts solid samples. Returns MaterialVolumeResult.
- `CalculateAddition(center, radius, MaterialId)` — entire area is that material.

### 7. Inventory & ResourceYield ✅

**Files:** `Runtime/Resources/Inventory.cs`, `Runtime/Resources/ResourceYieldDefinition.cs`

- `Inventory` — string-keyed slots with quantities and stack limits. `Add`, `Remove`, `GetQuantity`, `HasAtLeast`, `ForEach`.
- `ResourceYieldDefinition` — maps MaterialId → ResourceId → YieldPerUnitArea. `ComputeQuantity(float area)` → int.
- `ResourceYieldTable` — collection of yield rules. `Convert(MaterialVolumeResult)` → Dictionary<string, int> (resourceId → quantity). `Default(MaterialDatabase)` creates 100 items/unit-area for each material.

### 8. TerrainExcavationSystem (Pipeline Orchestration) ✅

**File:** `Runtime/Resources/TerrainExcavationSystem.cs`

- Excavation pipeline: `Excavate(localPosition, radius, chunkIndex)` → material volumes → resources → inventory → terrain removal edit.
- Placement pipeline: `Place(localPosition, radius, materialId, resourceId)` → checks inventory → consumes resources → SDF addition + material edit. Atomic failure on insufficient resources.
- Returns `ExcavationResult` and `PlacementResult` structs with outcome details.
- SampleResolution is configurable (default 8).

### 9. MaterialColorMap ✅

**File:** `Runtime/Materials/MaterialColorMap.cs`

- Maps MaterialId → Color for vertex-color rendering. Checks MaterialDatabase first, falls back to built-in palette.
- `GetColor(string, MaterialDatabase)` and `GetColor(MaterialId, MaterialDatabase)` overloads.
- Deterministic HSV color from numeric ID if no definition exists.

### 10. ChunkTerrainRenderer Integration ✅

**File:** `Runtime/Terrain/ChunkTerrainRenderer.cs`

- When `MaterialLayer` is assigned, vertex colors come from `MaterialLayer.Sample()` during rebuild. Takes precedence over `GeologicalProfile`.
- Each vertex colored via `MaterialColorMap.GetColor(sample.MaterialId, database)`.

### 11. MaterialDebugView ✅

**File:** `Runtime/Terrain/MaterialDebugView.cs`

- MonoBehaviour debug visualization with 4 modes:
  - **MaterialId** — each material colored via MaterialColorMap.
  - **MaterialIdRaw** — deterministic HSV color from numeric ID.
  - **EditsOnly** — shows only positions covered by player material edits (natural geology = black, air = white).
  - **MaterialBoundaries** — highlights material transitions (adjacent samples with different MaterialIds) in yellow.

### 12. Serialization Persistence ✅

**File:** `Runtime/Resources/WorldSaveData.cs`

- `SerializableTerrainEdit`, `SerializableMaterialEdit`, `SerializableInventorySlot` — JsonUtility-compatible wrappers.
- `WorldSaveData.Capture(TerrainField, MaterialLayer, Inventory, seed)` — snapshots all state.
- `ToJson()` / `FromJson(string)` — JSON round-trip.
- `Apply(TerrainField, MaterialLayer, Inventory)` — restores state to fresh systems.
- Verified: chunk loading/unloading produces identical material results.

### 13. Integration Tests ✅

**File:** `Tests/EditMode/MaterialSystemTests.cs`

- **Data model:** MaterialId equality/ToString, MaterialSample construction/clamping, MaterialEdit Contains/SampleDistance.
- **MaterialLayer:** air detection, geological fallback, edit override, last-edit-wins, LoadEdits, ClearEdits.
- **MaterialVolumeResult:** accumulation, querying, iteration, clear.
- **ExcavationCalculator:** single-material addition, invalid ID handling.
- **Inventory:** add/remove round-trip, excess removal, HasAtLeast, ToDictionary.
- **ResourceYieldTable:** conversion, unknown material skip, default creation.
- **Conservation:** mine → place → mine without duplication, multiple materials.
- **Pipeline integration:** excavate produces volumes + resources, outside-terrain no-op, place consumes resources + adds terrain, insufficient resources fails, mine/place/mine conservation end-to-end, mixed-material excavation exposes underlying geology, clear edits restores natural geology.
- **Serialization:** terrain edit round-trip, material edit round-trip, full WorldSaveData capture/apply for terrain + material + inventory, empty state round-trip, full pipeline conservation after save/load.

---

## Remaining Work

### 14. Performance Optimization (In Progress)

**Done:**
- `GeologicalLayerNoise.Sample()` — precomputes octave parameters (phase, harmonic, amplitude) once per (seed, frequency, octaves) triplet. Subsequent calls are allocation-free and branch-light. Amplitude normalization is baked as a closed-form formula (`2 - 0.5^n`) instead of accumulated at runtime. Eliminates 4 `SeededRandom` allocations per sample (12 per vertex in a 3-octave profile).

**Not Started:**
- Profile `SampleMaterial()` under load (many edits, large chunks).
- Profile excavation material integration with high sampleResolution.
- Cache procedural geological material where beneficial.
- Only store explicit material overrides where the player has modified terrain.


### 15. Persistence Integration ✅

**File:** `Runtime/Resources/WorldPersistence.cs`

- `WorldPersistence` — MonoBehaviour that wires `WorldSaveData` to disk via `Application.persistentDataPath`.
- Slot-based saves (up to 5 slots). Slot metadata stored in `PlayerPrefs` (name, seed, timestamp).
- Save files written as JSON to `persistentDataPath/SDFPlanetSaves/slot_N.json`.
- Auto-save on `Application.quitting` (configurable slot). Auto-load on start (configurable slot).
- `Save(slot)` and `Load(slot)` — full round-trip including renderer reinitialization and excavation system rewiring.
- `DeleteSlot(slot)` — removes file and PlayerPrefs metadata.
- `SlotMetadata` struct — lightweight per-slot info for UI save menus.
- `TerrainExcavationSystem` extended with `GetMaterialLayer()`, `GetInventory()`, `Rewire()` for persistence integration.
- Persistence tests added to `MaterialSystemTests.cs`.

### 16. Documentation Update ✅

- ARCHITECTURE.md v3.0: updated system layer diagram, subsystem ownership section, expanded component map with all material/resource/persistence classes, added material sampling / excavation / placement / persistence data flow diagrams.
- README.md: updated current state, folder layout, key classes table with new entries.

---

## Definition of Done (End-to-End Scenario)

The system passes this flow:

```
Generate planet
  → Natural terrain contains Stone + Iron Ore (via geological profile)
  → Player mines through both (ExcavationSystem.Excavate)
  → Inventory receives: Stone: X, IronOre: Y (via ResourceYieldTable)
  → Player builds a Stone structure (ExcavationSystem.Place)
  → Stone inventory decreases
  → Structure appears as Stone (MaterialLayer edit + vertex colors)
  → Player mines the structure (ExcavationSystem.Excavate)
  → Stone inventory increases again
  → Player reloads the chunk (WorldSaveData.Apply)
  → Structure/material state is unchanged
  → Player removes structure
  → Natural terrain underneath is restored/exposed (geological fallback)
  → No resources were duplicated (conservation tests verify)
```

**Status:** All core systems implemented, tested, and documented. Persistence wired. Only performance optimization (Task 14) remains.
