using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class SemanticRegistryCompactionTests
    {
        [Test]
        public void CompactionRetainsIdsAndRemapsAreaAndPolicyMasks()
        {
            var registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            var area = ScriptableObject.CreateInstance<NavigationAreaAsset>();
            var world = ScriptableObject.CreateInstance<NavigationWorldAsset>();
            var policy = ScriptableObject.CreateInstance<TraversalPolicyAsset>();
            try
            {
                SemanticId first = registry.Add("First");
                SemanticId deleted = registry.Add("Deleted");
                SemanticId last = registry.Add("Last");
                registry.Delete(deleted);

                NavigationPolygonRecord polygon = area.AddPolygon(new[]
                {
                    new Vector3(-1f, 0f, -1f),
                    new Vector3(-1f, 0f, 1f),
                    new Vector3(1f, 0f, 1f),
                    new Vector3(1f, 0f, -1f)
                });
                var mask = new SemanticMask(registry.SlotCapacity);
                mask.Set(0);
                mask.Set(2);
                polygon.SetSemantics(mask);
                world.SetSemanticRegistry(registry);
                world.AddArea(area);
                policy.SetRegistry(registry);
                policy.SetCostRule(last, 2d, 3d);

                int[] map = registry.BuildCompactionMap();
                SemanticRegistryCompactor.Compact(
                    registry,
                    map,
                    new[] { world },
                    new[] { area },
                    new[] { policy });

                Assert.That(registry.SlotCapacity, Is.EqualTo(2));
                Assert.That(registry.TryGet(first, out SemanticDefinition firstDefinition), Is.True);
                Assert.That(firstDefinition.Slot, Is.EqualTo(0));
                Assert.That(registry.TryGet(last, out SemanticDefinition lastDefinition), Is.True);
                Assert.That(lastDefinition.Slot, Is.EqualTo(1));
                Assert.That(registry.TryGet(deleted, out _), Is.False);
                Assert.That(polygon.Semantics.Contains(0), Is.True);
                Assert.That(polygon.Semantics.Contains(1), Is.True);
                Assert.That(polygon.Semantics.Contains(2), Is.False);
                Assert.That(policy.Data.CostRules.Count, Is.EqualTo(1));
                Assert.That(policy.Data.CostRules[0].SemanticId, Is.EqualTo(last));
                Assert.That(policy.Data.CostRules[0].Slot, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(policy);
                Object.DestroyImmediate(world);
                Object.DestroyImmediate(area);
                Object.DestroyImmediate(registry);
            }
        }
    }
}
