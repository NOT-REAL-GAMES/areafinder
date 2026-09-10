using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using Unity.Jobs.LowLevel.Unsafe;
using Debug = UnityEngine.Debug;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class NavigationBenchmarkTests
    {
        private const int WarmupCount = 32;
        private const int IdleTickCount = 1024;
        private const int SampleCount = 7;
        private const int RequestsPerSample = 128;
        private const string SuccessMarker = "AREAFINDER_BENCHMARK_SUCCESS";

        private static readonly SearchStrategy[] Strategies =
        {
            SearchStrategy.Reference03,
            SearchStrategy.HeapDijkstra,
            SearchStrategy.AStar,
            SearchStrategy.BidirectionalDijkstra,
            SearchStrategy.BidirectionalAStar,
            SearchStrategy.AltAStar,
            SearchStrategy.BidirectionalAlt
        };

        private static readonly int[] LandmarkCounts = { 4, 8, 16 };

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
        public void RecordsRepeatableRouteBaselinesAndZeroAllocationIdleTicks()
        {
            BenchmarkFixture sameArea = CreateSameAreaFixture();
            BenchmarkFixture crossArea = CreateCrossAreaFixture();
            long idleBytes = MeasureIdleAllocation(sameArea);
            ConcurrencyComparison sameAreaResult = MeasureComparison(sameArea);
            ConcurrencyComparison crossAreaResult = MeasureComparison(crossArea);
            BenchmarkFixture[] algorithmFixtures = CreateAlgorithmFixtures(sameArea, crossArea);
            AlgorithmBenchmark[] algorithmResults = MeasureAlgorithms(algorithmFixtures);
            var report = new BenchmarkReport
            {
                marker = SuccessMarker,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                machine = Environment.MachineName,
                processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                memoryMegabytes = SystemInfo.systemMemorySize,
                operatingSystem = SystemInfo.operatingSystem,
                unity = Application.unityVersion,
                burst = PackageVersion("com.unity.burst"),
                collections = PackageVersion("com.unity.collections"),
                backend = $"Editor/{PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}",
                warmups = WarmupCount,
                samples = SampleCount,
                requestsPerSample = RequestsPerSample,
                idleTickCount = IdleTickCount,
                idleAllocatedBytes = idleBytes,
                workerCount = JobsUtility.JobWorkerCount,
                sameArea32x32 = sameAreaResult,
                threeArea16x16 = crossAreaResult,
                algorithmSearches = algorithmResults
            };

            string json = JsonUtility.ToJson(report, true);
            string reportPath = Environment.GetEnvironmentVariable("AREAFINDER_BENCHMARK_REPORT");
            if (!string.IsNullOrEmpty(reportPath))
            {
                string directory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, json);
            }

            Debug.Log($"{SuccessMarker}{Environment.NewLine}{json}");
            Assert.That(idleBytes, Is.Zero, "Warmed idle Tick(0) calls allocated managed memory.");
            if (SystemInfo.processorType.IndexOf("Ryzen 9 7900X", StringComparison.OrdinalIgnoreCase) >= 0 &&
                sameAreaResult.cap4.effectiveMaxConcurrentSearches >= 4)
            {
                Assert.That(sameAreaResult.cap4ToCap1ThroughputRatio, Is.GreaterThan(1d),
                    "The four-lane 32x32 reference-host run must still scale above cap one.");
            }
        }

        private static long MeasureIdleAllocation(BenchmarkFixture fixture)
        {
            using var world = new NavigationWorld(fixture.Bake);
            GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < WarmupCount; index++)
            {
                world.Tick(0);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < IdleTickCount; index++)
            {
                world.Tick(0);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static ConcurrencyComparison MeasureComparison(BenchmarkFixture fixture)
        {
            BenchmarkMeasurement cap1 = Measure(fixture, 1, SearchStrategy.AStar, 8);
            BenchmarkMeasurement cap4 = Measure(fixture, 4, SearchStrategy.AStar, 8);
            return new ConcurrencyComparison
            {
                cap1 = cap1,
                cap4 = cap4,
                cap4ToCap1ThroughputRatio = cap4.medianRoutesPerSecond / cap1.medianRoutesPerSecond
            };
        }

        private static AlgorithmBenchmark[] MeasureAlgorithms(BenchmarkFixture[] fixtures)
        {
            var results = new List<AlgorithmBenchmark>(fixtures.Length * Strategies.Length);
            for (int fixtureIndex = 0; fixtureIndex < fixtures.Length; fixtureIndex++)
            {
                BenchmarkFixture fixture = fixtures[fixtureIndex];
                for (int strategyIndex = 0; strategyIndex < Strategies.Length; strategyIndex++)
                {
                    SearchStrategy strategy = Strategies[strategyIndex];
                    int variants = IsAlt(strategy) ? LandmarkCounts.Length : 1;
                    for (int variant = 0; variant < variants; variant++)
                    {
                        int landmarkCount = IsAlt(strategy) ? LandmarkCounts[variant] : 0;
                        int executionLandmarkCount = Math.Max(1, landmarkCount);
                        results.Add(new AlgorithmBenchmark
                        {
                            fixture = fixture.Name,
                            strategy = strategy.ToString(),
                            landmarkCount = landmarkCount,
                            cap1 = Measure(fixture, 1, strategy, executionLandmarkCount),
                            cap4 = fixture.RecordCapFour
                                ? Measure(fixture, 4, strategy, executionLandmarkCount)
                                : null
                        });
                    }
                }
            }

            return results.ToArray();
        }

        private static BenchmarkMeasurement Measure(
            BenchmarkFixture fixture,
            int concurrency,
            SearchStrategy strategy,
            int landmarkCount)
        {
            using var world = new NavigationWorld(
                fixture.Bake,
                0,
                concurrency,
                new SearchExecutionOptions(strategy, landmarkCount));
            bool acceleratorPrepared = !IsAlt(strategy) || world.PrepareSearchAccelerator(fixture.Policy);
            var warmupHandles = new PathRequestHandle[WarmupCount];
            for (int index = 0; index < warmupHandles.Length; index++)
            {
                warmupHandles[index] = world.Submit(fixture.Queries[index % fixture.Queries.Length]);
            }

            int warmupPeak = 0;
            DrainAndRelease(world, fixture, warmupHandles, null, null, ref warmupPeak);

            var routesPerSecond = new double[SampleCount];
            var bytesPerRequest = new double[SampleCount];
            var completionLatency = new double[SampleCount * fixture.RequestsPerSample];
            var handles = new PathRequestHandle[fixture.RequestsPerSample];
            var stopwatch = new Stopwatch();
            var diagnostics = new DiagnosticAccumulator();
            var mutationMilliseconds = new double[SampleCount];
            int peakInFlight = 0;
            for (int sample = 0; sample < SampleCount; sample++)
            {
                if (fixture.MutationPolygon.IsValid)
                {
                    Stopwatch mutation = Stopwatch.StartNew();
                    if (!world.SetPolygonEnabled(fixture.MutationPolygon, false))
                    {
                        throw new InvalidOperationException("The benchmark mutation target was not found.");
                    }

                    world.Tick(0);
                    if (!world.SetPolygonEnabled(fixture.MutationPolygon, true))
                    {
                        throw new InvalidOperationException("The benchmark mutation target was not found.");
                    }

                    world.Tick(0);
                    mutation.Stop();
                    mutationMilliseconds[sample] = mutation.Elapsed.TotalMilliseconds;
                }

                int latencyOffset = sample * fixture.RequestsPerSample;
                long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                stopwatch.Restart();
                world.SubmitBatch(
                    new ArraySegment<PathQuery>(fixture.Queries, 0, fixture.RequestsPerSample),
                    handles);
                DrainAndRelease(
                    world,
                    fixture,
                    handles,
                    stopwatch,
                    completionLatency,
                    ref peakInFlight,
                    latencyOffset,
                    diagnostics);
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                routesPerSecond[sample] = fixture.RequestsPerSample / stopwatch.Elapsed.TotalSeconds;
                bytesPerRequest[sample] = allocated / (double)fixture.RequestsPerSample;
            }

            Array.Sort(routesPerSecond);
            Array.Sort(bytesPerRequest);
            Array.Sort(completionLatency);
            Array.Sort(mutationMilliseconds);
            var measurement = new BenchmarkMeasurement
            {
                medianRoutesPerSecond = routesPerSecond[SampleCount / 2],
                medianAllocatedBytesPerRequest = bytesPerRequest[SampleCount / 2],
                medianCompletionLatencyMilliseconds = Percentile(completionLatency, 0.5d),
                p95CompletionLatencyMilliseconds = Percentile(completionLatency, 0.95d),
                effectiveMaxConcurrentSearches = world.EffectiveMaxConcurrentSearches,
                peakInFlightJobs = peakInFlight,
                requestsPerSample = fixture.RequestsPerSample,
                acceleratorPrepared = acceleratorPrepared,
                mutationRebuildMilliseconds = mutationMilliseconds[SampleCount / 2]
            };
            diagnostics.ApplyTo(measurement);
            return measurement;
        }

        private static void DrainAndRelease(
            NavigationWorld world,
            BenchmarkFixture fixture,
            PathRequestHandle[] handles,
            Stopwatch stopwatch,
            double[] completionLatency,
            ref int peakInFlight,
            int latencyOffset = 0,
            DiagnosticAccumulator diagnostics = null)
        {
            var observed = new bool[handles.Length];
            int terminal = 0;
            var timeout = Stopwatch.StartNew();
            while (terminal < handles.Length && timeout.Elapsed < TimeSpan.FromSeconds(60d))
            {
                world.Tick(64);
                peakInFlight = Math.Max(peakInFlight, world.InFlightSearchCount);
                double elapsed = stopwatch?.Elapsed.TotalMilliseconds ?? 0d;
                for (int index = 0; index < handles.Length; index++)
                {
                    if (observed[index])
                    {
                        continue;
                    }

                    PathRequestStatus status = world.GetStatus(handles[index]);
                    if (!IsTerminal(status))
                    {
                        continue;
                    }

                    observed[index] = true;
                    if (completionLatency != null)
                    {
                        completionLatency[latencyOffset + index] = elapsed;
                    }

                    terminal++;
                }

                Thread.Yield();
            }

            if (terminal != handles.Length)
            {
                throw new TimeoutException("The benchmark request batch did not complete within 60 seconds.");
            }

            for (int index = 0; index < handles.Length; index++)
            {
                PathRequestHandle handle = handles[index];
                if (diagnostics != null &&
                    world.TryGetSearchDiagnostics(handle, out SearchDiagnostics searchDiagnostics))
                {
                    diagnostics.Add(searchDiagnostics);
                }

                if (world.GetStatus(handle) != PathRequestStatus.Completed ||
                    !world.TryGetPath(handle, out NavigationPathView path) ||
                    path.PolygonCount < fixture.MinimumPolygonCount ||
                    path.PortalTransitionCount != fixture.ExpectedPortalCount ||
                    !world.IsCurrent(path) ||
                    !world.Release(handle))
                {
                    throw new InvalidOperationException("The benchmark route batch did not complete correctly.");
                }
            }
        }

        private static bool IsTerminal(PathRequestStatus status)
        {
            return status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
                   status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale;
        }

        private static bool IsAlt(SearchStrategy strategy)
        {
            return strategy == SearchStrategy.AltAStar || strategy == SearchStrategy.BidirectionalAlt;
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
            return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Length - 1))];
        }

        private BenchmarkFixture[] CreateAlgorithmFixtures(
            BenchmarkFixture sameArea,
            BenchmarkFixture crossArea)
        {
            var fixtures = new List<BenchmarkFixture>
            {
                sameArea,
                crossArea,
                CreateGridFixture("small-8x8", 8, 8, RequestsPerSample, 6),
                CreateGridFixture("large-local-48x48", 48, 48, 32, 24),
                CreateGridFixture("long-thin-256x1", 256, 1, 64, 180),
                CreateGridFixture("branch-heavy-40x40", 40, 40, 32, 28),
                CreateEqualCostFixture(),
                CreateDirectedFixture(),
                CreatePolicyDivergentFixture(),
                CreateMutationFixture()
            };
            return fixtures.ToArray();
        }

        private BenchmarkFixture CreateGridFixture(
            string name,
            int width,
            int height,
            int requestsPerSample,
            int minimumPolygonCount)
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset area = CreateGridArea(width, height, out _);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            PathQuery[] queries = GridQueries(area, policy, width, height, requestsPerSample);
            return new BenchmarkFixture(
                name,
                bake,
                queries,
                minimumPolygonCount,
                0,
                requestsPerSample,
                false,
                default);
        }

        private BenchmarkFixture CreateEqualCostFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 2f));
            area.AddPolygon(Rectangle(1f, 1f, 2f, 2f));
            area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            area.AddPolygon(Rectangle(2f, 0f, 3f, 2f));
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            var queries = new PathQuery[RequestsPerSample];
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = new PathQuery(
                    new NavigationLocation(area.Id, new Vector3(0.25f, 0f, 1f)),
                    new NavigationLocation(area.Id, new Vector3(2.75f, 0f, 1f)),
                    policy);
            }

            return new BenchmarkFixture(
                "equal-cost-diamond",
                bake,
                queries,
                3,
                0,
                RequestsPerSample,
                false,
                default);
        }

        private BenchmarkFixture CreateDirectedFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset area = CreateGridArea(64, 1, out _);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            KeepIncreasingXAdjacencyOnly(bake);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            PathQuery[] queries = GridQueries(area, policy, 64, 1, 64);
            return new BenchmarkFixture(
                "directed-64x1",
                bake,
                queries,
                48,
                0,
                64,
                false,
                default);
        }

        private BenchmarkFixture CreatePolicyDivergentFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            SemanticId expensive = registry.Add("Expensive");
            const int size = 24;
            NavigationAreaAsset area = CreateGridArea(size, size, out NavigationPolygonRecord[,] polygons);
            var semantics = new SemanticMask(registry.SlotCapacity);
            semantics.Set(0);
            for (int x = 1; x < size - 1; x++)
            {
                polygons[x, size / 2].SetSemantics(semantics);
            }

            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            var builder = new TraversalPolicyBuilder(registry).SetCost(expensive, 8d, 0.5d);
            Assert.That(builder.TryCompile(bake, out CompiledTraversalPolicy policy, out string error),
                Is.True, error);
            PathQuery[] queries = GridQueries(area, policy, size, size, 64);
            return new BenchmarkFixture(
                "policy-divergent-24x24",
                bake,
                queries,
                18,
                0,
                64,
                false,
                default);
        }

        private BenchmarkFixture CreateMutationFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            const int size = 24;
            NavigationAreaAsset area = CreateGridArea(size, size, out NavigationPolygonRecord[,] polygons);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            PathQuery[] queries = GridQueries(area, policy, size, size, 64);
            return new BenchmarkFixture(
                "mutation-24x24",
                bake,
                queries,
                16,
                0,
                64,
                false,
                polygons[size / 2, size / 2].Id);
        }

        private BenchmarkFixture CreateSameAreaFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            const int size = 32;
            NavigationAreaAsset area = CreateGridArea(size, size, out _);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            var queries = new PathQuery[RequestsPerSample];
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = new PathQuery(
                    new NavigationLocation(
                        area.Id,
                        new Vector3(index % 8 + 0.25f, 0f, index * 5 % size + 0.25f)),
                    new NavigationLocation(
                        area.Id,
                        new Vector3(size - 1 - index % 8 + 0.75f, 0f, size - 1 - index * 11 % size + 0.75f)),
                    policy);
            }

            return new BenchmarkFixture(
                "same-area-32x32",
                bake,
                queries,
                18,
                0,
                RequestsPerSample,
                true,
                default);
        }

        private BenchmarkFixture CreateCrossAreaFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            const int size = 16;
            NavigationAreaAsset first = CreateGridArea(size, size, out NavigationPolygonRecord[,] firstPolygons);
            NavigationAreaAsset second = CreateGridArea(size, size, out NavigationPolygonRecord[,] secondPolygons);
            NavigationAreaAsset third = CreateGridArea(size, size, out NavigationPolygonRecord[,] thirdPolygons);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(first);
            source.AddArea(second);
            source.AddArea(third);
            source.AddPortal(
                Span(first, firstPolygons[size - 1, 7], 2),
                Span(second, secondPolygons[0, 7], 0),
                PortalDirection.SourceToDestination,
                0.5d,
                new PortalTransform(new Double3(-size, 0d, 0d), Quaternion.identity));
            source.AddPortal(
                Span(second, secondPolygons[size - 1, 8], 2),
                Span(third, thirdPolygons[0, 8], 0),
                PortalDirection.SourceToDestination,
                0.5d,
                new PortalTransform(new Double3(-size, 0d, 0d), Quaternion.identity));
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            var queries = new PathQuery[RequestsPerSample];
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = new PathQuery(
                    new NavigationLocation(
                        first.Id,
                        new Vector3(index % 4 + 0.25f, 0f, index * 3 % size + 0.25f)),
                    new NavigationLocation(
                        third.Id,
                        new Vector3(size - 1 - index % 4 + 0.75f, 0f, size - 1 - index * 5 % size + 0.75f)),
                    policy);
            }

            return new BenchmarkFixture(
                "three-area-16x16",
                bake,
                queries,
                20,
                2,
                RequestsPerSample,
                true,
                default);
        }

        private NavigationAreaAsset CreateGridArea(
            int width,
            int height,
            out NavigationPolygonRecord[,] polygons)
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            polygons = new NavigationPolygonRecord[width, height];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    polygons[x, z] = area.AddPolygon(Rectangle(x, z, x + 1f, z + 1f));
                }
            }

            return area;
        }

        private static PathQuery[] GridQueries(
            NavigationAreaAsset area,
            CompiledTraversalPolicy policy,
            int width,
            int height,
            int count)
        {
            var queries = new PathQuery[count];
            for (int index = 0; index < queries.Length; index++)
            {
                int startX = 0;
                int startZ = index * 3 % height;
                int goalX = width - 1;
                int goalZ = height - 1 - index * 5 % height;
                queries[index] = new PathQuery(
                    new NavigationLocation(area.Id, new Vector3(startX + 0.25f, 0f, startZ + 0.25f)),
                    new NavigationLocation(area.Id, new Vector3(goalX + 0.75f, 0f, goalZ + 0.75f)),
                    policy);
            }

            return queries;
        }

        private static void KeepIncreasingXAdjacencyOnly(NavigationBakeAsset bake)
        {
            var retained = new List<CompiledAdjacencyRecord>();
            for (int index = 0; index < bake.RawAdjacencies.Length; index++)
            {
                CompiledAdjacencyRecord adjacency = bake.RawAdjacencies[index];
                if (bake.RawPolygons[adjacency.FromPolygon].Centroid.x <
                    bake.RawPolygons[adjacency.ToPolygon].Centroid.x)
                {
                    retained.Add(adjacency);
                }
            }

            retained.Sort((left, right) =>
            {
                int from = left.FromPolygon.CompareTo(right.FromPolygon);
                return from != 0 ? from : left.ToPolygon.CompareTo(right.ToPolygon);
            });
            CompiledPolygonRecord[] polygons = (CompiledPolygonRecord[])bake.RawPolygons.Clone();
            int cursor = 0;
            for (int polygon = 0; polygon < polygons.Length; polygon++)
            {
                int start = cursor;
                while (cursor < retained.Count && retained[cursor].FromPolygon == polygon)
                {
                    cursor++;
                }

                polygons[polygon] = polygons[polygon].WithAdjacencyRange(start, cursor - start);
            }

            bake.SetData(
                bake.Source,
                bake.SourceFingerprint,
                bake.SemanticRegistryFingerprint,
                bake.SemanticWordCount,
                (CompiledAreaRecord[])bake.RawAreas.Clone(),
                polygons,
                (CompiledVertexRecord[])bake.RawVertices.Clone(),
                retained.ToArray(),
                (CompiledPortalRecord[])bake.RawPortals.Clone(),
                (ulong[])bake.RawSemanticWords.Clone());
        }

        private NavigationBakeAsset Bake(NavigationWorldAsset source)
        {
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult result = NavigationBaker.Bake(source, bake);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            return bake;
        }

        private static CompiledTraversalPolicy Compile(
            SemanticRegistryAsset registry,
            NavigationBakeAsset bake)
        {
            var builder = new TraversalPolicyBuilder(registry);
            Assert.That(builder.TryCompile(bake, out CompiledTraversalPolicy policy, out string error),
                Is.True, error);
            return policy;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static PortalEntrySpan Span(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            int edge)
        {
            NavigationVertexRecord first = polygon.Vertices[edge];
            NavigationVertexRecord second = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                first.OutgoingEdgeId,
                first.Position,
                second.Position);
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

        private static string PackageVersion(string packageName)
        {
            UnityEditor.PackageManager.PackageInfo[] packages =
                UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int index = 0; index < packages.Length; index++)
            {
                if (packages[index].name == packageName)
                {
                    return packages[index].version;
                }
            }

            return "unknown";
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var issues = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                issues.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, issues);
        }

        private readonly struct BenchmarkFixture
        {
            internal BenchmarkFixture(
                string name,
                NavigationBakeAsset bake,
                PathQuery[] queries,
                int minimumPolygonCount,
                int expectedPortalCount,
                int requestsPerSample,
                bool recordCapFour,
                PolygonId mutationPolygon)
            {
                Name = name;
                Bake = bake;
                Queries = queries;
                MinimumPolygonCount = minimumPolygonCount;
                ExpectedPortalCount = expectedPortalCount;
                RequestsPerSample = requestsPerSample;
                RecordCapFour = recordCapFour;
                MutationPolygon = mutationPolygon;
            }

            internal string Name { get; }
            internal NavigationBakeAsset Bake { get; }
            internal PathQuery[] Queries { get; }
            internal CompiledTraversalPolicy Policy => Queries[0].Policy;
            internal int MinimumPolygonCount { get; }
            internal int ExpectedPortalCount { get; }
            internal int RequestsPerSample { get; }
            internal bool RecordCapFour { get; }
            internal PolygonId MutationPolygon { get; }
        }

        [Serializable]
        private sealed class BenchmarkMeasurement
        {
            public double medianRoutesPerSecond;
            public double medianAllocatedBytesPerRequest;
            public double medianCompletionLatencyMilliseconds;
            public double p95CompletionLatencyMilliseconds;
            public int effectiveMaxConcurrentSearches;
            public int peakInFlightJobs;
            public int requestsPerSample;
            public string requestedStrategy;
            public string executedStrategy;
            public string fallbackReason;
            public double nodesDiscoveredPerRequest;
            public double nodesExpandedPerRequest;
            public double edgesExaminedPerRequest;
            public double heapPushesPerRequest;
            public double heapPopsPerRequest;
            public long maximumFrontierSize;
            public double heuristicEvaluationsPerRequest;
            public int landmarkCount;
            public long scratchBytes;
            public long acceleratorBytes;
            public double preprocessingMilliseconds;
            public double mutationRebuildMilliseconds;
            public double localSearchesPerRequest;
            public bool acceleratorPrepared;
        }

        [Serializable]
        private sealed class AlgorithmBenchmark
        {
            public string fixture;
            public string strategy;
            public int landmarkCount;
            public BenchmarkMeasurement cap1;
            public BenchmarkMeasurement cap4;
        }

        [Serializable]
        private sealed class ConcurrencyComparison
        {
            public BenchmarkMeasurement cap1;
            public BenchmarkMeasurement cap4;
            public double cap4ToCap1ThroughputRatio;
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string marker;
            public string timestampUtc;
            public string machine;
            public string processor;
            public int processorCount;
            public int memoryMegabytes;
            public string operatingSystem;
            public string unity;
            public string burst;
            public string collections;
            public string backend;
            public int warmups;
            public int samples;
            public int requestsPerSample;
            public int idleTickCount;
            public long idleAllocatedBytes;
            public int workerCount;
            public ConcurrencyComparison sameArea32x32;
            public ConcurrencyComparison threeArea16x16;
            public AlgorithmBenchmark[] algorithmSearches;
        }

        private sealed class DiagnosticAccumulator
        {
            private SearchStrategy _requested;
            private SearchStrategy _executed;
            private SearchFallbackReason _fallback;
            private long _requestCount;
            private long _nodesDiscovered;
            private long _nodesExpanded;
            private long _edgesExamined;
            private long _heapPushes;
            private long _heapPops;
            private long _maximumFrontierSize;
            private long _heuristicEvaluations;
            private int _landmarkCount;
            private long _scratchBytes;
            private long _acceleratorBytes;
            private double _preprocessingMilliseconds;
            private long _localSearchCount;

            internal void Add(SearchDiagnostics value)
            {
                _requested = value.RequestedStrategy;
                _executed = value.ExecutedStrategy;
                if (_fallback == SearchFallbackReason.None)
                {
                    _fallback = value.FallbackReason;
                }

                _requestCount++;
                _nodesDiscovered += value.NodesDiscovered;
                _nodesExpanded += value.NodesExpanded;
                _edgesExamined += value.EdgesExamined;
                _heapPushes += value.HeapPushes;
                _heapPops += value.HeapPops;
                _maximumFrontierSize = Math.Max(_maximumFrontierSize, value.MaximumFrontierSize);
                _heuristicEvaluations += value.HeuristicEvaluations;
                _landmarkCount = Math.Max(_landmarkCount, value.LandmarkCount);
                _scratchBytes = Math.Max(_scratchBytes, value.ScratchBytes);
                _acceleratorBytes = Math.Max(_acceleratorBytes, value.AcceleratorBytes);
                _preprocessingMilliseconds = Math.Max(
                    _preprocessingMilliseconds,
                    value.PreprocessingMilliseconds);
                _localSearchCount += value.LocalSearchCount;
            }

            internal void ApplyTo(BenchmarkMeasurement target)
            {
                double count = Math.Max(1L, _requestCount);
                target.requestedStrategy = _requested.ToString();
                target.executedStrategy = _executed.ToString();
                target.fallbackReason = _fallback.ToString();
                target.nodesDiscoveredPerRequest = _nodesDiscovered / count;
                target.nodesExpandedPerRequest = _nodesExpanded / count;
                target.edgesExaminedPerRequest = _edgesExamined / count;
                target.heapPushesPerRequest = _heapPushes / count;
                target.heapPopsPerRequest = _heapPops / count;
                target.maximumFrontierSize = _maximumFrontierSize;
                target.heuristicEvaluationsPerRequest = _heuristicEvaluations / count;
                target.landmarkCount = _landmarkCount;
                target.scratchBytes = _scratchBytes;
                target.acceleratorBytes = _acceleratorBytes;
                target.preprocessingMilliseconds = _preprocessingMilliseconds;
                target.localSearchesPerRequest = _localSearchCount / count;
            }
        }
    }
}
