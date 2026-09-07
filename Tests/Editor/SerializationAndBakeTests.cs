using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class SerializationAndBakeTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        [SerializeField] private string _assetRoot;
        [SerializeField] private string _registryGuid;
        [SerializeField] private string _firstAreaGuid;
        [SerializeField] private string _secondAreaGuid;
        [SerializeField] private string _worldGuid;
        [SerializeField] private string _policyGuid;
        [SerializeField] private string _bakeGuid;
        [SerializeField] private string[] _semanticIds;
        [SerializeField] private string[] _authoringIds;
        [SerializeField] private string _sourceFingerprint;
        [SerializeField] private string _registryFingerprint;
        [SerializeField] private string _bakeBytes;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_assetRoot) && AssetDatabase.IsValidFolder(_assetRoot))
            {
                AssetDatabase.DeleteAsset(_assetRoot);
            }

            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_objects[index]);
            }

            _objects.Clear();
            _assetRoot = null;
        }

        [UnityTest]
        public IEnumerator AssetDatabaseRoundTripSurvivesDomainReload()
        {
            _assetRoot = $"Assets/AreafinderPersistence-{Guid.NewGuid():N}";
            Assert.That(AssetDatabase.CreateFolder("Assets", Path.GetFileName(_assetRoot)), Is.Not.Empty);

            var registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            var semantics = new SemanticId[129];
            for (int slot = 0; slot < semantics.Length; slot++)
            {
                semantics[slot] = registry.Add($"Semantic {slot}", $"Persistent slot {slot}");
            }

            Assert.That(registry.Delete(semantics[65]), Is.True);

            var firstArea = ScriptableObject.CreateInstance<NavigationAreaAsset>();
            firstArea.SetFrame(new AreaFrame(new Double3(125000d, -4d, 250000d), Quaternion.Euler(0f, 15f, 0f)));
            firstArea.SetSemantics(Mask(0, 64, 128));
            firstArea.SetRequiredCapabilities(Mask(1, 63, 127));
            NavigationPolygonRecord firstPolygon = firstArea.AddPolygon(Rectangle(0f, 1f));
            NavigationPolygonRecord secondPolygon = firstArea.AddPolygon(Rectangle(1f, 2f));
            firstPolygon.SetSemantics(Mask(1, 65, 127));
            firstPolygon.SetRequiredCapabilities(Mask(63, 128));
            firstArea.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(firstPolygon, 2),
                Edge(secondPolygon, 0),
                AdjacencyOverrideState.ForcedDisconnected));

            var secondArea = ScriptableObject.CreateInstance<NavigationAreaAsset>();
            secondArea.SetFrame(new AreaFrame(new Double3(125000d, -4d, 250000d), Quaternion.Euler(0f, 15f, 0f)));
            secondArea.SetSemantics(Mask(63, 65));
            NavigationPolygonRecord destinationPolygon = secondArea.AddPolygon(Rectangle(1f, 2f));

            var policy = ScriptableObject.CreateInstance<TraversalPolicyAsset>();
            policy.SetRegistry(registry);
            policy.SetCapabilityMask(Mask(1, 63, 127, 128));
            policy.SetEligibility(
                NavigationElementKind.Polygon,
                new SemanticPredicate(new SemanticMask(), Mask(1, 127), new SemanticMask()));
            policy.SetCostRule(semantics[63], 1.75d, 2.5d);
            policy.SetCostRule(semantics[128], 2.25d, 4.5d);

            var world = ScriptableObject.CreateInstance<NavigationWorldAsset>();
            world.SetSemanticRegistry(registry);
            world.SetInferenceSettings(new AdjacencyInferenceSettings(0.02f, 0.02f, 0.08f, 20f, 0.01f, 0.75f));
            world.AddArea(firstArea);
            world.AddArea(secondArea);
            world.AddPolicy(policy);
            NavigationPortalRecord portal = world.AddPortal(
                Span(firstArea, secondPolygon, 2),
                Span(secondArea, destinationPolygon, 2),
                PortalDirection.Bidirectional,
                1.5d,
                PortalTransform.Identity);
            portal.SetSemantics(Mask(0, 64, 128));
            portal.SetRequiredCapabilities(Mask(1, 127));

            var bake = ScriptableObject.CreateInstance<NavigationBakeAsset>();
            NavigationBakeResult initialBake = NavigationBaker.Bake(world, bake);
            Assert.That(initialBake.Succeeded, Is.True, FormatIssues(initialBake));

            string registryPath = $"{_assetRoot}/Registry.asset";
            string firstAreaPath = $"{_assetRoot}/First Area.asset";
            string secondAreaPath = $"{_assetRoot}/Second Area.asset";
            string worldPath = $"{_assetRoot}/World.asset";
            string policyPath = $"{_assetRoot}/Policy.asset";
            string bakePath = $"{_assetRoot}/Bake.asset";
            AssetDatabase.CreateAsset(registry, registryPath);
            AssetDatabase.CreateAsset(firstArea, firstAreaPath);
            AssetDatabase.CreateAsset(secondArea, secondAreaPath);
            AssetDatabase.CreateAsset(policy, policyPath);
            AssetDatabase.CreateAsset(world, worldPath);
            AssetDatabase.CreateAsset(bake, bakePath);
            AssetDatabase.SaveAssets();

            _registryGuid = AssetDatabase.AssetPathToGUID(registryPath);
            _firstAreaGuid = AssetDatabase.AssetPathToGUID(firstAreaPath);
            _secondAreaGuid = AssetDatabase.AssetPathToGUID(secondAreaPath);
            _worldGuid = AssetDatabase.AssetPathToGUID(worldPath);
            _policyGuid = AssetDatabase.AssetPathToGUID(policyPath);
            _bakeGuid = AssetDatabase.AssetPathToGUID(bakePath);
            int[] checkedSlots = { 0, 1, 63, 64, 65, 127, 128 };
            _semanticIds = new string[checkedSlots.Length];
            for (int index = 0; index < checkedSlots.Length; index++)
            {
                _semanticIds[index] = semantics[checkedSlots[index]].ToString();
            }

            _authoringIds = new[]
            {
                firstArea.Id.ToString(),
                secondArea.Id.ToString(),
                firstPolygon.Id.ToString(),
                secondPolygon.Id.ToString(),
                destinationPolygon.Id.ToString(),
                firstPolygon.Vertices[0].Id.ToString(),
                firstPolygon.Vertices[0].OutgoingEdgeId.ToString(),
                portal.Id.ToString(),
                policy.Id.ToString()
            };
            _sourceFingerprint = bake.SourceFingerprint.ToString();
            _registryFingerprint = bake.SemanticRegistryFingerprint.ToString();
            _bakeBytes = Convert.ToBase64String(File.ReadAllBytes(bakePath));

            CompilationPipeline.RequestScriptCompilation();
            yield return new RecompileScripts();

            registryPath = $"{_assetRoot}/Registry.asset";
            firstAreaPath = $"{_assetRoot}/First Area.asset";
            secondAreaPath = $"{_assetRoot}/Second Area.asset";
            worldPath = $"{_assetRoot}/World.asset";
            policyPath = $"{_assetRoot}/Policy.asset";
            bakePath = $"{_assetRoot}/Bake.asset";
            checkedSlots = new[] { 0, 1, 63, 64, 65, 127, 128 };

            Assert.That(AssetDatabase.GUIDToAssetPath(_registryGuid), Is.EqualTo(registryPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(_firstAreaGuid), Is.EqualTo(firstAreaPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(_secondAreaGuid), Is.EqualTo(secondAreaPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(_worldGuid), Is.EqualTo(worldPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(_policyGuid), Is.EqualTo(policyPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(_bakeGuid), Is.EqualTo(bakePath));

            registry = AssetDatabase.LoadAssetAtPath<SemanticRegistryAsset>(registryPath);
            firstArea = AssetDatabase.LoadAssetAtPath<NavigationAreaAsset>(firstAreaPath);
            secondArea = AssetDatabase.LoadAssetAtPath<NavigationAreaAsset>(secondAreaPath);
            world = AssetDatabase.LoadAssetAtPath<NavigationWorldAsset>(worldPath);
            policy = AssetDatabase.LoadAssetAtPath<TraversalPolicyAsset>(policyPath);
            bake = AssetDatabase.LoadAssetAtPath<NavigationBakeAsset>(bakePath);

            Assert.That(registry, Is.Not.Null);
            Assert.That(firstArea, Is.Not.Null);
            Assert.That(secondArea, Is.Not.Null);
            Assert.That(world, Is.Not.Null);
            Assert.That(policy, Is.Not.Null);
            Assert.That(bake, Is.Not.Null);
            Assert.That(world.SemanticRegistry, Is.SameAs(registry));
            Assert.That(world.Areas, Is.EqualTo(new[] { firstArea, secondArea }));
            Assert.That(world.Policies, Is.EqualTo(new[] { policy }));
            Assert.That(policy.Registry, Is.SameAs(registry));
            Assert.That(bake.Source, Is.SameAs(world));

            Assert.That(registry.SlotCapacity, Is.EqualTo(129));
            Assert.That(registry.RequiredWordCount, Is.EqualTo(3));
            for (int index = 0; index < checkedSlots.Length; index++)
            {
                Assert.That(registry.TryGet(checkedSlots[index], out SemanticDefinition definition), Is.True);
                Assert.That(definition.Slot, Is.EqualTo(checkedSlots[index]));
                Assert.That(definition.Id.ToString(), Is.EqualTo(_semanticIds[index]));
                Assert.That(definition.IsDeleted, Is.EqualTo(checkedSlots[index] == 65));
            }

            firstPolygon = firstArea.Polygons[0];
            secondPolygon = firstArea.Polygons[1];
            destinationPolygon = secondArea.Polygons[0];
            portal = world.Portals[0];
            Assert.That(firstArea.Id.ToString(), Is.EqualTo(_authoringIds[0]));
            Assert.That(secondArea.Id.ToString(), Is.EqualTo(_authoringIds[1]));
            Assert.That(firstPolygon.Id.ToString(), Is.EqualTo(_authoringIds[2]));
            Assert.That(secondPolygon.Id.ToString(), Is.EqualTo(_authoringIds[3]));
            Assert.That(destinationPolygon.Id.ToString(), Is.EqualTo(_authoringIds[4]));
            Assert.That(firstPolygon.Vertices[0].Id.ToString(), Is.EqualTo(_authoringIds[5]));
            Assert.That(firstPolygon.Vertices[0].OutgoingEdgeId.ToString(), Is.EqualTo(_authoringIds[6]));
            Assert.That(portal.Id.ToString(), Is.EqualTo(_authoringIds[7]));
            Assert.That(policy.Id.ToString(), Is.EqualTo(_authoringIds[8]));
            Assert.That(firstArea.AdjacencyOverrides.Count, Is.EqualTo(1));
            Assert.That(firstArea.AdjacencyOverrides[0].State,
                Is.EqualTo(AdjacencyOverrideState.ForcedDisconnected));
            Assert.That(firstArea.AdjacencyOverrides[0].First, Is.EqualTo(Edge(firstPolygon, 2)));
            Assert.That(firstArea.AdjacencyOverrides[0].Second, Is.EqualTo(Edge(secondPolygon, 0)));
            Assert.That(portal.Source.PolygonId, Is.EqualTo(secondPolygon.Id));
            Assert.That(portal.Destination.PolygonId, Is.EqualTo(destinationPolygon.Id));
            Assert.That(portal.Direction, Is.EqualTo(PortalDirection.Bidirectional));
            Assert.That(portal.BaseCost, Is.EqualTo(1.5d));

            AssertMask(firstArea.Semantics, 0, 64, 128);
            AssertMask(firstArea.RequiredCapabilities, 1, 63, 127);
            AssertMask(firstPolygon.Semantics, 1, 65, 127);
            AssertMask(firstPolygon.RequiredCapabilities, 63, 128);
            AssertMask(portal.Semantics, 0, 64, 128);
            AssertMask(portal.RequiredCapabilities, 1, 127);
            AssertMask(policy.Data.CapabilityMask, 1, 63, 127, 128);
            Assert.That(policy.Data.CostRules.Count, Is.EqualTo(2));
            Assert.That(policy.Data.CostRules[0].Slot, Is.EqualTo(63));
            Assert.That(policy.Data.CostRules[1].Slot, Is.EqualTo(128));

            Assert.That(bake.SchemaVersion, Is.EqualTo(1));
            Assert.That(bake.SemanticWordCount, Is.EqualTo(3));
            Assert.That(bake.SourceFingerprint.ToString(), Is.EqualTo(_sourceFingerprint));
            Assert.That(bake.SemanticRegistryFingerprint.ToString(), Is.EqualTo(_registryFingerprint));
            Assert.That(bake.SourceFingerprint, Is.EqualTo(NavigationBaker.ComputeSourceFingerprint(world)));
            Assert.That(bake.SemanticRegistryFingerprint, Is.EqualTo(registry.SchemaFingerprint));
            Assert.That(bake.Areas.Count, Is.EqualTo(2));
            Assert.That(bake.Polygons.Count, Is.EqualTo(3));
            Assert.That(bake.Portals.Count, Is.EqualTo(1));
            Assert.That(bake.Portals[0].Id, Is.EqualTo(portal.Id));
            Assert.That(bake.SemanticWords.Count % bake.SemanticWordCount, Is.Zero);
            Assert.That(bake.IsStale(world), Is.False);
            Assert.That(bake.IsUsable, Is.True);

            NavigationBakeResult repeatedBake = NavigationBaker.Bake(world, bake);
            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssets();
            Assert.That(repeatedBake.Succeeded, Is.True, FormatIssues(repeatedBake));
            CollectionAssert.AreEqual(Convert.FromBase64String(_bakeBytes), File.ReadAllBytes(bakePath));

            firstPolygon.MoveVertex(0, new Vector3(-0.25f, 0f, 0f));
            firstArea.Touch();
            Assert.That(bake.IsStale(world), Is.True);
            Assert.That(bake.IsUsable, Is.False);
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

        private static SemanticMask Mask(params int[] slots)
        {
            var mask = new SemanticMask();
            for (int index = 0; index < slots.Length; index++)
            {
                mask.Set(slots[index]);
            }

            return mask;
        }

        private static void AssertMask(SemanticMask mask, params int[] expectedSlots)
        {
            int highestSlot = -1;
            for (int index = 0; index < expectedSlots.Length; index++)
            {
                highestSlot = Math.Max(highestSlot, expectedSlots[index]);
            }

            Assert.That(mask.WordCount, Is.EqualTo(SemanticMask.WordCountForSlots(highestSlot + 1)));
            var expected = new HashSet<int>(expectedSlots);
            int[] boundaries = { 0, 1, 63, 64, 65, 127, 128 };
            for (int index = 0; index < boundaries.Length; index++)
            {
                Assert.That(mask.Contains(boundaries[index]), Is.EqualTo(expected.Contains(boundaries[index])),
                    $"Unexpected value for semantic slot {boundaries[index]}.");
            }
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
