Based on my analysis of the SDF Terrain codebase, here's a comprehensive breakdown of the current architecture and optimization opportunities for brush lag:

---

Current Architecture Summary

Edit-to-Chunk Relationship Flow

Brush Stroke → TerrainField.ApplyEdit()
  → RemoveContainedEdits()  (O(n) scan of all edits)
  → _edits.Add(edit)
  → IndexEdit(editIndex, edit)  (spatial indexing via bounding box)
    → _editsByChunkKey[key].Add(editIndex)  (forward index)
    → _editChunkKeys[editIndex] = keys     (reverse index)
  → TryBakeCoveredChunks()  (incremental baking check)

Sample Time:
  → TerrainField.Sample(position, chunkIndex)
    → Lookup _editsByChunkKey for chunk's packed key
    → Scan ONLY edits registered to that chunk

Key Insight: The spatial index (_editsByChunkKey) is the primary mechanism fmpling to O(chunk_local_edits). This is working correctly.

---

Identified Performance Bottlenecks

1. RemoveContainedEdits() — O(n²) Containment Checks

File: TerrainField.cs:336-537

Every brush stroke iterates through all existing edits to find contained ones. With hundreds of strokes, this becomes O(n²).

// Line 440-473: Scans ALL edits on EVERY brush stroke
for (int i = 0; i < count; i++)
{
    TerrainEdit existing = _edits[i];
    // Same shape, same sign, same clamped, radius check, bbox containment...
}

Impact: Heavy dragging → quadratic edit list scans → visible lag.

2. IndexEdit() — Overly Conservative Chunk Expansion

File: TerrainField.cs:551-603

// Line 563-569: Rectangles get Radius expansion for indexing
if (edit.Shape == BrushShape.Rectangle)
{
    brushMinX -= edit.Radius;  // <-- EXPENSIVE: inflates footprint
    brushMaxX += edit.Radius;
    brushMinY -= edit.Radius;
    brushMaxY += edit.Radius;
}

This causes Rectangle edits to be indexed against more chunks than necessary, leading to:
- Larger per-chunk edit lists
- More edits scanned during sampling
- Increased memory pressure on _editsByChunkKey

3. TryBakeCoveredChunks() — Per-Edit Baking Overhead

File: TerrainField.cs:318-390

Runs after every edit application. Samples the chunk with CartesianChunkFieldSampler (full lattice sampling), checks uniformity, and checks coverage. This is expensive for rapid brush strokes.

4. MarkDirtyRect() — Chunk Index Lookup Per-Chunk

File: ChunkTerrainRenderer.cs:369-377

_chunkGrid.ChunksInRect(minX, maxX, minY, maxY, _rectBuffer, createChunks);
for (int i = 0; i < _rectBuffer.Count; i++)
{
    int chunkIndex = _rectBuffer[i];
    _chunkGrid.GetChunk(chunkIndex).MarkDirty();  // <-- Linear search by index!
}

ChunkGrid.GetChunk(int index) at ChunkGrid.cs:117-127 does a linear scan of all chunks to find by index. With many dirty chunks, this adds up.

5. SmoothEdits() — O(n) Full Edit Scan

File: TerrainField.cs:262-286

Smooth scans every edit to check distance from brush center. No spatial culling.

---

Low-Hanging Fruit Optimizations

🔥 Priority 1: Replace GetChunk(int index) with Dictionary Lookup

Problem: ChunkGrid.GetChunk() iterates all chunks to find by index.

Fix: Add a _chunkByIndex dictionary to ChunkGrid.

// In ChunkGrid.cs
private readonly Dictionary<int, TerrainChunk> _chunkByIndex = new();

// In constructors and CreateChunk():
_chunkByIndex[chunk.Index] = chunk;

// Replace GetChunk(int index):
public TerrainChunk GetChunk(int index)
{
    if (!_chunkByIndex.TryGetValue(index, out var chunk))
        throw new ArgumentOutOfRangeException(...);
    return chunk;
}

