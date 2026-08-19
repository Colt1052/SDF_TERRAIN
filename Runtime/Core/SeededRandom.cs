using System;

namespace SDFTerrain.Core
{
    /// <summary>
    /// Deterministic PRNG (xorshift32) seeded explicitly from planet DNA. This is the only
    /// permitted source of randomness for procedural generation code — never use
    /// UnityEngine.Random in generation, since its global state breaks reproducibility.
    /// </summary>
    public struct SeededRandom
    {
        private uint _state;

        public SeededRandom(int seed)
        {
            // xorshift32 requires a non-zero state.
            _state = unchecked((uint)seed) == 0 ? 1u : unchecked((uint)seed);
        }

        /// <summary>Returns the next raw 32-bit value and advances the generator state.</summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Returns a float in [0, 1).</summary>
        public float NextFloat()
        {
            return NextUInt() / 4294967296f;
        }

        /// <summary>Returns a float in [min, max).</summary>
        public float NextFloat(float min, float max)
        {
            return min + (NextFloat() * (max - min));
        }

        /// <summary>Returns an int in [min, max).</summary>
        public int NextInt(int min, int max)
        {
            if (max <= min)
            {
                throw new ArgumentException($"max ({max}) must be greater than min ({min}).");
            }

            uint range = (uint)(max - min);
            return min + (int)(NextUInt() % range);
        }
    }
}
