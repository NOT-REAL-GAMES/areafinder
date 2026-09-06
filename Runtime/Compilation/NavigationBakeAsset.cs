using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public struct CompiledAreaRecord
    {
        [SerializeField] private AreaId _id;
        [SerializeField] private AreaFrame _frame;
        [SerializeField] private int _polygonStart;
        [SerializeField] private int _polygonCount;
        [SerializeField] private int _semanticOffset;
        [SerializeField] private int _requiredCapabilityOffset;
        [SerializeField] private Bounds _localBounds;

        internal CompiledAreaRecord(
            AreaId id,
            AreaFrame frame,
            int polygonStart,
            int polygonCount,
            int semanticOffset,
            int requiredCapabilityOffset,
            Bounds localBounds)
        {
            _id = id;
            _frame = frame;
            _polygonStart = polygonStart;
            _polygonCount = polygonCount;
            _semanticOffset = semanticOffset;
            _requiredCapabilityOffset = requiredCapabilityOffset;
            _localBounds = localBounds;
        }

        public AreaId Id => _id;
        public AreaFrame Frame => _frame;
        public int PolygonStart => _polygonStart;
        public int PolygonCount => _polygonCount;
        public int SemanticOffset => _semanticOffset;
        public int RequiredCapabilityOffset => _requiredCapabilityOffset;
        public Bounds LocalBounds => _localBounds;
    }

    [Serializable]
    public struct CompiledVertexRecord
    {
        [SerializeField] private VertexId _id;
        [SerializeField] private EdgeId _outgoingEdgeId;
        [SerializeField] private Vector3 _position;

        internal CompiledVertexRecord(VertexId id, EdgeId outgoingEdgeId, Vector3 position)
        {
            _id = id;
            _outgoingEdgeId = outgoingEdgeId;
            _position = position;
        }

        public VertexId Id => _id;
        public EdgeId OutgoingEdgeId => _outgoingEdgeId;
        public Vector3 Position => _position;
    }

    [Serializable]
    public struct CompiledPolygonRecord
    {
        [SerializeField] private PolygonId _id;
        [SerializeField] private int _areaIndex;
        [SerializeField] private int _vertexStart;
        [SerializeField] private int _vertexCount;
        [SerializeField] private int _adjacencyStart;
        [SerializeField] private int _adjacencyCount;
        [SerializeField] private int _semanticOffset;
        [SerializeField] private int _requiredCapabilityOffset;
        [SerializeField] private Vector3 _centroid;
        [SerializeField] private Vector3 _normal;
        [SerializeField] private bool _enabled;

        internal CompiledPolygonRecord(
            PolygonId id,
            int areaIndex,
            int vertexStart,
            int vertexCount,
            int adjacencyStart,
            int adjacencyCount,
            int semanticOffset,
            int requiredCapabilityOffset,
            Vector3 centroid,
            Vector3 normal,
            bool enabled)
        {
            _id = id;
            _areaIndex = areaIndex;
            _vertexStart = vertexStart;
            _vertexCount = vertexCount;
            _adjacencyStart = adjacencyStart;
            _adjacencyCount = adjacencyCount;
            _semanticOffset = semanticOffset;
            _requiredCapabilityOffset = requiredCapabilityOffset;
            _centroid = centroid;
            _normal = normal;
            _enabled = enabled;
        }

        public PolygonId Id => _id;
        public int AreaIndex => _areaIndex;
        public int VertexStart => _vertexStart;
        public int VertexCount => _vertexCount;
        public int AdjacencyStart => _adjacencyStart;
        public int AdjacencyCount => _adjacencyCount;
        public int SemanticOffset => _semanticOffset;
        public int RequiredCapabilityOffset => _requiredCapabilityOffset;
        public Vector3 Centroid => _centroid;
        public Vector3 Normal => _normal;
        public bool Enabled => _enabled;

        internal CompiledPolygonRecord WithAdjacencyRange(int start, int count)
        {
            return new CompiledPolygonRecord(
                _id,
                _areaIndex,
                _vertexStart,
                _vertexCount,
                start,
                count,
                _semanticOffset,
                _requiredCapabilityOffset,
                _centroid,
                _normal,
                _enabled);
        }
    }

    [Serializable]
    public struct CompiledAdjacencyRecord
    {
        [SerializeField] private int _fromPolygon;
        [SerializeField] private int _toPolygon;
        [SerializeField] private EdgeId _fromEdgeId;
        [SerializeField] private EdgeId _toEdgeId;
        [SerializeField] private Vector3 _spanStart;
        [SerializeField] private Vector3 _spanEnd;
        [SerializeField] private AdjacencyOverrideState _source;

        internal CompiledAdjacencyRecord(
            int fromPolygon,
            int toPolygon,
            EdgeId fromEdgeId,
            EdgeId toEdgeId,
            Vector3 spanStart,
            Vector3 spanEnd,
            AdjacencyOverrideState source)
        {
            _fromPolygon = fromPolygon;
            _toPolygon = toPolygon;
            _fromEdgeId = fromEdgeId;
            _toEdgeId = toEdgeId;
            _spanStart = spanStart;
            _spanEnd = spanEnd;
            _source = source;
        }

        public int FromPolygon => _fromPolygon;
        public int ToPolygon => _toPolygon;
        public EdgeId FromEdgeId => _fromEdgeId;
        public EdgeId ToEdgeId => _toEdgeId;
        public Vector3 SpanStart => _spanStart;
        public Vector3 SpanEnd => _spanEnd;
        public AdjacencyOverrideState Source => _source;
    }

    [Serializable]
    public struct CompiledPortalRecord
    {
        [SerializeField] private PortalId _id;
        [SerializeField] private int _sourceArea;
        [SerializeField] private int _sourcePolygon;
        [SerializeField] private int _destinationArea;
        [SerializeField] private int _destinationPolygon;
        [SerializeField] private PortalEntrySpan _sourceSpan;
        [SerializeField] private PortalEntrySpan _destinationSpan;
        [SerializeField] private PortalDirection _direction;
        [SerializeField] private bool _enabled;
        [SerializeField] private int _semanticOffset;
        [SerializeField] private int _requiredCapabilityOffset;
        [SerializeField] private double _baseCost;
        [SerializeField] private PortalTransform _sourceToDestination;

        internal CompiledPortalRecord(
            PortalId id,
            int sourceArea,
            int sourcePolygon,
            int destinationArea,
            int destinationPolygon,
            PortalEntrySpan sourceSpan,
            PortalEntrySpan destinationSpan,
            PortalDirection direction,
            bool enabled,
            int semanticOffset,
            int requiredCapabilityOffset,
            double baseCost,
            PortalTransform sourceToDestination)
        {
            _id = id;
            _sourceArea = sourceArea;
            _sourcePolygon = sourcePolygon;
            _destinationArea = destinationArea;
            _destinationPolygon = destinationPolygon;
            _sourceSpan = sourceSpan;
            _destinationSpan = destinationSpan;
            _direction = direction;
            _enabled = enabled;
            _semanticOffset = semanticOffset;
            _requiredCapabilityOffset = requiredCapabilityOffset;
            _baseCost = baseCost;
            _sourceToDestination = sourceToDestination;
        }

        public PortalId Id => _id;
        public int SourceArea => _sourceArea;
        public int SourcePolygon => _sourcePolygon;
        public int DestinationArea => _destinationArea;
        public int DestinationPolygon => _destinationPolygon;
        public PortalEntrySpan SourceSpan => _sourceSpan;
        public PortalEntrySpan DestinationSpan => _destinationSpan;
        public PortalDirection Direction => _direction;
        public bool Enabled => _enabled;
        public int SemanticOffset => _semanticOffset;
        public int RequiredCapabilityOffset => _requiredCapabilityOffset;
        public double BaseCost => _baseCost;
        public PortalTransform SourceToDestination => _sourceToDestination;
    }

    [CreateAssetMenu(fileName = "Areafinder Bake", menuName = "Areafinder/Navigation Bake")]
    public sealed class NavigationBakeAsset : ScriptableObject
    {
        internal const int CurrentSchemaVersion = 1;

        [SerializeField] private int _schemaVersion;
        [SerializeField] private NavigationWorldAsset _source;
        [SerializeField] private ulong _sourceFingerprint;
        [SerializeField] private ulong _semanticRegistryFingerprint;
        [SerializeField] private int _semanticWordCount;
        [SerializeField] private CompiledAreaRecord[] _areas = Array.Empty<CompiledAreaRecord>();
        [SerializeField] private CompiledPolygonRecord[] _polygons = Array.Empty<CompiledPolygonRecord>();
        [SerializeField] private CompiledVertexRecord[] _vertices = Array.Empty<CompiledVertexRecord>();
        [SerializeField] private CompiledAdjacencyRecord[] _adjacencies = Array.Empty<CompiledAdjacencyRecord>();
        [SerializeField] private CompiledPortalRecord[] _portals = Array.Empty<CompiledPortalRecord>();
        [SerializeField] private ulong[] _semanticWords = Array.Empty<ulong>();

        public int SchemaVersion => _schemaVersion;
        public NavigationWorldAsset Source => _source;
        public ulong SourceFingerprint => _sourceFingerprint;
        public ulong SemanticRegistryFingerprint => _semanticRegistryFingerprint;
        public int SemanticWordCount => _semanticWordCount;
        public IReadOnlyList<CompiledAreaRecord> Areas => _areas;
        public IReadOnlyList<CompiledPolygonRecord> Polygons => _polygons;
        public IReadOnlyList<CompiledVertexRecord> Vertices => _vertices;
        public IReadOnlyList<CompiledAdjacencyRecord> Adjacencies => _adjacencies;
        public IReadOnlyList<CompiledPortalRecord> Portals => _portals;
        public IReadOnlyList<ulong> SemanticWords => _semanticWords;
        public bool IsUsable =>
            _schemaVersion == CurrentSchemaVersion &&
            _source != null &&
            _areas != null && _polygons != null && _vertices != null &&
            _adjacencies != null && _portals != null && _semanticWords != null &&
            !IsStale(_source);

        internal CompiledAreaRecord[] RawAreas => _areas;
        internal CompiledPolygonRecord[] RawPolygons => _polygons;
        internal CompiledVertexRecord[] RawVertices => _vertices;
        internal CompiledAdjacencyRecord[] RawAdjacencies => _adjacencies;
        internal CompiledPortalRecord[] RawPortals => _portals;
        internal ulong[] RawSemanticWords => _semanticWords;

        public bool IsStale(NavigationWorldAsset source)
        {
            return source == null || _sourceFingerprint != NavigationBaker.ComputeSourceFingerprint(source);
        }

        internal void SetData(
            NavigationWorldAsset source,
            ulong sourceFingerprint,
            ulong semanticRegistryFingerprint,
            int semanticWordCount,
            CompiledAreaRecord[] areas,
            CompiledPolygonRecord[] polygons,
            CompiledVertexRecord[] vertices,
            CompiledAdjacencyRecord[] adjacencies,
            CompiledPortalRecord[] portals,
            ulong[] semanticWords)
        {
            _schemaVersion = CurrentSchemaVersion;
            _source = source;
            _sourceFingerprint = sourceFingerprint;
            _semanticRegistryFingerprint = semanticRegistryFingerprint;
            _semanticWordCount = semanticWordCount;
            _areas = areas ?? Array.Empty<CompiledAreaRecord>();
            _polygons = polygons ?? Array.Empty<CompiledPolygonRecord>();
            _vertices = vertices ?? Array.Empty<CompiledVertexRecord>();
            _adjacencies = adjacencies ?? Array.Empty<CompiledAdjacencyRecord>();
            _portals = portals ?? Array.Empty<CompiledPortalRecord>();
            _semanticWords = semanticWords ?? Array.Empty<ulong>();
        }
    }
}
