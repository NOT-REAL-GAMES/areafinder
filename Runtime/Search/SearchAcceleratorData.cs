using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    internal sealed class SearchAcceleratorData : IDisposable
    {
        private int _leases;

        private SearchAcceleratorData(
            int areaIndex,
            int areaPolygonStart,
            int areaPolygonCount,
            ulong policyFingerprint,
            NativeArray<int> landmarks,
            NativeArray<double> distancesFromLandmarks,
            NativeArray<double> distancesToLandmarks,
            double preprocessingMilliseconds)
        {
            AreaIndex = areaIndex;
            AreaPolygonStart = areaPolygonStart;
            AreaPolygonCount = areaPolygonCount;
            PolicyFingerprint = policyFingerprint;
            Landmarks = landmarks;
            DistancesFromLandmarks = distancesFromLandmarks;
            DistancesToLandmarks = distancesToLandmarks;
            PreprocessingMilliseconds = preprocessingMilliseconds;
        }

        internal int AreaIndex { get; }
        internal int AreaPolygonStart { get; }
        internal int AreaPolygonCount { get; }
        internal ulong PolicyFingerprint { get; }
        internal NativeArray<int> Landmarks { get; private set; }
        internal NativeArray<double> DistancesFromLandmarks { get; private set; }
        internal NativeArray<double> DistancesToLandmarks { get; private set; }
        internal double PreprocessingMilliseconds { get; }
        internal int LandmarkCount => Landmarks.IsCreated ? Landmarks.Length : 0;
        internal long ByteCount =>
            (long)LandmarkCount * sizeof(int) +
            (long)(DistancesFromLandmarks.Length + DistancesToLandmarks.Length) * sizeof(double);

        internal bool IsCompatible(
            int areaIndex,
            int areaPolygonStart,
            int areaPolygonCount,
            ulong policyFingerprint)
        {
            long expectedDistanceCount = (long)LandmarkCount * areaPolygonCount;
            return Landmarks.IsCreated && LandmarkCount > 0 &&
                   DistancesFromLandmarks.IsCreated && DistancesToLandmarks.IsCreated &&
                   AreaIndex == areaIndex && AreaPolygonStart == areaPolygonStart &&
                   AreaPolygonCount == areaPolygonCount &&
                   PolicyFingerprint == policyFingerprint &&
                   DistancesFromLandmarks.Length == expectedDistanceCount &&
                   DistancesToLandmarks.Length == expectedDistanceCount;
        }

        internal static SearchAcceleratorData Build(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            int areaIndex,
            int requestedLandmarks)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            CompiledAreaRecord area = data.Areas[areaIndex];
            var stopwatch = Stopwatch.StartNew();
            bool[] eligible = BuildEligibility(data, policy, area);
            int[] selected = SelectLandmarks(data, area, eligible, requestedLandmarks);
            var from = new NativeArray<double>(selected.Length * area.PolygonCount, Allocator.Persistent);
            var to = new NativeArray<double>(selected.Length * area.PolygonCount, Allocator.Persistent);
            var landmarks = new NativeArray<int>(selected, Allocator.Persistent);
            double areaMultiplier = policy.GetDistanceMultiplier(data.SemanticWords, area.SemanticOffset);

            for (int landmark = 0; landmark < selected.Length; landmark++)
            {
                double[] forward = Solve(data, policy, area, eligible, selected[landmark], false, areaMultiplier);
                double[] reverse = Solve(data, policy, area, eligible, selected[landmark], true, areaMultiplier);
                int offset = landmark * area.PolygonCount;
                for (int local = 0; local < area.PolygonCount; local++)
                {
                    from[offset + local] = forward[local];
                    to[offset + local] = reverse[local];
                }
            }

            stopwatch.Stop();
            return new SearchAcceleratorData(
                areaIndex,
                area.PolygonStart,
                area.PolygonCount,
                policy.Fingerprint,
                landmarks,
                from,
                to,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        internal void Acquire()
        {
            if (!Landmarks.IsCreated)
            {
                throw new ObjectDisposedException(nameof(SearchAcceleratorData));
            }

            _leases++;
        }

        internal void Release()
        {
            if (_leases <= 0)
            {
                throw new InvalidOperationException("The search accelerator lease count underflowed.");
            }

            _leases--;
        }

        public void Dispose()
        {
            if (_leases != 0)
            {
                throw new InvalidOperationException("Search accelerator data is still leased by physical work.");
            }

            if (Landmarks.IsCreated)
            {
                Landmarks.Dispose();
                Landmarks = default;
            }

            if (DistancesFromLandmarks.IsCreated)
            {
                DistancesFromLandmarks.Dispose();
                DistancesFromLandmarks = default;
            }

            if (DistancesToLandmarks.IsCreated)
            {
                DistancesToLandmarks.Dispose();
                DistancesToLandmarks = default;
            }
        }

        private static bool[] BuildEligibility(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            CompiledAreaRecord area)
        {
            var result = new bool[area.PolygonCount];
            for (int local = 0; local < area.PolygonCount; local++)
            {
                CompiledPolygonRecord polygon = data.Polygons[area.PolygonStart + local];
                result[local] = policy.CanTraverse(
                    NavigationElementKind.Polygon,
                    data.SemanticWords,
                    polygon.SemanticOffset,
                    polygon.RequiredCapabilityOffset);
            }

            return result;
        }

        private static int[] SelectLandmarks(
            NavigationRuntimeData data,
            CompiledAreaRecord area,
            bool[] eligible,
            int requested)
        {
            var result = new List<int>(Math.Min(requested, area.PolygonCount));
            for (int local = 0; local < eligible.Length; local++)
            {
                if (eligible[local])
                {
                    result.Add(area.PolygonStart + local);
                    break;
                }
            }

            while (result.Count < requested)
            {
                int best = -1;
                double bestDistance = double.NegativeInfinity;
                for (int local = 0; local < eligible.Length; local++)
                {
                    int polygon = area.PolygonStart + local;
                    if (!eligible[local] || result.Contains(polygon))
                    {
                        continue;
                    }

                    double nearest = double.PositiveInfinity;
                    Vector3 centroid = data.Polygons[polygon].Centroid;
                    for (int selected = 0; selected < result.Count; selected++)
                    {
                        double distance = (centroid - data.Polygons[result[selected]].Centroid).sqrMagnitude;
                        nearest = Math.Min(nearest, distance);
                    }

                    if (nearest > bestDistance || (nearest.Equals(bestDistance) && polygon < best))
                    {
                        best = polygon;
                        bestDistance = nearest;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                result.Add(best);
            }

            return result.ToArray();
        }

        private static double[] Solve(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            CompiledAreaRecord area,
            bool[] eligible,
            int landmark,
            bool reverse,
            double areaMultiplier)
        {
            var distance = new double[area.PolygonCount];
            var settled = new bool[area.PolygonCount];
            Array.Fill(distance, double.PositiveInfinity);
            distance[landmark - area.PolygonStart] = 0d;

            for (int iteration = 0; iteration < area.PolygonCount; iteration++)
            {
                int currentLocal = -1;
                double best = double.PositiveInfinity;
                for (int local = 0; local < area.PolygonCount; local++)
                {
                    if (!settled[local] && (distance[local] < best ||
                        (distance[local].Equals(best) &&
                         (currentLocal < 0 || local < currentLocal))))
                    {
                        currentLocal = local;
                        best = distance[local];
                    }
                }

                if (currentLocal < 0 || double.IsInfinity(best))
                {
                    break;
                }

                settled[currentLocal] = true;
                int current = area.PolygonStart + currentLocal;
                for (int adjacencyIndex = 0; adjacencyIndex < data.Adjacencies.Length; adjacencyIndex++)
                {
                    CompiledAdjacencyRecord adjacency = data.Adjacencies[adjacencyIndex];
                    int from = reverse ? adjacency.ToPolygon : adjacency.FromPolygon;
                    int to = reverse ? adjacency.FromPolygon : adjacency.ToPolygon;
                    if (from != current || to < area.PolygonStart ||
                        to >= area.PolygonStart + area.PolygonCount)
                    {
                        continue;
                    }

                    int toLocal = to - area.PolygonStart;
                    if (!eligible[toLocal] || settled[toLocal])
                    {
                        continue;
                    }

                    int originalTarget = adjacency.ToPolygon;
                    CompiledPolygonRecord target = data.Polygons[originalTarget];
                    double weight = Vector3.Distance(
                                        data.Polygons[adjacency.FromPolygon].Centroid,
                                        target.Centroid) * areaMultiplier *
                                    policy.GetDistanceMultiplier(data.SemanticWords, target.SemanticOffset) +
                                    policy.GetEntryPenalty(data.SemanticWords, target.SemanticOffset);
                    double candidate = best + weight;
                    if (candidate < distance[toLocal])
                    {
                        distance[toLocal] = candidate;
                    }
                }
            }

            return distance;
        }

    }
}
