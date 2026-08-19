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
    }
}
