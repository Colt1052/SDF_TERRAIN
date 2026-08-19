using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Precomputed per-(seed, settings) values that <see cref="TerrainNoise"/> would otherwise
    /// reconstruct from scratch on every single <see cref="TerrainNoise.SampleHeight"/> call: the
    /// domain-warp phase and one phase per octave. These are a pure function of seed and settings
    /// and never change for a given <see cref="TerrainField"/>'s lifetime, so building this once
    /// and reusing it avoids repeatedly constructing a <see cref="SeededRandom"/> per sample point
    /// per chunk rebuild. Runs the exact same SeededRandom sequence, in the same order, as the
    /// uncached path, so cached phases are bit-identical to the per-call values.
    /// </summary>
    public sealed class TerrainNoiseCache
    {
        public readonly float WarpPhase;
        public readonly float[] OctavePhases;

        private TerrainNoiseCache(float warpPhase, float[] octavePhases)
        {
            WarpPhase = warpPhase;
            OctavePhases = octavePhases;
        }

        public static TerrainNoiseCache Build(int seed, TerrainNoiseSettings settings)
        {
            float warpPhase = 0f;
            if (settings.WarpStrength != 0f)
            {
                var warpRng = new SeededRandom(seed ^ unchecked((int)0x57A9F00D));
                warpPhase = warpRng.NextFloat(0f, 2f * UnityEngine.Mathf.PI);
            }

            var octavePhases = new float[settings.Octaves];
            for (int octave = 0; octave < settings.Octaves; octave++)
            {
                var rng = new SeededRandom(seed + (octave * 1000003));
                octavePhases[octave] = rng.NextFloat(0f, 2f * UnityEngine.Mathf.PI);
            }

            return new TerrainNoiseCache(warpPhase, octavePhases);
        }
    }
}
