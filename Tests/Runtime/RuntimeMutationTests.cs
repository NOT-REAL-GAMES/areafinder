using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class RuntimeMutationTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_objects[index]);
            }

            _objects.Clear();
        }

        [Test]
        public void PolygonPairMutationTogglesEveryOverlappingCrossingSpan()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationWorldAsset source = Create<NavigationWorldAsset>();
            source.SetSemanticRegistry(registry);
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            source.AddArea(area);
            NavigationPolygonRecord left = area.AddPolygon(new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 0.5f),
                new Vector3(1f, 0f, 0f)
            });
            NavigationPolygonRecord right = area.AddPolygon(new[]
            {
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 1f),
                new Vector3(2f, 0f, 1f),
                new Vector3(2f, 0f, 0f)
            });

            NavigationBakeAsset bake = Create<NavigationBakeAsset>();
            NavigationBakeResult bakeResult = NavigationBaker.Bake(source, bake);
            Assert.That(bakeResult.Succeeded, Is.True, FormatIssues(bakeResult));
            Assert.That(bake.Adjacencies.Count, Is.EqualTo(4),
                "The fixture requires two crossing spans in each direction.");
            Assert.That(
                new TraversalPolicyBuilder(registry).TryCompile(
                    bake,
                    out CompiledTraversalPolicy policy,
                    out string policyError),
                Is.True,
                policyError);

            using var world = new NavigationWorld(bake);
            var query = new PathQuery(
                new NavigationLocation(area.Id, new Vector3(0.25f, 0f, 0.5f)),
                new NavigationLocation(area.Id, new Vector3(1.75f, 0f, 0.5f)),
                policy);
            PathRequestHandle initial = world.Submit(query);
            Complete(world, initial);
            Assert.That(world.GetStatus(initial), Is.EqualTo(PathRequestStatus.Completed));

            ulong initialRevision = world.GetAreaRevision(area.Id);
            Assert.That(world.SetAdjacencyEnabled(left.Id, right.Id, false), Is.True);
            world.Tick(0);
            Assert.That(world.GetAreaRevision(area.Id), Is.EqualTo(initialRevision + 1UL));

            PathRequestHandle disabled = world.Submit(query);
            Complete(world, disabled);
            Assert.That(world.GetStatus(disabled), Is.EqualTo(PathRequestStatus.Failed));
            Assert.That(world.TryGetFailure(disabled, out PathFailureReason failure), Is.True);
            Assert.That(failure, Is.EqualTo(PathFailureReason.NoLocalRoute));

            Assert.That(world.SetAdjacencyEnabled(left.Id, right.Id, true), Is.True);
            world.Tick(0);
            PathRequestHandle restored = world.Submit(query);
            Complete(world, restored);
            Assert.That(world.GetStatus(restored), Is.EqualTo(PathRequestStatus.Completed));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _objects.Add(value);
            return value;
        }

        private static void Complete(NavigationWorld world, PathRequestHandle handle)
        {
            for (int tick = 0; tick < 16; tick++)
            {
                world.Tick(64);
                PathRequestStatus status = world.GetStatus(handle);
                if (status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
                    status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale)
                {
                    return;
                }
            }

            Assert.Fail("The navigation request did not become terminal.");
        }

        private static string FormatIssues(NavigationBakeResult result)
        {
            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                messages.Add($"{result.Issues[index].Severity}: {result.Issues[index].Code}: " +
                             result.Issues[index].Message);
            }

            return string.Join(Environment.NewLine, messages);
        }
    }
}
