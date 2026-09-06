using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public readonly struct NavigationLocation : IEquatable<NavigationLocation>
    {
        public NavigationLocation(AreaId areaId, Vector3 localPosition)
        {
            AreaId = areaId;
            LocalPosition = localPosition;
        }

        public AreaId AreaId { get; }
        public Vector3 LocalPosition { get; }
        public bool IsValid => AreaId.IsValid && IsFinite(LocalPosition);

        public bool Equals(NavigationLocation other)
        {
            return AreaId == other.AreaId && LocalPosition == other.LocalPosition;
        }

        public override bool Equals(object obj) => obj is NavigationLocation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(AreaId, LocalPosition);
        public static bool operator ==(NavigationLocation left, NavigationLocation right) => left.Equals(right);
        public static bool operator !=(NavigationLocation left, NavigationLocation right) => !left.Equals(right);

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    public readonly struct PathQuery
    {
        public PathQuery(
            NavigationLocation start,
            NavigationLocation goal,
            CompiledTraversalPolicy policy,
            PathPriority priority = PathPriority.Normal,
            PathOutputFlags output = PathOutputFlags.Default)
        {
            Start = start;
            Goal = goal;
            Policy = policy;
            Priority = priority;
            Output = output;
        }

        public NavigationLocation Start { get; }
        public NavigationLocation Goal { get; }
        public CompiledTraversalPolicy Policy { get; }
        public PathPriority Priority { get; }
        public PathOutputFlags Output { get; }
        public bool IsValid => Start.IsValid && Goal.IsValid && Policy != null &&
                               Priority >= PathPriority.Low && Priority <= PathPriority.High;
    }

    public readonly struct NavigationCrossingSpan
    {
        internal NavigationCrossingSpan(
            AreaId areaId,
            PolygonId fromPolygon,
            PolygonId toPolygon,
            Vector3 start,
            Vector3 end)
        {
            AreaId = areaId;
            FromPolygon = fromPolygon;
            ToPolygon = toPolygon;
            Start = start;
            End = end;
        }

        public AreaId AreaId { get; }
        public PolygonId FromPolygon { get; }
        public PolygonId ToPolygon { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Vector3 Midpoint => (Start + End) * 0.5f;
    }

    public readonly struct NavigationAreaSegment
    {
        internal NavigationAreaSegment(
            AreaId areaId,
            NavigationLocation start,
            NavigationLocation end,
            int polygonStart,
            int polygonCount,
            int crossingStart,
            int crossingCount,
            int steeringStart,
            int steeringCount,
            ulong revision,
            double cost)
        {
            AreaId = areaId;
            Start = start;
            End = end;
            PolygonStart = polygonStart;
            PolygonCount = polygonCount;
            CrossingStart = crossingStart;
            CrossingCount = crossingCount;
            SteeringStart = steeringStart;
            SteeringCount = steeringCount;
            Revision = revision;
            Cost = cost;
        }

        public AreaId AreaId { get; }
        public NavigationLocation Start { get; }
        public NavigationLocation End { get; }
        public int PolygonStart { get; }
        public int PolygonCount { get; }
        public int CrossingStart { get; }
        public int CrossingCount { get; }
        public int SteeringStart { get; }
        public int SteeringCount { get; }
        public ulong Revision { get; }
        public double Cost { get; }
    }

    public readonly struct NavigationPortalTransition
    {
        internal NavigationPortalTransition(
            PortalId portalId,
            NavigationLocation entry,
            NavigationLocation exit,
            double cost,
            PortalTransform entryToExit)
        {
            PortalId = portalId;
            Entry = entry;
            Exit = exit;
            Cost = cost;
            EntryToExit = entryToExit;
        }

        public PortalId PortalId { get; }
        public NavigationLocation Entry { get; }
        public NavigationLocation Exit { get; }
        public double Cost { get; }
        public PortalTransform EntryToExit { get; }
    }

    public readonly struct NavigationRevisionStamp
    {
        internal NavigationRevisionStamp(AreaId areaId, ulong revision)
        {
            AreaId = areaId;
            Revision = revision;
        }

        public AreaId AreaId { get; }
        public ulong Revision { get; }
    }

    public sealed class NavigationPath
    {
        internal NavigationPath(
            double totalCost,
            NavigationAreaSegment[] areas,
            NavigationPortalTransition[] portals,
            PolygonId[] polygons,
            NavigationCrossingSpan[] crossings,
            Vector3[] steeringTargets,
            NavigationRevisionStamp[] revisions,
            ulong topologyRevision)
        {
            TotalCost = totalCost;
            Areas = Array.AsReadOnly(areas);
            PortalTransitions = Array.AsReadOnly(portals);
            PolygonCorridor = Array.AsReadOnly(polygons);
            CrossingSpans = Array.AsReadOnly(crossings);
            SteeringTargets = Array.AsReadOnly(steeringTargets);
            Revisions = Array.AsReadOnly(revisions);
            TopologyRevision = topologyRevision;
        }

        public double TotalCost { get; }
        public IReadOnlyList<NavigationAreaSegment> Areas { get; }
        public IReadOnlyList<NavigationPortalTransition> PortalTransitions { get; }
        public IReadOnlyList<PolygonId> PolygonCorridor { get; }
        public IReadOnlyList<NavigationCrossingSpan> CrossingSpans { get; }
        public IReadOnlyList<Vector3> SteeringTargets { get; }
        public IReadOnlyList<NavigationRevisionStamp> Revisions { get; }
        public ulong TopologyRevision { get; }
    }

    public readonly struct NavigationPathView
    {
        private readonly NavigationWorld _world;
        private readonly PathRequestHandle _handle;

        internal NavigationPathView(NavigationWorld world, PathRequestHandle handle)
        {
            _world = world;
            _handle = handle;
        }

        internal NavigationWorld World => _world;
        internal PathRequestHandle Handle => _handle;

        public bool IsValid => _world != null && _world.IsPathViewValid(_handle);
        public double TotalCost => _world.GetPathTotalCost(_handle);
        public int AreaCount => _world.GetPathAreaCount(_handle);
        public int PortalTransitionCount => _world.GetPathPortalCount(_handle);
        public int PolygonCount => _world.GetPathPolygonCount(_handle);
        public int CrossingSpanCount => _world.GetPathCrossingCount(_handle);
        public int SteeringTargetCount => _world.GetPathSteeringCount(_handle);
        public int RevisionCount => _world.GetPathRevisionCount(_handle);
        public ulong TopologyRevision => _world.GetPathTopologyRevision(_handle);

        public NavigationAreaSegment GetArea(int index) => _world.GetPathArea(_handle, index);
        public NavigationPortalTransition GetPortalTransition(int index) =>
            _world.GetPathPortal(_handle, index);
        public PolygonId GetPolygon(int index) => _world.GetPathPolygon(_handle, index);
        public NavigationCrossingSpan GetCrossingSpan(int index) =>
            _world.GetPathCrossing(_handle, index);
        public Vector3 GetSteeringTarget(int index) => _world.GetPathSteering(_handle, index);
        public NavigationRevisionStamp GetRevision(int index) => _world.GetPathRevision(_handle, index);
        public NavigationPath ToManagedCopy() => _world.CopyPath(_handle);
    }
}
