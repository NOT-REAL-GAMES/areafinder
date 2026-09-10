using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class SearchStrategyParityTests
    {
        private static readonly SearchStrategy[] OptimizedStrategies =
        {
            SearchStrategy.HeapDijkstra,
            SearchStrategy.AStar,
            SearchStrategy.BidirectionalDijkstra,
            SearchStrategy.BidirectionalAStar,
            SearchStrategy.AltAStar,
            SearchStrategy.BidirectionalAlt
        };

        [Test]
        public void EveryOptimizedStrategyMatchesReferenceOnEqualCostDirectedGraph()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.EqualCostDiamond();
            AssertAllStrategiesMatch(fixture, fixture.Query(0, 3));
        }

        [Test]
        public void EveryOptimizedStrategyMatchesReferenceOnDirectedIntersectionTrap()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.DirectedIntersectionTrap();
            AssertAllStrategiesMatch(fixture, fixture.Query(0, 6));
        }

        [Test]
        public void EveryOptimizedStrategyMatchesReferenceAcrossAZeroCostCycle()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.ZeroCostCycleTrap();
            AssertAllStrategiesMatch(fixture, fixture.Query(3, 0));
        }

        [TestCase(7)]
        [TestCase(19)]
        [TestCase(41)]
        [TestCase(73)]
        public void EveryOptimizedStrategyMatchesReferenceOnSeededDirectedGraph(int seed)
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(seed, true, seed == 41);
            AssertAllStrategiesMatch(fixture, fixture.Query(0, fixture.NodeCount - 1));
        }

        [Test]
        public void EveryOptimizedStrategyMatchesReferenceAcrossGeneratedGraphCorpus()
        {
            int[] seeds = { 503, 509, 521, 523, 541, 547, 557, 563, 569, 571, 577, 587 };
            for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
            {
                int seed = seeds[seedIndex];
                bool connected = seedIndex % 3 != 0;
                bool zeroCost = seedIndex % 4 == 0;
                using SearchGraphFixture fixture = SearchGraphFixture.Seeded(
                    seed,
                    connected,
                    zeroCost);
                var queries = new[]
                {
                    fixture.Query(0, fixture.NodeCount - 1),
                    fixture.Query(1, 7),
                    fixture.Query(8, fixture.NodeCount - 1),
                    fixture.Query(fixture.NodeCount - 1, seed % 5)
                };
                SearchOutcome[] expected = RunBatch(
                    fixture,
                    queries,
                    SearchStrategy.Reference03,
                    1,
                    false);

                for (int strategyIndex = 0;
                     strategyIndex < OptimizedStrategies.Length;
                     strategyIndex++)
                {
                    SearchStrategy strategy = OptimizedStrategies[strategyIndex];
                    SearchOutcome[] actual = RunBatch(
                        fixture,
                        queries,
                        strategy,
                        1,
                        IsAlt(strategy));
                    for (int queryIndex = 0; queryIndex < queries.Length; queryIndex++)
                    {
                        AssertExecuted(actual[queryIndex], strategy,
                            $"{fixture.Description}, query={queryIndex}");
                        AssertEquivalent(
                            expected[queryIndex],
                            actual[queryIndex],
                            $"{fixture.Description}{Environment.NewLine}" +
                            $"strategy={strategy}, query={queryIndex}");
                    }
                }
            }
        }

        [Test]
        public void EveryOptimizedStrategyMatchesReferenceWhenDestinationIsUnreachable()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(107, false, false);
            AssertAllStrategiesMatch(fixture, fixture.Query(0, fixture.NodeCount - 1));
        }

        [Test]
        public void MissingLandmarkDataFallsBackDeterministically()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.EqualCostDiamond();
            AssertFallback(fixture, SearchStrategy.AltAStar, SearchStrategy.AStar);
            AssertFallback(
                fixture,
                SearchStrategy.BidirectionalAlt,
                SearchStrategy.BidirectionalAStar);
        }

        [Test]
        public void PreparedLandmarkDataExecutesRequestedStrategy()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(211, true, false);
            AssertPrepared(fixture, SearchStrategy.AltAStar);
            AssertPrepared(fixture, SearchStrategy.BidirectionalAlt);
        }

        private static void AssertFallback(
            SearchGraphFixture fixture,
            SearchStrategy requested,
            SearchStrategy fallback)
        {
            SearchOutcome outcome = Run(fixture, fixture.Query(0, 3), requested, 1, false);

            Assert.That(outcome.Status, Is.EqualTo(PathRequestStatus.Completed), fixture.Description);
            Assert.That(outcome.Diagnostics.RequestedStrategy, Is.EqualTo(requested));
            Assert.That(outcome.Diagnostics.ExecutedStrategy, Is.EqualTo(fallback));
            Assert.That(outcome.Diagnostics.FallbackReason,
                Is.EqualTo(SearchFallbackReason.AcceleratorUnavailable));
        }

        private static void AssertPrepared(SearchGraphFixture fixture, SearchStrategy strategy)
        {
            SearchOutcome outcome = Run(
                fixture,
                fixture.Query(0, fixture.NodeCount - 1),
                strategy,
                1,
                true);

            Assert.That(outcome.Status, Is.EqualTo(PathRequestStatus.Completed), fixture.Description);
            Assert.That(outcome.Diagnostics.RequestedStrategy, Is.EqualTo(strategy));
            Assert.That(outcome.Diagnostics.ExecutedStrategy, Is.EqualTo(strategy));
            Assert.That(outcome.Diagnostics.FallbackReason, Is.EqualTo(SearchFallbackReason.None));
            Assert.That(outcome.Diagnostics.LandmarkCount, Is.GreaterThan(0));
            Assert.That(outcome.Diagnostics.AcceleratorBytes, Is.GreaterThan(0));
        }

        [Test]
        public void CapFourProducesTheSameRoutesAsCapOneForEveryStrategy()
        {
            using SearchGraphFixture fixture = SearchGraphFixture.Seeded(307, true, false);
            var queries = new PathQuery[8];
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = fixture.Query(index, fixture.NodeCount - 1);
            }

            for (int index = 0; index < OptimizedStrategies.Length; index++)
            {
                SearchStrategy strategy = OptimizedStrategies[index];
                SearchOutcome[] serial = RunBatch(fixture, queries, strategy, 1);
                SearchOutcome[] concurrent = RunBatch(fixture, queries, strategy, 4);
                for (int query = 0; query < queries.Length; query++)
                {
                    AssertExecuted(serial[query], strategy,
                        $"{fixture.Description}, cap=1, query={query}");
                    AssertExecuted(concurrent[query], strategy,
                        $"{fixture.Description}, cap=4, query={query}");
                    AssertEquivalent(serial[query], concurrent[query],
                        $"{fixture.Description}{Environment.NewLine}strategy={strategy}, query={query}");
                }
            }
        }

        private static void AssertAllStrategiesMatch(SearchGraphFixture fixture, PathQuery query)
        {
            SearchOutcome expected = Run(fixture, query, SearchStrategy.Reference03, 1, false);
            for (int index = 0; index < OptimizedStrategies.Length; index++)
            {
                SearchStrategy strategy = OptimizedStrategies[index];
                SearchOutcome actual = Run(fixture, query, strategy, 1, IsAlt(strategy));
                AssertExecuted(actual, strategy, fixture.Description);
                AssertEquivalent(expected, actual,
                    $"{fixture.Description}{Environment.NewLine}strategy={strategy}");
            }
        }

        private static void AssertExecuted(
            SearchOutcome outcome,
            SearchStrategy strategy,
            string context)
        {
            Assert.That(outcome.Diagnostics.RequestedStrategy, Is.EqualTo(strategy), context);
            Assert.That(outcome.Diagnostics.ExecutedStrategy, Is.EqualTo(strategy),
                $"{context}: optimized strategy unexpectedly fell back");
            Assert.That(outcome.Diagnostics.FallbackReason, Is.EqualTo(SearchFallbackReason.None),
                context);
        }

        private static SearchOutcome Run(
            SearchGraphFixture fixture,
            PathQuery query,
            SearchStrategy strategy,
            int concurrency,
            bool prepareAccelerator)
        {
            return RunBatch(fixture, new[] { query }, strategy, concurrency, prepareAccelerator)[0];
        }

        private static SearchOutcome[] RunBatch(
            SearchGraphFixture fixture,
            PathQuery[] queries,
            SearchStrategy strategy,
            int concurrency,
            bool prepareAccelerator = true)
        {
            using var world = new NavigationWorld(
                fixture.Bake,
                0,
                concurrency,
                new SearchExecutionOptions(strategy));
            if (prepareAccelerator && IsAlt(strategy))
            {
                Assert.That(world.PrepareSearchAccelerator(fixture.Policy), Is.True,
                    $"Could not prepare accelerator for {fixture.Description}");
            }

            var handles = new PathRequestHandle[queries.Length];
            world.SubmitBatch(queries, handles);
            Complete(world, handles);

            var outcomes = new SearchOutcome[handles.Length];
            for (int index = 0; index < handles.Length; index++)
            {
                PathRequestHandle handle = handles[index];
                PathRequestStatus status = world.GetStatus(handle);
                world.TryGetFailure(handle, out PathFailureReason failure);
                NavigationPath path = world.TryGetPath(handle, out NavigationPathView view)
                    ? view.ToManagedCopy()
                    : null;
                Assert.That(world.TryGetSearchDiagnostics(handle, out SearchDiagnostics diagnostics), Is.True,
                    $"Missing diagnostics for {strategy} on {fixture.Description}");
                outcomes[index] = new SearchOutcome(status, failure, path, diagnostics);
                Assert.That(world.Release(handle), Is.True);
            }

            return outcomes;
        }

        private static void Complete(NavigationWorld world, PathRequestHandle[] handles)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
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

            Assert.Fail("Search strategy parity requests did not complete within 10 seconds.");
        }

        private static void AssertEquivalent(SearchOutcome expected, SearchOutcome actual, string context)
        {
            Assert.That(actual.Status, Is.EqualTo(expected.Status), context);
            Assert.That(actual.Failure, Is.EqualTo(expected.Failure), context);
            if (expected.Path == null)
            {
                Assert.That(actual.Path, Is.Null, context);
                return;
            }

            Assert.That(actual.Path, Is.Not.Null, context);
            NavigationPath expectedPath = expected.Path;
            NavigationPath actualPath = actual.Path;
            Assert.That(actualPath.TotalCost, Is.EqualTo(expectedPath.TotalCost).Within(1e-9d), context);
            Assert.That(actualPath.TopologyRevision, Is.EqualTo(expectedPath.TopologyRevision), context);
            CollectionAssert.AreEqual(expectedPath.PolygonCorridor, actualPath.PolygonCorridor, context);
            CollectionAssert.AreEqual(expectedPath.Revisions, actualPath.Revisions, context);
            Assert.That(actualPath.Areas.Count, Is.EqualTo(expectedPath.Areas.Count), context);
            Assert.That(actualPath.PortalTransitions.Count,
                Is.EqualTo(expectedPath.PortalTransitions.Count), context);
            Assert.That(actualPath.CrossingSpans.Count,
                Is.EqualTo(expectedPath.CrossingSpans.Count), context);
            Assert.That(actualPath.SteeringTargets.Count,
                Is.EqualTo(expectedPath.SteeringTargets.Count), context);

            for (int index = 0; index < expectedPath.Areas.Count; index++)
            {
                NavigationAreaSegment expectedArea = expectedPath.Areas[index];
                NavigationAreaSegment actualArea = actualPath.Areas[index];
                Assert.That(actualArea.AreaId, Is.EqualTo(expectedArea.AreaId), context);
                Assert.That(actualArea.Start, Is.EqualTo(expectedArea.Start), context);
                Assert.That(actualArea.End, Is.EqualTo(expectedArea.End), context);
                Assert.That(actualArea.PolygonStart, Is.EqualTo(expectedArea.PolygonStart), context);
                Assert.That(actualArea.PolygonCount, Is.EqualTo(expectedArea.PolygonCount), context);
                Assert.That(actualArea.CrossingStart, Is.EqualTo(expectedArea.CrossingStart), context);
                Assert.That(actualArea.CrossingCount, Is.EqualTo(expectedArea.CrossingCount), context);
                Assert.That(actualArea.SteeringStart, Is.EqualTo(expectedArea.SteeringStart), context);
                Assert.That(actualArea.SteeringCount, Is.EqualTo(expectedArea.SteeringCount), context);
                Assert.That(actualArea.Revision, Is.EqualTo(expectedArea.Revision), context);
                Assert.That(actualArea.Cost, Is.EqualTo(expectedArea.Cost).Within(1e-9d), context);
            }

            for (int index = 0; index < expectedPath.CrossingSpans.Count; index++)
            {
                NavigationCrossingSpan expectedSpan = expectedPath.CrossingSpans[index];
                NavigationCrossingSpan actualSpan = actualPath.CrossingSpans[index];
                Assert.That(actualSpan.AreaId, Is.EqualTo(expectedSpan.AreaId), context);
                Assert.That(actualSpan.FromPolygon, Is.EqualTo(expectedSpan.FromPolygon), context);
                Assert.That(actualSpan.ToPolygon, Is.EqualTo(expectedSpan.ToPolygon), context);
                Assert.That(actualSpan.Start, Is.EqualTo(expectedSpan.Start), context);
                Assert.That(actualSpan.End, Is.EqualTo(expectedSpan.End), context);
            }

            for (int index = 0; index < expectedPath.SteeringTargets.Count; index++)
            {
                Assert.That(actualPath.SteeringTargets[index],
                    Is.EqualTo(expectedPath.SteeringTargets[index]), context);
            }
        }

        private static bool IsAlt(SearchStrategy strategy)
        {
            return strategy == SearchStrategy.AltAStar || strategy == SearchStrategy.BidirectionalAlt;
        }

        private readonly struct SearchOutcome
        {
            internal SearchOutcome(
                PathRequestStatus status,
                PathFailureReason failure,
                NavigationPath path,
                SearchDiagnostics diagnostics)
            {
                Status = status;
                Failure = failure;
                Path = path;
                Diagnostics = diagnostics;
            }

            internal PathRequestStatus Status { get; }
            internal PathFailureReason Failure { get; }
            internal NavigationPath Path { get; }
            internal SearchDiagnostics Diagnostics { get; }
        }
    }

    internal readonly struct SearchGraphEdge
    {
        internal SearchGraphEdge(int from, int to)
        {
            From = from;
            To = to;
        }

        internal int From { get; }
        internal int To { get; }
    }

    internal sealed class SearchGraphFixture : IDisposable
    {
        private readonly UnityEngine.Object[] _assets;
        private readonly Vector3[] _centers;

        private SearchGraphFixture(
            UnityEngine.Object[] assets,
            NavigationBakeAsset bake,
            CompiledTraversalPolicy policy,
            AreaId areaId,
            Vector3[] centers,
            string description)
        {
            _assets = assets;
            Bake = bake;
            Policy = policy;
            AreaId = areaId;
            _centers = centers;
            Description = description;
        }

        internal NavigationBakeAsset Bake { get; }
        internal CompiledTraversalPolicy Policy { get; }
        internal AreaId AreaId { get; }
        internal int NodeCount => _centers.Length;
        internal string Description { get; }

        internal PathQuery Query(int start, int goal)
        {
            return new PathQuery(
                new NavigationLocation(AreaId, _centers[start]),
                new NavigationLocation(AreaId, _centers[goal]),
                Policy,
                PathPriority.Normal,
                PathOutputFlags.Default);
        }

        internal static SearchGraphFixture EqualCostDiamond()
        {
            var centers = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 0f, 2f),
                new Vector3(2f, 0f, -2f),
                new Vector3(4f, 0f, 0f)
            };
            var edges = new[]
            {
                new SearchGraphEdge(0, 1),
                new SearchGraphEdge(0, 2),
                new SearchGraphEdge(1, 3),
                new SearchGraphEdge(2, 3)
            };
            return Create(1001, centers, edges, Array.Empty<int>(), 1d, 0d, "equal-cost-directed-diamond");
        }

        internal static SearchGraphFixture DirectedIntersectionTrap()
        {
            var centers = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 2f),
                new Vector3(2f, 0f, 2f),
                new Vector3(4f, 0f, 2f),
                new Vector3(5f, 0f, 0f),
                new Vector3(6f, 0f, 0f)
            };
            var edges = new[]
            {
                new SearchGraphEdge(0, 1),
                new SearchGraphEdge(1, 5),
                new SearchGraphEdge(5, 6),
                new SearchGraphEdge(0, 2),
                new SearchGraphEdge(2, 3),
                new SearchGraphEdge(3, 4),
                new SearchGraphEdge(4, 6),
                new SearchGraphEdge(3, 1),
                new SearchGraphEdge(5, 4)
            };
            return Create(2003, centers, edges, new[] { 5 }, 12d, 0d,
                "directed-first-intersection-trap");
        }

        internal static SearchGraphFixture ZeroCostCycleTrap()
        {
            var centers = new[]
            {
                new Vector3(3f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(0f, 0f, 0f)
            };
            var edges = new[]
            {
                new SearchGraphEdge(3, 2),
                new SearchGraphEdge(2, 1),
                new SearchGraphEdge(1, 2),
                new SearchGraphEdge(2, 0)
            };
            return Create(
                2011,
                centers,
                edges,
                new[] { 1, 2 },
                0d,
                0d,
                "directed-zero-cost-parent-cycle-trap");
        }

        internal static SearchGraphFixture Seeded(int seed, bool reachable, bool zeroCost)
        {
            const int count = 16;
            var random = new System.Random(seed);
            var centers = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                centers[index] = new Vector3((index % 4) * 2f, 0f, (index / 4) * 2f);
            }

            var present = new bool[count, count];
            var edges = new List<SearchGraphEdge>();
            int componentEnd = reachable ? count : count / 2;
            for (int index = 0; index < componentEnd - 1; index++)
            {
                AddEdge(edges, present, index, index + 1);
            }

            if (!reachable)
            {
                for (int index = count / 2; index < count - 1; index++)
                {
                    AddEdge(edges, present, index, index + 1);
                }
            }

            for (int from = 0; from < count; from++)
            {
                for (int to = 0; to < count; to++)
                {
                    bool sameComponent = reachable || (from < count / 2) == (to < count / 2);
                    if (from != to && sameComponent && random.NextDouble() < 0.16d)
                    {
                        AddEdge(edges, present, from, to);
                    }
                }
            }

            var costed = new List<int>();
            for (int index = 1; index < count - 1; index++)
            {
                if (random.Next(4) == 0)
                {
                    costed.Add(index);
                }
            }

            return Create(
                seed,
                centers,
                edges.ToArray(),
                costed.ToArray(),
                zeroCost ? 0d : 3d,
                zeroCost ? 0d : 0.25d,
                $"seed={seed}, reachable={reachable}, zeroCost={zeroCost}");
        }

        public void Dispose()
        {
            for (int index = _assets.Length - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[index]);
            }
        }

        private static SearchGraphFixture Create(
            int seed,
            Vector3[] centers,
            SearchGraphEdge[] edges,
            int[] costedNodes,
            double distanceMultiplier,
            double entryPenalty,
            string name)
        {
            SemanticRegistryAsset registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            SemanticId costedSemantic = registry.Add("Costed");
            NavigationWorldAsset source = ScriptableObject.CreateInstance<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationBakeAsset bake = ScriptableObject.CreateInstance<NavigationBakeAsset>();
            int wordCount = registry.RequiredWordCount;
            AreaId areaId = new AreaId(StableGuid(seed, 1, 0));
            var polygons = new CompiledPolygonRecord[centers.Length];
            var vertices = new CompiledVertexRecord[centers.Length * 4];
            var semanticWords = new ulong[(centers.Length + 1) * wordCount * 2];
            var costed = new bool[centers.Length];
            for (int index = 0; index < costedNodes.Length; index++)
            {
                costed[costedNodes[index]] = true;
            }

            Bounds bounds = new Bounds(centers[0], Vector3.zero);
            for (int node = 0; node < centers.Length; node++)
            {
                Vector3 center = centers[node];
                Vector3[] rectangle = Rectangle(center);
                int vertexStart = node * 4;
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    vertices[vertexStart + vertex] = new CompiledVertexRecord(
                        new VertexId(StableGuid(seed, 3, vertexStart + vertex)),
                        new EdgeId(StableGuid(seed, 4, vertexStart + vertex)),
                        rectangle[vertex]);
                    bounds.Encapsulate(rectangle[vertex]);
                }

                int semanticOffset = node * wordCount * 2;
                int capabilityOffset = semanticOffset + wordCount;
                if (costed[node])
                {
                    semanticWords[semanticOffset] = 1UL;
                }

                polygons[node] = new CompiledPolygonRecord(
                    new PolygonId(StableGuid(seed, 2, node)),
                    0,
                    vertexStart,
                    4,
                    0,
                    0,
                    semanticOffset,
                    capabilityOffset,
                    center,
                    Vector3.up,
                    true);
            }

            Array.Sort(edges, CompareEdges);
            var compiledEdges = new CompiledAdjacencyRecord[edges.Length];
            int cursor = 0;
            for (int node = 0; node < centers.Length; node++)
            {
                int start = cursor;
                while (cursor < edges.Length && edges[cursor].From == node)
                {
                    SearchGraphEdge edge = edges[cursor];
                    Vector3 midpoint = (centers[edge.From] + centers[edge.To]) * 0.5f;
                    compiledEdges[cursor] = new CompiledAdjacencyRecord(
                        edge.From,
                        edge.To,
                        vertices[edge.From * 4].OutgoingEdgeId,
                        vertices[edge.To * 4].OutgoingEdgeId,
                        midpoint + (Vector3.forward * 0.1f),
                        midpoint - (Vector3.forward * 0.1f),
                        AdjacencyOverrideState.ForcedConnected);
                    cursor++;
                }

                polygons[node] = polygons[node].WithAdjacencyRange(start, cursor - start);
            }

            int areaSemanticOffset = centers.Length * wordCount * 2;
            var areas = new[]
            {
                new CompiledAreaRecord(
                    areaId,
                    AreaFrame.Identity,
                    0,
                    centers.Length,
                    areaSemanticOffset,
                    areaSemanticOffset + wordCount,
                    bounds)
            };
            bake.SetData(
                source,
                NavigationBaker.ComputeSourceFingerprint(source),
                registry.SchemaFingerprint,
                wordCount,
                areas,
                polygons,
                vertices,
                compiledEdges,
                Array.Empty<CompiledPortalRecord>(),
                semanticWords);

            var policyBuilder = new TraversalPolicyBuilder(registry);
            if (costedNodes.Length > 0)
            {
                policyBuilder.SetCost(costedSemantic, distanceMultiplier, entryPenalty);
            }

            Assert.That(policyBuilder.TryCompile(
                bake,
                out CompiledTraversalPolicy policy,
                out string error), Is.True, error);
            string description = Describe(name, centers, edges, costedNodes,
                distanceMultiplier, entryPenalty);
            return new SearchGraphFixture(
                new UnityEngine.Object[] { registry, source, bake },
                bake,
                policy,
                areaId,
                centers,
                description);
        }

        private static void AddEdge(
            List<SearchGraphEdge> edges,
            bool[,] present,
            int from,
            int to)
        {
            if (!present[from, to])
            {
                present[from, to] = true;
                edges.Add(new SearchGraphEdge(from, to));
            }
        }

        private static int CompareEdges(SearchGraphEdge left, SearchGraphEdge right)
        {
            int from = left.From.CompareTo(right.From);
            return from != 0 ? from : left.To.CompareTo(right.To);
        }

        private static Vector3[] Rectangle(Vector3 center)
        {
            const float radius = 0.4f;
            return new[]
            {
                center + new Vector3(-radius, 0f, -radius),
                center + new Vector3(-radius, 0f, radius),
                center + new Vector3(radius, 0f, radius),
                center + new Vector3(radius, 0f, -radius)
            };
        }

        private static Guid StableGuid(int seed, int kind, int index)
        {
            var bytes = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(seed), 0, bytes, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(kind), 0, bytes, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(index), 0, bytes, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(~index), 0, bytes, 12, 4);
            return new Guid(bytes);
        }

        private static string Describe(
            string name,
            Vector3[] centers,
            SearchGraphEdge[] edges,
            int[] costedNodes,
            double multiplier,
            double penalty)
        {
            var result = new StringBuilder();
            result.Append(name).Append(", nodes=[");
            for (int index = 0; index < centers.Length; index++)
            {
                if (index > 0)
                {
                    result.Append(',');
                }

                result.Append(index).Append(":(")
                    .Append(centers[index].x).Append(',')
                    .Append(centers[index].z).Append(')');
            }

            result.Append("], edges=[");
            for (int index = 0; index < edges.Length; index++)
            {
                if (index > 0)
                {
                    result.Append(',');
                }

                result.Append(edges[index].From).Append("->").Append(edges[index].To);
            }

            result.Append("], costed=[").Append(string.Join(",", costedNodes))
                .Append("], multiplier=").Append(multiplier)
                .Append(", penalty=").Append(penalty);
            return result.ToString();
        }
    }
}
