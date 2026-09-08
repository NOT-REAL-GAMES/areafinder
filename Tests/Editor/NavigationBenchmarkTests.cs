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
                threeArea16x16 = crossAreaResult
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
                Assert.That(sameAreaResult.cap4ToCap1ThroughputRatio, Is.GreaterThanOrEqualTo(1.5d),
                    "The four-lane 32x32 reference-host baseline must be at least 1.5x cap one.");
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
            BenchmarkMeasurement cap1 = Measure(fixture, 1);
            BenchmarkMeasurement cap4 = Measure(fixture, 4);
            return new ConcurrencyComparison
            {
                cap1 = cap1,
                cap4 = cap4,
                cap4ToCap1ThroughputRatio = cap4.medianRoutesPerSecond / cap1.medianRoutesPerSecond
            };
        }

        private static BenchmarkMeasurement Measure(BenchmarkFixture fixture, int concurrency)
        {
            using var world = new NavigationWorld(fixture.Bake, 0, concurrency);
            var warmupHandles = new PathRequestHandle[WarmupCount];
            for (int index = 0; index < warmupHandles.Length; index++)
            {
                warmupHandles[index] = world.Submit(fixture.Queries[index]);
            }

            int warmupPeak = 0;
            DrainAndRelease(world, fixture, warmupHandles, null, null, ref warmupPeak);

            var routesPerSecond = new double[SampleCount];
            var bytesPerRequest = new double[SampleCount];
            var completionLatency = new double[SampleCount * RequestsPerSample];
            var handles = new PathRequestHandle[RequestsPerSample];
            var stopwatch = new Stopwatch();
            int peakInFlight = 0;
            for (int sample = 0; sample < SampleCount; sample++)
            {
                int latencyOffset = sample * RequestsPerSample;
                long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                stopwatch.Restart();
                world.SubmitBatch(fixture.Queries, handles);
                DrainAndRelease(
                    world,
                    fixture,
                    handles,
                    stopwatch,
                    completionLatency,
                    ref peakInFlight,
                    latencyOffset);
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                routesPerSecond[sample] = RequestsPerSample / stopwatch.Elapsed.TotalSeconds;
                bytesPerRequest[sample] = allocated / (double)RequestsPerSample;
            }

            Array.Sort(routesPerSecond);
            Array.Sort(bytesPerRequest);
            Array.Sort(completionLatency);
            return new BenchmarkMeasurement
            {
                medianRoutesPerSecond = routesPerSecond[SampleCount / 2],
                medianAllocatedBytesPerRequest = bytesPerRequest[SampleCount / 2],
                medianCompletionLatencyMilliseconds = Percentile(completionLatency, 0.5d),
                p95CompletionLatencyMilliseconds = Percentile(completionLatency, 0.95d),
                effectiveMaxConcurrentSearches = world.EffectiveMaxConcurrentSearches,
                peakInFlightJobs = peakInFlight
            };
        }

        private static void DrainAndRelease(
            NavigationWorld world,
            BenchmarkFixture fixture,
            PathRequestHandle[] handles,
            Stopwatch stopwatch,
            double[] completionLatency,
            ref int peakInFlight,
            int latencyOffset = 0)
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

        private static double Percentile(double[] sortedValues, double percentile)
        {
            int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
            return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Length - 1))];
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

            return new BenchmarkFixture(bake, queries, 18, 0);
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

            return new BenchmarkFixture(bake, queries, 20, 2);
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
                NavigationBakeAsset bake,
                PathQuery[] queries,
                int minimumPolygonCount,
                int expectedPortalCount)
            {
                Bake = bake;
                Queries = queries;
                MinimumPolygonCount = minimumPolygonCount;
                ExpectedPortalCount = expectedPortalCount;
            }

            internal NavigationBakeAsset Bake { get; }
            internal PathQuery[] Queries { get; }
            internal int MinimumPolygonCount { get; }
            internal int ExpectedPortalCount { get; }
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
        }
    }
}
