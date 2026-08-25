using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Maps material IDs to display colors for vertex-color rendering. Uses the
    /// <see cref="MaterialDatabase"/> when available, falls back to a built-in palette
    /// for known material IDs so that geological layers are visible even without
    /// MaterialDefinition assets on disk.
    /// </summary>
    public static class MaterialColorMap
    {
        private static readonly Dictionary<string, Color> _fallbackColors = new Dictionary<string, Color>
        {
            // Earth-like layers
            { "dirt", new Color(0.55f, 0.35f, 0.20f) },
            { "stone", new Color(0.55f, 0.55f, 0.55f) },
            { "deep_stone", new Color(0.35f, 0.35f, 0.38f) },
            { "mantle", new Color(0.70f, 0.30f, 0.10f) },
            { "molten_mantle", new Color(0.95f, 0.45f, 0.05f) },

            // Ice world layers
            { "snow", new Color(0.92f, 0.94f, 0.97f) },
            { "ice", new Color(0.50f, 0.75f, 0.92f) },
            { "water", new Color(0.25f, 0.55f, 0.85f) },

            // Generics
            { "air", Color.white },
            { "unknown", Color.gray }
        };

        /// <summary>
        /// Returns the display color for a given material ID.
        /// Checks the <paramref name="database"/> first; if not found or null,
        /// uses the built-in fallback palette.
        /// </summary>
        public static Color GetColor(string materialId, MaterialDatabase database = null)
        {
            if (!string.IsNullOrEmpty(materialId) && database != null)
            {
                var def = database.GetMaterial(materialId);
                if (def != null)
                    return def.Color;
            }

            if (!string.IsNullOrEmpty(materialId) && _fallbackColors.TryGetValue(materialId, out Color fallback))
            {
                return fallback;
            }

            return Color.gray;
        }

        /// <summary>
        /// Returns the display color for a given numeric <see cref="MaterialId"/>.
        /// Checks the <paramref name="database"/> for a registered material definition;
        /// falls back to the built-in palette for Air and Unknown.
        /// </summary>
        public static Color GetColor(MaterialId materialId, MaterialDatabase database = null)
        {
            if (materialId == MaterialId.Air)
                return Color.white;
            if (materialId == MaterialId.Unknown)
                return Color.gray;

            if (materialId.IsValid && database != null)
            {
                var def = database.GetMaterial(materialId);
                if (def != null)
                    return def.Color;
            }

            // Fallback: generate a deterministic color from the numeric ID.
            // This ensures every registered material has a visible color even without
            // a MaterialDefinition asset.
            return DeterministicColor(materialId.Value);
        }

        private static Color DeterministicColor(int value)
        {
            // Simple hash-based color: ensures adjacent IDs look different.
            float hue = ((value * 0.618033988749895f) % 1.0f); // golden ratio step
            return Color.HSVToRGB(hue, 0.4f, 0.85f);
        }
    }
}
