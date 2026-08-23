using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Data-driven definition of a single material type. Holds physical, visual, and gameplay
    /// properties consumed by terrain sampling, geological layers, ore placement, and mining.
    /// Never holds runtime simulation state.
    /// </summary>
    [CreateAssetMenu(fileName = "MaterialDefinition", menuName = "SDF Terrain/Material Definition")]
    public class MaterialDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique identifier (e.g. \"stone\", \"iron_ore\").")]
        [SerializeField] private string id = "unknown";

        [Tooltip("Human-readable name displayed in the Inspector and debug views.")]
        [SerializeField] private string displayName = "Unknown";

        [Tooltip("Visual color for rendering and debug visualizations.")]
        [SerializeField] private Color color = Color.gray;

        [Header("Physical Properties")]
        [Tooltip("Mass per unit volume.")]
        [SerializeField] private float density = 1f;

        [Tooltip("Resistance to mining and deformation (0 = soft, 1 = very hard).")]
        [SerializeField] private float hardness = 0.5f;

        [Tooltip("Surface friction coefficient (0 = frictionless, 1 = maximum friction).")]
        [SerializeField] private float friction = 0.5f;

        [Tooltip("Rate of heat transfer through the material.")]
        [SerializeField] private float thermalConductivity = 1f;

        [Tooltip("Temperature (Kelvin) at which the material transitions to liquid.")]
        [SerializeField] private float meltingPoint = 1500f;

        [Tooltip("Load-bearing capacity (0 = collapses easily, 1 = supports heavy loads).")]
        [SerializeField] private float structuralStrength = 0.5f;

        /// <summary>Unique identifier for lookups in the material database.</summary>
        public string Id => id;

        /// <summary>Human-readable name.</summary>
        public string DisplayName => displayName;

        /// <summary>Visual color for rendering and debug views.</summary>
        public Color Color => color;

        /// <summary>Mass per unit volume.</summary>
        public float Density => density;

        /// <summary>Resistance to mining and deformation.</summary>
        public float Hardness => hardness;

        /// <summary>Surface friction coefficient.</summary>
        public float Friction => friction;

        /// <summary>Heat transfer rate.</summary>
        public float ThermalConductivity => thermalConductivity;

        /// <summary>Temperature (Kelvin) at which the material melts.</summary>
        public float MeltingPoint => meltingPoint;

        /// <summary>Load-bearing capacity.</summary>
        public float StructuralStrength => structuralStrength;

        private void OnValidate()
        {
            // Identity must not be empty
            if (string.IsNullOrEmpty(id))
            {
                id = "unknown";
            }

            // Physical properties cannot be negative
            if (density < 0f)
            {
                density = 0f;
            }

            if (thermalConductivity < 0f)
            {
                thermalConductivity = 0f;
            }

            if (meltingPoint < 0f)
            {
                meltingPoint = 0f;
            }

            // Normalized ranges
            if (friction < 0f || friction > 1f)
            {
                friction = Mathf.Clamp(friction, 0f, 1f);
            }

            if (hardness < 0f || hardness > 1f)
            {
                hardness = Mathf.Clamp(hardness, 0f, 1f);
            }

            if (structuralStrength < 0f || structuralStrength > 1f)
            {
                structuralStrength = Mathf.Clamp(structuralStrength, 0f, 1f);
            }
        }
    }
}
