using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public sealed class SemanticDefinition
    {
        [SerializeField] private SemanticId _id;
        [SerializeField] private int _slot;
        [SerializeField] private string _displayName;
        [SerializeField] private string _description;
        [SerializeField] private bool _isDeleted;

        internal SemanticDefinition(SemanticId id, int slot, string displayName, string description)
        {
            _id = id;
            _slot = slot;
            _displayName = displayName;
            _description = description ?? string.Empty;
            _isDeleted = false;
        }

        public SemanticId Id => _id;
        public int Slot => _slot;
        public string DisplayName => _displayName;
        public string Description => _description;
        public bool IsDeleted => _isDeleted;

        internal void Rename(string displayName) => _displayName = displayName;
        internal void SetDescription(string description) => _description = description ?? string.Empty;
        internal void SetDeleted(bool isDeleted) => _isDeleted = isDeleted;
        internal void SetSlot(int slot) => _slot = slot;
    }

    [CreateAssetMenu(fileName = "Areafinder Semantic Registry", menuName = "Areafinder/Semantic Registry")]
    public sealed class SemanticRegistryAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private List<SemanticDefinition> _definitions = new List<SemanticDefinition>();
        [SerializeField] private int _nextSlot;
        [SerializeField] private ulong _revision = 1UL;

        public IReadOnlyList<SemanticDefinition> Definitions => _definitions;
        public int SlotCapacity => _nextSlot;
        public int RequiredWordCount => SemanticMask.WordCountForSlots(_nextSlot);
        public ulong Revision => _revision;

        public ulong SchemaFingerprint
        {
            get
            {
                FingerprintBuilder fingerprint = FingerprintBuilder.Create();
                fingerprint.Add(_nextSlot);
                for (int index = 0; index < _definitions.Count; index++)
                {
                    SemanticDefinition definition = _definitions[index];
                    if (definition == null)
                    {
                        fingerprint.Add(-1);
                        continue;
                    }

                    fingerprint.Add(definition.Id);
                    fingerprint.Add(definition.Slot);
                    fingerprint.Add(definition.IsDeleted);
                }

                return fingerprint.Value;
            }
        }

        public SemanticId Add(string displayName, string description = null)
        {
            ValidateDisplayName(displayName);
            SemanticId id = SemanticId.New();
            _definitions.Add(new SemanticDefinition(id, _nextSlot, displayName.Trim(), description));
            _nextSlot++;
            Touch();
            return id;
        }

        public bool Rename(SemanticId id, string displayName)
        {
            ValidateDisplayName(displayName);
            if (!TryGet(id, out SemanticDefinition definition))
            {
                return false;
            }

            string normalized = displayName.Trim();
            if (definition.DisplayName == normalized)
            {
                return true;
            }

            definition.Rename(normalized);
            Touch();
            return true;
        }

        public bool SetDescription(SemanticId id, string description)
        {
            if (!TryGet(id, out SemanticDefinition definition))
            {
                return false;
            }

            string normalized = description ?? string.Empty;
            if (definition.Description == normalized)
            {
                return true;
            }

            definition.SetDescription(normalized);
            Touch();
            return true;
        }

        public bool Delete(SemanticId id)
        {
            return SetDeleted(id, true);
        }

        public bool Restore(SemanticId id)
        {
            return SetDeleted(id, false);
        }

        public bool TryGet(SemanticId id, out SemanticDefinition definition)
        {
            for (int index = 0; index < _definitions.Count; index++)
            {
                SemanticDefinition candidate = _definitions[index];
                if (candidate != null && candidate.Id == id)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryGet(int slot, out SemanticDefinition definition)
        {
            for (int index = 0; index < _definitions.Count; index++)
            {
                SemanticDefinition candidate = _definitions[index];
                if (candidate != null && candidate.Slot == slot)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        internal int[] BuildCompactionMap()
        {
            int[] oldToNew = new int[_nextSlot];
            for (int slot = 0; slot < oldToNew.Length; slot++)
            {
                oldToNew[slot] = -1;
            }

            int next = 0;
            for (int slot = 0; slot < _nextSlot; slot++)
            {
                if (TryGet(slot, out SemanticDefinition definition) && !definition.IsDeleted)
                {
                    oldToNew[slot] = next++;
                }
            }

            return oldToNew;
        }

        internal void ApplyCompaction(int[] oldToNew)
        {
            if (oldToNew == null || oldToNew.Length != _nextSlot)
            {
                throw new ArgumentException("The compaction map must contain every allocated slot.", nameof(oldToNew));
            }

            var compacted = new List<SemanticDefinition>(_definitions.Count);
            int newCapacity = 0;
            for (int index = 0; index < _definitions.Count; index++)
            {
                SemanticDefinition definition = _definitions[index];
                if (definition == null || definition.IsDeleted)
                {
                    continue;
                }

                int newSlot = oldToNew[definition.Slot];
                if (newSlot < 0)
                {
                    throw new ArgumentException("The map removes an active semantic definition.", nameof(oldToNew));
                }

                definition.SetSlot(newSlot);
                compacted.Add(definition);
                newCapacity = Math.Max(newCapacity, newSlot + 1);
            }

            compacted.Sort((left, right) => left.Slot.CompareTo(right.Slot));
            _definitions = compacted;
            _nextSlot = newCapacity;
            Touch();
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (_definitions == null)
            {
                _definitions = new List<SemanticDefinition>();
            }

            int minimumNextSlot = 0;
            for (int index = 0; index < _definitions.Count; index++)
            {
                SemanticDefinition definition = _definitions[index];
                if (definition != null)
                {
                    minimumNextSlot = Math.Max(minimumNextSlot, definition.Slot + 1);
                }
            }

            _nextSlot = Math.Max(_nextSlot, minimumNextSlot);
        }

        private bool SetDeleted(SemanticId id, bool isDeleted)
        {
            if (!TryGet(id, out SemanticDefinition definition))
            {
                return false;
            }

            if (definition.IsDeleted == isDeleted)
            {
                return true;
            }

            definition.SetDeleted(isDeleted);
            Touch();
            return true;
        }

        private static void ValidateDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A semantic definition requires a display name.", nameof(displayName));
            }
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
