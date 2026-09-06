using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    public enum NavigationElementKind
    {
        Area,
        Polygon,
        Portal
    }

    [Serializable]
    public sealed class SemanticPredicate
    {
        [SerializeField] private SemanticMask _requiredAll = new SemanticMask();
        [SerializeField] private SemanticMask _requiredAny = new SemanticMask();
        [SerializeField] private SemanticMask _forbiddenAny = new SemanticMask();

        public SemanticPredicate()
        {
        }

        public SemanticPredicate(
            SemanticMask requiredAll,
            SemanticMask requiredAny,
            SemanticMask forbiddenAny)
        {
            _requiredAll = CloneOrEmpty(requiredAll);
            _requiredAny = CloneOrEmpty(requiredAny);
            _forbiddenAny = CloneOrEmpty(forbiddenAny);
        }

        public SemanticMask RequiredAll => CloneOrEmpty(_requiredAll);
        public SemanticMask RequiredAny => CloneOrEmpty(_requiredAny);
        public SemanticMask ForbiddenAny => CloneOrEmpty(_forbiddenAny);

        public bool Allows(SemanticMask semantics)
        {
            SemanticMask available = semantics ?? new SemanticMask();
            return available.ContainsAll(_requiredAll) &&
                   ((_requiredAny?.IsEmpty ?? true) || available.Intersects(_requiredAny)) &&
                   !available.Intersects(_forbiddenAny);
        }

        internal SemanticPredicate Clone() => new SemanticPredicate(_requiredAll, _requiredAny, _forbiddenAny);

        internal bool HasBitsAtOrAbove(int slotCapacity)
        {
            return (_requiredAll?.HasBitsAtOrAbove(slotCapacity) ?? false) ||
                   (_requiredAny?.HasBitsAtOrAbove(slotCapacity) ?? false) ||
                   (_forbiddenAny?.HasBitsAtOrAbove(slotCapacity) ?? false);
        }

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            EnsureMasks();
            _requiredAll.RemapSlots(oldToNew, newCapacity);
            _requiredAny.RemapSlots(oldToNew, newCapacity);
            _forbiddenAny.RemapSlots(oldToNew, newCapacity);
        }

        internal void AddToFingerprint(ref FingerprintBuilder fingerprint)
        {
            EnsureMasks();
            _requiredAll.AddToFingerprint(ref fingerprint);
            _requiredAny.AddToFingerprint(ref fingerprint);
            _forbiddenAny.AddToFingerprint(ref fingerprint);
        }

        private void EnsureMasks()
        {
            _requiredAll = _requiredAll ?? new SemanticMask();
            _requiredAny = _requiredAny ?? new SemanticMask();
            _forbiddenAny = _forbiddenAny ?? new SemanticMask();
        }

        private static SemanticMask CloneOrEmpty(SemanticMask mask) => mask?.Clone() ?? new SemanticMask();
    }

    [Serializable]
    public struct SemanticCostRule
    {
        [SerializeField] private SemanticId _semanticId;
        [SerializeField] private int _slot;
        [SerializeField] private double _distanceMultiplier;
        [SerializeField] private double _entryPenalty;

        public SemanticCostRule(
            SemanticId semanticId,
            int slot,
            double distanceMultiplier,
            double entryPenalty)
        {
            _semanticId = semanticId;
            _slot = slot;
            _distanceMultiplier = distanceMultiplier;
            _entryPenalty = entryPenalty;
        }

        public SemanticId SemanticId => _semanticId;
        public int Slot => _slot;
        public double DistanceMultiplier => _distanceMultiplier;
        public double EntryPenalty => _entryPenalty;

        internal SemanticCostRule WithSlot(int slot)
        {
            return new SemanticCostRule(_semanticId, slot, _distanceMultiplier, _entryPenalty);
        }
    }

    [Serializable]
    public sealed class TraversalPolicyData
    {
        [SerializeField] private SemanticMask _capabilityMask = new SemanticMask();
        [SerializeField] private SemanticPredicate _areaEligibility = new SemanticPredicate();
        [SerializeField] private SemanticPredicate _polygonEligibility = new SemanticPredicate();
        [SerializeField] private SemanticPredicate _portalEligibility = new SemanticPredicate();
        [SerializeField] private List<SemanticCostRule> _costRules = new List<SemanticCostRule>();

        public TraversalPolicyData()
        {
        }

        public TraversalPolicyData(
            SemanticMask capabilityMask,
            SemanticPredicate areaEligibility,
            SemanticPredicate polygonEligibility,
            SemanticPredicate portalEligibility,
            IEnumerable<SemanticCostRule> costRules = null)
        {
            _capabilityMask = capabilityMask?.Clone() ?? new SemanticMask();
            _areaEligibility = areaEligibility?.Clone() ?? new SemanticPredicate();
            _polygonEligibility = polygonEligibility?.Clone() ?? new SemanticPredicate();
            _portalEligibility = portalEligibility?.Clone() ?? new SemanticPredicate();
            _costRules = costRules == null
                ? new List<SemanticCostRule>()
                : new List<SemanticCostRule>(costRules);
            SortRules();
        }

        public SemanticMask CapabilityMask => (_capabilityMask ?? new SemanticMask()).Clone();
        public SemanticPredicate AreaEligibility => (_areaEligibility ?? new SemanticPredicate()).Clone();
        public SemanticPredicate PolygonEligibility => (_polygonEligibility ?? new SemanticPredicate()).Clone();
        public SemanticPredicate PortalEligibility => (_portalEligibility ?? new SemanticPredicate()).Clone();
        public IReadOnlyList<SemanticCostRule> CostRules => _costRules;

        public bool CanTraverse(
            NavigationElementKind kind,
            SemanticMask semantics,
            SemanticMask requiredCapabilities)
        {
            EnsureData();
            if (!_capabilityMask.ContainsAll(requiredCapabilities))
            {
                return false;
            }

            return GetPredicate(kind).Allows(semantics);
        }

        public double GetDistanceMultiplier(SemanticMask semantics)
        {
            EnsureData();
            double multiplier = 1d;
            for (int index = 0; index < _costRules.Count; index++)
            {
                SemanticCostRule rule = _costRules[index];
                if (semantics != null && semantics.Contains(rule.Slot))
                {
                    multiplier *= rule.DistanceMultiplier;
                }
            }

            return multiplier;
        }

        public double GetEntryPenalty(SemanticMask semantics)
        {
            EnsureData();
            double penalty = 0d;
            for (int index = 0; index < _costRules.Count; index++)
            {
                SemanticCostRule rule = _costRules[index];
                if (semantics != null && semantics.Contains(rule.Slot))
                {
                    penalty += rule.EntryPenalty;
                }
            }

            return penalty;
        }

        public bool TryValidate(SemanticRegistryAsset registry, out string error)
        {
            EnsureData();
            if (registry == null)
            {
                error = "A traversal policy requires a semantic registry.";
                return false;
            }

            int capacity = registry.SlotCapacity;
            if (_capabilityMask.HasBitsAtOrAbove(capacity) ||
                _areaEligibility.HasBitsAtOrAbove(capacity) ||
                _polygonEligibility.HasBitsAtOrAbove(capacity) ||
                _portalEligibility.HasBitsAtOrAbove(capacity))
            {
                error = "The policy contains semantic slots outside its registry.";
                return false;
            }

            var occupiedSlots = new HashSet<int>();
            for (int index = 0; index < _costRules.Count; index++)
            {
                SemanticCostRule rule = _costRules[index];
                if (!rule.SemanticId.IsValid ||
                    !registry.TryGet(rule.Slot, out SemanticDefinition definition) ||
                    definition.Id != rule.SemanticId)
                {
                    error = $"Cost rule {index} does not match a semantic registry slot.";
                    return false;
                }

                if (!occupiedSlots.Add(rule.Slot))
                {
                    error = $"Semantic slot {rule.Slot} has more than one cost rule.";
                    return false;
                }

                if (!IsNonnegativeFinite(rule.DistanceMultiplier) ||
                    !IsNonnegativeFinite(rule.EntryPenalty))
                {
                    error = $"Semantic slot {rule.Slot} has an invalid traversal cost.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        internal TraversalPolicyData Clone()
        {
            return new TraversalPolicyData(
                _capabilityMask,
                _areaEligibility,
                _polygonEligibility,
                _portalEligibility,
                _costRules);
        }

        internal ulong ComputeFingerprint(ulong registryFingerprint)
        {
            EnsureData();
            FingerprintBuilder fingerprint = FingerprintBuilder.Create();
            fingerprint.Add(registryFingerprint);
            _capabilityMask.AddToFingerprint(ref fingerprint);
            _areaEligibility.AddToFingerprint(ref fingerprint);
            _polygonEligibility.AddToFingerprint(ref fingerprint);
            _portalEligibility.AddToFingerprint(ref fingerprint);

            SortRules();
            fingerprint.Add(_costRules.Count);
            for (int index = 0; index < _costRules.Count; index++)
            {
                SemanticCostRule rule = _costRules[index];
                fingerprint.Add(rule.SemanticId);
                fingerprint.Add(rule.Slot);
                fingerprint.Add(rule.DistanceMultiplier);
                fingerprint.Add(rule.EntryPenalty);
            }

            return fingerprint.Value;
        }

        internal void SetCapabilityMask(SemanticMask mask)
        {
            _capabilityMask = mask?.Clone() ?? new SemanticMask();
        }

        internal void SetEligibility(NavigationElementKind kind, SemanticPredicate predicate)
        {
            SemanticPredicate value = predicate?.Clone() ?? new SemanticPredicate();
            switch (kind)
            {
                case NavigationElementKind.Area:
                    _areaEligibility = value;
                    break;
                case NavigationElementKind.Polygon:
                    _polygonEligibility = value;
                    break;
                case NavigationElementKind.Portal:
                    _portalEligibility = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal void SetCostRule(SemanticCostRule rule)
        {
            EnsureData();
            for (int index = 0; index < _costRules.Count; index++)
            {
                if (_costRules[index].Slot == rule.Slot)
                {
                    _costRules[index] = rule;
                    SortRules();
                    return;
                }
            }

            _costRules.Add(rule);
            SortRules();
        }

        internal bool RemoveCostRule(int slot)
        {
            EnsureData();
            for (int index = 0; index < _costRules.Count; index++)
            {
                if (_costRules[index].Slot == slot)
                {
                    _costRules.RemoveAt(index);
                    return true;
                }
            }

            return false;
        }

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            EnsureData();
            _capabilityMask.RemapSlots(oldToNew, newCapacity);
            _areaEligibility.RemapSemanticSlots(oldToNew, newCapacity);
            _polygonEligibility.RemapSemanticSlots(oldToNew, newCapacity);
            _portalEligibility.RemapSemanticSlots(oldToNew, newCapacity);

            for (int index = _costRules.Count - 1; index >= 0; index--)
            {
                SemanticCostRule rule = _costRules[index];
                int newSlot = rule.Slot >= 0 && rule.Slot < oldToNew.Length ? oldToNew[rule.Slot] : -1;
                if (newSlot < 0)
                {
                    _costRules.RemoveAt(index);
                }
                else
                {
                    _costRules[index] = rule.WithSlot(newSlot);
                }
            }

            SortRules();
        }

        private SemanticPredicate GetPredicate(NavigationElementKind kind)
        {
            switch (kind)
            {
                case NavigationElementKind.Area:
                    return _areaEligibility;
                case NavigationElementKind.Polygon:
                    return _polygonEligibility;
                case NavigationElementKind.Portal:
                    return _portalEligibility;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private void EnsureData()
        {
            _capabilityMask = _capabilityMask ?? new SemanticMask();
            _areaEligibility = _areaEligibility ?? new SemanticPredicate();
            _polygonEligibility = _polygonEligibility ?? new SemanticPredicate();
            _portalEligibility = _portalEligibility ?? new SemanticPredicate();
            _costRules = _costRules ?? new List<SemanticCostRule>();
        }

        private void SortRules()
        {
            EnsureData();
            _costRules.Sort((left, right) => left.Slot.CompareTo(right.Slot));
        }

        private static bool IsNonnegativeFinite(double value)
        {
            return value >= 0d && !double.IsInfinity(value);
        }
    }

    [CreateAssetMenu(fileName = "Areafinder Traversal Policy", menuName = "Areafinder/Traversal Policy")]
    public sealed class TraversalPolicyAsset : ScriptableObject
    {
        [SerializeField] private PolicyId _id = new PolicyId();
        [SerializeField] private SemanticRegistryAsset _registry;
        [SerializeField] private TraversalPolicyData _data = new TraversalPolicyData();
        [SerializeField] private ulong _revision = 1UL;

        public PolicyId Id => _id;
        public SemanticRegistryAsset Registry => _registry;
        public TraversalPolicyData Data => (_data ?? new TraversalPolicyData()).Clone();
        public ulong Revision => _revision;
        public ulong ContentFingerprint => (_data ?? new TraversalPolicyData()).ComputeFingerprint(
            _registry != null ? _registry.SchemaFingerprint : 0UL);

        public void SetRegistry(SemanticRegistryAsset registry)
        {
            if (_registry == registry)
            {
                return;
            }

            _registry = registry;
            Touch();
        }

        public void SetCapabilityMask(SemanticMask mask)
        {
            EnsureIdentityAndData();
            _data.SetCapabilityMask(mask);
            Touch();
        }

        public void SetEligibility(NavigationElementKind kind, SemanticPredicate predicate)
        {
            EnsureIdentityAndData();
            _data.SetEligibility(kind, predicate);
            Touch();
        }

        public void SetCostRule(SemanticId semanticId, double distanceMultiplier, double entryPenalty)
        {
            if (_registry == null || !_registry.TryGet(semanticId, out SemanticDefinition definition))
            {
                throw new ArgumentException("The semantic ID is not present in this policy's registry.", nameof(semanticId));
            }

            EnsureIdentityAndData();
            _data.SetCostRule(new SemanticCostRule(
                semanticId,
                definition.Slot,
                distanceMultiplier,
                entryPenalty));
            Touch();
        }

        public bool RemoveCostRule(int slot)
        {
            EnsureIdentityAndData();
            if (!_data.RemoveCostRule(slot))
            {
                return false;
            }

            Touch();
            return true;
        }

        public bool TryValidate(out string error)
        {
            EnsureIdentityAndData();
            if (!_id.IsValid)
            {
                error = "The traversal policy has no stable identity.";
                return false;
            }

            return _data.TryValidate(_registry, out error);
        }

        internal bool UsesRegistry(SemanticRegistryAsset registry) => _registry == registry;

        internal void RemapSemanticSlots(int[] oldToNew, int newCapacity)
        {
            EnsureIdentityAndData();
            _data.RemapSemanticSlots(oldToNew, newCapacity);
            Touch();
        }

        internal TraversalPolicyData RawData
        {
            get
            {
                EnsureIdentityAndData();
                return _data;
            }
        }

        private void OnEnable()
        {
            EnsureIdentityAndData();
        }

        private void EnsureIdentityAndData()
        {
            if (!_id.IsValid)
            {
                _id = PolicyId.New();
            }

            _data = _data ?? new TraversalPolicyData();
        }

        private void Touch()
        {
            unchecked
            {
                _revision++;
                if (_revision == 0UL)
                {
                    _revision = 1UL;
                }
            }
        }
    }
}
