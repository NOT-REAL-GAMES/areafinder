using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class SemanticsProjectSettingsTests
    {
        private const string TestAssetPath = "Assets/AreafinderSemanticRegistrySettingsTest.asset";
        private SemanticRegistryAsset _previousRegistry;

        [SetUp]
        public void SetUp()
        {
            _previousRegistry = SemanticRegistryProjectSettings.Registry;
            AssetDatabase.DeleteAsset(TestAssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            SemanticRegistryProjectSettings.TrySetRegistry(_previousRegistry, out _);
            AssetDatabase.DeleteAsset(TestAssetPath);
        }

        [Test]
        public void StoresExactlyOnePersistentRegistryForTheProject()
        {
            var registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            AssetDatabase.CreateAsset(registry, TestAssetPath);

            Assert.That(SemanticRegistryProjectSettings.TrySetRegistry(registry, out string error), Is.True, error);
            Assert.That(SemanticRegistryProjectSettings.Registry, Is.SameAs(registry));
        }

        [Test]
        public void RejectsUnsavedRegistry()
        {
            var registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            try
            {
                Assert.That(SemanticRegistryProjectSettings.TrySetRegistry(registry, out string error), Is.False);
                Assert.That(error, Does.Contain("saved"));
                Assert.That(SemanticRegistryProjectSettings.Registry, Is.SameAs(_previousRegistry));
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }
    }
}
