using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace NotRealGames.Areafinder
{
    internal sealed class BurstPolygonSearch : IDisposable
    {
        private readonly NavigationRuntimeData _data;
        private readonly NativeArray<float3> _centroids;
        private readonly NativeArray<int> _polygonArea;
        private readonly NativeArray<int> _adjacencyStarts;
        private readonly NativeArray<int> _adjacencyCounts;
        private readonly NativeArray<int> _semanticOffsets;
        private readonly NativeArray<int> _capabilityOffsets;
        private readonly NativeArray<int> _adjacencyTargets;
        private readonly NativeArray<ulong> _semanticWords;
        private readonly ScratchLane _synchronousLane;
        private readonly ScratchLane[] _lanes;
        private readonly Dictionary<ulong, NativePolicyData> _policies =
            new Dictionary<ulong, NativePolicyData>();
        private bool _disposed;

        internal BurstPolygonSearch(NavigationRuntimeData data, int maxConcurrentSearches)
        {
            if (maxConcurrentSearches < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentSearches));
            }

            _data = data ?? throw new ArgumentNullException(nameof(data));
            int polygonCount = data.Polygons.Length;
            _centroids = new NativeArray<float3>(polygonCount, Allocator.Persistent);
            _polygonArea = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _adjacencyStarts = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _adjacencyCounts = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _semanticOffsets = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _capabilityOffsets = new NativeArray<int>(polygonCount, Allocator.Persistent);
            for (int index = 0; index < polygonCount; index++)
            {
                CompiledPolygonRecord polygon = data.Polygons[index];
                _centroids[index] = new float3(
                    polygon.Centroid.x,
                    polygon.Centroid.y,
                    polygon.Centroid.z);
                _polygonArea[index] = polygon.AreaIndex;
                _adjacencyStarts[index] = polygon.AdjacencyStart;
                _adjacencyCounts[index] = polygon.AdjacencyCount;
                _semanticOffsets[index] = polygon.SemanticOffset;
                _capabilityOffsets[index] = polygon.RequiredCapabilityOffset;
            }

            _adjacencyTargets = new NativeArray<int>(data.Adjacencies.Length, Allocator.Persistent);
            for (int index = 0; index < data.Adjacencies.Length; index++)
            {
                _adjacencyTargets[index] = data.Adjacencies[index].ToPolygon;
            }

            _semanticWords = new NativeArray<ulong>(data.SemanticWords, Allocator.Persistent);
            _synchronousLane = new ScratchLane(polygonCount);
            _lanes = new ScratchLane[maxConcurrentSearches];
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index] = new ScratchLane(polygonCount);
            }
        }

        internal bool TryFind(
            CompiledTraversalPolicy policy,
            int areaIndex,
            int areaPolygonStart,
            int areaPolygonCount,
            int startPolygon,
            int goalPolygon,
            float3 start,
            float3 goal,
            double areaMultiplier,
            double baseCost,
            out double totalCost)
        {
            ThrowIfDisposed();
            _synchronousLane.Handle = Schedule(
                _synchronousLane,
                policy,
                _data.CurrentSnapshot,
                areaIndex,
                areaPolygonStart,
                areaPolygonCount,
                startPolygon,
                goalPolygon,
                start,
                goal,
                areaMultiplier,
                baseCost);
            _synchronousLane.Handle.Complete();
            totalCost = _synchronousLane.ResultCost[0];
            return _synchronousLane.ResultStatus[0] == 1;
        }

        internal int GetPrevious(int polygon) => _synchronousLane.Previous[polygon];
        internal int GetPreviousAdjacency(int polygon) => _synchronousLane.PreviousAdjacency[polygon];
        internal int InUseLaneCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _lanes.Length; index++)
                {
                    if (_lanes[index].InUse)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal bool TrySchedule(LocalSearchRequest request, out int laneIndex)
        {
            ThrowIfDisposed();
            for (int index = 0; index < _lanes.Length; index++)
            {
                ScratchLane lane = _lanes[index];
                if (lane.InUse)
                {
                    continue;
                }

                lane.InUse = true;
                try
                {
                    lane.Handle = Schedule(
                        lane,
                        request.Policy,
                        request.Snapshot,
                        request.AreaIndex,
                        request.AreaPolygonStart,
                        request.AreaPolygonCount,
                        request.StartPolygon,
                        request.GoalPolygon,
                        request.Start,
                        request.Goal,
                        request.AreaMultiplier,
                        request.BaseCost);
                    laneIndex = index;
                    return true;
                }
                catch
                {
                    lane.InUse = false;
                    lane.Handle = default;
                    throw;
                }
            }

            laneIndex = -1;
            return false;
        }

        internal bool IsCompleted(int laneIndex)
        {
            ThrowIfDisposed();
            ScratchLane lane = GetLane(laneIndex);
            return lane.InUse && lane.Handle.IsCompleted;
        }

        internal bool Complete(int laneIndex, out double totalCost)
        {
            ThrowIfDisposed();
            ScratchLane lane = GetLane(laneIndex);
            lane.Handle.Complete();
            totalCost = lane.ResultCost[0];
            return lane.ResultStatus[0] == 1;
        }

        internal int GetPrevious(int laneIndex, int polygon) => GetLane(laneIndex).Previous[polygon];
        internal int GetPreviousAdjacency(int laneIndex, int polygon) =>
            GetLane(laneIndex).PreviousAdjacency[polygon];

        internal void Release(int laneIndex)
        {
            ScratchLane lane = GetLane(laneIndex);
            lane.Handle = default;
            lane.InUse = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _synchronousLane.Dispose();
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index].Dispose();
            }

            foreach (NativePolicyData policy in _policies.Values)
            {
                policy.Dispose();
            }

            _policies.Clear();
            Dispose(_centroids);
            Dispose(_polygonArea);
            Dispose(_adjacencyStarts);
            Dispose(_adjacencyCounts);
            Dispose(_semanticOffsets);
            Dispose(_capabilityOffsets);
            Dispose(_adjacencyTargets);
            Dispose(_semanticWords);
        }

        private JobHandle Schedule(
            ScratchLane lane,
            CompiledTraversalPolicy policy,
            NavigationRuntimeSnapshot snapshot,
            int areaIndex,
            int areaPolygonStart,
            int areaPolygonCount,
            int startPolygon,
            int goalPolygon,
            float3 start,
            float3 goal,
            double areaMultiplier,
            double baseCost)
        {
            NativePolicyData nativePolicy = GetPolicy(policy);
            return new PolygonSearchJob
            {
                Centroids = _centroids,
                PolygonArea = _polygonArea,
                AdjacencyStarts = _adjacencyStarts,
                AdjacencyCounts = _adjacencyCounts,
                SemanticOffsets = _semanticOffsets,
                CapabilityOffsets = _capabilityOffsets,
                AdjacencyTargets = _adjacencyTargets,
                SemanticWords = _semanticWords,
                PolygonEnabled = snapshot.NativePolygonEnabled,
                AdjacencyEnabled = snapshot.NativeAdjacencyEnabled,
                Capabilities = nativePolicy.Capabilities,
                RequiredAll = nativePolicy.RequiredAll,
                RequiredAny = nativePolicy.RequiredAny,
                ForbiddenAny = nativePolicy.ForbiddenAny,
                CostRules = nativePolicy.CostRules,
                WordCount = policy.WordCount,
                AreaIndex = areaIndex,
                AreaPolygonStart = areaPolygonStart,
                AreaPolygonCount = areaPolygonCount,
                StartPolygon = startPolygon,
                GoalPolygon = goalPolygon,
                Start = start,
                Goal = goal,
                AreaMultiplier = areaMultiplier,
                BaseCost = baseCost,
                Distances = lane.Distances,
                Previous = lane.Previous,
                PreviousAdjacency = lane.PreviousAdjacency,
                Visited = lane.Visited,
                ResultStatus = lane.ResultStatus,
                ResultCost = lane.ResultCost
            }.Schedule();
        }

        private ScratchLane GetLane(int laneIndex)
        {
            if ((uint)laneIndex >= (uint)_lanes.Length || !_lanes[laneIndex].InUse)
            {
                throw new ArgumentOutOfRangeException(nameof(laneIndex));
            }

            return _lanes[laneIndex];
        }

        private NativePolicyData GetPolicy(CompiledTraversalPolicy policy)
        {
            if (_policies.TryGetValue(policy.Fingerprint, out NativePolicyData result))
            {
                return result;
            }

            result = new NativePolicyData(policy);
            _policies.Add(policy.Fingerprint, result);
            return result;
        }

        private static void Dispose<T>(NativeArray<T> value) where T : struct
        {
            if (value.IsCreated)
            {
                value.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BurstPolygonSearch));
            }
        }

        private sealed class ScratchLane : IDisposable
        {
            internal ScratchLane(int polygonCount)
            {
                Distances = new NativeArray<double>(polygonCount, Allocator.Persistent);
                Previous = new NativeArray<int>(polygonCount, Allocator.Persistent);
                PreviousAdjacency = new NativeArray<int>(polygonCount, Allocator.Persistent);
                Visited = new NativeArray<byte>(polygonCount, Allocator.Persistent);
                ResultStatus = new NativeArray<int>(1, Allocator.Persistent);
                ResultCost = new NativeArray<double>(1, Allocator.Persistent);
            }

            internal NativeArray<double> Distances;
            internal NativeArray<int> Previous;
            internal NativeArray<int> PreviousAdjacency;
            internal NativeArray<byte> Visited;
            internal NativeArray<int> ResultStatus;
            internal NativeArray<double> ResultCost;
            internal JobHandle Handle;
            internal bool InUse;

            public void Dispose()
            {
                if (InUse)
                {
                    Handle.Complete();
                }

                BurstPolygonSearch.Dispose(Distances);
                BurstPolygonSearch.Dispose(Previous);
                BurstPolygonSearch.Dispose(PreviousAdjacency);
                BurstPolygonSearch.Dispose(Visited);
                BurstPolygonSearch.Dispose(ResultStatus);
                BurstPolygonSearch.Dispose(ResultCost);
                InUse = false;
                Handle = default;
            }
        }

        private sealed class NativePolicyData : IDisposable
        {
            internal NativePolicyData(CompiledTraversalPolicy policy)
            {
                Capabilities = new NativeArray<ulong>(policy.WordCount, Allocator.Persistent);
                RequiredAll = new NativeArray<ulong>(policy.WordCount * 3, Allocator.Persistent);
                RequiredAny = new NativeArray<ulong>(policy.WordCount * 3, Allocator.Persistent);
                ForbiddenAny = new NativeArray<ulong>(policy.WordCount * 3, Allocator.Persistent);
                for (int word = 0; word < policy.WordCount; word++)
                {
                    Capabilities[word] = policy.GetCapabilityWord(word);
                    for (int kind = 0; kind < 3; kind++)
                    {
                        int offset = (kind * policy.WordCount) + word;
                        var elementKind = (NavigationElementKind)kind;
                        RequiredAll[offset] = policy.GetPredicateWord(elementKind, 0, word);
                        RequiredAny[offset] = policy.GetPredicateWord(elementKind, 1, word);
                        ForbiddenAny[offset] = policy.GetPredicateWord(elementKind, 2, word);
                    }
                }

                CostRules = new NativeArray<NativeCostRule>(policy.CostRuleCount, Allocator.Persistent);
                for (int index = 0; index < policy.CostRuleCount; index++)
                {
                    SemanticCostRule rule = policy.GetCostRule(index);
                    CostRules[index] = new NativeCostRule
                    {
                        Slot = rule.Slot,
                        DistanceMultiplier = rule.DistanceMultiplier,
                        EntryPenalty = rule.EntryPenalty
                    };
                }
            }

            internal NativeArray<ulong> Capabilities;
            internal NativeArray<ulong> RequiredAll;
            internal NativeArray<ulong> RequiredAny;
            internal NativeArray<ulong> ForbiddenAny;
            internal NativeArray<NativeCostRule> CostRules;

            public void Dispose()
            {
                BurstPolygonSearch.Dispose(Capabilities);
                BurstPolygonSearch.Dispose(RequiredAll);
                BurstPolygonSearch.Dispose(RequiredAny);
                BurstPolygonSearch.Dispose(ForbiddenAny);
                BurstPolygonSearch.Dispose(CostRules);
            }
        }

        internal struct NativeCostRule
        {
            internal int Slot;
            internal double DistanceMultiplier;
            internal double EntryPenalty;
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private struct PolygonSearchJob : IJob
        {
            [ReadOnly] internal NativeArray<float3> Centroids;
            [ReadOnly] internal NativeArray<int> PolygonArea;
            [ReadOnly] internal NativeArray<int> AdjacencyStarts;
            [ReadOnly] internal NativeArray<int> AdjacencyCounts;
            [ReadOnly] internal NativeArray<int> SemanticOffsets;
            [ReadOnly] internal NativeArray<int> CapabilityOffsets;
            [ReadOnly] internal NativeArray<int> AdjacencyTargets;
            [ReadOnly] internal NativeArray<ulong> SemanticWords;
            [ReadOnly] internal NativeArray<byte> PolygonEnabled;
            [ReadOnly] internal NativeArray<byte> AdjacencyEnabled;
            [ReadOnly] internal NativeArray<ulong> Capabilities;
            [ReadOnly] internal NativeArray<ulong> RequiredAll;
            [ReadOnly] internal NativeArray<ulong> RequiredAny;
            [ReadOnly] internal NativeArray<ulong> ForbiddenAny;
            [ReadOnly] internal NativeArray<NativeCostRule> CostRules;
            internal int WordCount;
            internal int AreaIndex;
            internal int AreaPolygonStart;
            internal int AreaPolygonCount;
            internal int StartPolygon;
            internal int GoalPolygon;
            internal float3 Start;
            internal float3 Goal;
            internal double AreaMultiplier;
            internal double BaseCost;
            internal NativeArray<double> Distances;
            internal NativeArray<int> Previous;
            internal NativeArray<int> PreviousAdjacency;
            internal NativeArray<byte> Visited;
            internal NativeArray<int> ResultStatus;
            internal NativeArray<double> ResultCost;

            public void Execute()
            {
                int end = AreaPolygonStart + AreaPolygonCount;
                for (int index = AreaPolygonStart; index < end; index++)
                {
                    Distances[index] = double.PositiveInfinity;
                    Previous[index] = -1;
                    PreviousAdjacency[index] = -1;
                    Visited[index] = 0;
                }

                ResultStatus[0] = 0;
                ResultCost[0] = double.PositiveInfinity;
                if (!CanUsePolygon(StartPolygon) || !CanUsePolygon(GoalPolygon))
                {
                    return;
                }

                double firstMultiplier = AreaMultiplier * GetDistanceMultiplier(SemanticOffsets[StartPolygon]);
                if (StartPolygon == GoalPolygon)
                {
                    double directCost = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                                        math.distance(Start, Goal) * firstMultiplier;
                    if (math.isfinite(directCost))
                    {
                        ResultCost[0] = directCost;
                        ResultStatus[0] = 1;
                    }

                    return;
                }

                Distances[StartPolygon] = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                                          math.distance(Start, Centroids[StartPolygon]) * firstMultiplier;

                for (int iteration = 0; iteration < AreaPolygonCount; iteration++)
                {
                    int current = FindCheapest(end);
                    if (current < 0)
                    {
                        return;
                    }

                    if (current == GoalPolygon)
                    {
                        double cost = Distances[current] +
                                      math.distance(Centroids[current], Goal) * AreaMultiplier *
                                      GetDistanceMultiplier(SemanticOffsets[current]);
                        if (math.isfinite(cost))
                        {
                            ResultCost[0] = cost;
                            ResultStatus[0] = 1;
                        }

                        return;
                    }

                    Visited[current] = 1;
                    int adjacencyEnd = AdjacencyStarts[current] + AdjacencyCounts[current];
                    for (int adjacency = AdjacencyStarts[current]; adjacency < adjacencyEnd; adjacency++)
                    {
                        if (AdjacencyEnabled[adjacency] == 0)
                        {
                            continue;
                        }

                        int neighbor = AdjacencyTargets[adjacency];
                        if (Visited[neighbor] != 0 || !CanUsePolygon(neighbor))
                        {
                            continue;
                        }

                        double candidate = Distances[current] +
                                           math.distance(Centroids[current], Centroids[neighbor]) * AreaMultiplier *
                                           GetDistanceMultiplier(SemanticOffsets[neighbor]) +
                                           GetEntryPenalty(SemanticOffsets[neighbor]);
                        if (candidate < Distances[neighbor] ||
                            (candidate == Distances[neighbor] && current < Previous[neighbor]))
                        {
                            Distances[neighbor] = candidate;
                            Previous[neighbor] = current;
                            PreviousAdjacency[neighbor] = adjacency;
                        }
                    }
                }
            }

            private int FindCheapest(int end)
            {
                int best = -1;
                double bestCost = double.PositiveInfinity;
                for (int index = AreaPolygonStart; index < end; index++)
                {
                    if (PolygonArea[index] == AreaIndex && Visited[index] == 0 &&
                        (Distances[index] < bestCost ||
                         (Distances[index] == bestCost && index < best)))
                    {
                        best = index;
                        bestCost = Distances[index];
                    }
                }

                return math.isfinite(bestCost) ? best : -1;
            }

            private bool CanUsePolygon(int polygon)
            {
                return PolygonEnabled[polygon] != 0 &&
                       Allows(1, SemanticOffsets[polygon], CapabilityOffsets[polygon]);
            }

            private bool Allows(int kind, int semanticOffset, int requiredCapabilityOffset)
            {
                bool hasRequiredAny = false;
                bool matchedRequiredAny = false;
                int predicateOffset = kind * WordCount;
                for (int word = 0; word < WordCount; word++)
                {
                    ulong semantics = SemanticWords[semanticOffset + word];
                    ulong requiredCapabilities = SemanticWords[requiredCapabilityOffset + word];
                    if ((Capabilities[word] & requiredCapabilities) != requiredCapabilities ||
                        (semantics & RequiredAll[predicateOffset + word]) != RequiredAll[predicateOffset + word] ||
                        (semantics & ForbiddenAny[predicateOffset + word]) != 0UL)
                    {
                        return false;
                    }

                    ulong any = RequiredAny[predicateOffset + word];
                    hasRequiredAny |= any != 0UL;
                    matchedRequiredAny |= (semantics & any) != 0UL;
                }

                return !hasRequiredAny || matchedRequiredAny;
            }

            private double GetDistanceMultiplier(int semanticOffset)
            {
                double result = 1d;
                for (int index = 0; index < CostRules.Length; index++)
                {
                    NativeCostRule rule = CostRules[index];
                    if (Contains(semanticOffset, rule.Slot))
                    {
                        result *= rule.DistanceMultiplier;
                    }
                }

                return result;
            }

            private double GetEntryPenalty(int semanticOffset)
            {
                double result = 0d;
                for (int index = 0; index < CostRules.Length; index++)
                {
                    NativeCostRule rule = CostRules[index];
                    if (Contains(semanticOffset, rule.Slot))
                    {
                        result += rule.EntryPenalty;
                    }
                }

                return result;
            }

            private bool Contains(int semanticOffset, int slot)
            {
                int word = slot / 64;
                return slot >= 0 && word < WordCount &&
                       (SemanticWords[semanticOffset + word] & (1UL << (slot % 64))) != 0UL;
            }
        }
    }
}
