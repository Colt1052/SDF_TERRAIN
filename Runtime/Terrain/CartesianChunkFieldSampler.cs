using System;
using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Samples a <see cref="TerrainField"/> over a single <see cref="TerrainChunk"/>'s angular
    /// wedge onto a fixed-size, axis-aligned Cartesian lattice (world units), producing a grid
    /// consumable by <see cref="Meshing.MarchingSquaresMesher"/>'s position-grid overload.
    /// Replaces the old angle/radius polar sampler: because every chunk samples the *same* global
    /// lattice (anchored at the planet's local origin), terrain/carve resolution is uniform
    /// everywhere on the planet, independent of both radius and angular position — a polar grid's
    /// arc-length-per-step shrinks near the center and grows near the rim, which made brush carves
    /// look like faceted arc segments rather than smooth circles.
    ///
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
        public readonly struct Result
        {
            public readonly float[,] Samples;
            public readonly Vector2[,] Positions;

            public Result(float[,] samples, Vector2[,] positions)
            {
                Samples = samples;
                Positions = positions;
            }
        }

        /// <summary>
        /// Samples the given chunk's angular wedge (from radius 0 to maxRadius) onto a Cartesian
        /// lattice with the given cell size (world units), clipped to the chunk's [StartAngle,
        /// EndAngle] range. When <paramref name="seamCache"/> is supplied, the boundary direction
        /// vectors used for the wedge clip come from it instead of being derived from this chunk's
        /// own StartAngle/EndAngle — this is what guarantees a neighboring chunk's sampling of the
        /// same shared ray uses the exact same direction values (see <see cref="ChunkSeamCache"/>).
        /// </summary>
        public static Result Sample(TerrainField field, TerrainChunk chunk, float maxRadius, float cellSize, ChunkSeamCache seamCache = null)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (maxRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "Max radius must be positive.");
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            float startAngle = chunk.StartAngle;
            float endAngle = chunk.EndAngle;
            float angularSize = endAngle - startAngle;
            bool fullCircle = angularSize >= (2f * Mathf.PI) - 1e-4f;

            (int ixMin, int ixMax, int iyMin, int iyMax) = ComputeLatticeBounds(startAngle, endAngle, maxRadius, cellSize, fullCircle);

            int width = ixMax - ixMin + 1;
            int height = iyMax - iyMin + 1;
            var samples = new float[width, height];
            var positions = new Vector2[width, height];

            Vector2 dirStart = seamCache != null ? seamCache.GetStartDirection(chunk.Index) : RadialMath.DirectionAt(startAngle);
            Vector2 dirEnd = seamCache != null ? seamCache.GetEndDirection(chunk.Index) : RadialMath.DirectionAt(endAngle);
            bool reflexWedge = angularSize > Mathf.PI;

            for (int i = 0; i < width; i++)
            {
                float x = (ixMin + i) * cellSize;

                for (int j = 0; j < height; j++)
                {
                    float y = (iyMin + j) * cellSize;
                    var position = new Vector2(x, y);

                    // Chunk-agnostic Sample(), not the chunk-indexed overload: chunks
                    // intentionally sample an overlapping lattice near their shared boundary (see
                    // ComputeLatticeBounds' 1-cell margin) so both sides agree on the border
                    // vertex position. The chunk-indexed overload decides edit membership per
                    // chunk via a coarse angular cone test, which is not guaranteed to treat a
                    // shared boundary point identically from both chunks once an edit lands near
                    // it -- that mismatch is what caused visible seams after digging/building near
                    // a chunk border. Scanning every edit here keeps the shared lattice points
                    // bit-identical regardless of which chunk samples them.
                    float terrainValue = field.Sample(position);
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
            }

            return new Result(samples, positions);
        }

        /// <summary>
        /// How steeply the angular-boundary half-plane terms ramp from "definitely inside" to
        /// "definitely outside", in field units per world unit of perpendicular distance from the
        /// boundary ray. Must be steep enough that, beyond roughly one lattice cell from the
        /// boundary, the mask's magnitude dwarfs any real terrain SampleContribution (which is
        /// bounded by brush/planet radii, typically a handful to a few hundred world units) so
        /// Max() below always lets real terrain win there — a plain unscaled perpendicular
        /// distance (the previous behavior) grows only proportionally to distance-from-boundary,
        /// which at large radius stays smaller than the real (deeply negative, solid-rock)
        /// terrain value for many cells in from the cut, so it wrongly overrode real terrain and
        /// manufactured a phantom air seam running the full length of every chunk boundary
        /// instead of only exactly at the cut line.
        /// </summary>
        private const float WedgeMaskSteepness = 1000f;

        /// <summary>
        /// Signed "distance-like" value: negative inside the angular wedge [startAngle, endAngle]
        /// and within maxRadius, positive outside. Not an exact Euclidean distance — the angular
        /// terms are steepened (see <see cref="WedgeMaskSteepness"/>) so they only compete with
        /// the real terrain value in a thin band right at the cut line, correctly signed and
        /// continuous there, which is all Marching Squares' linear edge interpolation needs to
        /// place the cut vertex precisely on the boundary ray.
        /// </summary>
        private static float WedgeMask(Vector2 position, Vector2 dirStart, Vector2 dirEnd, float maxRadius, bool reflexWedge)
        {
            float radiusMask = position.magnitude - maxRadius;

            if (!reflexWedge)
            {
                // Convex wedge (angular size <= PI): intersection (max) of the two boundary
                // half-planes directly describes the wedge interior.
                float maskStart = -Cross(dirStart, position) * WedgeMaskSteepness;
                float maskEnd = Cross(dirEnd, position) * WedgeMaskSteepness;
                return Mathf.Max(radiusMask, Mathf.Max(maskStart, maskEnd));
            }

            // Reflex wedge (angular size > PI): the wedge itself is concave, so intersecting its
            // two boundary half-planes directly would describe the wrong (complementary) region.
            // Instead compute the mask for the complementary convex wedge [endAngle, startAngle +
            // 2*PI] (angular size < PI, so the same half-plane intersection is valid there) and
            // negate it: inside the original reflex wedge is exactly outside its convex complement.
            float complementMaskStart = -Cross(dirEnd, position) * WedgeMaskSteepness;
            float complementMaskEnd = Cross(dirStart, position) * WedgeMaskSteepness;
            float complementMask = Mathf.Max(complementMaskStart, complementMaskEnd);
            return Mathf.Max(radiusMask, -complementMask);
        }

        /// <summary>2D cross product / perpendicular dot product: dir.x * p.y - dir.y * p.x.</summary>
        private static float Cross(Vector2 dir, Vector2 p)
        {
            return (dir.x * p.y) - (dir.y * p.x);
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
        /// </summary>
        private static (int ixMin, int ixMax, int iyMin, int iyMax) ComputeLatticeBounds(
            float startAngle, float endAngle, float maxRadius, float cellSize, bool fullCircle)
        {
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

            void Include(Vector2 p)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            if (fullCircle)
            {
                Include(new Vector2(-maxRadius, -maxRadius));
                Include(new Vector2(maxRadius, maxRadius));
            }
            else
            {
                Include(RadialMath.PositionAt(startAngle, maxRadius));
                Include(RadialMath.PositionAt(endAngle, maxRadius));

                // Rim points where x or y reaches +/- maxRadius (axis-aligned angles), if that
                // angle falls within the wedge — these are where the bounding box can extend
                // beyond the two endpoint rim points.
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
            }

            int ixMin = Mathf.FloorToInt(minX / cellSize) - 1;
            int ixMax = Mathf.CeilToInt(maxX / cellSize) + 1;
            int iyMin = Mathf.FloorToInt(minY / cellSize) - 1;
            int iyMax = Mathf.CeilToInt(maxY / cellSize) + 1;

            return (ixMin, ixMax, iyMin, iyMax);
        }
    }
}
