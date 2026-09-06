using NUnit.Framework;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class PathRequestTypesTests
    {
        [Test]
        public void DefaultHandleIsInvalid()
        {
            Assert.That(default(PathRequestHandle).IsValid, Is.False);
        }

        [Test]
        public void StatusesIncludeTheCompleteRequestLifecycle()
        {
            Assert.That(PathRequestStatus.Queued, Is.Not.EqualTo(PathRequestStatus.RunningGlobal));
            Assert.That(PathRequestStatus.RunningGlobal, Is.Not.EqualTo(PathRequestStatus.RunningLocal));
            Assert.That(PathRequestStatus.Completed, Is.Not.EqualTo(PathRequestStatus.Failed));
            Assert.That(PathRequestStatus.Cancelled, Is.Not.EqualTo(PathRequestStatus.Stale));
        }
    }
}
