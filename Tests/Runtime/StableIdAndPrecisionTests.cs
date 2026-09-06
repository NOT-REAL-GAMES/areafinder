using System;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class StableIdAndPrecisionTests
    {
        [Test]
        public void StableIdsRoundTripGuidAndPreserveOrdering()
        {
            Guid low = Guid.Parse("00000000-0000-0000-0000-000000000001");
            Guid high = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var first = new AreaId(low);
            var second = new AreaId(high);

            Assert.That(first.IsValid, Is.True);
            Assert.That(first.Value, Is.EqualTo(low));
            Assert.That(first, Is.EqualTo(new AreaId(low)));
            Assert.That(first.CompareTo(second), Is.LessThan(0));
            Assert.That(default(AreaId).IsValid, Is.False);
        }

        [Test]
        public void EveryPublicIdentityTypeRoundTripsItsGuid()
        {
            Guid value = Guid.Parse("12345678-1234-5678-90ab-cdef12345678");

            Assert.That(new PortalId(value).Value, Is.EqualTo(value));
            Assert.That(new PolygonId(value).Value, Is.EqualTo(value));
            Assert.That(new VertexId(value).Value, Is.EqualTo(value));
            Assert.That(new EdgeId(value).Value, Is.EqualTo(value));
            Assert.That(new SemanticId(value).Value, Is.EqualTo(value));
            Assert.That(new PolicyId(value).Value, Is.EqualTo(value));
        }

        [Test]
        public void RotatedAreaFrameRoundTripsAtLargeUniverseCoordinates()
        {
            var frame = new AreaFrame(
                new Double3(8_000_000_000.25d, -125.5d, 4_000_000_000.75d),
                Quaternion.Euler(17f, 113f, -9f));
            var local = new Vector3(12.5f, -3.25f, 40.75f);

            Vector3 roundTrip = frame.ToLocal(frame.ToUniverse(local));

            Assert.That(roundTrip.x, Is.EqualTo(local.x).Within(0.001f));
            Assert.That(roundTrip.y, Is.EqualTo(local.y).Within(0.001f));
            Assert.That(roundTrip.z, Is.EqualTo(local.z).Within(0.001f));
        }

        [Test]
        public void RotatedAreaFrameRoundTripsAtExtremeUniverseCoordinates()
        {
            var frame = new AreaFrame(
                new Double3(5_000_000_000_000.25d, -4_000_000_000_000.5d, 3_000_000_000_000.75d),
                Quaternion.Euler(-23f, 137f, 11f));
            var local = new Vector3(123.125f, -47.5f, 809.75f);

            Vector3 roundTrip = frame.ToLocal(frame.ToUniverse(local));

            Assert.That(roundTrip.x, Is.EqualTo(local.x).Within(0.002f));
            Assert.That(roundTrip.y, Is.EqualTo(local.y).Within(0.002f));
            Assert.That(roundTrip.z, Is.EqualTo(local.z).Within(0.002f));
        }

        [Test]
        public void PortalTransformAndInverseRoundTripPosition()
        {
            var transform = new PortalTransform(
                new Double3(1000.25d, -30d, 8000.5d),
                Quaternion.Euler(0f, 90f, 0f));
            var source = new Double3(17d, 4d, -8d);

            Double3 roundTrip = transform.Inverse.TransformPosition(transform.TransformPosition(source));

            Assert.That(roundTrip.X, Is.EqualTo(source.X).Within(1e-5d));
            Assert.That(roundTrip.Y, Is.EqualTo(source.Y).Within(1e-5d));
            Assert.That(roundTrip.Z, Is.EqualTo(source.Z).Within(1e-5d));
        }

        [Test]
        public void InvalidFramesAndTransformsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new AreaFrame(Double3.Zero, default));
            Assert.Throws<ArgumentException>(() => new PortalTransform(
                new Double3(double.NaN, 0d, 0d),
                Quaternion.identity));
        }
    }
}
