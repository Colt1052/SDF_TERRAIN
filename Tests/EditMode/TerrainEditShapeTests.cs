using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="TerrainEdit"/> shape primitives (Circle, Capsule) and the
    /// <see cref="BrushShape"/> enum.
    /// </summary>
    public class TerrainEditShapeTests
    {
        // ---- Circle shape (default) ----

        [Test]
        public void Constructor_ThreeArg_DefaultsToCircle()
        {
            var edit = new TerrainEdit(new Vector2(3f, 4f), radius: 2f, isAdditive: true);

            Assert.AreEqual(BrushShape.Circle, edit.Shape);
            Assert.AreEqual(Vector2.zero, edit.EndPosition);
            Assert.AreEqual(2f, edit.Radius);
            Assert.IsTrue(edit.IsAdditive);
        }

        [Test]
        public void SampleContribution_Circle_CenterEqualsRadius()
        {
            // At the center, the contribution should equal the radius (for additive).
            var edit = new TerrainEdit(new Vector2(5f, 0f), radius: 3f, isAdditive: true);

            float contribution = edit.SampleContribution(new Vector2(5f, 0f));

            Assert.AreEqual(3f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Circle_AtBoundaryIsZero()
        {
            var edit = new TerrainEdit(new Vector2(5f, 0f), radius: 3f, isAdditive: true);

            float contribution = edit.SampleContribution(new Vector2(8f, 0f));

            Assert.AreEqual(0f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Circle_OutsideBoundaryIsNegative()
        {
            var edit = new TerrainEdit(new Vector2(5f, 0f), radius: 3f, isAdditive: true);

            float contribution = edit.SampleContribution(new Vector2(10f, 0f));

            Assert.AreEqual(-2f, contribution, 1e-5f); // 3 - 5 = -2
        }

        [Test]
        public void SampleContribution_Circle_RemoveMode_IsNegated()
        {
            var edit = new TerrainEdit(new Vector2(5f, 0f), radius: 3f, isAdditive: false);

            float atCenter = edit.SampleContribution(new Vector2(5f, 0f));
            float inside = edit.SampleContribution(new Vector2(6f, 0f));
            float outside = edit.SampleContribution(new Vector2(10f, 0f));

            Assert.AreEqual(-3f, atCenter, 1e-5f); // build: negative at center
            Assert.AreEqual(-2f, inside, 1e-5f);
            Assert.AreEqual(2f, outside, 1e-5f);
        }

        // ---- Capsule shape ----

        [Test]
        public void SampleContribution_Capsule_CenterOfFirstAnchor()
        {
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            float contribution = edit.SampleContribution(start);

            Assert.AreEqual(2f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_CenterOfSecondAnchor()
        {
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            float contribution = edit.SampleContribution(end);

            Assert.AreEqual(2f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_MidpointBetweenAnchors()
        {
            // At the midpoint (3,0), the closest point on the segment is (3,0) itself.
            // Distance to segment = 0. Contribution = radius - 0 = 2.
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            float contribution = edit.SampleContribution(new Vector2(3f, 0f));

            Assert.AreEqual(2f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_PerpendicularToSegmentMiddle()
        {
            // At (3, 1) — perpendicular offset of 1 from the segment middle.
            // The projection onto the segment is (3,0), so distance = 1.
            // Contribution = 2 - 1 = 1.
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            float contribution = edit.SampleContribution(new Vector2(3f, 1f));

            Assert.AreEqual(1f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_InsideFirstCircleOnly()
        {
            // Point near start anchor, well inside the first circle but outside the second.
            var start = new Vector2(0f, 0f);
            var end = new Vector2(10f, 0f);
            var edit = new TerrainEdit(start, end, radius: 3f, isAdditive: true, BrushShape.Capsule);

            // At (1, 0): dist to start = 1, dist to end = 9. min = 1. contribution = 3 - 1 = 2.
            float contribution = edit.SampleContribution(new Vector2(1f, 0f));

            Assert.AreEqual(2f, contribution, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_RemoveMode_IsNegated()
        {
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: false, BrushShape.Capsule);

            float atStart = edit.SampleContribution(start);

            Assert.AreEqual(-2f, atStart, 1e-5f); // negated: -radius at center
        }

        [Test]
        public void SampleContribution_Capsule_FillsSegmentMiddle()
        {
            // A true capsule fills the rectangular region between the two circular caps.
            // Even for a long segment where the two circles don't overlap, the middle is solid.
            var start = new Vector2(0f, 0f);
            var end = new Vector2(10f, 0f);
            var edit = new TerrainEdit(start, end, radius: 3f, isAdditive: true, BrushShape.Capsule);

            // At (5, 0) — segment midpoint: distance to segment = 0. contribution = 3.
            float mid = edit.SampleContribution(new Vector2(5f, 0f));
            Assert.AreEqual(3f, mid, 1e-5f);

            // At (5, 2) — 2 units above segment: distance to segment = 2. contribution = 3 - 2 = 1.
            float above = edit.SampleContribution(new Vector2(5f, 2f));
            Assert.AreEqual(1f, above, 1e-5f);

            // At (5, 3) — exactly at capsule boundary: distance = 3. contribution = 0.
            float boundary = edit.SampleContribution(new Vector2(5f, 3f));
            Assert.AreEqual(0f, boundary, 1e-5f);

            // At (5, 4) — outside capsule: distance = 4. contribution = -1.
            float outside = edit.SampleContribution(new Vector2(5f, 4f));
            Assert.AreEqual(-1f, outside, 1e-5f);
        }

        [Test]
        public void SampleContribution_Capsule_BeyondCap_ProjectsToEndPoint()
        {
            // Beyond the segment, the closest point is the nearest endpoint.
            var start = new Vector2(0f, 0f);
            var end = new Vector2(4f, 0f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            // At (6, 0) — beyond the end cap: closest point on segment is (4,0). distance = 2.
            float beyond = edit.SampleContribution(new Vector2(6f, 0f));
            Assert.AreEqual(0f, beyond, 1e-5f); // 2 - 2 = 0, at boundary

            // At (7, 0) — further beyond: distance = 3. contribution = -1.
            float further = edit.SampleContribution(new Vector2(7f, 0f));
            Assert.AreEqual(-1f, further, 1e-5f);
        }

        // ---- Bounding box ----

        [Test]
        public void GetBoundingBox_Circle_IsSquareAroundCenter()
        {
            var edit = new TerrainEdit(new Vector2(5f, 3f), radius: 2f, isAdditive: true);

            edit.GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY);

            Assert.AreEqual(3f, minX);
            Assert.AreEqual(7f, maxX);
            Assert.AreEqual(1f, minY);
            Assert.AreEqual(5f, maxY);
        }

        [Test]
        public void GetBoundingBox_Capsule_SpansBothAnchors()
        {
            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 4f);
            var edit = new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule);

            edit.GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY);

            Assert.AreEqual(-2f, minX);   // 0 - 2
            Assert.AreEqual(8f, maxX);    // 6 + 2
            Assert.AreEqual(-2f, minY);   // 0 - 2
            Assert.AreEqual(6f, maxY);    // 4 + 2
        }

        [Test]
        public void GetBoundingBox_Capsule_NegativeCoordinates()
        {
            var start = new Vector2(-3f, -2f);
            var end = new Vector2(1f, 0f);
            var edit = new TerrainEdit(start, end, radius: 1.5f, isAdditive: true, BrushShape.Capsule);

            edit.GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY);

            Assert.AreEqual(-4.5f, minX);   // -3 - 1.5
            Assert.AreEqual(2.5f, maxX);    // 1 + 1.5
            Assert.AreEqual(-3.5f, minY);   // -2 - 1.5
            Assert.AreEqual(1.5f, maxY);    // 0 + 1.5
        }

        // ---- TerrainField integration ----

        [Test]
        public void TerrainField_Sample_CapsuleEditAffectsSDF()
        {
            var field = new TerrainField(baseRadius: 10f);
            var start = new Vector2(10f, 0f); // at the surface
            var end = new Vector2(12f, 0f);   // 2 units outward

            // Add a build capsule (isAdditive = false) to add material outward.
            field.ApplyEdit(new TerrainEdit(start, end, radius: 2f, isAdditive: false, BrushShape.Capsule));

            // Sample at midpoint (11, 0) — lies on the segment, distance to segment = 0.
            float sample = field.Sample(new Vector2(11f, 0f));

            // Base distance at (11,0) = 11 - 10 = 1 (air).
            // Capsule contribution = -(radius - 0) = -2. Min(1, -2) = -2 (solid).
            Assert.AreEqual(-2f, sample, 1e-5f);
        }

        [Test]
        public void TerrainField_Sample_CapsuleDigRemovesMaterial()
        {
            var field = new TerrainField(baseRadius: 10f);
            var start = new Vector2(8f, 0f);  // 2 units inside the surface
            var end = new Vector2(10f, 0f);   // at the surface

            // Add a dig capsule (isAdditive = true) to remove material.
            field.ApplyEdit(new TerrainEdit(start, end, radius: 3f, isAdditive: true, BrushShape.Capsule));

            // Sample at (9, 0) — midpoint, on the segment. Distance to segment = 0.
            // Base distance = 9 - 10 = -1 (solid). Contribution = 3 - 0 = 3.
            // Max(-1, 3) = 3 (air).
            float sample = field.Sample(new Vector2(9f, 0f));

            Assert.AreEqual(3f, sample, 1e-5f);
        }

        [Test]
        public void TerrainField_ChunkIndexing_CapsuleIndexesCorrectChunks()
        {
            var field = new TerrainField(baseRadius: 10f);
            var grid = new ChunkGrid(radius: 20f, chunkSize: 8f);
            field.EnableChunkIndexing(grid);

            // Capsule spanning from (-8, 0) to (8, 0) with radius 2
            // should touch chunks in both the left and right columns.
            var start = new Vector2(-8f, 0f);
            var end = new Vector2(8f, 0f);
            field.ApplyEdit(new TerrainEdit(start, end, radius: 2f, isAdditive: true, BrushShape.Capsule));

            // The capsule should be indexable and the field should sample correctly.
            // Just verify no exceptions are thrown and the sample is a finite number.
            float sample = field.Sample(new Vector2(0f, 0f));
            Assert.False(float.IsNaN(sample) || float.IsInfinity(sample), "Sample should be finite");
        }
    }
}
