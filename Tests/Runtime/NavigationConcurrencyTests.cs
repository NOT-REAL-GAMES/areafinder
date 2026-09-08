using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class NavigationConcurrencyTests
    {
        public enum RelatedMutation : byte
        {
            Polygon,
            Adjacency,
            AreaDirty
        }

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
        public void ConstructorValidatesConcurrencyAndCapsItToAvailableWorkers()
        {
            Fixture fixture = CreateFixture(2, 1, false);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NavigationWorld(fixture.Bake, 256, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NavigationWorld(fixture.Bake, 256, -1));

            int workerLimit = Math.Max(1, JobsUtility.JobWorkerCount);
            using var defaultWorld = new NavigationWorld(fixture.Bake);
            using var singleWorld = new NavigationWorld(fixture.Bake, 0, 1);
            using var oversizedWorld = new NavigationWorld(fixture.Bake, 0, int.MaxValue);

            Assert.That(
                typeof(NavigationWorld).GetProperty(nameof(NavigationWorld.EffectiveMaxConcurrentSearches)),
                Is.Not.Null,
                "The clamped cap must be inspectable without friend-assembly access.");
            Assert.That(defaultWorld.EffectiveMaxConcurrentSearches, Is.EqualTo(Math.Min(4, workerLimit)));
            Assert.That(singleWorld.EffectiveMaxConcurrentSearches, Is.EqualTo(1));
            Assert.That(oversizedWorld.EffectiveMaxConcurrentSearches, Is.EqualTo(workerLimit));
        }

        [Test]
        public void SaturatedLanesStayBoundedAndWeightedAdmissionReachesLowPriority()
        {
            Fixture fixture = CreateFixture(32, 1, false);
            using var world = new NavigationWorld(fixture.Bake, 0, 2);
            var high = new PathRequestHandle[12];
            for (int index = 0; index < high.Length; index++)
            {
                high[index] = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, index, PathPriority.High));
            }

            PathRequestHandle low = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, 31, PathPriority.Low));
            var timeout = Stopwatch.StartNew();
            while (world.GetStatus(low) == PathRequestStatus.Queued &&
                   timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(1);
                Assert.That(world.InFlightSearchCount,
                    Is.LessThanOrEqualTo(world.EffectiveMaxConcurrentSearches));
            }

            Assert.That(world.GetStatus(low), Is.Not.EqualTo(PathRequestStatus.Queued));
            Assert.That(Array.Exists(high, handle => world.GetStatus(handle) == PathRequestStatus.Queued),
                Is.True, "Low priority should be admitted before the earlier high-priority backlog drains.");

            var all = new PathRequestHandle[high.Length + 1];
            Array.Copy(high, all, high.Length);
            all[all.Length - 1] = low;
            Complete(world, all, out int peakInFlight);

            Assert.That(peakInFlight, Is.EqualTo(world.EffectiveMaxConcurrentSearches));
            for (int index = 0; index < all.Length; index++)
            {
                Assert.That(world.GetStatus(all[index]), Is.EqualTo(PathRequestStatus.Completed));
            }
        }

        [Test]
        public void CancelledInflightWorkCannotOverwriteAReusedHandleGeneration()
        {
            Fixture fixture = CreateFixture(32, 1, false);
            using var world = new NavigationWorld(fixture.Bake, 0, 1);
            PathRequestHandle abandoned = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, 0));
            AdvanceUntilInFlight(world, abandoned);

            Assert.That(world.InFlightSearchCount, Is.EqualTo(1));
            Assert.That(world.Cancel(abandoned), Is.True);
            Assert.That(world.InFlightSearchCount, Is.EqualTo(1),
                "Logical cancellation must not release the physical job's lane.");
            world.Tick(0);
            Assert.That(world.GetStatus(abandoned), Is.EqualTo(PathRequestStatus.Cancelled));
            Assert.That(world.InFlightSearchCount, Is.EqualTo(1),
                "Tick(0) publishes terminal state without harvesting jobs.");
            Assert.That(world.Release(abandoned), Is.True);
            Assert.That(world.InFlightSearchCount, Is.EqualTo(1),
                "Releasing the logical slot must not release physical work.");

            PathRequestHandle replacement = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, 1));
            Assert.That(replacement.Slot, Is.EqualTo(abandoned.Slot));
            Assert.That(replacement.Generation, Is.Not.EqualTo(abandoned.Generation));

            Complete(world, new[] { replacement }, out _);

            Assert.That(world.GetStatus(abandoned), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.GetStatus(replacement), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.TryGetPath(replacement, out NavigationPathView path), Is.True);
            Assert.That(path.PolygonCount, Is.GreaterThan(1));
        }

        [Test]
        public void TickPublishesCancellationBeforeItHarvestsCompletedPhysicalWork()
        {
            Fixture fixture = CreateFixture(32, 1, false);
            using var world = new NavigationWorld(fixture.Bake, 0, 1);
            int callbacks = 0;
            int physicalJobsObservedByCallback = -1;
            PathRequestHandle handle = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, 0),
                _ =>
                {
                    callbacks++;
                    physicalJobsObservedByCallback = world.InFlightSearchCount;
                });
            AdvanceUntilInFlight(world, handle);

            Assert.That(world.Cancel(handle), Is.True);
            world.Tick(64);

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(physicalJobsObservedByCallback, Is.EqualTo(1),
                "Publication is ordered before completed-job harvesting within Tick().");
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Cancelled));
        }

        [Test]
        public void OneMutationTickCreatesOneRevisionStepPerAffectedArea()
        {
            Fixture fixture = CreateFixture(16, 3, true);
            using var world = new NavigationWorld(fixture.Bake, 0, 1);
            ulong areaARevision = world.GetAreaRevision(fixture.AreaA.Id);
            ulong areaBRevision = world.GetAreaRevision(fixture.AreaB.Id);
            ulong topologyRevision = world.TopologyRevision;

            Assert.That(world.MarkAreaDirty(fixture.AreaA.Id), Is.True);
            Assert.That(world.SetPolygonEnabled(fixture.PolygonsA[8, 8].Id, false), Is.True);
            Assert.That(world.SetAdjacencyEnabled(
                fixture.PolygonsA[7, 8].Id,
                fixture.PolygonsA[8, 8].Id,
                false), Is.True);
            Assert.That(world.SetPortalEnabled(fixture.PortalAB.Id, false), Is.True);
            world.Tick(0);

            Assert.That(world.GetAreaRevision(fixture.AreaA.Id), Is.EqualTo(areaARevision + 1UL));
            Assert.That(world.GetAreaRevision(fixture.AreaB.Id), Is.EqualTo(areaBRevision + 1UL));
            Assert.That(world.TopologyRevision, Is.EqualTo(topologyRevision + 1UL));
        }

        [TestCase(RelatedMutation.Polygon)]
        [TestCase(RelatedMutation.Adjacency)]
        [TestCase(RelatedMutation.AreaDirty)]
        public void RelatedCopyOnWriteMutationMakesInflightResultStale(RelatedMutation mutation)
        {
            Fixture fixture = CreateFixture(32, 1, false);
            using var world = new NavigationWorld(fixture.Bake, 0, 1);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, 0));
            AdvanceUntilInFlight(world, handle);

            bool queued;
            switch (mutation)
            {
                case RelatedMutation.Polygon:
                    queued = world.SetPolygonEnabled(fixture.PolygonsA[16, 16].Id, false);
                    break;
                case RelatedMutation.Adjacency:
                    queued = world.SetAdjacencyEnabled(
                        fixture.PolygonsA[15, 16].Id,
                        fixture.PolygonsA[16, 16].Id,
                        false);
                    break;
                default:
                    queued = world.MarkAreaDirty(fixture.AreaA.Id);
                    break;
            }

            Assert.That(queued, Is.True);
            world.Tick(0);
            Complete(world, new[] { handle }, out _);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(world.TryGetPath(handle, out _), Is.False);
        }

        [Test]
        public void UnrelatedAreaMutationPreservesInflightResultCurrentness()
        {
            Fixture fixture = CreateFixture(32, 2, false);
            using var world = new NavigationWorld(fixture.Bake, 0, 1);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, 0));
            AdvanceUntilInFlight(world, handle);

            Assert.That(world.MarkAreaDirty(fixture.AreaB.Id), Is.True);
            world.Tick(0);
            Complete(world, new[] { handle }, out _);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.TryGetPath(handle, out NavigationPathView path), Is.True);
            Assert.That(world.IsCurrent(path), Is.True);
            Assert.That(path.RevisionCount, Is.EqualTo(1));
            Assert.That(path.GetRevision(0).AreaId, Is.EqualTo(fixture.AreaA.Id));
        }

        [Test]
        public void PortalSnapshotMutationStalesOldWorkAndChangesOnlyNewRequests()
        {
            Fixture fixture = CreateFixture(16, 3, true);
            using var world = new NavigationWorld(fixture.Bake, 0, 2);
            PathQuery query = Query(fixture, fixture.AreaA, fixture.AreaC, 0);
            PathRequestHandle old = world.Submit(query);
            AdvanceUntilInFlight(world, old);

            Assert.That(world.SetPortalEnabled(fixture.PortalAB.Id, false), Is.True);
            world.Tick(0);
            Complete(world, new[] { old }, out _);
            Assert.That(world.GetStatus(old), Is.EqualTo(PathRequestStatus.Stale));

            PathRequestHandle blocked = world.Submit(query);
            Complete(world, new[] { blocked }, out _);
            Assert.That(world.GetStatus(blocked), Is.EqualTo(PathRequestStatus.Failed));
            Assert.That(world.TryGetFailure(blocked, out PathFailureReason failure), Is.True);
            Assert.That(failure, Is.EqualTo(PathFailureReason.NoGlobalRoute));

            Assert.That(world.SetPortalEnabled(fixture.PortalAB.Id, true), Is.True);
            world.Tick(0);
            PathRequestHandle restored = world.Submit(query);
            Complete(world, new[] { restored }, out _);
            Assert.That(world.GetStatus(restored), Is.EqualTo(PathRequestStatus.Completed));
        }

        [Test]
        public void DisposeFencesInflightJobsAndReclaimsEveryLane()
        {
            Fixture fixture = CreateFixture(32, 1, false);
            var world = new NavigationWorld(fixture.Bake, 0, 4);
            try
            {
                for (int index = 0; index < 8; index++)
                {
                    world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, index));
                }

                AdvanceUntilAnyInFlight(world);
                Assert.That(world.InFlightSearchCount, Is.GreaterThan(0));
                Assert.DoesNotThrow(world.Dispose);
                Assert.That(world.IsDisposed, Is.True);
                Assert.That(world.InFlightSearchCount, Is.Zero);
                Assert.DoesNotThrow(world.Dispose);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void SaturatedMutationAndCancellationChurnReclaimsSnapshotsAndScratchLanes()
        {
            Fixture fixture = CreateFixture(32, 1, false);
            var world = new NavigationWorld(fixture.Bake, 0, 4);
            int snapshotBaseline = world.ActiveSnapshotCount;
            Assert.That(snapshotBaseline, Is.EqualTo(1));
            try
            {
                var handles = new PathRequestHandle[24];
                for (int index = 0; index < handles.Length; index++)
                {
                    handles[index] = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA, index));
                }

                var timeout = Stopwatch.StartNew();
                while (world.InFlightSearchCount < world.EffectiveMaxConcurrentSearches &&
                       timeout.Elapsed < TimeSpan.FromSeconds(10d))
                {
                    world.Tick(64);
                    Assert.That(world.InUseScratchLaneCount, Is.EqualTo(world.InFlightSearchCount));
                }

                Assert.That(world.InFlightSearchCount, Is.EqualTo(world.EffectiveMaxConcurrentSearches));
                Assert.That(world.MarkAreaDirty(fixture.AreaA.Id), Is.True);
                world.Tick(0);
                Assert.That(world.ActiveSnapshotCount, Is.GreaterThanOrEqualTo(snapshotBaseline + 1),
                    "The current snapshot and the snapshot retained by old work must coexist.");

                for (int index = 0; index < 8; index++)
                {
                    Assert.That(world.Cancel(handles[index]), Is.True);
                }

                int cancelledPhysical = world.InFlightSearchCount;
                world.Tick(0);
                Assert.That(world.InFlightSearchCount, Is.EqualTo(cancelledPhysical),
                    "Publishing cancellation must not reclaim physical jobs.");
                for (int index = 0; index < 8; index++)
                {
                    Assert.That(world.Release(handles[index]), Is.True);
                }

                timeout.Restart();
                bool drained = false;
                while (timeout.Elapsed < TimeSpan.FromSeconds(30d))
                {
                    world.Tick(64);
                    Assert.That(world.InUseScratchLaneCount, Is.EqualTo(world.InFlightSearchCount));
                    Assert.That(world.InFlightSearchCount,
                        Is.LessThanOrEqualTo(world.EffectiveMaxConcurrentSearches));

                    drained = world.InFlightSearchCount == 0;
                    for (int index = 8; index < handles.Length; index++)
                    {
                        drained &= IsTerminal(world.GetStatus(handles[index]));
                    }

                    if (drained)
                    {
                        break;
                    }

                    Thread.Yield();
                }

                Assert.That(drained, Is.True, "The churn batch did not fully drain.");
                for (int index = 8; index < handles.Length; index++)
                {
                    Assert.That(world.Release(handles[index]), Is.True);
                }

                Assert.That(world.InUseScratchLaneCount, Is.Zero);
                Assert.That(world.CurrentSnapshotReferenceCount, Is.EqualTo(1));
                Assert.That(world.ActiveSnapshotCount, Is.EqualTo(snapshotBaseline));
                Assert.That(world.AllocatedRequestSlotCount, Is.EqualTo(handles.Length));
            }
            finally
            {
                world.Dispose();
            }

            Assert.That(world.ActiveSnapshotCount, Is.Zero);
        }

        [Test]
        public void ConcurrentCrossAreaResultsMatchSingleLaneResults()
        {
            Fixture fixture = CreateFixture(16, 3, true);
            var queries = new PathQuery[12];
            for (int index = 0; index < queries.Length; index++)
            {
                queries[index] = Query(fixture, fixture.AreaA, fixture.AreaC, index);
            }

            NavigationPath[] expected = Solve(fixture, queries, 1);
            NavigationPath[] actual = Solve(fixture, queries, 4);

            for (int index = 0; index < expected.Length; index++)
            {
                AssertEquivalent(expected[index], actual[index]);
            }
        }

        private NavigationPath[] Solve(Fixture fixture, PathQuery[] queries, int concurrency)
        {
            using var world = new NavigationWorld(fixture.Bake, 0, concurrency);
            PathRequestHandle[] handles = world.SubmitBatch(queries);
            Complete(world, handles, out int peakInFlight);
            Assert.That(peakInFlight, Is.LessThanOrEqualTo(world.EffectiveMaxConcurrentSearches));
            var paths = new NavigationPath[handles.Length];
            for (int index = 0; index < handles.Length; index++)
            {
                Assert.That(world.TryGetPath(handles[index], out NavigationPathView path), Is.True);
                paths[index] = path.ToManagedCopy();
                Assert.That(world.Release(handles[index]), Is.True);
            }

            return paths;
        }

        private static void AssertEquivalent(NavigationPath expected, NavigationPath actual)
        {
            Assert.That(actual.TotalCost, Is.EqualTo(expected.TotalCost).Within(1e-9d));
            Assert.That(actual.Areas.Count, Is.EqualTo(expected.Areas.Count));
            Assert.That(actual.PortalTransitions.Count, Is.EqualTo(expected.PortalTransitions.Count));
            Assert.That(actual.PolygonCorridor.Count, Is.EqualTo(expected.PolygonCorridor.Count));
            Assert.That(actual.SteeringTargets.Count, Is.EqualTo(expected.SteeringTargets.Count));
            for (int index = 0; index < expected.Areas.Count; index++)
            {
                Assert.That(actual.Areas[index].AreaId, Is.EqualTo(expected.Areas[index].AreaId));
            }

            for (int index = 0; index < expected.PortalTransitions.Count; index++)
            {
                Assert.That(actual.PortalTransitions[index].PortalId,
                    Is.EqualTo(expected.PortalTransitions[index].PortalId));
            }

            for (int index = 0; index < expected.PolygonCorridor.Count; index++)
            {
                Assert.That(actual.PolygonCorridor[index], Is.EqualTo(expected.PolygonCorridor[index]));
            }

            for (int index = 0; index < expected.SteeringTargets.Count; index++)
            {
                Assert.That(actual.SteeringTargets[index], Is.EqualTo(expected.SteeringTargets[index]));
            }
        }

        private Fixture CreateFixture(int size, int areaCount, bool connectAreas)
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset areaA = CreateGridArea(source, size, out NavigationPolygonRecord[,] polygonsA);
            NavigationAreaAsset areaB = null;
            NavigationAreaAsset areaC = null;
            NavigationPolygonRecord[,] polygonsB = null;
            NavigationPolygonRecord[,] polygonsC = null;
            if (areaCount > 1)
            {
                areaB = CreateGridArea(source, size, out polygonsB);
            }

            if (areaCount > 2)
            {
                areaC = CreateGridArea(source, size, out polygonsC);
            }

            NavigationPortalRecord portalAB = null;
            NavigationPortalRecord portalBC = null;
            if (connectAreas)
            {
                portalAB = source.AddPortal(
                    Span(areaA, polygonsA[size - 1, size / 2 - 1], 2),
                    Span(areaB, polygonsB[0, size / 2 - 1], 0),
                    PortalDirection.SourceToDestination,
                    0.5d,
                    new PortalTransform(new Double3(-size, 0d, 0d), Quaternion.identity));
                portalBC = source.AddPortal(
                    Span(areaB, polygonsB[size - 1, size / 2], 2),
                    Span(areaC, polygonsC[0, size / 2], 0),
                    PortalDirection.SourceToDestination,
                    0.5d,
                    new PortalTransform(new Double3(-size, 0d, 0d), Quaternion.identity));
            }

            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult result = NavigationBaker.Bake(source, bake);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            Assert.That(
                new TraversalPolicyBuilder(registry).TryCompile(
                    bake,
                    out CompiledTraversalPolicy policy,
                    out string error),
                Is.True,
                error);
            return new Fixture
            {
                Size = size,
                Bake = bake,
                Policy = policy,
                AreaA = areaA,
                AreaB = areaB,
                AreaC = areaC,
                PolygonsA = polygonsA,
                PortalAB = portalAB,
                PortalBC = portalBC
            };
        }

        private NavigationAreaAsset CreateGridArea(
            NavigationWorldAsset source,
            int size,
            out NavigationPolygonRecord[,] polygons)
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            polygons = new NavigationPolygonRecord[size, size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    polygons[x, z] = area.AddPolygon(Rectangle(x, z, x + 1f, z + 1f));
                }
            }

            source.AddArea(area);
            return area;
        }

        private static PathQuery Query(
            Fixture fixture,
            NavigationAreaAsset startArea,
            NavigationAreaAsset goalArea,
            int seed,
            PathPriority priority = PathPriority.Normal)
        {
            int startZ = seed * 5 % fixture.Size;
            int goalZ = fixture.Size - 1 - seed * 7 % fixture.Size;
            return new PathQuery(
                new NavigationLocation(startArea.Id, new Vector3(0.25f, 0f, startZ + 0.25f)),
                new NavigationLocation(
                    goalArea.Id,
                    new Vector3(fixture.Size - 0.25f, 0f, goalZ + 0.75f)),
                fixture.Policy,
                priority);
        }

        private static void AdvanceUntilInFlight(NavigationWorld world, PathRequestHandle handle)
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(64);
                if (world.GetStatus(handle) == PathRequestStatus.RunningLocal &&
                    world.InFlightSearchCount > 0)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("The request did not schedule a local job within 10 seconds.");
        }

        private static void AdvanceUntilAnyInFlight(NavigationWorld world)
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(64);
                if (world.InFlightSearchCount > 0)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("No local job became observable within 10 seconds.");
        }

        private static void Complete(
            NavigationWorld world,
            PathRequestHandle[] handles,
            out int peakInFlight)
        {
            peakInFlight = world.InFlightSearchCount;
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(30d))
            {
                world.Tick(64);
                peakInFlight = Math.Max(peakInFlight, world.InFlightSearchCount);
                bool allTerminal = true;
                for (int index = 0; index < handles.Length; index++)
                {
                    allTerminal &= IsTerminal(world.GetStatus(handles[index]));
                }

                if (allTerminal)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("Requests did not become terminal within 30 seconds.");
        }

        private static bool IsTerminal(PathRequestStatus status)
        {
            return status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
                   status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale;
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

        private static string FormatIssues(NavigationBakeResult result)
        {
            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                messages.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, messages);
        }

        private sealed class Fixture
        {
            internal int Size;
            internal NavigationBakeAsset Bake;
            internal CompiledTraversalPolicy Policy;
            internal NavigationAreaAsset AreaA;
            internal NavigationAreaAsset AreaB;
            internal NavigationAreaAsset AreaC;
            internal NavigationPolygonRecord[,] PolygonsA;
            internal NavigationPortalRecord PortalAB;
            internal NavigationPortalRecord PortalBC;
        }
    }
}
