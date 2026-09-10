using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace NotRealGames.Areafinder
{
    internal sealed class BurstPolygonSearch : IDisposable
    {
        private readonly NavigationRuntimeData _data;
        private readonly SearchExecutionOptions _options;
        private readonly NativeArray<float3> _centroids;
        private readonly NativeArray<int> _polygonArea;
        private readonly NativeArray<int> _adjacencyStarts;
        private readonly NativeArray<int> _adjacencyCounts;
        private readonly NativeArray<int> _semanticOffsets;
        private readonly NativeArray<int> _capabilityOffsets;
        private readonly NativeArray<int> _adjacencySources;
        private readonly NativeArray<int> _adjacencyTargets;
        private readonly NativeArray<int> _reverseStarts;
        private readonly NativeArray<int> _reverseCounts;
        private readonly NativeArray<int> _reverseAdjacencies;
        private readonly NativeArray<ulong> _semanticWords;
        private readonly NativeArray<double> _emptyLandmarkDistances;
        private readonly ScratchLane _synchronousLane;
        private readonly ScratchLane[] _lanes;
        private readonly Dictionary<ulong, NativePolicyData> _policies = new Dictionary<ulong, NativePolicyData>();
        private readonly Dictionary<AcceleratorKey, SearchAcceleratorData> _accelerators =
            new Dictionary<AcceleratorKey, SearchAcceleratorData>();
        private bool _disposed;

        internal BurstPolygonSearch(
            NavigationRuntimeData data,
            int maxConcurrentSearches,
            SearchExecutionOptions options)
        {
            if (maxConcurrentSearches < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentSearches));
            }

            _data = data ?? throw new ArgumentNullException(nameof(data));
            _options = options;
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
                _centroids[index] = new float3(polygon.Centroid.x, polygon.Centroid.y, polygon.Centroid.z);
                _polygonArea[index] = polygon.AreaIndex;
                _adjacencyStarts[index] = polygon.AdjacencyStart;
                _adjacencyCounts[index] = polygon.AdjacencyCount;
                _semanticOffsets[index] = polygon.SemanticOffset;
                _capabilityOffsets[index] = polygon.RequiredCapabilityOffset;
            }

            int adjacencyCount = data.Adjacencies.Length;
            _adjacencySources = new NativeArray<int>(adjacencyCount, Allocator.Persistent);
            _adjacencyTargets = new NativeArray<int>(adjacencyCount, Allocator.Persistent);
            var incomingCounts = new int[polygonCount];
            for (int index = 0; index < adjacencyCount; index++)
            {
                CompiledAdjacencyRecord adjacency = data.Adjacencies[index];
                _adjacencySources[index] = adjacency.FromPolygon;
                _adjacencyTargets[index] = adjacency.ToPolygon;
                incomingCounts[adjacency.ToPolygon]++;
            }

            var incomingStarts = new int[polygonCount];
            int cursor = 0;
            for (int index = 0; index < polygonCount; index++)
            {
                incomingStarts[index] = cursor;
                cursor += incomingCounts[index];
            }

            var incoming = new int[adjacencyCount];
            var write = (int[])incomingStarts.Clone();
            for (int index = 0; index < adjacencyCount; index++)
            {
                incoming[write[data.Adjacencies[index].ToPolygon]++] = index;
            }

            _reverseStarts = new NativeArray<int>(incomingStarts, Allocator.Persistent);
            _reverseCounts = new NativeArray<int>(incomingCounts, Allocator.Persistent);
            _reverseAdjacencies = new NativeArray<int>(incoming, Allocator.Persistent);
            _semanticWords = new NativeArray<ulong>(data.SemanticWords, Allocator.Persistent);
            _emptyLandmarkDistances = new NativeArray<double>(1, Allocator.Persistent);
            _synchronousLane = new ScratchLane(polygonCount, options.Strategy);
            _lanes = new ScratchLane[maxConcurrentSearches];
            for (int index = 0; index < _lanes.Length; index++)
            {
                _lanes[index] = new ScratchLane(polygonCount, options.Strategy);
            }
        }

        internal bool PrepareAccelerators(CompiledTraversalPolicy policy)
        {
            ThrowIfDisposed();
            if (policy == null || policy.RegistryFingerprint != _data.RegistryFingerprint ||
                policy.WordCount != _data.SemanticWordCount)
            {
                return false;
            }

            for (int area = 0; area < _data.Areas.Length; area++)
            {
                var key = new AcceleratorKey(policy.Fingerprint, area, _options.LandmarkCount);
                if (!_accelerators.ContainsKey(key))
                {
                    _accelerators.Add(key, SearchAcceleratorData.Build(
                        _data, policy, area, _options.LandmarkCount));
                }
            }

            return true;
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
            try
            {
                _synchronousLane.Handle = Schedule(
                    _synchronousLane, policy, _data.CurrentSnapshot, areaIndex, areaPolygonStart,
                    areaPolygonCount, startPolygon, goalPolygon, start, goal, areaMultiplier, baseCost);
                _synchronousLane.Handle.Complete();
                totalCost = _synchronousLane.ResultCost[0];
                return _synchronousLane.ResultStatus[0] == 1;
            }
            finally
            {
                ReleaseAccelerator(_synchronousLane);
            }
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
                    count += _lanes[index].InUse ? 1 : 0;
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
                        lane, request.Policy, request.Snapshot, request.AreaIndex,
                        request.AreaPolygonStart, request.AreaPolygonCount, request.StartPolygon,
                        request.GoalPolygon, request.Start, request.Goal,
                        request.AreaMultiplier, request.BaseCost);
                    laneIndex = index;
                    return true;
                }
                catch
                {
                    ReleaseAccelerator(lane);
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

        internal bool Complete(int laneIndex, out double totalCost) =>
            Complete(laneIndex, out totalCost, out _);

        internal bool Complete(int laneIndex, out double totalCost, out SearchDiagnostics diagnostics)
        {
            ThrowIfDisposed();
            ScratchLane lane = GetLane(laneIndex);
            lane.Handle.Complete();
            totalCost = lane.ResultCost[0];
            diagnostics = lane.Diagnostics[0].ToManaged();
            return lane.ResultStatus[0] == 1;
        }

        internal int GetPrevious(int laneIndex, int polygon) => GetLane(laneIndex).Previous[polygon];
        internal int GetPreviousAdjacency(int laneIndex, int polygon) => GetLane(laneIndex).PreviousAdjacency[polygon];

        internal void Release(int laneIndex)
        {
            ScratchLane lane = GetLane(laneIndex);
            lane.Handle = default;
            ReleaseAccelerator(lane);
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

            foreach (SearchAcceleratorData accelerator in _accelerators.Values)
            {
                accelerator.Dispose();
            }

            Dispose(_centroids);
            Dispose(_polygonArea);
            Dispose(_adjacencyStarts);
            Dispose(_adjacencyCounts);
            Dispose(_semanticOffsets);
            Dispose(_capabilityOffsets);
            Dispose(_adjacencySources);
            Dispose(_adjacencyTargets);
            Dispose(_reverseStarts);
            Dispose(_reverseCounts);
            Dispose(_reverseAdjacencies);
            Dispose(_semanticWords);
            Dispose(_emptyLandmarkDistances);
            _policies.Clear();
            _accelerators.Clear();
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
            SearchStrategy requested = _options.Strategy;
            SearchStrategy executed = requested;
            SearchFallbackReason fallback = SearchFallbackReason.None;
            SearchAcceleratorData accelerator = null;
            if (UsesAlt(requested))
            {
                var key = new AcceleratorKey(policy.Fingerprint, areaIndex, _options.LandmarkCount);
                if (!_accelerators.TryGetValue(key, out accelerator))
                {
                    executed = requested == SearchStrategy.AltAStar
                        ? SearchStrategy.AStar
                        : SearchStrategy.BidirectionalAStar;
                    fallback = SearchFallbackReason.AcceleratorUnavailable;
                }
                else if (!accelerator.IsCompatible(
                             areaIndex,
                             areaPolygonStart,
                             areaPolygonCount,
                             policy.Fingerprint))
                {
                    accelerator = null;
                    executed = requested == SearchStrategy.AltAStar
                        ? SearchStrategy.AStar
                        : SearchStrategy.BidirectionalAStar;
                    fallback = SearchFallbackReason.AcceleratorIncompatible;
                }
            }

            accelerator?.Acquire();
            lane.Accelerator = accelerator;
            return new PolygonSearchJob
            {
                Centroids = _centroids,
                PolygonArea = _polygonArea,
                AdjacencyStarts = _adjacencyStarts,
                AdjacencyCounts = _adjacencyCounts,
                SemanticOffsets = _semanticOffsets,
                CapabilityOffsets = _capabilityOffsets,
                AdjacencySources = _adjacencySources,
                AdjacencyTargets = _adjacencyTargets,
                ReverseStarts = _reverseStarts,
                ReverseCounts = _reverseCounts,
                ReverseAdjacencies = _reverseAdjacencies,
                SemanticWords = _semanticWords,
                PolygonEnabled = snapshot.NativePolygonEnabled,
                AdjacencyEnabled = snapshot.NativeAdjacencyEnabled,
                Capabilities = nativePolicy.Capabilities,
                RequiredAll = nativePolicy.RequiredAll,
                RequiredAny = nativePolicy.RequiredAny,
                ForbiddenAny = nativePolicy.ForbiddenAny,
                CostRules = nativePolicy.CostRules,
                LandmarkDistancesFrom = accelerator == null
                    ? _emptyLandmarkDistances
                    : accelerator.DistancesFromLandmarks,
                LandmarkDistancesTo = accelerator == null
                    ? _emptyLandmarkDistances
                    : accelerator.DistancesToLandmarks,
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
                MinimumMultiplier = FindMinimumMultiplier(policy, areaPolygonStart, areaPolygonCount, areaMultiplier),
                RequestedStrategy = requested,
                ExecutedStrategy = executed,
                FallbackReason = fallback,
                LandmarkCount = accelerator == null ? 0 : accelerator.LandmarkCount,
                AcceleratorBytes = accelerator == null ? 0L : accelerator.ByteCount,
                PreprocessingMilliseconds = accelerator == null ? 0d : accelerator.PreprocessingMilliseconds,
                ScratchBytes = lane.ByteCount,
                Distances = lane.Distances,
                Previous = lane.Previous,
                PreviousAdjacency = lane.PreviousAdjacency,
                Visited = lane.Visited,
                HeapNodes = lane.HeapNodes,
                HeapPositions = lane.HeapPositions,
                HeapPrimary = lane.HeapPrimary,
                HeapSecondary = lane.HeapSecondary,
                ReverseDistances = lane.ReverseDistances,
                ReverseVisited = lane.ReverseVisited,
                ReverseHeapNodes = lane.ReverseHeapNodes,
                ReverseHeapPositions = lane.ReverseHeapPositions,
                ReverseHeapPrimary = lane.ReverseHeapPrimary,
                ReverseHeapSecondary = lane.ReverseHeapSecondary,
                ResultStatus = lane.ResultStatus,
                ResultCost = lane.ResultCost,
                Diagnostics = lane.Diagnostics
            }.Schedule();
        }

        private double FindMinimumMultiplier(
            CompiledTraversalPolicy policy,
            int start,
            int count,
            double areaMultiplier)
        {
            double minimum = double.PositiveInfinity;
            for (int index = start; index < start + count; index++)
            {
                CompiledPolygonRecord polygon = _data.Polygons[index];
                if (policy.CanTraverse(
                    NavigationElementKind.Polygon, _data.SemanticWords,
                    polygon.SemanticOffset, polygon.RequiredCapabilityOffset))
                {
                    minimum = Math.Min(minimum, areaMultiplier * policy.GetDistanceMultiplier(
                        _data.SemanticWords, polygon.SemanticOffset));
                }
            }

            return double.IsInfinity(minimum) ? 0d : minimum;
        }

        private NativePolicyData GetPolicy(CompiledTraversalPolicy policy)
        {
            if (!_policies.TryGetValue(policy.Fingerprint, out NativePolicyData result))
            {
                result = new NativePolicyData(policy);
                _policies.Add(policy.Fingerprint, result);
            }

            return result;
        }

        private ScratchLane GetLane(int index)
        {
            if ((uint)index >= (uint)_lanes.Length || !_lanes[index].InUse)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _lanes[index];
        }

        private static void ReleaseAccelerator(ScratchLane lane)
        {
            lane.Accelerator?.Release();
            lane.Accelerator = null;
        }

        private static bool UsesAlt(SearchStrategy value) =>
            value == SearchStrategy.AltAStar || value == SearchStrategy.BidirectionalAlt;

        private static bool UsesHeap(SearchStrategy value) => value != SearchStrategy.Reference03;

        private static bool UsesReverse(SearchStrategy value) =>
            value == SearchStrategy.BidirectionalDijkstra ||
            value == SearchStrategy.BidirectionalAStar ||
            value == SearchStrategy.BidirectionalAlt;

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

        private readonly struct AcceleratorKey : IEquatable<AcceleratorKey>
        {
            internal AcceleratorKey(ulong policy, int area, int landmarks)
            {
                Policy = policy;
                Area = area;
                Landmarks = landmarks;
            }

            private ulong Policy { get; }
            private int Area { get; }
            private int Landmarks { get; }
            public bool Equals(AcceleratorKey other) =>
                Policy == other.Policy && Area == other.Area && Landmarks == other.Landmarks;
            public override bool Equals(object obj) => obj is AcceleratorKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Policy, Area, Landmarks);
        }

        private sealed class ScratchLane : IDisposable
        {
            internal ScratchLane(int polygonCount, SearchStrategy strategy)
            {
                Distances = new NativeArray<double>(polygonCount, Allocator.Persistent);
                Previous = new NativeArray<int>(polygonCount, Allocator.Persistent);
                PreviousAdjacency = new NativeArray<int>(polygonCount, Allocator.Persistent);
                Visited = new NativeArray<byte>(polygonCount, Allocator.Persistent);
                ResultStatus = new NativeArray<int>(1, Allocator.Persistent);
                ResultCost = new NativeArray<double>(1, Allocator.Persistent);
                Diagnostics = new NativeArray<NativeSearchDiagnostics>(1, Allocator.Persistent);
                int heapLength = UsesHeap(strategy) ? polygonCount : 1;
                HeapNodes = new NativeArray<int>(heapLength, Allocator.Persistent);
                HeapPositions = new NativeArray<int>(heapLength, Allocator.Persistent);
                HeapPrimary = new NativeArray<double>(heapLength, Allocator.Persistent);
                HeapSecondary = new NativeArray<double>(heapLength, Allocator.Persistent);
                int reverseLength = UsesReverse(strategy) ? polygonCount : 1;
                ReverseDistances = new NativeArray<double>(reverseLength, Allocator.Persistent);
                ReverseVisited = new NativeArray<byte>(reverseLength, Allocator.Persistent);
                ReverseHeapNodes = new NativeArray<int>(reverseLength, Allocator.Persistent);
                ReverseHeapPositions = new NativeArray<int>(reverseLength, Allocator.Persistent);
                ReverseHeapPrimary = new NativeArray<double>(reverseLength, Allocator.Persistent);
                ReverseHeapSecondary = new NativeArray<double>(reverseLength, Allocator.Persistent);
            }

            internal NativeArray<double> Distances;
            internal NativeArray<int> Previous;
            internal NativeArray<int> PreviousAdjacency;
            internal NativeArray<byte> Visited;
            internal NativeArray<int> HeapNodes;
            internal NativeArray<int> HeapPositions;
            internal NativeArray<double> HeapPrimary;
            internal NativeArray<double> HeapSecondary;
            internal NativeArray<double> ReverseDistances;
            internal NativeArray<byte> ReverseVisited;
            internal NativeArray<int> ReverseHeapNodes;
            internal NativeArray<int> ReverseHeapPositions;
            internal NativeArray<double> ReverseHeapPrimary;
            internal NativeArray<double> ReverseHeapSecondary;
            internal NativeArray<int> ResultStatus;
            internal NativeArray<double> ResultCost;
            internal NativeArray<NativeSearchDiagnostics> Diagnostics;
            internal SearchAcceleratorData Accelerator;
            internal JobHandle Handle;
            internal bool InUse;
            internal long ByteCount =>
                Bytes(Distances) + Bytes(Previous) + Bytes(PreviousAdjacency) + Bytes(Visited) +
                Bytes(HeapNodes) + Bytes(HeapPositions) + Bytes(HeapPrimary) + Bytes(HeapSecondary) +
                Bytes(ReverseDistances) + Bytes(ReverseVisited) + Bytes(ReverseHeapNodes) +
                Bytes(ReverseHeapPositions) + Bytes(ReverseHeapPrimary) + Bytes(ReverseHeapSecondary) +
                Bytes(ResultStatus) + Bytes(ResultCost) + Bytes(Diagnostics);

            public void Dispose()
            {
                if (InUse)
                {
                    Handle.Complete();
                }

                ReleaseAccelerator(this);
                BurstPolygonSearch.Dispose(Distances);
                BurstPolygonSearch.Dispose(Previous);
                BurstPolygonSearch.Dispose(PreviousAdjacency);
                BurstPolygonSearch.Dispose(Visited);
                BurstPolygonSearch.Dispose(HeapNodes);
                BurstPolygonSearch.Dispose(HeapPositions);
                BurstPolygonSearch.Dispose(HeapPrimary);
                BurstPolygonSearch.Dispose(HeapSecondary);
                BurstPolygonSearch.Dispose(ReverseDistances);
                BurstPolygonSearch.Dispose(ReverseVisited);
                BurstPolygonSearch.Dispose(ReverseHeapNodes);
                BurstPolygonSearch.Dispose(ReverseHeapPositions);
                BurstPolygonSearch.Dispose(ReverseHeapPrimary);
                BurstPolygonSearch.Dispose(ReverseHeapSecondary);
                BurstPolygonSearch.Dispose(ResultStatus);
                BurstPolygonSearch.Dispose(ResultCost);
                BurstPolygonSearch.Dispose(Diagnostics);
                InUse = false;
                Handle = default;
            }

            private static long Bytes<T>(NativeArray<T> value) where T : struct =>
                value.IsCreated ? (long)value.Length * UnsafeUtility.SizeOf<T>() : 0L;
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
                        int offset = kind * policy.WordCount + word;
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

        internal struct NativeSearchDiagnostics
        {
            internal SearchStrategy RequestedStrategy;
            internal SearchStrategy ExecutedStrategy;
            internal SearchFallbackReason FallbackReason;
            internal long NodesDiscovered;
            internal long NodesExpanded;
            internal long EdgesExamined;
            internal long HeapPushes;
            internal long HeapPops;
            internal long MaximumFrontier;
            internal long HeuristicEvaluations;
            internal int LandmarkCount;
            internal long ScratchBytes;
            internal long AcceleratorBytes;
            internal double PreprocessingMilliseconds;

            internal SearchDiagnostics ToManaged() => new SearchDiagnostics
            {
                RequestedStrategy = RequestedStrategy,
                ExecutedStrategy = ExecutedStrategy,
                FallbackReason = FallbackReason,
                NodesDiscovered = NodesDiscovered,
                NodesExpanded = NodesExpanded,
                EdgesExamined = EdgesExamined,
                HeapPushes = HeapPushes,
                HeapPops = HeapPops,
                MaximumFrontierSize = MaximumFrontier,
                HeuristicEvaluations = HeuristicEvaluations,
                LandmarkCount = LandmarkCount,
                ScratchBytes = ScratchBytes,
                AcceleratorBytes = AcceleratorBytes,
                PreprocessingMilliseconds = PreprocessingMilliseconds,
                LocalSearchCount = 1
            };
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
            [ReadOnly] internal NativeArray<int> AdjacencySources;
            [ReadOnly] internal NativeArray<int> AdjacencyTargets;
            [ReadOnly] internal NativeArray<int> ReverseStarts;
            [ReadOnly] internal NativeArray<int> ReverseCounts;
            [ReadOnly] internal NativeArray<int> ReverseAdjacencies;
            [ReadOnly] internal NativeArray<ulong> SemanticWords;
            [ReadOnly] internal NativeArray<byte> PolygonEnabled;
            [ReadOnly] internal NativeArray<byte> AdjacencyEnabled;
            [ReadOnly] internal NativeArray<ulong> Capabilities;
            [ReadOnly] internal NativeArray<ulong> RequiredAll;
            [ReadOnly] internal NativeArray<ulong> RequiredAny;
            [ReadOnly] internal NativeArray<ulong> ForbiddenAny;
            [ReadOnly] internal NativeArray<NativeCostRule> CostRules;
            [ReadOnly] internal NativeArray<double> LandmarkDistancesFrom;
            [ReadOnly] internal NativeArray<double> LandmarkDistancesTo;
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
            internal double MinimumMultiplier;
            internal SearchStrategy RequestedStrategy;
            internal SearchStrategy ExecutedStrategy;
            internal SearchFallbackReason FallbackReason;
            internal int LandmarkCount;
            internal long AcceleratorBytes;
            internal double PreprocessingMilliseconds;
            internal long ScratchBytes;
            internal NativeArray<double> Distances;
            internal NativeArray<int> Previous;
            internal NativeArray<int> PreviousAdjacency;
            internal NativeArray<byte> Visited;
            internal NativeArray<int> HeapNodes;
            internal NativeArray<int> HeapPositions;
            internal NativeArray<double> HeapPrimary;
            internal NativeArray<double> HeapSecondary;
            internal NativeArray<double> ReverseDistances;
            internal NativeArray<byte> ReverseVisited;
            internal NativeArray<int> ReverseHeapNodes;
            internal NativeArray<int> ReverseHeapPositions;
            internal NativeArray<double> ReverseHeapPrimary;
            internal NativeArray<double> ReverseHeapSecondary;
            internal NativeArray<int> ResultStatus;
            internal NativeArray<double> ResultCost;
            internal NativeArray<NativeSearchDiagnostics> Diagnostics;

            public void Execute()
            {
                var diagnostics = new NativeSearchDiagnostics
                {
                    RequestedStrategy = RequestedStrategy,
                    ExecutedStrategy = ExecutedStrategy,
                    FallbackReason = FallbackReason,
                    LandmarkCount = LandmarkCount,
                    ScratchBytes = ScratchBytes,
                    AcceleratorBytes = AcceleratorBytes,
                    PreprocessingMilliseconds = PreprocessingMilliseconds
                };
                ResultStatus[0] = 0;
                ResultCost[0] = double.PositiveInfinity;
                switch (ExecutedStrategy)
                {
                    case SearchStrategy.Reference03:
                        ExecuteReference03(ref diagnostics);
                        break;
                    case SearchStrategy.HeapDijkstra:
                        ExecuteForward(false, false, ref diagnostics);
                        break;
                    case SearchStrategy.AStar:
                    case SearchStrategy.AltAStar:
                        ExecuteForward(true, ExecutedStrategy == SearchStrategy.AltAStar, ref diagnostics);
                        break;
                    default:
                        ExecuteBidirectional(
                            ExecutedStrategy != SearchStrategy.BidirectionalDijkstra,
                            ExecutedStrategy == SearchStrategy.BidirectionalAlt,
                            ref diagnostics);
                        break;
                }

                Diagnostics[0] = diagnostics;
            }

            // Frozen 0.3 oracle: the scan, costs, stop condition and tie rules are unchanged.
            private void ExecuteReference03(ref NativeSearchDiagnostics diagnostics)
            {
                int end = AreaPolygonStart + AreaPolygonCount;
                ResetForward();
                if (!CanUsePolygon(StartPolygon) || !CanUsePolygon(GoalPolygon))
                {
                    return;
                }

                double firstMultiplier = AreaMultiplier * GetDistanceMultiplier(SemanticOffsets[StartPolygon]);
                if (StartPolygon == GoalPolygon)
                {
                    CompleteDirect(firstMultiplier);
                    return;
                }

                Distances[StartPolygon] = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                                          math.distance(Start, Centroids[StartPolygon]) * firstMultiplier;
                diagnostics.NodesDiscovered = 1;
                for (int iteration = 0; iteration < AreaPolygonCount; iteration++)
                {
                    int current = FindCheapestReference(end);
                    if (current < 0)
                    {
                        return;
                    }

                    diagnostics.NodesExpanded++;
                    if (current == GoalPolygon)
                    {
                        CompleteGoal(Distances[current]);
                        return;
                    }

                    Visited[current] = 1;
                    int adjacencyEnd = AdjacencyStarts[current] + AdjacencyCounts[current];
                    for (int adjacency = AdjacencyStarts[current]; adjacency < adjacencyEnd; adjacency++)
                    {
                        diagnostics.EdgesExamined++;
                        if (AdjacencyEnabled[adjacency] == 0)
                        {
                            continue;
                        }

                        int neighbor = AdjacencyTargets[adjacency];
                        if (Visited[neighbor] != 0 || !CanUsePolygon(neighbor))
                        {
                            continue;
                        }

                        double candidate = Distances[current] + EdgeCost(adjacency);
                        if (candidate < Distances[neighbor] ||
                            (candidate == Distances[neighbor] && current < Previous[neighbor]))
                        {
                            if (!math.isfinite(Distances[neighbor]))
                            {
                                diagnostics.NodesDiscovered++;
                            }

                            Distances[neighbor] = candidate;
                            Previous[neighbor] = current;
                            PreviousAdjacency[neighbor] = adjacency;
                        }
                    }
                }
            }

            private void ExecuteForward(bool useHeuristic, bool useAlt, ref NativeSearchDiagnostics diagnostics)
            {
                ResetForward();
                if (!CanUsePolygon(StartPolygon) || !CanUsePolygon(GoalPolygon))
                {
                    return;
                }

                double firstMultiplier = AreaMultiplier * GetDistanceMultiplier(SemanticOffsets[StartPolygon]);
                if (StartPolygon == GoalPolygon)
                {
                    CompleteDirect(firstMultiplier);
                    return;
                }

                int heapCount = 0;
                double startCost = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                                   math.distance(Start, Centroids[StartPolygon]) * firstMultiplier;
                Distances[StartPolygon] = startCost;
                double heuristic = useHeuristic ? HeuristicToGoal(StartPolygon, useAlt, ref diagnostics) : 0d;
                HeapPushOrDecrease(false, StartPolygon, startCost + heuristic, startCost,
                    ref heapCount, ref diagnostics);
                diagnostics.NodesDiscovered++;
                double bestGoal = double.PositiveInfinity;

                while (heapCount > 0)
                {
                    if (useHeuristic && math.isfinite(bestGoal) && HeapPrimary[HeapNodes[0]] > bestGoal)
                    {
                        break;
                    }

                    int current = HeapPop(false, ref heapCount, ref diagnostics);
                    if (Visited[current] != 0)
                    {
                        continue;
                    }

                    Visited[current] = 1;
                    diagnostics.NodesExpanded++;
                    if (current == GoalPolygon)
                    {
                        bestGoal = math.min(bestGoal, Distances[current] + GoalLeg());
                        if (!useHeuristic)
                        {
                            break;
                        }

                        continue;
                    }

                    int adjacencyEnd = AdjacencyStarts[current] + AdjacencyCounts[current];
                    for (int adjacency = AdjacencyStarts[current]; adjacency < adjacencyEnd; adjacency++)
                    {
                        diagnostics.EdgesExamined++;
                        if (AdjacencyEnabled[adjacency] == 0)
                        {
                            continue;
                        }

                        int neighbor = AdjacencyTargets[adjacency];
                        if (!CanUsePolygon(neighbor))
                        {
                            continue;
                        }

                        double candidate = Distances[current] + EdgeCost(adjacency);
                        bool better = candidate < Distances[neighbor];
                        bool tie = Visited[neighbor] == 0 &&
                                   candidate == Distances[neighbor] &&
                                   IsCanonicalPredecessor(current, neighbor);
                        if (!better && !tie)
                        {
                            continue;
                        }

                        if (!math.isfinite(Distances[neighbor]))
                        {
                            diagnostics.NodesDiscovered++;
                        }

                        if (better)
                        {
                            Distances[neighbor] = candidate;
                            Visited[neighbor] = 0;
                        }

                        Previous[neighbor] = current;
                        PreviousAdjacency[neighbor] = adjacency;
                        if (better || HeapPositions[neighbor] >= 0)
                        {
                            heuristic = useHeuristic ? HeuristicToGoal(neighbor, useAlt, ref diagnostics) : 0d;
                            HeapPushOrDecrease(false, neighbor, Distances[neighbor] + heuristic,
                                Distances[neighbor], ref heapCount, ref diagnostics);
                        }
                    }
                }

                if (!math.isfinite(bestGoal) && math.isfinite(Distances[GoalPolygon]))
                {
                    bestGoal = Distances[GoalPolygon] + GoalLeg();
                }

                if (math.isfinite(bestGoal))
                {
                    ResultCost[0] = bestGoal;
                    ResultStatus[0] = 1;
                }
            }

            private void ExecuteBidirectional(bool useHeuristic, bool useAlt, ref NativeSearchDiagnostics diagnostics)
            {
                ResetForward();
                ResetReverse();
                if (!CanUsePolygon(StartPolygon) || !CanUsePolygon(GoalPolygon))
                {
                    return;
                }

                if (StartPolygon == GoalPolygon)
                {
                    ExecuteForward(useHeuristic, useAlt, ref diagnostics);
                    return;
                }

                int forwardCount = 0;
                int reverseCount = 0;
                double firstMultiplier = AreaMultiplier * GetDistanceMultiplier(SemanticOffsets[StartPolygon]);
                double startCost = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                                   math.distance(Start, Centroids[StartPolygon]) * firstMultiplier;
                double goalCost = GoalLeg();
                Distances[StartPolygon] = startCost;
                ReverseDistances[GoalPolygon] = goalCost;
                double startPotential = useHeuristic ? Potential(StartPolygon, useAlt, ref diagnostics) : 0d;
                double goalPotential = useHeuristic ? Potential(GoalPolygon, useAlt, ref diagnostics) : 0d;
                HeapPushOrDecrease(false, StartPolygon, startCost, startCost,
                    ref forwardCount, ref diagnostics);
                HeapPushOrDecrease(true, GoalPolygon, goalCost, goalCost,
                    ref reverseCount, ref diagnostics);
                diagnostics.NodesDiscovered += 2;
                double best = double.PositiveInfinity;
                bool inconsistent = false;

                // Balanced potential proof: pi=(h_t-h_s)/2 makes every forward reduced
                // edge w-pi(u)+pi(v), and its reverse traversal, nonnegative when both
                // heuristics are consistent. Frontier-key sums are therefore lower bounds.
                while (forwardCount > 0 && reverseCount > 0)
                {
                    double lowerBound = HeapPrimary[HeapNodes[0]] +
                                        ReverseHeapPrimary[ReverseHeapNodes[0]];
                    double transformedBest = useHeuristic && math.isfinite(best)
                        ? best - startPotential + goalPotential
                        : best;
                    if (lowerBound >= transformedBest)
                    {
                        break;
                    }

                    bool forward = HeapPrimary[HeapNodes[0]] <=
                                   ReverseHeapPrimary[ReverseHeapNodes[0]];
                    if (forward)
                    {
                        inconsistent = !ExpandForwardBidirectional(
                            useHeuristic, useAlt, startPotential,
                            ref forwardCount, ref best, ref diagnostics);
                    }
                    else
                    {
                        inconsistent = !ExpandReverseBidirectional(
                            useHeuristic, useAlt, goalPotential,
                            ref reverseCount, ref best, ref diagnostics);
                    }

                    if (inconsistent)
                    {
                        diagnostics.FallbackReason = SearchFallbackReason.InconsistentHeuristic;
                        diagnostics.ExecutedStrategy = SearchStrategy.AStar;
                        ExecuteForward(true, false, ref diagnostics);
                        return;
                    }
                }

                if (math.isfinite(best))
                {
                    // Finish the canonical forward plateau in this same physical job/lane.
                    ExecuteForward(useHeuristic, useAlt, ref diagnostics);
                }
            }

            private bool ExpandForwardBidirectional(
                bool useHeuristic,
                bool useAlt,
                double startPotential,
                ref int heapCount,
                ref double best,
                ref NativeSearchDiagnostics diagnostics)
            {
                int current = HeapPop(false, ref heapCount, ref diagnostics);
                if (Visited[current] != 0)
                {
                    return true;
                }

                Visited[current] = 1;
                diagnostics.NodesExpanded++;
                if (math.isfinite(ReverseDistances[current]))
                {
                    best = math.min(best, Distances[current] + ReverseDistances[current]);
                }

                int end = AdjacencyStarts[current] + AdjacencyCounts[current];
                for (int adjacency = AdjacencyStarts[current]; adjacency < end; adjacency++)
                {
                    diagnostics.EdgesExamined++;
                    if (AdjacencyEnabled[adjacency] == 0)
                    {
                        continue;
                    }

                    int neighbor = AdjacencyTargets[adjacency];
                    if (!CanUsePolygon(neighbor))
                    {
                        continue;
                    }

                    double weight = EdgeCost(adjacency);
                    if (math.isfinite(ReverseDistances[neighbor]))
                    {
                        best = math.min(best, Distances[current] + weight + ReverseDistances[neighbor]);
                    }

                    double candidate = Distances[current] + weight;
                    if (candidate >= Distances[neighbor])
                    {
                        continue;
                    }

                    if (!math.isfinite(Distances[neighbor]))
                    {
                        diagnostics.NodesDiscovered++;
                    }

                    Distances[neighbor] = candidate;
                    double key = candidate;
                    if (useHeuristic)
                    {
                        double currentPotential = Potential(current, useAlt, ref diagnostics);
                        double nextPotential = Potential(neighbor, useAlt, ref diagnostics);
                        if (weight - currentPotential + nextPotential < -1e-9d)
                        {
                            return false;
                        }

                        key = candidate - startPotential + nextPotential;
                    }

                    HeapPushOrDecrease(false, neighbor, key, candidate,
                        ref heapCount, ref diagnostics);
                }

                return true;
            }

            private bool ExpandReverseBidirectional(
                bool useHeuristic,
                bool useAlt,
                double goalPotential,
                ref int heapCount,
                ref double best,
                ref NativeSearchDiagnostics diagnostics)
            {
                int current = HeapPop(true, ref heapCount, ref diagnostics);
                if (ReverseVisited[current] != 0)
                {
                    return true;
                }

                ReverseVisited[current] = 1;
                diagnostics.NodesExpanded++;
                if (math.isfinite(Distances[current]))
                {
                    best = math.min(best, Distances[current] + ReverseDistances[current]);
                }

                int end = ReverseStarts[current] + ReverseCounts[current];
                for (int reverse = ReverseStarts[current]; reverse < end; reverse++)
                {
                    diagnostics.EdgesExamined++;
                    int adjacency = ReverseAdjacencies[reverse];
                    if (AdjacencyEnabled[adjacency] == 0)
                    {
                        continue;
                    }

                    int neighbor = AdjacencySources[adjacency];
                    if (!CanUsePolygon(neighbor))
                    {
                        continue;
                    }

                    double weight = EdgeCost(adjacency);
                    if (math.isfinite(Distances[neighbor]))
                    {
                        best = math.min(best, Distances[neighbor] + weight + ReverseDistances[current]);
                    }

                    double candidate = ReverseDistances[current] + weight;
                    if (candidate >= ReverseDistances[neighbor])
                    {
                        continue;
                    }

                    if (!math.isfinite(ReverseDistances[neighbor]))
                    {
                        diagnostics.NodesDiscovered++;
                    }

                    ReverseDistances[neighbor] = candidate;
                    double key = candidate;
                    if (useHeuristic)
                    {
                        double previousPotential = Potential(neighbor, useAlt, ref diagnostics);
                        double currentPotential = Potential(current, useAlt, ref diagnostics);
                        if (weight - previousPotential + currentPotential < -1e-9d)
                        {
                            return false;
                        }

                        key = candidate + goalPotential - previousPotential;
                    }

                    HeapPushOrDecrease(true, neighbor, key, candidate,
                        ref heapCount, ref diagnostics);
                }

                return true;
            }

            private void ResetForward()
            {
                int end = AreaPolygonStart + AreaPolygonCount;
                for (int index = AreaPolygonStart; index < end; index++)
                {
                    Distances[index] = double.PositiveInfinity;
                    Previous[index] = -1;
                    PreviousAdjacency[index] = -1;
                    Visited[index] = 0;
                    if (index < HeapPositions.Length)
                    {
                        HeapPositions[index] = -1;
                    }
                }
            }

            private void ResetReverse()
            {
                int end = AreaPolygonStart + AreaPolygonCount;
                for (int index = AreaPolygonStart; index < end; index++)
                {
                    ReverseDistances[index] = double.PositiveInfinity;
                    ReverseVisited[index] = 0;
                    ReverseHeapPositions[index] = -1;
                }
            }

            private void CompleteDirect(double multiplier)
            {
                double cost = BaseCost + GetEntryPenalty(SemanticOffsets[StartPolygon]) +
                              math.distance(Start, Goal) * multiplier;
                if (math.isfinite(cost))
                {
                    ResultCost[0] = cost;
                    ResultStatus[0] = 1;
                }
            }

            private void CompleteGoal(double costToCentroid)
            {
                double cost = costToCentroid + GoalLeg();
                if (math.isfinite(cost))
                {
                    ResultCost[0] = cost;
                    ResultStatus[0] = 1;
                }
            }

            private int FindCheapestReference(int end)
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

            private bool IsCanonicalPredecessor(int predecessor, int node)
            {
                int previous = Previous[node];
                if (previous < 0)
                {
                    return true;
                }

                return predecessor < previous &&
                       (Distances[predecessor] < Distances[node] ||
                        (Distances[predecessor] == Distances[node] && predecessor < node));
            }

            private double EdgeCost(int adjacency)
            {
                int from = AdjacencySources[adjacency];
                int to = AdjacencyTargets[adjacency];
                return math.distance(Centroids[from], Centroids[to]) * AreaMultiplier *
                       GetDistanceMultiplier(SemanticOffsets[to]) +
                       GetEntryPenalty(SemanticOffsets[to]);
            }

            private double GoalLeg() =>
                math.distance(Centroids[GoalPolygon], Goal) * AreaMultiplier *
                GetDistanceMultiplier(SemanticOffsets[GoalPolygon]);

            private double HeuristicToGoal(int polygon, bool useAlt, ref NativeSearchDiagnostics diagnostics)
            {
                diagnostics.HeuristicEvaluations++;
                double geometric = math.distance(Centroids[polygon], Goal) * MinimumMultiplier;
                if (!useAlt || LandmarkCount == 0)
                {
                    return geometric;
                }

                int node = polygon - AreaPolygonStart;
                int target = GoalPolygon - AreaPolygonStart;
                double bound = 0d;
                for (int landmark = 0; landmark < LandmarkCount; landmark++)
                {
                    int offset = landmark * AreaPolygonCount;
                    double lt = LandmarkDistancesFrom[offset + target];
                    double ln = LandmarkDistancesFrom[offset + node];
                    if (math.isfinite(lt) && math.isfinite(ln))
                    {
                        bound = math.max(bound, lt - ln);
                    }

                    double nl = LandmarkDistancesTo[offset + node];
                    double tl = LandmarkDistancesTo[offset + target];
                    if (math.isfinite(nl) && math.isfinite(tl))
                    {
                        bound = math.max(bound, nl - tl);
                    }
                }

                return math.max(geometric, math.max(0d, bound) + GoalLeg());
            }

            private double HeuristicFromStart(int polygon, bool useAlt, ref NativeSearchDiagnostics diagnostics)
            {
                diagnostics.HeuristicEvaluations++;
                double geometric = math.distance(Start, Centroids[polygon]) * MinimumMultiplier;
                if (!useAlt || LandmarkCount == 0)
                {
                    return geometric;
                }

                int node = polygon - AreaPolygonStart;
                int source = StartPolygon - AreaPolygonStart;
                double bound = 0d;
                for (int landmark = 0; landmark < LandmarkCount; landmark++)
                {
                    int offset = landmark * AreaPolygonCount;
                    double ln = LandmarkDistancesFrom[offset + node];
                    double ls = LandmarkDistancesFrom[offset + source];
                    if (math.isfinite(ln) && math.isfinite(ls))
                    {
                        bound = math.max(bound, ln - ls);
                    }

                    double sl = LandmarkDistancesTo[offset + source];
                    double nl = LandmarkDistancesTo[offset + node];
                    if (math.isfinite(sl) && math.isfinite(nl))
                    {
                        bound = math.max(bound, sl - nl);
                    }
                }

                return math.max(geometric, math.max(0d, bound));
            }

            private double Potential(int polygon, bool useAlt, ref NativeSearchDiagnostics diagnostics) =>
                (HeuristicToGoal(polygon, useAlt, ref diagnostics) -
                 HeuristicFromStart(polygon, useAlt, ref diagnostics)) * 0.5d;

            private void HeapPushOrDecrease(
                bool reverse,
                int node,
                double primary,
                double secondary,
                ref int count,
                ref NativeSearchDiagnostics diagnostics)
            {
                NativeArray<int> nodes = reverse ? ReverseHeapNodes : HeapNodes;
                NativeArray<int> positions = reverse ? ReverseHeapPositions : HeapPositions;
                NativeArray<double> primaries = reverse ? ReverseHeapPrimary : HeapPrimary;
                NativeArray<double> secondaries = reverse ? ReverseHeapSecondary : HeapSecondary;
                int position = positions[node];
                if (position >= 0)
                {
                    if (!ComesBefore(primary, secondary, node,
                        primaries[node], secondaries[node], node))
                    {
                        return;
                    }

                    primaries[node] = primary;
                    secondaries[node] = secondary;
                    SiftUp(nodes, positions, primaries, secondaries, position);
                    diagnostics.HeapPushes++;
                    return;
                }

                nodes[count] = node;
                positions[node] = count;
                primaries[node] = primary;
                secondaries[node] = secondary;
                SiftUp(nodes, positions, primaries, secondaries, count);
                count++;
                diagnostics.HeapPushes++;
                diagnostics.MaximumFrontier = math.max(diagnostics.MaximumFrontier, count);
            }

            private int HeapPop(bool reverse, ref int count, ref NativeSearchDiagnostics diagnostics)
            {
                NativeArray<int> nodes = reverse ? ReverseHeapNodes : HeapNodes;
                NativeArray<int> positions = reverse ? ReverseHeapPositions : HeapPositions;
                NativeArray<double> primaries = reverse ? ReverseHeapPrimary : HeapPrimary;
                NativeArray<double> secondaries = reverse ? ReverseHeapSecondary : HeapSecondary;
                int node = nodes[0];
                positions[node] = -1;
                count--;
                if (count > 0)
                {
                    int replacement = nodes[count];
                    nodes[0] = replacement;
                    positions[replacement] = 0;
                    SiftDown(nodes, positions, primaries, secondaries, 0, count);
                }

                diagnostics.HeapPops++;
                return node;
            }

            private static void SiftUp(
                NativeArray<int> nodes,
                NativeArray<int> positions,
                NativeArray<double> primaries,
                NativeArray<double> secondaries,
                int position)
            {
                int node = nodes[position];
                while (position > 0)
                {
                    int parent = (position - 1) / 2;
                    int parentNode = nodes[parent];
                    if (!ComesBefore(primaries[node], secondaries[node], node,
                        primaries[parentNode], secondaries[parentNode], parentNode))
                    {
                        break;
                    }

                    nodes[position] = parentNode;
                    positions[parentNode] = position;
                    position = parent;
                }

                nodes[position] = node;
                positions[node] = position;
            }

            private static void SiftDown(
                NativeArray<int> nodes,
                NativeArray<int> positions,
                NativeArray<double> primaries,
                NativeArray<double> secondaries,
                int position,
                int count)
            {
                int node = nodes[position];
                while (true)
                {
                    int left = position * 2 + 1;
                    if (left >= count)
                    {
                        break;
                    }

                    int right = left + 1;
                    int best = left;
                    if (right < count && ComesBefore(
                        primaries[nodes[right]], secondaries[nodes[right]], nodes[right],
                        primaries[nodes[left]], secondaries[nodes[left]], nodes[left]))
                    {
                        best = right;
                    }

                    int bestNode = nodes[best];
                    if (!ComesBefore(primaries[bestNode], secondaries[bestNode], bestNode,
                        primaries[node], secondaries[node], node))
                    {
                        break;
                    }

                    nodes[position] = bestNode;
                    positions[bestNode] = position;
                    position = best;
                }

                nodes[position] = node;
                positions[node] = position;
            }

            private static bool ComesBefore(
                double leftPrimary,
                double leftSecondary,
                int leftNode,
                double rightPrimary,
                double rightSecondary,
                int rightNode)
            {
                if (leftPrimary != rightPrimary)
                {
                    return leftPrimary < rightPrimary;
                }

                return leftSecondary != rightSecondary
                    ? leftSecondary < rightSecondary
                    : leftNode < rightNode;
            }

            private bool CanUsePolygon(int polygon) =>
                PolygonEnabled[polygon] != 0 &&
                Allows(1, SemanticOffsets[polygon], CapabilityOffsets[polygon]);

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
