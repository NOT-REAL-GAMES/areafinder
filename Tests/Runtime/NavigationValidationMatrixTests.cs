using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class NavigationValidationMatrixTests
    {
        private const float BoundaryTolerance = 0.125f;

        private static readonly NavigationValidationCode[] CoveredCodes =
        {
            NavigationValidationCode.MissingRegistry,
            NavigationValidationCode.InvalidSettings,
            NavigationValidationCode.NullArea,
            NavigationValidationCode.InvalidAreaId,
            NavigationValidationCode.DuplicateAreaId,
            NavigationValidationCode.InvalidAreaFrame,
            NavigationValidationCode.NullPolygon,
            NavigationValidationCode.InvalidPolygonId,
            NavigationValidationCode.DuplicatePolygonId,
            NavigationValidationCode.TooFewVertices,
            NavigationValidationCode.InvalidVertexId,
            NavigationValidationCode.DuplicateVertexId,
            NavigationValidationCode.InvalidEdgeId,
            NavigationValidationCode.DuplicateEdgeId,
            NavigationValidationCode.NonFiniteVertex,
            NavigationValidationCode.DuplicateVertex,
            NavigationValidationCode.ZeroLengthEdge,
            NavigationValidationCode.DegeneratePolygon,
            NavigationValidationCode.NonPlanarPolygon,
            NavigationValidationCode.InvalidWinding,
            NavigationValidationCode.ConcavePolygon,
            NavigationValidationCode.SelfIntersection,
            NavigationValidationCode.SemanticSlotOutOfRange,
            NavigationValidationCode.TombstonedSemantic,
            NavigationValidationCode.InvalidAdjacencyOverride,
            NavigationValidationCode.DuplicateAdjacencyOverride,
            NavigationValidationCode.InvalidForcedConnection,
            NavigationValidationCode.OrphanPolygon,
            NavigationValidationCode.UnreachableIsland,
            NavigationValidationCode.NullPortal,
            NavigationValidationCode.InvalidPortalId,
            NavigationValidationCode.DuplicatePortalId,
            NavigationValidationCode.InvalidPortalArea,
            NavigationValidationCode.InvalidPortalPolygon,
            NavigationValidationCode.InvalidPortalEdge,
            NavigationValidationCode.InvalidPortalSpan,
            NavigationValidationCode.InvalidPortalCost,
            NavigationValidationCode.InvalidPortalTransform,
            NavigationValidationCode.NullPolicy,
            NavigationValidationCode.InvalidPolicy,
            NavigationValidationCode.DuplicatePolicyId
        };

        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private SemanticRegistryAsset _registry;
        private NavigationWorldAsset _world;

        [SetUp]
        public void SetUp()
        {
            _registry = Create<SemanticRegistryAsset>();
            _registry.name = "Validation Registry";
            _world = Create<NavigationWorldAsset>();
            _world.name = "Validation World";
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
        public void MatrixListsEveryDeclaredValidationCodeExactlyOnce()
        {
            Assert.That(CoveredCodes, Is.Unique);
            Assert.That(CoveredCodes,
                Is.EqualTo((NavigationValidationCode[])Enum.GetValues(typeof(NavigationValidationCode))));
        }

        [TestCase(NavigationValidationCode.MissingRegistry)]
        [TestCase(NavigationValidationCode.InvalidSettings)]
        [TestCase(NavigationValidationCode.NullArea)]
        [TestCase(NavigationValidationCode.InvalidAreaId)]
        [TestCase(NavigationValidationCode.DuplicateAreaId)]
        [TestCase(NavigationValidationCode.InvalidAreaFrame)]
        [TestCase(NavigationValidationCode.NullPolygon)]
        public void WorldAndAreaErrorsExposeTheirContract(NavigationValidationCode code)
        {
            NavigationValidationTargetKind targetKind;
            string targetId;
            switch (code)
            {
                case NavigationValidationCode.MissingRegistry:
                    _world.SetSemanticRegistry(null);
                    targetKind = NavigationValidationTargetKind.World;
                    targetId = _world.name;
                    break;
                case NavigationValidationCode.InvalidSettings:
                    SetPrivate(_world, "_inferenceSettings", new AdjacencyInferenceSettings(
                        float.NaN, 0.01f, 0.05f, 15f, 0.005f, 0.5f));
                    targetKind = NavigationValidationTargetKind.World;
                    targetId = _world.name;
                    break;
                case NavigationValidationCode.NullArea:
                    ((List<NavigationAreaAsset>)_world.Areas).Add(null);
                    targetKind = NavigationValidationTargetKind.World;
                    targetId = _world.name;
                    break;
                case NavigationValidationCode.InvalidAreaId:
                {
                    NavigationAreaAsset area = AddArea();
                    SetPrivate(area, "_id", default(AreaId));
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = default(AreaId).ToString();
                    break;
                }
                case NavigationValidationCode.DuplicateAreaId:
                {
                    NavigationAreaAsset first = AddArea();
                    NavigationAreaAsset second = AddArea();
                    SetPrivate(second, "_id", first.Id);
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = first.Id.ToString();
                    break;
                }
                case NavigationValidationCode.InvalidAreaFrame:
                {
                    NavigationAreaAsset area = AddArea();
                    area.SetFrame(default);
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = area.Id.ToString();
                    break;
                }
                case NavigationValidationCode.NullPolygon:
                {
                    NavigationAreaAsset area = AddArea();
                    ((List<NavigationPolygonRecord>)area.Polygons).Add(null);
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = area.Id.ToString();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            AssertError(NavigationBaker.Validate(_world), code, targetKind, targetId);
        }

        [TestCase(NavigationValidationCode.InvalidPolygonId)]
        [TestCase(NavigationValidationCode.DuplicatePolygonId)]
        [TestCase(NavigationValidationCode.TooFewVertices)]
        [TestCase(NavigationValidationCode.InvalidVertexId)]
        [TestCase(NavigationValidationCode.DuplicateVertexId)]
        [TestCase(NavigationValidationCode.InvalidEdgeId)]
        [TestCase(NavigationValidationCode.DuplicateEdgeId)]
        [TestCase(NavigationValidationCode.NonFiniteVertex)]
        [TestCase(NavigationValidationCode.DuplicateVertex)]
        [TestCase(NavigationValidationCode.ZeroLengthEdge)]
        [TestCase(NavigationValidationCode.DegeneratePolygon)]
        [TestCase(NavigationValidationCode.NonPlanarPolygon)]
        [TestCase(NavigationValidationCode.InvalidWinding)]
        [TestCase(NavigationValidationCode.ConcavePolygon)]
        [TestCase(NavigationValidationCode.SelfIntersection)]
        public void PolygonErrorsExposeTheirContract(NavigationValidationCode code)
        {
            NavigationAreaAsset area = AddArea();
            NavigationPolygonRecord polygon;
            switch (code)
            {
                case NavigationValidationCode.TooFewVertices:
                    polygon = area.AddPolygon(new[] { Vector3.zero, Vector3.forward });
                    break;
                case NavigationValidationCode.DegeneratePolygon:
                    polygon = area.AddPolygon(new[]
                    {
                        Vector3.zero,
                        Vector3.forward,
                        Vector3.forward * 2f
                    });
                    break;
                case NavigationValidationCode.NonPlanarPolygon:
                    polygon = area.AddPolygon(Saddle(0.25f));
                    break;
                case NavigationValidationCode.InvalidWinding:
                    polygon = area.AddPolygon(new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(1f, 0f, 0f),
                        new Vector3(1f, 0f, 1f),
                        new Vector3(0f, 0f, 1f)
                    });
                    break;
                case NavigationValidationCode.ConcavePolygon:
                    polygon = area.AddPolygon(new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 2f),
                        new Vector3(1f, 0f, 1f),
                        new Vector3(2f, 0f, 2f),
                        new Vector3(2f, 0f, 0f)
                    });
                    break;
                case NavigationValidationCode.SelfIntersection:
                    polygon = area.AddPolygon(new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0f, 0f, 3f),
                        new Vector3(3f, 0f, 0f),
                        new Vector3(2f, 0f, 2f)
                    });
                    break;
                default:
                    polygon = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
                    break;
            }

            NavigationValidationTargetKind targetKind;
            string targetId;
            switch (code)
            {
                case NavigationValidationCode.InvalidPolygonId:
                    SetPrivate(polygon, "_id", default(PolygonId));
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = default(PolygonId).ToString();
                    break;
                case NavigationValidationCode.DuplicatePolygonId:
                {
                    NavigationPolygonRecord duplicate = area.AddPolygon(Rectangle(2f, 0f, 3f, 1f));
                    SetPrivate(duplicate, "_id", polygon.Id);
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = polygon.Id.ToString();
                    break;
                }
                case NavigationValidationCode.TooFewVertices:
                case NavigationValidationCode.DegeneratePolygon:
                case NavigationValidationCode.NonPlanarPolygon:
                case NavigationValidationCode.InvalidWinding:
                case NavigationValidationCode.ConcavePolygon:
                case NavigationValidationCode.SelfIntersection:
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = polygon.Id.ToString();
                    break;
                case NavigationValidationCode.InvalidVertexId:
                    SetPrivate(polygon.Vertices[0], "_id", default(VertexId));
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = polygon.Id.ToString();
                    break;
                case NavigationValidationCode.DuplicateVertexId:
                    SetPrivate(polygon.Vertices[1], "_id", polygon.Vertices[0].Id);
                    targetKind = NavigationValidationTargetKind.Vertex;
                    targetId = polygon.Vertices[0].Id.ToString();
                    break;
                case NavigationValidationCode.InvalidEdgeId:
                    SetPrivate(polygon.Vertices[0], "_outgoingEdgeId", default(EdgeId));
                    targetKind = NavigationValidationTargetKind.Vertex;
                    targetId = polygon.Vertices[0].Id.ToString();
                    break;
                case NavigationValidationCode.DuplicateEdgeId:
                    SetPrivate(polygon.Vertices[1], "_outgoingEdgeId", polygon.Vertices[0].OutgoingEdgeId);
                    targetKind = NavigationValidationTargetKind.Edge;
                    targetId = polygon.Vertices[0].OutgoingEdgeId.ToString();
                    break;
                case NavigationValidationCode.NonFiniteVertex:
                    polygon.MoveVertex(0, new Vector3(float.NaN, 0f, 0f));
                    targetKind = NavigationValidationTargetKind.Vertex;
                    targetId = polygon.Vertices[0].Id.ToString();
                    break;
                case NavigationValidationCode.DuplicateVertex:
                    polygon.MoveVertex(1, polygon.Vertices[0].Position);
                    targetKind = NavigationValidationTargetKind.Vertex;
                    targetId = polygon.Vertices[1].Id.ToString();
                    break;
                case NavigationValidationCode.ZeroLengthEdge:
                    polygon.MoveVertex(1, polygon.Vertices[0].Position);
                    targetKind = NavigationValidationTargetKind.Edge;
                    targetId = polygon.Vertices[0].OutgoingEdgeId.ToString();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            NavigationBakeResult result = NavigationBaker.Validate(_world);
            NavigationValidationIssue issue = AssertError(result, code, targetKind, targetId,
                code == NavigationValidationCode.ZeroLengthEdge ||
                code == NavigationValidationCode.NonPlanarPolygon);
            if (code == NavigationValidationCode.ZeroLengthEdge)
            {
                Assert.That(issue.MeasuredValue, Is.EqualTo(0d));
                Assert.That(issue.AllowedValue,
                    Is.EqualTo(_world.InferenceSettings.PositionTolerance).Within(1e-7d));
            }
            else if (code == NavigationValidationCode.NonPlanarPolygon)
            {
                Assert.That(issue.MeasuredValue, Is.GreaterThan(issue.AllowedValue));
                Assert.That(issue.AllowedValue,
                    Is.EqualTo(_world.InferenceSettings.PlanarityTolerance).Within(1e-7d));
            }
        }

        [TestCase(NavigationValidationCode.SemanticSlotOutOfRange)]
        [TestCase(NavigationValidationCode.InvalidAdjacencyOverride)]
        [TestCase(NavigationValidationCode.DuplicateAdjacencyOverride)]
        [TestCase(NavigationValidationCode.InvalidForcedConnection)]
        public void SemanticAndAdjacencyErrorsExposeTheirContract(NavigationValidationCode code)
        {
            NavigationValidationTargetKind targetKind;
            string targetId;
            bool hasMeasurements = false;
            switch (code)
            {
                case NavigationValidationCode.SemanticSlotOutOfRange:
                {
                    NavigationAreaAsset area = AddArea();
                    var mask = new SemanticMask();
                    mask.Set(0);
                    area.SetSemantics(mask);
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = area.Id.ToString();
                    break;
                }
                case NavigationValidationCode.InvalidAdjacencyOverride:
                {
                    NavigationAreaAsset area = AddArea();
                    area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
                    area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                        default,
                        default,
                        AdjacencyOverrideState.ForcedConnected));
                    targetKind = NavigationValidationTargetKind.Area;
                    targetId = area.Id.ToString();
                    break;
                }
                case NavigationValidationCode.DuplicateAdjacencyOverride:
                {
                    NavigationAreaAsset area = AddSeparatedPair(0f,
                        out NavigationPolygonRecord first,
                        out NavigationPolygonRecord second);
                    PolygonEdgeReference firstEdge = Edge(first, 2);
                    PolygonEdgeReference secondEdge = Edge(second, 0);
                    area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                        firstEdge,
                        secondEdge,
                        AdjacencyOverrideState.ForcedDisconnected));
                    area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                        firstEdge,
                        secondEdge,
                        AdjacencyOverrideState.ForcedConnected));
                    targetKind = NavigationValidationTargetKind.Edge;
                    targetId = firstEdge.EdgeId.ToString();
                    break;
                }
                case NavigationValidationCode.InvalidForcedConnection:
                {
                    NavigationAreaAsset area = AddSeparatedPair(0.75f,
                        out NavigationPolygonRecord first,
                        out NavigationPolygonRecord second);
                    area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                        Edge(first, 2),
                        Edge(second, 0),
                        AdjacencyOverrideState.ForcedConnected));
                    targetKind = NavigationValidationTargetKind.Edge;
                    targetId = FirstSortedEdgeId(first, 2, second, 0).ToString();
                    hasMeasurements = true;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            NavigationValidationIssue issue = AssertError(
                NavigationBaker.Validate(_world), code, targetKind, targetId, hasMeasurements);
            if (code == NavigationValidationCode.InvalidForcedConnection)
            {
                Assert.That(issue.MeasuredValue, Is.GreaterThan(issue.AllowedValue));
                Assert.That(issue.AllowedValue,
                    Is.EqualTo(_world.InferenceSettings.MaximumForcedGap).Within(1e-7d));
            }
        }

        [TestCase(NavigationValidationCode.TombstonedSemantic)]
        [TestCase(NavigationValidationCode.OrphanPolygon)]
        [TestCase(NavigationValidationCode.UnreachableIsland)]
        public void WarningsExposeTheirContractWithoutBlockingBake(NavigationValidationCode code)
        {
            NavigationValidationTargetKind targetKind;
            string targetId;
            switch (code)
            {
                case NavigationValidationCode.TombstonedSemantic:
                {
                    SemanticId semantic = _registry.Add("Retired");
                    Assert.That(_registry.Delete(semantic), Is.True);
                    var mask = new SemanticMask(_registry.SlotCapacity);
                    mask.Set(0);
                    AddArea().SetSemantics(mask);
                    targetKind = NavigationValidationTargetKind.Semantic;
                    targetId = semantic.ToString();
                    break;
                }
                case NavigationValidationCode.OrphanPolygon:
                {
                    NavigationPolygonRecord polygon = AddArea().AddPolygon(Rectangle(0f, 0f, 1f, 1f));
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = polygon.Id.ToString();
                    break;
                }
                case NavigationValidationCode.UnreachableIsland:
                {
                    NavigationAreaAsset area = AddArea();
                    NavigationPolygonRecord first = area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
                    NavigationPolygonRecord second = area.AddPolygon(Rectangle(2f, 0f, 3f, 1f));
                    targetKind = NavigationValidationTargetKind.Polygon;
                    targetId = (first.Id.CompareTo(second.Id) < 0 ? second.Id : first.Id).ToString();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            AssertWarning(NavigationBaker.Validate(_world), code, targetKind, targetId);
        }

        [TestCase(NavigationValidationCode.NullPortal)]
        [TestCase(NavigationValidationCode.InvalidPortalId)]
        [TestCase(NavigationValidationCode.DuplicatePortalId)]
        [TestCase(NavigationValidationCode.InvalidPortalArea)]
        [TestCase(NavigationValidationCode.InvalidPortalPolygon)]
        [TestCase(NavigationValidationCode.InvalidPortalEdge)]
        [TestCase(NavigationValidationCode.InvalidPortalSpan)]
        [TestCase(NavigationValidationCode.InvalidPortalCost)]
        [TestCase(NavigationValidationCode.InvalidPortalTransform)]
        public void PortalErrorsExposeTheirContract(NavigationValidationCode code)
        {
            if (code == NavigationValidationCode.NullPortal)
            {
                ((List<NavigationPortalRecord>)_world.Portals).Add(null);
                AssertError(
                    NavigationBaker.Validate(_world),
                    code,
                    NavigationValidationTargetKind.World,
                    _world.name);
                return;
            }

            NavigationPortalRecord portal = AddPortalFixture(
                out NavigationAreaAsset sourceArea,
                out NavigationPolygonRecord sourcePolygon,
                out _,
                out NavigationPolygonRecord destinationPolygon);
            switch (code)
            {
                case NavigationValidationCode.InvalidPortalId:
                    SetPrivate(portal, "_id", default(PortalId));
                    break;
                case NavigationValidationCode.DuplicatePortalId:
                {
                    NavigationPortalRecord duplicate = _world.AddPortal(
                        portal.Source,
                        portal.Destination,
                        PortalDirection.SourceToDestination,
                        0d,
                        PortalTransform.Identity);
                    SetPrivate(duplicate, "_id", portal.Id);
                    break;
                }
                case NavigationValidationCode.InvalidPortalArea:
                    portal.SetSpans(
                        portal.Source,
                        Span(sourceArea, sourcePolygon, 2));
                    break;
                case NavigationValidationCode.InvalidPortalPolygon:
                    portal.SetSpans(
                        new PortalEntrySpan(
                            portal.Source.AreaId,
                            default,
                            portal.Source.EdgeId,
                            portal.Source.Start,
                            portal.Source.End),
                        portal.Destination);
                    break;
                case NavigationValidationCode.InvalidPortalEdge:
                    portal.SetSpans(
                        new PortalEntrySpan(
                            portal.Source.AreaId,
                            sourcePolygon.Id,
                            default,
                            portal.Source.Start,
                            portal.Source.End),
                        portal.Destination);
                    break;
                case NavigationValidationCode.InvalidPortalSpan:
                    portal.SetSpans(
                        new PortalEntrySpan(
                            portal.Source.AreaId,
                            sourcePolygon.Id,
                            sourcePolygon.Vertices[2].OutgoingEdgeId,
                            new Vector3(4f, 0f, 4f),
                            new Vector3(4f, 0f, 5f)),
                        portal.Destination);
                    break;
                case NavigationValidationCode.InvalidPortalCost:
                    SetPrivate(portal, "_baseCost", -1d);
                    break;
                case NavigationValidationCode.InvalidPortalTransform:
                    portal.SetTransform(new PortalTransform(
                        new Double3(0.25d, 0d, 0d),
                        Quaternion.identity));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            string targetId = code == NavigationValidationCode.InvalidPortalId
                ? default(PortalId).ToString()
                : portal.Id.ToString();
            bool hasMeasurements = code == NavigationValidationCode.InvalidPortalSpan ||
                                   code == NavigationValidationCode.InvalidPortalTransform;
            NavigationValidationIssue issue = AssertError(
                NavigationBaker.Validate(_world),
                code,
                NavigationValidationTargetKind.Portal,
                targetId,
                hasMeasurements);

            if (code == NavigationValidationCode.InvalidPortalSpan)
            {
                Assert.That(issue.MeasuredValue, Is.GreaterThan(issue.AllowedValue));
                Assert.That(issue.AllowedValue, Is.EqualTo(Math.Max(
                    _world.InferenceSettings.PositionTolerance,
                    _world.InferenceSettings.HeightTolerance)).Within(1e-7d));
            }
            else if (code == NavigationValidationCode.InvalidPortalTransform)
            {
                Assert.That(issue.MeasuredValue, Is.EqualTo(0.25d).Within(1e-6d));
                Assert.That(issue.AllowedValue, Is.EqualTo(Math.Max(
                    _world.InferenceSettings.PositionTolerance,
                    _world.InferenceSettings.HeightTolerance)).Within(1e-7d));
            }
        }

        [TestCase(NavigationValidationCode.NullPolicy)]
        [TestCase(NavigationValidationCode.InvalidPolicy)]
        [TestCase(NavigationValidationCode.DuplicatePolicyId)]
        public void PolicyErrorsExposeTheirContract(NavigationValidationCode code)
        {
            NavigationValidationTargetKind targetKind;
            string targetId;
            switch (code)
            {
                case NavigationValidationCode.NullPolicy:
                    ((List<TraversalPolicyAsset>)_world.Policies).Add(null);
                    targetKind = NavigationValidationTargetKind.World;
                    targetId = _world.name;
                    break;
                case NavigationValidationCode.InvalidPolicy:
                {
                    TraversalPolicyAsset policy = Create<TraversalPolicyAsset>();
                    _world.AddPolicy(policy);
                    targetKind = NavigationValidationTargetKind.Policy;
                    targetId = policy.Id.ToString();
                    break;
                }
                case NavigationValidationCode.DuplicatePolicyId:
                {
                    TraversalPolicyAsset first = Create<TraversalPolicyAsset>();
                    TraversalPolicyAsset second = Create<TraversalPolicyAsset>();
                    first.SetRegistry(_registry);
                    second.SetRegistry(_registry);
                    SetPrivate(second, "_id", first.Id);
                    _world.AddPolicy(first);
                    _world.AddPolicy(second);
                    targetKind = NavigationValidationTargetKind.Policy;
                    targetId = first.Id.ToString();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            AssertError(NavigationBaker.Validate(_world), code, targetKind, targetId);
        }

        [TestCase(0.124f, true)]
        [TestCase(BoundaryTolerance, true)]
        [TestCase(0.126f, false)]
        public void PositionToleranceIncludesItsBoundary(float gap, bool adjacent)
        {
            SetSettings(position: BoundaryTolerance);
            AddSeparatedPair(gap, out _, out _);

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(CountCompiledAdjacencies(), Is.EqualTo(adjacent ? 2 : 0));
        }

        [TestCase(0.124f, true)]
        [TestCase(BoundaryTolerance, true)]
        [TestCase(0.126f, false)]
        public void DuplicateAndZeroLengthChecksIncludePositionBoundary(float edgeLength, bool rejected)
        {
            SetSettings(position: BoundaryTolerance);
            NavigationPolygonRecord polygon = AddArea().AddPolygon(
                Rectangle(0f, 0f, edgeLength, 1f));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(HasIssue(result, NavigationValidationCode.DuplicateVertex), Is.EqualTo(rejected),
                FormatIssues(result));
            Assert.That(HasIssue(result, NavigationValidationCode.ZeroLengthEdge), Is.EqualTo(rejected),
                FormatIssues(result));
            Assert.That(result.Succeeded, Is.EqualTo(!rejected), FormatIssues(result));
            if (rejected)
            {
                NavigationValidationIssue issue = FindIssue(result, NavigationValidationCode.ZeroLengthEdge);
                Assert.That(issue.TargetKind, Is.EqualTo(NavigationValidationTargetKind.Edge));
                Assert.That(issue.TargetId, Is.EqualTo(polygon.Vertices[1].OutgoingEdgeId.ToString()));
                Assert.That(issue.MeasuredValue, Is.EqualTo(edgeLength).Within(1e-7d));
                Assert.That(issue.AllowedValue, Is.EqualTo(BoundaryTolerance).Within(1e-7d));
            }
        }

        [TestCase(0.249f, false)]
        [TestCase(0.25f, true)]
        [TestCase(0.251f, true)]
        public void MinimumOverlapIncludesItsBoundary(float overlap, bool adjacent)
        {
            SetSettings(minimumOverlap: 0.25f);
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(-1f, 0f, 0f, 1f));
            area.AddPolygon(Rectangle(0f, 0f, 1f, overlap));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(CountCompiledAdjacencies(), Is.EqualTo(adjacent ? 2 : 0));
        }

        [TestCase(0.249f, false)]
        [TestCase(0.25f, true)]
        [TestCase(0.251f, true)]
        public void PortalMinimumOverlapIncludesItsBoundary(float spanLength, bool valid)
        {
            SetSettings(minimumOverlap: 0.25f);
            NavigationPortalRecord portal = AddPortalFixture(
                out _,
                out _,
                out _,
                out _);
            portal.SetSpans(
                Subspan(portal.Source, spanLength),
                Subspan(portal.Destination, spanLength));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(HasIssue(result, NavigationValidationCode.InvalidPortalSpan), Is.EqualTo(!valid),
                FormatIssues(result));
            Assert.That(result.Succeeded, Is.EqualTo(valid), FormatIssues(result));
        }

        [TestCase(0.124f, true)]
        [TestCase(BoundaryTolerance, true)]
        [TestCase(0.126f, false)]
        public void HeightToleranceIncludesItsBoundary(float height, bool adjacent)
        {
            SetSettings(height: BoundaryTolerance);
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(-1f, 0f, 0f, 1f));
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f, height));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(CountCompiledAdjacencies(), Is.EqualTo(adjacent ? 2 : 0));
        }

        [TestCase(0.998255f, true)]
        [TestCase(1f, true)]
        [TestCase(1.001747f, false)]
        public void NormalAngleToleranceIncludesItsBoundary(float rise, bool adjacent)
        {
            SetSettings(normalAngle: 45f);
            NavigationAreaAsset area = AddArea();
            area.AddPolygon(Rectangle(-1f, 0f, 0f, 1f));
            area.AddPolygon(new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, rise, 1f),
                new Vector3(1f, rise, 0f)
            });

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(CountCompiledAdjacencies(), Is.EqualTo(adjacent ? 2 : 0));
        }

        [TestCase(0.124f, false)]
        [TestCase(BoundaryTolerance, false)]
        [TestCase(0.126f, true)]
        public void PlanarityToleranceExcludesOnlyValuesAboveItsBoundary(float error, bool rejected)
        {
            SetSettings(planarity: BoundaryTolerance);
            AddArea().AddPolygon(Saddle(error));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(HasIssue(result, NavigationValidationCode.NonPlanarPolygon), Is.EqualTo(rejected),
                FormatIssues(result));
            Assert.That(result.Succeeded, Is.EqualTo(!rejected), FormatIssues(result));
            if (rejected)
            {
                NavigationValidationIssue issue = FindIssue(result, NavigationValidationCode.NonPlanarPolygon);
                Assert.That(issue.MeasuredValue, Is.EqualTo(error).Within(1e-7d));
                Assert.That(issue.AllowedValue, Is.EqualTo(BoundaryTolerance).Within(1e-7d));
            }
        }

        [TestCase(0.249f, true)]
        [TestCase(0.25f, true)]
        [TestCase(0.251f, false)]
        public void MaximumForcedGapIncludesItsBoundary(float gap, bool adjacent)
        {
            SetSettings(maximumForcedGap: 0.25f);
            NavigationAreaAsset area = AddSeparatedPair(
                gap,
                out NavigationPolygonRecord first,
                out NavigationPolygonRecord second);
            area.AddAdjacencyOverride(new AdjacencyOverrideRecord(
                Edge(first, 2),
                Edge(second, 0),
                AdjacencyOverrideState.ForcedConnected));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(HasIssue(result, NavigationValidationCode.InvalidForcedConnection),
                Is.EqualTo(!adjacent), FormatIssues(result));
            Assert.That(result.Succeeded, Is.EqualTo(adjacent), FormatIssues(result));
            if (adjacent)
            {
                Assert.That(CountCompiledAdjacencies(), Is.EqualTo(2));
            }
        }

        [TestCase(0.124d, true)]
        [TestCase(0.125d, true)]
        [TestCase(0.126d, false)]
        public void PortalTransformToleranceIncludesItsBoundary(double translation, bool valid)
        {
            SetSettings(position: BoundaryTolerance, height: BoundaryTolerance);
            NavigationPortalRecord portal = AddPortalFixture(out _, out _, out _, out _);
            portal.SetTransform(new PortalTransform(
                new Double3(translation, 0d, 0d),
                Quaternion.identity));

            NavigationBakeResult result = NavigationBaker.Validate(_world);

            Assert.That(HasIssue(result, NavigationValidationCode.InvalidPortalTransform), Is.EqualTo(!valid),
                FormatIssues(result));
            Assert.That(result.Succeeded, Is.EqualTo(valid), FormatIssues(result));
            if (!valid)
            {
                NavigationValidationIssue issue = FindIssue(
                    result,
                    NavigationValidationCode.InvalidPortalTransform);
                Assert.That(issue.MeasuredValue, Is.EqualTo(translation).Within(1e-7d));
                Assert.That(issue.AllowedValue, Is.EqualTo(BoundaryTolerance).Within(1e-7d));
            }
        }

        private NavigationValidationIssue AssertError(
            NavigationBakeResult result,
            NavigationValidationCode code,
            NavigationValidationTargetKind targetKind,
            string targetId,
            bool hasMeasurements = false)
        {
            NavigationValidationIssue issue = FindIssue(result, code);
            Assert.That(result.Succeeded, Is.False, FormatIssues(result));
            Assert.That(issue.Severity, Is.EqualTo(NavigationValidationSeverity.Error));
            Assert.That(issue.TargetKind, Is.EqualTo(targetKind));
            Assert.That(issue.TargetId, Is.EqualTo(targetId));
            Assert.That(issue.Message, Is.Not.Empty);
            if (!hasMeasurements)
            {
                Assert.That(issue.MeasuredValue, Is.EqualTo(0d));
                Assert.That(issue.AllowedValue, Is.EqualTo(0d));
            }

            return issue;
        }

        private static void AssertWarning(
            NavigationBakeResult result,
            NavigationValidationCode code,
            NavigationValidationTargetKind targetKind,
            string targetId)
        {
            NavigationValidationIssue issue = FindIssue(result, code);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(issue.Severity, Is.EqualTo(NavigationValidationSeverity.Warning));
            Assert.That(issue.TargetKind, Is.EqualTo(targetKind));
            Assert.That(issue.TargetId, Is.EqualTo(targetId));
            Assert.That(issue.Message, Is.Not.Empty);
            Assert.That(issue.MeasuredValue, Is.EqualTo(0d));
            Assert.That(issue.AllowedValue, Is.EqualTo(0d));
        }

        private NavigationPortalRecord AddPortalFixture(
            out NavigationAreaAsset sourceArea,
            out NavigationPolygonRecord sourcePolygon,
            out NavigationAreaAsset destinationArea,
            out NavigationPolygonRecord destinationPolygon)
        {
            sourceArea = AddArea();
            sourcePolygon = sourceArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            destinationArea = AddArea();
            destinationPolygon = destinationArea.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            return _world.AddPortal(
                Span(sourceArea, sourcePolygon, 2),
                Span(destinationArea, destinationPolygon, 2),
                PortalDirection.SourceToDestination,
                0d,
                PortalTransform.Identity);
        }

        private NavigationAreaAsset AddSeparatedPair(
            float gap,
            out NavigationPolygonRecord first,
            out NavigationPolygonRecord second)
        {
            NavigationAreaAsset area = AddArea();
            first = area.AddPolygon(Rectangle(-1f, 0f, 0f, 1f));
            second = area.AddPolygon(Rectangle(gap, 0f, gap + 1f, 1f));
            return area;
        }

        private NavigationAreaAsset AddArea()
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            _world.AddArea(area);
            return area;
        }

        private void SetSettings(
            float position = 0.01f,
            float minimumOverlap = 0.01f,
            float height = 0.05f,
            float normalAngle = 15f,
            float planarity = 0.005f,
            float maximumForcedGap = 0.5f)
        {
            _world.SetInferenceSettings(new AdjacencyInferenceSettings(
                position,
                minimumOverlap,
                height,
                normalAngle,
                planarity,
                maximumForcedGap));
        }

        private int CountCompiledAdjacencies()
        {
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult result = NavigationBaker.Bake(_world, bake);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            return bake.Adjacencies.Count;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static NavigationValidationIssue FindIssue(
            NavigationBakeResult result,
            NavigationValidationCode code)
        {
            for (int index = 0; index < result.Issues.Count; index++)
            {
                if (result.Issues[index].Code == code)
                {
                    return result.Issues[index];
                }
            }

            Assert.Fail($"Expected {code}.{Environment.NewLine}{FormatIssues(result)}");
            return default;
        }

        private static bool HasIssue(NavigationBakeResult result, NavigationValidationCode code)
        {
            for (int index = 0; index < result.Issues.Count; index++)
            {
                if (result.Issues[index].Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test corruption field {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }

        private static Vector3[] Rectangle(
            float minimumX,
            float minimumZ,
            float maximumX,
            float maximumZ,
            float height = 0f)
        {
            return new[]
            {
                new Vector3(minimumX, height, minimumZ),
                new Vector3(minimumX, height, maximumZ),
                new Vector3(maximumX, height, maximumZ),
                new Vector3(maximumX, height, minimumZ)
            };
        }

        private static Vector3[] Saddle(float error)
        {
            return new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, error, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, error, 0f)
            };
        }

        private static PolygonEdgeReference Edge(NavigationPolygonRecord polygon, int edgeIndex)
        {
            return new PolygonEdgeReference(polygon.Id, polygon.Vertices[edgeIndex].OutgoingEdgeId);
        }

        private static EdgeId FirstSortedEdgeId(
            NavigationPolygonRecord first,
            int firstEdge,
            NavigationPolygonRecord second,
            int secondEdge)
        {
            return first.Id.CompareTo(second.Id) < 0
                ? first.Vertices[firstEdge].OutgoingEdgeId
                : second.Vertices[secondEdge].OutgoingEdgeId;
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

        private static PortalEntrySpan Subspan(PortalEntrySpan span, float length)
        {
            Vector3 direction = (span.End - span.Start).normalized;
            return new PortalEntrySpan(
                span.AreaId,
                span.PolygonId,
                span.EdgeId,
                span.Start,
                span.Start + (direction * length));
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                messages.Add($"{issue.Severity}: {issue.Code}: {issue.TargetKind}/{issue.TargetId}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, messages);
        }
    }
}
