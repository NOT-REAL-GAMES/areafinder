using System;
using System.Collections.Generic;

namespace NotRealGames.Areafinder
{
    public sealed class CompiledTraversalPolicy
    {
        private readonly ulong[] _capabilities;
        private readonly CompiledPredicate _areaPredicate;
        private readonly CompiledPredicate _polygonPredicate;
        private readonly CompiledPredicate _portalPredicate;
        private readonly SemanticCostRule[] _costRules;

        private CompiledTraversalPolicy(
            PolicyId id,
            ulong revision,
            ulong fingerprint,
            ulong registryFingerprint,
            int wordCount,
            TraversalPolicyData data)
        {
            Id = id;
            Revision = revision;
            Fingerprint = fingerprint;
            RegistryFingerprint = registryFingerprint;
            WordCount = wordCount;
            _capabilities = CopyMask(data.CapabilityMask, wordCount);
            _areaPredicate = new CompiledPredicate(data.AreaEligibility, wordCount);
            _polygonPredicate = new CompiledPredicate(data.PolygonEligibility, wordCount);
            _portalPredicate = new CompiledPredicate(data.PortalEligibility, wordCount);
            _costRules = CopyRules(data.CostRules);
        }

        public PolicyId Id { get; }
        public ulong Revision { get; }
        public ulong Fingerprint { get; }
        public ulong RegistryFingerprint { get; }
        public int WordCount { get; }

        public static bool TryCompile(
            TraversalPolicyAsset source,
            NavigationBakeAsset bake,
            out CompiledTraversalPolicy policy,
            out string error)
        {
            if (source == null)
            {
                policy = null;
                error = "A traversal policy asset is required.";
                return false;
            }

            if (bake == null || !bake.IsUsable)
            {
                policy = null;
                error = "A usable navigation bake is required.";
                return false;
            }

            if (!source.TryValidate(out error))
            {
                policy = null;
                return false;
            }

            if (source.Registry.SchemaFingerprint != bake.SemanticRegistryFingerprint)
            {
                policy = null;
                error = "The traversal policy and navigation bake use different semantic registry schemas.";
                return false;
            }

            TraversalPolicyData data = source.Data;
            policy = new CompiledTraversalPolicy(
                source.Id,
                source.Revision,
                source.ContentFingerprint,
                source.Registry.SchemaFingerprint,
                bake.SemanticWordCount,
                data);
            error = null;
            return true;
        }

        internal static bool TryCompile(
            PolicyId id,
            ulong revision,
            SemanticRegistryAsset registry,
            TraversalPolicyData data,
            NavigationBakeAsset bake,
            out CompiledTraversalPolicy policy,
            out string error)
        {
            if (registry == null || data == null)
            {
                policy = null;
                error = "A semantic registry and traversal policy data are required.";
                return false;
            }

            if (bake == null || !bake.IsUsable)
            {
                policy = null;
                error = "A usable navigation bake is required.";
                return false;
            }

            if (!data.TryValidate(registry, out error))
            {
                policy = null;
                return false;
            }

            if (registry.SchemaFingerprint != bake.SemanticRegistryFingerprint)
            {
                policy = null;
                error = "The traversal policy and navigation bake use different semantic registry schemas.";
                return false;
            }

            ulong fingerprint = data.ComputeFingerprint(registry.SchemaFingerprint);
            policy = new CompiledTraversalPolicy(
                id.IsValid ? id : PolicyId.New(),
                revision == 0UL ? 1UL : revision,
                fingerprint,
                registry.SchemaFingerprint,
                bake.SemanticWordCount,
                data);
            error = null;
            return true;
        }

        internal bool CanTraverse(
            NavigationElementKind kind,
            ulong[] semanticWords,
            int semanticOffset,
            int requiredCapabilityOffset)
        {
            if (!ContainsAll(_capabilities, semanticWords, requiredCapabilityOffset, WordCount))
            {
                return false;
            }

            return GetPredicate(kind).Allows(semanticWords, semanticOffset, WordCount);
        }

        internal double GetDistanceMultiplier(ulong[] semanticWords, int semanticOffset)
        {
            double value = 1d;
            for (int index = 0; index < _costRules.Length; index++)
            {
                SemanticCostRule rule = _costRules[index];
                if (Contains(semanticWords, semanticOffset, WordCount, rule.Slot))
                {
                    value *= rule.DistanceMultiplier;
                }
            }

            return value;
        }

        internal double GetEntryPenalty(ulong[] semanticWords, int semanticOffset)
        {
            double value = 0d;
            for (int index = 0; index < _costRules.Length; index++)
            {
                SemanticCostRule rule = _costRules[index];
                if (Contains(semanticWords, semanticOffset, WordCount, rule.Slot))
                {
                    value += rule.EntryPenalty;
                }
            }

            return value;
        }

        internal ulong GetCapabilityWord(int word) => _capabilities[word];

