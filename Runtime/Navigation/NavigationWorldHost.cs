using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [DisallowMultipleComponent]
    public sealed class NavigationWorldHost : MonoBehaviour
    {
        private sealed class OwnedRequest
        {
            internal UnityEngine.Object Owner;
            internal PathRequestHandle Handle;
            internal bool Delivered;
        }

        [SerializeField] private NavigationBakeAsset _bake;
        [SerializeField, Min(0)] private int _cacheCapacityPerArea = 256;
        [SerializeField, Min(1)] private int _maxConcurrentSearches = 4;
        [SerializeField, Min(0)] private int _workBudgetPerFrame = 64;

        private readonly List<OwnedRequest> _ownedRequests = new List<OwnedRequest>();
        private NavigationWorld _world;

        public NavigationBakeAsset Bake => _bake;
        public NavigationWorld World => _world;

        public bool Initialize()
        {
            if (_world != null && !_world.IsDisposed)
            {
                return true;
            }

            if (_bake == null || !_bake.IsUsable)
            {
                return false;
            }

            _world = new NavigationWorld(
                _bake,
                _cacheCapacityPerArea,
                _maxConcurrentSearches);
            return true;
        }

        public PathRequestHandle Submit(
            PathQuery query,
            UnityEngine.Object owner,
            Action<PathRequestHandle> onTerminal = null)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (!Initialize())
            {
                throw new InvalidOperationException("The NavigationWorldHost has no usable bake.");
            }

            var request = new OwnedRequest { Owner = owner };
            request.Handle = _world.Submit(query, handle =>
            {
                request.Delivered = true;
                if (request.Owner != null)
                {
                    onTerminal?.Invoke(handle);
                }
                else
                {
                    _world?.Release(handle);
                }
            });
            _ownedRequests.Add(request);
            return request.Handle;
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void Update()
        {
            if (_world == null || _world.IsDisposed)
            {
                return;
            }

            for (int index = 0; index < _ownedRequests.Count; index++)
            {
                OwnedRequest request = _ownedRequests[index];
                if (!request.Delivered && request.Owner == null)
                {
                    _world.Cancel(request.Handle);
                }
            }

            _world.Tick(_workBudgetPerFrame);
            for (int index = _ownedRequests.Count - 1; index >= 0; index--)
            {
                if (_ownedRequests[index].Delivered ||
                    _world.GetStatus(_ownedRequests[index].Handle) == PathRequestStatus.Invalid)
                {
                    _ownedRequests.RemoveAt(index);
                }
            }
        }

        private void OnDisable()
        {
            _world?.Dispose();
            _world = null;
            _ownedRequests.Clear();
        }

        private void OnValidate()
        {
            _cacheCapacityPerArea = Math.Max(0, _cacheCapacityPerArea);
            _maxConcurrentSearches = Math.Max(1, _maxConcurrentSearches);
            _workBudgetPerFrame = Math.Max(0, _workBudgetPerFrame);
        }
    }
}
