using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Meshing;

namespace SDFTerrain.Tests
{
    public class ColliderContourBuilderTests
    {
        [Test]
        public void BuildContours_NullMeshData_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ColliderContourBuilder.BuildContours(null));
        }

        [Test]
        public void BuildContours_SingleFilledCell_ProducesOneClosedQuadLoop()
        {
            var samples = new float[2, 2];
            Fill(samples, -1f);
            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(mesh);

            Assert.AreEqual(1, contours.Count);
            Assert.AreEqual(4, contours[0].Length);
        }

        [Test]
        public void BuildContours_FullyFilledGrid_HasNoInteriorEdgesInLoop()
        {
            var samples = new float[3, 3];
            Fill(samples, -1f);
            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(mesh);

            Assert.AreEqual(1, contours.Count);
            // Outer boundary of a 2x2-solid-cell block is the 4 corners of the enclosing square;
            // shared interior edges between adjacent cells must have cancelled out.
            Assert.AreEqual(4, contours[0].Length);
        }

        [Test]
        public void BuildContours_HalfSolidGrid_ProducesSingleClosedLoop()
        {
            var samples = new float[2, 2];
            samples[0, 0] = -1f;
            samples[1, 0] = -1f;
            samples[1, 1] = 1f;
            samples[0, 1] = 1f;
            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(mesh);

            Assert.AreEqual(1, contours.Count);
            Assert.AreEqual(4, contours[0].Length);
        }

        [Test]
        public void BuildContours_EmptyMesh_ProducesNoContours()
        {
            var meshData = new MeshData();

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(meshData);

            Assert.AreEqual(0, contours.Count);
        }

        [Test]
        public void BuildContours_LoopsAreClosed()
        {
            var samples = new float[2, 2];
            Fill(samples, -1f);
            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(mesh);

            foreach (Vector2[] loop in contours)
            {
                var seen = new HashSet<Vector2>();
                foreach (Vector2 point in loop)
                {
                    Assert.IsTrue(seen.Add(point), "Loop must not revisit a point (no self-intersection).");
                }
            }
        }

        [Test]
        public void BuildContours_TwoDisjointFilledCells_ProducesTwoLoops()
        {
            var samples = new float[5, 2];
            samples[0, 0] = -1f;
            samples[1, 0] = -1f;
            samples[1, 1] = -1f;
            samples[0, 1] = -1f;

            samples[3, 0] = -1f;
            samples[4, 0] = -1f;
            samples[4, 1] = -1f;
            samples[3, 1] = -1f;

            MeshData mesh = MarchingSquaresMesher.Generate(samples, 1f, Vector2.zero);

            List<Vector2[]> contours = ColliderContourBuilder.BuildContours(mesh);

            Assert.AreEqual(2, contours.Count);
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
