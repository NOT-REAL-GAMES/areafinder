using System;
using System.Collections.Generic;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public sealed class SemanticMask : IEquatable<SemanticMask>
    {
        [SerializeField]
        private ulong[] _words = Array.Empty<ulong>();

        public SemanticMask()
        {
        }

        public SemanticMask(int slotCapacity)
        {
            if (slotCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCapacity));
            }

            _words = new ulong[WordCountForSlots(slotCapacity)];
        }

        private SemanticMask(ulong[] words)
        {
            _words = words;
        }

        public int WordCount => _words?.Length ?? 0;
        public int SlotCapacity => WordCount * 64;
        public bool IsEmpty
        {
            get
            {
                for (int index = 0; index < WordCount; index++)
                {
                    if (_words[index] != 0UL)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static int WordCountForSlots(int slotCapacity)
        {
            if (slotCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCapacity));
            }

            return (slotCapacity + 63) / 64;
        }

        public bool Contains(int slot)
        {
            ValidateSlot(slot);
            int wordIndex = slot / 64;
            return wordIndex < WordCount && (_words[wordIndex] & (1UL << (slot % 64))) != 0UL;
        }

        public void Set(int slot, bool value = true)
        {
            ValidateSlot(slot);
            int wordIndex = slot / 64;
            if (value)
            {
                EnsureSlotCapacity(slot + 1);
                _words[wordIndex] |= 1UL << (slot % 64);
            }
            else if (wordIndex < WordCount)
            {
                _words[wordIndex] &= ~(1UL << (slot % 64));
            }
        }

        public void Clear()
        {
            if (_words != null)
            {
                Array.Clear(_words, 0, _words.Length);
            }
        }

        public void EnsureSlotCapacity(int slotCapacity)
        {
            if (slotCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCapacity));
            }

            int requiredWords = WordCountForSlots(slotCapacity);
            if (requiredWords <= WordCount)
            {
                return;
            }

            Array.Resize(ref _words, requiredWords);
        }

        public ulong GetWord(int wordIndex)
        {
            if (wordIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(wordIndex));
            }

            return wordIndex < WordCount ? _words[wordIndex] : 0UL;
        }

        public bool ContainsAll(SemanticMask required)
        {
            if (required == null)
            {
                return true;
            }

            for (int index = 0; index < required.WordCount; index++)
            {
                ulong requiredWord = required.GetWord(index);
                if ((GetWord(index) & requiredWord) != requiredWord)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Intersects(SemanticMask other)
        {
            if (other == null)
            {
                return false;
            }

            int count = Math.Min(WordCount, other.WordCount);
            for (int index = 0; index < count; index++)
            {
                if ((_words[index] & other._words[index]) != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        public SemanticMask Clone()
        {
            if (_words == null || _words.Length == 0)
            {
                return new SemanticMask();
            }

            return new SemanticMask((ulong[])_words.Clone());
        }

        public bool Equals(SemanticMask other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null)
            {
                return false;
            }

            int count = Math.Max(WordCount, other.WordCount);
            for (int index = 0; index < count; index++)
            {
                if (GetWord(index) != other.GetWord(index))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SemanticMask);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                int lastWord = WordCount - 1;
                while (lastWord >= 0 && _words[lastWord] == 0UL)
                {
                    lastWord--;
                }

                for (int index = 0; index <= lastWord; index++)
                {
                    hash = (hash * 31) ^ _words[index].GetHashCode();
                }

                return hash;
            }
        }

        internal bool HasBitsAtOrAbove(int slot)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            int wordIndex = slot / 64;
            if (wordIndex >= WordCount)
            {
                return false;
            }

            int bitIndex = slot % 64;
            ulong lowerBits = bitIndex == 0 ? 0UL : (1UL << bitIndex) - 1UL;
            if ((_words[wordIndex] & ~lowerBits) != 0UL)
            {
                return true;
            }

            for (int index = wordIndex + 1; index < WordCount; index++)
            {
                if (_words[index] != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        internal void RemapSlots(int[] oldToNew, int newSlotCapacity)
        {
            if (oldToNew == null)
            {
                throw new ArgumentNullException(nameof(oldToNew));
            }

            ulong[] remapped = new ulong[WordCountForSlots(newSlotCapacity)];
            int count = Math.Min(oldToNew.Length, SlotCapacity);
            for (int oldSlot = 0; oldSlot < count; oldSlot++)
            {
                int newSlot = oldToNew[oldSlot];
                if (newSlot >= 0 && Contains(oldSlot))
                {
                    remapped[newSlot / 64] |= 1UL << (newSlot % 64);
                }
            }

            _words = remapped;
        }

        internal void AddToFingerprint(ref FingerprintBuilder fingerprint)
        {
            fingerprint.Add(WordCount);
            for (int index = 0; index < WordCount; index++)
            {
                fingerprint.Add(_words[index]);
            }
        }

        private static void ValidateSlot(int slot)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }

    internal struct FingerprintBuilder
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value;

        internal static FingerprintBuilder Create()
        {
            return new FingerprintBuilder { _value = OffsetBasis };
        }

        internal ulong Value => _value;

        internal void Add(bool value) => Add(value ? 1UL : 0UL);
        internal void Add(int value) => Add(unchecked((ulong)(uint)value));
        internal void Add(long value) => Add(unchecked((ulong)value));

        internal void Add(ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                _value ^= (byte)(value >> (index * 8));
                _value *= Prime;
            }
        }

        internal void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));
        internal void Add(SemanticId value)
        {
            Add(value.High);
            Add(value.Low);
        }

        internal void Add(PolicyId value)
        {
            Add(value.High);
            Add(value.Low);
        }
    }
}
