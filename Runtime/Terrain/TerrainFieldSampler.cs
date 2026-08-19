using System;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Samples a <see cref="TerrainField"/> onto a square grid covering the planet's bounding box,
    /// producing the density grid a mesher consumes. A pure function of its inputs — testable
    /// with a real TerrainField and no rendering/collider machinery. Whole-planet sampling is the
    /// simplest approach that satisfies Task 11 (single visible mesh); per-chunk sectioning for
    /// partial rebuilds is deferred to Task 15 (Chunk rebuilding).
    /// </summary>
    public static class TerrainFieldSampler
    {
        public readonly struct Result
        {
            public readonly float[,] Samples;
            public readonly Vector2 Origin;
            public readonly float CellSize;

            public Result(float[,] samples, Vector2 origin, float cellSize)
            {
                Samples = samples;
                Origin = origin;
                CellSize = cellSize;
            }
        }

        /// <summary>
        /// Samples a square grid of side <paramref name="resolution"/> cells, centered on the
        /// planet, extending to <paramref name="maxRadius"/> in every direction.
        /// </summary>
        public static Result Sample(TerrainField field, int resolution, float maxRadius)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Resolution must be positive.");
            }

            if (maxRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "Max radius must be positive.");
            }

            Vector2 bottomLeft = new Vector2(-maxRadius, -maxRadius);
            float cellSize = (2f * maxRadius) / resolution;

            int gridSide = resolution + 1;
            var samples = new float[gridSide, gridSide];

            for (int x = 0; x < gridSide; x++)
            {
                for (int y = 0; y < gridSide; y++)
                {
                    Vector2 localPosition = bottomLeft + new Vector2(x * cellSize, y * cellSize);
                    samples[x, y] = field.Sample(localPosition);
                }
            }

            return new Result(samples, bottomLeft, cellSize);
        }
    }
}
