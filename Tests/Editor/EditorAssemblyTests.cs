using System.Reflection;
using NUnit.Framework;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class EditorAssemblyTests
    {
        [Test]
        public void EditorAssemblyLoads()
        {
            Assert.That(
                Assembly.Load("NotRealGames.Areafinder.Editor").GetName().Name,
                Is.EqualTo("NotRealGames.Areafinder.Editor"));
        }
    }
}
