using System;
using System.Collections.Generic;
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
            internal ulong[] CapturedAreaRevisions;
            internal ulong CapturedTopologyRevision;
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
        private int _serviceCursor;
        private long _tick;
        private bool _disposed;

        public NavigationWorld(NavigationBakeAsset bake, int cacheCapacityPerArea = 256)
        {
            _data = new NavigationRuntimeData(bake, cacheCapacityPerArea);
        }

        public bool IsDisposed => _disposed;
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
            slot.CapturedAreaRevisions = null;
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
            for (int work = 0; work < workBudget; work++)
            {
                if (!TryDequeueWork(out PathRequestHandle handle))
                {
                    break;
                }

                AdvanceRequest(handle);
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
            for (int index = 0; index < _slots.Count; index++)
            {
                RequestSlot slot = _slots[index];
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
            slot.CapturedAreaRevisions = null;
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
                         slot.Status == PathRequestStatus.RunningGlobal ||
                         slot.Status == PathRequestStatus.RunningLocal))
                    {
                        handle = candidate;
                        return true;
                    }
                }
            }

            handle = default;
            return false;
        }

        private void AdvanceRequest(PathRequestHandle handle)
        {
            RequestSlot slot = _slots[handle.Slot];
            switch (slot.Status)
            {
                case PathRequestStatus.Queued:
                    slot.CapturedAreaRevisions = (ulong[])_data.AreaRevisions.Clone();
                    slot.CapturedTopologyRevision = _data.TopologyRevision;
                    slot.Status = PathRequestStatus.RunningGlobal;
                    EnqueueActive(handle, slot.Query.Priority);
                    break;

                case PathRequestStatus.RunningGlobal:
                    slot.Status = PathRequestStatus.RunningLocal;
                    EnqueueActive(handle, slot.Query.Priority);
                    break;

                case PathRequestStatus.RunningLocal:
                    bool solved = NavigationSearch.TrySolve(
                        _data,
                        slot.Query,
                        slot.CapturedAreaRevisions,
                        slot.CapturedTopologyRevision,
                        out PathResultData result,
                        out PathFailureReason failure);
                    if (solved && !IsCurrent(result))
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Stale, PathFailureReason.None, null);
                    }
                    else if (solved)
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Completed, PathFailureReason.None, result);
                    }
                    else if (CapturedStateChanged(slot))
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Stale, PathFailureReason.None, null);
                    }
                    else
                    {
                        ScheduleTerminal(slot, PathRequestStatus.Failed, failure, null);
                    }

                    break;
            }
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
            var callbacks = new List<(Action<PathRequestHandle> Callback, PathRequestHandle Handle)>();
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
                    callbacks.Add((slot.Callback, new PathRequestHandle(index, slot.Generation)));
                    slot.Callback = null;
                }
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
            while (_mutations.Count > 0)
            {
                PendingMutation mutation = _mutations.Dequeue();
                switch (mutation.Kind)
                {
                    case MutationKind.Polygon:
                        if (_data.PolygonEnabled[mutation.First] != mutation.Enabled)
                        {
                            _data.SetPolygonEnabled(mutation.First, mutation.Enabled);
                            _data.AdvanceAreaRevision(_data.Polygons[mutation.First].AreaIndex);
                        }

                        break;

                    case MutationKind.Adjacency:
                        bool changed = SetAdjacencyPair(mutation.First, mutation.Second, mutation.Enabled);
                        changed |= SetAdjacencyPair(mutation.Second, mutation.First, mutation.Enabled);
                        if (changed)
                        {
                            _data.AdvanceAreaRevision(_data.Polygons[mutation.First].AreaIndex);
                        }

                        break;

                    case MutationKind.Portal:
                        if (_data.PortalEnabled[mutation.First] != mutation.Enabled)
                        {
                            _data.PortalEnabled[mutation.First] = mutation.Enabled;
                            CompiledPortalRecord portal = _data.Portals[mutation.First];
                            _data.AdvanceAreaRevision(portal.SourceArea);
                            _data.AdvanceAreaRevision(portal.DestinationArea);
                            _data.AdvanceTopologyRevision();
                        }

                        break;

                    case MutationKind.AreaDirty:
                        _data.AdvanceAreaRevision(mutation.First);
                        break;
                }
            }
        }

        private bool SetAdjacencyPair(int from, int to, bool enabled)
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
                if (_data.AdjacencyEnabled[adjacency] != enabled)
                {
                    _data.SetAdjacencyEnabled(adjacency, enabled);
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

        private bool CapturedStateChanged(RequestSlot slot)
        {
            if (slot.CapturedAreaRevisions == null ||
                slot.CapturedTopologyRevision != _data.TopologyRevision ||
                slot.CapturedAreaRevisions.Length != _data.AreaRevisions.Length)
            {
                return true;
            }

            for (int index = 0; index < slot.CapturedAreaRevisions.Length; index++)
            {
                if (slot.CapturedAreaRevisions[index] != _data.AreaRevisions[index])
                {
                    return true;
                }
            }

            return false;
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NavigationWorld));
            }
        }
    }
}
