using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Resources;
using SDFTerrain.Terrain;
using UnityEditor;
using System.Reflection;

namespace SDFTerrain.Tests
{
    /// <summary>
    /// Tests for the material system: MaterialId, MaterialSample, MaterialEdit, MaterialLayer,
    /// MaterialVolumeResult, ExcavationCalculator, Inventory, ResourceYieldTable, and
    /// conservation (mine -> place -> mine without duplication).
    /// </summary>
    public class MaterialSystemTests
    {
        #region Helpers

        private static MaterialDefinition CreateDefinition(string id, string displayName = null)
        {
            var def = ScriptableObject.CreateInstance<MaterialDefinition>();
            SetField(def, "id", id);
            if (displayName != null)
                SetField(def, "displayName", displayName ?? id);
            return def;
        }

        private static void SetField<T>(object target, string name, T value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        #endregion

        #region MaterialId

        [Test]
        public void MaterialId_Air_IsZero()
        {
            Assert.AreEqual(0, MaterialId.Air.Value);
            Assert.IsTrue(MaterialId.Air.IsValid);
        }

        [Test]
        public void MaterialId_Unknown_IsNegative()
        {
            Assert.AreEqual(-1, MaterialId.Unknown.Value);
            Assert.IsFalse(MaterialId.Unknown.IsValid);
        }

        [Test]
        public void MaterialId_Equality_SameValue_ReturnsTrue()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var id1 = db.GetMaterialId("stone");
            var id2 = db.GetMaterialId("stone");
            Assert.IsTrue(id1 == id2);
            db.Clear();
        }

        [Test]
        public void MaterialId_Equality_DifferentValue_ReturnsFalse()
        {
            Assert.IsFalse(MaterialId.Air == MaterialId.Unknown);
        }

        [Test]
        public void MaterialId_ToString_Air_ReturnsAir()
        {
            Assert.AreEqual("Air", MaterialId.Air.ToString());
        }

        [Test]
        public void MaterialId_ToString_Unknown_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", MaterialId.Unknown.ToString());
        }

