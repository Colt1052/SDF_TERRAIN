using NUnit.Framework;
using SDFTerrain.Core;

namespace SDFTerrain.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var a = new SeededRandom(12345);
            var b = new SeededRandom(12345);

            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);

            Assert.AreNotEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void NextFloat_IsWithinZeroToOneRange()
        {
            var rng = new SeededRandom(42);

            for (int i = 0; i < 1000; i++)
            {
                float value = rng.NextFloat();
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }
        }

        [Test]
        public void NextFloatRange_IsWithinBounds()
        {
            var rng = new SeededRandom(7);

            for (int i = 0; i < 1000; i++)
            {
                float value = rng.NextFloat(-5f, 5f);
                Assert.GreaterOrEqual(value, -5f);
                Assert.Less(value, 5f);
            }
        }

        [Test]
        public void NextIntRange_IsWithinBounds()
        {
            var rng = new SeededRandom(99);

            for (int i = 0; i < 1000; i++)
            {
                int value = rng.NextInt(10, 20);
                Assert.GreaterOrEqual(value, 10);
                Assert.Less(value, 20);
            }
        }

        [Test]
        public void ZeroSeed_DoesNotProduceDegenerateAllZeroState()
        {
            var rng = new SeededRandom(0);
            Assert.AreNotEqual(0u, rng.NextUInt());
        }
    }
}
