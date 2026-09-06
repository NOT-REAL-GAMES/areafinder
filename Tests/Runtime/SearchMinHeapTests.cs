using NUnit.Framework;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class SearchMinHeapTests
    {
        [Test]
        public void PopUsesPriorityThenStableTieBreaker()
        {
            SearchMinHeap heap = new SearchMinHeap(4);
            heap.PushOrDecrease(2, 2f, 0);
            heap.PushOrDecrease(0, 1f, 20);
            heap.PushOrDecrease(1, 1f, 10);

            Assert.That(heap.TryPop(out int first, out _), Is.True);
            Assert.That(heap.TryPop(out int second, out _), Is.True);
            Assert.That(heap.TryPop(out int third, out _), Is.True);
            Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { 1, 0, 2 }));
        }

        [Test]
        public void ExistingNodeCanDecreasePriority()
        {
            SearchMinHeap heap = new SearchMinHeap(3);
            heap.PushOrDecrease(0, 4f, 0);
            heap.PushOrDecrease(1, 2f, 0);
            heap.PushOrDecrease(0, 1f, 0);

            Assert.That(heap.Count, Is.EqualTo(2));
            Assert.That(heap.TryPop(out int first, out float priority), Is.True);
            Assert.That(first, Is.EqualTo(0));
            Assert.That(priority, Is.EqualTo(1f));
        }
    }
}
