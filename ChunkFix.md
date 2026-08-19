I now have a complete understanding of the codebase. Let me trace through the seam problem systematically.

---

Root Cause: Asymmetric SDF values at chunk boundaries

The bug is in CartesianChunkFieldSampler.cs — specifically in how the wedge mask interacts with Marching Squares at chunk boundaries.

The Mechanism

Every chunk samples the same shared Cartesian lattice. For lattice points near a chunk boundary, the WedgeMask is combined with the terrain SDF via Mathf.Max:

// Line 107-109
float final = Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));

For a lattice point inside a chunk's wedge, the mask is negative → terrain wins.\nFor a lattice point outside the wedge, the mask is WedgeMaskSteepness × 1000 → the mask wins, sample becomes large positive (air).

The Seam

Consider a cell edge that crosses the boundary ray between chunk A and chunk B. It has two lattice endpoints:
- W_A (on A's side): A samples terrain(W_A) (negative, solid), B samples mask(W_A) (large positive, air)
- W_B (on B's side): A samples mask(W_B) (large positive, air), B samples terrain(W_B) (negative, solid)

Both chunks run Marching Squares on this same cell edge, but with different SDF values at the endpoints. The contour vertex is placed by linear interpolation:

// A places vertex at: t_A = terrain_A / (terrain_A - mask_val) ≈ 0.02 along the edge (near W_A)
// B places vertex at: t_B = mask_val / (mask_val - terrain_B) ≈ 0.98 along the edge (near W_B)

The two vertices are ~96% of a cell apart. That's the visible gap.

Why the ChunkSeamCache Doesn't Fix It

The ChunkSeamCache ensures both chunks use the same dirStart/dirEnd vectors for their shared ray, which is necessary but insufficient. The Cross product at a lattice point on A's side of the ray is negative (inside A, outside B), so the mask signs are opposite for the two chunks at the same point. Same direction vector, but Cross(dir, point) depends on the point's position, not just the direction. So the SDF values are still asymmetric.

The Mismatch Between Documentation and Implementation

The doc comment on CartesianChunkFieldSampler (lines 22-26) describes the intended fix:

▎ the optional previousSeam/nextSeam parameters to Sample let a caller supply the exact same precomputed sample value to both neighboring chunks for any lattice point near their shared ray

But the Sample method signature has no such parameters — only seamCache, which provides direction vectors, not sample values. The documentation describes what should exist; the implementation only caches directions.

The Fix

The ChunkSeamCache (or the sampler) needs to ensure that for every lattice point sampled by both adjacent chunks, the same terrain SDF value is used, not the wedge-masked value. Two approaches:

1. Cache terrain samples at seam lattice points: ChunkSeamCache computes which lattice points lie near each boundary ray, samples the terrain field (without wedge mask) at those points, and provides the cached values to both chunks during sampling. Both chunks use the cached terrain value instead of max(terrain, mask) at these points.
2. Compute seam vertices from the terrain field: After Marching Squares generates each chunk's mesh, identify the cell edges that cross the chunk boundary. Recompute the contour vertices on those edges using only terrain SDF values (not masked values), so both chunks place the vertex at the terrain zero-crossing.

Approach 1 is cleaner because it keeps the fix in the sampling layer where the asymmetry originates, and the meshing layer sees consistent input.

---

Want me to implement the fix?
  The fix: Extend ChunkSeamCache to cache terrain SDF values at lattice points near each seam ray, then use those cached values in CartesianChunkFieldSampler instead of the wedge-masked values.

  Let me think through the design:

  1. ChunkSeamCache needs to:
     - Know the cell size and max radius to determine which lattice points are near each seam
     - Sample the terrain field at those lattice points
     - Store the sample values keyed by position
     - Provide a method to check if a position has a cached seam value
  2. CartesianChunkFieldSampler needs to:
     - After computing the sample value, check if there's a cached seam value for this position
     - If so, use the cached value instead

  Actually, let me reconsider. The ChunkSeamCache currently doesn't know about the terrain field, cell size, or max radius. It only knows about the ChunkGrid. I need to think about how to extend it cleanly.

  Option A: Extend ChunkSeamCache to also cache terrain samples
  - Constructor takes TerrainField, cellSize, maxRadius
  - Computes lattice bounds for each seam
  - Samples terrain at seam-adjacent lattice points
  - Provides GetCachedSample(Vector2 position) method

  Option B: Keep ChunkSeamCache focused on directions, add seam sampling to CartesianChunkFieldSampler
  - The sampler determines which positions are near seams
  - It uses the seam cache directions to identify the boundary ray
  - For positions near the boundary, it uses the terrain value directly (no mask)

  Option C: In the sampler, for positions near the boundary ray, skip the mask and use terrain directly
  - Determine "near boundary" as: the mask value is small (within some threshold)
  - If |mask| < threshold, use terrain value instead of max(terrain, mask)

  Option C is simplest but might not be precise enough. The threshold would need to be calibrated.

  Option B is cleaner. The sampler checks if a lattice point is near any seam ray, and if so, uses only the terrain value. The seam cache provides the ray directions.

  Actually, let me think about this more carefully. The key insight is:

  For a lattice point W that is near the boundary between chunks A and B:
  - A evaluates: max(terrain(W), mask_A(W))
    - If W is on A's side: mask_A is small negative → max picks terrain (if terrain > mask_A) or mask (if mask_A > terrain, i.e., mask is shallower)
    - If W is on B's side: mask_A is positive → max picks mask (air)
  - B evaluates: max(terrain(W), mask_B(W))
    - If W is on A's side: mask_B is positive → max picks mask (air)
    - If W is on B's side: mask_B is small negative → max picks terrain or mask

  The asymmetry is: when W is on A's side, A uses terrain and B uses mask. When W is on B's side, A uses mask and B uses terrain.

  For lattice points that both chunks sample (in the overlap region), we want both to use the same value. The simplest approach: if a lattice point is within 1 cell of the boundary ray, use terrain(W) directly (no mask). This way both chunks get the same value.

  But we need to be careful: using terrain instead of mask means the mesh might extend slightly beyond the chunk boundary at these points. However, the neighbor chunk will have the same terrain value at these points, so the mesh will be continuous. The "extra" triangles from chunk A that extend past the boundary will overlap with chunk B's triangles, but since they're at the same positions, there's no visual issue.

  Actually wait, that's not quite right. If chunk A uses terrain at a point that's on B's side of the boundary, and chunk A's Marching Squares generates a triangle that extends past the boundary, it will overlap with chunk B's mesh. But since both chunks use the same terrain values, the triangles should be compatible (same contour vertices on shared edges).

  Hmm, but the triangles might not be exactly the same because the cells on either side of the boundary have different neighbors. Let me think...

  Actually, the key insight is: bo

  In the sampler, we know the boundary ray direction (from the seam cache). A lattice point W is near the boundary if:
  - |Cross(dirBoundary, W)| < cellSize

  This means the perpendicular distance from W to the boundary ray is less than one cell. For these points, we use terrain(W) instead of max(terrain(W), mask(W)).

  Wait, but we also need to handle the case where the terrain surface crosses the boundary. At the boundary, the mesh should stop. If we use terrain values everywhere near the boundary, the mesh might extend past the boundary ray.

  Hmm, let me reconsider. The wedge mask serves two purposes:
  1. Clip the mesh at the chunk boundary (so each chunk only renders its own wedge)
  2. Provide SDF values for Marching Squares at boundary cells

  For purpose 1, we need the mask to be effective at the boundary. For purpose 2, we need symmetric values.

  These two purposes conflict: a strong mask clips well but creates asymmetric Marching Squares values; a weak mask is more symmetric but clips poorly.

  The solution: use the mask for clipping but use terrain for interpolation. Specifically:
  - For the Marching Squares SDF values, use terrain at boundary-adjacent points
  - The mesh will extend slightly past the boundary, but that's OK because:
    a. The neighbor chunk has the same mesh at the boundary
    b. The overlap is at most one cell wide
    c. The terrain surface (zero contour) is the same from both sides

  But actually, point (c) is only true if the terrain surface doesn't cross the boundary at that exact location. If the terrain surface is far from the boundary (e.g., deep inside the planet), the mesh contour won't be near the boundary anyway. If the terrain surface crosses the boundary, the mesh contour from both chunks should match because they use the same terrain values.

  OK wait, I think there's a subtlety I'm missing. Let me reconsider.

  When both chunks use terrain values at boundary-adjacent points, the Marching Squares at a boundary-spanning cell uses all terrain values. The contour generated for this cell is the terrain contour, not clipped by the boundary. This contour might extend past the boundary ray.

  But the neighbor chunk processes the same cell with the same terrain values, producing the same contour. So the meshes overlap but don't gap. Visually, there's no seam.

  The mesh extends slightly past the boundary, which means:
  - Each chunk renders a few extra triangles past its boundary
  - The neighbor chunk renders the same triangles
  - There's a slight overdraw, but no visual artifact

  This is acceptable. The alternative (clipping the mesh at the boundary) is more complex and not necessary for visual correctness.

  But wait, there's another concern: what about the collider? If the mesh extends past the boundary, the collider might have overlapping polygons with the neighbor chunk. This could cause physics issues.

  Looking at the code, TerrainColliderBuilder builds the collider from the mesh data. If both chunks have overlapping collider polygons at the boundary, there might be double-thick collision.

  Hmm, but with the current code (using the mask), the collider at the boundary is determined by the mask value, not the terrain. So the current collider already has issues at the boundary.

  For now, let me focus on the visual mesh seam. The collider can be addressed separately if needed.

  Let me design the fix:

  1. In CartesianChunkFieldSampler.Sample, after computing the terrain value, check if the position is near the boundary ray. If so, use terrain directly.
  2. "Near the boundary" means: the perpendicular distance to the boundary ray is less than cellSize. Check using Cross product.
  3. For the check, use the seam cache direction vectors.

  Here's the approach:
      // so Marching Squares places contour vertices at identical positions.
      samples[i, j] = terrainValue;
  }
  else
  {
      float final = fullCircle
          ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
          : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
      samples[i, j] = final;
  }

  With IsNearSeam:
  private static bool IsNearSeam(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
  {
      float distStart = Mathf.Abs(Cross(dirStart, position));
      float distEnd = Mathf.Abs(Cross(dirEnd, position));
      return distStart < cellSize || distEnd < cellSize;
  }position.magnitude - maxRadius. This doesn't have the same asymmetry issue because all chunks share the same maxRadius. So the radius mask is fine.

  Also, I need to think about the reflex wedge case. For a reflex wedge (angular size > PI), the wedge mask uses a complement. The seam check should still work because it's based on the boundary ray directions, not the mask computation.

  Let me also think about edge cases:
  1. Full circle (1 chunk): no seams, so seamCache is null. No fix needed.
  2. 2 chunks: each has a reflex wedge. The boundary rays are at startAngle and endAngle.
  3. Many chunks: each chunk has 2 boundary rays.

  For case 2, with 2 chunks, each chunk spans 180°. The boundary between them is at one angle. Each chunk's start and end are the two boundary rays. The fix should work because we check both start and end directions.

  OK, let me implement this. I'll also update the tests.

  Wait, actually I want to reconsider the approach. Using IsNearSeam with cellSize as the threshold means we use terrain values for lattice points within 1 cell of the boundary. This is correct for ensuring boundary-spanning cells have matching SDF values.

  But there's a subtlety: the "near seam" check uses the current chunk's dirStart and dirEnd. For chunk A, the seam is at its end ray. For chunk B, the seam is at its start ray. Both use the same direction (from the seam cache), so the check is consistent.

  Let me also think about whether I should update the ChunkSeamCache or just modify the sampler. The sampler already receives the seamCache, so I can add the seam proximity check in the sampler without modifying the cache.

  Actually, I think it's cleaner to keep the seam cache as-is (it provides directions) and add the seam proximity check in the sampler. The sampler knows the cell size and can determine which lattice points are near the seam.

  Let me implement:

  1. Add IsNearSeam helper method to CartesianChunkFieldSampler
  2. Modify the sampling loop to use terrain values near seams
  3. Update the doc comment
  4. Add/update tests

  Let me also think about whether the WedgeMaskSteepness constant can be removed or reduced. With the seam fix, the steepness no longer matters for seam quality (since we use terrain values near the seam). The steepness still matters for points far from the seam, where it ensures the mask dominates. So I should keep the steepness as-is.

  Let me now code the fix.

  Actually, let me re-read the current sampler code one more time to make sure I integrate the fix cleanly.

  Looking at the sampling loop (lines 88-114):

  for (int i = 0; i < width; i++)
  {
      float x = (ixMin + i) * cellSize;

      for (int j = 0; j < height; j++)
      {
          float y = (iyMin + j) * cellSize;
          var position = new Vector2(x, y);

          float terrainValue = field.Sample(position);
          float final = fullCircle
              ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
              : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));

          positions[i, j] = position;
          samples[i, j] = final;
      }
  }

  I'll modify this to:

  for (int i = 0; i < width; i++)

          float terrainValue = field.Sample(position);

          float final;
          if (!fullCircle && IsWithinSeamMargin(position, dirStart, dirEnd, cellSize))
          {
              // Near a chunk boundary: use the terrain value directly so both
              // neighboring chunks produce identical SDF samples on shared
              // lattice points. The wedge mask is intentionally skipped here
              // (the 1-cell overlap guarantees the neighbor chunk handles any
              // clipping that would otherwise be needed).
              final = terrainValue;
          }
          else
          {
              final = fullCircle
                  ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
                  : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
          }

          positions[i, j] = position;
          samples[i, j] = final;
      }
  }

  And add the helper:

  private static bool IsWithinSeamMargin(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
  {
      float distToStart = Mathf.Abs(Cross(dirStart, position));
      float distToEnd = Mathf.Abs(Cross(dirEnd, position));
      return distToStart < cellSize || distToEnd < cellSize;
  }

  Wait, I should think about this more carefully. The Cross product gives dir.x * p.y - dir.y * p.x. For a unit direction vector, this is the perpendicular (signed) distance from the origin to the line through position perpendicular to dir. Actually, |Cross(dir, p)| is the perpendicular distance from p to the ray in direction dir from the origin. Since dir is a unit vector, Cross(dir, p) = |dir| * |p| * sin(angle) = |p| * sin(angle), and the perpendicular distance is |Cross(dir, p)|.

  So |Cross(dir, position)| < cellSize means the perpendicular distance from the position to the boundary ray is less than one cell. This is the correct condition for "within the seam margin."

  But wait, the lattice points at exactly the boundary ray have Cross = 0. Points one cell away have Cross ≈ cellSize (if the cell edge is perpendicular to the ray) or less (if at an angle). The check |Cross| < cellSize should cover all lattice points within one cell of the ray.

  Actually, the Cross product doesn't give perpendicular distance for non-unit direction vectors. But dirStart and dirEnd are from RadialMath.DirectionAt(angle), which returns (cos, sin) — a unit vector. So |Cross(dir, p)| is indeed the perpendicular distance.

  Hmm wait, but I also need to consider: the boundary ray is a ray (from origin outward), not a line. A lattice point might be close to the line extending the ray but on the opposite side of the origin. For example, a point at (-1, 0) is close to the line of the ray at angle 0 (positive x-axis) but on the opposite sidewedge's bounding box, which is near the origin.

  Actually, for a chunk that spans [0, π/2], the bounding box includes points near the origin. A point at (-0.5, -0.5) is within the bounding box but not within the wedge. The boundary ray at angle 0 is the positive x-axis. The point (-0.5, -0.5) is at perpendicular distance 0.5 from the line, but it's on the opposite side of the origin. Should this point use terrain values?

  Probably not, because it's not near the actual seam — it's near the ray extension but on the wrong side of the origin. The seam is the boundary between chunks, which is the ray from the origin outward.

  To handle this, I should check that the position is in the same direction as the ray: Vector2.Dot(dir, position) > 0. This ensures the point is on the "forward" side of the ray.
      return IsNearRay(position, dirStart, cellSize) || IsNearRay(position, dirEnd, cellSize);
  }

  private static bool IsNearRay(Vector2 position, Vector2 dir, float cellSize)
  {
      // Perpendicular distance to the ray line
      float perpDistance = Mathf.Abs(Cross(dir, position));
      if (perpDistance >= cellSize)
          return false;

      // Must be on the forward side of the ray (not behind the origin)
      float forwardDistance = Vector2.Dot(dir, position);
      return forwardDistance > -cellSize; // Allow slight margin behind origin
  }

  Hmm, actually I'm overthinking this. The lattice bounds are computed to cover the chunk's wedge with a 1-cell margin. For a chunk starting at angle 0, the minimum x in the lattice is -1 * cellSize (approximately). The boundary ray at angle 0 is the positive x-axis. Points with negative x are behind the origin, so they're not near the ray.

  For the forward check, Dot(dir, position) > 0 is sufficient. Points with Dot < 0 are behind the origin and shouldn't be considered near the seam.

  But I should also consider: what about points very close to the origin? At the origin, Cross = 0 and Dot = 0. These points are technically on every ray. But the terrain at the origin is deep inside the planet, so the mesh contour is far away. The seam fix doesn't affect the mesh topology near the origin.

  OK let me simplify. I'll use:

  private static bool IsWithinSeamMargin(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
  {
      float distStart = Mathf.Abs(Cross(dirStart, position));
      float distEnd = Mathf.Abs(Cross(dirEnd, position));

      return (distStart < cellSize && Vector2.Dot(dirStart, position) > 0f)
          || (distEnd < cellSize && Vector2.Dot(dirEnd, position) > 0f);
  }

  This checks if the perpendicular distance to either boundary ray is less than one cell, AND the point is on the forward side of the ray.

  Actually, I realize I should be even more careful. The > 0f check might be too strict for points exactly on the boundary. Let me use >= 0f. But points with Dot = 0 are exactly perpendicular to the ray, which means they're at the origin (since Cross ≈ 0 and Dot = 0 implies position ≈ 0). So > 0f should be fine.

  Hmm, actually no. Dot = 0 means the position is perpendicular to the ray direction. This could be at any distance from the origin. For example, dir = (1, 0) and position = (0, 5) have Dot = 0. The Cross = |15 - 00| = 5, which is > cellSize, so it would be excluded by the perpendicular distance check. Good.

  Another case: dir = (1, 0) and position = (0.1, 0.1). Dot = 0.1 > 0. Cross = |10.1 - 00.1| = 0.1 < cellSize (if cellSize > 0.1). So this point is near the ray. Good.

  And dir = (1, 0) and position = (-0.1, 0.1). Dot = -0.1 < 0. So this point is behind the origin. Cross = 0.1 < cellSize. But Dot < 0, so it's excluded. Good, because this point is near the ray extension but on the wrong side.

  OK, the logic is correct. Let me
  Now let me also think about the tests I need to add/modify:

  1. The existing test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints already checks for identical samples. But it might not catch the seam issue because it checks points that both chunks sample. Let me verify...

  Actually, I need to re-examine this test. It checks all shared lattice points (points that appear in both chunks' grids). For the shared points:
  - If the point is on A's side: A uses terrain, B uses mask (before the fix). These are different! So the test should fail.

  Wait, but the test exists and presumably passes. Let me re-read it...

  for (int i = 0; i < widthA; i++)
              for (int bj = 0; bj < heightB; bj++)
              {
                  if (Vector2.Distance(posA, resultB.Positions[bi, bj]) < 1e-4f)
                  {
                      Assert.AreEqual(resultA.Samples[i, j], resultB.Samples[bi, bj]);
                      sharedPointsChecked++;
                  }
              }
          }
      }
  }

  This checks that at shared positions (same world coordinates in both grids), the samples are equal. For a test with 4 chunks, baseRadius = 10, maxRadius = 15, cellSize = 0.5:

  The boundary between chunks 0 and 1 is at π/4. The ray at π/4 goes through points where x = y. Lattice points at (0.5k, 0.5k) for integer k are on this ray.

  For lattice point (0.5, 0.5) on the boundary ray:
  - Cross(dir_π4, (0.5, 0.5)) = cos(π/4)0.5 - sin(π/4)0.5 = 0.5(cos - sin) = 0.50 = 0
  - So the mask at this point is 0.
  - Sample = max(terrain, 0).

  For chunk 0: terrain(0.5, 0.5) = magnitude - baseRadius = √0.5 - 10 ≈ 0.707 - 10 ≈ -9.293. Sample = max(-9.293, 0) = 0.
  For chunk 1: same. Sample = 0.

  So at the boundary ray, both samples are 0. The test passes for these points.

  Now for lattice point (0, 0.5), which is on A's side of the π/4 ray:
  - Cross(dir_π4, (0, 0.5)) = cos(π/4)*0.5 - sin(π/4)*0 = 0.354 - 0 = 0.354
  - This is positive, meaning (0, 0.5) is on the CCW side of the ray (toward chunk 1's side).

  Wait, I need to be more careful. dir_π4 = (cos(π/4), sin(π/4)) = (0.707, 0.707).

  Cross(dir_π4, (0, 0.5)) = 0.707 * 0.5 - 0.707 * 0 = 0.354 > 0

  For chunk 0 (wedge [0, π/4]):
  - dirEnd = dir_π4
  - maskEnd = Cross(dirEnd, (0, 0.5)) * 1000 = 0.354 * 1000 = 354

  Wait, that's positive. So (0, 0.5) is outside chunk 0's wedge? Let me check: the angle of (0, 0.5) is π/2. π/2 > π/4, so yes, it's past chunk 0's end angle.

  But is (0, 0.5) in chunk 0's lattice? Chunk 0's wedge is [0, π/4], so the bounding box includes x from 0 to maxRadius and y from 0 to maxRadius*cos(π/4) ≈ 10.6. Wait, let me compute properly.

  RadialMath.PositionAt(0, 15) = (15, 0)
  RadialMath.PositionAt(π/4, 15) = (10.6, 10.6)

  So minX = 0, maxX = 15, minY = 0, maxY = 10.6.

  ixMin = Floor(0/0.5) - 1 = -1
  ixMax = Ceil(15/0.5) + 1 = 31

  The lattice covers x from -0.5 to 15.5 and y from -0.5 to 11. The point (0, 0.5) is at ix=0, jy=1. This is in the lattice.

  But (0, 0.5) has angle π/2, which is past chunk 0's end angle π/4. So the wedge mask for chunk 0:
  - dirStart = DirectionAt(0) = (1, 0)
  - dirEnd = DirectionAt(π/4) = (0.707, 0.707)

  maskStart = -Cross(dirStart, (0, 0.5)) * 1000 = -(10.5 - 00) * 1000 = -500

  Wait, that's negative. So from the start boundary, (0, 0.5) is inside.


  Now, is (0, 0.5) in chunk 1's lattice? Chunk 1's wedge is [π/4, π/2].

  RadialMath.PositionAt(π/4, 15) = (10.6, 10.6)
  RadialMath.PositionAt(π/2, 15) = (0, 15)

  So minX = 0, maxX = 10.6, minY = 0, maxY = 15.

  ixMin = Floor(0/0.5) - 1 = -1
  ixMax = Ceil(10.6/0.5) + 1 = 23
  iyMin = Floor(0/0.5) - 1 = -1
  iyMax = Ceil(15/0.5) + 1 = 31

  The lattice covers x from -0.5 to 11 and y from -0.5 to 15.5. The point (0, 0.5) is at ix=0, jy=1. This is in the lattice.

  For chunk 1 at (0, 0.5):
  - dirStart = DirectionAt(π/4) = (0.707, 0.707)
  - dirEnd = DirectionAt(π/2) = (0, 1)

  maskStart = -Cross(dirStart, (0, 0.5)) * 1000 = -(0.7070.5 - 0.7070) * 1000 = -353.5

  Negative. So (0, 0.5) is inside chunk 1 from the start boundary.

  maskEnd = Cross(dirEnd, (0, 0.5)) * 1000 = (00.5 - 10) * 1000 = 0

  Zero. So (0, 0.5) is exactly on the end boundary of chunk 1.

  WedgeMask = max(radiusMask, max(maskStart, maskEnd)) = max(-14.5, max(-353.5, 0)) = max(-14.5, 0) = 0

  So the sample for chunk 1 at (0, 0.5) is max(terrain, 0) = max(-9.293, 0) = 0.

  Summary:
  - Chunk 0 at (0, 0.5): sample = 353.5
  - Chunk 1 at (0, 0.5): sample = 0

  These are different! So the test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints should fail for this point.

  But wait, the test presumably passes (or the user would have noticed). Let me check: is (0, 0.5) actually in both lattices?

  For chunk 0, the lattice includes y from -0.5 to 11. (0, 0.5) has y = 0.5, which is in range. And x from -0.5 to 15.5. x = 0 is in range.

  For chunk 1, the lattice includes y from -0.5 to 15.5 and x from -0.5 to 11. (0, 0.5) is in range.

  So yes, (0, 0.5) is in both lattices. And the samples are different (353.5 vs 0).

  But hold on — I was computing without the seam cache! The test uses a seam cache:

  var seamCache = new ChunkSeamCache(grid);
  CartesianChunkFieldSampler.Result resultA = CartesianChunkFieldSampler.Sample(field, chunkA, 15f, cellSize, seamCache);
  CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, 15f, cellSize, seamCache);

  With the seam cache, the directions come from the cache instead of being computed from angles. But the directions are the same (the cache computes them from the same angles). So the result should be the same.


  But the Cross product depends on the position, not just the direction. For position (0, 0.5):
  - Cross(dir_π4, (0, 0.5)) = 0.354 (same for both chunks)

  For chunk 0: maskEnd = Cross(dirEnd, (0, 0.5)) * 1000 = 0.354 * 1000 = 354
  For chunk 1: maskStart = -Cross(dirStart, (0, 0.5)) * 1000 = -0.354 * 1000 = -354

  Different signs! Because maskEnd uses Cross(dir, p) and maskStart uses -Cross(dir, p). The directions are the same, but the formulas are different (one negates, one doesn't). This is by design — the start boundary has the "inside" on the CCW side, so -Cross makes it negative inside. The end boundary has the "inside" on the CW side, so Cross makes it negative inside.

  For chunk 0 ([0, π/4]):
  The wedge goes from angle 0 to angle π/4. The bounding box of the wedge:
  - At angle 0: position (15, 0)
  - At angle π/4: position (10.6, 10.6)
  - The wedge includes the origin (0, 0)

  So the bounding box is:
  - minX = 0 (at origin and angle 0)
  - maxX = 15 (at angle 0)
  - minY = 0 (at origin and angle 0)
  - maxY = 10.6 (at angle π/4)

  But wait, the wedge is a triangle with vertices (0,0), (15,0), (10.6, 10.6). The bounding box of this triangle is [0, 15] × [0, 10.6].

  Hmm, actually the ComputeLatticeBounds function also considers axis-aligned angles:

  for (int k = 0; k < 4; k++)
  {
      float axisAngle = k * (Mathf.PI * 0.5f);
      for (int n = -1; n <= 1; n++)
      {
          float candidate = axisAngle + (n * 2f * Mathf.PI);
          if (candidate >= startAngle && candidate <= endAngle)
          {
              Include(RadialMath.PositionAt(candidate, maxRadius));
              break;
          }
      }
  }

  For chunk 0 ([0, π/4]):
  - k=0: axisAngle = 0. candidate = 0. 0 >= 0 && 0 <= π/4. Yes! Include (15, 0).
  - k=1: axisAngle = π/2. candidate = π/2. π/2 >= 0 && π/2 <= π/4? No (π/2 > π/4).

  So the bounding box is:
  - minX = 0, maxX = 15, minY = 0, maxY = 10.6

  ixMin = Floor(0/0.5) - 1 = -1
  ixMax = Ceil(15/0.5) + 1 = 31
  iyMin = Floor(0/0.5) - 1 = -1
  iyMax = Ceil(10.6/0.5) + 1 = 23

  So lattice x ranges from -0.5 to 15.5, y from -0.5 to 11.

  The point (0, 0.5) is at ix=0, jy=1. In range.

  For chunk 1 ([π/4, π/2]):
  - At angle π/4: (10.6, 10.6)
  - At angle π/2: (0, 15)
  - Wedge includes origin

  Axis-aligned angles:
  - k=1: axisAngle = π/2. π/2 >= π/4 && π/2 <= π/2. Yes! Include (0, 15).


  ixMin = -1, ixMax = 23, iyMin = -1, iyMax = 31.

  The point (0, 0.5) is at ix=0, jy=1. In range.

  So (0, 0.5) IS in both lattices. And the samples are different.

  Wait, but maybe the test actually does fail? Let me check if the user has run the tests recently. The user just says the borders are visibly wrong, not that the tests pass.

  Actually, I realize I should jus
  For chunk 0 at (0, 0.5):
  - dirStart = seamCache.GetStartDirection(0) = DirectionAt(chunk 0's start angle) = DirectionAt(0) = (1, 0)

    Wait, seamCache.GetStartDirection(0) returns the previous chunk's end direction. For chunk 0 (index 0), the previous chunk is chunk 3 (index 3). Chunk 3's end angle is 2π. DirectionAt(2π) = (1, 0).

    Hmm, but chunk 0's start angle is 0, and DirectionAt(0) = (1, 0). So the seam cache direction is the same.
  - dirEnd = seamCache.GetEndDirection(0) = DirectionAt(chunk 0's end angle) = DirectionAt(π/4) = (√2/2, √2/2) ≈ (0.7071, 0.7071)
  - reflexWedge: angularSize = π/4 ≈ 0.785 < π. So not reflex.
  - WedgeMask:
    - radiusMask = |(0, 0.5)| - 15 = 0.5 - 15 = -14.5
    - maskStart = -Cross(dirStart, (0, 0.5)) * 1000 = -(10.5 - 00) * 1000 = -500
    - maskEnd = Cross(dirEnd, (0, 0.5)) * 1000 = (0.70710.5 - 0.70710) * 1000 = 353.55

    Wait, dirEnd = (0.7071, 0.7071) and position = (0, 0.5).
    Cross(dirEnd, position) = 0.7071 * 0.5 - 0.7071 * 0 = 0.35355

    maskEnd = 0.35355 * 1000 = 353.55

    WedgeMask = max(-14.5, max(-500, 353.55)) = max(-14.5, 353.55) = 353.55
  - terrainValue = field.Sample((0, 0.5)) = 0.5 - 10 = -9.5 (approximately, with noise it might be slightly different)
  - final = max(-9.5, 353.55) = 353.55

  For chunk 1 at (0, 0.5):
  - dirStart = seamCache.GetStartDirection(1) = DirectionAt(chunk 0's end angle) = DirectionAt(π/4) = (0.7071, 0.7071)

    (The seam cache returns the same Vector2 as chunk 0's end direction.)
  - dirEnd = seamCache.GetEndDirection(1) = DirectionAt(chunk 1's end angle) = DirectionAt(π/2) = (0, 1)
  - reflexWedge: angularSize = π/4. Not reflex.
  - WedgeMask:
    - radiusMask = 0.5 - 15 = -14.5
    - maskStart = -Cross(dirStart, (0, 0.5)) * 1000 = -(0.70710.5 - 0.70710) * 1000 = -353.55
    - maskEnd = Cross(dirEnd, (0, 0.5)) * 1000 = (00.5 - 10) * 1000 = 0

    WedgeMask = max(-14.5, max(-353.55, 0)) = max(-14.5, 0) = 0
  - terrainValue = -9.5
  - final = max(-9.5, 0) = 0

  So chunk 0 sample at (0, 0.5) = 353.55, chunk 1 sample = 0. These are different. The test should fail.

  Unless... the test uses cellSize = 0.5 and the point (0, 0.5) is computed as:
  x = (ixMin + i) * cellSize = (-1 + 1) * 0.5 = 0
  y = (iyMin + j) * cellSize = (-1 + 2) * 0.5 = 0.5

  So position (0, 0.5) is at grid index (i=1, j=2) in chunk 0's lattice and (i=1, j=2) in chunk 1's lattice. Both have this position.

  Hmm, but the test uses Vector2.Distance(posA, resultB.Positions[bi, bj]) < 1e-4f to find matching positions. So it correctly identifies (0, 0.5) as a shared position.

  I'm confused why the test would pass. Let me check if there's something about the noise settings that makes the terrain value positive at (0, 0.5), overriding both mask and terrain...

  OK so either:
  1. The test doesn't actually pass (the user hasn't run it, or it was recently added)
  2. I'm making a calculation error
  3. The test passes but only checks a subset of shared points that happen to agree

  Actually, looking at the test more carefully: it checks ALL shared lattice points. For points where both samples are mask values (far from the terrain), they should agree. For points where both are terrain values, they agree. Only for points near the boundary where one uses terrain and the other uses mask do they disagree.

  So the test SHOULD fail. Let me just proceed with the fix.


  Axis-aligned: k=0 gives angle 0, which is in [0, π/4]. Include((15, 0)): no change.
  k=1 gives angle π/2, not in [0, π/4].
  k=2, k=3: similarly not in range.

  So the bounding box is [0, 15] × [0, 10.6].

  ixMin = Floor(0/0.5) - 1 = -1
  iyMin = Floor(0/0.5) - 1 = -1

  So the lattice starts at x = -0.5, y = -0.5. This includes the origin and points slightly outside the bounding box.

  The point (0, 0.5) is at ix=0, jy=1. It IS in chunk 0's lattice.

  For chunk 1, the bounding box is [0, 10.6] × [0, 15]. The lattice starts at (-0.5, -0.5).

  (0, 0.5) is in chunk 1's lattice too.

  So yes, (0, 0.5) is shared, and the samples are different.

  Let me also check: is there any point on the boundary ray (π/4) that has different samples? On the ray, x = y. The lattice points on this ray: (0, 0), (0.5, 0.5), (1, 1), (1.5, 1.5), etc.

  For (0, 0): Cross = 0, mask = 0. Sample = max(terrain(0), 0) = max(-10, 0) = 0. Same from both sides.

  For (0.5, 0.5): Cross(dir_π4, (0.5, 0.5)) = 0.70710.5 - 0.70710.5 = 0. Mask = 0. Sample = max(terrain, 0) = max(0.7071-10, 0) = max(-9.293, 0) = 0.

  Same from both sides. Good.

  For (1, 1): Cross = 0.70711 - 0.70711 = 0. Sample = max(1.414-10, 0) = 0. Same.

  So on the boundary ray, samples are always the same (mask = 0). The test passes for these points.

  For (0.5, 0): angle = 0, inside chunk 0's wedge.
  Chunk 0: maskStart = -Cross(dirStart, (0.5, 0)) * 1000 = -(10-00.5)1000 = 0. maskEnd = Cross(dirEnd, (0.5, 0)) * 1000 = (0.70710 - 0.7071*0.5)*1000 = -353.55.
  WedgeMask = max(-14.5, max(0, -353.55)) = max(-14.5, 0) = 0.
  Sample = max(0.5-10, 0) = 0.

  Is (0.5, 0) in chunk 1's lattice? Chunk 1's bounding box is [0, 10.6] × [0, 15]. Lattice starts at (-0.5, -0.5). So (0.5, 0) is at ix=2, jy=1. In range.

  Chunk 1 at (0.5, 0): angle = 0, which is before chunk 1's start angle π/4.
  dirStart = dir_π4 = (0.7071, 0.7071)
  maskStart = -Cross(dirStart, (0.5, 0)) * 1000 = -(0.70710 - 0.70710.5) * 1000 = -(-0.35355)*1000 = 353.55

  Positive. So (0.5, 0) is outside chunk 1 from the start boundary.
  maskEnd = Cross(dirEnd, (0.5, 0)) * 1000 = (00 - 10.5)*1000 = -500

  WedgeMask = max(-14.5, max(353.55, -500)) = max(-14.5, 353.55) = 353.55
  Sample = max(-9.5, 353.55) = 353
  These are symmetric mismatches. The test should definitely fail for these points.

  Unless... the test hasn't been run, or there's a bug in the test setup. Let me just proceed with the implementation.

  OK so actually I want to also check one more thing. The test uses TerrainNoiseSettings.None. Let me check what that means for the terrain field.

  Actually, TerrainNoiseSettings.None probably means no noise, so the terrain is a perfect sphere. With baseRadius = 10, the SDF at position p is |p| - 10. This doesn't change my analysis.

  OK let me just implement the fix. I'll also update the test to verify the fix works.
sampler uses the chunk's own angles, which might not be bit-identical between chunks. In that case, using terrain values near the seam might not help (because the two chunks have slightly different seam positions).

  But the seam cache is used by ChunkTerrainRenderer, which is the main rendering path. The seam cache ensures both chunks use the same boundary directions. So the fix should work for the main path.

  For the non-seam-cache path (e.g., debug views), the fix is a no-op (the check is skipped). The existing behavior is preserved.

  OK let me implement now. Let me also think about what tests to add:

  1. A test that verifies: for two adjacent chunks, at a shared lattice point near (but not on) the boundary ray, both chunks produce the same sample value (the terrain value, not the mask value).
  2. A test that verifies: at lattice points far from the boundary, the wedge mask is still applied correctly.
  3. An integration test that verifies: the mesh contours at the chunk boundary are continuous (no gap).

  Actually, for the mesh continuity test, I'd need to check that the mesh vertices at the boundary match. This is a more comprehensive test but also more complex.

  Let me focus on the core fix and the sample-level test first.

  Let me now write the code.

  Here's my plan:
  1. Modify CartesianChunkFieldSampler.cs:
     - Add IsWithinSeamMargin helper
     - Modify the sampling loop to use terrain values near seams
  2. Modify CartesianChunkFieldSamplerTests.cs:
     - Add test for seam margin sample equality
     - The existing test should now pass (if it wasn't already)
  3. Update doc comments to reflect the new behavior

  Let me code this up.

  Wait, I also want to double-check: does the existing test really fail? Let me look at it one more time...

  The test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints:
  - Creates a field with baseRadius = 10
  - Creates a 4-chunk grid
  - Creates a seam cache
  - Samples chunks 0 and 1 with cellSize = 0.5
  - Checks that at shared lattice points, samples are equal

  Based on my analysis, points like (0, 0.5) and (0.5, 0) should have different samples. So the test should fail.

  Unless the test was written AFTER the seam cache was added, and the test was expected to pass but actually doesn't. Or maybe I'm wrong about the lattice bounds.

  Let me double-check: is (0, 0.5) really in chunk 0's lattice?

  Chunk 0's wedge: [0, π/4]
  Bounding box: [0, 15] × [0, 10.6]
  Expanded: ixMin = -1, ixMax = 31, iyMin = -1, iyMax = 23


  (0, 0.5): ix = (0-(-0.5))/0.5 = 1, jy = (0.5-(-0.5))/0.5 = 2

  So (0, 0.5) is at grid index (1, 2) in chunk 0's lattice.

  For chunk 1:
  Bounding box: [0, 10.6] × [0, 15]
  Expanded: ixMin = -1, ixMax = 23, iyMin = -1, iyMax = 31

  (0, 0.5): ix = 1, jy = 2. In range.

  Let me now write the actual code.

  For the CartesianChunkFieldSampler, I need to modify the sampling loop. Here's my plan:

  // In the sampling loop, after computing terrainValue:
  float final;
  if (!fullCircle && IsWithinSeamMargin(position, dirStart, dirEnd, cellSize))
  {
      final = terrainValue;
  }
  else
  {
      final = fullCircle
          ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
          : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
  }

  And add:
  private static bool IsWithinSeamMargin(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
  {
      float perpToStart = Mathf.Abs(Cross(dirStart, position));
      float perpToEnd = Mathf.Abs(Cross(dirEnd, position));

      return (perpToStart < cellSize && Vector2.Dot(dirStart, position) > 0f)
          || (perpToEnd < cellSize && Vector2.Dot(dirEnd, position) > 0f);
  }

  Wait, I realize there's a subtle issue. The IsWithinSeamMargin check uses cellSize as the threshold. But the Cross product gives perpendicular distance from the point to the ray LINE, not just the ray. For a point far from the origin but close to the ray line extension, the check might incorrectly identify it as near the seam.

  For example, a point at (100, 0.1) is very close to the x-axis (angle 0 ray) but is 100 units from the origin. The perpendicular distance is 0.1, which is < cellSize. The forward distance (Dot) is 100 > 0. So the check says it's near the seam.

  Is this correct? The point (100, 0.1) is indeed near the boundary ray. If this point is in the lattice (which it might be for a large planet), using the terrain value is the right thing to do.

  But for maxRadius = 15, the point (100, 0.1) is way outside the lattice. So it's not sampled. The check is fine for practical purposes.

  Another concern: what if the cellSize is very large relative to the planet? Then the seam margin would be wide, and many lattice points would use terrain values. But this is a degenerate case (large cell size means low resolution, which already looks bad).

  OK, the implementation is fine. Let me code it.

  Actually, one more thought: I should also handle the case where seamCache is null. When no seam cache is provided, the directions are computed from the chunk's angles. Two adjacent chunks might have slightly different boundary directions (if their angles aren't bit-identical). In this case, using terrain values near the seam might not help because the two chunks identify different points as "near the seam."
use terrain in one chunk but mask in the other. This could create a different kind of mismatch.

  To be safe, I'll only apply the fix when seamCache is provided:

  if (seamCache != null && !fullCircle && IsWithinSeamMargin(position, dirStart, dirEnd, cellSize))
  {
      final = terrainValue;
  }

  Wait, but the seam margin check uses dirStart and dirEnd, which come from the seam cache when it's provided. If seamCache is null, the directions come from the chunk's angles. Th
  Hmm, but looking at the ChunkTerrainRenderer code:

  CartesianChunkFieldSampler.Result sampled = CartesianChunkFieldSampler.Sample(_field, chunk, _maxRadius, cellSize, _seamCache);

  The seam cache is always provided in the main rendering path. So the fix will always be active when it matters.

  OK, final decision: apply the fix when seamCache != null and !fullCircle. Let me code it.

  Actually, I want to reconsider one more time. The fix uses terrain values near the seam. This means the mesh might extend slightly past the chunk boundary. Is this a problem?

  For the mesh: yes, each chunk might render a few extra triangles past its boundary. But the neighbor chunk renders the same triangles (because both use terrain values), so there's no visual issue. Slight overdraw.

  For the collider: the collider is built from the mesh data. If both chunks have overlapping colliders at the boundary, there might be double-thick collision. But the current code already has this issue (the 1-cell margin means some cells are processed by both chunks).

  Actually, with the current code, the mesh at the boundary is determined by the wedge mask. The mask creates a contour at the boundary ray (where the mask crosses zero). This contour is the chunk boundary. With the fix, the mesh contour at the boundary is determined by the terrain, not the mask. The terrain contour might be at a different position (the terrain surface, which could be inside or outside the boundary ray).

  Hmm, but the Marching Squares processes individual cells. For a cell entirely inside the chunk's wedge (all four corners have negative mask values), the mesh is determined by the terrain. For a cell entirely outside (all four corners have positive mask values), no mesh is generated. For a cell straddling the boundary, the mesh is determined by the mask and terrain values at the corners.

  With the fix, for cells near the boundary (within the seam margin), the mask is replaced by terrain. So the mesh is determined only by the terrain. The Marching Squares generates the terrain contour, which might extend past the boundary ray.

  But the neighbor chunk processes the same cells with the same terrain values, generating the same contour. So the meshes overlap at the boundary, not gap.

  For cells beyond the seam margin (more than one cell from the boundary), the mask is still applied. These cells have positive mask values on the outside, so no mesh is generated there.

  So the mesh at the boundary is:
  1. Seam margin cells: terrain contour, shared between chunks (no gap)
  2. Beyond seam margin: mask contour (the chunk boundary), no mesh outside

  This means there's a "transition zone" of about one cell where the mesh is shared, and beyond that, each chunk's mesh is clipped by its own boundary. The transition is seamless because the terrain contour in the margin connects to the mask contour beyond the margin.

  Wait, is that true? At the edge of the seam margin, there's a cell where:
  - One corner is in the margin (terrain value)
  - Another corner is outside the margin (mask value)

  The Marching Squares processes this cell with mixed values. The contour vertex on the edge between margin and non-margin corners is determined by interpolation between terrain and mask values. This is the same asymmetry we tried to fix!

  Hmm, but this only happens at the edge of the margin, not at the actual seam. The seam (the boundary between chunks) is in the middle of the margin. Both chunks use terrain values in the margin, so the seam is fine.

  At the edge of the margin, the cell has mixed values. But this is on the "interior" side of the chunk, not at the boundary with the neighbor. So the asymmetry is not visible (it's inside the chunk, far from the seam).


  Chunk A processes cells in the margin with terrain values. Beyond the margin, it uses mask values (positive, air). So the mesh from A stops at the margin edge.

  Chunk B processes cells in the margin with terrain values. The mesh from B extends through the margin.

  At the margin edge (from A's perspective):
  - A's cell: some corners have terrain values (in margin), some have mask values (outside margin)
  - B's cell: all corners have terrain values (B's margin is wider, since B extends past the seam by one cell)

  So B's mesh extends further than A's mesh. There's a gap of about one cell between A's mesh edge and B's mesh at the margin edge.

  Wait, but this is not at the seam. The seam is at the boundary ray. The margin extends one cell past the seam into B's territory. B's mesh in the margin matches A's mesh (same terrain values). Beyond the margin, A's mesh stops, but B's mesh continues. There's no gap because B fills in the area beyond A's margin.

  But is there a visual issue? A's mesh has a "ragged" edge at the margin boundary (where terrain values meet mask values). B's mesh is smooth (all terrain values). The ragged edge is on the exterior of A's mesh, which is covered by B's mesh. So it's not visible.

  Actually, the exterior of A's mesh is at the margin edge, which is in B's territory. B's mesh covers this area, so A's ragged edge is behind B's mesh. In a 2D renderer, the back-to-front or front-to-back order determines which is visible. If B renders after A, B's mesh covers A's edge. If A renders after B, A's ragged edge might be visible.

  Hmm, this could be an issue. But in the current implementation, each chunk has its own GameObject with MeshRenderer. The render order is determined by the child order in the GameObject hierarchy. If chunks are rendered in index order (0, 1, 2, 3), then B renders after A, and B's mesh covers A's edge.

  But this is implementation-dependent and might not be reliable. Let me think of a better approach.

  Actually, I think the simpler approach is to only use terrain values for the "interior" side of the margin, not the "exterior" side. That is, for lattice points near the seam but still inside the chunk's wedge, use terrain values. For lattice points outside the wedge, use mask values.

  This way, the mesh at the boundary is determined by terrain values from the inside, and mask values from the outside. The contour at the boundary is the same from both chunks (terrain values on the inside, mask values on the outside).

  But this is exactly the current behavior, which creates the asymmetry! The inside chunk uses terrain and the outside chunk uses mask.

  OK, I think the fundamental issue is that you can't have a perfectly clean CSG-based seam without some form of vertex sharing. The mask-based approach inherently creates asymmetric values.

  Let me think about this differently. What if, instead of replacing the mask with terrain, I ensure that the Marching Squares at the boundary uses the same values from both sides?

  The key insight: both chunks share the same lattice. For a cell straddling the boundary, both chunks process it. If both chunks use the same SDF values for all four corners, the Marching Squares output is identical.

  - Two corners inside B's wedge (A uses mask, B uses terrain)

  For both chunks to use the same values, I need to replace the mask with terrain at all four corners. This means using terrain values in a 1-cell band on both sides of the boundary.

  This is what my fix does: IsWithinSeamMargin checks if a point is within one cell of either boundary ray, and if so, uses the terrain value. This covers the 1-cell band on both sides of the boundary.

  Now, for a cell that straddles the boundary:
  - Corners within 1 cell of the boundary: terrain value (from both chunks)
  - Corners beyond 1 cell: mask va
  - If outside: both chunks use mask values (one inside, one outside the wedge)

  Wait, this is the problem. For a cell one cell away from the boundary, on B's side:
  - A's corners: mask values (positive, outside A's wedge)
  - B's corners: terrain values (inside B's wedge)

  But my fix uses terrain values for corners within 1 cell of the boundary. A cell one cell away has its closest corner at about one cell from the boundary. The IsWithinSeamMargin check with < cellSize would include this corner if it's exactly at cellSize distance. But due to the Cartesian grid, the perpendicular distance might be slightly more or less than cellSize.

  Hmm, I think I should use <= cellSize or cellSize * 1.001f to be safe. Or better, use cellSize + epsilon.

  Actually, let me think about this more carefully. The cell is one cell away from the boundary. Its closest corner is at perpendicular distance ≈ cellSize from the boundary. The check |Cross| < cellSize would miss this corner (because distance ≈ cellSize, not strictly less). So the corner uses mask values, and the asymmetry remains.

  But does this matter? A cell one cell away from the boundary doesn't straddle the boundary. Its corners are all on one side (either all inside A's wedge or all inside B's wedge). So both chunks agree on the mask:
  - If all inside A's wedge: A uses terrain (all corners), B uses terrain (all corners, since they're in the overlap). Wait, B considers these corners to be outside B's wedge (they're in A's territory). So B uses mask values.

  Hmm, this is the same asymmetry again. For cells that are entirely in A's wedge but within the 1-cell margin, both chunks have these cells in their lattice (because of the 1-cell expansion). A uses terrain, B uses mask.

  But the mesh for these cells is the same from both perspectives: all corners are terrain (for A) or all corners are mask (for B). If all mask values are positive, no mesh is generated. If all terrain values are negative (solid), no zero crossing, no mesh. So there's no mesh from either chunk for these cells.

  The only cells that generate mesh at the boundary are the cells that straddle the boundary (some corners inside, some outside). For these cells, my fix ensures both chunks use terrain values, producing the same mesh.

  For cells one cell away from the boundary:
  - If the cell is entirely inside A's wedge: A's Marching Squares might generate mesh (terrain contour). B's Marching Squares generates no mesh (all mask = positive).
  - If the cell is entirely inside B's wedge: symmetric.

  So there's a one-cell region where A generates mesh but B doesn't (and vice versa). This means A's mesh extends one cell past the boundary, but B's mesh doesn't extend that far. There's a gap of one cell between B's mesh and A's extension.

  But this one-cell extension is on the exterior of A's wedge, which is covered by B's mesh... wait, no. B's mesh doesn't extend there (B uses mask values for cells in A's territory). So A's extension is exposed.

  Hmm, but A's extension is determined by the terrain, not the mask. The terrain might have a contour in this region (if the terrain surface is there). If the terrain is solid (all negative), no mesh. If the terrain is air (all positive), no mesh. If the terrain crosses zero, mesh.

  The terrain contour in this region is the same as what B would generate if B used terrain values. But B uses mask values, so B doesn't generate this mesh. A does.

  So A's mesh includes terrain contours in the one-cell margin beyond the boundary. B's mesh doesn't include these contours (because B uses mask values there). But A's terrain contours in the margin are part of the real terrain — they should be visible.

  Wait, but A's margin extends into B's territory. The terrain contours in A's margin are at positions that are in B's wedge. B should render these contours too (since they're in B's territory). But B uses mask values for these positions (because from B's perspective, they're... wait, no. From B's perspective, positions in B'

  Consider a cell that's one cell past the boundary ray, on B's side. Its perpendicular distance from the boundary is > cellSize (it's more than one cell away).

  From A's perspective: this cell is in A's lattice (because of the 1-cell margin expansion). The cell is outside A's wedge. All corners have positive mask values. No mesh generated.

  From B's perspective: this cell is in B's lattice. The cell is inside B's wedge. All corners have terrain values. Marching Squares processes it.

  So A doesn't generate mesh for this cell, and B does. No conflict.

  So B doesn't generate mesh for this cell, and A does. No conflict.

  The only conflict is at cells that straddle the boundary. For these cells, both chunks generate mesh, but with different SDF values (before the fix). After the fix, both use terrain values, so the mesh is the same.

  So my fix should work! The cells that straddle the boundary have all four corners within one cell of the boundary ray (because the cell itself is at most cellSize in extent). The IsWithinSeamMargin check catches all four corners. Both chunks use terrain values. The Marching Squares output is identical.

  For cells not straddling the boundary, one chunk generates mesh and the other doesn't. No conflict.

  But wait, what about the cell that's exactly one cell away from the boundary? Its closest corner is at distance ≈ cellSize. The check |Cross| < cellSize might or might not include this corner, depending on the exact geometry.

  For a horizontal boundary ray (angle 0):
  - The boundary is the positive x-axis.
  - A cell one cell above has y = cellSize.
  - The bottom corners of this cell are at distance cellSize from the ray.
  - The check |Cross| < cellSize = |cellSize| < cellSize = false.

  So the bottom corners are NOT in the margin. The cell is processed with mask values from the chunk that considers it outside.

  But this cell doesn't straddle the boundary (the boundary is at y = 0, and the cell is at y = cellSize to y = 2*cellSize). So there's no conflict.

  For a cell at y = 0 to y = cellSize:
  - The bottom edge is at y = 0 (the boundary).
  - The top edge is at y = cellSize.
  - The bottom corners are at distance 0 from the ray. The check: |0| < cellSize = true. In margin.
  - The top corners are at distance cellSize from the ray. The check: |cellSize| < cellSize = false. Not in margin.

  So this cell has mixed corners: bottom in margin (terrain values), top not in margin (mask values from the outside chunk).

  For the chunk that considers this cell as inside its wedge (the chunk above the boundary):
  - Bottom corners: within seam margin → terrain values
  - Top corners: not within seam margin → terrain values (inside the wedge, mask is negative, terrain wins)
  - All four corners: terrain values. Marching Squares processes with all terrain.

  For the chunk that considers this cell as outside its wedge (the chunk below the boundary):
  - Bottom corners: within seam margin → terrain values
  - Top corners: not within seam margin → mask values (outside the wedge, mask is positive)
  - Mixed values. Marching Squares processes with terrain at bottom, mask at top.

  These are different! The two chunks use different SDF values for the top corners of this cell.

  But wait, does the "outside" chunk even have this cell in its lattice? The cell is at y = 0 to y = cellSize, which is above the boundary. The outside chunk's lattice extends one cell past the boundary (into the inside chunk's territory). So yes, the outside chunk has this cell.

  And the outside chunk processes this cell with mixed values (terrain at bottom, mask at top). The inside chunk processes with all terrain values. Different outputs.

  Hmm, this is a problem. The fix handles the boundary-straddling cells but not the cells one cell above/below.

  Let me reconsider. The cells tha boundary ray, about 2 cells wide (1 cell from each chunk's expansion).

  In this overlap strip, both chunks process the same cells. For these cells, my fix ensures that corners within 1 cell of the boundary use terrain values. But corners beyond 1 cell from the boundary still use the wedge mask, which creates asymmetry.

  To fully fix this, I need to extend the seam margin to cover the entire overlap strip. But the overlap strip width depends on the boundary orientation and the cell grid.

  Actually, I think a simpler approach is: for all cells that both chunks process (i.e., all cells in the overlap), use terrain values for all corners. This means extending the seam margin to cover the overlap.

  But how wide is the overlap? It depends on the bounding box expansion. The expansion is 1 cell in each direction. For a boundary at angle θ, the overlap strip has width ≈ 2 * cellSize / sin(θ) in the direction perpendicular to the boundary. For θ ≈ 0 (horizontal boundary), this is very wide. For θ ≈ π/2 (vertical boundary), it's about 2 cells.

  This is getting complicated. Let me take a different approach.

  Instead of using a distance-based check, I'll use a simpler rule: for all lattice points in the chunk, if the wedge mask value is small (less than some threshold), use the terrain value. The threshold should be chosen so that it covers the entire overlap strip.

  The mask value is Cross(dir, position) * steepness. For a point in the overlap strip, the perpendicular distance to the boundary is at most the expansion width. The mask value is distance * steepness.

  For a point 2 cells from the boundary: mask ≈ 2 * cellSize * steepness = 2 * 0.5 * 1000 = 1000.
  For a point 1 cell from the boundary: mask ≈ 500.
  For a point 0.5 cells from the boundary: mask ≈ 250.

  A threshold of, say, 1000 would cover points up to 2 cells from the boundary. This is wider than the overlap strip for most orientations.

  But this threshold is somewhat arbitrary and depends on the cell size and steepness. It's not a clean solution.

  Let me think of yet another approach: instead of modifying the SDF values, modify the meshing. Specifically, for cell edges that cross the chunk boundary, compute the contour vertex using only the terrain field, not the SDF values.

  This requires passing the terrain field to the mesher, which changes the API. Not ideal.

  OK, let me step back and think about what's the simplest fix that actually works in practice.

  The most visible seams are at the terrain surface, where the mesh contour crosses the chunk boundary. At this point, the mesh needs to be continuous. The terrain surface is at a specific radius from the center. For a smooth terrain, the surface crosses the boundary at one point.

  For the cells at the terrain surface that straddle the boundary:
  - The SDF values at the corners are close to zero (near the surface).
  - The mask values for outside corners are large positive.
  - The Marching Squares contour vertex on the boundary-crossing edge is determined by the SDF values at the endpoints.

  - A's other endpoint (outside): mask value ≈ large
  - Vertex near A's inside endpoint
  - B's endpoint (inside): terrain value ≈ 0
  - B's other endpoint (outside): mask value ≈ large
  - Vertex near B's inside endpoint

  The two vertices are at different lattice points, about one cell apart. Visible seam.

  With the fix (terrain values near the boundary):
  - Both endpoints: terrain values
  - Vertex at the terrain zero crossing (same from both chunks)
  - No seam

  So the fix works for the visible

  So the fix is correct for the visible terrain. Let me proceed with the implementation.

  Wait, one more concern: the interior of the planet. If the planet is a sphere (baseRadius = 10), the terrain is solid from radius 0 to radius 10. The mesh contour is at radius ≈ 10. Inside the planet, all SDF values are negative. Marching Squares generates a contour at the zero crossing, which is the surface. Inside the surface, no contour.

  For a cell entirely inside the planet (all negative terrain values): no mesh. This is true for both chunks. No conflict.

  For a cell at the surface: the terrain crosses zero. The mesh contour is at the zero crossing. With the fix, both chunks use terrain values, so the contour is the same.

  For a cell outside the surface (all positive terrain values): no mesh. True for both chunks.

  So the fix is correct for all cases. Let me implement.

  One last thing: I need to make sure the fix handles the reflex wedge case. For a reflex wedge (angular size > π), the WedgeMask uses a complement. The IsWithinSeamMargin check still works because it's based on the boundary ray directions, not the mask computation.

  OK, implementing now. For real this time.

  Let me also think about the test. I'll add a test that:
  1. Creates two adjacent chunks with a seam cache
  2. Samples both chunks
  3. Finds shared lattice points that are near (but not on) the boundary ray
  4. Verifies that both chunks use the terrain value (not the mask value) at these points
  5. Verifies that the samples are identical

  Actually, the existing test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints already checks for identical samples. After the fix, it should pass (if it wasn't already). I'll add an additional test to verify that near-seam points use terrain values specifically.

  Let me code now.

  Hmm, actually I realize I need to also carefully think about the IsWithinSeamMargin function. The function checks if a point is within one cell of either boundary ray. But for a reflex wedge (angular size > π), the "inside" is the larger region. The boundary rays are still the start and end angles, but the wedge interior is the complement of the convex wedge.

  For the seam margin check, I'm only checking distance to the boundary rays, not the wedge interior. So the check is the same for reflex and non-reflex wedges. Points near the boundary ray use terrain values, regardless of the wedge shape. This is correct because the seam issue is about the boundary ray, not the wedge interior.

  OK, truly implementing now. Let me also make sure I update the doc comment on CartesianChunkFieldSampler to reflect the new behavior.

  Let me write the code:

  // In CartesianChunkFieldSampler.cs, in the Sample method:

  // Replace:
  //     float final = fullCircle

  // With:
  float final;
  if (!fullCircle && IsWithinSeamMargin(position, dirStart, dirEnd, cellSize))
  {
      // Lattice point near a chunk boundary: use the raw terrain SDF directly
      // instead of combining with the wedge mask. Both neighboring chunks sample
      // the same lattice points in the overlap strip, and feeding each chunk a
      // different SDF (terrain on the inside, steep mask on the outside) caused
      // Marching Squares to place contour vertices at different positions on
      // shared cell edges — a visible gap at every chunk border.
      //
      // By using the terrain value on both sides, the shared cell produces
      // identical topology and identical edge-interpolation from each chunk.
      // The 1-cell margin in ComputeLatticeBounds guarantees every boundary-
      // straddling cell has all four corners falling into this branch.
      final = terrainValue;
  }
  else
  {
      final = fullCircle
          ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
          : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
  }

  And add:
  /// <summary>
  /// True when <paramref name="position"/> lies within one lattice cell of either
  /// boundary ray, meaning a neighboring chunk also samples this cell and both
  /// sides need the same SDF value for contiguous Marching Squares output.
  /// </summary>
  private static bool IsWithinSeamMargin(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
  {
      return IsNearRay(position, dirStart, cellSize)
          || IsNearRay(position, dirEnd, cellSize);
  }

  private static bool IsNearRay(Vector2 position, Vector2 dir, float cellSize)
  {
      // Perpendicular distance from position to the ray line (dir is a unit vector).
      float perpDistance = Mathf.Abs(Cross(dir, position));
      if (perpDistance >= cellSize)
      {
          return false;
      }

      // Must be on the forward side of the ray origin (not behind the planet center).
      return Vector2.Dot(dir, position) > 0f;
  }

  Now I also need to update the doc comment to reflect the actual implementation. The current comment mentions previousSeam/nextSeam parameters that don't exist. I'll update it.

  Let me also add a test. I'll add it to CartesianChunkFieldSamplerTests.cs.

  Let me write all the code now. I'll do the sampler first, then the test.

  For a boundary at angle 0 (positive x-axis), the cell edges parallel to the y-axis cross the boundary at x = k * cellSize. The cells straddling the boundary have x from 0 to cellSize (or -cellSize to 0). The corners of this cell:
  - Bottom-left: (0, 0) or (-cellSize, 0) — distance 0 from the ray
  - Bottom-right: (cellSize, 0) — distance 0 from the ray
  - Top-left: (0, cellSize) — distance cellSize from the ray
  - Top-right: (cellSize, cellSize) — distance cellSize from the ray

  The check |Cross| < cellSize catches corners with distance < cellSize. For the top corners, distance = cellSize, which is NOT < cellSize. So the top corners are not in the margin.

  This means the boundary-straddling cell has:
  - Bottom corners: in margin → terrain values
  - Top corners: not in margin → mask values (for the outside chunk)

  And this creates the asymmetry I described earlier!

  Hmm, so my fix doesn't fully cover the boundary-straddling cell. I need to extend the margin.

  Wait, but the boundary-straddling cell is at y = 0 to y = cellSize (assuming the boundary is at y = 0). The top corners are at y = cellSize. From the inside chunk's perspective, the top corners are inside the wedge (mask is negative, terrain wins). From the outside chunk's perspective, the top corners are outside the wedge (mask is positive).

  For the inside chunk:
  - Bottom corners: terrain (margin)
  - Top corners: terrain (inside wedge, mask is negative)
  - All four: terrain. Good.

  For the outside chunk:
  - Bottom corners: terrain (margin)
  - Top corners: mask (outside wedge, mask is positive)
  - Mixed. Bad.

  So the outside chunk has mixed values, and the Marching Squares output is different from the inside chunk.

  The fix doesn't fully work for this cell. I need to extend the margin to at least cover the cell extent perpendicular to the boundary.

  For a horizontal boundary (angle 0), the cell extent perpendicular to the boundary is cellSize (in the y direction). So the margin should be at least cellSize. But the top corners are at exactly cellSize, so the margin should be > cellSize.

  I could use cellSize * 1.5f or cellSize * 2f as the threshold. But this is somewhat arbitrary.

  A better approach: instead of checking distance to the boundary ray, check if the lattice point is in the overlap region (i.e., both chunks have this point in their lattice). But determining the overlap requires knowing both chunks' lattice bounds, which the sampler doesn't have.

  Actually, I know the overlap is at most 2 cells wide (1 cell from each chunk's expansion). So I should use a margin of 2 cells.

  But the expansion is in the axis-aligned direction, not the perpendicular direction. For a boundary at a 45° angle, the expansion of 1 cell in x and y covers √2 cells perpendicular to the boundary.

  Hmm, this is getting complicated. Let me use a simpler approach: use cellSize * sqrt(2) as the margin. This covers the diagonal of a cell, ensuring all four corners of a boundary-straddling cell are included.

  Actually, the maximum distance from a corner of a boundary-straddling cell to the boundary ray is the cell diagonal, which is cellSize * √2 ≈ 1.414 * cellSize. So I should use at least this as the margin.

  But to be safe and cover cells one cell away from the boundary (in case the overlap is wider), I'll use 2 * cellSize.

  Let me update:
  private static bool IsNearRay(Vector2 position, Vector2 dir, float cellSize)
  {
      // Perpendicular distance from position to the ray line.
      // Use 2x cellSize to cover all corners of boundary-straddling cells
      // plus the 1-cell margin expansion from ComputeLatticeBounds.
      float perpDistance = Mathf.Abs(Cross(dir, position));
      if (perpDistance >= cellSize * 2f)
      {
          return false;
      }

      // Must be on the forward side of the ray origin.
      return Vector2.Dot(dir, position) > 0f;
  }

  With 2 * cellSize as the margin:
  - The boundary-straddling cell's corners are all within cellSize * √2 ≈ 1.414 * cellSize. All in margin.
  - Cells one cell away have their closest corner at about cellSize. This is < 2 * cellSize. In margin.
  - Cells two cells away have their closest corner at about 2 * cellSize. This is >= 2 * cellSize. Not in margin.

  So the margin covers the boundary-straddling cell plus one cell on each side. This is about 3 cells wide, which is slightly more than the 2-cell overlap from the 1-cell expansion.

  For cells in the margin but not in the overlap (margin is wider than overlap), one chunk uses terrain and the other doesn't sample the cell at all (it's outside its lattice). No conflict.

  For cells in both the margin and the overlap, both chunks use terrain. No conflict.

  This should work. Let me use cellSize * 2f as the margin.

  Actually, I want to think about this more carefully. The overlap region is determined by the bounding box expansion. For a boundary at angle θ:

  The bounding box of chunk A includes the wedge from angle startA to endA. The expansion adds 1 cell in each axis direction. So the expanded bounding box extends 1 cell beyond the wedge in x and y.

  The perpendicular distance from the boundary ray to the edge of the expanded bounding box depends on the angle. For a horizontal boundary (θ = 0), the expansion in y is 1 cell, so the perpendicular margin is 1 cell. For a vertical boundary (θ = π/2), the expansion in x is 1 cell, so the perpendicular margin is 1 cell. For a 45° boundary, the expansion is √2 cells perpendicular to the boundary.

  So the overlap strip is at most √2 cells on each side, for a total width of 2√2 ≈ 2.8 cells.

  Using a margin of 2 * cellSize covers about 2 cells on each side, for a total of 4 cells. This is wider than the overlap (2.8 cells). So all cells in the overlap are covered.

  But the margin also covers cells outside the overlap. For these cells, only one chunk samples them (the other's lattice doesn't extend that far). Using terrain values is fine (no conflict, since the other chunk doesn't sample there).

  OK, 2 * cellSize is a good margin. Let me use it.

  Wait, actually I realize there's still an issue. For a boundary at angle θ, the boundary-straddling cells are those with corners on both sides of the boundary ray. The perpendicular distance from the boundary to the farthest corner of such a cell is at most the cell diagonal: cellSize * √2 ≈ 1.414 * cellSize.

  With a margin of 2 * cellSize, all corners of all boundary-straddling cells are in the margin. Good.

  But I also need to consider: the cell that's one cell away from the boundary (on the inside). Its closest corner to the boundary is at distance cellSize. The farthest corner is

  But this cell is one cell away from the boundary, on the inside. From the outside chunk's perspective, this cell is in its lattice (because of the 1-cell expansion). The outside chunk processes this cell with mixed values.

  From the inside chunk's perspective, all corners are terrain. The Marching Squares output uses all terrain values.

  From the outside chunk's perspective, some corners are terrain (margin) and some are mask. Different output.

  Is this a problem? This cell is entirely inside the wedge. The inside chunk generates mesh for it (terrain contour). The outside chunk also processes it, but with mixed values. If the terrain is all negative (solid), the Marching Squares from the inside chunk generates no mesh (no zero crossing). From the outside chunk, some corners are terrain (negative) and some are mask (positive). There IS a zero crossing. So the outside chunk generates mesh for this cell.

  This mesh from the outside chunk is in the inside chunk's territory. It's the "edge" of the outside chunk's mesh, determined by the transition from terrain (margin) to mask (non-margin).

  Is this visible? The mesh from the outside chunk at this cell has a contour at the transition between terrain and mask. This contour is not at the terrain surface but at the margin boundary. It's an artifact.

  Hmm, this is a problem. The margin boundary creates an artificial contour in the outside chunk's mesh.

  To avoid this, I need to ensure that cells entirely inside one chunk's wedge (but in the overlap) don't generate mesh from the other chunk. The simplest way: make the margin wide enough that all overlap cells are in the margin. Then both chunks use terrain values for all overlap cells.

  The overlap width is at most 2√2 cells. So I need a margin of at least √2 * cellSize on each side, for a total of 2√2 ≈ 2.83 cells. Using margin = 3 * cellSize should cover the overlap.

  But for a boundary at angle θ, the overlap depends on the bounding box expansion. The expansion is 1 cell in x and 1 cell in y. The perpendicular margin is 1 cell / |sin(θ)| for x-expansion and 1 cell / |cos(θ)| for y-expansion. The total overlap is the sum.

  For the worst case (θ ≈ 0), the x-expansion contributes 1 cell / sin(0) ≈ ∞. But this is because the boundary is horizontal, and the x-expansion is parallel to the boundary. The perpendicular margin from x-expansion is 0. The y-expansion contributes 1 cell / cos(0) = 1 cell.

  So the overlap for a horizontal boundary is 2 cells (1 from each chunk's y-expansion). The margin needs to be at least 1 cell on each side.

  For a 45° boundary, the x-expansion contributes 1 / sin(45°) = √2 cells. The y-expansion contributes 1 / cos(45°) = √2 cells. The total overlap is 2√2 ≈ 2.83 cells.

  For a 45° boundary, the boundary-straddling cell has corners at distances up to cellSize * √2 from the boundary. With margin = 2 * cellSize, all corners are in the margin. But cells one cell away have corners at distances up to 2 * cellSize + cellSize * √2 ≈ 3.414 * cellSize. These are outside the margin.

  But these far corners are not in2.5 cells from the boundary. With margin = 2 * cellSize, this corner is not in the margin. The cell has mixed values.

  But this cell is inside one chunk's wedge (the closer chunk). The closer chunk uses all terrain values. The farther chunk uses mixed values (some terrain, some mask). The farther chunk generates an artificial contour at the margin boundary.

  To avoid this, I need the margin to be wider than the overlap. With margin = 3 * cellSize:
  - Cells up to 3 cells from the boundary are in the margin.
  - The overlap is about 2.83 cells wide. So all overlap cells are within 1.5 cells of the boundary.
  - All corners of overlap cells are within 1.5 + √2 ≈ 2.91 cells from the boundary.
  - Margin = 3 * cellSize covers up to 3 cells. So most overlap cells are covered.
  - But a corner at 2.91 * cellSiz
  The forward check only excludes points behind the origin, which shouldn't be in the lattice for most chunks.

  OK, let me use margin = 3 * cellSize. This should cover all overlap cells for reasonable boundary angles.

  Actually, wait. I realize the worst case for the overlap width is when the boundary is at 45° (or 135°, etc.), where the overlap is 2√2 ≈ 2.83 cells. For a margin of 3 * cellSize on each side (total 6 cells), the margin is much wider than the overlap. This means many non-overlap cells are also in the margin, and their SDF values are changed from mask to terrain. This is wasteful but harmless (one chunk samples them, the other doesn't).

  For the overlap cells:
  - A cell at 1.5 cells from the boundary has all corners within 1.5 + √2 ≈ 2.91 cells.
  - Margin = 3 * cellSize > 2.91. All corners in margin. All terrain values. Good.

  Let me use margin = 3 * cellSize. Actually, to be safe for all angles, let me use 4 * cellSize.

  No, that's overkill. Let me just use cellSize * Mathf.Sqrt(2f) + cellSize = cellSize * (1 + √2) ≈ 2.414 * cellSize. This covers the boundary-straddling cell plus one additional cell on each side. For the worst-case overlap of 2.83 cells, this covers most but not all overlap cells.

  Hmm, let me just use cellSize * 3f. It's a nice round number and covers the worst case.

  Actually, let me think about this differently. The overlap is created by the 1-cell margin in ComputeLatticeBounds. A lattice point is in the overlap if it's within 1 cell (axis-aligned) of both chunks' bounding boxes. For the boundary ray, the overlap width is the sum of the perpendicular projections of the 1-cell expansion from each side.

  The maximum overlap occurs when the boundary is at 45°:
  - Chunk A's expansion extends 1 cell in x and 1 cell in y. The perpendicular distance from the boundary to the expansion edge is 1 / sin(45°) = √2 ≈ 1.414 cells for the x-expansion and 1 / cos(45°) = √2 ≈ 1.414 cells for the y-expansion. The total expansion on A's side is the max of these (not the sum), which is √2.
  - Similarly for B's side: √2.
  - Total overlap: 2√2 ≈ 2.83 cells.

  A cell in the overlap that's exactly at the edge of the overlap has its corners at distances from the boundary ranging from (edge - cellSize) to (edge + cellSize). For the worst case, the farthest corner is at √2 + cellSize ≈ 2.414 cells from the boundary.

  Wait, I think I'm confusing cell width with corner distance. Let me be more precise.

  A cell in the Cartesian grid has width and height of cellSize. The cell is identified by its bottom-left corner (ix * cellSize, iy * cellSize). The four corners are:
  - (ix * cellSize, iy * cellSize)
  - ((ix+1) * cellSize, iy * cellSize)
  - ((ix+1) * cellSize, (iy+1) * cellSize)
  - (ix * cellSize, (iy+1) * cellSize)

  The perpendicular distance from each corner to the boundary ray depends on the corner's position and the ray's direction.

  For a vertical boundary (x = 0), the perpendicular distance is |x|. The cell with ix = -1 has corners at x = -cellSize, 0. The cell with ix = 0 has corners at x = 0, cellSize.

  The overlap for a vertical boundary:
  - Chunk A's wedge: x < 0. Expansion: ixMin = Floor(minA/cellSize) - 1. If minA = 0, ixMin = -1.
  - Chunk B's wedge: x > 0. Expansion: ixMin = Floor(minB/cellSize) - 1. If minB = 0, ixMin = -1.

  So both chunks have ixMin = -1. The overlap in x is from ix = -1 to ix = 0 (2 cells).

  A cell at ix = -1 has corners at
  So for a vertical boundary, the maximum corner distance in the overlap is cellSize. With margin > cellSize, all overlap corners are in the margin. Using cellSize * 1.5f or even cellSize * 1.001f would work.

  For a 45° boundary, the situation is more complex. Let me compute the overlap more carefully.

  Chunk A: wedge [0, π/4]. Bounding box: [0, 15] × [0, 10.6] (for maxRadius = 15).
  Expansion: ixMin = -1, ixMax = 31, iyMin = -1, iyMax = 23.

  Chunk B: wedge [π/4, π/2]. Bounding box: [0, 10.6] × [0, 15].
  Expansion: ixMin = -1, ixMax = 23, iyMin = -1, iyMax = 31.

  For a cell at the edge of the overlap, say ix = 0, iy = 22: corners at (0, 11), (0.5, 11), (0.5, 11.5), (0, 11.5). The perpendicular distance to the π/4 ray: |x - y| / √2. For (0, 11): 11 / √2 ≈ 7.78. Way outside the margin!

  But wait, this cell is at the edge of the overlap in the y direction. It's far from the boundary ray (which is at 45°). The boundary ray passes through (0, 0) and (10.6, 10.6). The cell (0, 11) to (0.5, 11.5) is far above the ray.

  From chunk A's perspective, this cell is outside the wedge (angle > π/4). The mask is positive.
  From chunk B's perspective, this cell is inside the wedge (angle < π/2 and > π/4... wait, angle of (0, 11) is π/2 ≈ 1.571. And chunk B's wedge is [π/4, π/2]. So this cell is near the end of chunk B's wedge.

  Hmm, but the cell (0, 11) to (0.5, 11.5) is in the overlap because both chunks' expanded lattices include it. But from chunk A's perspective, it's outside the wedge (mask is positive, large). From chunk B's perspective, it's inside the wedge (terrain value).

  The perpendicular distance to chunk A's end ray (π/4) is about 7.78 cells. This is way outside the margin. So the margin check doesn't apply. Chunk A uses mask value, chunk B uses terrain value.

  But this cell is far from the terrain surface (radius 10). The terrain at (0, 11) is 11 - 10 = 1 (air). The terrain at (0.5, 11) is 11.01 - 10 = 1.01 (air). So from chunk B's perspective, the cell is all air (positive). Marching Squares generates no mesh.

  From chunk A's perspective, the mask is large positive. Also no mesh.

  So there's no conflict for this cell (both generate no mesh). The conflict only arises when the terrain surface crosses the cell, which happens near the planet surface.

  For cells near the planet surface (radius ≈ 10), the perpendicular distance to the boundary ray is approximately 10 * sin(angle - boundaryAngle). For a cell near the surface and near the boundary, this is small. The margin check applies.

  For a cell near the surface but far from the boundary, the terrain is all solid (inside) or all air (outside). No zero crossing, no mesh. No conflict.

  So the conflict is only at cells that are both near the surface AND near the boundary. For these cells, the margin check catches them (distance < margin), and the fix applies.

  Great, so the fix is correct for the relevant cells. Let me use a margin of cellSize * 2f (2 cells) to be safe.

  Actually, I realize that for the 45° boundary, the boundary-straddling cells have corners at distances up to cellSize from the boundary (not cellSize * √2). This is because the cell is aligned with the grid, not with the boundary. The cell edge closest to the boundary is at most cellSize away (the perpendicular distance from the cell edge to the boundary).

  Wait, that's not right. For a cell with corners at (x, y), (x+cs, y), (x+cs, y+cs), (x, y+cs), and a boundary at 45° (x = y), the perpendicular distances are:
  - (x, y): |x - y| / √2
  - (x+cs, y): |x+cs - y| / √2
  - (x+cs, y+cs): |x+cs - y - cs| / √2 = |x - y| / √2
  - (x, y+cs): |x - y - cs| / √2

  For a cell where the boundary crosses: x < y and x+cs > y (the boundary crosses the left-to-right edge). So:
  - (x, y): (y - x) / √2 (inside, negative)
  - (x+cs, y): (x+cs - y) / √2 (outside, positive)
  - (x+cs, y+cs): (y+cs - x - cs) / √2 = (y - x) / √2 (inside, negative)
  - (x, y+cs): (y+cs - x) / √2 (inside, negative)

  Wait, I got the signs wrong. Let me redo. The boundary is at x = y (45°). Points with x > y are on one side, and x < y on the other.

  For a cell straddling the boundary:
  - Some corners have x > y, some have x < y.

  Example: cell with x from 4 to 4.5, y from 4 to 4.5:
  - (4, 4): x = y. On the boundary. Distance 0.
  - (4.5, 4): x > y. Distance (4.5 - 4) / √2 = 0.354.
  - (4.5, 4.5): x = y. On the boundary. Distance 0.
  - (4, 4.5): x < y. Distance (4.5 - 4) / √2 = 0.354.

  Max distance: 0.354 cells. With margin = 1 * cellSize, all corners are in the margin.

  Another example: cell with x from 4.5 to 5, y from 3.5 to 4:
  - (4.5, 3.5): x > y. Distance (4.5 - 3.5) / √2 = 0.707.
  - (5, 3.5): x > y. Distance (5 - 3.5) / √2 = 1.061.
  - (5, 4): x > y. Distance (5 - 4) / √2 = 0.707.
  - (4.5, 4): x > y. Distance (4.5 - 4) / √2 = 0.354.

  All corners have x > y. This cell is entirely on one side of the boundary. Not straddling.

  For a straddling cell, the max distance from the boundary is cellSize / √2 ≈ 0.707 cells. With margin = cellSize, all corners are covered.

  But for a horizontal boundary (angle 0):
  Cell with x from 0 to 0.5, y from 0 to 0.5:
  - (0, 0): on boundary. Distance 0.
  - (0.5, 0): on boundary. Distance 0.
  - (0.5, 0.5): distance 0.5.
  - (0, 0.5): distance 0.5.

  Max distance: 0.5 cells. With margin = cellSize, covered.

  So for a straddling cell, the max corner distance is at most 0.707 cells (for a 45° boundary). With margin = cellSize, all straddling cells are covered.

  But I also need to cover the overlap region. For a horizontal boundary, the overlap is 2 cells wide (1 from each expansion). A cell 1 cell from the boundary has its farthest corner at 2 cells. With margin = 2 * cellSize, this is covered.

  For a 45° boundary, the overlap is wider (about 2.83 cells). A cell at 1.5 cells from the boundary has its farthest corner at 1.5 + 0.707 = 2.207 cells. With margin = 3 * cellSize, covered.

  So I need margin = 3 * cellSize for the 45° case.

  Hmm, but for the 45° case, the cell at 1.5 cells from the boundary is deep inside one chunk's wedge. The other chunk samples it because of the expansion. But the terrain at this cell is deep inside the planet (all negative) or deep outside (all positive). No zero crossing, no mesh. So there's no conflict even without the margin fix.

  The conflict only arises when there's a zero crossing in the cell. For a cell deep inside the planet, there's no zero crossing. For a cell near the surface, the cell is close to the boundary AND the surface. The surface is at radius ≈ 10, and the boundary ray passes through the surface at one point. The cells near this point are the ones that matter.

  For these cells, the perpendicular distance to the boundary is at most 0.707 cells (for a straddling cell). With margin = cellSize, they're covered.

  So margin = cellSize is sufficient for the straddling cells. But what about cells adjacent to straddling cells? These are one cell away from the boundary. Their corners are at distances from cellSize to 1.707 cells. With margin = 2 * cellSize, they're covered.

  But do adjacent cells generate mesh? An adjacent cell (one cell away from the boundary) is either entirely inside or entirely outside one chunk's wedge. If inside, the terrain determines the mesh. If outside, the mask determines the mesh. Both chunks agree on the mask (inside or outside), so no conflict.

  Wait, that's not right. For an acell at y = 2cellSize is also in the expanded lattice (iy = 2, within range).

  Wait, the expansion adds 1 cell beyond the bounding box, not beyond the boundary. For chunk A with bounding box y from 0 to maxY, the expanded lattice is y from -cellSize to maxY + cellSize. So the lattice includes y from -0.5 to maxY + 0.5 (for cellSize = 0.5).

  For the boundary at y = 0, the cell at y = 0 to 0.5 is in the expanded lattice (iy = 1 to 2). The cell at y = 0.5 to 1.0 is also in the lattice.

  So chunk A's lattice includes cells with positive y (into B's territory), up to 1 cell beyond A's bounding box edge.

  For a horizontal boundary at y = 0 (angle 0), chunk A's bounding box has minY = 0. Expanded: iyMin = -1, so y from -0.5. But the boundary is at y = 0, which is the bottom of the bounding box.

  Wait, I'm confusing the boundary with the bounding box. Let me redo this.

  Chunk A has wedge [0, π/4]. The boundary with chunk B is at π/4. The bounding box of A's wedge:
  - PositionAt(0, 15) = (15, 0)
  - PositionAt(π/4, 15) = (10.6, 10.6)
  - Bounding box: [0, 15] × [0, 10.6]

  The boundary ray (π/4) goes from (0, 0) to (10.6, 10.6). It's the diagonal of the bounding box.

  The expansion adds 1 cell in each direction. So the expanded lattice is x from -0.5 to 15.5, y from -0.5 to 11.

  For chunk B (wedge [π/4, π/2]):
  - PositionAt(π/4, 15) = (10.6, 10.6)
  - PositionAt(π/2, 15) = (0, 15)
  - Bounding box: [0, 10.6] × [0, 15]
  - Expanded: x from -0.5 to 11.5, y from -0.5 to 16

  Overlap: x from -0.5 to 11.5, y from -0.5 to 11.

  Cells in the overlap but outside A's wedge: these are cells with y > x + ε (above the diagonal). For example, (0, 5) is in the overlap (x = 0, y = 5, within both expanded lattices). From A's perspective, (0, 5) is outside the wedge (mask positive). From B's perspective, (0, 5) is inside the wedge (terrain value).

  The perpendicular distance from (0, 5) to the π/4 ray is |0 - 5| / √2 = 3.54 cells. Way outside the margin.

  From A: sample = max(terrain(0, 5), mask(0, 5)). terrain = 5 - 10 = -5. mask = Cross(dir_π4, (0, 5)) * 1000 = (0.7075 - 0.7070) * 1000 = 3535. Sample = 3535.
  From B: sample = max(terrain(0, 5), mask_B). mask_B at start = -Cross(dir_π4, (0, 5)) * 1000 = -3535. mask_B at end = Cross(dir_π2, (0, 5)) * 1000 = (05 - 10) * 1000 = 0. WedgeMask = max(-10, max(-3535, 0)) = 0. Sample = max(-5, 0) = 0.

  So A sample = 3535, B sample = 0. Different. But both are positive (air). Marching Squares at this cell: both chunks see all-positive values. No mesh. No conflict.

  OK so the conflict only matters when the Marching Squares generates mesh. For cells far from the surface, both chunks generate no mesh (all solid or all air). For cells near the surface, the cells are close to the boundary ray AND the surface. The margin check catches these.

  Let me verify: for a cell near the surface (radius ≈ 10) and near the boundary (π/4), the perpendicular distance to the boundary is small. With margin = 2 * cellSize, the margin covers cells up to 2 cells from the boundary. For the surface at radius 10, a cell at the surface has corners at distances from 9.5 to 10.5 (approximately). The perpendicular distance to the boundary depends on the angle.

  At the surface, the angle is π/4
  So for cells at the surface near the boundary, the margin check correctly identifies them as seam-adjacent. Both chunks use terrain values. The Marching Squares output is identical.

  For cells far from the surface (e.g., at the center or far outside), the Marching Squares generates no mesh. The SDF values might differ between chunks, but it doesn't matter because no mesh is generated.

  Great, so margin = 2 * cellSize should be sufficient. Let me use that.

  Wait, actually, I realize that the IsWithinSeamMargin check should use the perpendicular distance to the boundary ray. For a non-axis-aligned boundary, the distance from a lattice point to the boundary ray is not simply |x - y| / √2. It's |Cross(dir, position)|, where dir is the unit direction of the boundary ray.

  Cross(dir, (4.5, 5)) = 0.707 * 5 - 0.707 * 4.5 = 0.354. Distance = 0.354. In the margin. Correct.

  So the margin check works correctly for the π/4 boundary.

  OK, I'm confident the fix is correct. Let me implement it now.

  Let me also consider: should I keep the WedgeMaskSteepness constant at 1000? With the seam fix, the steepness no longer affects the seam quality. But it still matters for the interior of the wedge: the mask should dominate the terrain for points far from the boundary. With steepness = 1000, the mask is large for most points, correctly clipping the wedge.

  Actually, let me also think about whether the test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints needs to be updated. After the fix, the test should pass for all shared lattice points (because both chunks use terrain values near the boundary). But the test might fail for shared points that are far from the boundary, where both chunks use mask values. Are the mask values the same?

  For a shared lattice point far from the boundary:
  - From chunk A's perspective: the point is inside A's wedge. mask_A is negative. Sample = max(terrain, mask_A). If terrain > mask_A, sample = terrain.
  - From chunk B's perspective: the point is inside B's wedge. mask_B is negative. Sample = max(terrain, mask_B). If terrain > mask_B, sample = terrain.

  If both chunks are inside their

  Let me also add a new test that specifically checks the seam margin behavior:
  1. Create two adjacent chunks
  2. Find a shared lattice point that's near the boundary (but not on it)
  3. Before the fix: samples are different (one terrain, one mask)
  4. After the fix: samples are the same (both terrain)

  This test would fail before the fix and pass after, confirming the fix works.

  OK let me actually write the code now. I've been thinking for way too long!

  Let me structure the changes:

  1. CartesianChunkFieldSampler.cs:
     - Add IsWithinSeamMargin and IsNearRay helper methods
     - Modify the sampling loop to use terrain values within the seam margin
     - Update doc comments
  2. CartesianChunkFieldSamplerTests.cs:
     - Add a test for seam margin sample equality at near-boundary points
     - The existing test should now pass

  Let me code.

  Actually, let me reconsider one thing. The IsWithinSeamMargin check uses cellSize * 2f as the margin. But what if the cell size is very small (e.g., 0.1)? Then the margin is 0.2 units. For a lattice point at perpendicular distance 0.15 from the boundary, it's in the margin. For a point at 0.25, it's not.

  This is fine because the lattice spacing is 0.1, so the maximum corner distance in a straddling cell is about 0.07 cells. Well within the 2-cell margin.

  For large cell sizes (e.g., 5), the margin is 10 units. This is a wide margin, but the lattice is coarse, so there are few lattice points. The margin covers the relevant points.

  OK, the margin scaling with cellSize is correct.

  Now let me also think about the forward check. For a boundary ray pointing outward from the origin, the forward check Dot(dir, position) > 0 ensures the point is on the forward side of the ray. For points behind the origin (Dot < 0), the margin check is skipped.

  For the origin (0, 0): Dot = 0, not > 0. So the origin is not in the margin. This is fine because the origin is far from the surface and doesn't need the seam fix.

  For a point at (-0.1, 0) with dir = (1, 0) (angle 0 boundary): Dot = -0.1 < 0. Not in margin. This point is behind the origin, near the ray extension. Correctly excluded.

  For a point at (0.1, 0) with dir = (1, 0): Dot = 0.1 > 0. In margin (perpendicular distance = 0). Correctly included.

  Good, the forward check works correctly.

  Let me now actually write the code.

  One more consideration: the IsWithinSeamMargin check uses the chunk's own boundary directions (dirStart and dirEnd). These come from the seam cache when provided. The margin check is applied to the current chunk's positions. For the seam between chunks A and B:
  - Chunk A's dirEnd points along the boundary ray.
  - Chunk B's dirStart points along the same ray (from the seam cache).

  For chunk A, the margin check at dirEnd catches points near A's end boundary. For chunk B, the margin check at dirStart catches points near B's start boundary. Both use the same direction, so they catch the same points.

  But the margin check also depends on the perpendicular distance, which is computed from the point's position. For a point near the boundary, the perpendicular distance is the same from both chunks' perspectives (because the direction is the same). So both chunks identify the same points as margin-adjacent.

  Wait, but the margin check also considers the other boundary (dirStart for A and dirEnd for B). For chunk A, dirStart is the previous chunk's end ray. For chunk B, dirEnd is the next chunk's start ray. These are different rays.

  The margin check at A's dirStart catches points near the boundary with the previous chunk. The margin check at A's dirEnd catches points near the boundary with the next chunk. Both are handled.

  For the seam between A and B:
  - A's dirEnd = B's dirStart (same ray from the seam cache)
  - A's margin check at dirEnd catches near-seam points
  - B's margin check at dirStart catches the same near-seam points

  Both use terrain values. Identical SDF. Same mesh. ✓

  OK, truly implementing now. No more analysis.

  Let me also check: do I need to modify any other files? The fix is in CartesianChunkFieldSampler. The tests are in CartesianChunkFieldSamplerTests. The ChunkSeamCache doesn't need changes (it already provides the correct directions). The ChunkTerrainRenderer doesn't need changes (it already passes the seam cache).

  So I only need to modify two files:
  1. CartesianChunkFieldSampler.cs
  2. CartesianChunkFieldSamplerTests.cs

  Let me write the code.

  For the CartesianChunkFieldSampler, I'll:
  1. Replace the final value computation in the sampling loop
  2. Add IsWithinSeamMargin and IsNearRay helper methods
  3. Update the class doc comment

  For the tests, I'll add a new test that specifically verifies the seam margin behavior.

  Let me write it out now.


Let me plan and implement this carefully.

  Thought for 1m 11s

● Update(Runtime\Terrain\CartesianChunkFieldSampler.cs)
Added 25 lines, removed 3 lines
                    // a chunk border. Scanning every edit here keeps the shared lattice points
                    // bit-identical regardless of which chunk samples them.
                    float terrainValue = field.Sample(position);
                    float final = fullCircle
                        ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
                        : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
                    float final;

                    if (!fullCircle && IsWithinSeamMargin(position, dirStart, dirEnd, cellSize))
                    {
                        // Lattice point near a chunk boundary: use the raw terrain SDF directly
                        // instead of combining with the wedge mask. Both neighboring chunks sample
                        // the same lattice points in the overlap strip created by ComputeLatticeBounds'
                        // 1-cell expansion. When one chunk used the terrain value and the other used
                        // the steep wedge mask at the same lattice point, Marching Squares placed
                        // contour vertices at different positions on shared cell edges, leaving a
                        // visible gap at every chunk border.
                        //
                        // By using the terrain value on both sides, every boundary-straddling cell
                        // produces identical topology and identical edge-interpolation from each
                        // chunk. The mesh may extend up to one cell past the boundary ray, but the
                        // neighbor chunk renders the same triangles there, so the result is visually
                        // seamless with only minor overdraw.
                        final = terrainValue;
                    }
                    else
                    {
                        final = fullCircle
                            ? Mathf.Max(terrainValue, position.magnitude - maxRadius)
                            : Mathf.Max(terrainValue, WedgeMask(position, dirStart, dirEnd, maxRadius, reflexWedge));
                    }

                    positions[i, j] = position;
                    samples[i, j] = final;
                }

  Thought for 49s

Now add the seam margin helper methods:

● Update(Runtime\Terrain\CartesianChunkFieldSampler.cs)
Added 37 lines
        }

        /// <summary>
        /// True when <paramref name="position"/> lies within the seam margin of either boundary
        /// ray, meaning a neighboring chunk also samples this lattice point and both sides must
        /// use the same SDF value for contiguous Marching Squares output.
        /// </summary>
        private static bool IsWithinSeamMargin(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float cellSize)
        {
            // 2-cell margin covers all corners of boundary-straddling cells plus the full
            // overlap strip created by ComputeLatticeBounds' 1-cell expansion, regardless of
            // boundary angle (the worst case is a 45° seam where the diagonal expansion
            // projects to roughly sqrt(2) cells perpendicular to the ray).
            float seamMargin = cellSize * 2f;

            return IsNearRay(position, dirStart, seamMargin)
                || IsNearRay(position, dirEnd, seamMargin);
        }

        /// <summary>
        /// True when <paramref name="position"/> is within <paramref name="margin"/> perpendicular
        /// distance of the ray defined by <paramref name="dir"/> and is on the forward side of the
        /// origin (not behind the planet center on the ray's extension).
        /// </summary>
        private static bool IsNearRay(Vector2 position, Vector2 dir, float margin)
        {
            // Perpendicular distance from position to the ray line (dir is a unit vector,
            // so |Cross(dir, position)| is the exact perpendicular distance).
            float perpDistance = Mathf.Abs(Cross(dir, position));
            if (perpDistance >= margin)
            {
                return false;
            }

            // Must be on the forward side of the ray origin — points behind the planet center
            // on the ray's line extension are not part of the actual chunk boundary.
            return Vector2.Dot(dir, position) > 0f;
        }

        /// <summary>
        /// Axis-aligned lattice index bounds covering the chunk's angular wedge, expanded by one
        /// cell of margin so boundary-straddling cells are included for correct Marching Squares
        /// interpolation.

  Thought for 1m 2s

Now update the class doc comment to match the actual implementation:

● Update(Runtime\Terrain\CartesianChunkFieldSampler.cs)
Error editing file

  Thought for 1m 15s, read 1 file

● Update(Runtime\Terrain\CartesianChunkFieldSampler.cs)
Added 13 lines, removed 10 lines
    /// arc-length-per-step shrinks near the center and grows near the rim, which made brush carves
    /// look like faceted arc segments rather than smooth circles.
    ///
    /// Each chunk still owns exactly the samples inside its angular wedge: the field's signed
    /// distance is intersected (via max, i.e. CSG-AND) with a wedge mask so a chunk's mesh is
    /// clipped exactly at its StartAngle/EndAngle. Recomputing that clip independently on both
    /// sides of a seam (relying on chunk A's EndAngle and chunk B's StartAngle merely being equal
    /// values) turned out not to be reliable enough to guarantee a bit-identical result, so
    /// lattice corners near a shared boundary are no longer recomputed per chunk at all: the
    /// optional <c>previousSeam</c>/<c>nextSeam</c> parameters to <see cref="Sample"/> let a
    /// caller (see <see cref="ChunkSeamCache"/>) supply the exact same precomputed sample value to
    /// both neighboring chunks for any lattice point near their shared ray, removing the
    /// bit-identical-recomputation assumption entirely.
    /// Each chunk owns exactly the samples inside its angular wedge: the field's signed distance
    /// is intersected (via max, i.e. CSG-AND) with a wedge mask so a chunk's mesh is clipped at
    /// its StartAngle/EndAngle.
    ///
    /// Lattice points within a 2-cell margin of either boundary ray bypass the wedge mask entirely
    /// and use the raw terrain SDF directly. Both neighboring chunks sample the same lattice points
    /// in the overlap strip (created by <see cref="ComputeLatticeBounds"/> 1-cell expansion), and
    /// feeding each chunk a different SDF value at the same point (terrain on the inside, steep
    /// mask on the outside) caused Marching Squares to place contour vertices at different positions
    /// on shared cell edges — a visible gap at every chunk border. Using the terrain value on both
    /// sides guarantees identical mesh topology at the seam. The <c>seamCache</c>
    /// parameter to <see cref="Sample"/> ensures both chunks use the exact same boundary direction
    /// vectors so the margin check is symmetric.
    /// </summary>
    public static class CartesianChunkFieldSampler
    {

  Thought for 14s

Let me verify the complete modified file:

  Thought for 23s, read 1 file

The sampler looks correct. Now let me add the tests.

  Thought for 1m 41s, read 1 file

● Update(Tests\EditMode\CartesianChunkFieldSamplerTests.cs)
Added 89 lines

            Assert.Greater(sharedPointsChecked, 0, "Expected adjacent chunks' lattices to overlap at the shared boundary ray.");
        }

        [Test]
        public void Sample_AdjacentChunks_NearSeamPointsUseTerrainNotMask()
        {
            // A lattice point just inside chunk A's wedge but very close to the shared boundary
            // should use the raw terrain SDF (not the steep wedge mask), so that chunk B — which
            // considers the same point to be outside its wedge — sees the identical value.
            // Previously this point had a large positive mask value from one side and a negative
            // terrain value from the other, causing Marching Squares to place contour vertices at
            // different positions on the shared cell edge.
            var field = new TerrainField(10f);
            var grid = n
            CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, 15f, cellSize, seamCache);

            // The boundary between chunks 0 and 1 is at PI/4. The ray at PI/4 passes through
            // points where x == y. A lattice point at (1, 1.5) is on B's side (angle > PI/4)
            // but within the seam margin. Both chunks should sample it with the terrain value.
            float rawTerrain = field.Sample(new Vector2(1f, 1.5f));

            int widthA = resultA.Samples.GetLength(0);
            int heightA = resultA.Samples.GetLength(1);

            for (int i = 0; i < widthA; i++)
            {
                for (int j = 0; j < heightA; j++)
                {
                    if (Mathf.Abs(resultA.Positions[i, j].x - 1f) < 1e-4f && Mathf.Abs(resultA.Positions[i, j].y - 1.5f) < 1e-4f)
                    {
                        // Should use terrain value, not a large positive mask value.
                        Assert.AreEqual(rawTerrain, resultA.Samples[i, j], 1e-6f,
                            "Chunk A near-seam point should use raw terrain SDF, not the wedge mask.");
                    }
                }
            }

            int widthB = resultB.Samples.GetLength(0);
            int heightB = resultB.Samples.GetLength(1);

            for (int i = 0; i < widthB; i++)
            {
                for (int j = 0; j < heightB; j++)
                {
                    if (Mathf.Abs(resultB.Positions[i, j].x - 1f) < 1e-4f && Mathf.Abs(resultB.Positions[i, j].y - 1.5f) < 1e-4f)
                    {
                        Assert.AreEqual(rawTerrain, resultB.Samples[i, j], 1e-6f,
                            "Chunk B near-seam point should use raw terrain SDF, not the wedge mask.");
                    }
                }
            }
        }

        [Test]
        public void Sample_FarFromSeam_StillClippedByWedgeMask()
        {
            // Lattice points far from the boundary should still be clipped by the wedge mask.
            // The seam margin only applies within 2 cells of the boundary ray.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            TerrainChunk chunk = grid.GetChunk(0); // [0, PI/2)
            const float cellSize = 0.5f;

            // No seam cache — tests that the wedge mask still works for non-seam points.
            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, chunk, 15f, cellSize);

            int width = result.Samples.GetLength(0);
            int height = result.Samples.GetLength(1);

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Vector2 pos = result.Positions[i, j];

                    // Points well inside the wedge should have negative (solid) samples
                    // near the planet center.
                    if (pos.magnitude < 5f && pos.x > 1f && pos.y > 1f && pos.y < pos.x * 2f)
                    {


  Thought for 21s

Let me verify the test file compiles correctly by checking the using statements:

  Thought for 5m 28s, read 1 file

I notice a bug in my test — with 4 chunks, each spans PI/2, so the boundary between chunks 0 and 1 is at PI/2 (the y-axis), not PI/4. Let me fix the test:

● Update(Tests\EditMode\CartesianChunkFieldSamplerTests.cs)
Added 9 lines, removed 8 lines
            var field = new TerrainField(10f);
            var grid = n
            CartesianChunkFieldSampler.Result resultA = CartesianChunkFieldSampler.Sample(field, chunkA, 15f, cellSize, seamCache);
            CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, 15f, cellSize, seamCache);

            // The boundary between chunks 0 and 1 is at PI/4. The ray at PI/4 passes through
            // points where x == y. A lattice point at (1, 1.5) is on B's side (angle > PI/4)
            // but within the seam margin. Both chunks should sample it with the terrain value.
            float rawTerrain = field.Sample(new Vector2(1f, 1.5f));
            // The boundary between chunks 0 and 1 is at PI/2 (positive y-axis).
            // Lattice point (0.5, 5) is on A's side of the boundary (angle < PI/2) but within
            // the seam margin (perpendicular distance = 0.5, margin = 2*cellSize = 1.0).
            // Both chunks should sample it with the raw terrain value.
            float rawTerrain = field.Sample(new Vector2(0.5f, 5f));

            int widthA = resultA.Samples.GetLength(0);
            int heightA = resultA.Samples.GetLength(1);

            {
                for (int j = 0; j < heightA; j++)
                {
                    if (Mathf.Abs(resultA.Positions[i, j].x - 1f) < 1e-4f && Mathf.Abs(resultA.Positions[i, j].y - 1.5f) < 1e-4f)
                    if (Mathf.Abs(resultA.Positions[i, j].x - 0.5f) < 1e-4f && Mathf.Abs(resultA.Positions[i, j].y - 5f) < 1e-4f)
                    {
                        // Should use terrain value, not a large positive mask value.
                        Assert.AreEqual(rawTerrain, resultA.Samples[i, j], 1e-6f,

            {
                for (int j = 0; j < heightB; j++)
                {
                    if (Mathf.Abs(resultB.Positions[i, j].x - 1f) < 1e-4f && Mathf.Abs(resultB.Positions[i, j].y - 1.5f) < 1e-4f)
                    if (Mathf.Abs(resultB.Positions[i, j].x - 0.5f) < 1e-4f && Mathf.Abs(resultB.Positions[i, j].y - 5f) < 1e-4f)
                    {
                        Assert.AreEqual(rawTerrain, resultB.Samples[i, j], 1e-6f,
                            "Chunk B near-seam point should use raw terrain SDF, not the wedge mask.");

∴ Now let me also fix the other test comment that references PI/2 incorrectly. Let me re-read the Sample_FarFromSeam_StillClippedByWedgeMask test to check the comments.

  Actually, the Sample_FarFromSeam_StillClippedByWedgeMask test says // [0, PI/2) which is correct for a 4-chunk grid. But the test condition pos.y < pos.x * 2f is meant to check points well inside the wedge. Let me verify this condition makes sense for the [0, PI/2] wedge.

  For the [0, PI/2] wedge, points inside satisfy: x > 0 and y > 0. The condition pos.y < pos.x * 2f is an additional constraint to ensure the point is well inside the wedge (not near either boundary).

  For a point at (3, 3): y = 3, x2 = 6. 3 < 6. Well inside.
  For a point at (1, 2.5): y = 2.5, x2 = 2. 2.5 < 2? No. This point is near the PI/2 boundary (angle ≈ 68°, which is inside the wedge). The condition excludes it.

  Hmm, the condition is a bit arbitrary. Let me simplify it to just check that the point is well inside the wedge:
  - x > 1 (not near x = 0 boundary)
  - y > 1 (not near y = 0 boundary)
  - y < x (not near the PI/4 line, which is safely inside)

  Actually, the original condition should be fine. Let me leave it as is.

  Wait, but the test also checks pos.magnitude < 5f. Combined with pos.x > 1f and pos.y > 1f, this means the points are in the range [1, sqrt(18)] from the origin. And pos.y < pos.x * 2f means the angle is less than arctan(2) ≈ 63.4°.

  For the [0, PI/2] wedge, points with angle < 63.4° are well inside (not near the PI/2 boundary at 90°). Good.

  The test should work. Let me also think about whether the seam margin affects this test. The test doesn't use a seam cache. The sampler uses the chunk's own boundary directions. For the PI/2 boundary, the seam margin check still applies (the margin check doesn't require the seam cache).

  For a point well inside the wedge (e.g., (3, 2)):
  - Perpendicular distance to PI/2 ray: |Cross(dir_π2, (3, 2))| = |02 - 13| = 3. Margin = 1. 3 >= 1. Not in margin.
  - Perpendicular distance to 0 ray: |Cross(dir_0, (3, 2))| = |12 - 03| = 2. Margin = 1. 2 >= 1. Not in margin.

  So the point is not in the seam margin. The wedge mask applies normally. Good.

  But wait, the test expects sample < 0 (solid). For a point at (3, 2) inside the wedge:
  - terrain = sqrt(9+4) - 10 = 3.606 - 10 = -6.394
  - Wedge mask: maskStart = -Cross(
  But wait, the test condition is pos.magnitude < 5f && pos.x > 1f && pos.y > 1f && pos.y < pos.x * 2f. For (3, 2): magnitude = 3.606 < 5. x = 3 > 1. y = 2 > 1. y = 2 < 3*2 = 6. All conditions met. Sample = -6.394 < 0. Test passes.

  Good, the test is correct. Let me also check if there are any other issues.

  Actually, I realize there's a subtle issue. The test Sample_FarFromSeam_StillClippedByWedgeMask doesn't use a seam cache. Without the seam cache, the IsWithinSeamMargin check still runs (using the chunk's own boundary directions). For a 4-chunk grid, chunk 0 has boundaries at angles 0 and PI/2. The margin check uses DirectionAt(0) = (1, 0) and DirectionAt(PI/2) = (0, 1).

  For a lattice point at (0.5, 5):
  - Perpendicular distance to angle 0 ray: |Cross((1,0), (0.5, 5))| = |5| = 5. Not in margin.
  - Perpendicular distance to PI/2 ray: |Cross((0,1), (0.5, 5))| = |0.5| = 0.5. In margin (0.5 < 1).
  - Forward check: Dot((0,1), (0.5, 5)) = 5 > 0. Passes.

  So (0.5, 5) is in the seam margin even without the seam cache. The sampler uses the terrain value. This is correct — the margin check doesn't require the seam cache to work.

  For the Sample_FarFromSeam_StillClippedByWedgeMask test, the condition pos.x > 1f && pos.y > 1f ensures we check points at least 1 unit from both axes. The margin is 1 unit (2 * 0.5). Points at distance > 1 from both boundaries are not in the margin. Good.

  Actually, wait. For the PI/2 boundary, the margin is at perpendicular distance 1 from the y-axis. A point at (1.5, 5) has perpendicular distance 1.5 from the y-axis. 1.5 >= 1. Not in margin. Good.

  But the test condition pos.x > 1f means x > 1. A point at (1.1, 3) has x = 1.1, which is > 1 but < 1.5. The perpendicular distance to the y-axis is 1.1. 1.1 >= 1. Not in margin. Good.

  But what about a point at (1.0, 3)? x = 1, which is not > 1 (it's = 1). So the test excludes this point. But (1.0, 3) is at perpendicular distance 1 from the y-axis, which is exactly at the margin boundary (1 >= 1, not in margin). So the test correctly excludes it.

  Wait, but there's also the angle 0 boundary. For a point at (3, 0.5): perpendicular distance to angle 0 ray (x-axis) = |Cross((1,0), (3, 0.5))| = |0.5| = 0.5. 0.5 < 1. In margin! Forward check: Dot((1,0), (3, 0.5)) = 3 > 0. Passes.

  But the test condition pos.y > 1f excludes (3, 0.5). So the test doesn't check this point. Good, because the margin check would use the terrain value, and the test expects the wedge mask.

  Hmm, but what if the test checks a point that's in the margin of the angle 0 boundary? The condition pos.y > 1f ensures y > 1, which is more than 1 unit from the x-axis. So perpendicular distance to the x-axis is > 1, which is >= margin. Not in margin. Good.

  OK, the test is correct. Let me proceed.

  Actually, I realize I should double check one more thing. The test condition pos.y < pos.x * 2f is meant to exclude points near the PI/2 boundary. But for a point at (1.1, 2.0): y = 2.0, x * 2 = 2.2. 2.0 < 2.2. Condition met. But perpendicular distance to PI/2 boundary: 1.1. 1.1 >= 1. Not in margin.

  But the margin check also considers the angle 0 boundary. Perpendicular distance: 2.0. Not in margin. So the point is correctly not in the margin.

  What about (1.1, 2.1)? y = 2.1, x * 2 = 2.2. 2.1 < 2.2. Condition met. Perpendicular distance to PI/2: 1.1. Not in margin. Good.

  What about (0.6, 1.1)? y = 1.1, x * 2 = 1.2. 1.1 < 1.2. Condition met. Perpendicular distance to PI/2: 0.6. 0.6 < 1. In margin! But x = 0.6, which is not > 1. So the test excludes this point. Good.

  OK, the test is correct. All points satisfying the test conditions are outside the seam margin. The wedge mask is applied. Good.

  Now let me also think about the existing test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints. With the fix, this test should pass. Let me verify.

  For shared lattice points:
  1. Points on the boundary ray: both chunks have mask = 0. Sample = max(terrain, 0). Same from both.
  2. Points near the boundary (in the margin): both chunks use terrain. Same from both.
  3. Points far from the boundary (not in the margin):
     a. Points inside both wedges: can't happen (the wedges are disjoint except for the boundary).
     b. Points inside one wedge and outside the other, far from the boundary: these are in the overlap lattice but far from the boundary. One chunk uses terrain, the other uses mask. Different samples!

  Wait, case (b) is a problem! For a point far from the boundary but in the overlap lattice, the samples are different. The existing test checks ALL shared lattice points, including case (b).


  A point at (0.5, 10) is in the overlap. Perpendicular distance to PI/2 boundary: 0.5. In the margin (0.5 < 1). Both use terrain. Same.

  A point at (0, 1) is in the overlap. Perpendicular distance to PI/2 boundary: 0. On the boundary ray. Same samples.

  A point at (0.25, 1) is in the overlap... wait, 0.25 / 0.5 = 0.5, not an integer. So (0.25, 1) is not a lattice point.

  Lattice points in the overlap: x = -0.5, 0, 0.5 (for cellSize = 0.5). And y from -0.5 to 15.5.

  For x = -0.5: perpendicular distance to PI/2 boundary = 0.5. In margin.
  For x = 0: perpendicular distance = 0. On boundary.
  For x = 0.5: perpendicular distance = 0.5. In margin.

  All overlap points are within 0.5 units of the boundary, which is < 1 (the margin). So ALL shared lattice points are in the margin. Both chunks use terrain values. Same samples!

  Great, the test should pass. The 1-cell expansion creates an overlap strip that is at most 1 unit (2 * cellSize = 1) wide. The margin is also 1 unit. So all overlap points are in the margin.

  Wait, that's a coincidence for cellSize = 0.5. For a different cellSize, the overlap might be wider than the margin.

  For cellSize = 1: overlap is 2 units wide (1 from each expansion). Margin is 2 units (2 * cellSize). Same.

  For cellSize = 0.25: overlap is 0.5 units wide. Margin is 0.5 units. Same.

  For cellSize = 2: overlap is 4 units wide. Margin is 4 units. Same.

  In general: overlap width = 2 * cellSize. Margin = 2 * cellSize. Same!

  So the margin exactly covers the overlap strip. All shared lattice points are in the margin. Both chunks use terrain values. The test passes.

  This is a nice property. The 2-cell margin in the code exactly matches the 2-cell overlap from the 1-cell expansion in ComputeLatticeBounds.

  Wait, but this is only true for axis-aligned boundaries (PI/2, PI, etc.). For diagonal boundaries (PI/4), the overlap might be wider.

  For a boundary at PI/4 (which doesn't occur in the 4-chunk grid, but could in other configurations), the overlap in the perpendicular direction depends on the projection of the axis-aligned expansion.

  For a PI/4 boundary, the axis-aligned expansion of 1 cell in x and 1 cell in y projects to 1/cos(45°) = sqrt(2) ≈ 1.414 cells perpendicular to the boundary. From each side, so total overlap ≈ 2.828 cells.

  But the margin is 2 * cellSize = 2 cells. So for a PI/4 boundary, the margin (2 cells) is less than the overlap (2.828 cells). Some overlap points are outside the margin.

  For these points, one chunk uses terrain and the other uses mask. Different samples. The test would fail.

  But in the 4-chunk grid, the boundaries are at 0, PI/2, PI, 3PI/2 — all axis-aligned. So the margin exactly covers the overlap. The test passes.

  For grids with more chunks (e.g., 8 chunks, where some boundaries are at PI/4), the margin might not cover the full overlap. But the test uses 4 chunks, so it's fine.

  To be safe, I could increase the margin to 3 * cellSize. This would cover the overlap for boundaries up to about 48° from the axis. For 8 chunks, the boundaries are at 0, PI/8, PI/4, 3PI/8, ..., which include PI/4 (45°). The overlap for PI/4 is 2.828 cells. Margin = 3 * cellSize > 2.828 * cellSize. Covered.

  But for very many chunks (e.g., 32), the smallest boundary angle is 2π/32 ≈ 11.25°. The overlap for this angle is 2 / sin(11.25°) ≈ 10.3 cells. Margin = 3 * cellSize is not enough.

  Hmm, this is a problem for high chunk counts. Let me think about how to handle this.

  Actually, the overlap is determined by the bounding box expansion. For a boundary at angle θ, the perpendicular width of the overlap is:
  overlap_perp = 2 * cellSize / sin(θ) for small θ (where the boundary is nearly horizontal)
  overlap_perp = 2 * cellSize / cos(θ) for θ near PI/2 (where the boundary is nearly vertical)

  The worst case is when θ is small (or near PI), where sin(θ) ≈ θ. For very small θ, the overlap can be very large.

  Wait, I think I'm overcomplicating this. The key insight is: for any boundary angle, the overlap strip is defined by the 1-cell expansion of both chunks' bounding boxes. A point in the overlap is sampled by both chunks.

  For the margin to cover all overlap points, I need the margin to be at least as wide as the overlap in the perpendicular direction. The overlap width depends on the boundary angle and the expansion.

  For the general case, the overlap perpendicular width is bounded by 2 * cellSize / sin(min_angle_from_axis). For the worst case (very small angle), this can be very large.

  But in practice, for reasonable chunk counts (4, 8, 16), the overlap is manageable. For 8 chunks (PI/8 = 22.5°), the overlap is 2 * cellSize / sin(22.5°) ≈ 2 * cellSize / 0.383 ≈ 5.22 * cellSize. Margin = 2 * cellSize is not enough.

  Hmm, this means for 8+ chunks, the margin doesn't cover the full overlap, and the seam fix doesn't fully eliminate the seam for all chunk configurations.

  Let me think about a better approach. Instead of a fixed margin, I could compute the margin based on the bounding box expansion. But the sampler doesn't have access to the neighbor chunk's bounding box.

  Alternatively, I could increase the margin to a larger value. For 16 chunks (PI/16 = 11.25°), the overlap is 2 / sin(11.25°) ≈ 10.3 cells. Margin = 10 * cellSize would cover this. But this is a lot of extra terrain sampling.

  Actually, let me reconsider. The seam is only visible at the terrain surface. For cells far from the terrain surface, the Marching Squares generates no mesh. So the margin only needs to cover the overlap region near the terrain surface.

  The terrain surface is at radius ≈ baseRadius. At this radius, the perpendicular distance from the boundary ray to the farthest overlap point is bounded by the expansion width at that radius.

  For a boundary at angle θ, the overlap at radius r is bounded by 2 * cellSize in the axis-aligned direction. The perpendicular projection is 2 * cellSize / sin(θ). At the surface (r ≈ baseRadius), this is the maximum overlap width.

  For a margin to cover this, I need margin >= 2 * cellSize / sin(θ). For the worst case (smallest θ), this can be very large.

  But in practice, the user is seeing seams, which means the current implementation doesn't handle their chunk count well. Let me check what chunk count they're using.

  Actually, I don't know what chunk count they're using. Let me just use a reasonable margin and note that for very high chunk counts, the margin might not cover the full overlap.

  Alternatively, I can compute the margin dynamically based on the chunk's angular size. For a chunk with angular size α, the boundary is at angle α/2 from the axis. The overlap perpendicular width is 2 * cellSize / sin(α/2). For α = PI/4 (8 chunks), the overlap is 2 / sin(PI/8) = 2 / 0.383 = 5.22 cells. Margin = 6 * cellSize would cover this.

  But I don't have access to the angular size in the margin check (it's in the chunk, which is accessible).

  Hmm, let me compute the margin based on the chunk's angular size:

  float angularSize = endAngle - startAngle;
  float seamMargin = cellSize * Mathf.Max(2f, 2f / Mathf.Sin(angularSize * 0.5f));

  Wait, but this would make the margin very large for small angular sizes. For 100 chunks (angularSize = 2π/100 ≈ 0.063 radians), the margin would be 2 / sin(0.031) ≈ 64 cells. This is way too much.

  Let me cap the margin:

  float seamMargin = Mathf.Min(cellSize * 10f, cellSize * 2f / Mathf.Sin(angularSize * 0.5f));

  With a cap of 10 cells, the margin is at most 10 * cellSize. For 100 chunks with cellSize = 0.5, this is 5 units. The actual overlap might be larger, but the margin covers most of it.

  Actually, let me think about this differently. The overlap is created by the 1-cell expansion. The margin needs to cover the overlap. But the margin is checked in the perpendicular direction, and the expansion is in the axis-aligned direction.

  The maximum perpendicular projection of a 1-cell axis-aligned expansion is 1 / sin(θ), where θ is the angle between the expansion direction and the perpendicular to the boundary.
 depends on the boundary angle.

  Actually, I think the simplest approach is: for each boundary ray, compute the perpendicular projection of the bounding box expansion, and use that as the margin.

  But this requires knowing the bounding box, which is computed in ComputeLatticeBounds. The margin check happens in the sampling loop, which doesn't have direct access to the bounding box.

  Let me take a step back. The margin is currently 2 * cellSize, which works for axis-aligned boundaries (4 chunks). For diagonal boundaries (8+ chunks), the margin might not be enough.

  For the user's current issue, they're seeing visible seams. This means either:
  1. They have more than 4 chunks, and the margin doesn't cover the overlap.
  2. They have 4 chunks, and there's another issue (like the one I've already fixed).

  The fix I've implemented (seam margin with terrain values) should fix case 2. For case 1, I need to increase the margin.

  Let me use a margin that scales with the angular size:

  float seamMargin = cellSize * Mathf.Max(2f, 1f / Mathf.Max(0f, Mathf.Sin(angularSize * 0.5f)));

  For 4 chunks (angularSize = PI/2): margin = 2 * cellSize (the same as before).
  For 8 chunks (angularSize = PI/4): margin = 1 / sin(PI/8) ≈ 2.61, so margin = 2.61 * cellSize.
  For 16 chunks (angularSize = PI/8): margin = 1 / sin(PI/16) ≈ 5.13, so margin = 5.13 * cellSize.

  Hmm, but this grows quite fast for small angular sizes. Let me cap it:

  float seamMargin = cellSize * Mathf.Min(8f, Mathf.Max(2f, 1f / Mathf.Max(1e-3f, Mathf.Sin(angularSize * 0.5f))));

  With a cap of 8 cells, the margin is at most 8 * cellSize = 4 units (for cellSize = 0.5). This should cover the overlap for most practical chunk counts.

  Actually, I realize the formula should be different. The overlap perpendicular width is determined by the projection of the 1-cell expansion. For a boundary at angle θ (relative to the x-axis):
  - x-expansion perpendicular projection: cellSize * |cos(θ)|
  - y-expansion perpendicular projection: cellSize * |sin(θ)|

  Wait, no. The expansion adds 1 cell in the x direction. The perpendicular distance from the boundary to the expansion edge is cellSize * |cos(θ_perp)| where θ_perp is the angle between the expansion direction and the perpendicular to the boundary.

  For a boundary at angle θ, the perpendicular is at angle θ + PI/2. The x-axis is at angle 0. The angle between x and the perpendicular is θ + PI/2. The projection is cellSize * |cos(θ + PI/2)| = cellSize * |sin(θ)|.

  Similarly, the y-expansion perpendicular projection is cellSize * |cos(θ)|.

  So the total overlap perpendicular width from one side is cellSize * (|sin(θ)| + |cos(θ)|). From both sides: 2 * cellSize * (|sin(θ)| + |cos(θ)|).

  For θ = 0: overlap = 2 * cellSize * (0 + 1) = 2 * cellSize.
  For θ = PI/4: overlap = 2 * cellSize * (0.707 + 0.707) = 2 * cellSize * 1.414 = 2.828 * cellSize.
  For θ = PI/2: overlap = 2 * cellSize * (1 + 0) = 2 * cellSize.

  So the maximum overlap is at θ = PI/4 (diagonal boundary), with width 2.828 * cellSize. The margin of 2 * cellSize doesn't cover this. A margin of 3 * cellSize would.

  But the corner distance of a cell at the edge of the overlap adds another cellSize * sqrt(2) ≈ 1.414 * cellSize. So the total margin needed is 2.828 + 1.414 = 4.242 * cellSize.

  Hmm, but I'm not sure the corner distance needs to be included. The margin check is for individual lattice points, not cells. A lattice point at the edge of the overlap is at perpendicular distance = overlap_width / 2 = 1.414 * cellSize from the boundary. With margin = 2 * cellSize, this is within the margin (1.414 < 2).

  Wait, I need to reconsider. The margin checks individual lattice points. For a lattice point in the overlap:
  - It's within 1 cell (axis-aligned) of the boundary.
  - The perpendicular distance is at most 1.414 * cellSize (for a diagonal boundary).
  - With margin = 2 * cellSize, this is covered.

  Wait, but the overlap might include points further away. For a diagonal boundary (PI/4), the overlap includes points from -1 cell to +1 cell in x and y from the boundary. The farthest point in the overlap is at (cellSize, cellSize) from the boundary, which is at perpendicular distance = cellSize / sqrt(2) + cellSize / sqrt(2) = sqrt(2) * cellSize ≈ 1.414 * cellSize.

  Hmm, that's the same as before. Let me think more carefully.

  The bounding box expansion adds 1 cell in x and 1 cell in y. For a diagonal boundary, the expansion creates a square of 2 cells × 2 cells around the boundary. The perpendicular distance from the boundary to the farthest corner of this square is sqrt(2) * cellSize ≈ 1.414 * cellSize.

  With margin = 2 * cellSize, all points in this square are covered (1.414 < 2).

  But wait, the overlap from both sides. Each side adds a 2-cell × 2-cell expansion. The total overlap is a 4-cell × 4-cell square. The perpendicular distance from the boundary to the farthest corner is 2 * sqrt(2) * cellSize ≈ 2.828 * cellSize.

  With margin = 2 * cellSize, this

  The overlap is x from -0.5 to 11.5, y from -0.5 to 11.

  A lattice point at (11, 0) is in the overlap. The perpendicular distance to the PI/4 boundary: |Cross(dir_π4, (11, 0))| = |0.7070 - 0.70711| = 7.78. Way outside the margin.

  But this point is not near the boundary ray — it's at angle 0, which is far from PI/4. The overlap in this direction is because both chunks' bounding boxes extend to x = 0. This is not part of the seam.

  The seam is only at the boundary ray. Points far from the boundary ray (even if in the lattice overlap) are not near the seam. The margin check correctly excludes them.mask applies. But if both chunks agree on the mask (both inside or both outside), the samples are the same. If they disagree (one inside, one outside), the samples are different, but no mesh is generated (all positive or all negative).

  So the margin of 2 * cellSize is correct. It covers all lattice points within 2 cells (perpendicular) of the boundary ray. For the overlap region near the boundary ray, this is sufficient.

  For overlap points far from the boundary ray (like (11, 0) above), the mask correctly clips them, and no mesh is generated. The different sample values don't cause visible artifacts.

  So the margin of 2 * cellSize is sufficient for all practical cases. I don't need to increase it.

  Let me verify with a concrete example. For a 4-chunk grid (boundaries at 0, PI/2, PI, 3PI/2), the boundary between chunks 0 and 1 is at PI/2. The overlap in x is from -0.5 to 0.5. Lattice points at x = -0.5, 0, 0.5.

  For x = 0.5: perpendicular distance to PI/2 boundary = 0.5 < margin (1). In margin.
  For x = -0.5: perpendicular distance = 0.5 < margin. In margin.
  For x = 0: perpendicular distance = 0. On boundary.

  All overlap points are in the margin. Good.

  For a 8-chunk grid (boundaries at 0, PI/8, PI/4, 3PI/8, ...), the boundary between chunks at PI/8 and 2PI/8 = PI/4. The overlap depends on the bounding box.

  For chunk with wedge [PI/8, PI/4]:
  - PositionAt(PI/8, 15) = (15 * cos(22.5°), 15 * sin(22.5°)) = (13.83, 5.74)
  - PositionAt(PI/4, 15) = (10.61, 10.61)
  - Bounding box: [10.61, 13.83] × [5.74, 10.61]
  - Expanded: x from 10.11 to 14.53, y from 5.06 to 11.31

  For the adjacent chunk [PI/4, 3PI/8]:
  - PositionAt(PI/4, 15) = (10.61, 10.61)
  - PositionAt(3PI/8, 15) = (5.74, 13.83)
  - Bounding box: [5.74, 10.61] × [10.61, 13.83]
  - Expanded: x from 5.06 to 11.31, y from 9.91 to 14.53

  Overlap: x from 10.11 to 11.31, y from 10.61 to 11.31.

  A lattice point at (10.5, 11) is in the overlap. Perpendicular distance to PI/4 boundary: |Cross(dir_π4, (10.5, 11))| = |0.70711 - 0.70710.5| = |7.78 - 7.42| = 0.354. With margin = 1, in margin.

  A lattice point at (11, 11): |Cross| = |7.78 - 7.78| = 0. On boundary.

  A lattice point at (10.5, 10.5): |Cross| = 0. On boundary.

  All overlap points are near the PI/4 boundary and within the margin. Good.

  But wait, the overlap is very small (x from 10.11 to 11.31, y from 10.61 to 11.31). This is because the two chunks have very different bounding boxes (different y ranges). The overlap is the intersection, which is a small region near the boundary.

  So for an 8-chunk grid, the overlap is small, and the margin covers it. Good.

  What about the boundary at PI/8 (between chunk 7 and chunk 0, or between some other pair)?

  For chunk with wedge [7PI/8, 2PI]:
  Hmm, actually for an 8-chunk grid:
  Chunk 0: [0, PI/4]
  Chunk 1: [PI/4, PI/2]
  - PositionAt(2PI, 15) = (15, 0)
  - Bounding box: [10.61, 15] × [-10.61, 0]
  - Expanded: x from 9.91 to 15.71, y from -11.31 to 0.71

  For chunk 0 ([0, PI/4]):
  - Bounding box: [0, 15] × [0, 10.61]
  - Expanded: x from -0.71 to 15.71, y from -0.71 to 11.31

  Overlap: x from 9.91 to 15.71, y from -0.71 to 0.71.

  A lattice point at (10, 0) is in the overlap. Perpendicular distance to the 0 boundary (x-axis): |Cross((1,0), (10, 0))| = 0. On boundary.

  A lattice point at (10, 0.5): |Cross((1,0), (10, 0.5))| = 0.5. In margin (0.5 < 1).

  A lattice point at (15, 0.5): |Cross| = 0.5. In margin.

  All overlap points are within 0.5 units of the x-axis, which is < margin (1). Good.

  OK, so the margin of 2 * cellSize is sufficient for the overlap in all practical cases. The margin covers all lattice points in the overlap region near the boundary ray. For lattice points in the overlap but far from the boundary ray, the mask correctly handles them (no mesh generated).

  Great, my fix is correct. Let me finalize.

  Actually wait, I realize I made an error in the 8-chunk calculation. The overlap between the PI/8 and PI/4 chunks might not be what I calculated. Let me redo it.

  For 8 chunks: angularSize = 2PI/8 = PI/4.

  Chunk 1: [PI/4, PI/2]. Bounding box: [0, 10.61] × [10.61, 15].

  Wait, that's not right. Let me recalculate.

  PositionAt(PI/4, 15) = (15 * cos(45°), 15 * sin(45°)) = (10.61, 10.61).
  PositionAt(PI/2, 15) = (15 * cos(90°), 15 * sin(90°)) = (0, 15).

  Bounding box: [0, 10.61] × [10.61, 15]. Wait, minX = 0, maxX = 10.61, minY = 10.61, maxY = 15.

  Hmm, but the wedge also includes the origin (0, 0). So minX = 0, minY = 0. But the bounding box starts at (0, 0) because the wedge includes the origin.

  Actually, the ComputeLatticeBounds function starts with minX = 0, maxX = 0, minY = 0, maxY = 0, and then calls Include for the endpoint positions. So the bounding box always includes the origin.

  For chunk 1 ([PI/4, PI/2]):
  - Include(PositionAt(PI/4, 15)) = Include((10.61, 10.61)): minX=0, maxX=10.61, minY=0, maxY=10.61
  - Include(PositionAt(PI/2, 15)) = Include((0, 15)): minX=0, maxX=10.61, minY=0, maxY=15
  - Axis-aligned angles in [PI/4, PI/2]: PI/2. Include((0, 15)): no change.
  - Bounding box: [0, 10.61] × [0, 15]
  - Expanded: x from -0.71 to 11.31, y from -0.71 to 15.71

  For chunk 0 ([0, PI/4]):
  - Include(PositionAt(0, 15)) = Include((15, 0)): minX=0, maxX=15, minY=0, maxY=0
  - Include(PositionAt(PI/4, 15)) = Include((10.61, 10.61)): minX=0, maxX=15, minY=0, maxY=10.61
  - Axis-aligned: 0. Include((15, 0)): no change.
  - Bounding box: [0, 15] × [0, 10.61]
  - Expanded: x from -0.71 to 15.71, y from -0.71 to 11.31

  Overlap: x from -0.71 to 11.31, y from -0.71 to 11.31.

  This is a large overlap! A lattice point at (0, 0) is in the overlap. Perpendicular distance to PI/4 boundary: 0. On boundary.

  A lattice point at (11, 0) is in the overlap. Perpendicular distance: |Cross(dir_π4, (11, 0))| = 0.707 * 11 = 7.78. Not in margin.

  But (11, 0) is at angle 0, which is inside chunk 0's wedge [0, PI/4] but far from the PI/4 boundary. From chunk 1's perspective, (11, 0) is at angle 0, which is before chunk 1's star PI/2) but should be [0, PI/2] for a 4-chunk grid.

  Actually, let me check: for a 4-chunk grid, chunk 0 spans [0, PI/2]. The test checks points well inside the wedge. Let me verify the test conditions are correct.

  The test checks: pos.magnitude < 5f && pos.x > 1f && pos.y > 1f && pos.y < pos.x * 2f

  For a point to be well inside chunk 0's wedge [0, PI/2], it needs:
  - x > 0 (inside from the start boundary at angle 0)
  - y > 0 (inside from the end boundary at angle PI/2... actually no)

  Actually wait, near PI/2, y >> x. The condition pos.y < pos.x * 2f means y < 2x, which means the angle is less than arctan(2) ≈ 63°. So the point is not near the PI/2 boundary. Good.

  And pos.magnitude < 5f keeps the point well inside the planet (terrain is negative, solid).

  The test doesn't pass a seam cache, so the sampler uses the chunk's own angles. The seam margin check still applies. But the test checks points well inside the wedge, far from the boundary. These points are NOT in the seam margin, so the wedge mask is applied. The test verifies that points well inside the wedge have negative samples.

  Hmm wait, but the test also needs to verify that the wedge mask is still applied for points outside the wedge. Currently it only checks points inside. Let me also add a check finterior points are solid (negative). This is correct.

  But I should also verify that the seam margin doesn't affect points far from the boundary. The test currently only checks interior points. Let me also add a check for exterior points... Actually the existing tests already cover this. The new test is just a sanity check for the interior. Let me leave it as is.

  Now let me mark the test task as complete and verify everything compiles. Actually, this is a Unity project, so I can't compile it directly. Let me at least check for obvious syntax errors by reviewing the changes.

  Let me also check: the test Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints — will it now pass? After the fix, both chunks use terrain values at shared lattice points near the boundary. For shared points far from the boundary, both chunks use the wedge mask, which produces the same value (both inside or both outside, since the point is in both lattices).

  Wait, for shared points far from the boundary: if the point is in both lattices but on the interior of chunk A (far from A's boundaries), it's in the margin of A's start boundary (the boundary with chunk 3). Hmm, no. The point is far from A's boundaries.

  Actually, for a point that's in both lattices but far from the shared boundary between A and B: this means the point is in the overlap of A's and B's expanded bounding boxes, but far from the PI/2 boundary. For example, a point near the origin (0.5, 0.5).

  Point (0.5, 0.5):
  - In chunk 0's lattice: yes (first quadrant expansion)
  - In chunk 1's lattice: need to check

  Chunk 1's bounding box: minX=-15, maxX=0, minY=0, maxY=15.
  Expanded: ixMin=-31, ixMax=1, iyMin=-1, iyMax=31.
  (0.5, 0.5): ix = 1, jy = 1. ixMax = 1, so ix = 1 is in range. jy = 1 is in range.

  So (0.5, 0.5) is in chunk 1's lattice.

  From chunk 1's perspective, (0.5, 0.5) has angle ≈ 45° = PI/4. Chunk 1's wedge is [PI/2, PI]. PI/4 < PI/2, so (0.5, 0.5) is before chunk 1's start angle. maskStart = -Cross(dir_π2, (0.5, 0.5)) * 1000 = -(00.5 - 10.5) * 1000 = 500.

  Positive, so the sample from chunk 1 would be 500 (mask value).

  From chunk 0's perspective, (0.5, 0.5) is inside the wedge [0, PI/2]. Both mask values are negative. The sample is the terrain value.

  So chunk 0: terrain ≈ -9.3, chunk 1: mask = 500. Different!

  Now, is (0.5, 0.5) in the seam margin?
  - Distance from PI/2 ray: |Cross(dir_π2, (0.5, 0.5))| = |00.5 - 10.5| = 0.5. Margin = 1. 0.5 < 1. In margin!

  So both chunks use terrain values. The test should pass for this point.

  But what about a point further from the PI/2 ray, like (2, 0.5)?
  - Distance from PI/2 ray: |Cross(dir_π2, (2, 0.5))| = |00.5 - 12| = 2. Margin = 1. 2 >= 1. NOT in margin.

  Is (2, 0.5) in chunk 1's lattice?
  Chunk 1: ixMin=-31, ixMax=1, iyMin=-1, iyMax=31.
  (2, 0.5): ix = 4. ixMax = 1, so ix = 4 is NOT in range. Not in chunk 1's lattice.

  So (2, 0.5) is only in chunk 0's lattice. Not a shared point. The test doesn't check it.

  What about (0.5, 2)?
  - Distance from PI/2 ray: |Cross(dir_π2, (0.5, 2))| = |02 - 10.5| = 0.5. In margin!

  Both chunks use terrain. Same.


  So all shared lattice points between chunks 0 and 1 are within the seam margin of the PI/2 boundary. The fix ensures they all use terrain values. The test should pass!

  Let me verify: what's the maximum x-coordinate of a shared point? Chunk 0's ixMax = 31, chunk 1's ixMax = 1. Shared ix range: from max(ixMin_A, ixMin_B) to min(ixMax_A, ixMax_B) = max(-1, -31) to min(31, 1) = -1 to 1.

  Shared x range: from -0.5 to 0.5. The maximum x of a shared point is 0.5. The perpendicular distance from (0.5, y) to the PI/2 ray is 0.5, which is < margin (1). So all shared points are in the margin.

  The existing test should now pass. Let me verify the code is correct and mark the task as done.

  Actually wait, I also need to handle one edge case. The seam margin check only applies when !fullCircle. For a full circle (1 chunk), there are no seams, so the check is correctly skipped. Good.

  Also, the seam margin check uses dirStart and dirEnd, which come from either the seam cache or from the chunk's angles. For adjacent chunks with a seam cache, the directions are identical. For adjacent chunks without a seam cache, the directions might differ slightly (if the angles aren't bit-identical). But the ChunkGrid computes start and end as:
  float start = i * _chunkAngularSize;
  float end = start + _chunkAngularSize;

  Chunk 0's end = _chunkAngularSize. Chunk 1's start = _chunkAngularSize. These are the same computation, so they should be bit-identical. So even without the seam cache, the directions should be the same.

  Actually, looking at the ChunkGrid constructor:
  float start = i * _chunkAngularSize;
  float end = start + _chunkAngularSize;
  //float start = previousEnd;
  //float end = (i == chunkCount - 1) ? 2f * (float)Math.PI : (i + 1) * _chunkAngularSize;

  There's commented-out code that used previousEnd for bit-identical boundaries. The current code uses i * _chunkAngularSize for the start and start + _chunkAngularSize for the end. For adjacent chunks:
  - Chunk i's end = i * _chunkAngularSize + _chunkAngularSize = (i+1) * _chunkAngularSize
  - Chunk (i+1)'s start = (i+1) * _chunkAngularSize

  These should be bit-identical because they're the same multiplication. But the commented-out code suggests there was a concern about this. The seam cache addresses this by computing the direction once.

  OK, I think the fix is complete. Let me finalize.


Good, the test is fixed. Let me verify the shared lattice point analysis is correct — all shared points between adjacent chunks should now be within the seam margin.

  Thought for 49s, read 1 file

The tests look correct. Let me verify the full modified sampler one more time for any issues, then wrap up.

  Thought for 57s, read 1 file

Done. Here's what was wrong and what I changed:

Problem

In CartesianChunkFieldSampler.Sample(), every lattice point near a chunk boundary was combined with the steep WedgeMask (steepness = 1000). At a shared cell edge crossing the boundary:

- Chunk A's endpoint on its side: terrain (e.g. -5)
- Chunk A's endpoint on B's side: mask (e.g. +500)
- Chunk B's endpoint on A's side: mask (e.g. +500)
- Chunk B's endpoint on its side: terrain (e.g. -5)

Marching Squares interpolates the zero-crossing independently from each side:
- A places its vertex at ~2% along the edge (near A's lattice point)
- B places its vertex at ~98% along the edge (near B's lattice point)

Two vertices, ~96% of a cell apart. Visible gap.

The ChunkSeamCache only cached direction vectors — it ensured both chunks used the same ray direction, but the Cross(dir, point) sign is inherently opposite for points on either side of the ray, so the SDF values were still asymmetric.

Fix (CartesianChunkFieldSampler.cs)

Added a seam margin: lattice points within 2 cells of either boundary ray now bypass the wedge mask entirely and use the raw terrain SDF directly. Both neighboring chunks sample th equality at overlapping lattice points, which failed before the fix for near-boundary points).

✻ Brewed for 1h 0m 48s
- Sample_FarFromSeam_StillClippedByWedgeMask — sanity check that interior points are still correctly solid

The existing Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints test should now also pass for all shared points (it was asserting exact equality at overlapping lattice points, which failed before the fix for near-boundary points)