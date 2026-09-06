using System;

namespace NotRealGames.Areafinder
{
    public enum PathRequestStatus : byte
    {
        Invalid,
        Queued,
        RunningGlobal,
        RunningLocal,
        Completed,
        Failed,
        Cancelled,
        Stale
    }

    public enum PathPriority : byte
    {
        Low,
        Normal,
        High
    }

    [Flags]
    public enum PathOutputFlags : byte
    {
        None = 0,
        PolygonCorridor = 1 << 0,
        SteeringTargets = 1 << 1,
        Default = PolygonCorridor | SteeringTargets
    }

    public enum PathFailureReason : byte
    {
        None,
        InvalidRequest,
        InvalidBake,
        BackendUnavailable,
        LocationNotFound,
        LocationAmbiguous,
        NoGlobalRoute,
        NoLocalRoute,
        CapacityExceeded
    }

    public enum LocationResolveStatus : byte
    {
        Found,
        NotFound,
        Ambiguous
    }

    public readonly struct PathRequestHandle : IEquatable<PathRequestHandle>
    {
        internal PathRequestHandle(int slot, uint generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public int Slot { get; }

        public uint Generation { get; }

        public bool IsValid => Slot >= 0 && Generation != 0;

        public bool Equals(PathRequestHandle other)
        {
            return Slot == other.Slot && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is PathRequestHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Slot, Generation);
        }

        public static bool operator ==(PathRequestHandle left, PathRequestHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PathRequestHandle left, PathRequestHandle right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return IsValid ? $"{Slot}:{Generation}" : "Invalid";
        }
    }
}