Impact: O(n) → O(1) for dirty chunk marking. Significant for brushes affecting many chunks.

---

🔥 Priority 2: Spatial Index for RemoveContainedEdits()

Problem: Linear scan of all edits on every brush stroke.

Fix: Use the existing _editsByChunkKey to scope containment checks to only edits that overlap spatially.

public int RemoveContainedEdits(TerrainEdit newEdit)
{
    newEdit.GetBoundingBox(out float nMinX, out float nMaxX, out float nMinY

    // Collect candidate chunk keys that overlap with the new edit's bbox
    var candidateKeys = new HashSet<long>();
    float chunkSize = _chunkGrid.ChunkSize;
    float gridMinX = -(_chunkGrid.Cols * chunkSize) / 2f;
    float gridMinY = -(_chunkGrid.Rows * chunkSize) / 2f;

    int colStart = Mathf.FloorToInt((nMinX - gridMinX) / chunkSize);
    int colEnd = Mathf.CeilToInt((nMaxX - gridMinX) / chunkSize) - 1;
    int rowStart = Mathf.FloorToInt((nMinY - gridMinY) / chunkSize);
    int rowEnd = Mathf.CeilToInt((nMaxY - gridMinY) / chunkSize) - 1;

    for (int row = rowStart; row <= rowEnd; row++)
    {
        for (int col = colStart; col <= colEnd; col++)
        {
            long key = PackKey(col, row);
            if (_editsByChunkKey.TryGetValue(key, out var indices))
            {
                foreach (int idx in indices)
                    candidateKeys.Add(idx);
            }
        }
    }

    // Check containment ONLY against spatially overlapping edits
    List<int> toRemove = null;
    foreach (int i in candidateKeys)
    {
        TerrainEdit existing = _edits[i];
        // Same containment checks as before...
        if (IsContained(existing, newEdit))
        {
            toRemove ??= new List<int>();
            toRemove.Add(i);
        }
    }

    // ... rest of removal logic unchanged
}

Impact: Reduces containment checks from O(total_edits) to O(overlapping_edits). For localized brushing, this is often <10% of total edits.

---

🔥 Priority 3: Remove Unnecessary Rectangle Radius Expansion in Indexing

Problem: Rectangle edits are expanded by Radius during indexing, causing them to be registered against chunks they don't actually affect.

Fix: Remove the radius expansion for Rectangle shapes. Rectangles' bounding tent.

// In IndexEdit(), remove or conditionally apply the expansion:
if (edit.Shape == BrushShape.Rectangle)
{
    // REMOVE THIS BLOCK:
    // brushMinX -= edit.Radius;
    // brushMaxX += edit.Radius;
    // brushMinY -= edit.Radius;
    // brushMaxY += edit.Radius;
}

Impact: Rectangle edits only index against chunks they actually overlap. Reduces per-chunk edit list sizes.

---

🔥 Priority 4: Throttle/Defer Baking

Problem: TryBakeCoveredChunks() runs after every edit. Full chunk sampling + coverage checks are expensive during rapid input.

Fix Options:

Option A: Frame-based throttling
private int _bakeThrottleCounter = 0;
private const int BakeEveryNEdits = 50;

private void TryBakeCoveredChunks()
{
    _bakeThrottleCounter++;
    if (_bakeThrottleCounter < BakeEveryNEdits)
        return;
    _bakeThrottleCounter = 0;
    // ... existing baking logic
}

Option B: Input-idle baking
Defer baking until brush input stops (e.g., onMouseUp or after 100ms of no n

Impact: Eliminates per-edit baking overhead during continuous strokes. Bakint matters.

---

🔥 Priority 5: Spatial Culling for SmoothEdits()

Problem: Smooth scans all edits regardless of distance.

Fix: Use the chunk index to scope smoothing to only nearby edits.

public void SmoothEdits(Vector2 localPosition, float radius)
{
    if (radius <= 0f) return;

    // Find chunks that could contain edits within the smooth radius
    var affectedKeys = new HashSet<long>();
    float chunkSize = _chunkGrid.ChunkSize;
    int col = Mathf.FloorToInt((localPosition.x - _chunkGrid.MinX) / chunkSize);
    int row = Mathf.FloorToInt((localPosition.y - _chunkGrid.MinY) / chunkSize);

    // Expand by ceil(radius/chunkSize) to catch edge cases
    int expand = Mathf.CeilToInt(radius / chunkSize) + 1;
    for (int dr = -expand; dr <= expand; dr++)
    {
        for (int dc = -expand; dc <= expand; dc++)
        {
            long key = PackKey(col + dc, row + dr);
            if (_editsByChunkKey.TryGetValue(key, out var indices))
            {
                foreach (int idx in indices)
                    affectedKeys.Add(idx);
            }
        }
    }

    foreach (int i in affectedKeys)
    {
        TerrainEdit edit = _edits[i];
        float distanceFromBrush = edit.DistanceToShape(localPosition);
        if (distanceFromBrush >= radius) continue;

        float falloff = 1f - Mathf.SmoothStep(0f, radius, distanceFromBrush)
        edit.Radius = Mathf.Max(0f, edit.Radius - falloff * radius);
        _edits[i] = edit;
    }
}

Impact: Smooth operations become O(affected_chunks * edits_per_chunk) instea

---

🔧 Priority 6: Object Pooling for Sample Buffers

Problem: CartesianChunkFieldSampler.Sample() allocates new arrays each call. During rapid rebuilds, this generates GC pressure.

Fix: Use ObjectPool<float[,]> and ObjectPool<Vector2[,]> for sampler buffers.

// In CartesianChunkFieldSampler.cs
private static readonly ObjectPool<float[,]> _samplePool = new(maxStack: 16)
private static readonly ObjectPool<Vector2[,]> _positionPool = new(maxStack: 16);

public static Result Sample(TerrainField field, TerrainChunk chunk, float cellSize)
{
    float[,] samples = _samplePool.Get();
    Vector2[,] positions = _positionPool.Get();
    try
    {
        // ... sampling logic using pooled arrays
        return new Result(samples, positions, isUniform, isSolid);
    }
    finally
    {
        _samplePool.Recycle(samples);
        _positionPool.Recycle(positions);
    }
}

Impact: Reduces GC allocs during rapid brush strokes → smoother frame times.

---

Optimization Priority Summary

┌───────────────────────────────────┬────────┬────────┬───────────────────────────────┐
│           Optimization            │ Effort │ Impact │             Files             │
├───────────────────────────────────┼────────┼────────┼───────────────────────────────┤
│ ChunkGrid dictionary lookup       │ Low    │ High   │ ChunkGrid.cs
├───────────────────────────────────┼────────┼────────┼───────────────────────────────┤
│ Spatially-scoped containment      │ Medium │ High   │ TerrainField.cs
├───────────────────────────────────┼────────┼────────┼───────────────────────────────┤
│ Remove Rectangle radius expansion │ Low    │ Medium │ TerrainField.cs
├───────────────────────────────────┼────────┼────────┼───────────────────────────────┤
│ Throttle baking                   │ Low    │ Medium │ TerrainField.cs               │
├───────────────────────────────────┼────────┼────────┼─────────────────────
│ Smooth spatial culling            │ Medium │ Medium │ TerrainField.cs               │
├───────────────────────────────────┼────────┼────────┼───────────────────────────────┤
│ Sampler buffer pooling            │ Medium │ Medium │ CartesianChunkFieldSampler.cs │
└───────────────────────────────────┴────────┴────────┴─────────────────────

---

Recommendation

Start with Priority 1 (ChunkGrid dictionary) and Priority 3 (remove Rectangle expansion). These are low-risk, low-effort changes that provide immediate measurable improvement in brush responsiveness.

Would you like me to implement any of these optimizations, or dive deeper into a specific one?