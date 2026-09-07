using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class BurstPolygonSearchTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[index]);
            }

            _assets.Clear();
        }

        [Test]
        public void JobKernelMatchesManagedReferenceCorridorAndCost()
        {
            KernelFixture fixture = CreateFixture();
            using var data = new RuntimeDataScope(fixture.Bake);
            NavigationRuntimeData runtime = data.Value;
            int areaIndex = runtime.AreaById[fixture.Area.Id];
            int startPolygon = runtime.PolygonById[fixture.Start.Id];
            int goalPolygon = runtime.PolygonById[fixture.Goal.Id];
            CompiledAreaRecord area = runtime.Areas[areaIndex];
            var start = new Vector3(0.25f, 0f, 1f);
            var goal = new Vector3(2.75f, 0f, 1f);
            double areaMultiplier = fixture.Policy.GetDistanceMultiplier(
                runtime.SemanticWords,
                area.SemanticOffset);
            double baseCost = fixture.Policy.GetEntryPenalty(
                runtime.SemanticWords,
                area.SemanticOffset);

            ReferenceResult expected = SolveReference(
                runtime,
                fixture.Policy,
                area,
                startPolygon,
                goalPolygon,
                start,
                goal,
                areaMultiplier,
                baseCost);
            bool found = runtime.PolygonSearch.TryFind(
                fixture.Policy,
                areaIndex,
                area.PolygonStart,
                area.PolygonCount,
                startPolygon,
                goalPolygon,
                new float3(start.x, start.y, start.z),
                new float3(goal.x, goal.y, goal.z),
                areaMultiplier,
                baseCost,
                out double actualCost);

            Assert.That(found, Is.EqualTo(expected.Found));
            Assert.That(actualCost, Is.EqualTo(expected.Cost).Within(1e-9d));
            CollectionAssert.AreEqual(
                expected.Corridor,
                ReadJobCorridor(runtime.PolygonSearch, startPolygon, goalPolygon));
            Assert.That(runtime.Polygons[expected.Corridor[1]].Id, Is.EqualTo(fixture.Lower.Id));
        }

        [Test]
        public void WarmedJobKernelDoesNotAllocateManagedMemory()
        {
            KernelFixture fixture = CreateFixture();
            using var data = new RuntimeDataScope(fixture.Bake);
            NavigationRuntimeData runtime = data.Value;
            int areaIndex = runtime.AreaById[fixture.Area.Id];
            int startPolygon = runtime.PolygonById[fixture.Start.Id];
            int goalPolygon = runtime.PolygonById[fixture.Goal.Id];
            CompiledAreaRecord area = runtime.Areas[areaIndex];
            var start = new float3(0.25f, 0f, 1f);
            var goal = new float3(2.75f, 0f, 1f);

            GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 16; iteration++)
            {
                Assert.That(runtime.PolygonSearch.TryFind(
                    fixture.Policy,
                    areaIndex,
                    area.PolygonStart,
                    area.PolygonCount,
                    startPolygon,
                    goalPolygon,
                    start,
                    goal,
                    1d,
                    0d,
                    out _), Is.True);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 128; iteration++)
            {
                runtime.PolygonSearch.TryFind(
                    fixture.Policy,
                    areaIndex,
                    area.PolygonStart,
                    area.PolygonCount,
                    startPolygon,
                    goalPolygon,
                    start,
                    goal,
                    1d,
                    0d,
                    out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "The warmed Burst job kernel allocated managed memory.");
        }

        [TestCase(ParityScenario.SamePolygon)]
        [TestCase(ParityScenario.MultiPolygon)]
        [TestCase(ParityScenario.Unreachable)]
        [TestCase(ParityScenario.SemanticPolicy)]
        [TestCase(ParityScenario.RuntimeMutation)]
        [TestCase(ParityScenario.EqualCostTie)]
        public void ManagedReferenceMatchesBurstAcrossRouteScenarios(ParityScenario scenario)
        {
            KernelFixture fixture = CreateFixture();
            using var data = new RuntimeDataScope(fixture.Bake);
            NavigationRuntimeData runtime = data.Value;
            int areaIndex = runtime.AreaById[fixture.Area.Id];
            int startPolygon = runtime.PolygonById[fixture.Start.Id];
            int goalPolygon = runtime.PolygonById[fixture.Goal.Id];
            CompiledTraversalPolicy policy = scenario == ParityScenario.SemanticPolicy ||
                                             scenario == ParityScenario.RuntimeMutation
                ? fixture.Policy
                : fixture.NeutralPolicy;
            var start = new Vector3(0.25f, 0f, 1f);
            var goal = new Vector3(2.75f, 0f, 1f);

            switch (scenario)
            {
                case ParityScenario.SamePolygon:
                    goalPolygon = startPolygon;
                    goal = new Vector3(0.75f, 0f, 1f);
                    break;
                case ParityScenario.Unreachable:
                    runtime.SetPolygonEnabled(runtime.PolygonById[fixture.Upper.Id], false);
                    runtime.SetPolygonEnabled(runtime.PolygonById[fixture.Lower.Id], false);
                    break;
                case ParityScenario.RuntimeMutation:
                    runtime.SetPolygonEnabled(runtime.PolygonById[fixture.Lower.Id], false);
                    break;
            }

            CompiledAreaRecord area = runtime.Areas[areaIndex];
            double areaMultiplier = policy.GetDistanceMultiplier(runtime.SemanticWords, area.SemanticOffset);
            double baseCost = policy.GetEntryPenalty(runtime.SemanticWords, area.SemanticOffset);
            ReferenceResult expected = SolveReference(
                runtime,
                policy,
                area,
                startPolygon,
                goalPolygon,
                start,
                goal,
                areaMultiplier,
                baseCost);

            bool found = runtime.PolygonSearch.TryFind(
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
                out double actualCost);

            Assert.That(found, Is.EqualTo(expected.Found));
            if (!found)
            {
                Assert.That(actualCost, Is.EqualTo(double.PositiveInfinity));
                Assert.That(expected.Corridor, Is.Empty);
                return;
            }

            Assert.That(actualCost, Is.EqualTo(expected.Cost).Within(1e-9d));
            int[] actualCorridor = ReadJobCorridor(runtime.PolygonSearch, startPolygon, goalPolygon);
            CollectionAssert.AreEqual(expected.Corridor, actualCorridor);

            if (scenario == ParityScenario.RuntimeMutation)
            {
                CollectionAssert.DoesNotContain(actualCorridor, runtime.PolygonById[fixture.Lower.Id]);
                CollectionAssert.Contains(actualCorridor, runtime.PolygonById[fixture.Upper.Id]);
            }
            else if (scenario == ParityScenario.EqualCostTie)
            {
                Assert.That(runtime.PolygonSearch.TryFind(
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
                    out _), Is.True);
                CollectionAssert.AreEqual(
                    actualCorridor,
                    ReadJobCorridor(runtime.PolygonSearch, startPolygon, goalPolygon));
            }
        }

        private KernelFixture CreateFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            SemanticId expensive = registry.Add("Expensive");
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            source.AddArea(area);
            NavigationPolygonRecord start = area.AddPolygon(Rectangle(0f, 0f, 1f, 2f));
            NavigationPolygonRecord upper = area.AddPolygon(Rectangle(1f, 1f, 2f, 2f));
            NavigationPolygonRecord lower = area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            NavigationPolygonRecord goal = area.AddPolygon(Rectangle(2f, 0f, 3f, 2f));
            var semantics = new SemanticMask(registry.SlotCapacity);
            semantics.Set(0);
            upper.SetSemantics(semantics);

            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult bakeResult = NavigationBaker.Bake(source, bake);
            Assert.That(bakeResult.Succeeded, Is.True, FormatIssues(bakeResult));
            var builder = new TraversalPolicyBuilder(registry).SetCost(expensive, 4d, 0.25d);
            Assert.That(builder.TryCompile(bake, out CompiledTraversalPolicy policy, out string error),
                Is.True, error);
            var neutralBuilder = new TraversalPolicyBuilder(registry);
            Assert.That(neutralBuilder.TryCompile(
                bake,
                out CompiledTraversalPolicy neutralPolicy,
                out string neutralError), Is.True, neutralError);
            return new KernelFixture(area, start, upper, lower, goal, bake, policy, neutralPolicy);
        }

        private static ReferenceResult SolveReference(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            CompiledAreaRecord area,
            int startPolygon,
            int goalPolygon,
            Vector3 start,
            Vector3 goal,
            double areaMultiplier,
            double baseCost)
        {
            var distance = new double[data.Polygons.Length];
            var previous = new int[data.Polygons.Length];
            var visited = new bool[data.Polygons.Length];
            int end = area.PolygonStart + area.PolygonCount;
            for (int index = area.PolygonStart; index < end; index++)
            {
                distance[index] = double.PositiveInfinity;
                previous[index] = -1;
            }

            CompiledPolygonRecord first = data.Polygons[startPolygon];
            distance[startPolygon] = baseCost +
                                     policy.GetEntryPenalty(data.SemanticWords, first.SemanticOffset) +
                                     Vector3.Distance(start, first.Centroid) * areaMultiplier *
                                     policy.GetDistanceMultiplier(data.SemanticWords, first.SemanticOffset);

            for (int iteration = 0; iteration < area.PolygonCount; iteration++)
            {
                int current = Cheapest(distance, visited, area.PolygonStart, end);
                if (current < 0)
                {
                    return ReferenceResult.NotFound;
                }

                if (current == goalPolygon)
                {
                    CompiledPolygonRecord last = data.Polygons[current];
                    double cost = distance[current] +
                                  Vector3.Distance(last.Centroid, goal) * areaMultiplier *
                                  policy.GetDistanceMultiplier(data.SemanticWords, last.SemanticOffset);
                    return new ReferenceResult(true, cost, BuildCorridor(previous, startPolygon, goalPolygon));
                }

                visited[current] = true;
                CompiledPolygonRecord polygon = data.Polygons[current];
                int adjacencyEnd = polygon.AdjacencyStart + polygon.AdjacencyCount;
                for (int adjacencyIndex = polygon.AdjacencyStart;
                     adjacencyIndex < adjacencyEnd;
                     adjacencyIndex++)
                {
                    if (!data.AdjacencyEnabled[adjacencyIndex])
                    {
                        continue;
                    }

                    int neighbor = data.Adjacencies[adjacencyIndex].ToPolygon;
                    CompiledPolygonRecord next = data.Polygons[neighbor];
                    if (visited[neighbor] || !data.PolygonEnabled[neighbor] ||
                        !policy.CanTraverse(
                            NavigationElementKind.Polygon,
                            data.SemanticWords,
                            next.SemanticOffset,
                            next.RequiredCapabilityOffset))
                    {
                        continue;
                    }

                    double candidate = distance[current] +
                                       Vector3.Distance(polygon.Centroid, next.Centroid) * areaMultiplier *
                                       policy.GetDistanceMultiplier(data.SemanticWords, next.SemanticOffset) +
                                       policy.GetEntryPenalty(data.SemanticWords, next.SemanticOffset);
                    if (candidate < distance[neighbor] ||
                        (candidate.Equals(distance[neighbor]) && current < previous[neighbor]))
                    {
                        distance[neighbor] = candidate;
                        previous[neighbor] = current;
                    }
                }
            }

            return ReferenceResult.NotFound;
        }

        private static int Cheapest(double[] distance, bool[] visited, int start, int end)
        {
            int best = -1;
            double bestCost = double.PositiveInfinity;
            for (int index = start; index < end; index++)
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

        private static int[] BuildCorridor(int[] previous, int start, int goal)
        {
            var result = new List<int> { goal };
            int current = goal;
            while (current != start)
            {
                current = previous[current];
                result.Add(current);
            }

            result.Reverse();
            return result.ToArray();
        }

        private static int[] ReadJobCorridor(BurstPolygonSearch search, int start, int goal)
        {
            var result = new List<int> { goal };
            int current = goal;
            while (current != start)
            {
                current = search.GetPrevious(current);
                Assert.That(current, Is.GreaterThanOrEqualTo(0));
                result.Add(current);
            }

            result.Reverse();
            return result.ToArray();
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static Vector3[] Rectangle(float minX, float minZ, float maxX, float maxZ)
        {
            return new[]
            {
                new Vector3(minX, 0f, minZ),
                new Vector3(minX, 0f, maxZ),
                new Vector3(maxX, 0f, maxZ),
                new Vector3(maxX, 0f, minZ)
            };
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var lines = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                lines.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private readonly struct ReferenceResult
        {
            internal ReferenceResult(bool found, double cost, int[] corridor)
            {
                Found = found;
                Cost = cost;
                Corridor = corridor;
            }

            internal bool Found { get; }
            internal double Cost { get; }
            internal int[] Corridor { get; }
            internal static ReferenceResult NotFound =>
                new ReferenceResult(false, double.PositiveInfinity, Array.Empty<int>());
        }

        private readonly struct KernelFixture
        {
            internal KernelFixture(
                NavigationAreaAsset area,
                NavigationPolygonRecord start,
                NavigationPolygonRecord upper,
                NavigationPolygonRecord lower,
                NavigationPolygonRecord goal,
                NavigationBakeAsset bake,
                CompiledTraversalPolicy policy,
                CompiledTraversalPolicy neutralPolicy)
            {
                Area = area;
                Start = start;
                Upper = upper;
                Lower = lower;
                Goal = goal;
                Bake = bake;
                Policy = policy;
                NeutralPolicy = neutralPolicy;
            }

            internal NavigationAreaAsset Area { get; }
            internal NavigationPolygonRecord Start { get; }
            internal NavigationPolygonRecord Upper { get; }
            internal NavigationPolygonRecord Lower { get; }
            internal NavigationPolygonRecord Goal { get; }
            internal NavigationBakeAsset Bake { get; }
            internal CompiledTraversalPolicy Policy { get; }
            internal CompiledTraversalPolicy NeutralPolicy { get; }
        }

        public enum ParityScenario
        {
            SamePolygon,
            MultiPolygon,
            Unreachable,
            SemanticPolicy,
            RuntimeMutation,
            EqualCostTie
        }

        private sealed class RuntimeDataScope : IDisposable
        {
            internal RuntimeDataScope(NavigationBakeAsset bake)
            {
                Value = new NavigationRuntimeData(bake, 0);
            }

            internal NavigationRuntimeData Value { get; }
            public void Dispose() => Value.Dispose();
        }
    }
}
