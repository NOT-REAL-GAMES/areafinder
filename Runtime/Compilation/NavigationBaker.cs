using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    public sealed class NavigationBakeResult
    {
        internal NavigationBakeResult(List<NavigationValidationIssue> issues, ulong sourceFingerprint)
        {
            Issues = issues.AsReadOnly();
            SourceFingerprint = sourceFingerprint;
            bool succeeded = true;
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].Severity == NavigationValidationSeverity.Error)
                {
                    succeeded = false;
                    break;
                }
            }

            Succeeded = succeeded;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<NavigationValidationIssue> Issues { get; }
        public ulong SourceFingerprint { get; }
    }

    public static class NavigationBaker
    {
        private sealed class BuildData
        {
            internal readonly List<NavigationValidationIssue> Issues = new List<NavigationValidationIssue>();
            internal readonly List<CompiledAreaRecord> Areas = new List<CompiledAreaRecord>();
            internal readonly List<CompiledPolygonRecord> Polygons = new List<CompiledPolygonRecord>();
            internal readonly List<CompiledVertexRecord> Vertices = new List<CompiledVertexRecord>();
            internal readonly List<CompiledAdjacencyRecord> Adjacencies = new List<CompiledAdjacencyRecord>();
            internal readonly List<CompiledPortalRecord> Portals = new List<CompiledPortalRecord>();
            internal readonly List<ulong> SemanticWords = new List<ulong>();
            internal readonly Dictionary<AreaId, int> AreaIndices = new Dictionary<AreaId, int>();
            internal readonly Dictionary<PolygonId, PolygonBuild> PolygonById = new Dictionary<PolygonId, PolygonBuild>();
            internal readonly Dictionary<PolygonEdgeReference, EdgeBuild> EdgeByReference =
                new Dictionary<PolygonEdgeReference, EdgeBuild>();
            internal readonly List<List<EdgeBuild>> AreaEdges = new List<List<EdgeBuild>>();
            internal readonly HashSet<PolygonId> PortalPolygons = new HashSet<PolygonId>();
            internal int SemanticWordCount;
            internal ulong Fingerprint;
        }

        private sealed class PolygonBuild
        {
            internal NavigationPolygonRecord Source;
            internal int AreaIndex;
            internal int DenseIndex;
            internal Vector3 Normal;
            internal Vector3 Centroid;
        }

        private sealed class EdgeBuild
        {
            internal PolygonEdgeReference Reference;
            internal int AreaIndex;
            internal int PolygonIndex;
            internal Vector3 Start;
            internal Vector3 End;
            internal Vector3 Normal;
        }

        private readonly struct AreaPolygonKey : IEquatable<AreaPolygonKey>
        {
            internal AreaPolygonKey(AreaId areaId, PolygonId polygonId)
            {
                AreaId = areaId;
                PolygonId = polygonId;
            }

            internal AreaId AreaId { get; }
            internal PolygonId PolygonId { get; }

            public bool Equals(AreaPolygonKey other) => AreaId == other.AreaId && PolygonId == other.PolygonId;
            public override bool Equals(object obj) => obj is AreaPolygonKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (AreaId.GetHashCode() * 397) ^ PolygonId.GetHashCode();
                }
            }
        }

        private readonly struct EdgePairKey : IEquatable<EdgePairKey>
        {
            internal EdgePairKey(PolygonEdgeReference first, PolygonEdgeReference second)
            {
                if (Compare(first, second) <= 0)
                {
                    First = first;
                    Second = second;
                }
                else
                {
                    First = second;
                    Second = first;
                }
            }

            internal PolygonEdgeReference First { get; }
            internal PolygonEdgeReference Second { get; }

            public bool Equals(EdgePairKey other) => First.Equals(other.First) && Second.Equals(other.Second);
            public override bool Equals(object obj) => obj is EdgePairKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (First.GetHashCode() * 397) ^ Second.GetHashCode();
                }
            }

            private static int Compare(PolygonEdgeReference left, PolygonEdgeReference right)
            {
                int polygon = left.PolygonId.CompareTo(right.PolygonId);
                return polygon != 0 ? polygon : left.EdgeId.CompareTo(right.EdgeId);
            }
        }

        public static NavigationBakeResult Validate(NavigationWorldAsset source)
        {
            BuildData data = Build(source);
            return new NavigationBakeResult(data.Issues, data.Fingerprint);
        }

        public static NavigationBakeResult Bake(NavigationWorldAsset source, NavigationBakeAsset destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            BuildData data = Build(source);
            var result = new NavigationBakeResult(data.Issues, data.Fingerprint);
            if (!result.Succeeded)
            {
                return result;
            }

            destination.SetData(
                source,
                data.Fingerprint,
                source.SemanticRegistry.SchemaFingerprint,
                data.SemanticWordCount,
                data.Areas.ToArray(),
                data.Polygons.ToArray(),
                data.Vertices.ToArray(),
                data.Adjacencies.ToArray(),
                data.Portals.ToArray(),
                data.SemanticWords.ToArray());
            return result;
        }

        public static ulong ComputeSourceFingerprint(NavigationWorldAsset source)
        {
            if (source == null)
            {
                return 0UL;
            }

            FingerprintBuilder fingerprint = FingerprintBuilder.Create();
            SemanticRegistryAsset registry = source.SemanticRegistry;
            fingerprint.Add(registry != null ? registry.SchemaFingerprint : 0UL);
            AddSettings(ref fingerprint, source.RawInferenceSettings);

            var areas = new List<NavigationAreaAsset>();
            fingerprint.Add(source.Areas.Count);
            for (int index = 0; index < source.Areas.Count; index++)
            {
                if (source.Areas[index] != null)
                {
                    areas.Add(source.Areas[index]);
                }
            }

            areas.Sort((left, right) => left.Id.CompareTo(right.Id));
            fingerprint.Add(areas.Count);
            for (int areaIndex = 0; areaIndex < areas.Count; areaIndex++)
            {
                NavigationAreaAsset area = areas[areaIndex];
                AddId(ref fingerprint, area.Id.High, area.Id.Low);
                AddFrame(ref fingerprint, area.Frame);
                area.RawSemantics.AddToFingerprint(ref fingerprint);
                area.RawRequiredCapabilities.AddToFingerprint(ref fingerprint);

                var polygons = new List<NavigationPolygonRecord>();
                fingerprint.Add(area.Polygons.Count);
                for (int index = 0; index < area.Polygons.Count; index++)
                {
                    if (area.Polygons[index] != null)
                    {
                        polygons.Add(area.Polygons[index]);
                    }
                }

                polygons.Sort((left, right) => left.Id.CompareTo(right.Id));
                fingerprint.Add(polygons.Count);
                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    NavigationPolygonRecord polygon = polygons[polygonIndex];
                    AddId(ref fingerprint, polygon.Id.High, polygon.Id.Low);
                    fingerprint.Add(polygon.Enabled);
                    polygon.RawSemantics.AddToFingerprint(ref fingerprint);
                    polygon.RawRequiredCapabilities.AddToFingerprint(ref fingerprint);
                    fingerprint.Add(polygon.Vertices.Count);
                    for (int vertexIndex = 0; vertexIndex < polygon.Vertices.Count; vertexIndex++)
                    {
                        NavigationVertexRecord vertex = polygon.Vertices[vertexIndex];
                        if (vertex == null)
                        {
                            fingerprint.Add(-1);
                            continue;
                        }

                        AddId(ref fingerprint, vertex.Id.High, vertex.Id.Low);
                        AddId(ref fingerprint, vertex.OutgoingEdgeId.High, vertex.OutgoingEdgeId.Low);
                        AddVector(ref fingerprint, vertex.Position);
                    }
                }

                var overrides = new List<AdjacencyOverrideRecord>();
                fingerprint.Add(area.AdjacencyOverrides.Count);
                for (int index = 0; index < area.AdjacencyOverrides.Count; index++)
                {
                    if (area.AdjacencyOverrides[index] != null)
                    {
                        overrides.Add(area.AdjacencyOverrides[index]);
                    }
                }

                overrides.Sort((left, right) => CompareOverride(left, right));
                fingerprint.Add(overrides.Count);
                for (int index = 0; index < overrides.Count; index++)
                {
                    AddEdgeReference(ref fingerprint, overrides[index].First);
                    AddEdgeReference(ref fingerprint, overrides[index].Second);
                    fingerprint.Add((int)overrides[index].State);
                }
            }

            var portals = new List<NavigationPortalRecord>();
            fingerprint.Add(source.Portals.Count);
            for (int index = 0; index < source.Portals.Count; index++)
            {
                if (source.Portals[index] != null)
                {
                    portals.Add(source.Portals[index]);
                }
            }

            portals.Sort((left, right) => left.Id.CompareTo(right.Id));
            fingerprint.Add(portals.Count);
            for (int index = 0; index < portals.Count; index++)
            {
                NavigationPortalRecord portal = portals[index];
                AddId(ref fingerprint, portal.Id.High, portal.Id.Low);
                AddSpan(ref fingerprint, portal.Source);
                AddSpan(ref fingerprint, portal.Destination);
                fingerprint.Add((int)portal.Direction);
                fingerprint.Add(portal.Enabled);
                portal.RawSemantics.AddToFingerprint(ref fingerprint);
                portal.RawRequiredCapabilities.AddToFingerprint(ref fingerprint);
                fingerprint.Add(portal.BaseCost);
                AddPortalTransform(ref fingerprint, portal.SourceToDestination);
            }

            return fingerprint.Value;
        }

        private static BuildData Build(NavigationWorldAsset source)
        {
            var data = new BuildData();
            if (source == null)
            {
                AddError(data, NavigationValidationCode.MissingRegistry, NavigationValidationTargetKind.World,
                    string.Empty, "A navigation world is required.");
                return data;
            }

            data.Fingerprint = ComputeSourceFingerprint(source);
            SemanticRegistryAsset registry = source.SemanticRegistry;
            if (registry == null)
            {
                AddError(data, NavigationValidationCode.MissingRegistry, NavigationValidationTargetKind.World,
                    source.name, "The world must reference the project's semantic registry.");
                return data;
            }

            AdjacencyInferenceSettings settings = source.RawInferenceSettings;
            if (!settings.IsValid)
            {
                AddError(data, NavigationValidationCode.InvalidSettings, NavigationValidationTargetKind.World,
                    source.name, "Adjacency inference settings are invalid.");
                return data;
            }

            data.SemanticWordCount = registry.RequiredWordCount;
            ValidatePolicies(source, registry, data);
            BuildAreas(source, registry, settings, data);
            Dictionary<EdgePairKey, AdjacencyOverrideState> overrides = BuildOverrides(source, data);
            InferAdjacency(settings, data, overrides);
            BuildPortals(source, registry, settings, data);
            BuildConnectivityWarnings(data);
            ApplyAdjacencyRanges(data);
            return data;
        }

        private static void ValidatePolicies(
            NavigationWorldAsset source,
            SemanticRegistryAsset registry,
            BuildData data)
        {
            var seen = new HashSet<PolicyId>();
            for (int index = 0; index < source.Policies.Count; index++)
            {
                TraversalPolicyAsset policy = source.Policies[index];
                if (policy == null)
                {
                    AddError(data, NavigationValidationCode.NullPolicy,
                        NavigationValidationTargetKind.World, source.name,
                        $"Traversal policy entry {index} is null.");
                    continue;
                }

                if (!policy.Id.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidPolicy,
                        NavigationValidationTargetKind.Policy, policy.Id.ToString(),
                        "The traversal policy has no stable identity.");
                    continue;
                }

                if (!seen.Add(policy.Id))
                {
                    AddError(data, NavigationValidationCode.DuplicatePolicyId,
                        NavigationValidationTargetKind.Policy, policy.Id.ToString(),
                        "Traversal policy identities must be valid and unique within a world.");
                    continue;
                }

                string error = null;
                if (policy.Registry != registry || !policy.TryValidate(out error))
                {
                    AddError(data, NavigationValidationCode.InvalidPolicy,
                        NavigationValidationTargetKind.Policy, policy.Id.ToString(),
                        error ?? "The traversal policy uses a different semantic registry.");
                }
            }
        }

        private static void BuildAreas(
            NavigationWorldAsset source,
            SemanticRegistryAsset registry,
            AdjacencyInferenceSettings settings,
            BuildData data)
        {
            var areas = new List<NavigationAreaAsset>();
            for (int index = 0; index < source.Areas.Count; index++)
            {
                NavigationAreaAsset area = source.Areas[index];
                if (area == null)
                {
                    AddError(data, NavigationValidationCode.NullArea, NavigationValidationTargetKind.World,
                        source.name, $"Area entry {index} is null.");
                }
                else
                {
                    areas.Add(area);
                }
            }

            areas.Sort((left, right) => left.Id.CompareTo(right.Id));
            var seenAreas = new HashSet<AreaId>();
            var seenPolygons = new HashSet<PolygonId>();
            var seenVertices = new HashSet<VertexId>();
            var seenEdges = new HashSet<EdgeId>();

            for (int areaSourceIndex = 0; areaSourceIndex < areas.Count; areaSourceIndex++)
            {
                NavigationAreaAsset area = areas[areaSourceIndex];
                string areaTarget = area.Id.ToString();
                if (!area.Id.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidAreaId, NavigationValidationTargetKind.Area,
                        areaTarget, "The Area has no stable identity.");
                    continue;
                }

                if (!seenAreas.Add(area.Id))
                {
                    AddError(data, NavigationValidationCode.DuplicateAreaId, NavigationValidationTargetKind.Area,
                        areaTarget, "The Area identity is duplicated in this world.");
                    continue;
                }

                if (!area.Frame.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidAreaFrame, NavigationValidationTargetKind.Area,
                        areaTarget, "The Area frame is invalid.");
                }

                ValidateMask(registry, area.RawSemantics, NavigationValidationTargetKind.Area, areaTarget, data);
                ValidateMask(registry, area.RawRequiredCapabilities, NavigationValidationTargetKind.Area, areaTarget, data);

                int areaIndex = data.Areas.Count;
                data.AreaIndices.Add(area.Id, areaIndex);
                data.AreaEdges.Add(new List<EdgeBuild>());
                int polygonStart = data.Polygons.Count;
                bool hasBounds = false;
                Bounds bounds = default;

                var polygons = new List<NavigationPolygonRecord>();
                for (int index = 0; index < area.Polygons.Count; index++)
                {
                    NavigationPolygonRecord polygon = area.Polygons[index];
                    if (polygon == null)
                    {
                        AddError(data, NavigationValidationCode.NullPolygon, NavigationValidationTargetKind.Area,
                            areaTarget, $"Polygon entry {index} is null.");
                    }
                    else
                    {
                        polygons.Add(polygon);
                    }
                }

                polygons.Sort((left, right) => left.Id.CompareTo(right.Id));
                for (int polygonSourceIndex = 0; polygonSourceIndex < polygons.Count; polygonSourceIndex++)
                {
                    NavigationPolygonRecord polygon = polygons[polygonSourceIndex];
                    string polygonTarget = polygon.Id.ToString();
                    if (!polygon.Id.IsValid)
                    {
                        AddError(data, NavigationValidationCode.InvalidPolygonId,
                            NavigationValidationTargetKind.Polygon, polygonTarget,
                            "The polygon has no stable identity.");
                        continue;
                    }

                    if (!seenPolygons.Add(polygon.Id))
                    {
                        AddError(data, NavigationValidationCode.DuplicatePolygonId,
                            NavigationValidationTargetKind.Polygon, polygonTarget,
                            "The polygon identity is duplicated in this world.");
                        continue;
                    }

                    ValidateMask(registry, polygon.RawSemantics,
                        NavigationValidationTargetKind.Polygon, polygonTarget, data);
                    ValidateMask(registry, polygon.RawRequiredCapabilities,
                        NavigationValidationTargetKind.Polygon, polygonTarget, data);

                    if (!ValidatePolygon(polygon, settings, data, seenVertices, seenEdges,
                            out Vector3 normal, out Vector3 centroid))
                    {
                        continue;
                    }

                    int densePolygon = data.Polygons.Count;
                    int vertexStart = data.Vertices.Count;
                    int semanticOffset = AppendMask(polygon.RawSemantics, data.SemanticWordCount, data.SemanticWords);
                    int capabilityOffset = AppendMask(
                        polygon.RawRequiredCapabilities,
                        data.SemanticWordCount,
                        data.SemanticWords);

                    for (int vertexIndex = 0; vertexIndex < polygon.Vertices.Count; vertexIndex++)
                    {
                        NavigationVertexRecord vertex = polygon.Vertices[vertexIndex];
                        data.Vertices.Add(new CompiledVertexRecord(
                            vertex.Id,
                            vertex.OutgoingEdgeId,
                            vertex.Position));

                        if (!hasBounds)
                        {
                            bounds = new Bounds(vertex.Position, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(vertex.Position);
                        }
                    }

                    data.Polygons.Add(new CompiledPolygonRecord(
                        polygon.Id,
                        areaIndex,
                        vertexStart,
                        polygon.Vertices.Count,
                        0,
                        0,
                        semanticOffset,
                        capabilityOffset,
                        centroid,
                        normal,
                        polygon.Enabled));

                    var polygonBuild = new PolygonBuild
                    {
                        Source = polygon,
                        AreaIndex = areaIndex,
                        DenseIndex = densePolygon,
                        Normal = normal,
                        Centroid = centroid
                    };
                    data.PolygonById.Add(polygon.Id, polygonBuild);

                    for (int vertexIndex = 0; vertexIndex < polygon.Vertices.Count; vertexIndex++)
                    {
                        NavigationVertexRecord first = polygon.Vertices[vertexIndex];
                        NavigationVertexRecord second = polygon.Vertices[(vertexIndex + 1) % polygon.Vertices.Count];
                        var reference = new PolygonEdgeReference(polygon.Id, first.OutgoingEdgeId);
                        var edge = new EdgeBuild
                        {
                            Reference = reference,
                            AreaIndex = areaIndex,
                            PolygonIndex = densePolygon,
                            Start = first.Position,
                            End = second.Position,
                            Normal = normal
                        };
                        data.EdgeByReference[reference] = edge;
                        data.AreaEdges[areaIndex].Add(edge);
                    }
                }

                int areaSemanticOffset = AppendMask(area.RawSemantics, data.SemanticWordCount, data.SemanticWords);
                int areaCapabilityOffset = AppendMask(
                    area.RawRequiredCapabilities,
                    data.SemanticWordCount,
                    data.SemanticWords);
                data.Areas.Add(new CompiledAreaRecord(
                    area.Id,
                    area.Frame,
                    polygonStart,
                    data.Polygons.Count - polygonStart,
                    areaSemanticOffset,
                    areaCapabilityOffset,
                    hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero)));
            }
        }

        private static Dictionary<EdgePairKey, AdjacencyOverrideState> BuildOverrides(
            NavigationWorldAsset source,
            BuildData data)
        {
            var result = new Dictionary<EdgePairKey, AdjacencyOverrideState>();
            for (int areaIndex = 0; areaIndex < source.Areas.Count; areaIndex++)
            {
                NavigationAreaAsset area = source.Areas[areaIndex];
                if (area == null)
                {
                    continue;
                }

                for (int overrideIndex = 0; overrideIndex < area.AdjacencyOverrides.Count; overrideIndex++)
                {
                    AdjacencyOverrideRecord value = area.AdjacencyOverrides[overrideIndex];
                    if (value == null ||
                        !data.EdgeByReference.TryGetValue(value.First, out EdgeBuild first) ||
                        !data.EdgeByReference.TryGetValue(value.Second, out EdgeBuild second) ||
                        first.AreaIndex != second.AreaIndex ||
                        first.AreaIndex != GetCompiledAreaIndex(data, area.Id) ||
                        first.PolygonIndex == second.PolygonIndex)
                    {
                        AddError(data, NavigationValidationCode.InvalidAdjacencyOverride,
                            NavigationValidationTargetKind.Area, area.Id.ToString(),
                            $"Adjacency override {overrideIndex} references missing or incompatible edges.");
                        continue;
                    }

                    var key = new EdgePairKey(value.First, value.Second);
                    if (result.ContainsKey(key))
                    {
                        AddError(data, NavigationValidationCode.DuplicateAdjacencyOverride,
                            NavigationValidationTargetKind.Edge, value.First.EdgeId.ToString(),
                            "More than one override targets the same edge pair.");
                        continue;
                    }

                    if (value.State != AdjacencyOverrideState.Automatic)
                    {
                        result.Add(key, value.State);
                    }
                }
            }

            return result;
        }

        private static void InferAdjacency(
            AdjacencyInferenceSettings settings,
            BuildData data,
            Dictionary<EdgePairKey, AdjacencyOverrideState> overrides)
        {
            // ponytail: O(E^2) is intentional for manually authored v1 Areas; add a spatial index when profiling shows it matters.
            for (int areaIndex = 0; areaIndex < data.AreaEdges.Count; areaIndex++)
            {
                List<EdgeBuild> edges = data.AreaEdges[areaIndex];
                for (int firstIndex = 0; firstIndex < edges.Count; firstIndex++)
                {
                    EdgeBuild first = edges[firstIndex];
                    for (int secondIndex = firstIndex + 1; secondIndex < edges.Count; secondIndex++)
                    {
                        EdgeBuild second = edges[secondIndex];
                        if (first.PolygonIndex == second.PolygonIndex)
                        {
                            continue;
                        }

                        var key = new EdgePairKey(first.Reference, second.Reference);
                        overrides.TryGetValue(key, out AdjacencyOverrideState state);
                        if (state == AdjacencyOverrideState.ForcedDisconnected)
                        {
                            continue;
                        }

                        bool forced = state == AdjacencyOverrideState.ForcedConnected;
                        if (!TryBuildAdjacencySpan(first, second, settings, forced,
                                out Vector3 spanStart, out Vector3 spanEnd, out double gap))
                        {
                            if (forced)
                            {
                                AddError(data, NavigationValidationCode.InvalidForcedConnection,
                                    NavigationValidationTargetKind.Edge, first.Reference.EdgeId.ToString(),
                                    "The forced edge pair cannot form a finite overlapping crossing span.",
                                    gap,
                                    settings.MaximumForcedGap);
                            }

                            continue;
                        }

                        AdjacencyOverrideState source = forced
                            ? AdjacencyOverrideState.ForcedConnected
                            : AdjacencyOverrideState.Automatic;
                        data.Adjacencies.Add(new CompiledAdjacencyRecord(
                            first.PolygonIndex,
                            second.PolygonIndex,
                            first.Reference.EdgeId,
                            second.Reference.EdgeId,
                            spanStart,
                            spanEnd,
                            source));
                        data.Adjacencies.Add(new CompiledAdjacencyRecord(
                            second.PolygonIndex,
                            first.PolygonIndex,
                            second.Reference.EdgeId,
                            first.Reference.EdgeId,
                            spanEnd,
                            spanStart,
                            source));
                    }
                }
            }

            data.Adjacencies.Sort((left, right) =>
            {
                int from = left.FromPolygon.CompareTo(right.FromPolygon);
                if (from != 0)
                {
                    return from;
                }

                int to = left.ToPolygon.CompareTo(right.ToPolygon);
                return to != 0 ? to : left.FromEdgeId.CompareTo(right.FromEdgeId);
            });
        }

        private static void BuildPortals(
            NavigationWorldAsset source,
            SemanticRegistryAsset registry,
            AdjacencyInferenceSettings settings,
            BuildData data)
        {
            var portals = new List<NavigationPortalRecord>();
            for (int index = 0; index < source.Portals.Count; index++)
            {
                NavigationPortalRecord portal = source.Portals[index];
                if (portal == null)
                {
                    AddError(data, NavigationValidationCode.NullPortal, NavigationValidationTargetKind.World,
                        source.name, $"Portal entry {index} is null.");
                }
                else
                {
                    portals.Add(portal);
                }
            }

            portals.Sort((left, right) => left.Id.CompareTo(right.Id));
            var seen = new HashSet<PortalId>();
            for (int index = 0; index < portals.Count; index++)
            {
                NavigationPortalRecord portal = portals[index];
                string target = portal.Id.ToString();
                bool valid = true;
                if (!portal.Id.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidPortalId,
                        NavigationValidationTargetKind.Portal, target, "The Portal has no stable identity.");
                    valid = false;
                }
                else if (!seen.Add(portal.Id))
                {
                    AddError(data, NavigationValidationCode.DuplicatePortalId,
                        NavigationValidationTargetKind.Portal, target, "The Portal identity is duplicated.");
                    valid = false;
                }

                if (portal.Source.AreaId == portal.Destination.AreaId ||
                    !data.AreaIndices.TryGetValue(portal.Source.AreaId, out int sourceArea) ||
                    !data.AreaIndices.TryGetValue(portal.Destination.AreaId, out int destinationArea))
                {
                    AddError(data, NavigationValidationCode.InvalidPortalArea,
                        NavigationValidationTargetKind.Portal, target,
                        "Portal sides must reference two distinct Areas in this world.");
                    valid = false;
                    sourceArea = -1;
                    destinationArea = -1;
                }

                valid &= ValidatePortalSpan(portal.Source, sourceArea, settings, data, target, out int sourcePolygon);
                valid &= ValidatePortalSpan(
                    portal.Destination,
                    destinationArea,
                    settings,
                    data,
                    target,
                    out int destinationPolygon);

                if (portal.BaseCost < 0d || double.IsNaN(portal.BaseCost) || double.IsInfinity(portal.BaseCost))
                {
                    AddError(data, NavigationValidationCode.InvalidPortalCost,
                        NavigationValidationTargetKind.Portal, target,
                        "Portal base cost must be finite and nonnegative.");
                    valid = false;
                }

                double transformError = 0d;
                bool transformValid = portal.SourceToDestination.IsValid;
                if (transformValid && sourceArea >= 0 && destinationArea >= 0)
                {
                    transformValid = PortalTransformMatches(portal, data, settings, out transformError);
                }

                if (!transformValid)
                {
                    AddError(data, NavigationValidationCode.InvalidPortalTransform,
                        NavigationValidationTargetKind.Portal, target,
                        "The Portal transform does not map its source span onto its destination span.",
                        sourceArea >= 0 && destinationArea >= 0 ? transformError : 0d,
                        Math.Max(settings.PositionTolerance, settings.HeightTolerance));
                    valid = false;
                }

                ValidateMask(registry, portal.RawSemantics,
                    NavigationValidationTargetKind.Portal, target, data);
                ValidateMask(registry, portal.RawRequiredCapabilities,
                    NavigationValidationTargetKind.Portal, target, data);

                if (!valid)
                {
                    continue;
                }

                int semanticOffset = AppendMask(portal.RawSemantics, data.SemanticWordCount, data.SemanticWords);
                int capabilityOffset = AppendMask(
                    portal.RawRequiredCapabilities,
                    data.SemanticWordCount,
                    data.SemanticWords);
                data.Portals.Add(new CompiledPortalRecord(
                    portal.Id,
                    sourceArea,
                    sourcePolygon,
                    destinationArea,
                    destinationPolygon,
                    portal.Source,
                    portal.Destination,
                    portal.Direction,
                    portal.Enabled,
                    semanticOffset,
                    capabilityOffset,
                    portal.BaseCost,
                    portal.SourceToDestination));
                data.PortalPolygons.Add(portal.Source.PolygonId);
                data.PortalPolygons.Add(portal.Destination.PolygonId);
            }
        }

        private static void BuildConnectivityWarnings(BuildData data)
        {
            var neighbors = new List<int>[data.Polygons.Count];
            for (int index = 0; index < neighbors.Length; index++)
            {
                neighbors[index] = new List<int>();
            }

            for (int index = 0; index < data.Adjacencies.Count; index++)
            {
                CompiledAdjacencyRecord edge = data.Adjacencies[index];
                neighbors[edge.FromPolygon].Add(edge.ToPolygon);
            }

            for (int index = 0; index < data.Polygons.Count; index++)
            {
                if (neighbors[index].Count == 0 && !data.PortalPolygons.Contains(data.Polygons[index].Id))
                {
                    AddWarning(data, NavigationValidationCode.OrphanPolygon,
                        NavigationValidationTargetKind.Polygon, data.Polygons[index].Id.ToString(),
                        "The polygon has no neighbor or attached Portal.");
                }
            }

            for (int areaIndex = 0; areaIndex < data.Areas.Count; areaIndex++)
            {
                CompiledAreaRecord area = data.Areas[areaIndex];
                var visited = new HashSet<int>();
                int components = 0;
                for (int offset = 0; offset < area.PolygonCount; offset++)
                {
                    int start = area.PolygonStart + offset;
                    if (visited.Contains(start))
                    {
                        continue;
                    }

                    components++;
                    var queue = new Queue<int>();
                    queue.Enqueue(start);
                    visited.Add(start);
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        for (int neighborIndex = 0; neighborIndex < neighbors[current].Count; neighborIndex++)
                        {
                            int neighbor = neighbors[current][neighborIndex];
                            if (data.Polygons[neighbor].AreaIndex == areaIndex && visited.Add(neighbor))
                            {
                                queue.Enqueue(neighbor);
                            }
                        }
                    }

                    if (components > 1)
                    {
                        AddWarning(data, NavigationValidationCode.UnreachableIsland,
                            NavigationValidationTargetKind.Polygon, data.Polygons[start].Id.ToString(),
                            "The Area contains more than one disconnected polygon island.");
                    }
                }
            }
        }

        private static void ApplyAdjacencyRanges(BuildData data)
        {
            int adjacencyIndex = 0;
            for (int polygonIndex = 0; polygonIndex < data.Polygons.Count; polygonIndex++)
            {
                int start = adjacencyIndex;
                while (adjacencyIndex < data.Adjacencies.Count &&
                       data.Adjacencies[adjacencyIndex].FromPolygon == polygonIndex)
                {
                    adjacencyIndex++;
                }

                data.Polygons[polygonIndex] = data.Polygons[polygonIndex]
                    .WithAdjacencyRange(start, adjacencyIndex - start);
            }
        }

        private static bool ValidatePolygon(
            NavigationPolygonRecord polygon,
            AdjacencyInferenceSettings settings,
            BuildData data,
            HashSet<VertexId> seenVertices,
            HashSet<EdgeId> seenEdges,
            out Vector3 normal,
            out Vector3 centroid)
        {
            normal = Vector3.up;
            centroid = Vector3.zero;
            string target = polygon.Id.ToString();
            bool valid = true;
            bool positionsUsable = true;
            if (polygon.Vertices.Count < 3)
            {
                AddError(data, NavigationValidationCode.TooFewVertices,
                    NavigationValidationTargetKind.Polygon, target,
                    "A polygon requires at least three vertices.");
                return false;
            }

            for (int index = 0; index < polygon.Vertices.Count; index++)
            {
                NavigationVertexRecord vertex = polygon.Vertices[index];
                if (vertex == null || !vertex.Id.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidVertexId,
                        NavigationValidationTargetKind.Polygon, target,
                        $"Vertex {index} has no stable identity.");
                    valid = false;
                    if (vertex == null)
                    {
                        positionsUsable = false;
                    }
                    continue;
                }

                if (!seenVertices.Add(vertex.Id))
                {
                    AddError(data, NavigationValidationCode.DuplicateVertexId,
                        NavigationValidationTargetKind.Vertex, vertex.Id.ToString(),
                        "The vertex identity is duplicated in this world.");
                    valid = false;
                }

                if (!vertex.OutgoingEdgeId.IsValid)
                {
                    AddError(data, NavigationValidationCode.InvalidEdgeId,
                        NavigationValidationTargetKind.Vertex, vertex.Id.ToString(),
                        "The outgoing edge has no stable identity.");
                    valid = false;
                }
                else if (!seenEdges.Add(vertex.OutgoingEdgeId))
                {
                    AddError(data, NavigationValidationCode.DuplicateEdgeId,
                        NavigationValidationTargetKind.Edge, vertex.OutgoingEdgeId.ToString(),
                        "The edge identity is duplicated in this world.");
                    valid = false;
                }

                if (!IsFinite(vertex.Position))
                {
                    AddError(data, NavigationValidationCode.NonFiniteVertex,
                        NavigationValidationTargetKind.Vertex, vertex.Id.ToString(),
                        "The vertex position must be finite.");
                    valid = false;
                    positionsUsable = false;
                }

                centroid += vertex.Position;
            }

            if (!positionsUsable)
            {
                centroid = Vector3.zero;
                return false;
            }

            centroid /= polygon.Vertices.Count;
            float squaredTolerance = settings.PositionTolerance * settings.PositionTolerance;
            for (int first = 0; first < polygon.Vertices.Count; first++)
            {
                Vector3 firstPosition = polygon.Vertices[first].Position;
                int next = (first + 1) % polygon.Vertices.Count;
                float edgeLengthSquared = (polygon.Vertices[next].Position - firstPosition).sqrMagnitude;
                if (edgeLengthSquared <= squaredTolerance)
                {
                    AddError(data, NavigationValidationCode.ZeroLengthEdge,
                        NavigationValidationTargetKind.Edge,
                        polygon.Vertices[first].OutgoingEdgeId.ToString(),
                        "The edge is shorter than the configured position tolerance.",
                        Math.Sqrt(edgeLengthSquared),
                        settings.PositionTolerance);
                    valid = false;
                }

                for (int second = first + 1; second < polygon.Vertices.Count; second++)
                {
                    if ((polygon.Vertices[second].Position - firstPosition).sqrMagnitude <= squaredTolerance)
                    {
                        AddError(data, NavigationValidationCode.DuplicateVertex,
                            NavigationValidationTargetKind.Vertex,
                            polygon.Vertices[second].Id.ToString(),
                            "Two polygon vertices occupy the same position.");
                        valid = false;
                    }
                }
            }

            Vector3 newell = ComputeNewellNormal(polygon.Vertices);
            if (newell.sqrMagnitude <= 1e-12f)
            {
                AddError(data, NavigationValidationCode.DegeneratePolygon,
                    NavigationValidationTargetKind.Polygon, target,
                    "The polygon has no stable plane.");
                return false;
            }

            normal = newell.normalized;
            if (Vector3.Dot(normal, Vector3.up) <= 0f)
            {
                AddError(data, NavigationValidationCode.InvalidWinding,
                    NavigationValidationTargetKind.Polygon, target,
                    "Polygon vertices must wind counter-clockwise around Area up.");
                valid = false;
            }

            float maximumPlanarityError = 0f;
            Vector3 planePoint = polygon.Vertices[0].Position;
            for (int index = 1; index < polygon.Vertices.Count; index++)
            {
                maximumPlanarityError = Math.Max(
                    maximumPlanarityError,
                    Math.Abs(Vector3.Dot(normal, polygon.Vertices[index].Position - planePoint)));
            }

            if (maximumPlanarityError > settings.PlanarityTolerance)
            {
                AddError(data, NavigationValidationCode.NonPlanarPolygon,
                    NavigationValidationTargetKind.Polygon, target,
                    "Polygon non-planarity exceeds the configured tolerance.",
                    maximumPlanarityError,
                    settings.PlanarityTolerance);
                valid = false;
            }

            if (!IsConvex(polygon.Vertices, normal, settings.PositionTolerance))
            {
                AddError(data, NavigationValidationCode.ConcavePolygon,
                    NavigationValidationTargetKind.Polygon, target,
                    "Version 1 polygons must be convex.");
                valid = false;
            }

            if (HasSelfIntersection(polygon.Vertices, normal, settings.PositionTolerance))
            {
                AddError(data, NavigationValidationCode.SelfIntersection,
                    NavigationValidationTargetKind.Polygon, target,
                    "The polygon boundary intersects itself.");
                valid = false;
            }

            return valid;
        }

        private static bool ValidatePortalSpan(
            PortalEntrySpan span,
            int expectedArea,
            AdjacencyInferenceSettings settings,
            BuildData data,
            string portalTarget,
            out int polygonIndex)
        {
            polygonIndex = -1;
            if (!data.PolygonById.TryGetValue(span.PolygonId, out PolygonBuild polygon) ||
                polygon.AreaIndex != expectedArea)
            {
                AddError(data, NavigationValidationCode.InvalidPortalPolygon,
                    NavigationValidationTargetKind.Portal, portalTarget,
                    "A Portal side references a polygon outside its Area.");
                return false;
            }

            var reference = new PolygonEdgeReference(span.PolygonId, span.EdgeId);
            if (!data.EdgeByReference.TryGetValue(reference, out EdgeBuild edge))
            {
                AddError(data, NavigationValidationCode.InvalidPortalEdge,
                    NavigationValidationTargetKind.Portal, portalTarget,
                    "A Portal side references a missing polygon edge.");
                return false;
            }

            float tolerance = Math.Max(settings.PositionTolerance, settings.HeightTolerance);
            float length = Vector3.Distance(span.Start, span.End);
            float startDistance = DistanceToSegment(span.Start, edge.Start, edge.End);
            float endDistance = DistanceToSegment(span.End, edge.Start, edge.End);
            if (!IsFinite(span.Start) || !IsFinite(span.End) ||
                length < settings.MinimumOverlap ||
                startDistance > tolerance || endDistance > tolerance)
            {
                AddError(data, NavigationValidationCode.InvalidPortalSpan,
                    NavigationValidationTargetKind.Portal, portalTarget,
                    "A Portal entry span must be finite, nondegenerate, and lie on its referenced edge.",
                    Math.Max(startDistance, endDistance),
                    tolerance);
                return false;
            }

            polygonIndex = polygon.DenseIndex;
            return true;
        }

        private static bool PortalTransformMatches(
            NavigationPortalRecord portal,
            BuildData data,
            AdjacencyInferenceSettings settings,
            out double error)
        {
            AreaFrame sourceFrame = data.Areas[data.AreaIndices[portal.Source.AreaId]].Frame;
            AreaFrame destinationFrame = data.Areas[data.AreaIndices[portal.Destination.AreaId]].Frame;
            Double3 mappedStart = portal.SourceToDestination.TransformPosition(
                sourceFrame.ToUniverse(portal.Source.Start));
            Double3 mappedEnd = portal.SourceToDestination.TransformPosition(
                sourceFrame.ToUniverse(portal.Source.End));
            Double3 destinationStart = destinationFrame.ToUniverse(portal.Destination.Start);
            Double3 destinationEnd = destinationFrame.ToUniverse(portal.Destination.End);

            double direct = Math.Max(
                Distance(mappedStart, destinationStart),
                Distance(mappedEnd, destinationEnd));
            double reversed = Math.Max(
                Distance(mappedStart, destinationEnd),
                Distance(mappedEnd, destinationStart));
            error = Math.Min(direct, reversed);
            return error <= Math.Max(settings.PositionTolerance, settings.HeightTolerance);
        }

        private static bool TryBuildAdjacencySpan(
            EdgeBuild first,
            EdgeBuild second,
            AdjacencyInferenceSettings settings,
            bool forced,
            out Vector3 spanStart,
            out Vector3 spanEnd,
            out double gap)
        {
            spanStart = default;
            spanEnd = default;
            gap = double.PositiveInfinity;
            Vector3 firstVector = first.End - first.Start;
            Vector3 secondVector = second.End - second.Start;
            float firstLength = firstVector.magnitude;
            float secondLength = secondVector.magnitude;
            if (firstLength <= 1e-6f || secondLength <= 1e-6f)
            {
                return false;
            }

            Vector3 firstDirection = firstVector / firstLength;
            Vector3 secondDirection = secondVector / secondLength;
            float cosine = Mathf.Cos(settings.NormalAngleTolerance * Mathf.Deg2Rad);
            float directionDot = Vector3.Dot(firstDirection, secondDirection);
            if (!forced && directionDot > -cosine)
            {
                return false;
            }

            float projectedSecondStart = Vector3.Dot(second.Start - first.Start, firstDirection);
            float projectedSecondEnd = Vector3.Dot(second.End - first.Start, firstDirection);
            float overlapStart = Math.Max(0f, Math.Min(projectedSecondStart, projectedSecondEnd));
            float overlapEnd = Math.Min(firstLength, Math.Max(projectedSecondStart, projectedSecondEnd));
            if (overlapEnd - overlapStart < settings.MinimumOverlap)
            {
                return false;
            }

            Vector3 firstStart = first.Start + (firstDirection * overlapStart);
            Vector3 firstEnd = first.Start + (firstDirection * overlapEnd);
            Vector3 secondStart = ClosestPointOnSegment(firstStart, second.Start, second.End);
            Vector3 secondEnd = ClosestPointOnSegment(firstEnd, second.Start, second.End);
            float startGap = Vector3.Distance(firstStart, secondStart);
            float endGap = Vector3.Distance(firstEnd, secondEnd);
            gap = Math.Max(startGap, endGap);

            if (forced)
            {
                if (gap > settings.MaximumForcedGap)
                {
                    return false;
                }
            }
            else
            {
                float normalAngle = Vector3.Angle(first.Normal, second.Normal);
                float horizontalGap = Math.Max(
                    HorizontalDistance(firstStart, secondStart),
                    HorizontalDistance(firstEnd, secondEnd));
                float heightGap = Math.Max(
                    Math.Abs(firstStart.y - secondStart.y),
                    Math.Abs(firstEnd.y - secondEnd.y));
                if (normalAngle > settings.NormalAngleTolerance ||
                    horizontalGap > settings.PositionTolerance ||
                    heightGap > settings.HeightTolerance)
                {
                    return false;
                }
            }

            spanStart = (firstStart + secondStart) * 0.5f;
            spanEnd = (firstEnd + secondEnd) * 0.5f;
            return IsFinite(spanStart) && IsFinite(spanEnd);
        }

        private static Vector3 ComputeNewellNormal(IReadOnlyList<NavigationVertexRecord> vertices)
        {
            Vector3 normal = Vector3.zero;
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 current = vertices[index].Position;
                Vector3 next = vertices[(index + 1) % vertices.Count].Position;
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            return normal;
        }

        private static bool IsConvex(
            IReadOnlyList<NavigationVertexRecord> vertices,
            Vector3 normal,
            float tolerance)
        {
            float sign = 0f;
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 previous = vertices[(index + vertices.Count - 1) % vertices.Count].Position;
                Vector3 current = vertices[index].Position;
                Vector3 next = vertices[(index + 1) % vertices.Count].Position;
                float turn = Vector3.Dot(Vector3.Cross(current - previous, next - current), normal);
                if (Math.Abs(turn) <= tolerance * tolerance)
                {
                    continue;
                }

                float currentSign = Math.Sign(turn);
                if (sign == 0f)
                {
                    sign = currentSign;
                }
                else if (currentSign != sign)
                {
                    return false;
                }
            }

            return sign != 0f;
        }

        private static bool HasSelfIntersection(
            IReadOnlyList<NavigationVertexRecord> vertices,
            Vector3 normal,
            float tolerance)
        {
            Vector3 axisX = (vertices[1].Position - vertices[0].Position).normalized;
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;
            for (int first = 0; first < vertices.Count; first++)
            {
                int firstNext = (first + 1) % vertices.Count;
                Vector2 firstA = Project(vertices[first].Position, axisX, axisY);
                Vector2 firstB = Project(vertices[firstNext].Position, axisX, axisY);
                for (int second = first + 1; second < vertices.Count; second++)
                {
                    int secondNext = (second + 1) % vertices.Count;
                    if (first == second || firstNext == second || secondNext == first)
                    {
                        continue;
                    }

                    Vector2 secondA = Project(vertices[second].Position, axisX, axisY);
                    Vector2 secondB = Project(vertices[secondNext].Position, axisX, axisY);
                    if (SegmentsIntersect(firstA, firstB, secondA, secondB, tolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float tolerance)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return ((abC > tolerance && abD < -tolerance) || (abC < -tolerance && abD > tolerance)) &&
                   ((cdA > tolerance && cdB < -tolerance) || (cdA < -tolerance && cdB > tolerance));
        }

        private static void ValidateMask(
            SemanticRegistryAsset registry,
            SemanticMask mask,
            NavigationValidationTargetKind targetKind,
            string target,
            BuildData data)
        {
            if (mask == null)
            {
                return;
            }

            if (mask.HasBitsAtOrAbove(registry.SlotCapacity))
            {
                AddError(data, NavigationValidationCode.SemanticSlotOutOfRange, targetKind, target,
                    "The semantic mask contains a bit outside the project registry.");
            }

            for (int slot = 0; slot < registry.SlotCapacity; slot++)
            {
                if (mask.Contains(slot) && registry.TryGet(slot, out SemanticDefinition definition) && definition.IsDeleted)
                {
                    AddWarning(data, NavigationValidationCode.TombstonedSemantic,
                        NavigationValidationTargetKind.Semantic, definition.Id.ToString(),
                        $"The mask retains tombstoned semantic slot {slot}.");
                }
            }
        }

        private static int AppendMask(SemanticMask mask, int wordCount, List<ulong> words)
        {
            int offset = words.Count;
            for (int index = 0; index < wordCount; index++)
            {
                words.Add(mask?.GetWord(index) ?? 0UL);
            }

            return offset;
        }

        private static int GetCompiledAreaIndex(BuildData data, AreaId id)
        {
            return data.AreaIndices.TryGetValue(id, out int index) ? index : -1;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 1e-12f)
            {
                return start;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return start + (segment * t);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            return Vector3.Distance(point, ClosestPointOnSegment(point, start, end));
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }

        private static double Distance(Double3 first, Double3 second)
        {
            double x = first.X - second.X;
            double y = first.Y - second.Y;
            double z = first.Z - second.Z;
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private static Vector2 Project(Vector3 value, Vector3 axisX, Vector3 axisY)
        {
            return new Vector2(Vector3.Dot(value, axisX), Vector3.Dot(value, axisY));
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return (left.x * right.y) - (left.y * right.x);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void AddError(
            BuildData data,
            NavigationValidationCode code,
            NavigationValidationTargetKind targetKind,
            string target,
            string message,
            double measured = 0d,
            double allowed = 0d)
        {
            data.Issues.Add(new NavigationValidationIssue(
                NavigationValidationSeverity.Error,
                code,
                targetKind,
                target,
                message,
                measured,
                allowed));
        }

        private static void AddWarning(
            BuildData data,
            NavigationValidationCode code,
            NavigationValidationTargetKind targetKind,
            string target,
            string message)
        {
            data.Issues.Add(new NavigationValidationIssue(
                NavigationValidationSeverity.Warning,
                code,
                targetKind,
                target,
                message));
        }

        private static int CompareOverride(AdjacencyOverrideRecord left, AdjacencyOverrideRecord right)
        {
            var leftKey = new EdgePairKey(left.First, left.Second);
            var rightKey = new EdgePairKey(right.First, right.Second);
            int firstPolygon = leftKey.First.PolygonId.CompareTo(rightKey.First.PolygonId);
            if (firstPolygon != 0)
            {
                return firstPolygon;
            }

            int firstEdge = leftKey.First.EdgeId.CompareTo(rightKey.First.EdgeId);
            if (firstEdge != 0)
            {
                return firstEdge;
            }

            int secondPolygon = leftKey.Second.PolygonId.CompareTo(rightKey.Second.PolygonId);
            return secondPolygon != 0
                ? secondPolygon
                : leftKey.Second.EdgeId.CompareTo(rightKey.Second.EdgeId);
        }

        private static void AddSettings(ref FingerprintBuilder fingerprint, AdjacencyInferenceSettings settings)
        {
            fingerprint.Add((double)settings.PositionTolerance);
            fingerprint.Add((double)settings.MinimumOverlap);
            fingerprint.Add((double)settings.HeightTolerance);
            fingerprint.Add((double)settings.NormalAngleTolerance);
            fingerprint.Add((double)settings.PlanarityTolerance);
            fingerprint.Add((double)settings.MaximumForcedGap);
        }

        private static void AddFrame(ref FingerprintBuilder fingerprint, AreaFrame frame)
        {
            fingerprint.Add(frame.UniverseOrigin.X);
            fingerprint.Add(frame.UniverseOrigin.Y);
            fingerprint.Add(frame.UniverseOrigin.Z);
            fingerprint.Add((double)frame.Rotation.x);
            fingerprint.Add((double)frame.Rotation.y);
            fingerprint.Add((double)frame.Rotation.z);
            fingerprint.Add((double)frame.Rotation.w);
        }

        private static void AddPortalTransform(ref FingerprintBuilder fingerprint, PortalTransform transform)
        {
            fingerprint.Add(transform.Translation.X);
            fingerprint.Add(transform.Translation.Y);
            fingerprint.Add(transform.Translation.Z);
            fingerprint.Add((double)transform.Rotation.x);
            fingerprint.Add((double)transform.Rotation.y);
            fingerprint.Add((double)transform.Rotation.z);
            fingerprint.Add((double)transform.Rotation.w);
        }

        private static void AddSpan(ref FingerprintBuilder fingerprint, PortalEntrySpan span)
        {
            AddId(ref fingerprint, span.AreaId.High, span.AreaId.Low);
            AddId(ref fingerprint, span.PolygonId.High, span.PolygonId.Low);
            AddId(ref fingerprint, span.EdgeId.High, span.EdgeId.Low);
            AddVector(ref fingerprint, span.Start);
            AddVector(ref fingerprint, span.End);
        }

        private static void AddEdgeReference(ref FingerprintBuilder fingerprint, PolygonEdgeReference reference)
        {
            AddId(ref fingerprint, reference.PolygonId.High, reference.PolygonId.Low);
            AddId(ref fingerprint, reference.EdgeId.High, reference.EdgeId.Low);
        }

        private static void AddVector(ref FingerprintBuilder fingerprint, Vector3 value)
        {
            fingerprint.Add((double)value.x);
            fingerprint.Add((double)value.y);
            fingerprint.Add((double)value.z);
        }

        private static void AddId(ref FingerprintBuilder fingerprint, ulong high, ulong low)
        {
            fingerprint.Add(high);
            fingerprint.Add(low);
        }
    }
}