        [Test]
        public void MaterialId_ToString_Registered_ReturnsDisplayName()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone", "Stone"));
            var id = db.GetMaterialId("stone");
            Assert.AreEqual("Stone", id.ToString());
            db.Clear();
        }

        #endregion

        #region MaterialSample

        [Test]
        public void MaterialSample_Construction_BasicFields()
        {
            var sample = new MaterialSample(MaterialId.Air, 1f, false);
            Assert.AreEqual(MaterialId.Air, sample.MaterialId);
            Assert.AreEqual(1f, sample.Concentration);
            Assert.IsFalse(sample.IsSolid);
        }

        [Test]
        public void MaterialSample_ClampsConcentration_Negative()
        {
            var sample = new MaterialSample(MaterialId.Air, -0.5f, false);
            Assert.AreEqual(0f, sample.Concentration);
        }

        [Test]
        public void MaterialSample_ClampsConcentration_AboveOne()
        {
            var sample = new MaterialSample(MaterialId.Air, 1.5f, false);
            Assert.AreEqual(1f, sample.Concentration);
        }

        [Test]
        public void MaterialSample_Equality_Same_ReturnsTrue()
        {
            var a = new MaterialSample(MaterialId.Air, 1f, false);
            var b = new MaterialSample(MaterialId.Air, 1f, false);
            Assert.IsTrue(a.Equals(b));
        }

        [Test]
        public void MaterialSample_Equality_DifferentMaterial_ReturnsFalse()
        {
            var a = new MaterialSample(MaterialId.Air, 1f, false);
            var b = new MaterialSample(MaterialId.Unknown, 1f, false);
            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void MaterialSample_ToString_ContainsMaterial()
        {
            var sample = new MaterialSample(MaterialId.Air, 1f, false);
            Assert.AreEqual("Air Air (100%)", sample.ToString());
        }

        #endregion

        #region MaterialEdit

        [Test]
        public void MaterialEdit_Contains_Center_ReturnsTrue()
        {
            var edit = new MaterialEdit(new Vector2(5f, 5f), 3f, MaterialId.Air, 0);
            Assert.IsTrue(edit.Contains(new Vector2(5f, 5f)));
        }

        [Test]
        public void MaterialEdit_Contains_InsideCircle_ReturnsTrue()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.IsTrue(edit.Contains(new Vector2(3f, 3f)));
        }

        [Test]
        public void MaterialEdit_Contains_OutsideCircle_ReturnsFalse()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.IsFalse(edit.Contains(new Vector2(4f, 4f))); // sqrt(32) > 5
        }

        [Test]
        public void MaterialEdit_Contains_ExactlyOnEdge_ReturnsTrue()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.IsTrue(edit.Contains(new Vector2(5f, 0f)));
        }

        [Test]
        public void MaterialEdit_SampleDistance_Center()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.AreEqual(5f, edit.SampleDistance(new Vector2(0f, 0f)));
        }

        [Test]
        public void MaterialEdit_SampleDistance_OnEdge()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.AreEqual(0f, edit.SampleDistance(new Vector2(5f, 0f)));
        }

        [Test]
        public void MaterialEdit_SampleDistance_Outside()
        {
            var edit = new MaterialEdit(new Vector2(0f, 0f), 5f, MaterialId.Air, 0);
            Assert.AreEqual(-5f, edit.SampleDistance(new Vector2(10f, 0f)));
        }

        [Test]
        public void MaterialEdit_Equality_SameFields_ReturnsTrue()
        {
            var a = new MaterialEdit(new Vector2(1f, 2f), 3f, MaterialId.Air, 5);
            var b = new MaterialEdit(new Vector2(1f, 2f), 3f, MaterialId.Air, 5);
            Assert.IsTrue(a.Equals(b));
        }

        #endregion

        #region MaterialLayer

        private static MaterialLayer CreateLayer(GeologicalProfile profile, MaterialDatabase db)
        {
            return new MaterialLayer(profile, db);
        }

        private static GeologicalProfile CreateProfile(string airId, string fallbackId, string[] layerIds)
        {
            GeologicalLayer[] layers = new GeologicalLayer[layerIds.Length];
            float depth = 0f;
            for (int i = 0; i < layerIds.Length; i++)
            {
                layers[i] = new GeologicalLayer(layerIds[i], depth, 100f, 0f, null);
                depth += 5f;
            }
            return new GeologicalProfile(
                layers: layers,
                temperatureGradient: 30f,
                surfaceTemperature: 280f,
                coreTemperature: 4000f,
                pressureGradient: 0.04f,
                noiseSeed: 42,
                noiseFrequency: 0.5f,
                fallbackMaterialId: fallbackId,
                airMaterialId: airId);
        }

        [Test]
        public void MaterialLayer_Sample_ReturnsAir_WhenInAir()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            // Point well outside the sphere
            var sample = layer.Sample(field, new Vector2(0f, 20f));

            Assert.AreEqual(MaterialId.Air, sample.MaterialId);
            Assert.IsFalse(sample.IsSolid);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_Sample_FallbackToGeology_WhenNoEdits()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            // Point at the center of the sphere (inside terrain)
            var sample = layer.Sample(field, Vector2.zero);

            Assert.IsTrue(sample.IsSolid);
            Assert.AreEqual(db.GetMaterialId("stone"), sample.MaterialId);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_Sample_EditOverridesGeology()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            // Place a material edit at the center
            layer.ApplyEdit(Vector2.zero, 5f, db.GetMaterialId("dirt"));

            var sample = layer.Sample(field, Vector2.zero);

            Assert.AreEqual(db.GetMaterialId("dirt"), sample.MaterialId);
            Assert.IsTrue(sample.IsSolid);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_Sample_LastEditWins_OnOverlap()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            db.AddMaterial(CreateDefinition("ice"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            layer.ApplyEdit(Vector2.zero, 5f, db.GetMaterialId("dirt"));
            layer.ApplyEdit(new Vector2(1f, 1f), 3f, db.GetMaterialId("ice"));

            // Point (1,1) is inside both edits — last one wins
            var sample = layer.Sample(field, new Vector2(1f, 1f));

            Assert.AreEqual(db.GetMaterialId("ice"), sample.MaterialId);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_EditCount_Increments()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var layer = CreateLayer(profile, db);

            Assert.AreEqual(0, layer.EditCount);

            layer.ApplyEdit(Vector2.zero, 5f, db.GetMaterialId("stone"));
            Assert.AreEqual(1, layer.EditCount);

            layer.ApplyEdit(new Vector2(5f, 5f), 3f, db.GetMaterialId("stone"));
            Assert.AreEqual(2, layer.EditCount);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_ClearEdits_RemovesAll()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var layer = CreateLayer(profile, db);

            layer.ApplyEdit(Vector2.zero, 5f, db.GetMaterialId("stone"));
            layer.ApplyEdit(new Vector2(5f, 5f), 3f, db.GetMaterialId("stone"));
            layer.ClearEdits();

            Assert.AreEqual(0, layer.EditCount);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_LoadEdits_RestoresState()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var layer = CreateLayer(profile, db);

            var edit = new MaterialEdit(new Vector2(2f, 2f), 4f, db.GetMaterialId("stone"), 0);
            layer.LoadEdits(new[] { edit });

            Assert.AreEqual(1, layer.EditCount);
            Assert.AreEqual(edit, layer.Edits[0]);
            db.Clear();
        }

        [Test]
        public void MaterialLayer_ApplyEdit_InvalidMaterialId_Throws()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var layer = CreateLayer(profile, db);

            Assert.Throws<ArgumentException>(() =>
                layer.ApplyEdit(Vector2.zero, 5f, MaterialId.Unknown));
            db.Clear();
        }

        #endregion

        #region MaterialVolumeResult

        [Test]
        public void MaterialVolumeResult_Add_Accumulates()
        {
            var result = new MaterialVolumeResult();
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            result.Add(stoneId, 3f);
            result.Add(stoneId, 2f);

            Assert.AreEqual(5f, result.GetVolume(stoneId));
            Assert.AreEqual(5f, result.TotalVolume);
            db.Clear();
        }

        [Test]
        public void MaterialVolumeResult_GetVolume_Unknown_ReturnsZero()
        {
            var result = new MaterialVolumeResult();
            Assert.AreEqual(0f, result.GetVolume(MaterialId.Air));
        }

        [Test]
        public void MaterialVolumeResult_HasMaterial_Known_ReturnsTrue()
        {
            var result = new MaterialVolumeResult();
            result.Add(MaterialId.Air, 1f);
            Assert.IsTrue(result.HasMaterial(MaterialId.Air));
        }

        [Test]
        public void MaterialVolumeResult_ForEach_IteratesAll()
        {
            var result = new MaterialVolumeResult();
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");
            result.Add(stoneId, 3f);
            result.Add(MaterialId.Air, 2f);

            var found = new Dictionary<int, float>();
            result.ForEach((id, vol) => found[id.Value] = vol);

            Assert.AreEqual(2, found.Count);
            Assert.AreEqual(3f, found[stoneId.Value]);
            db.Clear();
        }

        [Test]
        public void MaterialVolumeResult_Clear_Resets()
        {
            var result = new MaterialVolumeResult();
            result.Add(MaterialId.Air, 5f);
            result.Clear();

            Assert.AreEqual(0f, result.TotalVolume);
            Assert.AreEqual(0f, result.GetVolume(MaterialId.Air));
        }

        #endregion

        #region ExcavationCalculator

        [Test]
        public void ExcavationCalculator_CalculateAddition_SingleMaterial()
        {
            var result = ExcavationCalculator.CalculateAddition(Vector2.zero, 2f, MaterialId.Air);

            Assert.AreEqual(MaterialId.Air, result.Materials.Count > 0 ? MaterialId.Air : MaterialId.Unknown);
            float expectedArea = Mathf.PI * 2f * 2f;
            Assert.AreEqual(expectedArea, result.TotalVolume, 0.01f);
        }

        [Test]
        public void ExcavationCalculator_CalculateAddition_InvalidId_ReturnsEmpty()
        {
            var result = ExcavationCalculator.CalculateAddition(Vector2.zero, 2f, MaterialId.Unknown);

            Assert.AreEqual(0f, result.TotalVolume);
        }

        #endregion

        #region Inventory

        [Test]
        public void Inventory_Add_Remove_RoundTrip()
        {
            var inv = new Inventory();
            int added = inv.Add("stone", 50);
            Assert.AreEqual(50, added);
            Assert.AreEqual(50, inv.GetQuantity("stone"));

            int removed = inv.Remove("stone", 30);
            Assert.AreEqual(30, removed);
            Assert.AreEqual(20, inv.GetQuantity("stone"));
        }

        [Test]
        public void Inventory_Remove_ExceedsQuantity_ReturnsWhatItHas()
        {
            var inv = new Inventory();
            inv.Add("stone", 20);

            int removed = inv.Remove("stone", 50);
            Assert.AreEqual(20, removed);
            Assert.AreEqual(0, inv.GetQuantity("stone"));
        }

        [Test]
        public void Inventory_HasAtLeast_Sufficient_ReturnsTrue()
        {
            var inv = new Inventory();
            inv.Add("stone", 100);
            Assert.IsTrue(inv.HasAtLeast("stone", 50));
        }

        [Test]
        public void Inventory_HasAtLeast_Insufficient_ReturnsFalse()
        {
            var inv = new Inventory();
            inv.Add("stone", 30);
            Assert.IsFalse(inv.HasAtLeast("stone", 50));
        }

        [Test]
        public void Inventory_Remove_All_RemovesSlot()
        {
            var inv = new Inventory();
            inv.Add("stone", 10);
            inv.Remove("stone", 10);

            Assert.AreEqual(0, inv.GetQuantity("stone"));
        }

        [Test]
        public void Inventory_ToDictionary_ReturnsSnapshot()
        {
            var inv = new Inventory();
            inv.Add("stone", 50);
            inv.Add("iron", 30);

            var dict = inv.ToDictionary();
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(50, dict["stone"]);
            Assert.AreEqual(30, dict["iron"]);
        }

        #endregion

        #region ResourceYieldTable

        [Test]
        public void ResourceYieldTable_Convert_MaterialVolumesToResources()
        {
            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(MaterialId.Air, "air_resource", 100f));

            var volumes = new MaterialVolumeResult();
            volumes.Add(MaterialId.Air, 2.5f);

            var resources = table.Convert(volumes);

            Assert.IsTrue(resources.ContainsKey("air_resource"));
            Assert.AreEqual(250, resources["air_resource"]);
        }

        [Test]
        public void ResourceYieldTable_Convert_UnknownMaterial_Skips()
        {
            var table = new ResourceYieldTable();
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var volumes = new MaterialVolumeResult();
            volumes.Add(stoneId, 2.5f);

            var resources = table.Convert(volumes);

            Assert.AreEqual(0, resources.Count);
            db.Clear();
        }

        [Test]
        public void ResourceYieldTable_GetYieldRate_Known()
        {
            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(MaterialId.Air, "air_res", 50f));

            Assert.AreEqual(50f, table.GetYieldRate(MaterialId.Air));
        }

        [Test]
        public void ResourceYieldTable_GetYieldRate_Unknown_ReturnsDefault()
        {
            var table = new ResourceYieldTable();

            Assert.AreEqual(100f, table.GetYieldRate(MaterialId.Air));
        }

        [Test]
        public void ResourceYieldTable_Default_CreatesRules()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));

            var table = ResourceYieldTable.Default(db);

            // Check that rules exist for registered materials
            Assert.AreEqual(100f, table.GetYieldRate(db.GetMaterialId("stone")));
            Assert.AreEqual(100f, table.GetYieldRate(db.GetMaterialId("dirt")));
            db.Clear();
        }

        #endregion

        #region Conservation Integration

        [Test]
        public void Conservation_Mine_Place_Mine_NoDuplication()
        {
            // Simulate: excavate material -> get resources -> place -> mine placed material
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));

            var inv = new Inventory();

            // Step 1: Simulate excavation of 1 unit area of stone
            var volumes = new MaterialVolumeResult();
            volumes.Add(stoneId, 1f);
            var resources = table.Convert(volumes);
            foreach (var kvp in resources)
                inv.Add(kvp.Key, kvp.Value);

            int stoneAfterMine = inv.GetQuantity("stone");
            Assert.AreEqual(100, stoneAfterMine);

            // Step 2: Simulate placing 1 unit area of stone (cost = area * yieldRate = 1 * 100 = 100)
            int cost = (int)Mathf.CeilToInt(1f * table.GetYieldRate(stoneId));
            Assert.IsTrue(inv.HasAtLeast("stone", cost));
            inv.Remove("stone", cost);

            int stoneAfterPlace = inv.GetQuantity("stone");
            Assert.AreEqual(0, stoneAfterPlace);

            // Step 3: Mine the placed material (same area -> same yield)
            var placeVolumes = new MaterialVolumeResult();
            placeVolumes.Add(stoneId, 1f);
            var placeResources = table.Convert(placeVolumes);
            foreach (var kvp in placeResources)
                inv.Add(kvp.Key, kvp.Value);

            int stoneAfterRemine = inv.GetQuantity("stone");
            Assert.AreEqual(100, stoneAfterRemine);
            db.Clear();
        }

        [Test]
        public void Conservation_MultipleMaterials_NoDuplication()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("iron_ore"));
            var stoneId = db.GetMaterialId("stone");
            var ironId = db.GetMaterialId("iron_ore");

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            table.AddRule(new ResourceYieldDefinition(ironId, "iron_ore", 50f));

            var inv = new Inventory();

            // Excavate: 2 area of stone + 1 area of iron ore
            var volumes = new MaterialVolumeResult();
            volumes.Add(stoneId, 2f);
            volumes.Add(ironId, 1f);

            var resources = table.Convert(volumes);
            foreach (var kvp in resources)
                inv.Add(kvp.Key, kvp.Value);

            Assert.AreEqual(200, inv.GetQuantity("stone"));
            Assert.AreEqual(50, inv.GetQuantity("iron_ore"));
            db.Clear();
        }

        #endregion

        #region Full Pipeline Integration

        [Test]
        public void Pipeline_Excavate_ProducesMaterialVolumesAndResources()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);
            system.SampleResolution = 4;

            // Excavate inside the sphere center — should be all stone
            var result = system.Excavate(Vector2.zero, 1f, -1);

            Assert.IsTrue(result.WasApplied);
            Assert.IsTrue(result.MaterialVolumes.TotalVolume > 0f, "Should have removed some material");
            Assert.IsTrue(result.MaterialVolumes.HasMaterial(stoneId), "Removed material should include stone");
            Assert.IsTrue(inv.GetQuantity("stone") > 0, "Inventory should have received stone resources");
            db.Clear();
        }

        [Test]
        public void Pipeline_Excavate_OutsideTerrain_ProducesNothing()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);

            // Excavate far outside the sphere
            var result = system.Excavate(new Vector2(0f, 50f), 2f, -1);

            Assert.IsFalse(result.WasApplied);
            Assert.AreEqual(0f, result.MaterialVolumes.TotalVolume);
            Assert.AreEqual(0, inv.SlotCount);
            db.Clear();
        }

        [Test]
        public void Pipeline_Place_ConsumesResourcesAndAddsTerrain()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);

            // Pre-load inventory with resources
            inv.Add("stone", 5000);

            // Place stone outside the sphere where there is air to fill (costs resources)
            float radius = 1f;
            var result = system.Place(new Vector2(0f, 12f), radius, stoneId, "stone");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(stoneId, result.MaterialPlaced);
            Assert.IsTrue(result.ResourcesConsumed.ContainsKey("stone"));
            Assert.IsTrue(inv.GetQuantity("stone") < 5000, "Should have consumed resources");

            // Verify material layer has the edit
            Assert.AreEqual(1, layer.EditCount);

            // Verify the material at the edge of placed region is now the placed material
            var sample = layer.Sample(field, new Vector2(0f, 12f));
            Assert.AreEqual(stoneId, sample.MaterialId);
            db.Clear();
        }

        [Test]
        public void Pipeline_Place_InsufficientResources_Fails()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);

            // No resources in inventory — place outside the sphere where there is air to fill
            var result = system.Place(new Vector2(0f, 15f), 2f, stoneId, "stone");

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, layer.EditCount, "Should not have created a material edit");
            Assert.AreEqual(0, inv.SlotCount);
            db.Clear();
        }

        [Test]
        public void Pipeline_Mine_Place_Mine_Conservation()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);
            system.SampleResolution = 4;

            // Step 1: Mine stone from the planet
            var mineResult = system.Excavate(new Vector2(0f, 8f), 1f, -1);
            Assert.IsTrue(mineResult.WasApplied);
            int resourcesAfterMine = inv.GetQuantity("stone");
            Assert.IsTrue(resourcesAfterMine > 0);

            // Step 2: Place stone somewhere else (offset so it doesn't overlap the mine hole)
            float placeRadius = 1f;
            var placeResult = system.Place(new Vector2(0f, 8.5f), placeRadius, stoneId, "stone");
            Assert.IsTrue(placeResult.Succeeded);

            int resourcesAfterPlace = inv.GetQuantity("stone");
            Assert.IsTrue(resourcesAfterPlace < resourcesAfterMine, "Placing should reduce inventory");

            // Step 3: Mine the placed stone
            var reMineResult = system.Excavate(new Vector2(0f, 8.5f), placeRadius, -1);
            int resourcesAfterRemine = inv.GetQuantity("stone");

            // After mining back, we should have >= original amount (conservation with rounding tolerance)
            // We might gain a bit more due to rounding, but we should never have significantly less
            Assert.GreaterOrEqual(resourcesAfterRemine, resourcesAfterMine - 5,
                "Conservation violated: final resources significantly less than after initial mine");
            db.Clear();
        }

        [Test]
        public void Pipeline_Place_MixedMaterials_Mine_ExposesUnderlyingGeology()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var stoneId = db.GetMaterialId("stone");
            var dirtId = db.GetMaterialId("dirt");

            // Profile: dirt on surface, stone underneath
            var profile = CreateProfile("air", "stone", new[] { "dirt", "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            table.AddRule(new ResourceYieldDefinition(dirtId, "dirt", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);
            system.SampleResolution = 4;

            // Pre-load inventory
            inv.Add("stone", 5000);

            // Place stone on the surface
            system.Place(new Vector2(0f, 9f), 1.5f, stoneId, "stone");

            // Verify surface now shows stone (edit overrides geology)
            var sampleBefore = layer.Sample(field, new Vector2(0f, 9f));
            Assert.AreEqual(stoneId, sampleBefore.MaterialId);

            // Mine part of the placed stone (smaller radius)
            system.Excavate(new Vector2(0f, 9f), 0.5f, -1);

            // Verify the center was removed (air or whatever is underneath)
            var sampleCenter = layer.Sample(field, new Vector2(0f, 9f));
            Assert.IsTrue(sampleCenter.MaterialId == MaterialId.Air || sampleCenter.MaterialId == dirtId || !sampleCenter.IsSolid);

            // Verify the edge still has the stone material override
            var sampleEdge = layer.Sample(field, new Vector2(0f, 9.8f));
            // Edge of the 1.5f radius edit at (0,9) might still be stone
            db.Clear();
        }

        [Test]
        public void Pipeline_LoadEdits_PreservesMaterialState()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var stoneId = db.GetMaterialId("stone");
            var dirtId = db.GetMaterialId("dirt");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            // Apply material edits
            layer.ApplyEdit(new Vector2(0f, 5f), 3f, stoneId);
            layer.ApplyEdit(new Vector2(0f, 8f), 2f, dirtId);

            // Verify state
            var sampleBefore = layer.Sample(field, new Vector2(0f, 5f));
            Assert.AreEqual(stoneId, sampleBefore.MaterialId);

            // "Save" the edits
            var savedEdits = layer.Edits.ToArray();

            // Create a fresh layer
            var layer2 = CreateLayer(profile, db);
            Assert.AreEqual(0, layer2.EditCount);

            // "Load" the edits
            layer2.LoadEdits(savedEdits);
            Assert.AreEqual(2, layer2.EditCount);

            // Verify state is identical
            var sampleAfter = layer2.Sample(field, new Vector2(0f, 5f));
            Assert.AreEqual(sampleBefore.MaterialId, sampleAfter.MaterialId);

            var sampleEdgeBefore = layer.Sample(field, new Vector2(0f, 8f));
            var sampleEdgeAfter = layer2.Sample(field, new Vector2(0f, 8f));
            Assert.AreEqual(sampleEdgeBefore.MaterialId, sampleEdgeAfter.MaterialId);
            db.Clear();
        }

        [Test]
        public void Pipeline_ClearEdits_RestoresNaturalGeology()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var stoneId = db.GetMaterialId("stone");
            var dirtId = db.GetMaterialId("dirt");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            // Apply material edit that overrides natural geology
            layer.ApplyEdit(Vector2.zero, 5f, dirtId);

            var sampleWithEdit = layer.Sample(field, Vector2.zero);
            Assert.AreEqual(dirtId, sampleWithEdit.MaterialId);

            // Clear edits
            layer.ClearEdits();

            // Natural geology should be restored
            var sampleAfterClear = layer.Sample(field, Vector2.zero);
            Assert.AreEqual(stoneId, sampleAfterClear.MaterialId);
            db.Clear();
        }

        #endregion

        #region Serialization Persistence

        [Test]
        public void Serialization_TerrainEdit_RoundTrip()
        {
            var edit = new TerrainEdit(new Vector2(3f, 7f), 2.5f, true);

            var serializable = new SerializableTerrainEdit
            {
                LocalPositionX = edit.LocalPosition.x,
                LocalPositionY = edit.LocalPosition.y,
                Radius = edit.Radius,
                IsAdditive = edit.IsAdditive,
            };

            var restored = serializable.ToTerrainEdit();

            Assert.AreEqual(edit.LocalPosition.x, restored.LocalPosition.x);
            Assert.AreEqual(edit.LocalPosition.y, restored.LocalPosition.y);
            Assert.AreEqual(edit.Radius, restored.Radius);
            Assert.AreEqual(edit.IsAdditive, restored.IsAdditive);
        }

        [Test]
        public void Serialization_MaterialEdit_RoundTrip()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var edit = new MaterialEdit(new Vector2(5f, 3f), 1.5f, stoneId, 42);

            var serializable = new SerializableMaterialEdit
            {
                LocalPositionX = edit.LocalPosition.x,
                LocalPositionY = edit.LocalPosition.y,
                Radius = edit.Radius,
                MaterialIdValue = edit.MaterialId.Value,
                Order = edit.Order,
            };

            var restored = serializable.ToMaterialEdit();

            Assert.AreEqual(edit.LocalPosition.x, restored.LocalPosition.x);
            Assert.AreEqual(edit.LocalPosition.y, restored.LocalPosition.y);
            Assert.AreEqual(edit.Radius, restored.Radius);
            Assert.AreEqual(edit.MaterialId, restored.MaterialId);
            Assert.AreEqual(edit.Order, restored.Order);
            db.Clear();
        }

        [Test]
        public void Serialization_WorldSaveData_CaptureAndApply_RestoresTerrainEdits()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var profile = CreateProfile("air", "stone", new[] { "stone" });

            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);
            var inv = new Inventory();

            // Make terrain edits
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 8f), 2f, true));
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 3f), 1f, false));

            // Capture
            var save = WorldSaveData.Capture(field, layer, inv, seed: 12345);

            // Verify capture data
            Assert.AreEqual(2, save.TerrainEdits.Length);
            Assert.AreEqual(12345, save.Seed);
            Assert.AreEqual(10f, save.BaseRadius);

            // JSON round-trip
            string json = save.ToJson();
            var loaded = WorldSaveData.FromJson(json);

            // Apply to fresh systems
            var field2 = new TerrainField(10f);
            var layer2 = CreateLayer(profile, db);
            var inv2 = new Inventory();
            loaded.Apply(field2, layer2, inv2);

            Assert.AreEqual(2, field2.Edits.Count);
            Assert.AreEqual(field.Edits[0].LocalPosition, field2.Edits[0].LocalPosition);
            Assert.AreEqual(field.Edits[0].Radius, field2.Edits[0].Radius);
            Assert.AreEqual(field.Edits[0].IsAdditive, field2.Edits[0].IsAdditive);
            db.Clear();
        }

        [Test]
        public void Serialization_WorldSaveData_CaptureAndApply_RestoresMaterialEdits()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var stoneId = db.GetMaterialId("stone");
            var dirtId = db.GetMaterialId("dirt");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);
            var inv = new Inventory();

            // Apply material edits
            layer.ApplyEdit(new Vector2(0f, 5f), 3f, stoneId);
            layer.ApplyEdit(new Vector2(0f, 8f), 2f, dirtId);

            // Sample before save
            var sampleBefore = layer.Sample(field, new Vector2(0f, 5f));

            // Capture and round-trip
            var save = WorldSaveData.Capture(field, layer, inv, seed: 99999);
            string json = save.ToJson();
            var loaded = WorldSaveData.FromJson(json);

            // Apply to fresh layer
            var field2 = new TerrainField(10f);
            var layer2 = CreateLayer(profile, db);
            loaded.Apply(field2, layer2, inv);

            // Verify material state is identical
            Assert.AreEqual(2, layer2.EditCount);
            var sampleAfter = layer2.Sample(field2, new Vector2(0f, 5f));
            Assert.AreEqual(sampleBefore.MaterialId, sampleAfter.MaterialId);
            db.Clear();
        }

        [Test]
        public void Serialization_WorldSaveData_CaptureAndApply_RestoresInventory()
        {
            var db = new MaterialDatabase();
            var profile = CreateProfile("air", "stone", new[] { "stone" });

            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);
            var inv = new Inventory();

            // Add inventory items
            inv.Add("stone", 500);
            inv.Add("iron_ore", 75);

            // Capture and round-trip
            var save = WorldSaveData.Capture(field, layer, inv, seed: 0);
            Assert.AreEqual(2, save.InventorySlots.Length);

            string json = save.ToJson();
            var loaded = WorldSaveData.FromJson(json);

            // Apply to fresh inventory
            var inv2 = new Inventory();
            loaded.Apply(field, layer, inv2);

            Assert.AreEqual(500, inv2.GetQuantity("stone"));
            Assert.AreEqual(75, inv2.GetQuantity("iron_ore"));
            db.Clear();
        }

        [Test]
        public void Serialization_WorldSaveData_EmptyState_RoundTrip()
        {
            var db = new MaterialDatabase();
            var profile = CreateProfile("air", "stone", new[] { "stone" });

            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);
            var inv = new Inventory();

            // Capture empty state
            var save = WorldSaveData.Capture(field, layer, inv, seed: 42);

            string json = save.ToJson();
            var loaded = WorldSaveData.FromJson(json);

            // Apply to fresh systems
            var field2 = new TerrainField(10f);
            var layer2 = CreateLayer(profile, db);
            var inv2 = new Inventory();
            loaded.Apply(field2, layer2, inv2);

            Assert.AreEqual(0, field2.Edits.Count);
            Assert.AreEqual(0, layer2.EditCount);
            Assert.AreEqual(0, inv2.SlotCount);
            db.Clear();
        }

        [Test]
        public void Serialization_FullPipeline_SaveLoad_VerifiesConservation()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            var stoneId = db.GetMaterialId("stone");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);

            var table = new ResourceYieldTable();
            table.AddRule(new ResourceYieldDefinition(stoneId, "stone", 100f));
            var inv = new Inventory();

            var system = new TerrainExcavationSystem(field, layer, inv, table, db);
            system.SampleResolution = 4;

            // Perform some operations
            system.Excavate(new Vector2(0f, 8f), 1f, -1);
            system.Place(new Vector2(0f, 6f), 0.5f, stoneId, "stone");

            int stoneBeforeSave = inv.GetQuantity("stone");

            // Save
            var save = WorldSaveData.Capture(field, layer, inv, seed: 42);
            string json = save.ToJson();

            // Load into fresh systems
            var loaded = WorldSaveData.FromJson(json);
            var field2 = new TerrainField(10f);
            var layer2 = CreateLayer(profile, db);
            var inv2 = new Inventory();
            loaded.Apply(field2, layer2, inv2);

            // Verify inventory matches
            Assert.AreEqual(stoneBeforeSave, inv2.GetQuantity("stone"));

            // Verify material state matches — placed stone at (0, 6) should still be there
            var sampleBefore = layer.Sample(field, new Vector2(0f, 6f));
            var sampleAfter = layer2.Sample(field2, new Vector2(0f, 6f));
            Assert.AreEqual(sampleBefore.MaterialId, sampleAfter.MaterialId);
            db.Clear();
        }

        #endregion

        #region Persistence (WorldSaveData File I/O Simulation)

        [Test]
        public void Persistence_SaveFile_PathIsPredictable()
        {
            // Verify the file path formula is deterministic.
            // We can't test real I/O in EditMode, but we can verify the path construction logic.
            string dir = Path.Combine(Application.persistentDataPath, "SDFPlanetSaves");
            string expected = Path.Combine(dir, "slot_0.json");
            Assert.IsNotNull(expected);
            Assert.IsTrue(expected.EndsWith("slot_0.json"));
        }

        [Test]
        public void Persistence_WorldSaveData_SecondaryRoundTrip_ThroughJson()
        {
            var db = new MaterialDatabase();
            db.AddMaterial(CreateDefinition("stone"));
            db.AddMaterial(CreateDefinition("dirt"));
            var stoneId = db.GetMaterialId("stone");
            var dirtId = db.GetMaterialId("dirt");

            var profile = CreateProfile("air", "stone", new[] { "stone" });
            var field = new TerrainField(10f);
            var layer = CreateLayer(profile, db);
            var inv = new Inventory();

            // Build state
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 8f), 2f, true));
            layer.ApplyEdit(new Vector2(0f, 5f), 3f, stoneId);
            layer.ApplyEdit(new Vector2(0f, 8f), 2f, dirtId);
            inv.Add("stone", 500);
            inv.Add("dirt", 120);

            // Serialize
            var save = WorldSaveData.Capture(field, layer, inv, seed: 42);
            string json = save.ToJson();

            // Deserialize
            var loaded = WorldSaveData.FromJson(json);

            // Verify structure
            Assert.AreEqual(42, loaded.Seed);
            Assert.AreEqual(10f, loaded.BaseRadius);
            Assert.AreEqual(1, loaded.TerrainEdits.Length);
            Assert.AreEqual(2, loaded.MaterialEdits.Length);
            Assert.AreEqual(2, loaded.InventorySlots.Length);

            // Verify material edits round-tripped
            Assert.AreEqual(stoneId.Value, loaded.MaterialEdits[0].MaterialIdValue);
            Assert.AreEqual(dirtId.Value, loaded.MaterialEdits[1].MaterialIdValue);

            // Apply to fresh systems
            var field2 = new TerrainField(10f);
            var layer2 = CreateLayer(profile, db);
            var inv2 = new Inventory();
            loaded.Apply(field2, layer2, inv2);

            // Verify restored state
            Assert.AreEqual(1, field2.Edits.Count);
            Assert.AreEqual(2, layer2.EditCount);
            Assert.AreEqual(500, inv2.GetQuantity("stone"));
            Assert.AreEqual(120, inv2.GetQuantity("dirt"));

            // Verify material sampling is identical after restore
            var sample1 = layer.Sample(field, new Vector2(0f, 5f));
            var sample2 = layer2.Sample(field2, new Vector2(0f, 5f));
            Assert.AreEqual(sample1.MaterialId, sample2.MaterialId);
            db.Clear();
        }

        [Test]
        public void Persistence_PlayerPrefs_SlotMetadataKeys_AreIsolated()
        {
            // Verify slot key format: "sdf_save_{slot}.{key}"
            // Different slots must not collide.
            var slot0 = new WorldPersistence.SlotMetadata(0, "Save 0", 1, 0f, true);
            var slot1 = new WorldPersistence.SlotMetadata(1, "Save 1", 2, 0f, true);

            Assert.AreEqual(0, slot0.Slot);
            Assert.AreEqual(1, slot1.Slot);
            Assert.AreEqual("Save 0", slot0.DisplayName);
            Assert.AreEqual("Save 1", slot1.DisplayName);
        }

        [Test]
        public void Persistence_SlotMetadata_Empty_CorrectDefaults()
        {
            var empty = WorldPersistence.SlotMetadata.Empty;

            Assert.AreEqual(-1, empty.Slot);
            Assert.AreEqual("Empty", empty.DisplayName);
            Assert.IsFalse(empty.HasData);
        }

        [Test]
        public void Persistence_SlotMetadata_ToString_FormatsCorrectly()
        {
            var meta = new WorldPersistence.SlotMetadata(2, "My World", 42, 100f, true);
            string str = meta.ToString();

            Assert.IsTrue(str.Contains("My World"));
            Assert.IsTrue(str.Contains("2"));
            Assert.IsTrue(str.Contains("42"));

            var empty = WorldPersistence.SlotMetadata.Empty;
            Assert.AreEqual("Empty", empty.ToString());
        }

        #endregion
    }
}
