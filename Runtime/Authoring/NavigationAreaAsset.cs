using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public struct AdjacencyInferenceSettings
    {
        [SerializeField] private bool _initialized;
        [SerializeField] private float _positionTolerance;
        [SerializeField] private float _minimumOverlap;
        [SerializeField] private float _heightTolerance;
        [SerializeField] private float _normalAngleTolerance;
        [SerializeField] private float _planarityTolerance;
        [SerializeField] private float _maximumForcedGap;

        public AdjacencyInferenceSettings(
            float positionTolerance,
            float minimumOverlap,
            float heightTolerance,
            float normalAngleTolerance,
            float planarityTolerance,
            float maximumForcedGap)
        {
            _initialized = true;
            _positionTolerance = positionTolerance;
            _minimumOverlap = minimumOverlap;
            _heightTolerance = heightTolerance;
            _normalAngleTolerance = normalAngleTolerance;
            _planarityTolerance = planarityTolerance;
            _maximumForcedGap = maximumForcedGap;
        }

        public float PositionTolerance => _positionTolerance;
        public float MinimumOverlap => _minimumOverlap;
        public float HeightTolerance => _heightTolerance;
        public float NormalAngleTolerance => _normalAngleTolerance;
        public float PlanarityTolerance => _planarityTolerance;
        public float MaximumForcedGap => _maximumForcedGap;

        public bool IsValid =>
            _initialized &&
            IsFiniteNonnegative(_positionTolerance) &&
            IsFiniteNonnegative(_minimumOverlap) &&
            IsFiniteNonnegative(_heightTolerance) &&
            IsFiniteNonnegative(_normalAngleTolerance) && _normalAngleTolerance <= 180f &&
            IsFiniteNonnegative(_planarityTolerance) &&
            IsFiniteNonnegative(_maximumForcedGap);

        public static AdjacencyInferenceSettings Default => new AdjacencyInferenceSettings(
            0.01f,
            0.01f,
            0.05f,
            15f,
            0.005f,
            0.5f);

        private static bool IsFiniteNonnegative(float value)
        {
            return value >= 0f && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class NavigationVertexRecord
    {
        [SerializeField] private VertexId _id;
        [SerializeField] private EdgeId _outgoingEdgeId;
        [SerializeField] private Vector3 _position;

        internal NavigationVertexRecord(Vector3 position)
        {
            _id = VertexId.New();
            _outgoingEdgeId = EdgeId.New();
            _position = position;
        }

        public VertexId Id => _id;
        public EdgeId OutgoingEdgeId => _outgoingEdgeId;
        public Vector3 Position => _position;

        internal void SetPosition(Vector3 position)
        {
            _position = position;
        }

        internal void ReplaceOutgoingEdgeIdentity()
        {
            _outgoingEdgeId = EdgeId.New();
        }
    }

    [Serializable]
    public sealed class NavigationPolygonRecord
    {
        [SerializeField] private PolygonId _id;
        [SerializeField] private List<NavigationVertexRecord> _vertices = new List<NavigationVertexRecord>();
        [SerializeField] private SemanticMask _semantics = new SemanticMask();
        [SerializeField] private SemanticMask _requiredCapabilities = new SemanticMask();
        [SerializeField] private bool _enabled = true;

        internal NavigationPolygonRecord(IEnumerable<Vector3> vertices)
        {
            _id = PolygonId.New();
            if (vertices != null)
            {
                foreach (Vector3 vertex in vertices)
                {
                    _vertices.Add(new NavigationVertexRecord(vertex));
                }
            }
        }

        public PolygonId Id => _id;
        public IReadOnlyList<NavigationVertexRecord> Vertices => _vertices;
        public SemanticMask Semantics => (_semantics ?? new SemanticMask()).Clone();
        public SemanticMask RequiredCapabilities => (_requiredCapabilities ?? new SemanticMask()).Clone();
        public bool Enabled => _enabled;

        internal SemanticMask RawSemantics => _semantics ?? (_semantics = new SemanticMask());
        internal SemanticMask RawRequiredCapabilities =>
            _requiredCapabilities ?? (_requiredCapabilities = new SemanticMask());

        internal void SetEnabled(bool enabled) => _enabled = enabled;
        internal void SetSemantics(SemanticMask semantics) => _semantics = semantics?.Clone() ?? new SemanticMask();
        internal void SetRequiredCapabilities(SemanticMask capabilities) =>
            _requiredCapabilities = capabilities?.Clone() ?? new SemanticMask();

        internal void MoveVertex(int index, Vector3 position)
        {
            _vertices[index].SetPosition(position);
        }

        internal void InsertVertex(int edgeStartIndex, Vector3 position)
        {
            if ((uint)edgeStartIndex >= (uint)_vertices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeStartIndex));
            }

            _vertices[edgeStartIndex].ReplaceOutgoingEdgeIdentity();
            _vertices.Insert(edgeStartIndex + 1, new NavigationVertexRecord(position));
        }

        internal void RemoveVertex(int index)
        {
            if ((uint)index >= (uint)_vertices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int previous = (index + _vertices.Count - 1) % _vertices.Count;
            _vertices.RemoveAt(index);
            if (_vertices.Count > 0)
            {
                int survivingPrevious = previous >= _vertices.Count ? _vertices.Count - 1 : previous;
                _vertices[survivingPrevious].ReplaceOutgoingEdgeIdentity();
            }
        }

        internal void ReplaceGeometry(IEnumerable<Vector3> vertices)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            var replacement = new List<NavigationVertexRecord>();
            foreach (Vector3 position in vertices)
            {
                replacement.Add(new NavigationVertexRecord(position));
            }

            _vertices = replacement;
        }

        internal void CopyAttributesFrom(NavigationPolygonRecord source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _semantics = source.RawSemantics.Clone();
            _requiredCapabilities = source.RawRequiredCapabilities.Clone();
            _enabled = source.Enabled;
        }

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            RawSemantics.RemapSlots(oldToNew, newCapacity);
            RawRequiredCapabilities.RemapSlots(oldToNew, newCapacity);
        }
    }

    [Serializable]
    public struct PolygonEdgeReference : IEquatable<PolygonEdgeReference>
    {
        [SerializeField] private PolygonId _polygonId;
        [SerializeField] private EdgeId _edgeId;

        public PolygonEdgeReference(PolygonId polygonId, EdgeId edgeId)
        {
            _polygonId = polygonId;
            _edgeId = edgeId;
        }

        public PolygonId PolygonId => _polygonId;
        public EdgeId EdgeId => _edgeId;

        public bool Equals(PolygonEdgeReference other)
        {
            return _polygonId == other._polygonId && _edgeId == other._edgeId;
        }

        public override bool Equals(object obj) => obj is PolygonEdgeReference other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return (_polygonId.GetHashCode() * 397) ^ _edgeId.GetHashCode();
            }
        }
    }

    public enum AdjacencyOverrideState : byte
    {
        Automatic,
        ForcedConnected,
        ForcedDisconnected
    }

    [Serializable]
    public sealed class AdjacencyOverrideRecord
    {
        [SerializeField] private PolygonEdgeReference _first;
        [SerializeField] private PolygonEdgeReference _second;
        [SerializeField] private AdjacencyOverrideState _state;

        internal AdjacencyOverrideRecord(
            PolygonEdgeReference first,
            PolygonEdgeReference second,
            AdjacencyOverrideState state)
        {
            _first = first;
            _second = second;
            _state = state;
        }

        public PolygonEdgeReference First => _first;
        public PolygonEdgeReference Second => _second;
        public AdjacencyOverrideState State => _state;
    }

    [CreateAssetMenu(fileName = "Areafinder Area", menuName = "Areafinder/Navigation Area")]
    public sealed class NavigationAreaAsset : ScriptableObject
    {
        [SerializeField] private AreaId _id;
        [SerializeField] private AreaFrame _frame;
        [SerializeField] private SemanticMask _semantics = new SemanticMask();
        [SerializeField] private SemanticMask _requiredCapabilities = new SemanticMask();
        [SerializeField] private List<NavigationPolygonRecord> _polygons = new List<NavigationPolygonRecord>();
        [SerializeField] private List<AdjacencyOverrideRecord> _adjacencyOverrides = new List<AdjacencyOverrideRecord>();
        [SerializeField] private ulong _authoringRevision = 1UL;

        public AreaId Id => _id;
        public AreaFrame Frame => _frame;
        public SemanticMask Semantics => (_semantics ?? new SemanticMask()).Clone();
        public SemanticMask RequiredCapabilities => (_requiredCapabilities ?? new SemanticMask()).Clone();
        public IReadOnlyList<NavigationPolygonRecord> Polygons => _polygons;
        public IReadOnlyList<AdjacencyOverrideRecord> AdjacencyOverrides => _adjacencyOverrides;
        public ulong AuthoringRevision => _authoringRevision;

        internal SemanticMask RawSemantics => _semantics ?? (_semantics = new SemanticMask());
        internal SemanticMask RawRequiredCapabilities =>
            _requiredCapabilities ?? (_requiredCapabilities = new SemanticMask());

        internal void SetFrame(AreaFrame frame)
        {
            _frame = frame;
            Touch();
        }

        internal void SetSemantics(SemanticMask semantics)
        {
            _semantics = semantics?.Clone() ?? new SemanticMask();
            Touch();
        }

        internal void SetRequiredCapabilities(SemanticMask capabilities)
        {
            _requiredCapabilities = capabilities?.Clone() ?? new SemanticMask();
            Touch();
        }

        internal NavigationPolygonRecord AddPolygon(IEnumerable<Vector3> vertices)
        {
            var polygon = new NavigationPolygonRecord(vertices);
            _polygons.Add(polygon);
            Touch();
            return polygon;
        }

        internal bool RemovePolygon(PolygonId id)
        {
            int removed = _polygons.RemoveAll(polygon => polygon != null && polygon.Id == id);
            if (removed == 0)
            {
                return false;
            }

            Touch();
            return true;
        }

        internal bool SplitPolygon(
            PolygonId id,
            int firstVertex,
            int secondVertex,
            out NavigationPolygonRecord preserved,
            out NavigationPolygonRecord created)
        {
            preserved = null;
            created = null;
            if (!TryGetPolygon(id, out NavigationPolygonRecord polygon))
            {
                return false;
            }

            int count = polygon.Vertices.Count;
            if ((uint)firstVertex >= (uint)count || (uint)secondVertex >= (uint)count ||
                firstVertex == secondVertex ||
                (firstVertex + 1) % count == secondVertex ||
                (secondVertex + 1) % count == firstVertex)
            {
                return false;
            }

            List<Vector3> firstGeometry = WalkVertices(polygon, firstVertex, secondVertex);
            List<Vector3> secondGeometry = WalkVertices(polygon, secondVertex, firstVertex);
            polygon.ReplaceGeometry(firstGeometry);
            var secondPolygon = new NavigationPolygonRecord(secondGeometry);
            secondPolygon.CopyAttributesFrom(polygon);
            _polygons.Add(secondPolygon);
            preserved = polygon;
            created = secondPolygon;
            Touch();
            return true;
        }

        internal bool MergePolygons(
            PolygonId preservedId,
            PolygonId removedId,
            float positionTolerance,
            out NavigationPolygonRecord merged)
        {
            merged = null;
            if (positionTolerance < 0f || float.IsNaN(positionTolerance) || float.IsInfinity(positionTolerance) ||
                preservedId == removedId ||
                !TryGetPolygon(preservedId, out NavigationPolygonRecord first) ||
                !TryGetPolygon(removedId, out NavigationPolygonRecord second) ||
                first.Enabled != second.Enabled ||
                !first.RawSemantics.Equals(second.RawSemantics) ||
                !first.RawRequiredCapabilities.Equals(second.RawRequiredCapabilities))
            {
                return false;
            }

            float squaredTolerance = positionTolerance * positionTolerance;
            for (int firstEdge = 0; firstEdge < first.Vertices.Count; firstEdge++)
            {
                Vector3 firstStart = first.Vertices[firstEdge].Position;
                Vector3 firstEnd = first.Vertices[(firstEdge + 1) % first.Vertices.Count].Position;
                for (int secondEdge = 0; secondEdge < second.Vertices.Count; secondEdge++)
                {
                    Vector3 secondStart = second.Vertices[secondEdge].Position;
                    Vector3 secondEnd = second.Vertices[(secondEdge + 1) % second.Vertices.Count].Position;
                    if ((firstStart - secondEnd).sqrMagnitude > squaredTolerance ||
                        (firstEnd - secondStart).sqrMagnitude > squaredTolerance)
                    {
                        continue;
                    }

                    List<Vector3> outline = BuildMergedOutline(first, firstEdge, second, secondEdge);
                    if (!IsConvexAreaUp(outline, positionTolerance))
                    {
                        return false;
                    }

                    first.ReplaceGeometry(outline);
                    _polygons.Remove(second);
                    merged = first;
                    Touch();
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetPolygon(PolygonId id, out NavigationPolygonRecord polygon)
        {
            for (int index = 0; index < _polygons.Count; index++)
            {
                NavigationPolygonRecord candidate = _polygons[index];
                if (candidate != null && candidate.Id == id)
                {
                    polygon = candidate;
                    return true;
                }
            }

            polygon = null;
            return false;
        }

        internal void AddAdjacencyOverride(AdjacencyOverrideRecord value)
        {
            _adjacencyOverrides.Add(value ?? throw new ArgumentNullException(nameof(value)));
            Touch();
        }

        internal void ClearAdjacencyOverrides()
        {
            if (_adjacencyOverrides.Count == 0)
            {
                return;
            }

            _adjacencyOverrides.Clear();
            Touch();
        }

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            RawSemantics.RemapSlots(oldToNew, newCapacity);
            RawRequiredCapabilities.RemapSlots(oldToNew, newCapacity);
            for (int index = 0; index < _polygons.Count; index++)
            {
                _polygons[index]?.RemapSemanticSlots(oldToNew, newCapacity);
            }

            Touch();
        }

        internal void Touch()
        {
            unchecked
            {
                _authoringRevision++;
                if (_authoringRevision == 0UL)
                {
                    _authoringRevision = 1UL;
                }
            }
        }

        private static List<Vector3> WalkVertices(
            NavigationPolygonRecord polygon,
            int start,
            int inclusiveEnd)
        {
            var result = new List<Vector3>();
            int index = start;
            while (true)
            {
                result.Add(polygon.Vertices[index].Position);
                if (index == inclusiveEnd)
                {
                    return result;
                }

                index = (index + 1) % polygon.Vertices.Count;
            }
        }

        private static List<Vector3> BuildMergedOutline(
            NavigationPolygonRecord first,
            int firstSharedEdge,
            NavigationPolygonRecord second,
            int secondSharedEdge)
        {
            var result = new List<Vector3>(first.Vertices.Count + second.Vertices.Count - 2);
            int index = (firstSharedEdge + 1) % first.Vertices.Count;
            while (true)
            {
                result.Add(first.Vertices[index].Position);
                if (index == firstSharedEdge)
                {
                    break;
                }

                index = (index + 1) % first.Vertices.Count;
            }

            index = (secondSharedEdge + 2) % second.Vertices.Count;
            while (index != secondSharedEdge)
            {
                result.Add(second.Vertices[index].Position);
                index = (index + 1) % second.Vertices.Count;
            }

            return result;
        }

        private static bool IsConvexAreaUp(IReadOnlyList<Vector3> vertices, float tolerance)
        {
            if (vertices.Count < 3)
            {
                return false;
            }

            float minimumTurn = -(tolerance * tolerance);
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 previous = vertices[(index + vertices.Count - 1) % vertices.Count];
                Vector3 current = vertices[index];
                Vector3 next = vertices[(index + 1) % vertices.Count];
                if (Vector3.Cross(current - previous, next - current).y < minimumTurn)
                {
                    return false;
                }
            }

            return true;
        }

        private void OnEnable()
        {
            if (!_id.IsValid)
            {
                _id = AreaId.New();
            }

            if (!_frame.IsValid)
            {
                _frame = AreaFrame.Identity;
            }

            _semantics = _semantics ?? new SemanticMask();
            _requiredCapabilities = _requiredCapabilities ?? new SemanticMask();
            _polygons = _polygons ?? new List<NavigationPolygonRecord>();
            _adjacencyOverrides = _adjacencyOverrides ?? new List<AdjacencyOverrideRecord>();
        }
    }
}
