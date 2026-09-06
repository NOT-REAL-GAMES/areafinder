using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class SerializationAndBakeTests
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
        public void UnitySerializationPreservesAuthoringAndCompiledIdentities()
        {
            Fixture fixture = CreateFixture();
            string registryJson = EditorJsonUtility.ToJson(fixture.Registry, true);
            string areaJson = EditorJsonUtility.ToJson(fixture.FirstArea, true);
            string worldJson = EditorJsonUtility.ToJson(fixture.World, true);
            string policyJson = EditorJsonUtility.ToJson(fixture.Policy, true);
            string bakeJson = EditorJsonUtility.ToJson(fixture.Bake, true);

            SemanticRegistryAsset registryCopy = Create<SemanticRegistryAsset>();
            NavigationAreaAsset areaCopy = Create<NavigationAreaAsset>();
            NavigationWorldAsset worldCopy = Create<NavigationWorldAsset>();
            TraversalPolicyAsset policyCopy = Create<TraversalPolicyAsset>();
            NavigationBakeAsset bakeCopy = Create<NavigationBakeAsset>();
            EditorJsonUtility.FromJsonOverwrite(registryJson, registryCopy);
            EditorJsonUtility.FromJsonOverwrite(areaJson, areaCopy);
            EditorJsonUtility.FromJsonOverwrite(worldJson, worldCopy);
            EditorJsonUtility.FromJsonOverwrite(policyJson, policyCopy);
            EditorJsonUtility.FromJsonOverwrite(bakeJson, bakeCopy);

            Assert.That(registryCopy.SlotCapacity, Is.EqualTo(14));
            Assert.That(registryCopy.TryGet(12, out SemanticDefinition deleted), Is.True);
            Assert.That(deleted.Id, Is.EqualTo(fixture.DeletedSemantic));
            Assert.That(deleted.IsDeleted, Is.True);
            Assert.That(registryCopy.TryGet(13, out SemanticDefinition following), Is.True);
            Assert.That(following.Id, Is.EqualTo(fixture.FollowingSemantic));

            Assert.That(areaCopy.Id, Is.EqualTo(fixture.FirstArea.Id));
            Assert.That(areaCopy.Polygons.Count, Is.EqualTo(2));
            Assert.That(areaCopy.Polygons[0].Id, Is.EqualTo(fixture.FirstPolygon.Id));
            Assert.That(areaCopy.Polygons[0].Vertices[0].Id, Is.EqualTo(fixture.FirstVertex));
            Assert.That(areaCopy.Polygons[0].Vertices[0].OutgoingEdgeId, Is.EqualTo(fixture.FirstEdge));
            Assert.That(areaCopy.AdjacencyOverrides.Count, Is.EqualTo(1));
            Assert.That(areaCopy.AdjacencyOverrides[0].State,
                Is.EqualTo(AdjacencyOverrideState.ForcedDisconnected));

            Assert.That(worldCopy.Portals.Count, Is.EqualTo(1));
            Assert.That(worldCopy.Portals[0].Id, Is.EqualTo(fixture.Portal.Id));
            Assert.That(worldCopy.Portals[0].Source.PolygonId, Is.EqualTo(fixture.SecondPolygon.Id));
            Assert.That(worldCopy.Policies.Count, Is.EqualTo(1));
            Assert.That(policyCopy.Id, Is.EqualTo(fixture.Policy.Id));
            Assert.That(policyCopy.Data.CostRules[0].SemanticId, Is.EqualTo(fixture.FollowingSemantic));

            Assert.That(bakeCopy.SourceFingerprint, Is.EqualTo(fixture.Bake.SourceFingerprint));
            Assert.That(bakeCopy.Areas.Count, Is.EqualTo(2));
            Assert.That(bakeCopy.Polygons.Count, Is.EqualTo(3));
            Assert.That(bakeCopy.Portals[0].Id, Is.EqualTo(fixture.Portal.Id));
        }

        [Test]
        public void RepeatedBakeIsDeterministicAndAnEditMakesTheCommittedBakeUnusable()
        {
            Fixture fixture = CreateFixture();
            string first = EditorJsonUtility.ToJson(fixture.Bake, true);

            NavigationBakeResult secondResult = NavigationBaker.Bake(fixture.World, fixture.Bake);
            string second = EditorJsonUtility.ToJson(fixture.Bake, true);

            Assert.That(secondResult.Succeeded, Is.True, FormatIssues(secondResult));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(fixture.Bake.IsUsable, Is.True);

            fixture.FirstPolygon.MoveVertex(0, new Vector3(-0.25f, 0f, 0f));
            fixture.FirstArea.Touch();

            Assert.That(fixture.Bake.IsStale(fixture.World), Is.True);
            Assert.That(fixture.Bake.IsUsable, Is.False);
            Assert.That(() => new NavigationWorld(fixture.Bake), Throws.ArgumentException);
        }

        private Fixture CreateFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            SemanticId deleted = default;
            SemanticId following = default;
            for (int slot = 0; slot < 14; slot++)
            {
                SemanticId id = registry.Add($"Semantic {slot}");
                if (slot == 12)
                {
                    deleted = id;
                }
                else if (slot == 13)
                {
                    following = id;
                }
            }

            registry.Delete(deleted);

            NavigationAreaAsset firstArea = Create<NavigationAreaAsset>();
            NavigationPolygonRecord firstPolygon = firstArea.AddPolygon(Rectangle(0f, 1f));
            NavigationPolygonRecord secondPolygon = firstArea.AddPolygon(Rectangle(1f, 2f));
            firstPolygon.RawSemantics.Set(13);
            firstArea.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(firstPolygon, 2),
                Edge(secondPolygon, 0),
                AdjacencyOverrideState.ForcedDisconnected));

            NavigationAreaAsset secondArea = Create<NavigationAreaAsset>();
            NavigationPolygonRecord destinationPolygon = secondArea.AddPolygon(Rectangle(1f, 2f));

            TraversalPolicyAsset policy = Create<TraversalPolicyAsset>();
            policy.SetRegistry(registry);
            policy.SetCostRule(following, 2d, 3d);

            NavigationWorldAsset world = Create<NavigationWorldAsset>();
            world.SetSemanticRegistry(registry);
            world.AddArea(firstArea);
            world.AddArea(secondArea);
            world.AddPolicy(policy);
            NavigationPortalRecord portal = world.AddPortal(
                Span(firstArea, secondPolygon, 2),
                Span(secondArea, destinationPolygon, 2),
                PortalDirection.Bidirectional,
                1.5d,
                PortalTransform.Identity);

            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult result = NavigationBaker.Bake(world, bake);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));

            return new Fixture(
                registry,
                world,
                firstArea,
                firstPolygon,
                secondPolygon,
                policy,
                portal,
                bake,
                deleted,
                following,
                firstPolygon.Vertices[0].Id,
                firstPolygon.Vertices[0].OutgoingEdgeId);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _objects.Add(value);
            return value;
        }

        private static Vector3[] Rectangle(float minimumX, float maximumX)
        {
            return new[]
            {
                new Vector3(minimumX, 0f, 0f),
                new Vector3(minimumX, 0f, 1f),
                new Vector3(maximumX, 0f, 1f),
                new Vector3(maximumX, 0f, 0f)
            };
        }

        private static PolygonEdgeReference Edge(NavigationPolygonRecord polygon, int edge)
        {
            return new PolygonEdgeReference(polygon.Id, polygon.Vertices[edge].OutgoingEdgeId);
        }

        private static PortalEntrySpan Span(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            int edge)
        {
            NavigationVertexRecord first = polygon.Vertices[edge];
            NavigationVertexRecord second = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                first.OutgoingEdgeId,
                first.Position,
                second.Position);
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                messages.Add($"{result.Issues[index].Severity}: {result.Issues[index].Code}: " +
                             result.Issues[index].Message);
            }

            return string.Join(Environment.NewLine, messages);
        }

        private sealed class Fixture
        {
            internal Fixture(
                SemanticRegistryAsset registry,
                NavigationWorldAsset world,
                NavigationAreaAsset firstArea,
                NavigationPolygonRecord firstPolygon,
                NavigationPolygonRecord secondPolygon,
                TraversalPolicyAsset policy,
                NavigationPortalRecord portal,
                NavigationBakeAsset bake,
                SemanticId deletedSemantic,
                SemanticId followingSemantic,
                VertexId firstVertex,
                EdgeId firstEdge)
            {
                Registry = registry;
                World = world;
                FirstArea = firstArea;
                FirstPolygon = firstPolygon;
                SecondPolygon = secondPolygon;
                Policy = policy;
                Portal = portal;
                Bake = bake;
                DeletedSemantic = deletedSemantic;
                FollowingSemantic = followingSemantic;
                FirstVertex = firstVertex;
                FirstEdge = firstEdge;
            }

            internal SemanticRegistryAsset Registry { get; }
            internal NavigationWorldAsset World { get; }
            internal NavigationAreaAsset FirstArea { get; }
            internal NavigationPolygonRecord FirstPolygon { get; }
            internal NavigationPolygonRecord SecondPolygon { get; }
            internal TraversalPolicyAsset Policy { get; }
            internal NavigationPortalRecord Portal { get; }
            internal NavigationBakeAsset Bake { get; }
            internal SemanticId DeletedSemantic { get; }
            internal SemanticId FollowingSemantic { get; }
            internal VertexId FirstVertex { get; }
            internal EdgeId FirstEdge { get; }
        }
    }
}
