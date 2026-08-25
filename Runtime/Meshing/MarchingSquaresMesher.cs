using System;
using UnityEngine;

namespace SDFTerrain.Meshing
{
    /// <summary>
    /// Converts a 2D grid of signed-distance samples into a filled mesh via Marching Squares.
    /// A pure function of its inputs — no side effects, no dependency on Planet/TerrainField, so
    /// it is fully testable against small synthetic fields. Negative samples are solid (inside),
    /// positive samples are air (outside), matching TerrainField.Sample's convention.
    ///
    /// Ambiguous saddle cases (diagonally-opposite corners solid, adjacent corners not) are
    /// resolved by splitting into two disjoint triangles rather than reading a center sample —
    /// a standard simplification that can leave a thin diagonal gap in checkerboard-like input.
    /// Acceptable because chunk resolution is expected to be much finer than terrain features;
    /// revisit only if visible artifacts appear in practice.
    /// </summary>
    public static class MarchingSquaresMesher
    {
        /// <summary>
        /// Generates mesh data for the given sample grid. samples[x, y] is the density at grid
        /// point (x, y); grid point (x, y) is positioned at origin + (x, y) * cellSize in local
        /// space. uvScale controls texture tiling density (UV = position * uvScale).
        /// Vertices are emitted with white vertex colors.
        /// </summary>
        public static MeshData Generate(float[,] samples, float cellSize, Vector2 origin, float uvScale = 1f)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            int width = samples.GetLength(0);
            int height = samples.GetLength(1);
            var meshData = new MeshData();

            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    Vector2 p0 = origin + (new Vector2(x, y) * cellSize);
                    Vector2 p1 = origin + (new Vector2(x + 1, y) * cellSize);
                    Vector2 p2 = origin + (new Vector2(x + 1, y + 1) * cellSize);
                    Vector2 p3 = origin + (new Vector2(x, y + 1) * cellSize);

