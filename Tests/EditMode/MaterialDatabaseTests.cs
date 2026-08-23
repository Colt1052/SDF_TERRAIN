using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using SDFTerrain.Materials;

namespace SDFTerrain.Tests
{
    public class MaterialDatabaseTests
    {
        private MaterialDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = new MaterialDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            // Clear singleton so subsequent tests start fresh
            _database.Clear();
        }

        #region AddMaterial

        [Test]
        public void AddMaterial_ValidDefinition_CanRetrieveById()
        {
            var def = CreateDefinition("stone");

            _database.AddMaterial(def);

            var retrieved = _database.GetMaterial("stone");
            Assert.AreEqual(def, retrieved);
        }

        [Test]
        public void AddMaterial_NullDefinition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _database.AddMaterial(null));
        }

        [Test]
        public void AddMaterial_DuplicateId_Overwrites()
        {
            var defA = CreateDefinition("stone", "Stone");
            var defB = CreateDefinition("stone", "Granite");

            _database.AddMaterial(defA);
            _database.AddMaterial(defB);

            var retrieved = _database.GetMaterial("stone");
            Assert.AreEqual(defB, retrieved);
        }

        #endregion

        #region GetMaterial

        [Test]
        public void GetMaterial_WithValidId_ReturnsDefinition()
        {
            var def = CreateDefinition("iron_ore");

            _database.AddMaterial(def);

            var retrieved = _database.GetMaterial("iron_ore");

            Assert.AreEqual(def, retrieved);
            Assert.AreEqual("iron_ore", retrieved.Id);
        }

        [Test]
        public void GetMaterial_WithUnknownId_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => _database.GetMaterial("nonexistent"));
        }

        [Test]
        public void GetMaterial_WithNullId_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _database.GetMaterial(null));
        }

        [Test]
        public void GetMaterial_WithEmptyId_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _database.GetMaterial(""));
        }

        [Test]
        public void GetMaterial_IsDeterministicAcrossCalls()
        {
            var def = CreateDefinition("basalt");

            _database.AddMaterial(def);

            var first = _database.GetMaterial("basalt");
            var second = _database.GetMaterial("basalt");
            var third = _database.GetMaterial("basalt");

            Assert.AreSame(first, second);
            Assert.AreSame(second, third);
        }

        #endregion

        #region HasMaterial

        [Test]
        public void HasMaterial_ExistingId_ReturnsTrue()
        {
            var def = CreateDefinition("dirt");

            _database.AddMaterial(def);

            Assert.IsTrue(_database.HasMaterial("dirt"));
        }

        [Test]
        public void HasMaterial_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(_database.HasMaterial("does_not_exist"));
        }

        [Test]
        public void HasMaterial_NullId_ReturnsFalse()
        {
            Assert.IsFalse(_database.HasMaterial(null));
        }

        [Test]
        public void HasMaterial_EmptyId_ReturnsFalse()
        {
            Assert.IsFalse(_database.HasMaterial(""));
        }

        #endregion

        #region AllMaterials

        [Test]
        public void AllMaterials_ContainsAllRegisteredMaterials()
        {
            var defA = CreateDefinition("stone");
            var defB = CreateDefinition("ice");
            var defC = CreateDefinition("magma");

            _database.AddMaterial(defA);
            _database.AddMaterial(defB);
            _database.AddMaterial(defC);

            Assert.AreEqual(3, _database.AllMaterials.Count);

            var list = _database.AllMaterials.ToList();
            Assert.IsTrue(list.Contains(defA));
            Assert.IsTrue(list.Contains(defB));
            Assert.IsTrue(list.Contains(defC));
        }

        [Test]
        public void AllMaterials_EmptyDatabase_ReturnsEmptyCollection()
        {
            Assert.AreEqual(0, _database.AllMaterials.Count);
        }

        #endregion

        #region IsInitialized

        [Test]
        public void IsInitialized_NewDatabase_ReturnsFalse()
        {
            Assert.IsFalse(_database.IsInitialized);
        }

        [Test]
        public void IsInitialized_AfterAddingMaterial_ReturnsTrue()
        {
            var def = CreateDefinition("stone");

            _database.AddMaterial(def);

            Assert.IsTrue(_database.IsInitialized);
        }

        #endregion

        #region MaterialDefinition Validation

        [Test]
        public void MaterialDefinition_DefaultValues_AreWithinValidRanges()
        {
            var def = CreateDefinition("test");

            Assert.GreaterOrEqual(def.Density, 0f);
            Assert.GreaterOrEqual(def.Hardness, 0f);
            Assert.LessOrEqual(def.Hardness, 1f);
            Assert.GreaterOrEqual(def.Friction, 0f);
            Assert.LessOrEqual(def.Friction, 1f);
            Assert.GreaterOrEqual(def.ThermalConductivity, 0f);
            Assert.GreaterOrEqual(def.MeltingPoint, 0f);
            Assert.GreaterOrEqual(def.StructuralStrength, 0f);
            Assert.LessOrEqual(def.StructuralStrength, 1f);
        }

        [Test]
        public void MaterialDefinition_OnValidate_ClampsNegativeDensityToZero()
        {
            var def = CreateDefinition("test");
            SetField(def, "density", -5f);
            CallOnValidate(def);

            Assert.AreEqual(0f, def.Density);
        }

        [Test]
        public void MaterialDefinition_OnValidate_ClampsHardnessToRange()
        {
            var def = CreateDefinition("test");
            SetField(def, "hardness", 2.5f);
            CallOnValidate(def);

            Assert.AreEqual(1f, def.Hardness);
        }

        [Test]
        public void MaterialDefinition_OnValidate_ClampsFrictionToRange()
        {
            var def = CreateDefinition("test");
            SetField(def, "friction", -0.5f);
            CallOnValidate(def);

            Assert.AreEqual(0f, def.Friction);
        }

        [Test]
        public void MaterialDefinition_OnValidate_ClampsEmptyIdToUnknown()
        {
            var def = CreateDefinition("test");
            SetField(def, "id", "");
            CallOnValidate(def);

            Assert.AreEqual("unknown", def.Id);
        }

        #endregion

        #region Duplicate ID Handling

        [Test]
        public void DefaultMaterials_DuplicateId_LastWriteWins()
        {
            var defA = CreateDefinition("stone", "Stone");
            var defB = CreateDefinition("iron_ore");
            var defC = CreateDefinition("stone", "Duplicate");

            _database.AddMaterial(defA);
            _database.AddMaterial(defB);
            _database.AddMaterial(defC);

            // C overwrote A — should have 2 entries, not 3
            Assert.AreEqual(2, _database.AllMaterials.Count);

            // The last registration wins
            var stone = _database.GetMaterial("stone");
            Assert.AreEqual("Duplicate", stone.DisplayName);
        }

        #endregion

        #region Helpers

        private static MaterialDefinition CreateDefinition(string id, string displayName = null)
        {
            var def = ScriptableObject.CreateInstance<MaterialDefinition>();
            SetField(def, "id", id);
            if (displayName != null)
            {
                SetField(def, "displayName", displayName);
            }
            return def;
        }

        private static void SetField<T>(MaterialDefinition target, string fieldName, T value)
        {
            var field = typeof(MaterialDefinition)
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
            {
                throw new MissingFieldException($"Field \"{fieldName}\" not found on MaterialDefinition.");
            }

            field.SetValue(target, value);
        }

        private static void CallOnValidate(MaterialDefinition def)
        {
            var method = typeof(MaterialDefinition)
                .GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new MissingMethodException("OnValidate not found on MaterialDefinition.");
            }

            method.Invoke(def, null);
        }

        #endregion
    }
}
