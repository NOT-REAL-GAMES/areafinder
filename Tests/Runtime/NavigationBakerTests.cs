using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class NavigationBakerTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private SemanticRegistryAsset _registry;
        private NavigationWorldAsset _world;
        private NavigationBakeAsset _bake;

        [SetUp]
        public void SetUp()
        {
            _registry = Create<SemanticRegistryAsset>();
            _world = Create<NavigationWorldAsset>();
            _bake = Create<NavigationBakeAsset>();
            _world.SetSemanticRegistry(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[index]);
            }

            _assets.Clear();
        }

        [Test]
        public void MatchingReversedEdgesInferBidirectionalAdjacency()
        {
            AddAdjacentPair(0f, out _, out _);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies.Count, Is.EqualTo(2));
            Assert.That(_bake.Adjacencies[0].Source, Is.EqualTo(AdjacencyOverrideState.Automatic));
            Assert.That(_bake.Adjacencies[0].FromPolygon, Is.EqualTo(_bake.Adjacencies[1].ToPolygon));
            Assert.That(_bake.Adjacencies[0].ToPolygon, Is.EqualTo(_bake.Adjacencies[1].FromPolygon));
        }

        [Test]
        public void PartialEdgeOverlapProducesOnlyTheSharedCrossingSpan()
        {
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            area.AddPolygon(Rectangle(1f, 0.25f, 2f, 0.75f));

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies.Count, Is.EqualTo(2));
            Assert.That(Vector3.Distance(
                _bake.Adjacencies[0].SpanStart,
                _bake.Adjacencies[0].SpanEnd), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void MovingEdgesOutsideToleranceRemovesAutomaticAdjacency()
        {
            AddAdjacentPair(0.02f, out _, out _);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies, Is.Empty);
        }

        [Test]
        public void ForcedDisconnectedOverridesCoincidentGeometry()
        {
            NavigationAreaAsset area = AddAdjacentPair(0f, out NavigationPolygonRecord first, out NavigationPolygonRecord second);
            area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(first, 2),
                Edge(second, 0),
                AdjacencyOverrideState.ForcedDisconnected));

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies, Is.Empty);
        }

        [Test]
        public void ForcedConnectedMayBypassSoftGapWithinHardLimit()
        {
            NavigationAreaAsset area = AddAdjacentPair(0.25f, out NavigationPolygonRecord first, out NavigationPolygonRecord second);
            area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(first, 2),
                Edge(second, 0),
                AdjacencyOverrideState.ForcedConnected));

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies.Count, Is.EqualTo(2));
            Assert.That(_bake.Adjacencies[0].Source, Is.EqualTo(AdjacencyOverrideState.ForcedConnected));
        }

        [Test]
        public void ForcedConnectedCannotBypassHardGapLimit()
        {
            NavigationAreaAsset area = AddAdjacentPair(0.51f, out NavigationPolygonRecord first, out NavigationPolygonRecord second);
            area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(first, 2),
                Edge(second, 0),
                AdjacencyOverrideState.ForcedConnected));

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.False);
            AssertIssue(result, NavigationValidationCode.InvalidForcedConnection);
        }

        [Test]
        public void ForcedConnectedMayBypassSoftEdgeAngleTolerance()
        {
            NavigationAreaAsset area = AddArea();
            NavigationPolygonRecord first = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationPolygonRecord second = area.AddPolygon(new[]
            {
                new Vector3(1.1f, 0f, 0f),
                new Vector3(1.3f, 0f, 0.346f),
                new Vector3(2f, 0f, 0.346f),
                new Vector3(2f, 0f, 0f)
            });
            area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(first, 2),
                Edge(second, 0),
                AdjacencyOverrideState.ForcedConnected));

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Adjacencies.Count, Is.EqualTo(2));
            Assert.That(_bake.Adjacencies[0].Source,
                Is.EqualTo(AdjacencyOverrideState.ForcedConnected));
        }

        [Test]
        public void BakeCompilesValidPortalWithoutMergingAreaTopology()
        {
            NavigationAreaAsset sourceArea = AddArea();
            NavigationPolygonRecord sourcePolygon = sourceArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationAreaAsset destinationArea = AddArea();
            NavigationPolygonRecord destinationPolygon = destinationArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            PortalEntrySpan source = Span(sourceArea, sourcePolygon, 2);
            PortalEntrySpan destination = Span(destinationArea, destinationPolygon, 2);
            NavigationPortalRecord portal = _world.AddPortal(
                source,
                destination,
                PortalDirection.Bidirectional,
                2.5d,
                PortalTransform.Identity);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.Areas.Count, Is.EqualTo(2));
            Assert.That(_bake.Portals.Count, Is.EqualTo(1));
            Assert.That(_bake.Portals[0].Id, Is.EqualTo(portal.Id));
            Assert.That(_bake.Portals[0].Direction, Is.EqualTo(PortalDirection.Bidirectional));
            Assert.That(_bake.Portals[0].BaseCost, Is.EqualTo(2.5d));
            Assert.That(_bake.Portals[0].SourceArea, Is.Not.EqualTo(_bake.Portals[0].DestinationArea));
        }

        [Test]
        public void PortalSpanOutsideReferencedEdgeBlocksBake()
        {
            NavigationAreaAsset sourceArea = AddArea();
            NavigationPolygonRecord sourcePolygon = sourceArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationAreaAsset destinationArea = AddArea();
            NavigationPolygonRecord destinationPolygon = destinationArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            PortalEntrySpan invalidSource = new PortalEntrySpan(
                sourceArea.Id,
                sourcePolygon.Id,
                sourcePolygon.Vertices[2].OutgoingEdgeId,
                new Vector3(4f, 0f, 4f),
                new Vector3(4f, 0f, 5f));
            _world.AddPortal(
                invalidSource,
                Span(destinationArea, destinationPolygon, 2),
                PortalDirection.SourceToDestination,
                0d,
                PortalTransform.Identity);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.False);
            AssertIssue(result, NavigationValidationCode.InvalidPortalSpan);
        }

        [Test]
        public void ReplacingAreaPolygonsPreservesWorldTopologyButInvalidatesAttachments()
        {
            NavigationAreaAsset sourceArea = AddArea();
            NavigationPolygonRecord oldPolygon = sourceArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationAreaAsset destinationArea = AddArea();
            NavigationPolygonRecord destinationPolygon = destinationArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationPortalRecord portal = _world.AddPortal(
                Span(sourceArea, oldPolygon, 2),
                Span(destinationArea, destinationPolygon, 2),
                PortalDirection.SourceToDestination,
                0d,
                PortalTransform.Identity);
            AreaId areaId = sourceArea.Id;
            PortalId portalId = portal.Id;

            Assert.That(sourceArea.RemovePolygon(oldPolygon.Id), Is.True);
            sourceArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(sourceArea.Id, Is.EqualTo(areaId));
            Assert.That(_world.Portals[0].Id, Is.EqualTo(portalId));
            Assert.That(result.Succeeded, Is.False);
            AssertIssue(result, NavigationValidationCode.InvalidPortalPolygon);
        }

        [TestCaseSource(nameof(InvalidPolygonCases))]
        public void InvalidPolygonGeometryBlocksBake(
            Vector3[] vertices,
            NavigationValidationCode expectedIssue)
        {
            AddArea().AddPolygon(vertices);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.False);
            AssertIssue(result, expectedIssue);
        }

        [Test]
        public void FailedBakeLeavesPreviousCompiledDataUntouched()
        {
            NavigationAreaAsset area = AddArea();
            NavigationPolygonRecord valid = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            Assert.That(NavigationBaker.Bake(_world, _bake).Succeeded, Is.True);
            ulong fingerprint = _bake.SourceFingerprint;
            PolygonId compiledPolygon = _bake.Polygons[0].Id;

            valid.MoveVertex(2, valid.Vertices[1].Position);
            NavigationBakeResult failed = NavigationBaker.Bake(_world, _bake);

            Assert.That(failed.Succeeded, Is.False);
            Assert.That(_bake.SourceFingerprint, Is.EqualTo(fingerprint));
            Assert.That(_bake.Polygons[0].Id, Is.EqualTo(compiledPolygon));
            Assert.That(_bake.IsStale(_world), Is.True);
        }

        [Test]
        public void InvalidNullEntryMakesPreviousBakeStale()
        {
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            Assert.That(NavigationBaker.Bake(_world, _bake).Succeeded, Is.True);

            ((List<NavigationAreaAsset>)_world.Areas).Add(null);
            NavigationBakeResult failed = NavigationBaker.Bake(_world, _bake);

            Assert.That(failed.Succeeded, Is.False);
            AssertIssue(failed, NavigationValidationCode.NullArea);
            Assert.That(_bake.IsStale(_world), Is.True);
            Assert.That(_bake.IsUsable, Is.False);
        }

        [Test]
        public void TrackedPolicyContentEditLeavesPreviousGeometryBakeUsable()
        {
            SemanticId walkable = _registry.Add("Walkable");
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            TraversalPolicyAsset policy = Create<TraversalPolicyAsset>();
            policy.SetRegistry(_registry);
            _world.AddPolicy(policy);
            Assert.That(NavigationBaker.Bake(_world, _bake).Succeeded, Is.True);
            ulong fingerprint = _bake.SourceFingerprint;

            policy.SetCostRule(walkable, 2d, 3d);

            Assert.That(NavigationBaker.Validate(_world).Succeeded, Is.True);
            Assert.That(NavigationBaker.ComputeSourceFingerprint(_world), Is.EqualTo(fingerprint));
            Assert.That(_bake.IsStale(_world), Is.False);
            Assert.That(_bake.IsUsable, Is.True);
        }

        [Test]
        public void TrackedPolicyRegistryMismatchBlocksBakeWithoutStalingPreviousGeometry()
        {
            _registry.Add("Walkable");
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            TraversalPolicyAsset policy = Create<TraversalPolicyAsset>();
            policy.SetRegistry(_registry);
            _world.AddPolicy(policy);
            Assert.That(NavigationBaker.Bake(_world, _bake).Succeeded, Is.True);

            SemanticRegistryAsset clone = Create<SemanticRegistryAsset>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_registry), clone);
            Assert.That(clone.SchemaFingerprint, Is.EqualTo(_registry.SchemaFingerprint));
            policy.SetRegistry(clone);
            NavigationBakeResult failed = NavigationBaker.Bake(_world, _bake);

            Assert.That(failed.Succeeded, Is.False);
            AssertIssue(failed, NavigationValidationCode.InvalidPolicy);
            Assert.That(_bake.IsStale(_world), Is.False);
            Assert.That(_bake.IsUsable, Is.True);
        }

        [Test]
        public void BakeUsesRegistryWidthIncludingTombstonedSlots()
        {
            SemanticId last = default;
            for (int slot = 0; slot <= 128; slot++)
            {
                SemanticId id = _registry.Add($"Semantic {slot}");
                if (slot == 64)
                {
                    _registry.Delete(id);
                }

                last = id;
            }

            NavigationAreaAsset area = AddArea();
            NavigationPolygonRecord polygon = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            Assert.That(_registry.TryGet(last, out SemanticDefinition definition), Is.True);
            polygon.RawSemantics.Set(definition.Slot);

            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(_bake.SemanticWordCount, Is.EqualTo(3));
            Assert.That(
                _bake.SemanticWords[_bake.Polygons[0].SemanticOffset + 2] & 1UL,
                Is.EqualTo(1UL));
        }

        private static IEnumerable<TestCaseData> InvalidPolygonCases()
        {
            yield return new TestCaseData(
                    new[] { Vector3.zero, Vector3.forward },
                    NavigationValidationCode.TooFewVertices)
                .SetName("Too_few_vertices");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(1f, 0f, 0f),
                        new Vector3(1f, 0f, 1f),
                        new Vector3(0f, 0f, 1f)
                    },
                    NavigationValidationCode.InvalidWinding)
                .SetName("Clockwise_winding");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 1f),
                        new Vector3(1f, 0f, 1f),
                        new Vector3(1f, 0f, 1f)
                    },
                    NavigationValidationCode.DuplicateVertex)
                .SetName("Duplicate_vertex");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 2f),
                        new Vector3(1f, 0f, 1f),
                        new Vector3(2f, 0f, 2f),
                        new Vector3(2f, 0f, 0f)
                    },
                    NavigationValidationCode.ConcavePolygon)
                .SetName("Concave_polygon");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 2f),
                        new Vector3(2f, 0.1f, 2f),
                        new Vector3(2f, 0f, 0f)
                    },
                    NavigationValidationCode.NonPlanarPolygon)
                .SetName("Non_planar_polygon");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 3f),
                        new Vector3(3f, 0f, 0f),
                        new Vector3(2f, 0f, 2f)
                    },
                    NavigationValidationCode.SelfIntersection)
                .SetName("Self_intersecting_polygon");
            yield return new TestCaseData(
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(1f, 0f, 0f),
                        new Vector3(2f, 0f, 0f)
                    },
                    NavigationValidationCode.DegeneratePolygon)
                .SetName("Degenerate_polygon");
        }

        private NavigationAreaAsset AddAdjacentPair(
            float gap,
            out NavigationPolygonRecord first,
            out NavigationPolygonRecord second)
        {
            NavigationAreaAsset area = AddArea();
            first = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            second = area.AddPolygon(Rectangle(1f + gap, 0f, 2f + gap, 1f));
            return area;
        }

        private NavigationAreaAsset AddArea()
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            _world.AddArea(area);
            return area;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static Vector3[] Rectangle(float minimumX, float minimumZ, float maximumX, float maximumZ)
        {
            return new[]
            {
                new Vector3(minimumX, 0f, minimumZ),
                new Vector3(minimumX, 0f, maximumZ),
                new Vector3(maximumX, 0f, maximumZ),
                new Vector3(maximumX, 0f, minimumZ)
            };
        }

        private static PolygonEdgeReference Edge(NavigationPolygonRecord polygon, int edgeIndex)
        {
            return new PolygonEdgeReference(polygon.Id, polygon.Vertices[edgeIndex].OutgoingEdgeId);
        }

        private static PortalEntrySpan Span(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            int edgeIndex)
        {
            NavigationVertexRecord start = polygon.Vertices[edgeIndex];
            NavigationVertexRecord end = polygon.Vertices[(edgeIndex + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                start.OutgoingEdgeId,
                start.Position,
                end.Position);
        }

        private static void AssertIssue(NavigationBakeResult result, NavigationValidationCode code)
        {
            Assert.That(result.Issues, Has.Some.Property("Code").EqualTo(code), FormatIssues(result));
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                messages.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, messages);
        }
    }
}
