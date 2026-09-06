using System;
using UnityEngine;

namespace NotRealGames.Areafinder
{
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        [SerializeField] private double _x;
        [SerializeField] private double _y;
        [SerializeField] private double _z;

        public Double3(double x, double y, double z)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        public double X => _x;
        public double Y => _y;
        public double Z => _z;
        public bool IsFinite => IsFiniteValue(_x) && IsFiniteValue(_y) && IsFiniteValue(_z);

        public static Double3 Zero => new Double3(0d, 0d, 0d);

        public static Double3 operator +(Double3 left, Double3 right) =>
            new Double3(left._x + right._x, left._y + right._y, left._z + right._z);

        public static Double3 operator -(Double3 left, Double3 right) =>
            new Double3(left._x - right._x, left._y - right._y, left._z - right._z);

        public static Double3 operator -(Double3 value) => new Double3(-value._x, -value._y, -value._z);

        public static Double3 operator *(Double3 value, double scale) =>
            new Double3(value._x * scale, value._y * scale, value._z * scale);

        public bool Equals(Double3 other) => _x.Equals(other._x) && _y.Equals(other._y) && _z.Equals(other._z);
        public override bool Equals(object obj) => obj is Double3 other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _x.GetHashCode();
                hash = (hash * 397) ^ _y.GetHashCode();
                return (hash * 397) ^ _z.GetHashCode();
            }
        }

        public static bool operator ==(Double3 left, Double3 right) => left.Equals(right);
        public static bool operator !=(Double3 left, Double3 right) => !left.Equals(right);

        internal static Double3 Rotate(Quaternion rotation, Double3 value)
        {
            double tx = 2d * ((rotation.y * value._z) - (rotation.z * value._y));
            double ty = 2d * ((rotation.z * value._x) - (rotation.x * value._z));
            double tz = 2d * ((rotation.x * value._y) - (rotation.y * value._x));

            return new Double3(
                value._x + (rotation.w * tx) + (rotation.y * tz) - (rotation.z * ty),
                value._y + (rotation.w * ty) + (rotation.z * tx) - (rotation.x * tz),
                value._z + (rotation.w * tz) + (rotation.x * ty) - (rotation.y * tx));
        }

        internal static bool IsFiniteValue(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public struct AreaFrame
    {
        [SerializeField] private Double3 _universeOrigin;
        [SerializeField] private Quaternion _rotation;

        public AreaFrame(Double3 universeOrigin, Quaternion rotation)
        {
            if (!universeOrigin.IsFinite)
            {
                throw new ArgumentException("The universe origin must be finite.", nameof(universeOrigin));
            }

            _universeOrigin = universeOrigin;
            _rotation = Normalize(rotation, nameof(rotation));
        }

        public Double3 UniverseOrigin => _universeOrigin;
        public Quaternion Rotation => _rotation;
        public bool IsValid => _universeOrigin.IsFinite && IsValidRotation(_rotation);
        public static AreaFrame Identity => new AreaFrame(Double3.Zero, Quaternion.identity);

        public Double3 ToUniverse(Vector3 localPosition)
        {
            EnsureValid();
            return _universeOrigin + Double3.Rotate(
                _rotation,
                new Double3(localPosition.x, localPosition.y, localPosition.z));
        }

        public Vector3 ToLocal(Double3 universePosition)
        {
            EnsureValid();
            Double3 local = Double3.Rotate(Quaternion.Inverse(_rotation), universePosition - _universeOrigin);
            return new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        }

        public Quaternion ToUniverseRotation(Quaternion localRotation)
        {
            EnsureValid();
            return _rotation * localRotation;
        }

        public Quaternion ToLocalRotation(Quaternion universeRotation)
        {
            EnsureValid();
            return Quaternion.Inverse(_rotation) * universeRotation;
        }

        private void EnsureValid()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("The area frame is not valid.");
            }
        }

        internal static Quaternion Normalize(Quaternion rotation, string parameterName)
        {
            if (!IsValidRotation(rotation))
            {
                throw new ArgumentException("The rotation must be finite and non-zero.", parameterName);
            }

            return rotation.normalized;
        }

        internal static bool IsValidRotation(Quaternion rotation)
        {
            if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w))
            {
                return false;
            }

            double lengthSquared =
                ((double)rotation.x * rotation.x) +
                ((double)rotation.y * rotation.y) +
                ((double)rotation.z * rotation.z) +
                ((double)rotation.w * rotation.w);
            return lengthSquared > 1e-12d;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public struct PortalTransform
    {
        [SerializeField] private Double3 _translation;
        [SerializeField] private Quaternion _rotation;

        public PortalTransform(Double3 translation, Quaternion rotation)
        {
            if (!translation.IsFinite)
            {
                throw new ArgumentException("The translation must be finite.", nameof(translation));
            }

            _translation = translation;
            _rotation = AreaFrame.Normalize(rotation, nameof(rotation));
        }

        public Double3 Translation => _translation;
        public Quaternion Rotation => _rotation;
        public bool IsValid => _translation.IsFinite && AreaFrame.IsValidRotation(_rotation);
        public static PortalTransform Identity => new PortalTransform(Double3.Zero, Quaternion.identity);

        public Double3 TransformPosition(Double3 sourcePosition)
        {
            EnsureValid();
            return Double3.Rotate(_rotation, sourcePosition) + _translation;
        }

        public Quaternion TransformRotation(Quaternion sourceRotation)
        {
            EnsureValid();
            return _rotation * sourceRotation;
        }

        public PortalTransform Inverse
        {
            get
            {
                EnsureValid();
                Quaternion inverseRotation = Quaternion.Inverse(_rotation);
                return new PortalTransform(Double3.Rotate(inverseRotation, -_translation), inverseRotation);
            }
        }

        private void EnsureValid()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("The portal transform is not valid.");
            }
        }
    }
}
