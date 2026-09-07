using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public struct PortalEntrySpan
    {
        [SerializeField] private AreaId _areaId;
        [SerializeField] private PolygonId _polygonId;
        [SerializeField] private EdgeId _edgeId;
        [SerializeField] private Vector3 _start;
        [SerializeField] private Vector3 _end;

        public PortalEntrySpan(
            AreaId areaId,
            PolygonId polygonId,
            EdgeId edgeId,
            Vector3 start,
            Vector3 end)
        {
            _areaId = areaId;
            _polygonId = polygonId;
            _edgeId = edgeId;
            _start = start;
            _end = end;
        }

        public AreaId AreaId => _areaId;
        public PolygonId PolygonId => _polygonId;
        public EdgeId EdgeId => _edgeId;
        public Vector3 Start => _start;
        public Vector3 End => _end;
        public Vector3 Midpoint => (_start + _end) * 0.5f;
    }

    public enum PortalDirection : byte
    {
        SourceToDestination,
        Bidirectional
    }

    [Serializable]
    public sealed class NavigationPortalRecord
    {
        [SerializeField] private PortalId _id;
        [SerializeField] private PortalEntrySpan _source;
        [SerializeField] private PortalEntrySpan _destination;
        [SerializeField] private PortalDirection _direction;
        [SerializeField] private bool _enabled = true;
        [SerializeField] private SemanticMask _semantics = new SemanticMask();
        [SerializeField] private SemanticMask _requiredCapabilities = new SemanticMask();
        [SerializeField] private double _baseCost;
        [SerializeField] private PortalTransform _sourceToDestination;

        internal NavigationPortalRecord(
            PortalEntrySpan source,
            PortalEntrySpan destination,
            PortalDirection direction,
            double baseCost,
            PortalTransform sourceToDestination)
        {
            if (!IsFiniteNonnegative(baseCost))
            {
                throw new ArgumentOutOfRangeException(nameof(baseCost));
            }

            _id = PortalId.New();
            _source = source;
            _destination = destination;
            _direction = direction;
            _baseCost = baseCost;
            _sourceToDestination = sourceToDestination;
        }

        public PortalId Id => _id;
        public PortalEntrySpan Source => _source;
        public PortalEntrySpan Destination => _destination;
        public PortalDirection Direction => _direction;
        public bool Enabled => _enabled;
        public SemanticMask Semantics => (_semantics ?? new SemanticMask()).Clone();
        public SemanticMask RequiredCapabilities => (_requiredCapabilities ?? new SemanticMask()).Clone();
        public double BaseCost => _baseCost;
        public PortalTransform SourceToDestination => _sourceToDestination;

        internal SemanticMask RawSemantics => _semantics ?? (_semantics = new SemanticMask());
        internal SemanticMask RawRequiredCapabilities =>
            _requiredCapabilities ?? (_requiredCapabilities = new SemanticMask());

        internal void SetEnabled(bool enabled) => _enabled = enabled;
        internal void SetSemantics(SemanticMask value) => _semantics = value?.Clone() ?? new SemanticMask();
        internal void SetRequiredCapabilities(SemanticMask value) =>
            _requiredCapabilities = value?.Clone() ?? new SemanticMask();
        internal void SetBaseCost(double value)
        {
            if (!IsFiniteNonnegative(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _baseCost = value;
        }

        internal void SetSpans(PortalEntrySpan source, PortalEntrySpan destination)
        {
            _source = source;
            _destination = destination;
        }

        internal void SetTransform(PortalTransform value) => _sourceToDestination = value;

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            RawSemantics.RemapSlots(oldToNew, newCapacity);
            RawRequiredCapabilities.RemapSlots(oldToNew, newCapacity);
        }

        private static bool IsFiniteNonnegative(double value)
        {
            return value >= 0d && !double.IsInfinity(value);
        }
    }

    [CreateAssetMenu(fileName = "Areafinder World", menuName = "Areafinder/Navigation World")]
    public sealed class NavigationWorldAsset : ScriptableObject
    {
        [SerializeField] private SemanticRegistryAsset _semanticRegistry;
        [SerializeField] private AdjacencyInferenceSettings _inferenceSettings;
        [SerializeField] private List<NavigationAreaAsset> _areas = new List<NavigationAreaAsset>();
        [SerializeField] private List<NavigationPortalRecord> _portals = new List<NavigationPortalRecord>();
        [SerializeField] private List<TraversalPolicyAsset> _policies = new List<TraversalPolicyAsset>();
        [SerializeField] private ulong _authoringRevision = 1UL;

        public SemanticRegistryAsset SemanticRegistry => _semanticRegistry;
        public AdjacencyInferenceSettings InferenceSettings =>
            _inferenceSettings.IsValid ? _inferenceSettings : AdjacencyInferenceSettings.Default;
        internal AdjacencyInferenceSettings RawInferenceSettings => _inferenceSettings;
        public IReadOnlyList<NavigationAreaAsset> Areas => _areas;
        public IReadOnlyList<NavigationPortalRecord> Portals => _portals;
        public IReadOnlyList<TraversalPolicyAsset> Policies => _policies;
        public ulong AuthoringRevision => _authoringRevision;

        internal void SetSemanticRegistry(SemanticRegistryAsset value)
        {
            _semanticRegistry = value;
            Touch();
        }

        internal void SetInferenceSettings(AdjacencyInferenceSettings value)
        {
            if (!value.IsValid)
            {
                throw new ArgumentException("Adjacency inference settings must be finite and nonnegative.", nameof(value));
            }

            _inferenceSettings = value;
            Touch();
        }

        internal void AddArea(NavigationAreaAsset area)
        {
            if (area == null)
            {
                throw new ArgumentNullException(nameof(area));
            }

            if (!_areas.Contains(area))
            {
                _areas.Add(area);
                Touch();
            }
        }

        internal bool RemoveArea(NavigationAreaAsset area)
        {
            if (!_areas.Remove(area))
            {
                return false;
            }

            Touch();
            return true;
        }

        internal NavigationPortalRecord AddPortal(
            PortalEntrySpan source,
            PortalEntrySpan destination,
            PortalDirection direction,
            double baseCost,
            PortalTransform sourceToDestination)
        {
            var portal = new NavigationPortalRecord(
                source,
                destination,
                direction,
                baseCost,
                sourceToDestination);
            _portals.Add(portal);
            Touch();
            return portal;
        }

        internal bool RemovePortal(PortalId id)
        {
            int removed = _portals.RemoveAll(portal => portal != null && portal.Id == id);
            if (removed == 0)
            {
                return false;
            }

            Touch();
            return true;
        }

        internal void AddPolicy(TraversalPolicyAsset policy)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (!_policies.Contains(policy))
            {
                _policies.Add(policy);
                Touch();
            }
        }

        internal bool RemovePolicy(TraversalPolicyAsset policy)
        {
            if (!_policies.Remove(policy))
            {
                return false;
            }

            Touch();
            return true;
        }

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            for (int index = 0; index < _areas.Count; index++)
            {
                _areas[index]?.RemapSemanticSlots(oldToNew, newCapacity);
            }

            for (int index = 0; index < _portals.Count; index++)
            {
                _portals[index]?.RemapSemanticSlots(oldToNew, newCapacity);
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

        private void OnEnable()
        {
            if (!_inferenceSettings.IsValid)
            {
                _inferenceSettings = AdjacencyInferenceSettings.Default;
            }

            _areas = _areas ?? new List<NavigationAreaAsset>();
            _portals = _portals ?? new List<NavigationPortalRecord>();
            _policies = _policies ?? new List<TraversalPolicyAsset>();
        }
    }
}
