using System;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class TraversalPolicyTests
    {
        private SemanticRegistryAsset _registry;
        private SemanticId _walkable;
        private SemanticId _road;
        private SemanticId _authorized;

        [SetUp]
        public void SetUp()
        {
            _registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            _walkable = _registry.Add("Walkable");
            _road = _registry.Add("Road");
            _authorized = _registry.Add("Authorized");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_registry);
        }

        [Test]
        public void EligibilitySeparatesSemanticsFromAgentCapabilities()
        {
            var requiredAll = Mask(0);
            var requiredAny = Mask(1, 2);
            var forbiddenAny = Mask(2);
            var policy = new TraversalPolicyData(
                Mask(2),
                new SemanticPredicate(),
                new SemanticPredicate(requiredAll, requiredAny, forbiddenAny),
                new SemanticPredicate());

            Assert.That(policy.CanTraverse(
                NavigationElementKind.Polygon,
                Mask(0, 1),
                Mask(2)), Is.True);
            Assert.That(policy.CanTraverse(
                NavigationElementKind.Polygon,
                Mask(0, 2),
                Mask(2)), Is.False, "forbidden semantic");
            Assert.That(policy.CanTraverse(
                NavigationElementKind.Polygon,
                Mask(0, 1),
                Mask(1)), Is.False, "missing capability");
        }

        [Test]
        public void ActiveSemanticCostsMultiplyAndPenaltiesAdd()
        {
            var policy = new TraversalPolicyData(
                new SemanticMask(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new[]
                {
                    new SemanticCostRule(_walkable, 0, 0.5d, 2d),
                    new SemanticCostRule(_road, 1, 8d, 3d),
                    new SemanticCostRule(_authorized, 2, 4d, 20d)
                });

            Assert.That(policy.GetDistanceMultiplier(Mask(0, 1)), Is.EqualTo(4d));
            Assert.That(policy.GetEntryPenalty(Mask(0, 1)), Is.EqualTo(5d));
        }

        [Test]
        public void ValidPolicyMatchesRegistrySlotsAndIdentities()
        {
            var policy = new TraversalPolicyData(
                new SemanticMask(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new[] { new SemanticCostRule(_road, 1, 3d, 2d) });

            Assert.That(policy.TryValidate(_registry, out string error), Is.True, error);
        }

        [TestCase(-1d, 0d)]
        [TestCase(double.NaN, 0d)]
        [TestCase(double.PositiveInfinity, 0d)]
        [TestCase(1d, -1d)]
        [TestCase(1d, double.NaN)]
        [TestCase(1d, double.PositiveInfinity)]
        public void InvalidCostsFailPolicyCompilation(double multiplier, double penalty)
        {
            var policy = new TraversalPolicyData(
                new SemanticMask(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new[] { new SemanticCostRule(_road, 1, multiplier, penalty) });

            Assert.That(policy.TryValidate(_registry, out string error), Is.False);
            Assert.That(error, Does.Contain("invalid traversal cost"));
        }

        [Test]
        public void RegistryMismatchFailsPolicyCompilation()
        {
            var policy = new TraversalPolicyData(
                new SemanticMask(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new[] { new SemanticCostRule(SemanticId.New(), 1, 1d, 0d) });

            Assert.That(policy.TryValidate(_registry, out string error), Is.False);
            Assert.That(error, Does.Contain("does not match"));
        }

        [Test]
        public void DuplicateCostRulesFailPolicyCompilation()
        {
            var policy = new TraversalPolicyData(
                new SemanticMask(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new[]
                {
                    new SemanticCostRule(_road, 1, 2d, 0d),
                    new SemanticCostRule(_road, 1, 3d, 0d)
                });

            Assert.That(policy.TryValidate(_registry, out string error), Is.False);
            Assert.That(error, Does.Contain("more than one"));
        }

        [Test]
        public void SemanticSlotsBeyondRegistryFailPolicyCompilation()
        {
            var policy = new TraversalPolicyData(
                Mask(64),
                new SemanticPredicate(),
                new SemanticPredicate(),
                new SemanticPredicate());

            Assert.That(policy.TryValidate(_registry, out string error), Is.False);
            Assert.That(error, Does.Contain("outside"));
        }

        private static SemanticMask Mask(params int[] slots)
        {
            var mask = new SemanticMask();
            for (int index = 0; index < slots.Length; index++)
            {
                mask.Set(slots[index]);
            }

            return mask;
        }
    }
}
