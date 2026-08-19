using System;
using NUnit.Framework;
using UnityEngine;
using PlanetComponent = SDFTerrain.Planet.Planet;
using PlanetManager = SDFTerrain.Planet.PlanetManager;
using PlanetSettings = SDFTerrain.Planet.PlanetSettings;

namespace SDFTerrain.Tests
{
    public class PlanetManagerTests
    {
        private PlanetManager _manager;
        private PlanetSettings _settings;
        private GameObject _gameObjectA;
        private GameObject _gameObjectB;

        [SetUp]
        public void SetUp()
        {
            _manager = new PlanetManager();
            _settings = ScriptableObject.CreateInstance<PlanetSettings>();
            _gameObjectA = new GameObject("PlanetA");
            _gameObjectB = new GameObject("PlanetB");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObjectA);
            UnityEngine.Object.DestroyImmediate(_gameObjectB);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        private PlanetComponent CreatePlanet(GameObject go, Vector3 position, float radius, int seed)
        {
            go.transform.position = position;
            PlanetComponent planet = go.AddComponent<PlanetComponent>();
            planet.Initialize(_settings, seed, radius, gravityStrength: 1f);
            return planet;
        }

        [Test]
        public void Register_AddsToAllPlanets()
        {
            PlanetComponent planet = CreatePlanet(_gameObjectA, Vector3.zero, 10f, 1);

            _manager.Register(planet);

            Assert.AreEqual(1, _manager.AllPlanets.Count);
            Assert.Contains(planet, (System.Collections.ICollection)_manager.AllPlanets);
        }

        [Test]
        public void Register_SamePlanetTwice_DoesNotDuplicate()
        {
            PlanetComponent planet = CreatePlanet(_gameObjectA, Vector3.zero, 10f, 1);

            _manager.Register(planet);
            _manager.Register(planet);

            Assert.AreEqual(1, _manager.AllPlanets.Count);
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Register(null));
        }

        [Test]
        public void Unregister_RemovesPlanet()
        {
            PlanetComponent planet = CreatePlanet(_gameObjectA, Vector3.zero, 10f, 1);
            _manager.Register(planet);

            _manager.Unregister(planet);

            Assert.AreEqual(0, _manager.AllPlanets.Count);
        }

        [Test]
        public void GetPlanetAt_NoPlanetsRegistered_ReturnsNull()
        {
            PlanetComponent result = _manager.GetPlanetAt(Vector2.zero);
            Assert.IsNull(result);
        }

        [Test]
        public void GetPlanetAt_MultiplePlanets_ReturnsNearestBySurfaceDistance()
        {
            PlanetComponent near = CreatePlanet(_gameObjectA, new Vector3(0f, 0f, 0f), radius: 5f, seed: 1);
            PlanetComponent far = CreatePlanet(_gameObjectB, new Vector3(100f, 0f, 0f), radius: 5f, seed: 2);

            _manager.Register(near);
            _manager.Register(far);

            PlanetComponent result = _manager.GetPlanetAt(new Vector2(1f, 0f));

            Assert.AreEqual(near, result);
        }

        [Test]
        public void GetPlanetAt_AccountsForDifferingRadii()
        {
            // Small planet centered near the query point, large planet centered far away but
            // with a big enough radius that its surface is actually closer.
            PlanetComponent small = CreatePlanet(_gameObjectA, new Vector3(20f, 0f, 0f), radius: 1f, seed: 1);
            PlanetComponent large = CreatePlanet(_gameObjectB, new Vector3(0f, 0f, 0f), radius: 15f, seed: 2);

            _manager.Register(small);
            _manager.Register(large);

            PlanetComponent result = _manager.GetPlanetAt(new Vector2(16f, 0f));

            Assert.AreEqual(large, result);
        }
    }
}
