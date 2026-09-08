using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    internal sealed class LocalPathData
    {
        internal LocalPathData(
            int areaIndex,
            Vector3 start,
            Vector3 goal,
            double cost,
            int[] polygons,
            NavigationCrossingSpan[] crossings,
            Vector3[] steeringTargets)
        {
            AreaIndex = areaIndex;
            Start = start;
            Goal = goal;
            Cost = cost;
            Polygons = polygons;
            Crossings = crossings;
            SteeringTargets = steeringTargets;
        }

        internal int AreaIndex { get; }
        internal Vector3 Start { get; }
        internal Vector3 Goal { get; }
        internal double Cost { get; }
        internal int[] Polygons { get; }
        internal NavigationCrossingSpan[] Crossings { get; }
        internal Vector3[] SteeringTargets { get; }
    }

    internal sealed class PathResultData
    {
        internal double TotalCost;
        internal NavigationAreaSegment[] Areas = Array.Empty<NavigationAreaSegment>();
        internal NavigationPortalTransition[] Portals = Array.Empty<NavigationPortalTransition>();
        internal PolygonId[] Polygons = Array.Empty<PolygonId>();
        internal NavigationCrossingSpan[] Crossings = Array.Empty<NavigationCrossingSpan>();
        internal Vector3[] SteeringTargets = Array.Empty<Vector3>();
        internal NavigationRevisionStamp[] Revisions = Array.Empty<NavigationRevisionStamp>();
        internal ulong TopologyRevision;

        internal NavigationPath Copy()
        {
            return new NavigationPath(
                TotalCost,
                (NavigationAreaSegment[])Areas.Clone(),
                (NavigationPortalTransition[])Portals.Clone(),
                (PolygonId[])Polygons.Clone(),
                (NavigationCrossingSpan[])Crossings.Clone(),
                (Vector3[])SteeringTargets.Clone(),
                (NavigationRevisionStamp[])Revisions.Clone(),
                TopologyRevision);
        }
    }

    internal readonly struct LocalSearchRequest
    {
        internal LocalSearchRequest(
            CompiledTraversalPolicy policy,
            NavigationRuntimeSnapshot snapshot,
            int areaIndex,
            int areaPolygonStart,
            int areaPolygonCount,
            int startPolygon,
            int goalPolygon,
            Vector3 start,
            Vector3 goal,
            double areaMultiplier,
            double baseCost)
        {
            Policy = policy;
            Snapshot = snapshot;
            AreaIndex = areaIndex;
            AreaPolygonStart = areaPolygonStart;
            AreaPolygonCount = areaPolygonCount;
            StartPolygon = startPolygon;
            GoalPolygon = goalPolygon;
            Start = new float3(start.x, start.y, start.z);
            Goal = new float3(goal.x, goal.y, goal.z);
            AreaMultiplier = areaMultiplier;
            BaseCost = baseCost;
        }

        internal CompiledTraversalPolicy Policy { get; }
        internal NavigationRuntimeSnapshot Snapshot { get; }
        internal int AreaIndex { get; }
        internal int AreaPolygonStart { get; }
        internal int AreaPolygonCount { get; }
        internal int StartPolygon { get; }
        internal int GoalPolygon { get; }
        internal float3 Start { get; }
        internal float3 Goal { get; }
        internal double AreaMultiplier { get; }
        internal double BaseCost { get; }
    }

    internal enum SearchAdvanceStatus : byte
    {
        Continue,
        NeedsLocalSearch,
        Completed,
        Failed
    }

    internal readonly struct LocalCacheKey : IEquatable<LocalCacheKey>, IComparable<LocalCacheKey>
    {
        internal LocalCacheKey(int fromPortalSide, int toPortalSide, ulong policy, ulong revision)
        {
            FromPortalSide = fromPortalSide;
            ToPortalSide = toPortalSide;
            Policy = policy;
            Revision = revision;
        }

        internal int FromPortalSide { get; }
        internal int ToPortalSide { get; }
        internal ulong Policy { get; }
        internal ulong Revision { get; }

        public bool Equals(LocalCacheKey other)
        {
            return FromPortalSide == other.FromPortalSide &&
                   ToPortalSide == other.ToPortalSide &&
                   Policy == other.Policy &&
                   Revision == other.Revision;
        }

        public override bool Equals(object obj) => obj is LocalCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(FromPortalSide, ToPortalSide, Policy, Revision);

        public int CompareTo(LocalCacheKey other)
        {
            int comparison = FromPortalSide.CompareTo(other.FromPortalSide);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ToPortalSide.CompareTo(other.ToPortalSide);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = Policy.CompareTo(other.Policy);
            return comparison != 0 ? comparison : Revision.CompareTo(other.Revision);
        }
    }

    internal sealed class AreaPathCache
    {
        private sealed class Entry
        {
            internal LocalPathData Path;
            internal long LastUse;
        }

        private readonly int _capacity;
        private readonly Dictionary<LocalCacheKey, Entry> _entries =
            new Dictionary<LocalCacheKey, Entry>();
        private long _clock;

        internal AreaPathCache(int capacity)
        {
            _capacity = capacity;
        }

        internal int Count => _entries.Count;

        internal bool TryGet(LocalCacheKey key, out LocalPathData path, out bool reachable)
        {
            if (_entries.TryGetValue(key, out Entry entry))
            {
                entry.LastUse = NextClock();
                path = entry.Path;
                reachable = path != null;
                return true;
            }

            path = null;
            reachable = false;
            return false;
        }

        internal void Store(LocalCacheKey key, LocalPathData path)
        {
            if (_capacity == 0)
            {
                return;
            }

            if (_entries.TryGetValue(key, out Entry existing))
            {
                existing.Path = path;
                existing.LastUse = NextClock();
                return;
            }

            if (_entries.Count >= _capacity)
            {
                LocalCacheKey oldestKey = default;
                Entry oldest = null;
                foreach (KeyValuePair<LocalCacheKey, Entry> pair in _entries)
                {
                    if (oldest == null || pair.Value.LastUse < oldest.LastUse ||
                        (pair.Value.LastUse == oldest.LastUse && pair.Key.CompareTo(oldestKey) < 0))
                    {
                        oldestKey = pair.Key;
                        oldest = pair.Value;
                    }
                }

                _entries.Remove(oldestKey);
            }

            _entries.Add(key, new Entry { Path = path, LastUse = NextClock() });
        }

        internal void Clear()
        {
            _entries.Clear();
            _clock = 0L;
        }

        private long NextClock()
        {
            unchecked
            {
                _clock++;
                if (_clock <= 0L)
                {
                    _clock = 1L;
                }

                return _clock;
            }
        }
    }

    internal static class NavigationSearch
    {
        private readonly struct DirectedPortal
        {
            internal DirectedPortal(CompiledPortalRecord portal, int portalIndex, bool reverse)
            {
                Portal = portal;
                PortalIndex = portalIndex;
                Reverse = reverse;
                SideKey = (portalIndex * 2) + (reverse ? 1 : 0);
                EntryArea = reverse ? portal.DestinationArea : portal.SourceArea;
                EntryPolygon = reverse ? portal.DestinationPolygon : portal.SourcePolygon;
                EntrySpan = reverse ? portal.DestinationSpan : portal.SourceSpan;
                ExitArea = reverse ? portal.SourceArea : portal.DestinationArea;
                ExitPolygon = reverse ? portal.SourcePolygon : portal.DestinationPolygon;
                ExitSpan = reverse ? portal.SourceSpan : portal.DestinationSpan;
                Transform = reverse ? portal.SourceToDestination.Inverse : portal.SourceToDestination;
            }

            internal CompiledPortalRecord Portal { get; }
            internal int PortalIndex { get; }
            internal bool Reverse { get; }
            internal int SideKey { get; }
            internal int EntryArea { get; }
            internal int EntryPolygon { get; }
            internal PortalEntrySpan EntrySpan { get; }
            internal int ExitArea { get; }
            internal int ExitPolygon { get; }
            internal PortalEntrySpan ExitSpan { get; }
            internal PortalTransform Transform { get; }
        }

        private readonly struct RouteStep
        {
            internal RouteStep(LocalPathData localPath, DirectedPortal portal)
            {
                LocalPath = localPath;
                Portal = portal;
            }

            internal LocalPathData LocalPath { get; }
            internal DirectedPortal Portal { get; }
        }

        internal sealed class State : IDisposable
        {
            private enum Phase : byte
            {
                SelectNode,
                GoalEdge,
                PortalEdges
            }

            private enum LocalPurpose : byte
            {
                Goal,
                Portal
            }

            private readonly NavigationRuntimeData _data;
            private readonly NavigationRuntimeSnapshot _snapshot;
            private readonly PathQuery _query;
            private readonly int _startArea;
            private readonly int _goalArea;
            private readonly int _startPolygon;
            private readonly int _goalPolygon;
            private readonly DirectedPortal[] _portals;
            private readonly bool[] _evaluatedAreas;
            private readonly double[] _distances;
            private readonly bool[] _visited;
            private readonly int[] _previous;
            private readonly int[] _incomingPortal;
            private readonly LocalPathData[] _incomingLocal;
            private Phase _phase;
            private int _current;
            private int _currentArea;
            private int _currentPolygon;
            private Vector3 _currentPosition;
            private int _portalCursor;
            private double _bestGoalCost = double.PositiveInfinity;
            private int _bestGoalNode = -1;
            private LocalPathData _bestGoalLocal;
            private LocalPurpose _pendingPurpose;
            private int _pendingPortal;
            private int _pendingArea;
            private bool _pendingCacheStore;
            private LocalCacheKey _pendingCacheKey;
            private bool _disposed;

            private State(
                NavigationRuntimeData data,
                NavigationRuntimeSnapshot snapshot,
                PathQuery query,
                int startArea,
                int goalArea,
                int startPolygon,
                int goalPolygon)
            {
                _data = data;
                _snapshot = snapshot;
                _query = query;
                _startArea = startArea;
                _goalArea = goalArea;
                _startPolygon = startPolygon;
                _goalPolygon = goalPolygon;
                _evaluatedAreas = new bool[data.Areas.Length];
                _evaluatedAreas[startArea] = true;
                _evaluatedAreas[goalArea] = true;
                _portals = BuildDirectedPortals(data, query.Policy, snapshot);
                int nodeCount = _portals.Length + 1;
                _distances = new double[nodeCount];
                _visited = new bool[nodeCount];
                _previous = new int[nodeCount];
                _incomingPortal = new int[nodeCount];
                _incomingLocal = new LocalPathData[nodeCount];
                for (int index = 0; index < nodeCount; index++)
                {
                    _distances[index] = double.PositiveInfinity;
                    _previous[index] = -1;
                    _incomingPortal[index] = -1;
                }

                _distances[0] = 0d;
            }

            internal LocalSearchRequest PendingLocalSearch { get; private set; }

            internal static bool TryCreate(
                NavigationRuntimeData data,
                NavigationRuntimeSnapshot snapshot,
                PathQuery query,
                out State state,
                out PathFailureReason failure)
            {
                state = null;
                failure = PathFailureReason.None;
                if (!query.IsValid || query.Policy.RegistryFingerprint != data.RegistryFingerprint ||
                    query.Policy.WordCount != data.SemanticWordCount)
                {
                    failure = PathFailureReason.InvalidRequest;
                    return false;
                }

                if (!data.AreaById.TryGetValue(query.Start.AreaId, out int startArea) ||
                    !data.AreaById.TryGetValue(query.Goal.AreaId, out int goalArea) ||
                    !TryFindPolygon(
                        data,
                        snapshot,
                        query.Policy,
                        startArea,
                        query.Start.LocalPosition,
                        out int startPolygon) ||
                    !TryFindPolygon(
                        data,
                        snapshot,
                        query.Policy,
                        goalArea,
                        query.Goal.LocalPosition,
                        out int goalPolygon))
                {
                    failure = PathFailureReason.LocationNotFound;
                    return false;
                }

                state = new State(
                    data,
                    snapshot,
                    query,
                    startArea,
                    goalArea,
                    startPolygon,
                    goalPolygon);
                return true;
            }

            internal SearchAdvanceStatus Advance(
                out PathResultData result,
                out PathFailureReason failure)
            {
                result = null;
                failure = PathFailureReason.None;
                switch (_phase)
                {
                    case Phase.SelectNode:
                        _current = FindCheapestUnvisited(_distances, _visited);
                        if (_current < 0 || _distances[_current] > _bestGoalCost)
                        {
                            return Finish(out result, out failure);
                        }

                        _visited[_current] = true;
                        GetNodeLocation(
                            _portals,
                            _current,
                            _startArea,
                            _startPolygon,
                            _query.Start.LocalPosition,
                            out _currentArea,
                            out _currentPolygon,
                            out _currentPosition);
                        _evaluatedAreas[_currentArea] = true;
                        _portalCursor = 0;
                        _phase = Phase.GoalEdge;
                        return SearchAdvanceStatus.Continue;

                    case Phase.GoalEdge:
                        _phase = Phase.PortalEdges;
                        return _currentArea == _goalArea
                            ? StartLocal(
                                LocalPurpose.Goal,
                                -1,
                                false,
                                default,
                                _currentArea,
                                _currentPosition,
                                _query.Goal.LocalPosition,
                                _currentPolygon,
                                _goalPolygon)
                            : SearchAdvanceStatus.Continue;

                    case Phase.PortalEdges:
                        if (_portalCursor >= _portals.Length)
                        {
                            _phase = Phase.SelectNode;
                            return SearchAdvanceStatus.Continue;
                        }

                        int portalIndex = _portalCursor++;
                        DirectedPortal portal = _portals[portalIndex];
                        if (portal.EntryArea != _currentArea)
                        {
                            return SearchAdvanceStatus.Continue;
                        }

                        int incomingSide = _current > 0 ? _portals[_current - 1].SideKey : -1;
                        bool cacheStore = incomingSide >= 0;
                        var cacheKey = new LocalCacheKey(
                            incomingSide,
                            portal.SideKey,
                            _query.Policy.Fingerprint,
                            _snapshot.AreaRevisions[_currentArea]);
                        if (cacheStore &&
                            _data.Caches[_currentArea].TryGet(cacheKey, out LocalPathData cached, out bool reachable))
                        {
                            if (reachable)
                            {
                                ApplyPortal(portalIndex, cached);
                            }

                            return SearchAdvanceStatus.Continue;
                        }

                        return StartLocal(
                            LocalPurpose.Portal,
                            portalIndex,
                            cacheStore,
                            cacheKey,
                            _currentArea,
                            _currentPosition,
                            portal.EntrySpan.Midpoint,
                            _currentPolygon,
                            portal.EntryPolygon);

                    default:
                        throw new InvalidOperationException("The navigation search entered an invalid phase.");
                }
            }

            internal void CompleteLocal(BurstPolygonSearch search, int laneIndex, bool found, double totalCost)
            {
                LocalSearchRequest request = PendingLocalSearch;
                LocalPathData path = null;
                if (found)
                {
                    var corridor = new List<int>();
                    var adjacencyPath = new List<int>();
                    int cursor = request.GoalPolygon;
                    corridor.Add(cursor);
                    while (cursor != request.StartPolygon)
                    {
                        int adjacency = search.GetPreviousAdjacency(laneIndex, cursor);
                        cursor = search.GetPrevious(laneIndex, cursor);
                        if (adjacency < 0 || cursor < 0)
                        {
                            throw new InvalidOperationException("The Burst search returned an invalid corridor.");
                        }

                        adjacencyPath.Add(adjacency);
                        corridor.Add(cursor);
                    }

                    corridor.Reverse();
                    adjacencyPath.Reverse();
                    var crossings = new NavigationCrossingSpan[adjacencyPath.Count];
                    for (int index = 0; index < crossings.Length; index++)
                    {
                        CompiledAdjacencyRecord adjacency = _data.Adjacencies[adjacencyPath[index]];
                        crossings[index] = new NavigationCrossingSpan(
                            _data.Areas[request.AreaIndex].Id,
                            _data.Polygons[adjacency.FromPolygon].Id,
                            _data.Polygons[adjacency.ToPolygon].Id,
                            adjacency.SpanStart,
                            adjacency.SpanEnd);
                    }

                    Vector3 start = new Vector3(request.Start.x, request.Start.y, request.Start.z);
                    Vector3 goal = new Vector3(request.Goal.x, request.Goal.y, request.Goal.z);
                    path = new LocalPathData(
                        request.AreaIndex,
                        start,
                        goal,
                        totalCost,
                        corridor.ToArray(),
                        crossings,
                        BuildFunnel(_data, corridor, crossings, start, goal));
                }

                CompletePendingLocal(path);
            }

            internal bool CapturedStateChanged()
            {
                if (_snapshot.TopologyRevision != _data.TopologyRevision)
                {
                    return true;
                }

                for (int index = 0; index < _evaluatedAreas.Length; index++)
                {
                    if (_evaluatedAreas[index] &&
                        _snapshot.AreaRevisions[index] != _data.AreaRevisions[index])
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _snapshot.Release();
            }

            private SearchAdvanceStatus StartLocal(
                LocalPurpose purpose,
                int portalIndex,
                bool cacheStore,
                LocalCacheKey cacheKey,
                int areaIndex,
                Vector3 start,
                Vector3 goal,
                int startPolygon,
                int goalPolygon)
            {
                _pendingPurpose = purpose;
                _pendingPortal = portalIndex;
                _pendingArea = areaIndex;
                _pendingCacheStore = cacheStore;
                _pendingCacheKey = cacheKey;
                CompiledAreaRecord area = _data.Areas[areaIndex];
                if (!_query.Policy.CanTraverse(
                        NavigationElementKind.Area,
                        _data.SemanticWords,
                        area.SemanticOffset,
                        area.RequiredCapabilityOffset) ||
                    !CanUsePolygon(_data, _snapshot, _query.Policy, startPolygon) ||
                    !CanUsePolygon(_data, _snapshot, _query.Policy, goalPolygon))
                {
                    CompletePendingLocal(null);
                    return SearchAdvanceStatus.Continue;
                }

                double areaMultiplier = _query.Policy.GetDistanceMultiplier(
                    _data.SemanticWords,
                    area.SemanticOffset);
                double baseCost = _query.Policy.GetEntryPenalty(_data.SemanticWords, area.SemanticOffset);
                PendingLocalSearch = new LocalSearchRequest(
                    _query.Policy,
                    _snapshot,
                    areaIndex,
                    area.PolygonStart,
                    area.PolygonCount,
                    startPolygon,
                    goalPolygon,
                    start,
                    goal,
                    areaMultiplier,
                    baseCost);
                return SearchAdvanceStatus.NeedsLocalSearch;
            }

            private void CompletePendingLocal(LocalPathData path)
            {
                if (_pendingCacheStore &&
                    _data.IsAreaRevisionCurrent(_snapshot, _pendingArea))
                {
                    _data.Caches[_pendingArea].Store(_pendingCacheKey, path);
                }

                if (path != null)
                {
                    if (_pendingPurpose == LocalPurpose.Goal)
                    {
                        double candidate = _distances[_current] + path.Cost;
                        if (candidate < _bestGoalCost ||
                            (candidate.Equals(_bestGoalCost) && _current < _bestGoalNode))
                        {
                            _bestGoalCost = candidate;
                            _bestGoalNode = _current;
                            _bestGoalLocal = path;
                        }
                    }
                    else
                    {
                        ApplyPortal(_pendingPortal, path);
                    }
                }

                _pendingCacheStore = false;
                PendingLocalSearch = default;
            }

            private void ApplyPortal(int portalIndex, LocalPathData path)
            {
                DirectedPortal portal = _portals[portalIndex];
                double candidate = _distances[_current] + path.Cost +
                                   GetPortalCost(_data, _query.Policy, portal.Portal);
                if (!IsFinite(candidate))
                {
                    return;
                }

                int destination = portalIndex + 1;
                if (candidate < _distances[destination] ||
                    (candidate.Equals(_distances[destination]) && _current < _previous[destination]))
                {
                    _distances[destination] = candidate;
                    _previous[destination] = _current;
                    _incomingPortal[destination] = portalIndex;
                    _incomingLocal[destination] = path;
                }
            }

            private SearchAdvanceStatus Finish(
                out PathResultData result,
                out PathFailureReason failure)
            {
                if (_bestGoalNode < 0)
                {
                    result = null;
                    failure = _startArea == _goalArea
                        ? PathFailureReason.NoLocalRoute
                        : PathFailureReason.NoGlobalRoute;
                    return SearchAdvanceStatus.Failed;
                }

                result = BuildResult(
                    _data,
                    _query,
                    _portals,
                    _previous,
                    _incomingPortal,
                    _incomingLocal,
                    _bestGoalNode,
                    _bestGoalLocal,
                    _bestGoalCost,
                    _snapshot.AreaRevisions,
                    _snapshot.TopologyRevision,
                    _evaluatedAreas);
                failure = PathFailureReason.None;
                return SearchAdvanceStatus.Completed;
            }
        }

        internal static bool TrySolve(
            NavigationRuntimeData data,
            PathQuery query,
            ulong[] capturedAreaRevisions,
            ulong capturedTopologyRevision,
            out PathResultData result,
            out PathFailureReason failure)
        {
            result = null;
            failure = PathFailureReason.None;
            NavigationRuntimeSnapshot snapshot = data.CurrentSnapshot;
            if (!query.IsValid || query.Policy.RegistryFingerprint != data.RegistryFingerprint ||
                query.Policy.WordCount != data.SemanticWordCount)
            {
                failure = PathFailureReason.InvalidRequest;
                return false;
            }

            if (!data.AreaById.TryGetValue(query.Start.AreaId, out int startArea) ||
                !data.AreaById.TryGetValue(query.Goal.AreaId, out int goalArea) ||
                !TryFindPolygon(data, snapshot, query.Policy, startArea, query.Start.LocalPosition, out int startPolygon) ||
                !TryFindPolygon(data, snapshot, query.Policy, goalArea, query.Goal.LocalPosition, out int goalPolygon))
            {
                failure = PathFailureReason.LocationNotFound;
                return false;
            }

            var evaluatedAreas = new bool[data.Areas.Length];
            evaluatedAreas[startArea] = true;
            evaluatedAreas[goalArea] = true;
            DirectedPortal[] portals = BuildDirectedPortals(data, query.Policy, snapshot);
            int nodeCount = portals.Length + 1;
            var distances = new double[nodeCount];
            var visited = new bool[nodeCount];
            var previous = new int[nodeCount];
            var incomingPortal = new int[nodeCount];
            var incomingLocal = new LocalPathData[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                distances[index] = double.PositiveInfinity;
                previous[index] = -1;
                incomingPortal[index] = -1;
            }

            distances[0] = 0d;
            double bestGoalCost = double.PositiveInfinity;
            int bestGoalNode = -1;
            LocalPathData bestGoalLocal = null;

            for (int iteration = 0; iteration < nodeCount; iteration++)
            {
                int current = FindCheapestUnvisited(distances, visited);
                if (current < 0 || distances[current] > bestGoalCost)
                {
                    break;
                }

                visited[current] = true;
                GetNodeLocation(
                    portals,
                    current,
                    startArea,
                    startPolygon,
                    query.Start.LocalPosition,
                    out int currentArea,
                    out int currentPolygon,
                    out Vector3 currentPosition);
                evaluatedAreas[currentArea] = true;

                if (currentArea == goalArea &&
                    TryFindLocalPath(
                        data,
                        snapshot,
                        query.Policy,
                        currentArea,
                        currentPosition,
                        query.Goal.LocalPosition,
                        currentPolygon,
                        goalPolygon,
                        out LocalPathData localToGoal))
                {
                    double candidateGoal = distances[current] + localToGoal.Cost;
                    if (candidateGoal < bestGoalCost ||
                        (candidateGoal.Equals(bestGoalCost) && current < bestGoalNode))
                    {
                        bestGoalCost = candidateGoal;
                        bestGoalNode = current;
                        bestGoalLocal = localToGoal;
                    }
                }

                for (int portalIndex = 0; portalIndex < portals.Length; portalIndex++)
                {
                    DirectedPortal portal = portals[portalIndex];
                    if (portal.EntryArea != currentArea)
                    {
                        continue;
                    }

                    if (!TryGetPortalLocalPath(
                            data,
                            snapshot,
                            query.Policy,
                            current > 0 ? portals[current - 1].SideKey : -1,
                            portal.SideKey,
                            currentArea,
                            currentPolygon,
                            currentPosition,
                            portal,
                            out LocalPathData localToPortal))
                    {
                        continue;
                    }

                    double portalCost = GetPortalCost(data, query.Policy, portal.Portal);
                    double candidate = distances[current] + localToPortal.Cost + portalCost;
                    if (double.IsNaN(candidate) || double.IsInfinity(candidate))
                    {
                        continue;
                    }

                    int destinationNode = portalIndex + 1;
                    if (candidate < distances[destinationNode] ||
                        (candidate.Equals(distances[destinationNode]) && current < previous[destinationNode]))
                    {
                        distances[destinationNode] = candidate;
                        previous[destinationNode] = current;
                        incomingPortal[destinationNode] = portalIndex;
                        incomingLocal[destinationNode] = localToPortal;
                    }
                }
            }

            if (bestGoalNode < 0)
            {
                failure = startArea == goalArea ? PathFailureReason.NoLocalRoute : PathFailureReason.NoGlobalRoute;
                return false;
            }

            result = BuildResult(
                data,
                query,
                portals,
                previous,
                incomingPortal,
                incomingLocal,
                bestGoalNode,
                bestGoalLocal,
                bestGoalCost,
                capturedAreaRevisions,
                capturedTopologyRevision,
                evaluatedAreas);
            return true;
        }

        internal static LocationResolveStatus Resolve(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            Double3 universePosition,
            AreaId areaHint,
            out NavigationLocation location)
        {
            location = default;
            if (!universePosition.IsFinite || policy == null ||
                policy.RegistryFingerprint != data.RegistryFingerprint)
            {
                return LocationResolveStatus.NotFound;
            }

            int foundArea = -1;
            Vector3 foundPosition = default;
            if (areaHint.IsValid)
            {
                if (!data.AreaById.TryGetValue(areaHint, out int hintedArea))
                {
                    return LocationResolveStatus.NotFound;
                }

                Vector3 local = data.Areas[hintedArea].Frame.ToLocal(universePosition);
                if (!TryFindPolygon(data, data.CurrentSnapshot, policy, hintedArea, local, out _))
                {
                    return LocationResolveStatus.NotFound;
                }

                location = new NavigationLocation(areaHint, local);
                return LocationResolveStatus.Found;
            }

            for (int areaIndex = 0; areaIndex < data.Areas.Length; areaIndex++)
            {
                Vector3 local = data.Areas[areaIndex].Frame.ToLocal(universePosition);
                if (!TryFindPolygon(data, data.CurrentSnapshot, policy, areaIndex, local, out _))
                {
                    continue;
                }

                if (foundArea >= 0)
                {
                    return LocationResolveStatus.Ambiguous;
                }

                foundArea = areaIndex;
                foundPosition = local;
            }

            if (foundArea < 0)
            {
                return LocationResolveStatus.NotFound;
            }

            location = new NavigationLocation(data.Areas[foundArea].Id, foundPosition);
            return LocationResolveStatus.Found;
        }

        private static DirectedPortal[] BuildDirectedPortals(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            NavigationRuntimeSnapshot snapshot)
        {
            var result = new List<DirectedPortal>(data.Portals.Length * 2);
            for (int index = 0; index < data.Portals.Length; index++)
            {
                CompiledPortalRecord portal = data.Portals[index];
                if (!snapshot.PortalEnabled[index] ||
                    !policy.CanTraverse(
                        NavigationElementKind.Portal,
                        data.SemanticWords,
                        portal.SemanticOffset,
                        portal.RequiredCapabilityOffset))
                {
                    continue;
                }

                result.Add(new DirectedPortal(portal, index, false));
                if (portal.Direction == PortalDirection.Bidirectional)
                {
                    result.Add(new DirectedPortal(portal, index, true));
                }
            }

            return result.ToArray();
        }

        private static bool TryGetPortalLocalPath(
            NavigationRuntimeData data,
            NavigationRuntimeSnapshot snapshot,
            CompiledTraversalPolicy policy,
            int incomingPortalSide,
            int outgoingPortalSide,
            int areaIndex,
            int startPolygon,
            Vector3 start,
            DirectedPortal outgoing,
            out LocalPathData path)
        {
            if (incomingPortalSide >= 0)
            {
                var key = new LocalCacheKey(
                    incomingPortalSide,
                    outgoingPortalSide,
                    policy.Fingerprint,
                    snapshot.AreaRevisions[areaIndex]);
                if (data.Caches[areaIndex].TryGet(key, out path, out bool reachable))
                {
                    return reachable;
                }

                if (!TryFindLocalPath(
                        data,
                        snapshot,
                        policy,
                        areaIndex,
                        start,
                        outgoing.EntrySpan.Midpoint,
                        startPolygon,
                        outgoing.EntryPolygon,
                        out path))
                {
                    data.Caches[areaIndex].Store(key, null);
                    return false;
                }

                data.Caches[areaIndex].Store(key, path);
                return true;
            }

            return TryFindLocalPath(
                data,
                snapshot,
                policy,
                areaIndex,
                start,
                outgoing.EntrySpan.Midpoint,
                startPolygon,
                outgoing.EntryPolygon,
                out path);
        }

        private static bool TryFindLocalPath(
            NavigationRuntimeData data,
            NavigationRuntimeSnapshot snapshot,
            CompiledTraversalPolicy policy,
            int areaIndex,
            Vector3 start,
            Vector3 goal,
            int startPolygon,
            int goalPolygon,
            out LocalPathData result)
        {
            result = null;
            CompiledAreaRecord area = data.Areas[areaIndex];
            if (!policy.CanTraverse(
                    NavigationElementKind.Area,
                    data.SemanticWords,
                    area.SemanticOffset,
                    area.RequiredCapabilityOffset) ||
                !CanUsePolygon(data, snapshot, policy, startPolygon) ||
                !CanUsePolygon(data, snapshot, policy, goalPolygon))
            {
                return false;
            }

            double areaMultiplier = policy.GetDistanceMultiplier(data.SemanticWords, area.SemanticOffset);
            double baseCost = policy.GetEntryPenalty(data.SemanticWords, area.SemanticOffset);
            if (startPolygon == goalPolygon)
            {
                CompiledPolygonRecord polygon = data.Polygons[startPolygon];
                double cost = baseCost +
                              policy.GetEntryPenalty(data.SemanticWords, polygon.SemanticOffset) +
                              Vector3.Distance(start, goal) * areaMultiplier *
                              policy.GetDistanceMultiplier(data.SemanticWords, polygon.SemanticOffset);
                result = new LocalPathData(
                    areaIndex,
                    start,
                    goal,
                    cost,
                    new[] { startPolygon },
                    Array.Empty<NavigationCrossingSpan>(),
                    new[] { goal });
                return IsFinite(cost);
            }

            if (!data.PolygonSearch.TryFind(
                    policy,
                    areaIndex,
                    area.PolygonStart,
                    area.PolygonCount,
                    startPolygon,
                    goalPolygon,
                    new float3(start.x, start.y, start.z),
                    new float3(goal.x, goal.y, goal.z),
                    areaMultiplier,
                    baseCost,
                    out double total))
            {
                return false;
            }

            var corridor = new List<int>();
            var adjacencyPath = new List<int>();
            int cursor = goalPolygon;
            corridor.Add(cursor);
            while (cursor != startPolygon)
            {
                adjacencyPath.Add(data.PolygonSearch.GetPreviousAdjacency(cursor));
                cursor = data.PolygonSearch.GetPrevious(cursor);
                corridor.Add(cursor);
            }

            corridor.Reverse();
            adjacencyPath.Reverse();
            var crossings = new NavigationCrossingSpan[adjacencyPath.Count];
            for (int index = 0; index < crossings.Length; index++)
            {
                CompiledAdjacencyRecord adjacency = data.Adjacencies[adjacencyPath[index]];
                crossings[index] = new NavigationCrossingSpan(
                    area.Id,
                    data.Polygons[adjacency.FromPolygon].Id,
                    data.Polygons[adjacency.ToPolygon].Id,
                    adjacency.SpanStart,
                    adjacency.SpanEnd);
            }

            result = new LocalPathData(
                areaIndex,
                start,
                goal,
                total,
                corridor.ToArray(),
                crossings,
                BuildFunnel(data, corridor, crossings, start, goal));
            return true;
        }

        private static PathResultData BuildResult(
            NavigationRuntimeData data,
            PathQuery query,
            DirectedPortal[] portals,
            int[] previous,
            int[] incomingPortal,
            LocalPathData[] incomingLocal,
            int goalNode,
            LocalPathData finalLocal,
            double totalCost,
            ulong[] capturedAreaRevisions,
            ulong capturedTopologyRevision,
            bool[] evaluatedAreas)
        {
            var reverseSteps = new List<RouteStep>();
            int cursor = goalNode;
            while (cursor > 0)
            {
                int directedPortal = incomingPortal[cursor];
                reverseSteps.Add(new RouteStep(incomingLocal[cursor], portals[directedPortal]));
                cursor = previous[cursor];
            }

            reverseSteps.Reverse();
            var segments = new List<NavigationAreaSegment>(reverseSteps.Count + 1);
            var transitions = new List<NavigationPortalTransition>(reverseSteps.Count);
            var polygonIds = new List<PolygonId>();
            var crossings = new List<NavigationCrossingSpan>();
            var steering = new List<Vector3>();
            var revisions = new List<NavigationRevisionStamp>();
            var stampedAreas = new HashSet<int>();

            for (int index = 0; index < reverseSteps.Count; index++)
            {
                AppendLocalResult(
                    data,
                    query.Output,
                    reverseSteps[index].LocalPath,
                    capturedAreaRevisions,
                    segments,
                    polygonIds,
                    crossings,
                    steering,
                    revisions,
                    stampedAreas);

                DirectedPortal directed = reverseSteps[index].Portal;
                double portalCost = GetPortalCost(data, query.Policy, directed.Portal);
                transitions.Add(new NavigationPortalTransition(
                    directed.Portal.Id,
                    new NavigationLocation(data.Areas[directed.EntryArea].Id, directed.EntrySpan.Midpoint),
                    new NavigationLocation(data.Areas[directed.ExitArea].Id, directed.ExitSpan.Midpoint),
                    portalCost,
                    directed.Transform));
            }

            AppendLocalResult(
                data,
                query.Output,
                finalLocal,
                capturedAreaRevisions,
                segments,
                polygonIds,
                crossings,
                steering,
                revisions,
                stampedAreas);

            for (int areaIndex = 0; areaIndex < evaluatedAreas.Length; areaIndex++)
            {
                if (evaluatedAreas[areaIndex] && stampedAreas.Add(areaIndex))
                {
                    revisions.Add(new NavigationRevisionStamp(
                        data.Areas[areaIndex].Id,
                        capturedAreaRevisions[areaIndex]));
                }
            }

            return new PathResultData
            {
                TotalCost = totalCost,
                Areas = segments.ToArray(),
                Portals = transitions.ToArray(),
                Polygons = polygonIds.ToArray(),
                Crossings = crossings.ToArray(),
                SteeringTargets = steering.ToArray(),
                Revisions = revisions.ToArray(),
                TopologyRevision = capturedTopologyRevision
            };
        }

        private static void AppendLocalResult(
            NavigationRuntimeData data,
            PathOutputFlags output,
            LocalPathData local,
            ulong[] capturedAreaRevisions,
            List<NavigationAreaSegment> segments,
            List<PolygonId> polygonIds,
            List<NavigationCrossingSpan> crossings,
            List<Vector3> steering,
            List<NavigationRevisionStamp> revisions,
            HashSet<int> stampedAreas)
        {
            int polygonStart = polygonIds.Count;
            int crossingStart = crossings.Count;
            int steeringStart = steering.Count;
            bool includeCorridor = (output & PathOutputFlags.PolygonCorridor) != 0;
            bool includeSteering = (output & PathOutputFlags.SteeringTargets) != 0;
            if (includeCorridor)
            {
                for (int index = 0; index < local.Polygons.Length; index++)
                {
                    polygonIds.Add(data.Polygons[local.Polygons[index]].Id);
                }

                crossings.AddRange(local.Crossings);
            }

            if (includeSteering)
            {
                steering.AddRange(local.SteeringTargets);
            }

            AreaId areaId = data.Areas[local.AreaIndex].Id;
            ulong revision = capturedAreaRevisions[local.AreaIndex];
            segments.Add(new NavigationAreaSegment(
                areaId,
                new NavigationLocation(areaId, local.Start),
                new NavigationLocation(areaId, local.Goal),
                polygonStart,
                includeCorridor ? local.Polygons.Length : 0,
                crossingStart,
                includeCorridor ? local.Crossings.Length : 0,
                steeringStart,
                includeSteering ? local.SteeringTargets.Length : 0,
                revision,
                local.Cost));

            if (stampedAreas.Add(local.AreaIndex))
            {
                revisions.Add(new NavigationRevisionStamp(areaId, revision));
            }
        }

        private static Vector3[] BuildFunnel(
            NavigationRuntimeData data,
            List<int> corridor,
            NavigationCrossingSpan[] crossings,
            Vector3 start,
            Vector3 goal)
        {
            if (crossings.Length == 0)
            {
                return new[] { goal };
            }

            int portalCount = crossings.Length + 2;
            var left = new Vector3[portalCount];
            var right = new Vector3[portalCount];
            left[0] = right[0] = start;
            left[portalCount - 1] = right[portalCount - 1] = goal;
            for (int index = 0; index < crossings.Length; index++)
            {
                Vector3 from = data.Polygons[corridor[index]].Centroid;
                Vector3 to = data.Polygons[corridor[index + 1]].Centroid;
                Vector3 midpoint = crossings[index].Midpoint;
                Vector3 first = crossings[index].Start;
                Vector3 second = crossings[index].End;
                if (Cross2(to - from, first - midpoint) >= 0f)
                {
                    left[index + 1] = first;
                    right[index + 1] = second;
                }
                else
                {
                    left[index + 1] = second;
                    right[index + 1] = first;
                }
            }

            var targets = new List<Vector3>();
            Vector3 apex = start;
            Vector3 portalLeft = start;
            Vector3 portalRight = start;
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;
            for (int index = 1; index < portalCount; index++)
            {
                Vector3 nextLeft = left[index];
                Vector3 nextRight = right[index];
                if (TriangleArea2(apex, portalRight, nextRight) <= 0f)
                {
                    if (SamePoint(apex, portalRight) || TriangleArea2(apex, portalLeft, nextRight) > 0f)
                    {
                        portalRight = nextRight;
                        rightIndex = index;
                    }
                    else
                    {
                        targets.Add(portalLeft);
                        apex = portalLeft;
                        apexIndex = leftIndex;
                        portalLeft = apex;
                        portalRight = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        index = apexIndex;
                        continue;
                    }
                }

                if (TriangleArea2(apex, portalLeft, nextLeft) >= 0f)
                {
                    if (SamePoint(apex, portalLeft) || TriangleArea2(apex, portalRight, nextLeft) < 0f)
                    {
                        portalLeft = nextLeft;
                        leftIndex = index;
                    }
                    else
                    {
                        targets.Add(portalRight);
                        apex = portalRight;
                        apexIndex = rightIndex;
                        portalLeft = apex;
                        portalRight = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        index = apexIndex;
                    }
                }
            }

            if (targets.Count == 0 || !SamePoint(targets[targets.Count - 1], goal))
            {
                targets.Add(goal);
            }

            return targets.ToArray();
        }

        private static bool TryFindPolygon(
            NavigationRuntimeData data,
            NavigationRuntimeSnapshot snapshot,
            CompiledTraversalPolicy policy,
            int areaIndex,
            Vector3 position,
            out int polygonIndex)
        {
            CompiledAreaRecord area = data.Areas[areaIndex];
            if (!policy.CanTraverse(
                    NavigationElementKind.Area,
                    data.SemanticWords,
                    area.SemanticOffset,
                    area.RequiredCapabilityOffset))
            {
                polygonIndex = -1;
                return false;
            }

            int end = area.PolygonStart + area.PolygonCount;
            for (int index = area.PolygonStart; index < end; index++)
            {
                if (CanUsePolygon(data, snapshot, policy, index) && PointInPolygon(data, index, position))
                {
                    polygonIndex = index;
                    return true;
                }
            }

            polygonIndex = -1;
            return false;
        }

        private static bool PointInPolygon(NavigationRuntimeData data, int polygonIndex, Vector3 point)
        {
            CompiledPolygonRecord polygon = data.Polygons[polygonIndex];
            Vector3 planePoint = data.Vertices[polygon.VertexStart].Position;
            if (Math.Abs(Vector3.Dot(polygon.Normal, point - planePoint)) > 0.1f)
            {
                return false;
            }

            int end = polygon.VertexStart + polygon.VertexCount;
            for (int vertex = polygon.VertexStart; vertex < end; vertex++)
            {
                Vector3 first = data.Vertices[vertex].Position;
                Vector3 second = data.Vertices[
                    vertex + 1 < end ? vertex + 1 : polygon.VertexStart].Position;
                float side = Vector3.Dot(Vector3.Cross(second - first, point - first), polygon.Normal);
                if (side < -0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanUsePolygon(
            NavigationRuntimeData data,
            NavigationRuntimeSnapshot snapshot,
            CompiledTraversalPolicy policy,
            int polygonIndex)
        {
            if ((uint)polygonIndex >= (uint)data.Polygons.Length || !snapshot.PolygonEnabled[polygonIndex])
            {
                return false;
            }

            CompiledPolygonRecord polygon = data.Polygons[polygonIndex];
            return policy.CanTraverse(
                NavigationElementKind.Polygon,
                data.SemanticWords,
                polygon.SemanticOffset,
                polygon.RequiredCapabilityOffset);
        }

        private static int FindCheapestUnvisited(double[] distance, bool[] visited)
        {
            int best = -1;
            double bestCost = double.PositiveInfinity;
            for (int index = 0; index < distance.Length; index++)
            {
                if (!visited[index] &&
                    (distance[index] < bestCost ||
                     (distance[index].Equals(bestCost) && index < best)))
                {
                    best = index;
                    bestCost = distance[index];
                }
            }

            return double.IsInfinity(bestCost) ? -1 : best;
        }

        private static void GetNodeLocation(
            DirectedPortal[] portals,
            int node,
            int startArea,
            int startPolygon,
            Vector3 start,
            out int area,
            out int polygon,
            out Vector3 position)
        {
            if (node == 0)
            {
                area = startArea;
                polygon = startPolygon;
                position = start;
                return;
            }

            DirectedPortal portal = portals[node - 1];
            area = portal.ExitArea;
            polygon = portal.ExitPolygon;
            position = portal.ExitSpan.Midpoint;
        }

        private static double GetPortalCost(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            CompiledPortalRecord portal)
        {
            return portal.BaseCost * policy.GetDistanceMultiplier(data.SemanticWords, portal.SemanticOffset) +
                   policy.GetEntryPenalty(data.SemanticWords, portal.SemanticOffset);
        }

        private static float Cross2(Vector3 first, Vector3 second)
        {
            return (first.x * second.z) - (first.z * second.x);
        }

        private static float TriangleArea2(Vector3 first, Vector3 second, Vector3 third)
        {
            return ((second.x - first.x) * (third.z - first.z)) -
                   ((second.z - first.z) * (third.x - first.x));
        }

        private static bool SamePoint(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z) < 1e-8f;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
