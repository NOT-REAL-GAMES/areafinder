using UnityEngine;

namespace NotRealGames.Areafinder.Samples.RoomToRoom
{
    [DisallowMultipleComponent]
    public sealed class RoomToRoomPathPreview : MonoBehaviour
    {
        [SerializeField] private NavigationWorldAsset _source;
        [SerializeField] private NavigationBakeAsset _bake;
        [SerializeField] private TraversalPolicyAsset _policy;
        [SerializeField] private NavigationAreaAsset _startArea;
        [SerializeField] private NavigationAreaAsset _goalArea;
        [SerializeField] private Vector3 _start = new Vector3(-1f, 0f, 0f);
        [SerializeField] private Vector3 _goal = new Vector3(1f, 0f, 0f);
        [SerializeField] private string _status = "Enter Play Mode to request a path.";

        private NavigationWorld _world;
        private PathRequestHandle _request;
        private NavigationPath _path;

        public string Status => _status;

        public void Configure(
            NavigationWorldAsset source,
            NavigationBakeAsset bake,
            TraversalPolicyAsset policy,
            NavigationAreaAsset startArea,
            NavigationAreaAsset goalArea)
        {
            _source = source;
            _bake = bake;
            _policy = policy;
            _startArea = startArea;
            _goalArea = goalArea;
        }

        private void Start()
        {
            RequestPath();
        }

        private void Update()
        {
            if (_world == null)
            {
                return;
            }

            _world.Tick(64);
            if (_request.IsValid)
            {
                _status = _world.GetStatus(_request).ToString();
            }
        }

        private void OnDisable()
        {
            _world?.Dispose();
            _world = null;
            _request = default;
            _path = null;
        }

        [ContextMenu("Request Path")]
        public void RequestPath()
        {
            if (!Application.isPlaying)
            {
                _status = "Enter Play Mode to request a path.";
                return;
            }

            if (!TryInitialize(out CompiledTraversalPolicy compiledPolicy))
            {
                return;
            }

            if (_request.IsValid)
            {
                _world.Cancel(_request);
            }

            _path = null;
            var query = new PathQuery(
                new NavigationLocation(_startArea.Id, _start),
                new NavigationLocation(_goalArea.Id, _goal),
                compiledPolicy,
                PathPriority.Normal,
                PathOutputFlags.Default);
            _request = _world.Submit(query, OnPathTerminal);
            _status = PathRequestStatus.Queued.ToString();
        }

        private bool TryInitialize(out CompiledTraversalPolicy compiledPolicy)
        {
            compiledPolicy = null;
            if (_bake == null || !_bake.IsUsable || _policy == null ||
                _startArea == null || _goalArea == null)
            {
                _status = "Assign a current bake, policy, and endpoint Areas.";
                Debug.LogError(_status, this);
                return false;
            }

            if (!CompiledTraversalPolicy.TryCompile(_policy, _bake, out compiledPolicy, out string error))
            {
                _status = error;
                Debug.LogError(error, this);
                return false;
            }

            if (_world == null)
            {
                _world = new NavigationWorld(_bake);
            }

            return true;
        }

        private void OnPathTerminal(PathRequestHandle handle)
        {
            PathRequestStatus status = _world.GetStatus(handle);
            if (status == PathRequestStatus.Completed && _world.TryGetPath(handle, out NavigationPathView view))
            {
                _path = view.ToManagedCopy();
                _status = $"Completed: {_path.Areas.Count} Areas, " +
                          $"{_path.PortalTransitions.Count} Portal, {_path.TotalCost:0.###} cost";
            }
            else
            {
                _world.TryGetFailure(handle, out PathFailureReason failure);
                _status = failure == PathFailureReason.None ? status.ToString() : $"{status}: {failure}";
            }

            _world.Release(handle);
            if (handle == _request)
            {
                _request = default;
            }
        }

        private void OnDrawGizmos()
        {
            DrawAuthoredWorld();
            if (_path == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            for (int segmentIndex = 0; segmentIndex < _path.Areas.Count; segmentIndex++)
            {
                NavigationAreaSegment segment = _path.Areas[segmentIndex];
                Vector3 previous = ToScene(segment.AreaId, segment.Start.LocalPosition);
                for (int steeringIndex = 0; steeringIndex < segment.SteeringCount; steeringIndex++)
                {
                    Vector3 next = ToScene(
                        segment.AreaId,
                        _path.SteeringTargets[segment.SteeringStart + steeringIndex]);
                    Gizmos.DrawLine(previous + Vector3.up * 0.08f, next + Vector3.up * 0.08f);
                    Gizmos.DrawSphere(next + Vector3.up * 0.08f, 0.08f);
                    previous = next;
                }

                Vector3 end = ToScene(segment.AreaId, segment.End.LocalPosition);
                Gizmos.DrawLine(previous + Vector3.up * 0.08f, end + Vector3.up * 0.08f);
            }

            Gizmos.color = Color.magenta;
            for (int index = 0; index < _path.PortalTransitions.Count; index++)
            {
                NavigationPortalTransition portal = _path.PortalTransitions[index];
                Gizmos.DrawLine(
                    ToScene(portal.Entry.AreaId, portal.Entry.LocalPosition) + Vector3.up * 0.1f,
                    ToScene(portal.Exit.AreaId, portal.Exit.LocalPosition) + Vector3.up * 0.1f);
            }
        }

        private void DrawAuthoredWorld()
        {
            if (_source == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int areaIndex = 0; areaIndex < _source.Areas.Count; areaIndex++)
            {
                NavigationAreaAsset area = _source.Areas[areaIndex];
                if (area == null)
                {
                    continue;
                }

                for (int polygonIndex = 0; polygonIndex < area.Polygons.Count; polygonIndex++)
                {
                    NavigationPolygonRecord polygon = area.Polygons[polygonIndex];
                    if (polygon == null || polygon.Vertices.Count < 2)
                    {
                        continue;
                    }

                    for (int vertexIndex = 0; vertexIndex < polygon.Vertices.Count; vertexIndex++)
                    {
                        Vector3 first = ToScene(area.Id, polygon.Vertices[vertexIndex].Position);
                        Vector3 second = ToScene(
                            area.Id,
                            polygon.Vertices[(vertexIndex + 1) % polygon.Vertices.Count].Position);
                        Gizmos.DrawLine(first + Vector3.up * 0.04f, second + Vector3.up * 0.04f);
                    }
                }
            }

            Gizmos.color = Color.magenta;
            for (int index = 0; index < _source.Portals.Count; index++)
            {
                NavigationPortalRecord portal = _source.Portals[index];
                if (portal != null)
                {
                    Gizmos.DrawLine(
                        ToScene(portal.Source.AreaId, portal.Source.Midpoint),
                        ToScene(portal.Destination.AreaId, portal.Destination.Midpoint));
                }
            }
        }

        private Vector3 ToScene(AreaId areaId, Vector3 localPosition)
        {
            if (_source != null)
            {
                for (int index = 0; index < _source.Areas.Count; index++)
                {
                    NavigationAreaAsset area = _source.Areas[index];
                    if (area != null && area.Id == areaId)
                    {
                        Double3 universe = area.Frame.ToUniverse(localPosition);
                        return new Vector3((float)universe.X, (float)universe.Y, (float)universe.Z);
                    }
                }
            }

            return localPosition;
        }
    }
}
