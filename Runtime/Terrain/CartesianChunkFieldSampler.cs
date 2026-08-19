using System;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Samples a <see cref="TerrainField"/> over a single <see cref="TerrainChunk"/>'s rectangular
    /// bounding box onto a fixed-size, axis-aligned Cartesian lattice (world units), producing a
    /// grid consumable by <see cref="Meshing.MarchingSquaresMesher"/>'s position-grid overload.
    ///
    /// Each chunk samples the field freely within its bounding box — no clipping mask is applied.
    /// Lattice points outside the planet's surface naturally read as air (positive SDF), so
    /// Marching Squares produces no contour in all-air regions. Chunks that lie partially outside
    /// the planet simply render nothing for those regions.
    ///
    /// The lattice is expanded by one cell in each direction so boundary-straddling cells have a
    /// neighbor sample to interpolate against. Because every chunk samples the *same* global
    /// Cartesian lattice, adjacent chunks naturally share boundary lattice points with identical
    /// terrain values — no seam cache or special margin logic is needed.
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
        /// Samples the given chunk's rectangular bounding box onto a Cartesian lattice with the
        /// given cell size (world units). The lattice is expanded by one cell in each direction
        /// so boundary-straddling cells are fully included for correct Marching Squares interpolation.
        /// </summary>
        public static Result Sample(TerrainField field, TerrainChunk chunk, float cellSize)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            // Compute lattice index bounds: chunk bounding box expanded by 1 cell.
            int ixMin = Mathf.FloorToInt(chunk.MinX / cellSize) - 1;
            int ixMax = Mathf.CeilToInt(chunk.MaxX / cellSize) + 1;
            int iyMin = Mathf.FloorToInt(chunk.MinY / cellSize) - 1;
            int iyMax = Mathf.CeilToInt(chunk.MaxY / cellSize) + 1;

            int width = ixMax - ixMin + 1;
            int height = iyMax - iyMin + 1;
            var samples = new float[width, height];
            var positions = new Vector2[width, height];

            for (int i = 0; i < width; i++)
            {
                float x = (ixMin + i) * cellSize;

                for (int j = 0; j < height; j++)
                {
                    float y = (iyMin + j) * cellSize;
                    var position = new Vector2(x, y);

                    // Chunk-agnostic Sample(), not the chunk-indexed overload: chunks
                    // intentionally sample an overlapping lattice near their shared boundary
                    // (the 1-cell margin) so both sides agree on the border vertex position.
                    // The chunk-indexed overload decides edit membership per chunk via a
                    // coarse rectangular test, which is not guaranteed to treat a shared
                    // boundary point identically from both chunks once an edit lands near
                    // it. Scanning every edit here keeps the shared lattice points
                    // bit-identical regardless of which chunk samples them.
                    float terrainValue = field.Sample(position);

                    positions[i, j] = position;
                    samples[i, j] = terrainValue;
                }
            }

            return new Result(samples, positions);
        }
    }
}
