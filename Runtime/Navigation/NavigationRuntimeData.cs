using System;
using System.Collections.Generic;

namespace NotRealGames.Areafinder
{
    internal sealed class NavigationRuntimeData
    {
        internal NavigationRuntimeData(NavigationBakeAsset bake, int cacheCapacityPerArea)
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
            AreaRevisions = new ulong[Areas.Length];
            PolygonEnabled = new bool[Polygons.Length];
            AdjacencyEnabled = new bool[Adjacencies.Length];
            PortalEnabled = new bool[Portals.Length];
            Caches = new AreaPathCache[Areas.Length];

            for (int index = 0; index < Areas.Length; index++)
            {
                if (!Areas[index].Id.IsValid || AreaById.ContainsKey(Areas[index].Id))
                {
                    throw new ArgumentException("The bake contains invalid or duplicate Area identities.", nameof(bake));
                }

                AreaById.Add(Areas[index].Id, index);
                AreaRevisions[index] = 1UL;
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
                PolygonEnabled[index] = polygon.Enabled;
            }

            for (int index = 0; index < Adjacencies.Length; index++)
            {
                CompiledAdjacencyRecord adjacency = Adjacencies[index];
                if ((uint)adjacency.FromPolygon >= (uint)Polygons.Length ||
                    (uint)adjacency.ToPolygon >= (uint)Polygons.Length)
                {
                    throw new ArgumentException("The bake contains invalid adjacency data.", nameof(bake));
                }

                AdjacencyEnabled[index] = true;
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
                PortalEnabled[index] = portal.Enabled;
            }

            ValidateSemanticStorage();
            PolygonSearch = new BurstPolygonSearch(this);
        }

        internal ulong RegistryFingerprint { get; }
        internal int SemanticWordCount { get; }
        internal CompiledAreaRecord[] Areas { get; }
        internal CompiledPolygonRecord[] Polygons { get; }
        internal CompiledVertexRecord[] Vertices { get; }
        internal CompiledAdjacencyRecord[] Adjacencies { get; }
        internal CompiledPortalRecord[] Portals { get; }
        internal ulong[] SemanticWords { get; }
        internal ulong[] AreaRevisions { get; }
        internal bool[] PolygonEnabled { get; }
        internal bool[] AdjacencyEnabled { get; }
        internal bool[] PortalEnabled { get; }
        internal AreaPathCache[] Caches { get; }
        internal Dictionary<AreaId, int> AreaById { get; } = new Dictionary<AreaId, int>();
        internal Dictionary<PolygonId, int> PolygonById { get; } = new Dictionary<PolygonId, int>();
        internal Dictionary<PortalId, int> PortalById { get; } = new Dictionary<PortalId, int>();
        internal Dictionary<RuntimeEdgePair, List<int>> AdjacencyByPolygonPair { get; } =
            new Dictionary<RuntimeEdgePair, List<int>>();
        internal ulong TopologyRevision { get; set; } = 1UL;
        internal BurstPolygonSearch PolygonSearch { get; }

        internal void SetPolygonEnabled(int polygonIndex, bool enabled)
        {
            PolygonEnabled[polygonIndex] = enabled;
            PolygonSearch.SetPolygonEnabled(polygonIndex, enabled);
        }

        internal void SetAdjacencyEnabled(int adjacencyIndex, bool enabled)
        {
            AdjacencyEnabled[adjacencyIndex] = enabled;
            PolygonSearch.SetAdjacencyEnabled(adjacencyIndex, enabled);
        }

        internal void Dispose()
        {
            PolygonSearch.Dispose();
        }

        internal void AdvanceAreaRevision(int areaIndex)
        {
            unchecked
            {
                AreaRevisions[areaIndex]++;
                if (AreaRevisions[areaIndex] == 0UL)
                {
                    AreaRevisions[areaIndex] = 1UL;
                }
            }

            Caches[areaIndex].Clear();
        }

        internal void AdvanceTopologyRevision()
        {
            unchecked
            {
                TopologyRevision++;
                if (TopologyRevision == 0UL)
                {
                    TopologyRevision = 1UL;
                }
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
