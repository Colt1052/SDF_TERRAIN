using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainBrushTests
    {
        [Test]
        public void Constructor_NonPositiveRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainBrush(BrushMode.Add, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainBrush(BrushMode.Add, -1f));
        }

        [Test]
        public void ToEdit_Add_ProducesNonAdditiveEdit()
        {
            var brush = new TerrainBrush(BrushMode.Add, radius: 3f);
            var position = new Vector2(5f, 0f);

            TerrainEdit edit = brush.ToEdit(position);

            Assert.IsFalse(edit.IsAdditive);
            Assert.AreEqual(position, edit.LocalPosition);
            Assert.AreEqual(3f, edit.Radius);
        }

        [Test]
        public void ToEdit_Remove_ProducesAdditiveEdit()
        {
            var brush = new TerrainBrush(BrushMode.Remove, radius: 2f);
            var position = new Vector2(0f, 7f);

            TerrainEdit edit = brush.ToEdit(position);

            Assert.IsTrue(edit.IsAdditive);
            Assert.AreEqual(position, edit.LocalPosition);
            Assert.AreEqual(2f, edit.Radius);
        }

        [Test]
        public void ToEdit_Smooth_ThrowsInvalidOperationException()
        {
            var brush = new TerrainBrush(BrushMode.Smooth, radius: 3f);
            var position = new Vector2(5f, 0f);

            Assert.Throws<InvalidOperationException>(() => brush.ToEdit(position));
        }

        [Test]
        public void Constructor_SmoothMode_Accepts()
        {
            // Smooth is a valid mode that should construct without error.
            var brush = new TerrainBrush(BrushMode.Smooth, radius: 2f);
            Assert.AreEqual(BrushMode.Smooth, brush.Mode);
            Assert.AreEqual(2f, brush.Radius);
        }

        [Test]
        public void Constructor_ElectricMode_Accepts()
        {
            var brush = new TerrainBrush(BrushMode.Electric, radius: 3f);
            Assert.AreEqual(BrushMode.Electric, brush.Mode);
            Assert.AreEqual(3f, brush.Radius);
        }

        [Test]
        public void ToEdit_Electric_ProducesAdditiveEdit()
        {
            // Electric mode should produce an additive (dig) edit, same as Remove.
            // Note: ChunkTerrainRenderer constructs TerrainEdit directly for Electric mode
            // (to use the strike radius), but ToEdit still supports Electric for callers
            // that need the standard conversion.
            var brush = new TerrainBrush(BrushMode.Electric, radius: 2f);
            var position = new Vector2(5f, 0f);

            TerrainEdit edit = brush.ToEdit(position);

            Assert.IsTrue(edit.IsAdditive);
            Assert.AreEqual(position, edit.LocalPosition);
            Assert.AreEqual(2f, edit.Radius);
        }
    }
}
