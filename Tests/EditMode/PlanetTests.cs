using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Planet;
using PlanetComponent = SDFTerrain.Planet.Planet;

namespace SDFTerrain.Tests
{
    public class PlanetTests
    {
        private GameObject _gameObject;
        private PlanetSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("TestPlanet");
            _settings = ScriptableObject.CreateInstance<PlanetSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void Initialize_SetsFieldsAndMarksInitialized()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            planet.Initialize(_settings, seed: 42, radius: 10f, gravityStrength: 9.81f);

            Assert.IsTrue(planet.IsInitialized);
            Assert.AreEqual(_settings, planet.Settings);
            Assert.AreEqual(42, planet.Seed);
            Assert.AreEqual(10f, planet.Radius);
            Assert.AreEqual(9.81f, planet.GravityStrength);
        }

        [Test]
        public void Initialize_NullSettings_Throws()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            Assert.Throws<ArgumentNullException>(() =>
                planet.Initialize(null, seed: 1, radius: 10f, gravityStrength: 1f));
        }

        [Test]
        public void Initialize_NonPositiveRadius_Throws()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                planet.Initialize(_settings, seed: 1, radius: 0f, gravityStrength: 1f));
        }

        [Test]
        public void Initialize_NegativeGravity_Throws()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                planet.Initialize(_settings, seed: 1, radius: 10f, gravityStrength: -1f));
        }

        [Test]
        public void Initialize_SettingsSeedOverride_TakesPrecedenceOverSuppliedSeed()
        {
            var settingsField = typeof(PlanetSettings).GetField("seedOverride",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            settingsField.SetValue(_settings, 777);

            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();
            planet.Initialize(_settings, seed: 42, radius: 10f, gravityStrength: 1f);

            Assert.AreEqual(777, planet.Seed);
        }

        [Test]
        public void Center_ReflectsTransformPosition()
        {
            _gameObject.transform.position = new Vector3(5f, 3f, 0f);
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            Assert.AreEqual(new Vector2(5f, 3f), planet.Center);
        }

        [Test]
        public void OnEnable_AutoRegistersWithSharedPlanetManager()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            Assert.Contains(planet, (System.Collections.ICollection)PlanetManager.Instance.AllPlanets);
        }

        [Test]
        public void OnDisable_AutoUnregistersFromSharedPlanetManager()
        {
            PlanetComponent planet = _gameObject.AddComponent<PlanetComponent>();

            _gameObject.SetActive(false);

            CollectionAssert.DoesNotContain((System.Collections.ICollection)PlanetManager.Instance.AllPlanets, planet);
        }
    }
}
