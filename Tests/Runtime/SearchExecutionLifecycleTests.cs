using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class SearchExecutionLifecycleTests
    {
        [TestCase((int)SearchStrategy.HeapDijkstra)]
        [TestCase((int)SearchStrategy.AStar)]
        [TestCase((int)SearchStrategy.BidirectionalDijkstra)]
        [TestCase((int)SearchStrategy.BidirectionalAStar)]
        [TestCase((int)SearchStrategy.AltAStar)]
        [TestCase((int)SearchStrategy.BidirectionalAlt)]
        public void CancelledPhysicalWorkCannotPublishDiagnosticsIntoAReusedSlot(
            int strategyValue)
        {
            var strategy = (SearchStrategy)strategyValue;
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(401, true, false);
            using var world = new NavigationWorld(
                fixture.Bake,
                0,
                1,
                new SearchExecutionOptions(strategy));
            PrepareIfNeeded(world, fixture.Policy, strategy);

            PathRequestHandle abandoned = world.Submit(fixture.Query(0, fixture.NodeCount - 1));
            AdvanceUntilInFlight(world, abandoned);
            Assert.That(world.Cancel(abandoned), Is.True);
            world.Tick(0);
            Assert.That(world.Release(abandoned), Is.True);
            Assert.That(world.IsPhysicalWorkInFlight(abandoned), Is.True,
                "Releasing the logical generation must not release its physical work.");
            Assert.That(world.TryGetSearchDiagnostics(abandoned, out _), Is.False);

            PathRequestHandle replacement = world.Submit(fixture.Query(2, 2));
            Assert.That(replacement.Slot, Is.EqualTo(abandoned.Slot));
            Assert.That(replacement.Generation, Is.Not.EqualTo(abandoned.Generation));
            Assert.That(world.TryGetSearchDiagnostics(replacement, out SearchDiagnostics initial), Is.True);
            AssertEmpty(initial);

            Complete(world, replacement);

            Assert.That(world.GetStatus(abandoned), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.GetStatus(replacement), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.TryGetSearchDiagnostics(abandoned, out _), Is.False);
            Assert.That(world.TryGetSearchDiagnostics(replacement, out SearchDiagnostics diagnostics),
                Is.True);
            Assert.That(diagnostics.RequestedStrategy, Is.EqualTo(strategy));
            Assert.That(diagnostics.ExecutedStrategy, Is.EqualTo(strategy));
            Assert.That(diagnostics.LocalSearchCount, Is.EqualTo(1));
            Assert.That(diagnostics.NodesExpanded, Is.Zero,
                "A same-polygon replacement must not inherit the abandoned search's counters.");
            Assert.That(diagnostics.EdgesExamined, Is.Zero);
            Assert.That(world.Release(replacement), Is.True);
        }

        [Test]
        public void AltAcceleratorsRemainSeparatedByPolicyFingerprint()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(211, true, false);
            SemanticRegistryAsset registry = fixture.Bake.Source.SemanticRegistry;
            SemanticDefinition costed = registry.Definitions[0];
            Assert.That(
                new TraversalPolicyBuilder(registry)
                    .SetCost(costed.Id, 0.25d, 10000d)
                    .TryCompile(fixture.Bake, out CompiledTraversalPolicy alternative, out string error),
                Is.True,
                error);
            Assert.That(alternative.Fingerprint, Is.Not.EqualTo(fixture.Policy.Fingerprint));

            int target = FindFirstPolygonWithSemantic(fixture.Bake, costed.Slot);
            PathQuery first = fixture.Query(0, target);
            var second = new PathQuery(
                first.Start,
                first.Goal,
                alternative,
                first.Priority,
                first.Output);

            double[] expected = SolveCosts(
                fixture,
                SearchStrategy.Reference03,
                false,
                first,
                second);
            SearchResult[] actual = Solve(
                fixture,
                SearchStrategy.AltAStar,
                true,
                new[] { fixture.Policy, alternative },
                first,
                second);

            Assert.That(Math.Abs(expected[0] - expected[1]), Is.GreaterThan(1e-9d),
                "The fixture must distinguish the two policy-specific distance tables.");
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.That(actual[index].Cost, Is.EqualTo(expected[index]).Within(1e-8d));
                Assert.That(actual[index].Diagnostics.RequestedStrategy,
                    Is.EqualTo(SearchStrategy.AltAStar));
                Assert.That(actual[index].Diagnostics.ExecutedStrategy,
                    Is.EqualTo(SearchStrategy.AltAStar));
                Assert.That(actual[index].Diagnostics.FallbackReason,
                    Is.EqualTo(SearchFallbackReason.None));
                Assert.That(actual[index].Diagnostics.LandmarkCount, Is.GreaterThan(0));
            }
        }

        [Test]
        public void PreparedAltDataSurvivesCopyOnWriteMutationAndOldPhysicalLease()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(307, true, false);
            using var world = new NavigationWorld(
                fixture.Bake,
                0,
                1,
                new SearchExecutionOptions(SearchStrategy.AltAStar));
            Assert.That(world.PrepareSearchAccelerator(fixture.Policy), Is.True);
            int snapshotBaseline = world.ActiveSnapshotCount;
            PathQuery query = fixture.Query(0, fixture.NodeCount - 1);

            PathRequestHandle old = world.Submit(query);
            AdvanceUntilInFlight(world, old);
            int adjacency = FindNonChainAdjacency(fixture.Bake);
            CompiledAdjacencyRecord changed = fixture.Bake.RawAdjacencies[adjacency];
            Assert.That(world.SetAdjacencyEnabled(
                fixture.Bake.RawPolygons[changed.FromPolygon].Id,
                fixture.Bake.RawPolygons[changed.ToPolygon].Id,
                false), Is.True);
            world.Tick(0);

            Assert.That(world.IsPhysicalWorkInFlight(old), Is.True);
            Assert.That(world.ActiveSnapshotCount, Is.GreaterThan(snapshotBaseline),
                "Old physical work must retain its pre-mutation snapshot.");
            Complete(world, old);
            Assert.That(world.GetStatus(old), Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(world.TryGetSearchDiagnostics(old, out SearchDiagnostics oldDiagnostics),
                Is.True);
            Assert.That(oldDiagnostics.ExecutedStrategy, Is.EqualTo(SearchStrategy.AltAStar));
            Assert.That(world.ActiveSnapshotCount, Is.EqualTo(snapshotBaseline));

            PathRequestHandle current = world.Submit(query);
            Complete(world, current);
            Assert.That(world.GetStatus(current), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.TryGetPath(current, out NavigationPathView path), Is.True);
            Assert.That(world.IsCurrent(path), Is.True);
            Assert.That(world.TryGetSearchDiagnostics(current, out SearchDiagnostics diagnostics),
                Is.True);
            Assert.That(diagnostics.ExecutedStrategy, Is.EqualTo(SearchStrategy.AltAStar));
            Assert.That(diagnostics.FallbackReason, Is.EqualTo(SearchFallbackReason.None));
            Assert.That(world.Release(old), Is.True);
            Assert.That(world.Release(current), Is.True);
            Assert.That(world.CurrentSnapshotReferenceCount, Is.EqualTo(1));
        }

        [TestCase(211, false)]
        [TestCase(41, true)]
        public void GeometricAndAltBoundsDoNotExceedReferenceRemainingCost(
            int seed,
            bool zeroCost)
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(seed, true, zeroCost);
            var data = new NavigationRuntimeData(
                fixture.Bake,
                0,
                1,
                new SearchExecutionOptions(SearchStrategy.AStar));
            SearchAcceleratorData accelerator = null;
            try
            {
                accelerator = SearchAcceleratorData.Build(data, fixture.Policy, 0, 8);
                int source = 0;
                int target = fixture.NodeCount - 1;
                var queries = new PathQuery[fixture.NodeCount * 2];
                for (int node = 0; node < fixture.NodeCount; node++)
                {
                    queries[node] = fixture.Query(node, target);
                    queries[fixture.NodeCount + node] = fixture.Query(source, node);
                }

                double[] exact = SolveCosts(
                    fixture,
                    SearchStrategy.Reference03,
                    false,
                    queries);
                CompiledAreaRecord area = data.Areas[0];
                double minimumMultiplier = FindMinimumMultiplier(data, fixture.Policy, area);
                double areaPenalty = fixture.Policy.GetEntryPenalty(
                    data.SemanticWords,
                    area.SemanticOffset);

                for (int node = 0; node < fixture.NodeCount; node++)
                {
                    CompiledPolygonRecord polygon = data.Polygons[area.PolygonStart + node];
                    double startPenalty = fixture.Policy.GetEntryPenalty(
                        data.SemanticWords,
                        polygon.SemanticOffset);
                    double exactToGoal = exact[node] - areaPenalty - startPenalty;
                    double exactFromStart = exact[fixture.NodeCount + node] - areaPenalty -
                                            fixture.Policy.GetEntryPenalty(
                                                data.SemanticWords,
                                                data.Polygons[area.PolygonStart + source].SemanticOffset);
                    double toGoal = ToGoalHeuristic(
                        data,
                        accelerator,
                        area,
                        node,
                        target,
                        minimumMultiplier);
                    double fromStart = FromStartHeuristic(
                        data,
                        accelerator,
                        area,
                        source,
                        node,
                        minimumMultiplier);

                    Assert.That(toGoal, Is.LessThanOrEqualTo(exactToGoal + 1e-7d),
                        $"forward heuristic overestimated at node {node}, seed={seed}");
                    Assert.That(fromStart, Is.LessThanOrEqualTo(exactFromStart + 1e-7d),
                        $"reverse heuristic overestimated at node {node}, seed={seed}");
                }
            }
            finally
            {
                accelerator?.Dispose();
                data.Dispose();
            }
        }

        private static SearchResult[] Solve(
            SearchGraphFixture fixture,
            SearchStrategy strategy,
            bool prepare,
            CompiledTraversalPolicy[] policies,
            params PathQuery[] queries)
        {
            using var world = new NavigationWorld(
                fixture.Bake,
                0,
                1,
                new SearchExecutionOptions(strategy));
            if (prepare)
            {
                for (int index = 0; index < policies.Length; index++)
                {
                    Assert.That(world.PrepareSearchAccelerator(policies[index]), Is.True);
                }
            }

            PathRequestHandle[] handles = world.SubmitBatch(queries);
            Complete(world, handles);
            var result = new SearchResult[handles.Length];
            for (int index = 0; index < handles.Length; index++)
            {
                Assert.That(world.GetStatus(handles[index]), Is.EqualTo(PathRequestStatus.Completed));
                Assert.That(world.TryGetPath(handles[index], out NavigationPathView path), Is.True);
                Assert.That(world.TryGetSearchDiagnostics(
                    handles[index], out SearchDiagnostics diagnostics), Is.True);
                result[index] = new SearchResult(path.TotalCost, diagnostics);
                Assert.That(world.Release(handles[index]), Is.True);
            }

            return result;
        }

        private static double[] SolveCosts(
            SearchGraphFixture fixture,
            SearchStrategy strategy,
            bool prepare,
            params PathQuery[] queries)
        {
            CompiledTraversalPolicy[] policies = prepare
                ? Array.ConvertAll(queries, query => query.Policy)
                : Array.Empty<CompiledTraversalPolicy>();
            SearchResult[] results = Solve(fixture, strategy, prepare, policies, queries);
            return Array.ConvertAll(results, result => result.Cost);
        }

        private static double FindMinimumMultiplier(
            NavigationRuntimeData data,
            CompiledTraversalPolicy policy,
            CompiledAreaRecord area)
        {
            double areaMultiplier = policy.GetDistanceMultiplier(
                data.SemanticWords,
                area.SemanticOffset);
            double minimum = double.PositiveInfinity;
            for (int local = 0; local < area.PolygonCount; local++)
            {
                CompiledPolygonRecord polygon = data.Polygons[area.PolygonStart + local];
                if (policy.CanTraverse(
                    NavigationElementKind.Polygon,
                    data.SemanticWords,
                    polygon.SemanticOffset,
                    polygon.RequiredCapabilityOffset))
                {
                    minimum = Math.Min(
                        minimum,
                        areaMultiplier * policy.GetDistanceMultiplier(
                            data.SemanticWords,
                            polygon.SemanticOffset));
                }
            }

            return double.IsInfinity(minimum) ? 0d : minimum;
        }

        private static double ToGoalHeuristic(
            NavigationRuntimeData data,
            SearchAcceleratorData accelerator,
            CompiledAreaRecord area,
            int node,
            int target,
            double minimumMultiplier)
        {
            double geometric = Vector3.Distance(
                data.Polygons[area.PolygonStart + node].Centroid,
                data.Polygons[area.PolygonStart + target].Centroid) * minimumMultiplier;
            double bound = 0d;
            for (int landmark = 0; landmark < accelerator.LandmarkCount; landmark++)
            {
                int offset = landmark * area.PolygonCount;
                double lt = accelerator.DistancesFromLandmarks[offset + target];
                double ln = accelerator.DistancesFromLandmarks[offset + node];
                if (IsFinite(lt) && IsFinite(ln))
                {
                    bound = Math.Max(bound, lt - ln);
                }

                double nl = accelerator.DistancesToLandmarks[offset + node];
                double tl = accelerator.DistancesToLandmarks[offset + target];
                if (IsFinite(nl) && IsFinite(tl))
                {
                    bound = Math.Max(bound, nl - tl);
                }
            }

            return Math.Max(geometric, Math.Max(0d, bound));
        }

        private static double FromStartHeuristic(
            NavigationRuntimeData data,
            SearchAcceleratorData accelerator,
            CompiledAreaRecord area,
            int source,
            int node,
            double minimumMultiplier)
        {
            double geometric = Vector3.Distance(
                data.Polygons[area.PolygonStart + source].Centroid,
                data.Polygons[area.PolygonStart + node].Centroid) * minimumMultiplier;
            double bound = 0d;
            for (int landmark = 0; landmark < accelerator.LandmarkCount; landmark++)
            {
                int offset = landmark * area.PolygonCount;
                double ln = accelerator.DistancesFromLandmarks[offset + node];
                double ls = accelerator.DistancesFromLandmarks[offset + source];
                if (IsFinite(ln) && IsFinite(ls))
                {
                    bound = Math.Max(bound, ln - ls);
                }

                double sl = accelerator.DistancesToLandmarks[offset + source];
                double nl = accelerator.DistancesToLandmarks[offset + node];
                if (IsFinite(sl) && IsFinite(nl))
                {
                    bound = Math.Max(bound, sl - nl);
                }
            }

            return Math.Max(geometric, Math.Max(0d, bound));
        }

        private static int FindFirstPolygonWithSemantic(NavigationBakeAsset bake, int slot)
        {
            int word = slot / 64;
            ulong bit = 1UL << (slot % 64);
            for (int index = 1; index < bake.RawPolygons.Length; index++)
            {
                if ((bake.RawSemanticWords[bake.RawPolygons[index].SemanticOffset + word] & bit) != 0UL)
                {
                    return index;
                }
            }

            Assert.Fail("The policy-separation fixture has no costed destination polygon.");
            return -1;
        }

        private static int FindNonChainAdjacency(NavigationBakeAsset bake)
        {
            for (int index = 0; index < bake.RawAdjacencies.Length; index++)
            {
                CompiledAdjacencyRecord adjacency = bake.RawAdjacencies[index];
                if (adjacency.ToPolygon != adjacency.FromPolygon + 1)
                {
                    return index;
                }
            }

            Assert.Fail("The mutation fixture has no non-chain adjacency.");
            return -1;
        }

        private static void PrepareIfNeeded(
            NavigationWorld world,
            CompiledTraversalPolicy policy,
            SearchStrategy strategy)
        {
            if (strategy == SearchStrategy.AltAStar ||
                strategy == SearchStrategy.BidirectionalAlt)
            {
                Assert.That(world.PrepareSearchAccelerator(policy), Is.True);
            }
        }

        private static void AdvanceUntilInFlight(
            NavigationWorld world,
            PathRequestHandle handle)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(64);
                if (world.GetStatus(handle) == PathRequestStatus.RunningLocal &&
                    world.IsPhysicalWorkInFlight(handle))
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("The request did not schedule physical search work within 10 seconds.");
        }

        private static void Complete(NavigationWorld world, params PathRequestHandle[] handles)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15d))
            {
                world.Tick(64);
                bool terminal = true;
                for (int index = 0; index < handles.Length; index++)
                {
                    PathRequestStatus status = world.GetStatus(handles[index]);
                    terminal &= status == PathRequestStatus.Completed ||
                                status == PathRequestStatus.Failed ||
                                status == PathRequestStatus.Cancelled ||
                                status == PathRequestStatus.Stale;
                }

                if (terminal)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("Search execution lifecycle request did not complete within 15 seconds.");
        }

        private static void AssertEmpty(SearchDiagnostics diagnostics)
        {
            Assert.That(diagnostics.NodesDiscovered, Is.Zero);
            Assert.That(diagnostics.NodesExpanded, Is.Zero);
            Assert.That(diagnostics.EdgesExamined, Is.Zero);
            Assert.That(diagnostics.HeapPushes, Is.Zero);
            Assert.That(diagnostics.HeapPops, Is.Zero);
            Assert.That(diagnostics.HeuristicEvaluations, Is.Zero);
            Assert.That(diagnostics.LocalSearchCount, Is.Zero);
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private readonly struct SearchResult
        {
            internal SearchResult(double cost, SearchDiagnostics diagnostics)
            {
                Cost = cost;
                Diagnostics = diagnostics;
            }

            internal double Cost { get; }
            internal SearchDiagnostics Diagnostics { get; }
        }
    }
}
