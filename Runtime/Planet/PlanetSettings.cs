using UnityEngine;

namespace SDFTerrain.Planet
{
    /// <summary>
    /// Data-driven configuration for a planet archetype. Holds ranges and parameters consumed
    /// by generation; never holds runtime state or logic.
    /// </summary>
    [CreateAssetMenu(fileName = "PlanetSettings", menuName = "SDF Terrain/Planet Settings")]
    public class PlanetSettings : ScriptableObject
    {
        [Header("Shape")]
        [SerializeField] private float minRadius = 20f;
        [SerializeField] private float maxRadius = 60f;

        [Header("Physics")]
        [SerializeField] private float density = 1f;
        [SerializeField] private float gravityStrength = 9.81f;

        [Header("Determinism")]
        [Tooltip("If >= 0, overrides any externally supplied seed. -1 means no override.")]
        [SerializeField] private int seedOverride = -1;

        public float MinRadius => minRadius;
        public float MaxRadius => maxRadius;
        public float Density => density;
        public float GravityStrength => gravityStrength;
        public int SeedOverride => seedOverride;

        public bool HasSeedOverride => seedOverride >= 0;

        private void OnValidate()
        {
            if (maxRadius < minRadius)
            {
                maxRadius = minRadius;
            }

            if (density < 0f)
            {
                density = 0f;
            }

            if (gravityStrength < 0f)
            {
                gravityStrength = 0f;
            }
        }
    }
}
