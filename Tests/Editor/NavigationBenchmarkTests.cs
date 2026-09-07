using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
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
            BenchmarkMeasurement sameAreaResult = Measure(sameArea);
            BenchmarkMeasurement crossAreaResult = Measure(crossArea);
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
                sameArea16x16 = sameAreaResult,
                threeArea8x8 = crossAreaResult
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

        private static BenchmarkMeasurement Measure(BenchmarkFixture fixture)
        {
            using var world = new NavigationWorld(fixture.Bake);
            for (int index = 0; index < WarmupCount; index++)
            {
                RunCycle(world, fixture);
            }

            var routesPerSecond = new double[SampleCount];
            var bytesPerRequest = new double[SampleCount];
            var stopwatch = new Stopwatch();
            for (int sample = 0; sample < SampleCount; sample++)
            {
                long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                stopwatch.Restart();
                for (int request = 0; request < RequestsPerSample; request++)
                {
                    RunCycle(world, fixture);
                }

                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                routesPerSecond[sample] = RequestsPerSample / stopwatch.Elapsed.TotalSeconds;
                bytesPerRequest[sample] = allocated / (double)RequestsPerSample;
            }

            Array.Sort(routesPerSecond);
            Array.Sort(bytesPerRequest);
            return new BenchmarkMeasurement
            {
                medianRoutesPerSecond = routesPerSecond[SampleCount / 2],
                medianAllocatedBytesPerRequest = bytesPerRequest[SampleCount / 2]
            };
        }

        private static void RunCycle(NavigationWorld world, BenchmarkFixture fixture)
        {
            PathRequestHandle handle = world.Submit(fixture.Query);
            world.Tick(64);
            world.Tick(0);
            if (world.GetStatus(handle) != PathRequestStatus.Completed ||
                !world.TryGetPath(handle, out NavigationPathView path) ||
                path.PolygonCount < fixture.MinimumPolygonCount ||
                path.PortalTransitionCount != fixture.ExpectedPortalCount ||
                !world.IsCurrent(path) ||
                !world.Release(handle))
            {
                throw new InvalidOperationException("The benchmark route cycle did not complete correctly.");
            }
        }

        private BenchmarkFixture CreateSameAreaFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset area = CreateGridArea(16, 16, out NavigationPolygonRecord[,] polygons);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            return new BenchmarkFixture(
                bake,
                new PathQuery(
                    new NavigationLocation(area.Id, new Vector3(0.25f, 0f, 0.25f)),
                    new NavigationLocation(area.Id, new Vector3(15.75f, 0f, 15.75f)),
                    policy),
                31,
                0);
        }

        private BenchmarkFixture CreateCrossAreaFixture()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset first = CreateGridArea(8, 8, out NavigationPolygonRecord[,] firstPolygons);
            NavigationAreaAsset second = CreateGridArea(8, 8, out NavigationPolygonRecord[,] secondPolygons);
            NavigationAreaAsset third = CreateGridArea(8, 8, out NavigationPolygonRecord[,] thirdPolygons);
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            source.AddArea(first);
            source.AddArea(second);
            source.AddArea(third);
            source.AddPortal(
                Span(first, firstPolygons[7, 3], 2),
                Span(second, secondPolygons[0, 3], 0),
                PortalDirection.SourceToDestination,
                0.5d,
                new PortalTransform(new Double3(-8d, 0d, 0d), Quaternion.identity));
            source.AddPortal(
                Span(second, secondPolygons[7, 4], 2),
                Span(third, thirdPolygons[0, 4], 0),
                PortalDirection.SourceToDestination,
                0.5d,
                new PortalTransform(new Double3(-8d, 0d, 0d), Quaternion.identity));
            NavigationBakeAsset bake = Bake(source);
            CompiledTraversalPolicy policy = Compile(registry, bake);
            return new BenchmarkFixture(
                bake,
                new PathQuery(
                    new NavigationLocation(first.Id, new Vector3(0.25f, 0f, 0.25f)),
                    new NavigationLocation(third.Id, new Vector3(7.75f, 0f, 7.75f)),
                    policy),
                24,
                2);
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
                PathQuery query,
                int minimumPolygonCount,
                int expectedPortalCount)
            {
                Bake = bake;
                Query = query;
                MinimumPolygonCount = minimumPolygonCount;
                ExpectedPortalCount = expectedPortalCount;
            }

            internal NavigationBakeAsset Bake { get; }
            internal PathQuery Query { get; }
            internal int MinimumPolygonCount { get; }
            internal int ExpectedPortalCount { get; }
        }

        [Serializable]
        private sealed class BenchmarkMeasurement
        {
            public double medianRoutesPerSecond;
            public double medianAllocatedBytesPerRequest;
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
            public BenchmarkMeasurement sameArea16x16;
            public BenchmarkMeasurement threeArea8x8;
        }
    }
}
