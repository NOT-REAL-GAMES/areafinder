using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class NavigationWorldTests
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
        public void RequestMovesThroughExplicitLifecycleAndReturnsStructuredLocalGuidance()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Queued));
            AdvanceUntilStatus(world, handle, PathRequestStatus.RunningGlobal);
            AdvanceUntilStatus(world, handle, PathRequestStatus.RunningLocal);
            AdvanceUntilReadyForPublication(world, handle);
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.RunningLocal),
                "terminal publication is deferred to a later tick");
            world.Tick(0);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.TryGetPath(handle, out NavigationPathView path), Is.True);
            Assert.That(path.AreaCount, Is.EqualTo(1));
            Assert.That(path.PortalTransitionCount, Is.Zero);
            Assert.That(path.PolygonCount, Is.EqualTo(1));
            Assert.That(path.SteeringTargetCount, Is.EqualTo(1));
            Assert.That(path.GetSteeringTarget(0), Is.EqualTo(new Vector3(0.75f, 0f, 0.75f)));
            Assert.That(world.IsCurrent(path), Is.True);
        }

        [Test]
        public void CrossAreaRouteCombinesThreeLocalSegmentsAndTwoPortals()
        {
            TestWorld fixture = CreateThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaC));

            Complete(world, handle);

            Assert.That(world.TryGetPath(handle, out NavigationPathView path), Is.True);
            Assert.That(path.AreaCount, Is.EqualTo(3));
            Assert.That(path.PortalTransitionCount, Is.EqualTo(2));
            Assert.That(path.GetArea(0).AreaId, Is.EqualTo(fixture.AreaA.Id));
            Assert.That(path.GetArea(1).AreaId, Is.EqualTo(fixture.AreaB.Id));
            Assert.That(path.GetArea(2).AreaId, Is.EqualTo(fixture.AreaC.Id));
            Assert.That(path.GetPortalTransition(0).PortalId, Is.EqualTo(fixture.PortalAB.Id));
            Assert.That(path.GetPortalTransition(1).PortalId, Is.EqualTo(fixture.PortalBC.Id));
        }

        [Test]
        public void DirectionalPortalDoesNotInventAReverseRoute()
        {
            TestWorld fixture = CreateThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaC, fixture.AreaA));

            Complete(world, handle);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Failed));
            Assert.That(world.TryGetFailure(handle, out PathFailureReason failure), Is.True);
            Assert.That(failure, Is.EqualTo(PathFailureReason.NoGlobalRoute));
        }

        [Test]
        public void SemanticPoliciesSelectDifferentPolygonCorridors()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            SemanticId road = registry.Add("Road");
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            source.AddArea(area);
            NavigationPolygonRecord start = area.AddPolygon(Rectangle(0f, 0f, 1f, 2f));
            NavigationPolygonRecord upperRoad = area.AddPolygon(Rectangle(1f, 1f, 2f, 2f));
            NavigationPolygonRecord lower = area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            NavigationPolygonRecord goal = area.AddPolygon(Rectangle(2f, 0f, 3f, 2f));
            var roadMask = new SemanticMask(registry.SlotCapacity);
            roadMask.Set(0);
            upperRoad.SetSemantics(roadMask);
            NavigationBakeAsset bake = Bake(source);

            CompiledTraversalPolicy cautious = Compile(
                new TraversalPolicyBuilder(registry).SetCost(road, 8d, 0d),
                bake);
            CompiledTraversalPolicy reckless = Compile(
                new TraversalPolicyBuilder(registry).SetCost(road, 0.1d, 0d),
                bake);
            var startLocation = new NavigationLocation(area.Id, new Vector3(0.5f, 0f, 1f));
            var goalLocation = new NavigationLocation(area.Id, new Vector3(2.5f, 0f, 1f));

            using var world = new NavigationWorld(bake);
            PathRequestHandle cautiousHandle = world.Submit(new PathQuery(startLocation, goalLocation, cautious));
            PathRequestHandle recklessHandle = world.Submit(new PathQuery(startLocation, goalLocation, reckless));
            Complete(world, cautiousHandle, recklessHandle);

            Assert.That(world.TryGetPath(cautiousHandle, out NavigationPathView cautiousPath), Is.True);
            Assert.That(world.TryGetPath(recklessHandle, out NavigationPathView recklessPath), Is.True);
            Assert.That(Contains(cautiousPath, lower.Id), Is.True);
            Assert.That(Contains(cautiousPath, upperRoad.Id), Is.False);
            Assert.That(Contains(recklessPath, upperRoad.Id), Is.True);
            Assert.That(Contains(recklessPath, lower.Id), Is.False);
            Assert.That(cautiousPath.GetPolygon(0), Is.EqualTo(start.Id));
            Assert.That(cautiousPath.GetPolygon(cautiousPath.PolygonCount - 1), Is.EqualTo(goal.Id));
        }

        [Test]
        public void CancellationWinsBeforePublicationAndCallbackRunsExactlyOnce()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            int callbacks = 0;
            PathRequestHandle handle = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                _ => callbacks++);

            AdvanceUntilReadyForPublication(world, handle);
            Assert.That(world.Cancel(handle), Is.True);
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Cancelled));
            Assert.That(callbacks, Is.Zero);
            world.Tick(0);

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Cancelled));
            Assert.That(world.TryGetPath(handle, out _), Is.False);
        }

        [Test]
        public void AreaMutationDuringARequestPublishesStaleInsteadOfSuccess()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
            world.Tick(1);

            Assert.That(world.MarkAreaDirty(fixture.AreaA.Id), Is.True);
            Complete(world, handle);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Stale));
            Assert.That(world.TryGetPath(handle, out _), Is.False);
        }

        [Test]
        public void UnrelatedAreaRevisionDoesNotInvalidateCompletedLocalPath()
        {
            TestWorld fixture = CreateThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
            Complete(world, handle);
            Assert.That(world.TryGetPath(handle, out NavigationPathView path), Is.True);

            Assert.That(world.MarkAreaDirty(fixture.AreaC.Id), Is.True);
            world.Tick(0);

            Assert.That(world.IsCurrent(path), Is.True);
            Assert.That(world.GetAreaRevision(fixture.AreaA.Id), Is.EqualTo(path.GetRevision(0).Revision));
        }

        [Test]
        public void DisablingPortalAdvancesOnlyEndpointAreasAndTopology()
        {
            TestWorld fixture = CreateThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            ulong areaA = world.GetAreaRevision(fixture.AreaA.Id);
            ulong areaB = world.GetAreaRevision(fixture.AreaB.Id);
            ulong areaC = world.GetAreaRevision(fixture.AreaC.Id);
            ulong topology = world.TopologyRevision;

            Assert.That(world.SetPortalEnabled(fixture.PortalAB.Id, false), Is.True);
            world.Tick(0);

            Assert.That(world.GetAreaRevision(fixture.AreaA.Id), Is.GreaterThan(areaA));
            Assert.That(world.GetAreaRevision(fixture.AreaB.Id), Is.GreaterThan(areaB));
            Assert.That(world.GetAreaRevision(fixture.AreaC.Id), Is.EqualTo(areaC));
            Assert.That(world.TopologyRevision, Is.GreaterThan(topology));

            PathRequestHandle handle = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaC));
            Complete(world, handle);
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Failed));
        }

        [Test]
        public void WeightedQueuesServeLowPriorityWithoutBreakingPriorityFifo()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            var callbackOrder = new List<PathRequestHandle>();
            PathRequestHandle highFirst = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.High), callbackOrder.Add);
            PathRequestHandle highSecond = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.High), callbackOrder.Add);
            PathRequestHandle low = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.Low), callbackOrder.Add);

            world.Tick(3);
            Assert.That(world.GetStatus(low), Is.Not.EqualTo(PathRequestStatus.Queued));
            Complete(world, highFirst, highSecond, low);

            Assert.That(callbackOrder.IndexOf(highFirst), Is.LessThan(callbackOrder.IndexOf(highSecond)));
            Assert.That(callbackOrder, Does.Contain(low));
        }

        [Test]
        public void StaleQueueEntryCannotAdvanceAReusedSlotAtItsFormerPriority()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle cancelled = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.High));

            Assert.That(world.Cancel(cancelled), Is.True);
            world.Tick(0);
            Assert.That(world.Release(cancelled), Is.True);

            PathRequestHandle reused = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.Low));
            PathRequestHandle high = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.High));
            Assert.That(reused.Slot, Is.EqualTo(cancelled.Slot));
            Assert.That(reused.Generation, Is.Not.EqualTo(cancelled.Generation));

            world.Tick(1);

            Assert.That(world.GetStatus(high), Is.EqualTo(PathRequestStatus.RunningGlobal));
            Assert.That(world.GetStatus(reused), Is.EqualTo(PathRequestStatus.Queued));
        }

        [Test]
        public void ReleasingResultInvalidatesViewAndOldHandleGeneration()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle first = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
            Complete(world, first);
            Assert.That(world.TryGetPath(first, out NavigationPathView view), Is.True);
            NavigationPath copy = view.ToManagedCopy();

            Assert.That(world.Release(first), Is.True);
            Assert.That(view.IsValid, Is.False);
            PathRequestHandle second = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));

            Assert.That(second.Slot, Is.EqualTo(first.Slot));
            Assert.That(second.Generation, Is.Not.EqualTo(first.Generation));
            Assert.That(world.GetStatus(first), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.IsCurrent(copy), Is.True);
        }

        [Test]
        public void ResolverReportsAmbiguousOverlappingAreasUnlessHinted()
        {
            TestWorld fixture = CreateThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            Double3 point = new Double3(0.5d, 0d, 0.5d);

            Assert.That(world.Resolve(point, fixture.Policy, out _), Is.EqualTo(LocationResolveStatus.Ambiguous));
            Assert.That(
                world.Resolve(point, fixture.AreaB.Id, fixture.Policy, out NavigationLocation location),
                Is.EqualTo(LocationResolveStatus.Found));
            Assert.That(location.AreaId, Is.EqualTo(fixture.AreaB.Id));
        }

        [Test]
        public void BidirectionalPortalPublishesForwardAndInverseRigidTransforms()
        {
            var transform = new PortalTransform(
                new Double3(1_000.25d, -32.5d, 4_000.75d),
                Quaternion.Euler(13f, 91f, -7f));
            TestWorld fixture = CreateBidirectionalWorld(transform);
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle forward = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaB));
            PathRequestHandle reverse = world.Submit(Query(fixture, fixture.AreaB, fixture.AreaA));

            Complete(world, forward, reverse);

            Assert.That(world.TryGetPath(forward, out NavigationPathView forwardPath), Is.True);
            Assert.That(world.TryGetPath(reverse, out NavigationPathView reversePath), Is.True);
            NavigationPortalTransition forwardTransition = forwardPath.GetPortalTransition(0);
            NavigationPortalTransition reverseTransition = reversePath.GetPortalTransition(0);
            Assert.That(forwardTransition.Entry.AreaId, Is.EqualTo(fixture.AreaA.Id));
            Assert.That(forwardTransition.Exit.AreaId, Is.EqualTo(fixture.AreaB.Id));
            Assert.That(reverseTransition.Entry.AreaId, Is.EqualTo(fixture.AreaB.Id));
            Assert.That(reverseTransition.Exit.AreaId, Is.EqualTo(fixture.AreaA.Id));

            var probe = new Double3(17.25d, 4d, -8.5d);
            AssertDouble3(
                forwardTransition.EntryToExit.TransformPosition(probe),
                transform.TransformPosition(probe));
            AssertDouble3(
                reverseTransition.EntryToExit.TransformPosition(probe),
                transform.Inverse.TransformPosition(probe));
        }

        [Test]
        public void EqualCostGlobalRoutesChooseLowestStablePortalIdAndAccumulateExactSegmentCosts()
        {
            TestWorld fixture = CreateTiedThreeAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            var handles = new PathRequestHandle[8];
            var query = new PathQuery(
                new NavigationLocation(fixture.AreaA.Id, new Vector3(0.25f, 0f, 0.5f)),
                new NavigationLocation(fixture.AreaC.Id, new Vector3(0.75f, 0f, 0.5f)),
                fixture.Policy);
            for (int index = 0; index < handles.Length; index++)
            {
                handles[index] = world.Submit(query);
            }

            Complete(world, handles);

            for (int request = 0; request < handles.Length; request++)
            {
                Assert.That(world.TryGetPath(handles[request], out NavigationPathView path), Is.True);
                Assert.That(path.GetPortalTransition(0).PortalId, Is.EqualTo(fixture.PortalAB.Id));
                Assert.That(path.GetPortalTransition(1).PortalId, Is.EqualTo(fixture.PortalBC.Id));
                Assert.That(path.TotalCost, Is.EqualTo(7.5d).Within(1e-9d));

                double accumulated = 0d;
                for (int index = 0; index < path.AreaCount; index++)
                {
                    accumulated += path.GetArea(index).Cost;
                }

                for (int index = 0; index < path.PortalTransitionCount; index++)
                {
                    accumulated += path.GetPortalTransition(index).Cost;
                }

                Assert.That(path.TotalCost, Is.EqualTo(accumulated).Within(1e-9d));
            }
        }

        [Test]
        public void SuccessfulCallbackIsDeferredAndDeliveredExactlyOnce()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            int callbackCount = 0;
            PathRequestStatus callbackStatus = PathRequestStatus.Invalid;
            PathRequestHandle callbackHandle = default;
            PathRequestHandle handle = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                completed =>
                {
                    callbackCount++;
                    callbackHandle = completed;
                    callbackStatus = world.GetStatus(completed);
                });

            AdvanceUntilReadyForPublication(world, handle);
            Assert.That(callbackCount, Is.Zero);
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.RunningLocal));
            world.Tick(0);
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(callbackHandle, Is.EqualTo(handle));
            Assert.That(callbackStatus, Is.EqualTo(PathRequestStatus.Completed));

            world.Tick(64);
            world.Tick(0);
            Assert.That(callbackCount, Is.EqualTo(1));
        }

        [Test]
        public void SubmitBatchOverloadsPreserveOrderAndValidateDestinations()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathQuery[] queries =
            {
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.Low),
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.Normal),
                Query(fixture, fixture.AreaA, fixture.AreaA, PathPriority.High)
            };

            PathRequestHandle[] allocated = world.SubmitBatch(queries);
            var destination = new PathRequestHandle[queries.Length + 1];
            world.SubmitBatch(queries, destination);

            for (int index = 0; index < queries.Length; index++)
            {
                Assert.That(allocated[index].IsValid, Is.True);
                Assert.That(destination[index].IsValid, Is.True);
                Assert.That(allocated[index], Is.Not.EqualTo(destination[index]));
                Assert.That(world.GetStatus(allocated[index]), Is.EqualTo(PathRequestStatus.Queued));
                Assert.That(world.GetStatus(destination[index]), Is.EqualTo(PathRequestStatus.Queued));
            }

            Assert.That(destination[queries.Length], Is.EqualTo(default(PathRequestHandle)));
            Assert.Throws<ArgumentNullException>(
                () => world.SubmitBatch((IReadOnlyList<PathQuery>)null));
            Assert.Throws<ArgumentNullException>(
                () => world.SubmitBatch((IReadOnlyList<PathQuery>)null, destination));
            Assert.Throws<ArgumentException>(() => world.SubmitBatch(queries, null));
            Assert.Throws<ArgumentException>(
                () => world.SubmitBatch(queries, new PathRequestHandle[queries.Length - 1]));

            Complete(world, allocated);
            Complete(world, destination[0], destination[1], destination[2]);
            for (int index = 0; index < queries.Length; index++)
            {
                Assert.That(world.GetStatus(allocated[index]), Is.EqualTo(PathRequestStatus.Completed));
                Assert.That(world.GetStatus(destination[index]), Is.EqualTo(PathRequestStatus.Completed));
            }
        }

        [Test]
        public void InvalidAndReleasedHandlesHaveNoObservableRequestState()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);

            Assert.That(world.GetStatus(default), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.TryGetFailure(default, out PathFailureReason emptyFailure), Is.False);
            Assert.That(emptyFailure, Is.EqualTo(PathFailureReason.None));
            Assert.That(world.TryGetPath(default, out _), Is.False);
            Assert.That(world.Cancel(default), Is.False);
            Assert.That(world.Release(default), Is.False);

            PathRequestHandle invalid = world.Submit(default);
            Assert.That(world.GetStatus(invalid), Is.EqualTo(PathRequestStatus.Queued));
            Assert.That(world.Release(invalid), Is.False);
            world.Tick(0);
            Assert.That(world.GetStatus(invalid), Is.EqualTo(PathRequestStatus.Failed));
            Assert.That(world.TryGetFailure(invalid, out PathFailureReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(PathFailureReason.InvalidRequest));
            Assert.That(world.Release(invalid), Is.True);

            PathRequestHandle reused = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
            Assert.That(reused.Slot, Is.EqualTo(invalid.Slot));
            Assert.That(reused.Generation, Is.Not.EqualTo(invalid.Generation));
            Assert.That(world.GetStatus(invalid), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.Cancel(invalid), Is.False);
            Assert.That(world.Release(invalid), Is.False);
        }

        [TestCase(PathOutputFlags.None, 0, 0)]
        [TestCase(PathOutputFlags.PolygonCorridor, 2, 0)]
        [TestCase(PathOutputFlags.SteeringTargets, 0, 1)]
        [TestCase(PathOutputFlags.Default, 2, 1)]
        public void EveryOutputFlagCombinationReturnsOnlyRequestedGuidance(
            PathOutputFlags output,
            int expectedPolygons,
            int expectedSteeringTargets)
        {
            TestWorld fixture = CreateTwoPolygonWorld();
            using var world = new NavigationWorld(fixture.Bake);
            var query = new PathQuery(
                new NavigationLocation(fixture.AreaA.Id, new Vector3(0.25f, 0f, 0.5f)),
                new NavigationLocation(fixture.AreaA.Id, new Vector3(1.75f, 0f, 0.5f)),
                fixture.Policy,
                PathPriority.Normal,
                output);

            PathRequestHandle handle = world.Submit(query);
            Complete(world, handle);

            Assert.That(world.TryGetPath(handle, out NavigationPathView path), Is.True);
            Assert.That(path.PolygonCount, Is.EqualTo(expectedPolygons));
            Assert.That(path.CrossingSpanCount, Is.EqualTo(expectedPolygons == 0 ? 0 : 1));
            Assert.That(path.SteeringTargetCount, Is.EqualTo(expectedSteeringTargets));
            Assert.That(path.GetArea(0).PolygonCount, Is.EqualTo(expectedPolygons));
            Assert.That(path.GetArea(0).SteeringCount, Is.EqualTo(expectedSteeringTargets));
        }

        [TestCase(0, PathRequestStatus.Queued)]
        [TestCase(1, PathRequestStatus.RunningGlobal)]
        [TestCase(2, PathRequestStatus.RunningLocal)]
        [TestCase(3, PathRequestStatus.RunningLocal)]
        public void CancellationWinsAtEveryPreterminalStage(
            int workSteps,
            PathRequestStatus expectedStatus)
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            int callbackCount = 0;
            PathRequestHandle handle = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                _ => callbackCount++);

            switch (workSteps)
            {
                case 1:
                    AdvanceUntilStatus(world, handle, PathRequestStatus.RunningGlobal);
                    break;
                case 2:
                    AdvanceUntilInFlight(world, handle);
                    break;
                case 3:
                    AdvanceUntilReadyForPublication(world, handle);
                    break;
            }

            Assert.That(world.GetStatus(handle), Is.EqualTo(expectedStatus));
            Assert.That(world.Cancel(handle), Is.True);
            Assert.That(world.Cancel(handle), Is.False);
            Assert.That(world.Release(handle), Is.False);
            world.Tick(0);

            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Cancelled));
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(world.TryGetFailure(handle, out _), Is.False);
            Assert.That(world.TryGetPath(handle, out _), Is.False);
            Assert.That(world.Release(handle), Is.True);
        }

        [Test]
        public void CallbackMayReleaseSubmitAndCancelRequests()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle submitted = default;
            PathRequestHandle victim = default;
            bool released = false;
            bool cancelled = false;
            PathRequestHandle completed = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                handle =>
                {
                    released = world.Release(handle);
                    submitted = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
                    cancelled = world.Cancel(victim);
                });

            AdvanceUntilReadyForPublication(world, completed);
            victim = world.Submit(Query(fixture, fixture.AreaA, fixture.AreaA));
            world.Tick(0);

            Assert.That(released, Is.True);
            Assert.That(cancelled, Is.True);
            Assert.That(world.GetStatus(completed), Is.EqualTo(PathRequestStatus.Invalid));
            Assert.That(world.GetStatus(victim), Is.EqualTo(PathRequestStatus.Cancelled));
            Assert.That(world.GetStatus(submitted), Is.EqualTo(PathRequestStatus.Queued));
            Complete(world, submitted);
            Assert.That(world.GetStatus(submitted), Is.EqualTo(PathRequestStatus.Completed));
        }

        [Test]
        public void CallbackExceptionDoesNotPreventLaterCallbacks()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            int delivered = 0;
            PathRequestHandle throwing = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                _ => throw new InvalidOperationException("callback boom"));
            PathRequestHandle succeeding = world.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                _ => delivered++);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: callback boom"));
            Assert.DoesNotThrow(() => Complete(world, throwing, succeeding));

            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(world.GetStatus(throwing), Is.EqualTo(PathRequestStatus.Completed));
            Assert.That(world.GetStatus(succeeding), Is.EqualTo(PathRequestStatus.Completed));
        }

        [Test]
        public void WarmedIdleTicksDoNotAllocateManagedMemory()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            using var world = new NavigationWorld(fixture.Bake);
            GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 32; index++)
            {
                world.Tick(0);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1024; index++)
            {
                world.Tick(0);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Warmed idle Tick(0) calls allocated managed memory.");
        }

        [Test]
        public void ManagedResultCopyOutlivesReleasedViewAndDisposedWorld()
        {
            TestWorld fixture = CreateTwoPolygonWorld();
            var world = new NavigationWorld(fixture.Bake);
            PathRequestHandle handle = world.Submit(new PathQuery(
                new NavigationLocation(fixture.AreaA.Id, new Vector3(0.25f, 0f, 0.5f)),
                new NavigationLocation(fixture.AreaA.Id, new Vector3(1.75f, 0f, 0.5f)),
                fixture.Policy));
            Complete(world, handle);
            Assert.That(world.TryGetPath(handle, out NavigationPathView view), Is.True);
            NavigationPath copy = view.ToManagedCopy();
            double totalCost = copy.TotalCost;

            Assert.That(world.Release(handle), Is.True);
            Assert.That(view.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = view.TotalCost);
            world.Dispose();

            Assert.That(copy.TotalCost, Is.EqualTo(totalCost));
            Assert.That(copy.PolygonCorridor.Count, Is.EqualTo(2));
            Assert.That(copy.CrossingSpans.Count, Is.EqualTo(1));
        }

        [Test]
        public void HostCancelsDestroyedOwnerAndDisposesWorldWhenDisabled()
        {
            TestWorld fixture = CreateSingleAreaWorld();
            var hostObject = new GameObject("NavigationWorldHost test");
            _assets.Add(hostObject);
            NavigationWorldHost host = hostObject.AddComponent<NavigationWorldHost>();
            typeof(NavigationWorldHost)
                .GetField("_bake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(host, fixture.Bake);
            Assert.That(host.Initialize(), Is.True);
            NavigationWorld world = host.World;
            var owner = new GameObject("request owner");
            int callbackCount = 0;
            PathRequestHandle handle = host.Submit(
                Query(fixture, fixture.AreaA, fixture.AreaA),
                owner,
                _ => callbackCount++);

            UnityEngine.Object.DestroyImmediate(owner);
            typeof(NavigationWorldHost)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(host, null);

            Assert.That(callbackCount, Is.Zero);
            Assert.That(world.GetStatus(handle), Is.EqualTo(PathRequestStatus.Invalid));
            hostObject.SetActive(false);
            Assert.That(world.IsDisposed, Is.True);
            Assert.That(host.World, Is.Null);
        }

        private TestWorld CreateSingleAreaWorld()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            return new TestWorld
            {
                Registry = registry,
                Source = source,
                Bake = bake,
                Policy = Compile(new TraversalPolicyBuilder(registry), bake),
                AreaA = area
            };
        }

        private TestWorld CreateTwoPolygonWorld()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            area.AddPolygon(Rectangle(1f, 0f, 2f, 1f));
            source.AddArea(area);
            NavigationBakeAsset bake = Bake(source);
            return new TestWorld
            {
                Registry = registry,
                Source = source,
                Bake = bake,
                Policy = Compile(new TraversalPolicyBuilder(registry), bake),
                AreaA = area
            };
        }

        private TestWorld CreateThreeAreaWorld()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset areaA = AddSinglePolygonArea(source);
            NavigationAreaAsset areaB = AddSinglePolygonArea(source);
            NavigationAreaAsset areaC = AddSinglePolygonArea(source);
            NavigationPolygonRecord polygonA = areaA.Polygons[0];
            NavigationPolygonRecord polygonB = areaB.Polygons[0];
            NavigationPolygonRecord polygonC = areaC.Polygons[0];
            NavigationPortalRecord portalAB = source.AddPortal(
                Span(areaA, polygonA, 2),
                Span(areaB, polygonB, 2),
                PortalDirection.SourceToDestination,
                1d,
                PortalTransform.Identity);
            NavigationPortalRecord portalBC = source.AddPortal(
                Span(areaB, polygonB, 0),
                Span(areaC, polygonC, 0),
                PortalDirection.SourceToDestination,
                1d,
                PortalTransform.Identity);
            NavigationBakeAsset bake = Bake(source);
            return new TestWorld
            {
                Registry = registry,
                Source = source,
                Bake = bake,
                Policy = Compile(new TraversalPolicyBuilder(registry), bake),
                AreaA = areaA,
                AreaB = areaB,
                AreaC = areaC,
                PortalAB = portalAB,
                PortalBC = portalBC
            };
        }

        private TestWorld CreateBidirectionalWorld(PortalTransform transform)
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset areaA = AddSinglePolygonArea(source);
            NavigationAreaAsset areaB = AddSinglePolygonArea(source);
            areaB.SetFrame(new AreaFrame(transform.Translation, transform.Rotation));
            NavigationPortalRecord portal = source.AddPortal(
                Span(areaA, areaA.Polygons[0], 2),
                Span(areaB, areaB.Polygons[0], 2),
                PortalDirection.Bidirectional,
                1d,
                transform);
            NavigationBakeAsset bake = Bake(source);
            return new TestWorld
            {
                Registry = registry,
                Source = source,
                Bake = bake,
                Policy = Compile(new TraversalPolicyBuilder(registry), bake),
                AreaA = areaA,
                AreaB = areaB,
                PortalAB = portal
            };
        }

        private TestWorld CreateTiedThreeAreaWorld()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset areaA = AddSinglePolygonArea(source);
            NavigationAreaAsset areaB = AddSinglePolygonArea(source);
            NavigationAreaAsset areaC = AddSinglePolygonArea(source);
            var doorwayTransform = new PortalTransform(
                new Double3(-1d, 0d, 0d),
                Quaternion.identity);
            NavigationPortalRecord firstAB = source.AddPortal(
                Span(areaA, areaA.Polygons[0], 2),
                Span(areaB, areaB.Polygons[0], 0),
                PortalDirection.SourceToDestination,
                2d,
                doorwayTransform);
            NavigationPortalRecord secondAB = source.AddPortal(
                Span(areaA, areaA.Polygons[0], 2),
                Span(areaB, areaB.Polygons[0], 0),
                PortalDirection.SourceToDestination,
                2d,
                doorwayTransform);
            NavigationPortalRecord portalBC = source.AddPortal(
                Span(areaB, areaB.Polygons[0], 2),
                Span(areaC, areaC.Polygons[0], 0),
                PortalDirection.SourceToDestination,
                3d,
                doorwayTransform);
            NavigationBakeAsset bake = Bake(source);
            return new TestWorld
            {
                Registry = registry,
                Source = source,
                Bake = bake,
                Policy = Compile(new TraversalPolicyBuilder(registry), bake),
                AreaA = areaA,
                AreaB = areaB,
                AreaC = areaC,
                PortalAB = firstAB.Id.CompareTo(secondAB.Id) <= 0 ? firstAB : secondAB,
                PortalBC = portalBC
            };
        }

        private NavigationAreaAsset AddSinglePolygonArea(NavigationWorldAsset world)
        {
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            area.AddPolygon(Rectangle(0f, 0f, 1f, 1f));
            world.AddArea(area);
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
            TraversalPolicyBuilder builder,
            NavigationBakeAsset bake)
        {
            Assert.That(builder.TryCompile(bake, out CompiledTraversalPolicy policy, out string error),
                Is.True, error);
            return policy;
        }

        private static PathQuery Query(
            TestWorld fixture,
            NavigationAreaAsset start,
            NavigationAreaAsset goal,
            PathPriority priority = PathPriority.Normal)
        {
            return new PathQuery(
                new NavigationLocation(start.Id, new Vector3(0.25f, 0f, 0.25f)),
                new NavigationLocation(goal.Id, new Vector3(0.75f, 0f, 0.75f)),
                fixture.Policy,
                priority);
        }

        private static void Complete(NavigationWorld world, params PathRequestHandle[] handles)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(64);
                bool allTerminal = true;
                for (int index = 0; index < handles.Length; index++)
                {
                    PathRequestStatus status = world.GetStatus(handles[index]);
                    allTerminal &= status == PathRequestStatus.Completed ||
                                   status == PathRequestStatus.Failed ||
                                   status == PathRequestStatus.Cancelled ||
                                   status == PathRequestStatus.Stale;
                }

                if (allTerminal)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("Requests did not reach terminal states within 10 seconds.");
        }

        private static void AdvanceUntilStatus(
            NavigationWorld world,
            PathRequestHandle handle,
            PathRequestStatus expected)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                if (world.GetStatus(handle) == expected)
                {
                    return;
                }

                world.Tick(1);
                Thread.Yield();
            }

            Assert.Fail($"Request did not reach {expected} within 10 seconds.");
        }

        private static void AdvanceUntilInFlight(
            NavigationWorld world,
            PathRequestHandle handle)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
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

            Assert.Fail("Request did not schedule local work within 10 seconds.");
        }

        private static void AdvanceUntilReadyForPublication(
            NavigationWorld world,
            params PathRequestHandle[] handles)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10d))
            {
                world.Tick(64);
                bool ready = world.InFlightSearchCount == 0;
                for (int index = 0; index < handles.Length; index++)
                {
                    ready &= world.GetStatus(handles[index]) == PathRequestStatus.RunningLocal;
                }

                if (ready)
                {
                    return;
                }

                Thread.Yield();
            }

            Assert.Fail("Requests did not reach pre-publication within 10 seconds.");
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

        private static PortalEntrySpan Span(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            int edge)
        {
            NavigationVertexRecord start = polygon.Vertices[edge];
            NavigationVertexRecord end = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                start.OutgoingEdgeId,
                start.Position,
                end.Position);
        }

        private static bool Contains(NavigationPathView path, PolygonId id)
        {
            for (int index = 0; index < path.PolygonCount; index++)
            {
                if (path.GetPolygon(index) == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertDouble3(Double3 actual, Double3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-8d));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-8d));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(1e-8d));
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

        private sealed class TestWorld
        {
            internal SemanticRegistryAsset Registry;
            internal NavigationWorldAsset Source;
            internal NavigationBakeAsset Bake;
            internal CompiledTraversalPolicy Policy;
            internal NavigationAreaAsset AreaA;
            internal NavigationAreaAsset AreaB;
            internal NavigationAreaAsset AreaC;
            internal NavigationPortalRecord PortalAB;
            internal NavigationPortalRecord PortalBC;
        }
    }
}
