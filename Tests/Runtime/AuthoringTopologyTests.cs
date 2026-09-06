using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class AuthoringTopologyTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                Object.DestroyImmediate(_assets[index]);
            }

            _assets.Clear();
        }

        [Test]
        public void ConvexSplitPreservesChosenPolygonIdentityAndCreatesFreshGeometryIdentities()
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            NavigationPolygonRecord original = area.AddPolygon(Rectangle(0f, 0f, 2f, 2f));
            PolygonId originalId = original.Id;
            VertexId oldVertex = original.Vertices[0].Id;
            EdgeId oldEdge = original.Vertices[0].OutgoingEdgeId;

            bool split = area.SplitPolygon(original.Id, 0, 2, out NavigationPolygonRecord preserved,
                out NavigationPolygonRecord created);

            Assert.That(split, Is.True);
            Assert.That(area.Polygons.Count, Is.EqualTo(2));
            Assert.That(preserved.Id, Is.EqualTo(originalId));
            Assert.That(created.Id, Is.Not.EqualTo(originalId));
            Assert.That(preserved.Vertices.Count, Is.EqualTo(3));
            Assert.That(created.Vertices.Count, Is.EqualTo(3));
            Assert.That(preserved.Vertices[0].Id, Is.Not.EqualTo(oldVertex));
            Assert.That(preserved.Vertices[0].OutgoingEdgeId, Is.Not.EqualTo(oldEdge));
        }

        [Test]
        public void CompatibleMergePreservesFirstPolygonAndProducesConvexOutline()
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            NavigationPolygonRecord first = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationPolygonRecord second = area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            PolygonId preservedId = first.Id;

            bool merged = area.MergePolygons(first.Id, second.Id, 0.01f, out NavigationPolygonRecord result);

            Assert.That(merged, Is.True);
            Assert.That(area.Polygons.Count, Is.EqualTo(1));
            Assert.That(result.Id, Is.EqualTo(preservedId));
            Assert.That(result.Vertices.Count, Is.EqualTo(6),
                "collinear authored vertices remain explicit rather than being silently simplified");

            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset world = Create<NavigationWorldAsset>();
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            world.SetSemanticRegistry(registry);
            world.AddArea(area);
            Assert.That(NavigationBaker.Bake(world, bake).Succeeded, Is.True);
        }

        [Test]
        public void MergeRefusesToDiscardDifferentSemanticMeaning()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            registry.Add("Different");
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            NavigationPolygonRecord first = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationPolygonRecord second = area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            var mask = new SemanticMask(registry.SlotCapacity);
            mask.Set(0);
            second.SetSemantics(mask);

            Assert.That(area.MergePolygons(first.Id, second.Id, 0.01f, out _), Is.False);
            Assert.That(area.Polygons.Count, Is.EqualTo(2));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static Vector3[] Rectangle(float minX, float minZ, float maxX, float maxZ)
        {
            return new[]
            {
                new Vector3(minX, 0f, minZ),
                new Vector3(minX, 0f, maxZ),
                new Vector3(maxX, 0f, maxZ),
                new Vector3(maxX, 0f, minZ)
            };
        }
    }
}
