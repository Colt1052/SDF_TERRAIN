using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    /// <summary>
    /// Verifies that material sampling correctly maps terrain positions to materials based on
    /// depth below the SDF surface. Tests use a plain sphere (no noise) so depths are
    /// predictable: at a radius of (baseRadius - D) the depth is exactly D.
    /// </summary>
    public class MaterialSamplerTests
    {
        private const float BaseRadius = 10f;
        private const float DirtDepth = 3f;
        private const float StoneDepth = 8f;

        private MaterialDatabase _database;
        private TerrainField _field;
        private MaterialSampleSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _database = new MaterialDatabase();
            _database.AddMaterial(CreateDefinition("air", "Air"));
            _database.AddMaterial(CreateDefinition("dirt", "Dirt"));
            _database.AddMaterial(CreateDefinition("stone", "Stone"));
            _database.AddMaterial(CreateDefinition("ice", "Ice"));

            _field = new TerrainField(BaseRadius);
            _settings = MaterialSampleSettings.DirtStoneIce(DirtDepth, StoneDepth, airMaterialId: "air");
        }

        [TearDown]
        public void TearDown()
        {
            _database.Clear();
        }

        #region Sample - Depth-based lookup

        [Test]
        public void Sample_InAir_ReturnsFallbackMaterial()
        {
            // Position well outside the sphere
            Vector2 pos = new Vector2(0f, BaseRadius + 5f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("air", material.Id);
        }

        [Test]
        public void Sample_AtSurface_ReturnsDirt()
        {
            // Exactly on the surface: depth = 0, should be in [0, DirtDepth) band
            Vector2 pos = new Vector2(0f, BaseRadius);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("dirt", material.Id);
        }

        [Test]
        public void Sample_ShallowDepth_ReturnsDirt()
        {
            // 1 unit below surface: depth = 1, inside dirt band [0, 3)
            Vector2 pos = new Vector2(0f, BaseRadius - 1f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("dirt", material.Id);
        }

        [Test]
        public void Sample_AtDirtBoundary_ReturnsStone()
        {
            // Exactly at dirt/stone boundary: depth = 3, dirt is [0, 3) so stone [3, 8) matches
            Vector2 pos = new Vector2(0f, BaseRadius - DirtDepth);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("stone", material.Id);
        }

        [Test]
        public void Sample_MediumDepth_ReturnsStone()
        {
            // 5 units below surface: depth = 5, inside stone band [3, 8)
            Vector2 pos = new Vector2(0f, BaseRadius - 5f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("stone", material.Id);
        }

        [Test]
        public void Sample_AtStoneBoundary_ReturnsIce()
        {
            // Exactly at stone/ice boundary: depth = 8
            Vector2 pos = new Vector2(0f, BaseRadius - StoneDepth);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("ice", material.Id);
        }

        [Test]
        public void Sample_DeepDepth_ReturnsIce()
        {
            // 15 units below surface: depth = 15, inside ice band [8, +inf)
            Vector2 pos = new Vector2(0f, BaseRadius - 15f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("ice", material.Id);
        }

        [Test]
        public void Sample_AtCenter_ReturnsIce()
        {
            // Center of planet: depth = BaseRadius = 10, well inside ice band
            Vector2 pos = Vector2.zero;

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("ice", material.Id);
        }

        #endregion

        #region Sample - Different angles (sphere is uniform)

        [Test]
        public void Sample_XAxis_ReturnsDirtAtShallowDepth()
        {
            Vector2 pos = new Vector2(BaseRadius - 1f, 0f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("dirt", material.Id);
        }

        [Test]
        public void Sample_Diagonal_ReturnsCorrectMaterial()
        {
            // Diagonal direction at 1 unit below surface
            Vector2 dir = Vector2.Normalize(new Vector2(1f, 1f));
            Vector2 pos = dir * (BaseRadius - 1f);

            var material = MaterialSampler.Sample(_field, pos, _settings, _database);

            Assert.AreEqual("dirt", material.Id);
        }

        #endregion

        #region SampleId

        [Test]
        public void SampleId_InAir_ReturnsFallbackId()
        {
            Vector2 pos = new Vector2(0f, BaseRadius + 5f);
            string id = MaterialSampler.SampleId(_field, pos, _settings);

            Assert.AreEqual("air", id);
        }

        [Test]
        public void SampleId_InDirtZone_ReturnsDirt()
        {
            Vector2 pos = new Vector2(0f, BaseRadius - 1f);
            string id = MaterialSampler.SampleId(_field, pos, _settings);

            Assert.AreEqual("dirt", id);
        }

        [Test]
        public void SampleId_InStoneZone_ReturnsStone()
        {
            Vector2 pos = new Vector2(0f, BaseRadius - 5f);
            string id = MaterialSampler.SampleId(_field, pos, _settings);

            Assert.AreEqual("stone", id);
        }

        [Test]
        public void SampleId_InIceZone_ReturnsIce()
        {
            Vector2 pos = new Vector2(0f, BaseRadius - 15f);
            string id = MaterialSampler.SampleId(_field, pos, _settings);

            Assert.AreEqual("ice", id);
        }

        #endregion

        #region Edge cases

        [Test]
        public void Sample_NullField_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MaterialSampler.Sample(null, Vector2.zero, _settings, _database));
        }

        [Test]
        public void Sample_NullDatabase_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MaterialSampler.Sample(_field, Vector2.zero, _settings, null));
        }

        [Test]
        public void SampleId_NullField_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MaterialSampler.SampleId(null, Vector2.zero, _settings));
        }

        [Test]
        public void Sample_UnknownMaterialId_Throws()
        {
            // Settings reference "ice" but we remove it from the database
            _database.Clear();
            _database.AddMaterial(CreateDefinition("air"));
            _database.AddMaterial(CreateDefinition("dirt"));
            _database.AddMaterial(CreateDefinition("stone"));

            Vector2 pos = new Vector2(0f, BaseRadius - 10f);

            Assert.Throws<KeyNotFoundException>(() =>
                MaterialSampler.Sample(_field, pos, _settings, _database));
        }

        #endregion

        #region MaterialSampleSettings validation

        [Test]
        public void MaterialSampleSettings_NullBands_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MaterialSampleSettings(null, "air"));
        }

        [Test]
        public void MaterialSampleSettings_EmptyBands_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MaterialSampleSettings(new MaterialBand[0], "air"));
        }

        [Test]
        public void MaterialSampleSettings_NullFallback_Throws()
        {
            var bands = new MaterialBand[] { new MaterialBand("stone", 0f, float.PositiveInfinity) };
            Assert.Throws<ArgumentNullException>(() =>
                new MaterialSampleSettings(bands, null));
        }

        [Test]
        public void MaterialSampleSettings_EmptyFallback_Throws()
        {
            var bands = new MaterialBand[] { new MaterialBand("stone", 0f, float.PositiveInfinity) };
            Assert.Throws<ArgumentNullException>(() =>
                new MaterialSampleSettings(bands, ""));
        }

        [Test]
        public void MaterialSampleSettings_DirtStoneIce_HasThreeBands()
        {
            var settings = MaterialSampleSettings.DirtStoneIce(2f, 6f);

            Assert.AreEqual(3, settings.Bands.Length);
            Assert.AreEqual("dirt", settings.Bands[0].MaterialId);
            Assert.AreEqual("stone", settings.Bands[1].MaterialId);
            Assert.AreEqual("ice", settings.Bands[2].MaterialId);
            Assert.AreEqual("air", settings.FallbackMaterialId);
        }

        #endregion

        #region MaterialBand validation

        [Test]
        public void MaterialBand_NullId_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MaterialBand(null, 0f, 5f));
        }

        [Test]
        public void MaterialBand_EmptyId_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MaterialBand("", 0f, 5f));
        }

        [Test]
        public void MaterialBand_NegativeMinDepth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialBand("stone", -1f, 5f));
        }

        [Test]
        public void MaterialBand_MaxLessThanMin_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialBand("stone", 5f, 3f));
        }

        [Test]
        public void MaterialBand_ContainsDepth_WithinRange_ReturnsTrue()
        {
            var band = new MaterialBand("stone", 2f, 6f);

            Assert.IsTrue(band.ContainsDepth(2f));
            Assert.IsTrue(band.ContainsDepth(4f));
            Assert.IsTrue(band.ContainsDepth(5.999f));
        }

        [Test]
        public void MaterialBand_ContainsDepth_OutsideRange_ReturnsFalse()
        {
            var band = new MaterialBand("stone", 2f, 6f);

            Assert.IsFalse(band.ContainsDepth(1.999f));
            Assert.IsFalse(band.ContainsDepth(6f));
            Assert.IsFalse(band.ContainsDepth(10f));
        }

        #endregion

        #region Edits affect material sampling

        [Test]
        public void Sample_AfterDigEdit_ReturnsAir()
        {
            // Dig a hole at a position that would normally be stone
            Vector2 pos = new Vector2(0f, BaseRadius - 5f);

            // Before edit: depth 5 -> stone
            var before = MaterialSampler.Sample(_field, pos, _settings, _database);
            Assert.AreEqual("stone", before.Id);

            // Apply a dig brush that carves out this position
            var edit = new TerrainEdit(
                localPosition: new Vector2(0f, BaseRadius - 5f),
                radius: 2f,
                isAdditive: true);
            _field.ApplyEdit(edit);

            // After edit: position is now air
            var after = MaterialSampler.Sample(_field, pos, _settings, _database);
            Assert.AreEqual("air", after.Id);
        }

        [Test]
        public void Sample_AfterBuildEdit_ReturnsDeeperMaterial()
        {
            // Build terrain downward so a position that was air becomes solid
            Vector2 pos = new Vector2(0f, BaseRadius + 3f);

            // Before edit: in air
            var before = MaterialSampler.Sample(_field, pos, _settings, _database);
            Assert.AreEqual("air", before.Id);

            // Build a large brush that adds material at this position
            var edit = new TerrainEdit(
                localPosition: new Vector2(0f, BaseRadius + 3f),
                radius: 2f,
                isAdditive: false);
            _field.ApplyEdit(edit);

            // After edit: solid — depth depends on edit's contribution at center
            // At the center of the brush, the edit makes it solid (depth ~0 from brush center)
            var after = MaterialSampler.Sample(_field, pos, _settings, _database);
            Assert.AreEqual("dirt", after.Id);
        }

        #endregion

        #region Helpers

        private static MaterialDefinition CreateDefinition(string id, string displayName = null)
        {
            var def = ScriptableObject.CreateInstance<MaterialDefinition>();

            var field = typeof(MaterialDefinition).GetField("id",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(def, id);

            if (displayName != null)
            {
                var nameField = typeof(MaterialDefinition).GetField("displayName",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                nameField.SetValue(def, displayName);
            }

            return def;
        }

        #endregion
    }
}
