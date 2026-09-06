using System;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    internal struct StableId128 : IEquatable<StableId128>, IComparable<StableId128>
    {
        [SerializeField]
        private ulong _high;

        [SerializeField]
        private ulong _low;

        internal StableId128(ulong high, ulong low)
        {
            _high = high;
            _low = low;
        }

        internal ulong High => _high;

        internal ulong Low => _low;

        internal bool IsValid => _high != 0UL || _low != 0UL;

        internal static StableId128 New()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            return new StableId128(ReadUInt64(bytes, 0), ReadUInt64(bytes, 8));
        }

        internal static StableId128 FromGuid(Guid value)
        {
            byte[] bytes = value.ToByteArray();
            return new StableId128(ReadUInt64(bytes, 0), ReadUInt64(bytes, 8));
        }

        internal Guid ToGuid()
        {
            byte[] bytes = new byte[16];
            WriteUInt64(bytes, 0, _high);
            WriteUInt64(bytes, 8, _low);
            return new Guid(bytes);
        }

        public bool Equals(StableId128 other)
        {
            return _high == other._high && _low == other._low;
        }

        public int CompareTo(StableId128 other)
        {
            int highComparison = _high.CompareTo(other._high);
            return highComparison != 0 ? highComparison : _low.CompareTo(other._low);
        }

        public override bool Equals(object obj)
        {
            return obj is StableId128 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_high.GetHashCode() * 397) ^ _low.GetHashCode();
            }
        }

        public override string ToString()
        {
            return ToGuid().ToString("N");
        }

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            ulong result = 0UL;
            for (int index = 0; index < 8; index++)
            {
                result |= (ulong)bytes[offset + index] << (index * 8);
            }

            return result;
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                bytes[offset + index] = (byte)(value >> (index * 8));
            }
        }
    }

    [Serializable]
    public struct AreaId : IEquatable<AreaId>, IComparable<AreaId>
    {
        [SerializeField] private StableId128 _value;
        public AreaId(Guid value) => _value = StableId128.FromGuid(value);
        internal AreaId(StableId128 value) => _value = value;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public static AreaId New() => new AreaId(StableId128.New());
        public int CompareTo(AreaId other) => _value.CompareTo(other._value);
        public bool Equals(AreaId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is AreaId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(AreaId left, AreaId right) => left.Equals(right);
        public static bool operator !=(AreaId left, AreaId right) => !left.Equals(right);
    }

    [Serializable]
    public struct PortalId : IEquatable<PortalId>, IComparable<PortalId>
    {
        [SerializeField] private StableId128 _value;
        public PortalId(Guid value) => _value = StableId128.FromGuid(value);
        internal PortalId(StableId128 value) => _value = value;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public static PortalId New() => new PortalId(StableId128.New());
        public int CompareTo(PortalId other) => _value.CompareTo(other._value);
        public bool Equals(PortalId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is PortalId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(PortalId left, PortalId right) => left.Equals(right);
        public static bool operator !=(PortalId left, PortalId right) => !left.Equals(right);
    }

    [Serializable]
    public struct PolygonId : IEquatable<PolygonId>, IComparable<PolygonId>
    {
        [SerializeField] private StableId128 _value;
        public PolygonId(Guid value) => _value = StableId128.FromGuid(value);
        internal PolygonId(StableId128 value) => _value = value;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public static PolygonId New() => new PolygonId(StableId128.New());
        public int CompareTo(PolygonId other) => _value.CompareTo(other._value);
        public bool Equals(PolygonId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is PolygonId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(PolygonId left, PolygonId right) => left.Equals(right);
        public static bool operator !=(PolygonId left, PolygonId right) => !left.Equals(right);
    }

    [Serializable]
    public struct VertexId : IEquatable<VertexId>, IComparable<VertexId>
    {
        [SerializeField] private StableId128 _value;
        public VertexId(Guid value) => _value = StableId128.FromGuid(value);
        internal VertexId(StableId128 value) => _value = value;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public static VertexId New() => new VertexId(StableId128.New());
        public int CompareTo(VertexId other) => _value.CompareTo(other._value);
        public bool Equals(VertexId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is VertexId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(VertexId left, VertexId right) => left.Equals(right);
        public static bool operator !=(VertexId left, VertexId right) => !left.Equals(right);
    }

    [Serializable]
    public struct EdgeId : IEquatable<EdgeId>, IComparable<EdgeId>
    {
        [SerializeField] private StableId128 _value;
        public EdgeId(Guid value) => _value = StableId128.FromGuid(value);
        internal EdgeId(StableId128 value) => _value = value;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public static EdgeId New() => new EdgeId(StableId128.New());
        public int CompareTo(EdgeId other) => _value.CompareTo(other._value);
        public bool Equals(EdgeId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is EdgeId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(EdgeId left, EdgeId right) => left.Equals(right);
        public static bool operator !=(EdgeId left, EdgeId right) => !left.Equals(right);
    }

    [Serializable]
    public struct SemanticId : IEquatable<SemanticId>, IComparable<SemanticId>
    {
        [SerializeField] private StableId128 _value;
        public SemanticId(Guid value) => _value = StableId128.FromGuid(value);
        internal SemanticId(StableId128 value) => _value = value;
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        public static SemanticId New() => new SemanticId(StableId128.New());
        public int CompareTo(SemanticId other) => _value.CompareTo(other._value);
        public bool Equals(SemanticId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is SemanticId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(SemanticId left, SemanticId right) => left.Equals(right);
        public static bool operator !=(SemanticId left, SemanticId right) => !left.Equals(right);
    }

    [Serializable]
    public struct PolicyId : IEquatable<PolicyId>, IComparable<PolicyId>
    {
        [SerializeField] private StableId128 _value;
        public PolicyId(Guid value) => _value = StableId128.FromGuid(value);
        internal PolicyId(StableId128 value) => _value = value;
        internal ulong High => _value.High;
        internal ulong Low => _value.Low;
        public bool IsValid => _value.IsValid;
        public Guid Value => _value.ToGuid();
        public static PolicyId New() => new PolicyId(StableId128.New());
        public int CompareTo(PolicyId other) => _value.CompareTo(other._value);
        public bool Equals(PolicyId other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is PolicyId other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static bool operator ==(PolicyId left, PolicyId right) => left.Equals(right);
        public static bool operator !=(PolicyId left, PolicyId right) => !left.Equals(right);
    }
}