                    EmitCell(samples[x, y], samples[x + 1, y], samples[x + 1, y + 1], samples[x, y + 1],
                        p0, p1, p2, p3,
                        Color.white, Color.white, Color.white, Color.white,
                        uvScale, meshData);
                }
            }

            return meshData;
        }

        /// <summary>
        /// Generates mesh data from a sample grid whose vertex positions are supplied explicitly
        /// rather than computed from a uniform cell size — used for non-Cartesian grids (e.g. a
        /// chunk's angular/radial wedge, where grid columns are angle steps and rows are radius
        /// steps). <paramref name="positions"/> must have the same dimensions as
        /// <paramref name="samples"/>.
        /// </summary>
        public static MeshData Generate(float[,] samples, Vector2[,] positions, float uvScale = 1f)
        {
            return Generate(samples, positions, colors: null, uvScale);
        }

        /// <summary>
        /// Like <see cref="Generate(float[,],Vector2[,],float)"/> but also propagates per-vertex
        /// colors from <paramref name="colors"/>. When <paramref name="colors"/> is null, vertices
        /// are emitted with default white colors. When non-null, <paramref name="colors"/> must
        /// match the dimensions of <paramref name="samples"/>.
        /// </summary>
        public static MeshData Generate(float[,] samples, Vector2[,] positions, Color[,] colors, float uvScale = 1f)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (positions == null)
            {
                throw new ArgumentNullException(nameof(positions));
            }

            int width = samples.GetLength(0);
            int height = samples.GetLength(1);

            if (positions.GetLength(0) != width || positions.GetLength(1) != height)
            {
                throw new ArgumentException("positions must have the same dimensions as samples.", nameof(positions));
            }

            if (colors != null)
            {
                if (colors.GetLength(0) != width || colors.GetLength(1) != height)
                {
                    throw new ArgumentException("colors must have the same dimensions as samples.", nameof(colors));
                }
            }

            var meshData = new MeshData();

            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    Color c0 = colors != null ? colors[x, y] : Color.white;
                    Color c1 = colors != null ? colors[x + 1, y] : Color.white;
                    Color c2 = colors != null ? colors[x + 1, y + 1] : Color.white;
                    Color c3 = colors != null ? colors[x, y + 1] : Color.white;

                    EmitCell(samples[x, y], samples[x + 1, y], samples[x + 1, y + 1], samples[x, y + 1],
                        positions[x, y], positions[x + 1, y], positions[x + 1, y + 1], positions[x, y + 1],
                        c0, c1, c2, c3, uvScale, meshData);
                }
            }

            return meshData;
        }

        private static void EmitCell(float v0, float v1, float v2, float v3,
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            Color c0, Color c1, Color c2, Color c3,
            float uvScale, MeshData meshData)
        {
            Vector2 e01 = Interp(p0, p1, v0, v1);
            Vector2 e12 = Interp(p1, p2, v1, v2);
            Vector2 e23 = Interp(p2, p3, v2, v3);
            Vector2 e30 = Interp(p3, p0, v3, v0);

            // Interpolate colors at edge crossing points
            Color ce01 = Interp(c0, c1, v0, v1);
            Color ce12 = Interp(c1, c2, v1, v2);
            Color ce23 = Interp(c2, c3, v2, v3);
            Color ce30 = Interp(c3, c0, v3, v0);

            int caseIndex = 0;
            if (v0 < 0f) caseIndex |= 1;
            if (v1 < 0f) caseIndex |= 2;
            if (v2 < 0f) caseIndex |= 4;
            if (v3 < 0f) caseIndex |= 8;

            switch (caseIndex)
            {
                case 0:
                    break;
                case 1:
                    AddTriangle(meshData, uvScale, p0, e01, e30, c0, ce01, ce30);
                    break;
                case 2:
                    AddTriangle(meshData, uvScale, e01, p1, e12, ce01, c1, ce12);
                    break;
                case 3:
                    AddQuad(meshData, uvScale, p0, p1, e12, e30, c0, c1, ce12, ce30);
                    break;
                case 4:
                    AddTriangle(meshData, uvScale, e12, p2, e23, ce12, c2, ce23);
                    break;
                case 5:
                    // Ambiguous saddle: corners 0 and 2 solid, 1 and 3 not. Which of the two
                    // valid triangulations is correct depends on whether the true field is
                    // solid or air at the cell center — decided via the asymptotic decider
                    // (bilinear estimate of the center value) rather than always picking the
                    // same one, which caused visible topology-flip artifacts (a smoothly moving
                    // circle edge would suddenly show a spike/notch as it crossed a saddle cell).
                    if (EstimateCenter(v0, v1, v2, v3) < 0f)
                    {
                        AddHexagon(meshData, uvScale, p0, e01, e12, p2, e23, e30,
                            c0, ce01, ce12, c2, ce23, ce30);
                    }
                    else
                    {
                        AddTriangle(meshData, uvScale, p0, e01, e30, c0, ce01, ce30);
                        AddTriangle(meshData, uvScale, e12, p2, e23, ce12, c2, ce23);
                    }
                    break;
                case 6:
                    AddQuad(meshData, uvScale, e01, p1, p2, e23, ce01, c1, c2, ce23);
                    break;
                case 7:
                    AddPentagon(meshData, uvScale, p0, p1, p2, e23, e30, c0, c1, c2, ce23, ce30);
                    break;
                case 8:
                    AddTriangle(meshData, uvScale, e23, p3, e30, ce23, c3, ce30);
                    break;
                case 9:
                    AddQuad(meshData, uvScale, p0, e01, e23, p3, c0, ce01, ce23, c3);
                    break;
                case 10:
                    // Ambiguous saddle: corners 1 and 3 solid, 0 and 2 not. See case 5's comment
                    // — same asymptotic-decider treatment, mirrored to this saddle's corners.
                    if (EstimateCenter(v0, v1, v2, v3) < 0f)
                    {
                        AddHexagon(meshData, uvScale, e01, p1, e12, e23, p3, e30,
                            ce01, c1, ce12, ce23, c3, ce30);
                    }
                    else
                    {
                        AddTriangle(meshData, uvScale, e01, p1, e12, ce01, c1, ce12);
                        AddTriangle(meshData, uvScale, e23, p3, e30, ce23, c3, ce30);
                    }
                    break;
                case 11:
                    AddPentagon(meshData, uvScale, p0, p1, e12, e23, p3, c0, c1, ce12, ce23, c3);
                    break;
                case 12:
                    AddQuad(meshData, uvScale, e12, p2, p3, e30, ce12, c2, c3, ce30);
                    break;
                case 13:
                    AddPentagon(meshData, uvScale, p0, e01, e12, p2, p3, c0, ce01, ce12, c2, c3);
                    break;
                case 14:
                    AddPentagon(meshData, uvScale, e01, p1, p2, p3, e30, ce01, c1, c2, c3, ce30);
                    break;
                case 15:
                    AddQuad(meshData, uvScale, p0, p1, p2, p3, c0, c1, c2, c3);
                    break;
            }
        }

        /// <summary>
        /// Bilinear estimate of the field value at a cell's center, used as the asymptotic
        /// decider for ambiguous saddle cases 5/10 — resolves which of the two valid
        /// triangulations matches the true underlying field instead of always picking one.
        /// </summary>
        private static float EstimateCenter(float v0, float v1, float v2, float v3)
        {
            return (v0 + v1 + v2 + v3) * 0.25f;
        }

        /// <summary>
        /// The edge crossing point <see cref="Interp"/> would use to place a mesh vertex between
        /// two corner samples, or null if the values don't straddle zero (no crossing on this
        /// edge, matching EmitCell's case table). Exposed for debug visualization so an overlay
        /// can show exactly what the mesher computes, not a re-derived copy that could drift.
        /// </summary>
        public static Vector2? FindEdgeCrossing(Vector2 a, Vector2 b, float valueA, float valueB)
        {
            if ((valueA < 0f) == (valueB < 0f))
            {
                return null;
            }

            return Interp(a, b, valueA, valueB);
        }

        private static Vector2 Interp(Vector2 a, Vector2 b, float valueA, float valueB)
        {
            float denominator = valueA - valueB;
            if (Mathf.Approximately(denominator, 0f))
            {
                return (a + b) * 0.5f;
            }

            float t = valueA / denominator;
            return Vector2.Lerp(a, b, t);
        }

        /// <summary>
        /// Interpolates color along an edge crossing using the same linear factor as
        /// <see cref="Interp(Vector2,Vector2,float,float)"/> so vertex colors blend smoothly
        /// across the contour between grid corners.
        /// <para>
        /// When the edge straddles the air/solid boundary (one corner is air, the other is solid),
        /// the contour vertex uses the solid side's color. This prevents the air material color
        /// (typically white) from bleeding into the terrain surface and creating a washed-out rim.
        /// </para>
        /// </summary>
        private static Color Interp(Color a, Color b, float valueA, float valueB)
        {
            // At the air/solid boundary, prefer the solid side's color.
            bool aIsSolid = valueA < 0f;
            bool bIsSolid = valueB < 0f;
            if (aIsSolid != bIsSolid)
            {
                return aIsSolid ? a : b;
            }

            // Both corners are on the same side — blend normally for interior contours.
            float denominator = valueA - valueB;
            if (Mathf.Approximately(denominator, 0f))
            {
                return (a + b) * 0.5f;
            }

            float t = valueA / denominator;
            return Color.Lerp(a, b, t);
        }

        private static void AddTriangle(MeshData meshData, float uvScale,
            Vector2 a, Vector2 b, Vector2 c,
            Color ca, Color cb, Color cc)
        {
            meshData.AddTriangle(a, b, c, uvScale, ca, cb, cc);
        }

        private static void AddQuad(MeshData meshData, float uvScale,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color ca, Color cb, Color cc, Color cd)
        {
            meshData.AddQuad(a, b, c, d, uvScale, ca, cb, cc, cd);
        }

        private static void AddPentagon(MeshData meshData, float uvScale,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 e,
            Color ca, Color cb, Color cc, Color cd, Color ce)
        {
            meshData.AddTriangle(a, b, c, uvScale, ca, cb, cc);
            meshData.AddTriangle(a, c, d, uvScale, ca, cc, cd);
            meshData.AddTriangle(a, d, e, uvScale, ca, cd, ce);
        }

        private static void AddHexagon(MeshData meshData, float uvScale,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 e, Vector2 f,
            Color ca, Color cb, Color cc, Color cd, Color ce, Color cf)
        {
            meshData.AddTriangle(a, b, c, uvScale, ca, cb, cc);
            meshData.AddTriangle(a, c, d, uvScale, ca, cc, cd);
            meshData.AddTriangle(a, d, e, uvScale, ca, cd, ce);
            meshData.AddTriangle(a, e, f, uvScale, ca, ce, cf);
        }

        /// <summary>
        /// Computes the total solid area of the mesh using the shoelace formula over the triangle
        /// list. All vertices lie in the XY plane (Z=0), so the 3D cross product reduces to a 2D
        /// operation. Because MarchingSquaresMesher produces non-overlapping triangles that tile
        /// the solid region exactly, the sum of triangle areas equals the solid area. Returns 0
        /// for an empty mesh (no triangles).
        /// </summary>
        public static float ComputeSolidArea(MeshData meshData)
        {
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }

            if (meshData.Triangles.Count == 0)
            {
                return 0f;
            }

            float totalArea = 0f;
            var vertices = meshData.Vertices;
            var triangles = meshData.Triangles;

            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                // Shoelace formula for 2D triangle area.
                totalArea += Mathf.Abs(a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y)) * 0.5f;
            }

            return totalArea;
        }
    }
}
