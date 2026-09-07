using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class AreaPathCacheTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[index]);
            }

            _assets.Clear();
        }

        [Test]
        public void CacheDistinguishesHitsUnreachableEntriesAndZeroCapacity()
        {
            var cache = new AreaPathCache(2);
            LocalCacheKey reachableKey = Key(1, 2, 10UL, 1UL);
            LocalCacheKey unreachableKey = Key(2, 3, 10UL, 1UL);
            LocalPathData expected = Path(42d);

            Assert.That(cache.TryGet(reachableKey, out _, out _), Is.False);
            cache.Store(reachableKey, expected);
            cache.Store(unreachableKey, null);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGet(reachableKey, out LocalPathData actual, out bool reachable), Is.True);
            Assert.That(reachable, Is.True);
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(cache.TryGet(unreachableKey, out actual, out reachable), Is.True);
            Assert.That(reachable, Is.False);
            Assert.That(actual, Is.Null);

            var disabled = new AreaPathCache(0);
            disabled.Store(reachableKey, expected);
            Assert.That(disabled.Count, Is.Zero);
            Assert.That(disabled.TryGet(reachableKey, out _, out _), Is.False);
        }

        [Test]
        public void BoundedCacheEvictsTheLeastRecentlyUsedKeyDeterministically()
        {
            var cache = new AreaPathCache(2);
            LocalCacheKey first = Key(0, 1, 1UL, 1UL);
            LocalCacheKey second = Key(1, 2, 1UL, 1UL);
            LocalCacheKey third = Key(2, 3, 1UL, 1UL);
            LocalPathData firstPath = Path(1d);
            LocalPathData secondPath = Path(2d);
            LocalPathData thirdPath = Path(3d);

            cache.Store(first, firstPath);
            cache.Store(second, secondPath);
            Assert.That(cache.TryGet(first, out _, out _), Is.True);
            cache.Store(third, thirdPath);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGet(second, out _, out _), Is.False);
            Assert.That(cache.TryGet(first, out LocalPathData retainedFirst, out bool firstReachable), Is.True);
            Assert.That(firstReachable, Is.True);
            Assert.That(retainedFirst, Is.SameAs(firstPath));
            Assert.That(cache.TryGet(third, out LocalPathData retainedThird, out bool thirdReachable), Is.True);
            Assert.That(thirdReachable, Is.True);
            Assert.That(retainedThird, Is.SameAs(thirdPath));

            cache.Store(first, secondPath);
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGet(first, out LocalPathData replaced, out _), Is.True);
            Assert.That(replaced, Is.SameAs(secondPath));
        }

        [Test]
        public void CacheKeysSeparatePoliciesAndRevisions()
        {
            var cache = new AreaPathCache(4);
            LocalCacheKey original = Key(4, 8, 100UL, 7UL);
            LocalCacheKey otherPolicy = Key(4, 8, 101UL, 7UL);
            LocalCacheKey otherRevision = Key(4, 8, 100UL, 8UL);
            cache.Store(original, Path(5d));

            Assert.That(cache.TryGet(original, out _, out bool reachable), Is.True);
            Assert.That(reachable, Is.True);
            Assert.That(cache.TryGet(otherPolicy, out _, out _), Is.False);
            Assert.That(cache.TryGet(otherRevision, out _, out _), Is.False);
        }

        [Test]
        public void AdvancingOneAreaRevisionInvalidatesOnlyThatAreasCache()
        {
            NavigationBakeAsset bake = CreateTwoAreaBake();
            var data = new NavigationRuntimeData(bake, 2);
            try
            {
                LocalCacheKey firstKey = Key(0, 1, 9UL, data.AreaRevisions[0]);
                LocalCacheKey secondKey = Key(2, 3, 9UL, data.AreaRevisions[1]);
                LocalPathData firstPath = Path(1d);
                LocalPathData secondPath = Path(2d);
                data.Caches[0].Store(firstKey, firstPath);
                data.Caches[1].Store(secondKey, secondPath);
                ulong untouchedRevision = data.AreaRevisions[1];

                data.AdvanceAreaRevision(0);

                Assert.That(data.AreaRevisions[0], Is.EqualTo(2UL));
                Assert.That(data.AreaRevisions[1], Is.EqualTo(untouchedRevision));
                Assert.That(data.Caches[0].Count, Is.Zero);
                Assert.That(data.Caches[0].TryGet(firstKey, out _, out _), Is.False);
                Assert.That(data.Caches[1].Count, Is.EqualTo(1));
                Assert.That(data.Caches[1].TryGet(secondKey, out LocalPathData retained, out bool reachable),
                    Is.True);
                Assert.That(reachable, Is.True);
                Assert.That(retained, Is.SameAs(secondPath));
            }
            finally
            {
                data.Dispose();
            }
        }

        private NavigationBakeAsset CreateTwoAreaBake()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset world = Create<NavigationWorldAsset>();
            world.SetSemanticRegistry(registry);
            NavigationAreaAsset first = Create<NavigationAreaAsset>();
            first.AddPolygon(Rectangle(0f, 1f));
            NavigationAreaAsset second = Create<NavigationAreaAsset>();
            second.AddPolygon(Rectangle(2f, 3f));
            world.AddArea(first);
            world.AddArea(second);
            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult result = NavigationBaker.Bake(world, bake);
            Assert.That(result.Succeeded, Is.True, FormatIssues(result));
            return bake;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _assets.Add(value);
            return value;
        }

        private static LocalCacheKey Key(int from, int to, ulong policy, ulong revision)
        {
            return new LocalCacheKey(from, to, policy, revision);
        }

        private static LocalPathData Path(double cost)
        {
            return new LocalPathData(
                0,
                Vector3.zero,
                Vector3.one,
                cost,
                Array.Empty<int>(),
                Array.Empty<NavigationCrossingSpan>(),
                Array.Empty<Vector3>());
        }

        private static Vector3[] Rectangle(float minimumX, float maximumX)
        {
            return new[]
            {
                new Vector3(minimumX, 0f, 0f),
                new Vector3(minimumX, 0f, 1f),
                new Vector3(maximumX, 0f, 1f),
                new Vector3(maximumX, 0f, 0f)
            };
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var issues = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                issues.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, issues);
        }
    }
}
