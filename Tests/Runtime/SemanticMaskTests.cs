using NUnit.Framework;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class SemanticMaskTests
    {
        private static readonly int[] BoundarySlots = { 0, 1, 63, 64, 65, 127, 128 };

        [Test]
        public void DynamicMaskStoresEveryBoundarySlot()
        {
            var mask = new SemanticMask();

            for (int index = 0; index < BoundarySlots.Length; index++)
            {
                mask.Set(BoundarySlots[index]);
            }

            Assert.That(mask.WordCount, Is.EqualTo(3));
            for (int index = 0; index < BoundarySlots.Length; index++)
            {
                Assert.That(mask.Contains(BoundarySlots[index]), Is.True, $"slot {BoundarySlots[index]}");
            }
            Assert.That(mask.Contains(2), Is.False);
            Assert.That(mask.Contains(126), Is.False);
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(63, 1)]
        [TestCase(64, 1)]
        [TestCase(65, 2)]
        [TestCase(127, 2)]
        [TestCase(128, 2)]
        [TestCase(129, 3)]
        public void WordCountUsesHighestAllocatedSlotPlusOne(int slotCapacity, int expectedWords)
        {
            Assert.That(SemanticMask.WordCountForSlots(slotCapacity), Is.EqualTo(expectedWords));
        }

        [Test]
        public void EligibilityOperationsWorkAcrossWordBoundaries()
        {
            var available = new SemanticMask();
            available.Set(0);
            available.Set(64);
            available.Set(128);
            var required = new SemanticMask();
            required.Set(64);
            required.Set(128);
            var overlapping = new SemanticMask();
            overlapping.Set(128);

            Assert.That(available.ContainsAll(required), Is.True);
            Assert.That(available.Intersects(overlapping), Is.True);

            required.Set(127);
            Assert.That(available.ContainsAll(required), Is.False);
            overlapping.Set(128, false);
            Assert.That(available.Intersects(overlapping), Is.False);
        }

        [Test]
        public void ClearingHighBitDoesNotShrinkOrDisturbOtherWords()
        {
            var mask = new SemanticMask();
            mask.Set(1);
            mask.Set(128);

            mask.Set(128, false);

            Assert.That(mask.WordCount, Is.EqualTo(3));
            Assert.That(mask.Contains(1), Is.True);
            Assert.That(mask.Contains(128), Is.False);
        }

        [Test]
        public void NegativeSlotsAreRejected()
        {
            var mask = new SemanticMask();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => mask.Set(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => mask.Contains(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SemanticMask.WordCountForSlots(-1));
        }
    }
}
