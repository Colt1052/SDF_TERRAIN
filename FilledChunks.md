 Plan: Create Rectangular Edits for Uniform Chunks with Neighbor Merging                                                                                                        │
│                                                                                                                                                                                │
│ Context                                                                                                                                                                        │
│                                                                                                                                                                                │
│ When the union of all edits affecting a terrain chunk completely covers that chunk, the chunk is uniformly solid or uniformly empty. We want to create a new rectangular terrain edit to represent this uniform region, keeping the original edits alongside it. When adjacent chunks are also uniform with the same solidity, their rectangles should merge into a single larger shape, reducing total edit count.                                                                                                                   │
│                                                                                                                                                                                │
│ This is NOT a "bake" that replaces existing edits — the rectangular edit is added as a regular terrain edit, just like a brush stroke.                                         │
│                                                                                                                                                                                │
│ Files to Modify                                                                                                                                                                │
│                                                                                                                                                                                │
│ ┌───────────────────────────────────────────────┬───────────────────────────────────────────────────────────────────────┐                                                      │
│ │                     File                      │                                Purpose                                │                                                      │
│ ├───────────────────────────────────────────────┼──────────────────────────────────────────┤                                                      │
│ │ Runtime/Terrain/TerrainEdit.cs                │ Add Rectangle to BrushShape, implement rectangle SDF and bounding box │                                                      │
│ ├───────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤                                                      │
│ │ Runtime/Terrain/TerrainChunk.cs               │ Add IsUniform/IsSolid properties (already exist, verify)              │                                                      │
│ ├───────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤                                                      │
│ │ Runtime/Terrain/TerrainField.cs               │ Uniform chunk detectibor merging         │                                                      │
│ ├───────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤                                                      │
│ │ Runtime/Terrain/ChunkGrid.cs                  │ Add GetAdjacentChunks                    │                                                      │
│ ├───────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤                                                      │
│ │ Runtime/Terrain/CartesianChunkFieldSampler.cs │ Add IsUniform/IsSolid                    │                                                      │
│ ├───────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤                                                      │
│ │ Tests/EditMode/                               │ Tests for uniform detection and rectangle merging                     │                                                      │
│ └───────────────────────────────────────────────┴──────────────────────────────────────────┘                                                      │
│                                                                                                                                                                                │
│ Implementation Steps                                                                                                                                                           │
│                                                                                                                                                                                │
│ 1. Extend TerrainEdit with Rectangle Shape                                                                                                                                     │
│                                                                                                                                                   │
│ File: Runtime/Terrain/TerrainEdit.cs                                                                                                                                           │
│                                                                                                                                                   │
│ - Add Rectangle = 2 to BrushShape enum                                                                                                                                         │
│ - Implement SampleContribution() for Rectangle:                                                                                                   │
│   - LocalPosition = bottom-left corner, EndPosition = top-right corner                                                                                                         │
│   - Compute signed distance to axis-aligned rect boundary                                                                                                                      │
│   - Inside rect: contribution = Radius (full push/pull), sign based on                                                                            │
│   - On boundary: contribution = 0                                                                                                                                              │
│   - Outside: falls off linearly, clamped when Clamped is true                                                                                                                  │
│ - Implement GetBoundingBox() for Rectangle: return the rect corners dirEndPosition                                                                │
│                                                                                                                                                                                │
│ 2. Add IsUniform/IsSolid to Chunk and Sampler                                                                                                                                  │
│                                                                                                                                                                                │
│ Files: Runtime/Terrain/TerrainChunk.cs, Runtime/Terrain/CartesianChunkF                                                                           │
│                                                                                                                                                                                │
│ - Add public bool IsUniform { get; set; } and public bool IsSolid { get                                                                           │
│ - Add public bool IsUniform and public bool IsSolid to CartesianChunkFieldSampler.Result                                                                                       │
│ - In CartesianChunkFieldSampler.Sample(), after sampling the lattice, c                                                                           │
│   - IsUniform: all samples have the same sign (all < 0 or all > 0)                                                                                                             │
│   - IsSolid: when uniform, true if all negative (solid), false if all positive (air)                                                                                           │
│                                                                                                                                                   │
│ 3. Uniform Chunk Detection via Edit Union                                                                                                                                      │
│                                                                                                                                                                                │
│ File: Runtime/Terrain/TerrainField.cs                                                                                                                                          │
│                                                                                                                                                                                │
│ Add CreateUniformRegionEdits(float cellSize) method:                                                                                              │
│                                                                                                                                                                                │
│ 1. Sample all chunks using CartesianChunkFieldSampler.Sample() to popul                                                                           │
│ 2. For each uniform chunk, verify that the union of its edits fully covers the chunk:                                                                                          │
│    - Get all edit indices from _editsByChunkKey for this chunk                                                                                    │
│    - Compute the union of all edit bounding boxes                                                                                                                              │
│    - Check if the union AABB fully contains the chunk's AABB                                                                                                                   │
│    - If yes, this chunk is a candidate for a rectangular edit                                                                                     │
│ 3. Group contiguous uniform chunks by solidity (IsSolid):                                                                                                                      │
│    - Build a list of candidate chunks, separated by solid vs air                                                                                                               │
│    - For each group, find the bounding rectangle of all chunks                                                                                                                 │
│ 4. Create a rectangular edit for each group:                                                                                                                                   │
│    - Shape = BrushShape.Rectangle                                                                                                                 │
│    - LocalPosition = (minX, minY) of the group's bounding rect                                                                                                                 │
│    - EndPosition = (maxX, maxY) of the group's bounding rect                                                                                      │
│    - Radius = large enough value to fully cover the rect (e.g., rect diagonal * 2)                                                                                             │
│    - IsAdditive = group.IsSolid ? false : true (solid = placement/min,                                                                            │
│    - Clamped = true                                                                                                                                                            │
│ 5. Apply the rectangular edit via ApplyEdit() — this adds it alongside existing edits                                                                                          │
│ 6. Merge with existing rectangular edits (see next step)                                                                                          │
│                                                                                                                                                                                │
│ 4. Rectangle Merging                                                                                                                                                           │
│                                                                                                                                                                                │
│ File: Runtime/Terrain/TerrainField.cs                                                                                                             │
│                                                                                                                                                                                │
│ Add MergeAdjacentRectangleEdits() method:                                                                                                         │
│                                                                                                                                                                                │
│ 1. Collect all Rectangle-shaped edits from _edits                                                                                                 │
│ 2. Sort by (LocalPosition.x, LocalPosition.y) for deterministic order                                                                                                          │
│ 3. Iterative merge sweep (fixed-point):                                                                                                                                        │
│    - For each pair of rectangles:                                                                                                                 │
│      - Check if they are adjacent (share an edge, not just touching at a corner)                                                                                               │
│      - Check if they have the same IsAdditive (same solidity)                                                                                                                  │
│      - Check if they align perfectly on the shared edge (same extent on the perpendicular axis)                                                                                │
│      - If all pass: create union rectangle, replace both with merged ed                                                                           │
│    - Repeat until no more merges occur                                                                                                                                         │
│ 4. Re-index merged edits in _editsByChunkKey and _editChunkKeys                                                                                   │
│                                                                                                                                                                                │
│ Adjacency check for rectangles A and B:                                                                                                           │
│ - They share an edge if one is exactly to the left/right/above/below the other                                                                                                 │
│ - E.g., A.Right == B.Left AND A.MinY == B.MinY AND A.MaxY == B.MaxY                                                                               │
│                                                                                                                                                                                │
│ 5. ChunkGrid Helpers                                                                                                                                                           │
│                                                                                                                                                   │
│ File: Runtime/Terrain/ChunkGrid.cs                                                                                                                                             │
│                                                                                                                                                                                │
│ - Add List<TerrainChunk> GetAdjacentChunks(TerrainChunk chunk) that ret                                                                           │
│                                                                                                                                                                                │
│ 6. Integration Point                                                                                                                                                           │
│                                                                                                                                                   │
│ Call CreateUniformRegionEdits(cellSize) followed by MergeAdjacentRectangleEdits() from:                                                                                        │
│ - ApplyEdit() after the edit is indexed (for incremental consolidation)                                                                                                        │
│ - A manual field.ConsolidateUniformRegions(cellSize) API for batch operations                                                                                                  │
│                                                                                                                                                                                │
│ Key Design Notes                                                                                                                                  │
│                                                                                                                                                                                │
│ - Original edits are kept — the rectangular edit is additive, not a rep                                                                           │
│ - 4-directional merging — up/down/left/right only, no diagonal                                                                                                                 │
│ - Union-based uniformity — a chunk is uniform only when the combined ed                                                                           │
│ - Deterministic — sorting and fixed-order sweeps ensure same output for same inputs                                                                                            │
│                                                                                                                                                   │
│ Verification                                                                                                                                                                   │
│                                                                                                                                                                                │
│ 1. Unit tests:                                                                                                                                    │
│    - Rectangle SDF correctness at sample points                                                                                                                                │
│    - Uniform chunk detection matches sampler results                                                                                                                           │
│    - Rectangle merging for adjacent same-solidity chunks                                                                                                                       │
│    - Merging respects 4-direction adjacency (no diagonal)                                                                                         │
│ 2. Integration: 2x2 uniform region produces single merged rectangle                                                                                                            │
│ 3. Regression: All existing UniformRedundantEditTests and TerrainEditSh                                                                           │
│ 4. Visual: Debug view shows merged rectangles instead of many small ones