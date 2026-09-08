using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    public sealed class NavigationWorld : IDisposable
    {
        private static readonly PathPriority[] ServiceOrder =
        {
            PathPriority.High,
            PathPriority.Normal,
            PathPriority.High,
            PathPriority.Low,
            PathPriority.High,
            PathPriority.Normal,
            PathPriority.High
        };

        private sealed class RequestSlot
        {
            internal uint Generation;
            internal PathRequestStatus Status;
            internal PathFailureReason Failure;
            internal PathQuery Query;
            internal Action<PathRequestHandle> Callback;
            internal bool CallbackPending;
            internal long PublishTick;
            internal PathRequestStatus PendingStatus;
            internal PathFailureReason PendingFailure;
            internal PathResultData PendingResult;
            internal PathResultData Result;
            internal NavigationSearch.State Search;
            internal bool LocalSearchPending;
            internal long PhysicalWorkId;
        }

        private sealed class InFlightSearch
        {
            internal long Id;
            internal long AdmissionSequence;
            internal int LaneIndex;
            internal PathRequestHandle OriginHandle;
            internal NavigationSearch.State Search;
        }

        private enum MutationKind : byte
        {
            Polygon,
            Adjacency,
            Portal,
            AreaDirty
        }

        private readonly struct PendingMutation
        {
            internal PendingMutation(MutationKind kind, int first, int second, bool enabled)
            {
                Kind = kind;
                First = first;
                Second = second;
                Enabled = enabled;
            }

            internal MutationKind Kind { get; }
            internal int First { get; }
            internal int Second { get; }
            internal bool Enabled { get; }
        }

        private readonly NavigationRuntimeData _data;
        private readonly List<RequestSlot> _slots = new List<RequestSlot>();
        private readonly Stack<int> _freeSlots = new Stack<int>();
        private readonly Queue<PathRequestHandle>[] _queues =
        {
            new Queue<PathRequestHandle>(),
            new Queue<PathRequestHandle>(),
            new Queue<PathRequestHandle>()
        };
        private readonly Queue<PendingMutation> _mutations = new Queue<PendingMutation>();
        private readonly InFlightSearch[] _inFlight;
        private int _serviceCursor;
        private int _inFlightSearchCount;
        private long _nextPhysicalWorkId;
        private long _nextAdmissionSequence;
        private long _tick;
        private bool _disposed;

        public NavigationWorld(NavigationBakeAsset bake, int cacheCapacityPerArea = 256)
            : this(bake, cacheCapacityPerArea, 4)
        {
        }

        public NavigationWorld(
            NavigationBakeAsset bake,
            int cacheCapacityPerArea,
            int maxConcurrentSearches)
        {
            if (maxConcurrentSearches < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentSearches));
            }

            EffectiveMaxConcurrentSearches = Math.Min(
                maxConcurrentSearches,
                Math.Max(1, JobsUtility.JobWorkerCount));
            _data = new NavigationRuntimeData(
                bake,
                cacheCapacityPerArea,
                EffectiveMaxConcurrentSearches);
            _inFlight = new InFlightSearch[EffectiveMaxConcurrentSearches];
        }

        public bool IsDisposed => _disposed;
        public int EffectiveMaxConcurrentSearches { get; }
        internal int InFlightSearchCount => _inFlightSearchCount;
        internal int InUseScratchLaneCount => _data.PolygonSearch.InUseLaneCount;
        internal int ActiveSnapshotCount => _data.ActiveSnapshotCount;
        internal int CurrentSnapshotReferenceCount => _data.CurrentSnapshot.ReferenceCount;
        internal int AllocatedRequestSlotCount => _slots.Count;
        internal bool IsPhysicalWorkInFlight(PathRequestHandle handle)
        {
            for (int index = 0; index < _inFlight.Length; index++)
            {
                InFlightSearch physical = _inFlight[index];
                if (physical != null &&
                    physical.OriginHandle.Slot == handle.Slot &&
                    physical.OriginHandle.Generation == handle.Generation)
                {
                    return true;
                }
            }

            return false;
        }

        public ulong TopologyRevision
        {
            get
            {
                ThrowIfDisposed();
                return _data.TopologyRevision;
            }
        }

        public PathRequestHandle Submit(
            PathQuery query,
            Action<PathRequestHandle> onTerminal = null)
        {
            ThrowIfDisposed();
            int slotIndex = AllocateSlot(out RequestSlot slot);
            var handle = new PathRequestHandle(slotIndex, slot.Generation);
            slot.Query = query;
            slot.Callback = onTerminal;
            slot.Status = PathRequestStatus.Queued;

            if (!query.IsValid || query.Policy.RegistryFingerprint != _data.RegistryFingerprint ||
                query.Policy.WordCount != _data.SemanticWordCount)
            {
                ScheduleTerminal(slot, PathRequestStatus.Failed, PathFailureReason.InvalidRequest, null);
                return handle;
            }

            _queues[(int)query.Priority].Enqueue(handle);
            return handle;
        }

        public PathRequestHandle[] SubmitBatch(IReadOnlyList<PathQuery> queries)
        {
            if (queries == null)
            {
                throw new ArgumentNullException(nameof(queries));
            }

            var handles = new PathRequestHandle[queries.Count];
            SubmitBatch(queries, handles);
            return handles;
        }

        public void SubmitBatch(IReadOnlyList<PathQuery> queries, PathRequestHandle[] destination)
        {
            ThrowIfDisposed();
            if (queries == null)
            {
                throw new ArgumentNullException(nameof(queries));
            }

            if (destination == null || destination.Length < queries.Count)
            {
                throw new ArgumentException("The destination must have room for every query.", nameof(destination));
            }

            for (int index = 0; index < queries.Count; index++)
            {
                destination[index] = Submit(queries[index]);
            }
        }

        public PathRequestStatus GetStatus(PathRequestHandle handle)
        {
            return TryGetSlot(handle, out RequestSlot slot) ? slot.Status : PathRequestStatus.Invalid;
        }

        public bool TryGetFailure(PathRequestHandle handle, out PathFailureReason reason)
        {
            if (TryGetSlot(handle, out RequestSlot slot) &&
                (slot.Status == PathRequestStatus.Failed || slot.Status == PathRequestStatus.Stale))
            {
                reason = slot.Failure;
                return true;
            }

            reason = PathFailureReason.None;
            return false;
        }

        public bool TryGetPath(PathRequestHandle handle, out NavigationPathView path)
        {
            if (TryGetSlot(handle, out RequestSlot slot) &&
                slot.Status == PathRequestStatus.Completed && slot.Result != null)
            {
                path = new NavigationPathView(this, handle);
                return true;
            }

            path = default;
            return false;
        }

        public bool Cancel(PathRequestHandle handle)
        {
            ThrowIfDisposed();
            if (!TryGetSlot(handle, out RequestSlot slot) || IsTerminal(slot.Status))
            {
                return false;
            }

            slot.Search?.Dispose();
            slot.Search = null;
            slot.LocalSearchPending = false;

            slot.Status = PathRequestStatus.Cancelled;
            ScheduleTerminal(slot, PathRequestStatus.Cancelled, PathFailureReason.None, null);
            return true;
        }

        public bool Release(PathRequestHandle handle)
        {
            ThrowIfDisposed();
            if (!TryGetSlot(handle, out RequestSlot slot) || !IsTerminal(slot.Status) || slot.CallbackPending)
            {
                return false;
            }

            slot.Status = PathRequestStatus.Invalid;
            slot.Query = default;
            slot.Callback = null;
            slot.Result = null;
            slot.PendingResult = null;
            slot.Search?.Dispose();
            slot.Search = null;
            slot.LocalSearchPending = false;
            slot.PhysicalWorkId = 0L;
            _freeSlots.Push(handle.Slot);
            return true;
        }

        public void Tick(int workBudget = 64)
        {
            ThrowIfDisposed();
            if (workBudget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workBudget));
            }

            unchecked
            {
                _tick++;
            }

            ApplyPendingMutations();
            PublishReadyRequests();
            if (workBudget == 0)
            {
                return;
            }

            bool scheduledJobs = false;
            int work = 0;
            while (work < workBudget && TryHarvestCompletedSearch())
            {
                work++;
            }

            while (work < workBudget)
            {
                if (!TryDequeueWork(out PathRequestHandle handle))
                {
                    break;
                }

                if (!AdvanceRequest(handle, ref scheduledJobs))
                {
                    break;
                }

                work++;
            }

            if (scheduledJobs)
            {
                JobHandle.ScheduleBatchedJobs();
            }
        }

        public LocationResolveStatus Resolve(
            Double3 universePosition,
            CompiledTraversalPolicy policy,
            out NavigationLocation location)
        {
            ThrowIfDisposed();
            return NavigationSearch.Resolve(_data, policy, universePosition, default, out location);
        }

        public LocationResolveStatus Resolve(
            Double3 universePosition,
            AreaId areaHint,
            CompiledTraversalPolicy policy,
            out NavigationLocation location)
        {
            ThrowIfDisposed();
            return NavigationSearch.Resolve(_data, policy, universePosition, areaHint, out location);
        }

        public ulong GetAreaRevision(AreaId areaId)
        {
            ThrowIfDisposed();
            return _data.AreaById.TryGetValue(areaId, out int areaIndex)
                ? _data.AreaRevisions[areaIndex]
                : 0UL;
        }

        public bool SetPolygonEnabled(PolygonId polygonId, bool enabled)
        {
            ThrowIfDisposed();
            if (!_data.PolygonById.TryGetValue(polygonId, out int polygonIndex))
            {
                return false;
            }

            _mutations.Enqueue(new PendingMutation(MutationKind.Polygon, polygonIndex, -1, enabled));
            return true;
        }

        public bool SetAdjacencyEnabled(PolygonId first, PolygonId second, bool enabled)
        {
            ThrowIfDisposed();
            if (!_data.PolygonById.TryGetValue(first, out int firstIndex) ||
                !_data.PolygonById.TryGetValue(second, out int secondIndex) ||
                !_data.AdjacencyByPolygonPair.ContainsKey(new RuntimeEdgePair(firstIndex, secondIndex)))
            {
                return false;
            }

            _mutations.Enqueue(new PendingMutation(MutationKind.Adjacency, firstIndex, secondIndex, enabled));
            return true;
        }

        public bool SetPortalEnabled(PortalId portalId, bool enabled)
        {
            ThrowIfDisposed();
            if (!_data.PortalById.TryGetValue(portalId, out int portalIndex))
            {
                return false;
            }

            _mutations.Enqueue(new PendingMutation(MutationKind.Portal, portalIndex, -1, enabled));
            return true;
        }

        public bool MarkAreaDirty(AreaId areaId)
        {
            ThrowIfDisposed();
            if (!_data.AreaById.TryGetValue(areaId, out int areaIndex))
            {
                return false;
            }

            _mutations.Enqueue(new PendingMutation(MutationKind.AreaDirty, areaIndex, -1, false));
            return true;
        }

        public bool IsCurrent(NavigationPathView path)
        {
            ThrowIfDisposed();
            return ReferenceEquals(path.World, this) && path.IsValid && IsCurrent(GetResult(path.Handle));
        }

        public bool IsCurrent(NavigationPath path)
        {
            ThrowIfDisposed();
            if (path == null || path.TopologyRevision != _data.TopologyRevision)
            {
                return false;
            }

            for (int index = 0; index < path.Revisions.Count; index++)
            {
                NavigationRevisionStamp stamp = path.Revisions[index];
                if (!_data.AreaById.TryGetValue(stamp.AreaId, out int areaIndex) ||
                    _data.AreaRevisions[areaIndex] != stamp.Revision)
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int index = 0; index < _inFlight.Length; index++)
            {
                InFlightSearch active = _inFlight[index];
                if (active == null)
                {
                    continue;
                }

                try
                {
                    _data.PolygonSearch.Complete(active.LaneIndex, out _);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    _data.PolygonSearch.Release(active.LaneIndex);
                    active.Search.Dispose();
                    _inFlight[index] = null;
                }
            }

            _inFlightSearchCount = 0;
            for (int index = 0; index < _slots.Count; index++)
            {
                RequestSlot slot = _slots[index];
                slot.Search?.Dispose();
                slot.Search = null;
                slot.Callback = null;
                slot.Result = null;
                slot.PendingResult = null;
                slot.Status = PathRequestStatus.Invalid;
            }

            for (int index = 0; index < _data.Caches.Length; index++)
            {
                _data.Caches[index].Clear();
            }

            _data.Dispose();

            _mutations.Clear();
            for (int index = 0; index < _queues.Length; index++)
            {
                _queues[index].Clear();
            }
        }

        internal bool IsPathViewValid(PathRequestHandle handle)
        {
            return !_disposed && TryGetSlot(handle, out RequestSlot slot) &&
                   slot.Status == PathRequestStatus.Completed && slot.Result != null;
        }

        internal double GetPathTotalCost(PathRequestHandle handle) => GetResult(handle).TotalCost;
        internal int GetPathAreaCount(PathRequestHandle handle) => GetResult(handle).Areas.Length;
        internal int GetPathPortalCount(PathRequestHandle handle) => GetResult(handle).Portals.Length;
        internal int GetPathPolygonCount(PathRequestHandle handle) => GetResult(handle).Polygons.Length;
        internal int GetPathCrossingCount(PathRequestHandle handle) => GetResult(handle).Crossings.Length;
        internal int GetPathSteeringCount(PathRequestHandle handle) => GetResult(handle).SteeringTargets.Length;
        internal int GetPathRevisionCount(PathRequestHandle handle) => GetResult(handle).Revisions.Length;
        internal ulong GetPathTopologyRevision(PathRequestHandle handle) => GetResult(handle).TopologyRevision;
        internal NavigationAreaSegment GetPathArea(PathRequestHandle handle, int index) =>
            GetResult(handle).Areas[index];
        internal NavigationPortalTransition GetPathPortal(PathRequestHandle handle, int index) =>
            GetResult(handle).Portals[index];
        internal PolygonId GetPathPolygon(PathRequestHandle handle, int index) =>
            GetResult(handle).Polygons[index];
        internal NavigationCrossingSpan GetPathCrossing(PathRequestHandle handle, int index) =>
            GetResult(handle).Crossings[index];
        internal Vector3 GetPathSteering(PathRequestHandle handle, int index) =>
            GetResult(handle).SteeringTargets[index];
        internal NavigationRevisionStamp GetPathRevision(PathRequestHandle handle, int index) =>
            GetResult(handle).Revisions[index];
        internal NavigationPath CopyPath(PathRequestHandle handle) => GetResult(handle).Copy();

        private int AllocateSlot(out RequestSlot slot)
        {
            int index;
            if (_freeSlots.Count > 0)
            {
                index = _freeSlots.Pop();
                slot = _slots[index];
                unchecked
                {
                    slot.Generation++;
                    if (slot.Generation == 0U)
                    {
                        slot.Generation = 1U;
                    }
                }
            }
            else
            {
                index = _slots.Count;
                slot = new RequestSlot { Generation = 1U };
                _slots.Add(slot);
            }

            slot.Status = PathRequestStatus.Invalid;
            slot.Failure = PathFailureReason.None;
            slot.CallbackPending = false;
            slot.PendingResult = null;
            slot.Result = null;
            slot.Search = null;
            slot.LocalSearchPending = false;
            slot.PhysicalWorkId = 0L;
            return index;
        }

        private bool TryGetSlot(PathRequestHandle handle, out RequestSlot slot)
        {
            if (handle.IsValid && (uint)handle.Slot < (uint)_slots.Count)
            {
                slot = _slots[handle.Slot];
                return slot.Generation == handle.Generation && slot.Status != PathRequestStatus.Invalid;
            }

            slot = null;
            return false;
        }

        private bool TryDequeueWork(out PathRequestHandle handle)
        {
            for (int attempt = 0; attempt < ServiceOrder.Length; attempt++)
            {
                PathPriority priority = ServiceOrder[_serviceCursor];
                _serviceCursor = (_serviceCursor + 1) % ServiceOrder.Length;
                Queue<PathRequestHandle> queue = _queues[(int)priority];
                while (queue.Count > 0)
                {
                    PathRequestHandle candidate = queue.Dequeue();
                    if (TryGetSlot(candidate, out RequestSlot slot) &&
                        (slot.Status == PathRequestStatus.Queued ||
                         ((slot.Status == PathRequestStatus.RunningGlobal ||
                           slot.Status == PathRequestStatus.RunningLocal) &&
                          slot.Search != null)))
                    {
                        handle = candidate;
                        return true;
                    }
                }
            }

            handle = default;
            return false;
        }

        private bool AdvanceRequest(PathRequestHandle handle, ref bool scheduledJobs)
        {
            RequestSlot slot = _slots[handle.Slot];
            switch (slot.Status)
            {
                case PathRequestStatus.Queued:
                    NavigationRuntimeSnapshot snapshot = _data.AcquireCurrentSnapshot();
                    if (!NavigationSearch.State.TryCreate(
                            _data,
                            snapshot,
                            slot.Query,
                            out NavigationSearch.State search,
                            out PathFailureReason createFailure))
                    {
                        snapshot.Release();
                        ScheduleTerminal(slot, PathRequestStatus.Failed, createFailure, null);
                        return true;
                    }

                    slot.Search = search;
                    slot.Status = PathRequestStatus.RunningGlobal;
                    EnqueueActive(handle, slot.Query.Priority);
                    return true;

                case PathRequestStatus.RunningGlobal:
                case PathRequestStatus.RunningLocal:
                    return AdvanceSearch(handle, slot, ref scheduledJobs);
            }

            return true;
        }

        private bool AdvanceSearch(
            PathRequestHandle handle,
            RequestSlot slot,
            ref bool scheduledJobs)
        {
            if (slot.LocalSearchPending)
            {
                return TryScheduleLocalSearch(handle, slot, ref scheduledJobs);
            }

            SearchAdvanceStatus advance = slot.Search.Advance(
                out PathResultData result,
                out PathFailureReason failure);
            switch (advance)
            {
                case SearchAdvanceStatus.Continue:
                    EnqueueActive(handle, slot.Query.Priority);
                    return true;

                case SearchAdvanceStatus.NeedsLocalSearch:
                    slot.LocalSearchPending = true;
                    return TryScheduleLocalSearch(handle, slot, ref scheduledJobs);

                case SearchAdvanceStatus.Completed:
                    slot.Search.Dispose();
                    slot.Search = null;
                    if (!IsCurrent(result))
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Stale, PathFailureReason.None, null);
                    }
                    else
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Completed, PathFailureReason.None, result);
                    }

                    return true;

                case SearchAdvanceStatus.Failed:
                    bool stale = slot.Search.CapturedStateChanged();
                    slot.Search.Dispose();
                    slot.Search = null;
                    ScheduleTerminal(
                        slot,
                        stale ? PathRequestStatus.Stale : PathRequestStatus.Failed,
                        stale ? PathFailureReason.None : failure,
                        null);
                    return true;

                default:
                    throw new InvalidOperationException("The navigation search returned an invalid status.");
            }
        }

        private bool TryScheduleLocalSearch(
            PathRequestHandle handle,
            RequestSlot slot,
            ref bool scheduledJobs)
        {
            int scheduledLane = -1;
            try
            {
                if (!_data.PolygonSearch.TrySchedule(
                        slot.Search.PendingLocalSearch,
                        out int laneIndex))
                {
                    EnqueueActive(handle, slot.Query.Priority);
                    return false;
                }

                scheduledLane = laneIndex;
                long workId = NextPositive(ref _nextPhysicalWorkId);
                var physical = new InFlightSearch
                {
                    Id = workId,
                    AdmissionSequence = NextPositive(ref _nextAdmissionSequence),
                    LaneIndex = laneIndex,
                    OriginHandle = handle,
                    Search = slot.Search
                };
                if (_inFlight[laneIndex] != null)
                {
                    throw new InvalidOperationException("A Burst scratch lane was admitted twice.");
                }

                _inFlight[laneIndex] = physical;
                scheduledLane = -1;
                _inFlightSearchCount++;
                slot.Search = null;
                slot.LocalSearchPending = false;
                slot.PhysicalWorkId = workId;
                slot.Status = PathRequestStatus.RunningLocal;
                scheduledJobs = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (scheduledLane >= 0)
                {
                    try
                    {
                        _data.PolygonSearch.Complete(scheduledLane, out _);
                    }
                    catch (Exception completionException)
                    {
                        Debug.LogException(completionException);
                    }
                    finally
                    {
                        _data.PolygonSearch.Release(scheduledLane);
                    }
                }

                slot.Search?.Dispose();
                slot.Search = null;
                slot.LocalSearchPending = false;
                slot.PhysicalWorkId = 0L;
                ScheduleTerminal(
                    slot,
                    PathRequestStatus.Failed,
                    PathFailureReason.BackendUnavailable,
                    null);
                return true;
            }
        }

        private bool TryHarvestCompletedSearch()
        {
            InFlightSearch next = null;
            for (int index = 0; index < _inFlight.Length; index++)
            {
                InFlightSearch candidate = _inFlight[index];
                if (candidate == null || !_data.PolygonSearch.IsCompleted(candidate.LaneIndex))
                {
                    continue;
                }

                if (next == null || candidate.AdmissionSequence < next.AdmissionSequence)
                {
                    next = candidate;
                }
            }

            if (next == null)
            {
                return false;
            }

            HarvestCompletedSearch(next);
            return true;
        }

        private void HarvestCompletedSearch(InFlightSearch physical)
        {
            Exception error = null;
            try
            {
                bool found = _data.PolygonSearch.Complete(physical.LaneIndex, out double totalCost);
                physical.Search.CompleteLocal(_data.PolygonSearch, physical.LaneIndex, found, totalCost);
            }
            catch (Exception exception)
            {
                error = exception;
                Debug.LogException(exception);
            }
            finally
            {
                _data.PolygonSearch.Release(physical.LaneIndex);
                _inFlight[physical.LaneIndex] = null;
                _inFlightSearchCount--;
            }

            if (TryGetSlot(physical.OriginHandle, out RequestSlot slot) &&
                slot.Status == PathRequestStatus.RunningLocal &&
                slot.PhysicalWorkId == physical.Id)
            {
                slot.PhysicalWorkId = 0L;
                if (error == null)
                {
                    slot.Search = physical.Search;
                    physical.Search = null;
                    EnqueueActive(physical.OriginHandle, slot.Query.Priority);
                }
                else
                {
                    ScheduleTerminal(
                        slot,
                        PathRequestStatus.Failed,
                        PathFailureReason.BackendUnavailable,
                        null);
                }
            }

            physical.Search?.Dispose();
            physical.Search = null;
        }

        private void EnqueueActive(PathRequestHandle handle, PathPriority priority)
        {
            _queues[(int)priority].Enqueue(handle);
        }

        private void ScheduleTerminal(
            RequestSlot slot,
            PathRequestStatus status,
            PathFailureReason failure,
            PathResultData result)
        {
            slot.PendingStatus = status;
            slot.PendingFailure = failure;
            slot.PendingResult = result;
            slot.PublishTick = _tick + 1L;
            slot.CallbackPending = true;
        }

        private void PublishReadyRequests()
        {
            List<(Action<PathRequestHandle> Callback, PathRequestHandle Handle)> callbacks = null;
            for (int index = 0; index < _slots.Count; index++)
            {
                RequestSlot slot = _slots[index];
                if (!slot.CallbackPending || slot.PublishTick > _tick)
                {
                    continue;
                }

                if (slot.PendingStatus == PathRequestStatus.Completed &&
                    !IsCurrent(slot.PendingResult))
                {
                    slot.PendingStatus = PathRequestStatus.Stale;
                    slot.PendingFailure = PathFailureReason.None;
                    slot.PendingResult = null;
                }

                slot.CallbackPending = false;
                slot.Status = slot.PendingStatus;
                slot.Failure = slot.PendingFailure;
                slot.Result = slot.PendingResult;
                slot.PendingResult = null;
                if (slot.Callback != null)
                {
                    callbacks ??= new List<(Action<PathRequestHandle>, PathRequestHandle)>();
                    callbacks.Add((slot.Callback, new PathRequestHandle(index, slot.Generation)));
                    slot.Callback = null;
                }
            }

            if (callbacks == null)
            {
                return;
            }

            for (int index = 0; index < callbacks.Count; index++)
            {
                try
                {
                    callbacks[index].Callback(callbacks[index].Handle);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void ApplyPendingMutations()
        {
            if (_mutations.Count == 0)
            {
                return;
            }

            var polygons = (bool[])_data.PolygonEnabled.Clone();
            var adjacencies = (bool[])_data.AdjacencyEnabled.Clone();
            var portals = (bool[])_data.PortalEnabled.Clone();
            var affectedAreas = new bool[_data.Areas.Length];
            bool changed = false;
            bool topologyChanged = false;
            while (_mutations.Count > 0)
            {
                PendingMutation mutation = _mutations.Dequeue();
                switch (mutation.Kind)
                {
                    case MutationKind.Polygon:
                        if (polygons[mutation.First] != mutation.Enabled)
                        {
                            polygons[mutation.First] = mutation.Enabled;
                            affectedAreas[_data.Polygons[mutation.First].AreaIndex] = true;
                            changed = true;
                        }

                        break;

                    case MutationKind.Adjacency:
                        bool adjacencyChanged = SetAdjacencyPair(
                            adjacencies,
                            mutation.First,
                            mutation.Second,
                            mutation.Enabled);
                        adjacencyChanged |= SetAdjacencyPair(
                            adjacencies,
                            mutation.Second,
                            mutation.First,
                            mutation.Enabled);
                        if (adjacencyChanged)
                        {
                            affectedAreas[_data.Polygons[mutation.First].AreaIndex] = true;
                            changed = true;
                        }

                        break;

                    case MutationKind.Portal:
                        if (portals[mutation.First] != mutation.Enabled)
                        {
                            portals[mutation.First] = mutation.Enabled;
                            CompiledPortalRecord portal = _data.Portals[mutation.First];
                            affectedAreas[portal.SourceArea] = true;
                            affectedAreas[portal.DestinationArea] = true;
                            topologyChanged = true;
                            changed = true;
                        }

                        break;

                    case MutationKind.AreaDirty:
                        affectedAreas[mutation.First] = true;
                        changed = true;
                        break;
                }
            }

            if (changed)
            {
                _data.ApplySnapshot(
                    polygons,
                    adjacencies,
                    portals,
                    affectedAreas,
                    topologyChanged);
            }
        }

        private bool SetAdjacencyPair(bool[] adjacencies, int from, int to, bool enabled)
        {
            if (!_data.AdjacencyByPolygonPair.TryGetValue(
                    new RuntimeEdgePair(from, to),
                    out List<int> indices))
            {
                return false;
            }

            bool changed = false;
            for (int index = 0; index < indices.Count; index++)
            {
                int adjacency = indices[index];
                if (adjacencies[adjacency] != enabled)
                {
                    adjacencies[adjacency] = enabled;
                    changed = true;
                }
            }

            return changed;
        }

        private bool IsCurrent(PathResultData result)
        {
            if (result == null || result.TopologyRevision != _data.TopologyRevision)
            {
                return false;
            }

            for (int index = 0; index < result.Revisions.Length; index++)
            {
                NavigationRevisionStamp stamp = result.Revisions[index];
                if (!_data.AreaById.TryGetValue(stamp.AreaId, out int areaIndex) ||
                    _data.AreaRevisions[areaIndex] != stamp.Revision)
                {
                    return false;
                }
            }

            return true;
        }

        private PathResultData GetResult(PathRequestHandle handle)
        {
            if (!IsPathViewValid(handle))
            {
                throw new InvalidOperationException("The navigation path view is no longer valid.");
            }

            return _slots[handle.Slot].Result;
        }

        private static bool IsTerminal(PathRequestStatus status)
        {
            return status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
                   status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale;
        }

        private static long NextPositive(ref long value)
        {
            unchecked
            {
                value++;
                if (value <= 0L)
                {
                    value = 1L;
                }

                return value;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NavigationWorld));
            }
        }
    }
}