        internal ulong GetPredicateWord(
            NavigationElementKind kind,
            int set,
            int word)
        {
            CompiledPredicate predicate = GetPredicate(kind);
            switch (set)
            {
                case 0:
                    return predicate.GetRequiredAll(word);
                case 1:
                    return predicate.GetRequiredAny(word);
                case 2:
                    return predicate.GetForbiddenAny(word);
                default:
                    throw new ArgumentOutOfRangeException(nameof(set));
            }
        }

        internal int CostRuleCount => _costRules.Length;
        internal SemanticCostRule GetCostRule(int index) => _costRules[index];

        private CompiledPredicate GetPredicate(NavigationElementKind kind)
        {
            switch (kind)
            {
                case NavigationElementKind.Area:
                    return _areaPredicate;
                case NavigationElementKind.Polygon:
                    return _polygonPredicate;
                case NavigationElementKind.Portal:
                    return _portalPredicate;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static SemanticCostRule[] CopyRules(IReadOnlyList<SemanticCostRule> source)
        {
            var result = new SemanticCostRule[source.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }

        private static ulong[] CopyMask(SemanticMask source, int wordCount)
        {
            var result = new ulong[wordCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source?.GetWord(index) ?? 0UL;
            }

            return result;
        }

        private static bool ContainsAll(
            ulong[] available,
            ulong[] requiredStorage,
            int requiredOffset,
            int wordCount)
        {
            for (int word = 0; word < wordCount; word++)
            {
                ulong required = requiredStorage[requiredOffset + word];
                if ((available[word] & required) != required)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(
            ulong[] storage,
            int offset,
            int wordCount,
            int slot)
        {
            int word = slot / 64;
            return slot >= 0 && word < wordCount &&
                   (storage[offset + word] & (1UL << (slot % 64))) != 0UL;
        }

        private readonly struct CompiledPredicate
        {
            private readonly ulong[] _requiredAll;
            private readonly ulong[] _requiredAny;
            private readonly ulong[] _forbiddenAny;
            private readonly bool _hasRequiredAny;

            internal CompiledPredicate(SemanticPredicate source, int wordCount)
            {
                _requiredAll = CopyMask(source.RequiredAll, wordCount);
                _requiredAny = CopyMask(source.RequiredAny, wordCount);
                _forbiddenAny = CopyMask(source.ForbiddenAny, wordCount);
                _hasRequiredAny = HasAny(_requiredAny);
            }

            internal bool Allows(ulong[] storage, int offset, int wordCount)
            {
                bool matchedAny = false;
                for (int word = 0; word < wordCount; word++)
                {
                    ulong available = storage[offset + word];
                    if ((available & _requiredAll[word]) != _requiredAll[word] ||
                        (available & _forbiddenAny[word]) != 0UL)
                    {
                        return false;
                    }

                    matchedAny |= (available & _requiredAny[word]) != 0UL;
                }

                return !_hasRequiredAny || matchedAny;
            }

            internal ulong GetRequiredAll(int word) => _requiredAll[word];
            internal ulong GetRequiredAny(int word) => _requiredAny[word];
            internal ulong GetForbiddenAny(int word) => _forbiddenAny[word];

            private static bool HasAny(ulong[] words)
            {
                for (int index = 0; index < words.Length; index++)
                {
                    if (words[index] != 0UL)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public sealed class TraversalPolicyBuilder
    {
        private readonly SemanticRegistryAsset _registry;
        private readonly TraversalPolicyData _data = new TraversalPolicyData();
        private readonly PolicyId _id = PolicyId.New();
        private ulong _revision = 1UL;

        public TraversalPolicyBuilder(SemanticRegistryAsset registry)
        {
            _registry = registry != null
                ? registry
                : throw new ArgumentNullException(nameof(registry));
        }

        public TraversalPolicyBuilder SetCapabilities(SemanticMask capabilities)
        {
            _data.SetCapabilityMask(capabilities);
            _revision++;
            return this;
        }

        public TraversalPolicyBuilder SetEligibility(
            NavigationElementKind kind,
            SemanticPredicate predicate)
        {
            _data.SetEligibility(kind, predicate);
            _revision++;
            return this;
        }

        public TraversalPolicyBuilder SetCost(
            SemanticId semanticId,
            double distanceMultiplier,
            double entryPenalty)
        {
            if (!_registry.TryGet(semanticId, out SemanticDefinition definition))
            {
                throw new ArgumentException("The semantic ID is not present in the registry.", nameof(semanticId));
            }

            _data.SetCostRule(new SemanticCostRule(
                semanticId,
                definition.Slot,
                distanceMultiplier,
                entryPenalty));
            _revision++;
            return this;
        }

        public bool TryCompile(
            NavigationBakeAsset bake,
            out CompiledTraversalPolicy policy,
            out string error)
        {
            return CompiledTraversalPolicy.TryCompile(
                _id,
                _revision,
                _registry,
                _data,
                bake,
                out policy,
                out error);
        }
    }
}
