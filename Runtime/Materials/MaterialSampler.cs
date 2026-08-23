using System;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Pure-function utility that determines which material occupies a given position in a
    /// <see cref="TerrainField"/>. Uses depth below the surface (derived from the SDF) to
    /// select the matching <see cref="MaterialBand"/> from the provided settings.
    /// </summary>
    public static class MaterialSampler
    {
        /// <summary>
        /// Returns the material definition at <paramref name="localPosition"/> within
        /// <paramref name="field"/>.
        /// <para>
        /// If the SDF value is positive (air), the fallback material is returned.
        /// If negative (solid), the depth below surface (-SDF) is matched against the
        /// band list in order; the first band containing the depth determines the material.
        /// </para>
        /// </summary>
        /// <param name="field">Authoritative terrain SDF.</param>
        /// <param name="localPosition">Planet-local query position.</param>
        /// <param name="settings">Band configuration and fallback material.</param>
        /// <param name="database">Registry of material definitions.</param>
        /// <returns>The material definition for the position.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when a referenced material ID is not in the database.</exception>
        public static MaterialDefinition Sample(
            TerrainField field,
            Vector2 localPosition,
            MaterialSampleSettings settings,
            MaterialDatabase database)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (settings.Bands == null || settings.Bands.Length == 0)
                throw new ArgumentNullException(nameof(settings), "Settings must contain at least one band.");
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            float sdf = field.Sample(localPosition);

            // In air — return fallback
            if (sdf > 0f)
            {
                return database.GetMaterial(settings.FallbackMaterialId);
            }

            // On or below surface: depth is distance inward from surface
            float depth = -sdf;

            for (int i = 0; i < settings.Bands.Length; i++)
            {
                MaterialBand band = settings.Bands[i];
                if (band.ContainsDepth(depth))
                {
                    return database.GetMaterial(band.MaterialId);
                }
            }

            // No band matched (shouldn't happen if the last band uses PositiveInfinity) —
            // fall back to the deepest band's material as a safety net.
            MaterialBand lastBand = settings.Bands[settings.Bands.Length - 1];
            return database.GetMaterial(lastBand.MaterialId);
        }

        /// <summary>
        /// Computes the material ID at <paramref name="localPosition"/> without resolving the
        /// full <see cref="MaterialDefinition"/>. Useful when you only need the identifier
        /// (e.g. for debug labels or serialization).
        /// </summary>
        public static string SampleId(
            TerrainField field,
            Vector2 localPosition,
            MaterialSampleSettings settings)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (settings.Bands == null || settings.Bands.Length == 0)
                throw new ArgumentNullException(nameof(settings), "Settings must contain at least one band.");

            float sdf = field.Sample(localPosition);

            if (sdf > 0f)
            {
                return settings.FallbackMaterialId;
            }

            float depth = -sdf;

            for (int i = 0; i < settings.Bands.Length; i++)
            {
                MaterialBand band = settings.Bands[i];
                if (band.ContainsDepth(depth))
                {
                    return band.MaterialId;
                }
            }

            return settings.Bands[settings.Bands.Length - 1].MaterialId;
        }
    }
}
