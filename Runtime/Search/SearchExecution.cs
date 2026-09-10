using System;

namespace NotRealGames.Areafinder
{
    internal enum SearchStrategy : byte
    {
        Reference03,
        HeapDijkstra,
        AStar,
        BidirectionalDijkstra,
        BidirectionalAStar,
        AltAStar,
        BidirectionalAlt
    }

    internal enum SearchFallbackReason : byte
    {
        None,
        AcceleratorUnavailable,
        AcceleratorIncompatible,
        InconsistentHeuristic
    }

    internal readonly struct SearchExecutionOptions
    {
        internal SearchExecutionOptions(SearchStrategy strategy, int landmarkCount = 8)
        {
            if (!Enum.IsDefined(typeof(SearchStrategy), strategy))
            {
                throw new ArgumentOutOfRangeException(nameof(strategy));
            }

            if (landmarkCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(landmarkCount));
            }

            Strategy = strategy;
            LandmarkCount = landmarkCount;
        }

        internal SearchStrategy Strategy { get; }
        internal int LandmarkCount { get; }

        internal static SearchExecutionOptions Production =>
            new SearchExecutionOptions(SearchStrategy.AStar, 8);
    }

    internal struct SearchDiagnostics
    {
        internal SearchStrategy RequestedStrategy { get; set; }
        internal SearchStrategy ExecutedStrategy { get; set; }
        internal SearchFallbackReason FallbackReason { get; set; }
        internal long NodesDiscovered { get; set; }
        internal long NodesExpanded { get; set; }
        internal long EdgesExamined { get; set; }
        internal long HeapPushes { get; set; }
        internal long HeapPops { get; set; }
        internal long MaximumFrontierSize { get; set; }
        internal long HeuristicEvaluations { get; set; }
        internal int LandmarkCount { get; set; }
        internal long ScratchBytes { get; set; }
        internal long AcceleratorBytes { get; set; }
        internal double PreprocessingMilliseconds { get; set; }
        internal int LocalSearchCount { get; set; }

        internal void Add(SearchDiagnostics value)
        {
            RequestedStrategy = value.RequestedStrategy;
            ExecutedStrategy = value.ExecutedStrategy;
            if (FallbackReason == SearchFallbackReason.None)
            {
                FallbackReason = value.FallbackReason;
            }

            NodesDiscovered += value.NodesDiscovered;
            NodesExpanded += value.NodesExpanded;
            EdgesExamined += value.EdgesExamined;
            HeapPushes += value.HeapPushes;
            HeapPops += value.HeapPops;
            MaximumFrontierSize = Math.Max(MaximumFrontierSize, value.MaximumFrontierSize);
            HeuristicEvaluations += value.HeuristicEvaluations;
            LandmarkCount = Math.Max(LandmarkCount, value.LandmarkCount);
            ScratchBytes = Math.Max(ScratchBytes, value.ScratchBytes);
            AcceleratorBytes = Math.Max(AcceleratorBytes, value.AcceleratorBytes);
            PreprocessingMilliseconds = Math.Max(
                PreprocessingMilliseconds,
                value.PreprocessingMilliseconds);
            LocalSearchCount += value.LocalSearchCount;
        }
    }
}
