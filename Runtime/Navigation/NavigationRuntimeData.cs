using System;
using System.Collections.Generic;
using Unity.Collections;

namespace NotRealGames.Areafinder
{
    internal sealed class NavigationRuntimeData
    {
        private readonly NavigationSnapshotTracker _snapshotTracker = new NavigationSnapshotTracker();

        internal NavigationRuntimeData(
            NavigationBakeAsset bake,
            int cacheCapacityPerArea,
            int maxConcurrentSearches = 1)
        {
            if (bake == null || !bake.IsUsable)
            {
                throw new ArgumentException("A usable navigation bake is required.", nameof(bake));
            }

            if (cacheCapacityPerArea < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheCapacityPerArea));
            }

            RegistryFingerprint = bake.SemanticRegistryFingerprint;
            SemanticWordCount = bake.SemanticWordCount;
            Areas = Clone(bake.RawAreas);
            Polygons = Clone(bake.RawPolygons);
            Vertices = Clone(bake.RawVertices);
            Adjacencies = Clone(bake.RawAdjacencies);
            Portals = Clone(bake.RawPortals);
            SemanticWords = Clone(bake.RawSemanticWords);
            var areaRevisions = new ulong[Areas.Length];
            var polygonEnabled = new bool[Polygons.Length];
            var adjacencyEnabled = new bool[Adjacencies.Length];
            var portalEnabled = new bool[Portals.Length];
            Caches = new AreaPathCache[Areas.Length];

            for (int index = 0; index < Areas.Length; index++)
            {
                if (!Areas[index].Id.IsValid || AreaById.ContainsKey(Areas[index].Id))
                {
                    throw new ArgumentException("The bake contains invalid or duplicate Area identities.", nameof(bake));
                }

                AreaById.Add(Areas[index].Id, index);
                areaRevisions[index] = 1UL;
                Caches[index] = new AreaPathCache(cacheCapacityPerArea);
            }

            for (int index = 0; index < Polygons.Length; index++)
            {
                CompiledPolygonRecord polygon = Polygons[index];
                if (!polygon.Id.IsValid || PolygonById.ContainsKey(polygon.Id) ||
                    (uint)polygon.AreaIndex >= (uint)Areas.Length)
                {
                    throw new ArgumentException("The bake contains invalid polygon data.", nameof(bake));
                }

                PolygonById.Add(polygon.Id, index);
                polygonEnabled[index] = polygon.Enabled;
            }

            for (int index = 0; index < Adjacencies.Length; index++)
            {
                CompiledAdjacencyRecord adjacency = Adjacencies[index];
                if ((uint)adjacency.FromPolygon >= (uint)Polygons.Length ||
                    (uint)adjacency.ToPolygon >= (uint)Polygons.Length)
                {
                    throw new ArgumentException("The bake contains invalid adjacency data.", nameof(bake));
                }

                adjacencyEnabled[index] = true;
                var pair = new RuntimeEdgePair(adjacency.FromPolygon, adjacency.ToPolygon);
                if (!AdjacencyByPolygonPair.TryGetValue(pair, out List<int> indices))
                {
                    indices = new List<int>();
                    AdjacencyByPolygonPair.Add(pair, indices);
                }

                indices.Add(index);
            }

            for (int index = 0; index < Portals.Length; index++)
            {
                CompiledPortalRecord portal = Portals[index];
                if (!portal.Id.IsValid || PortalById.ContainsKey(portal.Id) ||
                    (uint)portal.SourceArea >= (uint)Areas.Length ||
                    (uint)portal.DestinationArea >= (uint)Areas.Length)
                {
                    throw new ArgumentException("The bake contains invalid Portal data.", nameof(bake));
                }

                PortalById.Add(portal.Id, index);
                portalEnabled[index] = portal.Enabled;
            }

