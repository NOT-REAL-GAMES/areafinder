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
        private readonly NativeArray<float3> _centroids;
        private readonly NativeArray<int> _polygonArea;
        private readonly NativeArray<int> _adjacencyStarts;
        private readonly NativeArray<int> _adjacencyCounts;
        private readonly NativeArray<int> _semanticOffsets;
        private readonly NativeArray<int> _capabilityOffsets;
        private readonly NativeArray<int> _adjacencyTargets;
        private readonly NativeArray<ulong> _semanticWords;
        private NativeArray<byte> _polygonEnabled;
        private NativeArray<byte> _adjacencyEnabled;
        private readonly NativeArray<double> _distances;
        private readonly NativeArray<int> _previous;
        private readonly NativeArray<int> _previousAdjacency;
        private readonly NativeArray<byte> _visited;
        private readonly NativeArray<int> _resultStatus;
        private readonly NativeArray<double> _resultCost;
        private readonly Dictionary<ulong, NativePolicyData> _policies =
            new Dictionary<ulong, NativePolicyData>();
        private bool _disposed;

        internal BurstPolygonSearch(NavigationRuntimeData data)
        {
            int polygonCount = data.Polygons.Length;
            _centroids = new NativeArray<float3>(polygonCount, Allocator.Persistent);
            _polygonArea = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _adjacencyStarts = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _adjacencyCounts = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _semanticOffsets = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _capabilityOffsets = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _polygonEnabled = new NativeArray<byte>(polygonCount, Allocator.Persistent);
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
                _polygonEnabled[index] = data.PolygonEnabled[index] ? (byte)1 : (byte)0;
            }

            _adjacencyTargets = new NativeArray<int>(data.Adjacencies.Length, Allocator.Persistent);
            _adjacencyEnabled = new NativeArray<byte>(data.Adjacencies.Length, Allocator.Persistent);
            for (int index = 0; index < data.Adjacencies.Length; index++)
            {
                _adjacencyTargets[index] = data.Adjacencies[index].ToPolygon;
                _adjacencyEnabled[index] = data.AdjacencyEnabled[index] ? (byte)1 : (byte)0;
            }

            _semanticWords = new NativeArray<ulong>(data.SemanticWords, Allocator.Persistent);
            _distances = new NativeArray<double>(polygonCount, Allocator.Persistent);
            _previous = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _previousAdjacency = new NativeArray<int>(polygonCount, Allocator.Persistent);
            _visited = new NativeArray<byte>(polygonCount, Allocator.Persistent);
            _resultStatus = new NativeArray<int>(1, Allocator.Persistent);
            _resultCost = new NativeArray<double>(1, Allocator.Persistent);
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
            NativePolicyData nativePolicy = GetPolicy(policy);
            var job = new PolygonSearchJob
            {
                Centroids = _centroids,
                PolygonArea = _polygonArea,
                AdjacencyStarts = _adjacencyStarts,
                AdjacencyCounts = _adjacencyCounts,
                SemanticOffsets = _semanticOffsets,
                CapabilityOffsets = _capabilityOffsets,
                AdjacencyTargets = _adjacencyTargets,
                SemanticWords = _semanticWords,
                PolygonEnabled = _polygonEnabled,
                AdjacencyEnabled = _adjacencyEnabled,
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
                Distances = _distances,
                Previous = _previous,
                PreviousAdjacency = _previousAdjacency,
                Visited = _visited,
                ResultStatus = _resultStatus,
                ResultCost = _resultCost
            };
            job.Schedule().Complete();
            totalCost = _resultCost[0];
            return _resultStatus[0] == 1;
        }

        internal int GetPrevious(int polygon) => _previous[polygon];
        internal int GetPreviousAdjacency(int polygon) => _previousAdjacency[polygon];

        internal void SetPolygonEnabled(int polygon, bool enabled)
        {
            ThrowIfDisposed();
            _polygonEnabled[polygon] = enabled ? (byte)1 : (byte)0;
        }

        internal void SetAdjacencyEnabled(int adjacency, bool enabled)
        {
            ThrowIfDisposed();
            _adjacencyEnabled[adjacency] = enabled ? (byte)1 : (byte)0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
            Dispose(_polygonEnabled);
            Dispose(_adjacencyEnabled);
            Dispose(_distances);
            Dispose(_previous);
            Dispose(_previousAdjacency);
            Dispose(_visited);
            Dispose(_resultStatus);
            Dispose(_resultCost);
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
