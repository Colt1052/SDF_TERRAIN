using System;
using UnityEngine;

namespace SDFTerrain.Planet
{
    /// <summary>
    /// Thin data component identifying a planet: radius, seed, gravity strength, and its
    /// configuration asset. Holds no generation, rendering, or physics logic — those live in
    /// dedicated systems (PlanetGenerator, PlanetGravity, etc.) that read this component.
    /// </summary>
    public class Planet : MonoBehaviour
    {
        [SerializeField] private PlanetSettings settings;
        [SerializeField] private int seed;
        [SerializeField] private float radius;
        [SerializeField] private float gravityStrength;

        private bool _initialized;

        public PlanetSettings Settings => settings;
        public int Seed => seed;
        public float Radius => radius;
        public float GravityStrength => gravityStrength;
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Assigns this planet's identity. Must be called exactly once before the planet is
        /// registered with a PlanetManager or used by generation.
        /// </summary>
        public void Initialize(PlanetSettings planetSettings, int seed, float radius, float gravityStrength)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Planet radius must be positive.");
            }

            if (gravityStrength < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(gravityStrength), gravityStrength, "Gravity strength cannot be negative.");
            }

            this.settings = planetSettings;
            this.seed = planetSettings != null && planetSettings.HasSeedOverride ? planetSettings.SeedOverride : seed;
            this.radius = radius;
            this.gravityStrength = gravityStrength;
            _initialized = true;
        }

        /// <summary>
        /// Overload for demos and runtime use cases that don't need a PlanetSettings asset.
        /// </summary>
        public void Initialize(int seed, float radius, float gravityStrength = 9.81f)
        {
            Initialize(null, seed, radius, gravityStrength);
        }

        public Vector2 Center => transform.position;

        private void OnEnable()
        {
            PlanetManager.Instance.Register(this);
        }

        private void OnDisable()
        {
            PlanetManager.Instance.Unregister(this);
        }
    }
}