            ValidateSemanticStorage();
            CurrentSnapshot = new NavigationRuntimeSnapshot(
                _snapshotTracker,
                polygonEnabled,
                adjacencyEnabled,
                portalEnabled,
                areaRevisions,
                1UL);
            PolygonSearch = new BurstPolygonSearch(this, maxConcurrentSearches);
        }

        internal ulong RegistryFingerprint { get; }
        internal int SemanticWordCount { get; }
        internal CompiledAreaRecord[] Areas { get; }
        internal CompiledPolygonRecord[] Polygons { get; }
        internal CompiledVertexRecord[] Vertices { get; }
        internal CompiledAdjacencyRecord[] Adjacencies { get; }
        internal CompiledPortalRecord[] Portals { get; }
        internal ulong[] SemanticWords { get; }
        internal ulong[] AreaRevisions => CurrentSnapshot.AreaRevisions;
        internal bool[] PolygonEnabled => CurrentSnapshot.PolygonEnabled;
        internal bool[] AdjacencyEnabled => CurrentSnapshot.AdjacencyEnabled;
        internal bool[] PortalEnabled => CurrentSnapshot.PortalEnabled;
        internal AreaPathCache[] Caches { get; }
        internal Dictionary<AreaId, int> AreaById { get; } = new Dictionary<AreaId, int>();
        internal Dictionary<PolygonId, int> PolygonById { get; } = new Dictionary<PolygonId, int>();
        internal Dictionary<PortalId, int> PortalById { get; } = new Dictionary<PortalId, int>();
        internal Dictionary<RuntimeEdgePair, List<int>> AdjacencyByPolygonPair { get; } =
            new Dictionary<RuntimeEdgePair, List<int>>();
        internal ulong TopologyRevision => CurrentSnapshot.TopologyRevision;
        internal int ActiveSnapshotCount => _snapshotTracker.ActiveCount;
        internal BurstPolygonSearch PolygonSearch { get; }
        internal NavigationRuntimeSnapshot CurrentSnapshot { get; private set; }

        internal void SetPolygonEnabled(int polygonIndex, bool enabled)
        {
            bool[] polygons = (bool[])PolygonEnabled.Clone();
            polygons[polygonIndex] = enabled;
            ReplaceSnapshot(new NavigationRuntimeSnapshot(
                _snapshotTracker,
                polygons,
                (bool[])AdjacencyEnabled.Clone(),
                (bool[])PortalEnabled.Clone(),
                (ulong[])AreaRevisions.Clone(),
                TopologyRevision));
        }

        internal void SetAdjacencyEnabled(int adjacencyIndex, bool enabled)
        {
            bool[] adjacencies = (bool[])AdjacencyEnabled.Clone();
            adjacencies[adjacencyIndex] = enabled;
            ReplaceSnapshot(new NavigationRuntimeSnapshot(
                _snapshotTracker,
                (bool[])PolygonEnabled.Clone(),
                adjacencies,
                (bool[])PortalEnabled.Clone(),
                (ulong[])AreaRevisions.Clone(),
                TopologyRevision));
        }

        internal void Dispose()
        {
            PolygonSearch.Dispose();
            CurrentSnapshot.Release();
            CurrentSnapshot = null;
        }

        internal void AdvanceAreaRevision(int areaIndex)
        {
            ulong[] revisions = (ulong[])AreaRevisions.Clone();
            unchecked
            {
                revisions[areaIndex]++;
                if (revisions[areaIndex] == 0UL)
                {
                    revisions[areaIndex] = 1UL;
                }
            }

            Caches[areaIndex].Clear();
            ReplaceSnapshot(new NavigationRuntimeSnapshot(
                _snapshotTracker,
                (bool[])PolygonEnabled.Clone(),
                (bool[])AdjacencyEnabled.Clone(),
                (bool[])PortalEnabled.Clone(),
                revisions,
                TopologyRevision));
        }

        internal void AdvanceTopologyRevision()
        {
            ulong topologyRevision = TopologyRevision;
            unchecked
            {
                topologyRevision++;
                if (topologyRevision == 0UL)
                {
                    topologyRevision = 1UL;
                }
            }

            ReplaceSnapshot(new NavigationRuntimeSnapshot(
                _snapshotTracker,
                (bool[])PolygonEnabled.Clone(),
                (bool[])AdjacencyEnabled.Clone(),
                (bool[])PortalEnabled.Clone(),
                (ulong[])AreaRevisions.Clone(),
                topologyRevision));
        }

        internal NavigationRuntimeSnapshot AcquireCurrentSnapshot()
        {
            CurrentSnapshot.Acquire();
            return CurrentSnapshot;
        }

        internal bool IsAreaRevisionCurrent(NavigationRuntimeSnapshot snapshot, int areaIndex)
        {
            return (uint)areaIndex < (uint)AreaRevisions.Length &&
                   snapshot.AreaRevisions[areaIndex] == AreaRevisions[areaIndex];
        }

        internal void ApplySnapshot(
            bool[] polygonEnabled,
            bool[] adjacencyEnabled,
            bool[] portalEnabled,
            bool[] affectedAreas,
            bool advanceTopology)
        {
            if (polygonEnabled == null || polygonEnabled.Length != Polygons.Length ||
                adjacencyEnabled == null || adjacencyEnabled.Length != Adjacencies.Length ||
                portalEnabled == null || portalEnabled.Length != Portals.Length ||
                affectedAreas == null || affectedAreas.Length != Areas.Length)
            {
                throw new ArgumentException("The runtime snapshot shape does not match the bake.");
            }

            ulong[] revisions = (ulong[])AreaRevisions.Clone();
            for (int areaIndex = 0; areaIndex < affectedAreas.Length; areaIndex++)
            {
                if (!affectedAreas[areaIndex])
                {
                    continue;
                }

                revisions[areaIndex] = IncrementRevision(revisions[areaIndex]);
                Caches[areaIndex].Clear();
            }

            ulong topologyRevision = advanceTopology
                ? IncrementRevision(TopologyRevision)
                : TopologyRevision;
            ReplaceSnapshot(new NavigationRuntimeSnapshot(
                _snapshotTracker,
                polygonEnabled,
                adjacencyEnabled,
                portalEnabled,
                revisions,
                topologyRevision));
        }

        internal void ReplaceSnapshot(NavigationRuntimeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            NavigationRuntimeSnapshot previous = CurrentSnapshot;
            CurrentSnapshot = snapshot;
            previous.Release();
        }

        private static ulong IncrementRevision(ulong revision)
        {
            unchecked
            {
                revision++;
                return revision == 0UL ? 1UL : revision;
            }
        }

        private void ValidateSemanticStorage()
        {
            if (SemanticWordCount < 0)
            {
                throw new ArgumentException("The bake has an invalid semantic word count.");
            }

            for (int index = 0; index < Areas.Length; index++)
            {
                ValidateMaskOffset(Areas[index].SemanticOffset);
                ValidateMaskOffset(Areas[index].RequiredCapabilityOffset);
            }

            for (int index = 0; index < Polygons.Length; index++)
            {
                ValidateMaskOffset(Polygons[index].SemanticOffset);
                ValidateMaskOffset(Polygons[index].RequiredCapabilityOffset);
            }

            for (int index = 0; index < Portals.Length; index++)
            {
                ValidateMaskOffset(Portals[index].SemanticOffset);
                ValidateMaskOffset(Portals[index].RequiredCapabilityOffset);
            }
        }

        private void ValidateMaskOffset(int offset)
        {
            if (offset < 0 || offset + SemanticWordCount > SemanticWords.Length)
            {
                throw new ArgumentException("The bake contains an invalid semantic mask offset.");
            }
        }

        private static T[] Clone<T>(T[] source)
        {
            return source == null ? Array.Empty<T>() : (T[])source.Clone();
        }
    }

    internal sealed class NavigationSnapshotTracker
    {
        internal int ActiveCount { get; private set; }

        internal void Register() => ActiveCount++;

        internal void Unregister()
        {
            if (ActiveCount <= 0)
            {
                throw new InvalidOperationException("The navigation snapshot tracker underflowed.");
            }

            ActiveCount--;
        }
    }

    internal sealed class NavigationRuntimeSnapshot
    {
        private readonly NavigationSnapshotTracker _tracker;
        private int _references = 1;

        internal NavigationRuntimeSnapshot(
            NavigationSnapshotTracker tracker,
            bool[] polygonEnabled,
            bool[] adjacencyEnabled,
            bool[] portalEnabled,
            ulong[] areaRevisions,
            ulong topologyRevision)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            PolygonEnabled = polygonEnabled ?? throw new ArgumentNullException(nameof(polygonEnabled));
            AdjacencyEnabled = adjacencyEnabled ?? throw new ArgumentNullException(nameof(adjacencyEnabled));
            PortalEnabled = portalEnabled ?? throw new ArgumentNullException(nameof(portalEnabled));
            AreaRevisions = areaRevisions ?? throw new ArgumentNullException(nameof(areaRevisions));
            TopologyRevision = topologyRevision;
            NativePolygonEnabled = ToNative(PolygonEnabled);
            NativeAdjacencyEnabled = ToNative(AdjacencyEnabled);
            _tracker.Register();
        }

        internal int ReferenceCount => _references;

        internal bool[] PolygonEnabled { get; }
        internal bool[] AdjacencyEnabled { get; }
        internal bool[] PortalEnabled { get; }
        internal ulong[] AreaRevisions { get; }
        internal ulong TopologyRevision { get; }
        internal NativeArray<byte> NativePolygonEnabled { get; private set; }
        internal NativeArray<byte> NativeAdjacencyEnabled { get; private set; }

        internal void Acquire()
        {
            if (_references <= 0)
            {
                throw new ObjectDisposedException(nameof(NavigationRuntimeSnapshot));
            }

            _references++;
        }

        internal void Release()
        {
            if (_references <= 0)
            {
                throw new ObjectDisposedException(nameof(NavigationRuntimeSnapshot));
            }

            if (--_references != 0)
            {
                return;
            }

            if (NativePolygonEnabled.IsCreated)
            {
                NativePolygonEnabled.Dispose();
            }

            if (NativeAdjacencyEnabled.IsCreated)
            {
                NativeAdjacencyEnabled.Dispose();
            }

            _tracker.Unregister();
        }

        private static NativeArray<byte> ToNative(bool[] source)
        {
            var result = new NativeArray<byte>(source.Length, Allocator.Persistent);
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = source[index] ? (byte)1 : (byte)0;
            }

            return result;
        }
    }

    internal readonly struct RuntimeEdgePair : IEquatable<RuntimeEdgePair>
    {
        internal RuntimeEdgePair(int fromPolygon, int toPolygon)
        {
            FromPolygon = fromPolygon;
            ToPolygon = toPolygon;
        }

        internal int FromPolygon { get; }
        internal int ToPolygon { get; }
        public bool Equals(RuntimeEdgePair other) =>
            FromPolygon == other.FromPolygon && ToPolygon == other.ToPolygon;
        public override bool Equals(object obj) => obj is RuntimeEdgePair other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(FromPolygon, ToPolygon);
    }
}
