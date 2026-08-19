using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Planet
{
    /// <summary>
    /// Registration, lookup, and lifetime tracking for all active planets. Holds no per-planet
    /// simulation logic — it is purely an index over registered Planet components.
    /// </summary>
    public class PlanetManager
    {
        private static PlanetManager _instance;

        /// <summary>
        /// Shared instance used by Planet's automatic Awake/OnDestroy registration. Tests that
        /// need an isolated manager should construct their own PlanetManager() instead.
        /// </summary>
        public static PlanetManager Instance => _instance ??= new PlanetManager();

        private readonly List<Planet> _planets = new List<Planet>();

        /// <summary>
        /// All registered planets in registration order. This order is the defined update
        /// ordering for any system that must process planets deterministically (e.g. gravity,
        /// generation) — first registered, first processed.
        /// </summary>
        public IReadOnlyList<Planet> AllPlanets => _planets;

        public void Register(Planet planet)
        {
            if (planet == null)
            {
                throw new ArgumentNullException(nameof(planet));
            }

            if (_planets.Contains(planet))
            {
                return;
            }

            _planets.Add(planet);
        }

        public void Unregister(Planet planet)
        {
            if (planet == null)
            {
                throw new ArgumentNullException(nameof(planet));
            }

            _planets.Remove(planet);
        }

        /// <summary>
        /// Returns the planet whose surface is nearest to the given world position, or null if
        /// no planets are registered. "Nearest" is measured as distance from the position to
        /// each planet's center minus that planet's radius, so it accounts for differing sizes.
        /// </summary>
        public Planet GetPlanetAt(Vector2 worldPosition)
        {
            Planet closest = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < _planets.Count; i++)
            {
                Planet planet = _planets[i];
                float distanceToSurface = Vector2.Distance(worldPosition, planet.Center) - planet.Radius;

                if (distanceToSurface < closestDistance)
                {
                    closestDistance = distanceToSurface;
                    closest = planet;
                }
            }

            return closest;
        }
    }
}
