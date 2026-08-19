using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Meshing;

namespace SDFTerrain.Tests
{
    public class MarchingSquaresMesherTests
    {
        [Test]
        public void Generate_NullSamples_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MarchingSquaresMesher.Generate(null, 1f, Vector2.zero));
        }

        [Test]
        public void Generate_NonPositiveCellSize_Throws()
        {
            var samples = new float[2, 2];
            Assert.Throws<ArgumentOutOfRangeException>(() => MarchingSquaresMesher.Generate(samples, 0f, Vector2.zero));
        }

        [Test]
        public void Generate_AllPositive_ProducesNoGeometry()
        {
            var samples = new float[3, 3];
            Fill(samples, 1f);

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(0, mesh.Triangles.Count);
        }

        [Test]
        public void Generate_AllNegative_ProducesFullyFilledGrid()
        {
            var samples = new float[3, 3];
            Fill(samples, -1f);

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            // A 2x2 grid of cells, each fully solid (case 15) emits 2 triangles per cell.
            Assert.AreEqual(4 * 2, mesh.Triangles.Count / 3);
        }

        [Test]
        public void Generate_SingleSolidCorner_ProducesOneTriangle()
        {
            var samples = new float[2, 2];
            samples[0, 0] = -1f;
            samples[1, 0] = 1f;
            samples[1, 1] = 1f;
            samples[0, 1] = 1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(1, mesh.Triangles.Count / 3);
        }

        [Test]
        public void Generate_HalfSolidGrid_ProducesQuad()
        {
            var samples = new float[2, 2];
            samples[0, 0] = -1f;
            samples[1, 0] = -1f;
            samples[1, 1] = 1f;
            samples[0, 1] = 1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(2, mesh.Triangles.Count / 3);
        }

        [Test]
        public void Generate_VerticesLieOnCellBoundsForFullyFilledGrid()
        {
            var samples = new float[2, 2];
            Fill(samples, -1f);

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 2f, Vector2.zero);

            foreach (Vector3 vertex in mesh.Vertices)
            {
                Assert.GreaterOrEqual(vertex.x, 0f);
                Assert.LessOrEqual(vertex.x, 2f);
                Assert.GreaterOrEqual(vertex.y, 0f);
                Assert.LessOrEqual(vertex.y, 2f);
            }
        }

        [Test]
        public void Generate_OriginOffsetsAllVertices()
        {
            var samples = new float[2, 2];
            Fill(samples, -1f);
            var origin = new Vector2(10f, 20f);

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, origin);

            foreach (Vector3 vertex in mesh.Vertices)
            {
                Assert.GreaterOrEqual(vertex.x, origin.x);
                Assert.GreaterOrEqual(vertex.y, origin.y);
            }
        }

        [Test]
        public void Generate_NormalsAndVerticesAndUVsAreSameCount()
        {
            var samples = new float[3, 3];
            samples[0, 0] = -1f;
            samples[1, 0] = -1f;
            samples[2, 0] = 1f;
            samples[0, 1] = -1f;
            samples[1, 1] = 1f;
            samples[2, 1] = 1f;
            samples[0, 2] = 1f;
            samples[1, 2] = 1f;
            samples[2, 2] = 1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(mesh.Vertices.Count, mesh.Normals.Count);
            Assert.AreEqual(mesh.Vertices.Count, mesh.UVs.Count);
        }

        [Test]
        public void Generate_AdjacentCellsSharingAnEdge_DeduplicateSharedVertices()
        {
            // A 3x3 grid of fully-solid samples produces a 2x2 block of cells; the interior
            // shared corner/edge vertices between adjacent cells must be deduplicated rather
            // than emitted as separate copies per triangle.
            var samples = new float[3, 3];
            Fill(samples, -1f);

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            // Fully solid 3x3 samples -> 3x3 unique vertex positions, far fewer than 3 per
            // triangle (4 cells * 2 triangles * 3 = 24 without dedup).
            Assert.AreEqual(9, mesh.Vertices.Count);
            Assert.Less(mesh.Vertices.Count, mesh.Triangles.Count);
        }

        [Test]
        public void Generate_AmbiguousSaddleCase_WithAirCenterEstimate_ProducesTwoDisjointTriangles()
        {
            // Diagonal corners solid (0,0) and (1,1), opposite corners not: saddle case 5.
            // Bilinear center estimate is exactly zero here (symmetric magnitudes), which the
            // asymptotic decider treats as air, keeping the two solid corners disjoint.
            var samples = new float[2, 2];
            samples[0, 0] = -1f;
            samples[1, 0] = 1f;
            samples[1, 1] = -1f;
            samples[0, 1] = 1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(2, mesh.Triangles.Count / 3);
        }

        [Test]
        public void Generate_AmbiguousSaddleCase_WithSolidCenterEstimate_ProducesMergedHexagon()
        {
            // Same saddle corners as above, but the non-solid corners are only weakly positive
            // while the solid corners are strongly negative, so the bilinear center estimate is
            // negative (solid). The asymptotic decider should then connect the two solid corners
            // through the center as one hexagon instead of leaving them disjoint.
            var samples = new float[2, 2];
            samples[0, 0] = -10f;
            samples[1, 0] = 1f;
            samples[1, 1] = -10f;
            samples[0, 1] = 1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            Assert.AreEqual(4, mesh.Triangles.Count / 3);
        }

        [Test]
        public void FindEdgeCrossing_SameSignCorners_ReturnsNull()
        {
            Vector2? crossing = MarchingSquaresMesher.FindEdgeCrossing(Vector2.zero, Vector2.right, 1f, 2f);

            Assert.IsNull(crossing);
        }

        [Test]
        public void FindEdgeCrossing_OppositeSignCorners_ReturnsLerpedPoint()
        {
            Vector2? crossing = MarchingSquaresMesher.FindEdgeCrossing(Vector2.zero, Vector2.right, -1f, 3f);

            Assert.IsNotNull(crossing);
            Vector2 expected = Vector2.Lerp(Vector2.zero, Vector2.right, -1f / (-1f - 3f));
            Assert.AreEqual(expected.x, crossing.Value.x, 1e-5f);
            Assert.AreEqual(expected.y, crossing.Value.y, 1e-5f);
        }

        [Test]
        public void FindEdgeCrossing_EqualOppositeSignValues_FallsBackToMidpoint()
        {
            Vector2? crossing = MarchingSquaresMesher.FindEdgeCrossing(Vector2.zero, Vector2.right, -1f, 1f);

            Assert.IsNotNull(crossing);
            Assert.AreEqual(0.5f, crossing.Value.x, 1e-5f);
        }

        private static void Fill(float[,] samples, float value)
        {
            for (int x = 0; x < samples.GetLength(0); x++)
            {
                for (int y = 0; y < samples.GetLength(1); y++)
                {
                    samples[x, y] = value;
                }
            }
        }
    }
}
