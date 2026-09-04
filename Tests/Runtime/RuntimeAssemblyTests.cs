using System.Reflection;
using NUnit.Framework;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class RuntimeAssemblyTests
    {
        [Test]
        public void RuntimeAssemblyLoads()
        {
            Assert.That(
                Assembly.Load("NotRealGames.Areafinder").GetName().Name,
                Is.EqualTo("NotRealGames.Areafinder"));
        }
    }
}
