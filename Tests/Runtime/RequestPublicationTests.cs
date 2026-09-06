using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class RequestPublicationTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_objects[index]);
            }

            _objects.Clear();
        }

        [Test]
        public void MutationAfterComputationButBeforePublicationProducesStale()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            NavigationPolygonRecord polygon = area.AddPolygon(new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 0f)
            });
            source.AddArea(area);
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            Assert.That(NavigationBaker.Bake(source, bake).Succeeded, Is.True);
            Assert.That(
                new TraversalPolicyBuilder(registry).TryCompile(
                    bake,
                    out CompiledTraversalPolicy policy,
                    out string error),
                Is.True,
                error);

            using var world = new NavigationWorld(bake);
            PathRequestStatus callbackStatus = PathRequestStatus.Invalid;
            int callbackCount = 0;
            PathRequestHandle request = world.Submit(
                new PathQuery(
                    new NavigationLocation(area.Id, new Vector3(0.25f, 0f, 0.5f)),
                    new NavigationLocation(area.Id, new Vector3(0.75f, 0f, 0.5f)),
                    policy),
                handle =>
                {
                    callbackCount++;
                    callbackStatus = world.GetStatus(handle);
                });

            world.Tick(64);
            Assert.That(world.GetStatus(request), Is.EqualTo(PathRequestStatus.RunningLocal));
            Assert.That(world.SetPolygonEnabled(polygon.Id, false), Is.True);
            world.Tick(0);

            Assert.That(world.GetStatus(request), Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(callbackStatus, Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(world.TryGetPath(request, out _), Is.False);
        }

        [Test]
        public void MutationOfEvaluatedAlternativeAreaBeforePublicationProducesStale()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset startArea = AddArea(source);
            NavigationAreaAsset goalArea = AddArea(source);
            NavigationAreaAsset alternativeArea = AddArea(source);
            var portalTransform = new PortalTransform(
                new Double3(-1d, 0d, 0d),
                Quaternion.identity);
            source.AddPortal(
                Span(startArea, 2),
                Span(goalArea, 0),
                PortalDirection.SourceToDestination,
                10d,
                portalTransform);
            source.AddPortal(
                Span(startArea, 2),
                Span(alternativeArea, 0),
                PortalDirection.SourceToDestination,
                1d,
                portalTransform);
            source.AddPortal(
                Span(alternativeArea, 2),
                Span(goalArea, 0),
                PortalDirection.SourceToDestination,
                20d,
                portalTransform);
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            Assert.That(NavigationBaker.Bake(source, bake).Succeeded, Is.True);
            Assert.That(
                new TraversalPolicyBuilder(registry).TryCompile(
                    bake,
                    out CompiledTraversalPolicy policy,
                    out string error),
                Is.True,
                error);

            using var world = new NavigationWorld(bake);
            PathRequestHandle request = world.Submit(new PathQuery(
                new NavigationLocation(startArea.Id, new Vector3(0.25f, 0f, 0.5f)),
                new NavigationLocation(goalArea.Id, new Vector3(0.75f, 0f, 0.5f)),
                policy));

            world.Tick(64);
            Assert.That(world.GetStatus(request), Is.EqualTo(PathRequestStatus.RunningLocal));
            Assert.That(world.MarkAreaDirty(alternativeArea.Id), Is.True);
            world.Tick(0);

            Assert.That(world.GetStatus(request), Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(world.TryGetPath(request, out _), Is.False);
        }

        private NavigationAreaAsset AddArea(NavigationWorldAsset source)
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            area.AddPolygon(new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 0f)
            });
            source.AddArea(area);
            return area;
        }

        private static PortalEntrySpan Span(NavigationAreaAsset area, int edge)
        {
            NavigationPolygonRecord polygon = area.Polygons[0];
            NavigationVertexRecord start = polygon.Vertices[edge];
            NavigationVertexRecord end = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                start.OutgoingEdgeId,
                start.Position,
                end.Position);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _objects.Add(value);
            return value;
        }
    }
}
