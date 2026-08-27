# Plan: Edit-to-Chunk Reverse Index (Edit-Centric Indexing)

## Context

Terrain edits need to know which chunks they affect, so we can:
1. **Prune isolated edits** — remove edits whose affected chunks are all unloaded or have no geometry (the edit created nothing visible).
2. **Bake stable edits** — remove edits whose affected chunks are all fully solid or fully empty (the edit's effect is permanent and redundant to track).
3. **Optimize pruning** — restrict redundant-edit searches to edits in specific regions, instead of scanning all edits globally.

**Current state:** `TerrainField` already has a chunk-centric index (`_editsByChunkKey: Dictionary<long, List<int>>`) that maps chunk-key → edit indices. This is used for fast SDF sampling. We need the **reverse**: edit index → chunk keys.

## Design

Add `_editChunkKeys: Dictionary<int, HashSet<long>>` to `TerrainField`, populated during `IndexEdit`. This is a **parallel collection**, not stored on `TerrainEdit` struct (keeps the struct serializable for save files; chunk keys are derived from bounding box + grid params, not intrinsic edit data).

The existing chunk-centric index is kept — it serves the hot sampling path. The new edit-centric index serves batch operations (pruning, baking, isolation checks).

## Changes

### 1. `TerrainField.cs` — Core Changes

**New field:**
```csharp
private Dictionary<int, HashSet<long>> _editChunkKeys;
```

**New helper methods:**
```csharp
static int ExtractCol(long key) => (int)(key >> 32);
static int ExtractRow(long key) => (int)(key & 0xffffffffL);
```

**`IndexEdit`** — Collect chunk keys into a `HashSet<long>` and store in `_editChunkKeys[editIndex]`. Already iterates the same chunk range — just add the keys to a set instead of discarding them.

**`EnableChunkIndexing`** — Initialize `_editChunkKeys = new Dictionary<int, HashSet<long>>()` alongside `_editsByChunkKey`. Reuse the existing loop that calls `IndexEdit` for all current edits.

**`PruneDeadEdits`** — After compacting `_edits`, rebuild `_editChunkKeys` using the same `oldToNew` mapping already used for `_editsByChunkKey`. Remove entries where `oldToNew[oldIndex] == -1`.

**`PruneRedundantEdits`** — Same pattern as `PruneDeadEdits`. Both already have the remapping infrastructure.

**`ClearEdits`** — Clear `_editChunkKeys` alongside the other structures.

**`LoadEdits`** — Clear and rebuild `_editChunkKeys` alongside the other structures. Already has the pattern.

**New method: `PruneIsolatedEdits`:**
```csharp
/// <summary>
/// Removes edits that are "isolated" — all their affected chunks are considered
/// non-active by the caller's predicate. Use <paramref name="isChunkActive"/> to
/// define what "active" means (e.g., chunk is loaded and has geometry).
/// </summary>
/// <returns>The number of edits removed.</returns>
public int PruneIsolatedEdits(Func<long, bool> isChunkActive)
```
- Iterate all edits. For each, check if ALL keys in `_editChunkKeys[i]` return `false` from the predicate.
- If all chunks are inactive, mark edit for removal.
- Compact using the same pattern as existing prune methods.

### 2. `ChunkTerrainRenderer.cs` — Stability Helper

**New method** to support isolation pruning from the renderer:

```csharp
public int PruneIsolatedEdits()
{
    return _field.PruneIsolatedEdits(key =>
    {
        int col = (int)(key >> 32);
        int row = (int)(key & 0xffffffffL);
        if (!_chunkGrid.HasChunkAtGrid(col, row))
            return false;
        var chunk = _chunkGrid.GetChunkAtGrid(col, row);
        return _chunkViews.ContainsKey(chunk.Index);
    });
}
```

### 3. Tests — `Tests/EditMode/TerrainFieldTests.cs`

Add test cases following the existing patterns:

- **EditChunkKeys_PopulatedOnApplyEdit** — Apply edit with chunk indexing enabled, verify keys match expected chunk grid positions.
- **EditChunkKeys_CapsuleSpansMultipleChunks** — Capsule edit spanning multiple chunks has all their keys.
- **EditChunkKeys_ClearedWithClearEdits** — `ClearEdits()` clears the reverse index.
- **EditChunkKeys_RebuiltWithLoadEdits** — `LoadEdits()` rebuilds the reverse index.
- **PruneDeadEdits_PreservesEditChunkKeys** — After pruning dead edits, `_editChunkKeys` still maps correctly (validate via chunk-indexed sampling).
- **PruneRedundantEdits_PreservesEditChunkKeys** — Same validation after redundant pruning.
- **PruneIsolatedEdits_RemovesEditsWithNoActiveChunks** — Edits whose chunks all fail the predicate are removed.
- **PruneIsolatedEdits_KeepsEditsWithActiveChunks** — Edits with at least one active chunk are kept.
- **PruneIsolatedEdits_SafeOnEmptyField** — No edits, no exceptions.

## Files Modified

| File | Change |
|------|--------|
| `Runtime/Terrain/TerrainField.cs` | Add `_editChunkKeys`, update `IndexEdit`, `EnableChunkIndexing`, `PruneDeadEdits`, `PruneRedundantEdits`, `ClearEdits`, `LoadEdits`. Add `PruneIsolatedEdits`, `ExtractCol`, `ExtractRow`. |
| `Runtime/Terrain/ChunkTerrainRenderer.cs` | Add `PruneIsolatedEdits()` wrapper method. |
| `Tests/EditMode/TerrainFieldTests.cs` | Add ~9 tests for reverse index and isolation pruning. |

## Verification

1. Run existing tests: `TerrainFieldTests` and `TerrainEditShapeTests` — all should pass (no behavior change to sampling or existing pruning).
2. Run new tests — validate reverse index population, cleanup, and isolation pruning.
3. In-game: Apply brush strokes, call `PruneIsolatedEdits()` from debug menu, verify terrain geometry unchanged and edit count decreased appropriately.
