using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace NotRealGames.Areafinder.Editor.Tests
{
    public sealed class AuthoringUndoTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            AreafinderAuthoringSession.SetWorld(null);
            Undo.ClearAll();
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_objects[index]);
            }

            _objects.Clear();
        }

        [Test]
        public void EditUndoAndRedoRefreshValidationAdjacencyAndRoutePreview()
        {
            AssertUndoRefreshContract();
        }

        [UnityTest]
        public IEnumerator UndoRefreshSubscriptionIsRestoredAfterDomainReload()
        {
            CompilationPipeline.RequestScriptCompilation();
            yield return new RecompileScripts();

            AssertUndoRefreshContract();
        }

        private void AssertUndoRefreshContract()
        {
            SemanticRegistryAsset registry = Create<SemanticRegistryAsset>();
            NavigationAreaAsset area = Create<NavigationAreaAsset>();
            NavigationPolygonRecord first = area.AddPolygon(Rectangle(0f, 1f));
            area.AddPolygon(Rectangle(1f, 2f));
            NavigationWorldAsset world = Create<NavigationWorldAsset>();
            world.SetSemanticRegistry(registry);
            world.AddArea(area);

            AreafinderAuthoringSession.SetWorld(world);
            Assert.That(HasErrors(), Is.False, FormatIssues());
            Assert.That(AreafinderAuthoringSession.AdjacencyPreview.Count, Is.EqualTo(2));

            int changed = 0;
            void OnChanged() => changed++;
            AreafinderAuthoringSession.Changed += OnChanged;
            try
            {
                Undo.RegisterCompleteObjectUndo(area, "Invalidate Areafinder polygon");
                first.MoveVertex(1, first.Vertices[0].Position);
                area.Touch();
                EditorUtility.SetDirty(area);
                Undo.FlushUndoRecordObjects();
                AreafinderAuthoringSession.NotifyAuthoringChanged();

                Assert.That(HasCode(NavigationValidationCode.DuplicateVertex), Is.True, FormatIssues());
                Assert.That(HasCode(NavigationValidationCode.ZeroLengthEdge), Is.True, FormatIssues());
                Assert.That(AreafinderAuthoringSession.AdjacencyPreview, Is.Empty);

                NavigationPath preview = EmptyPath();
                AreafinderAuthoringSession.SetPathPreview(preview, "invalid preview");
                int beforeUndo = changed;
                Undo.PerformUndo();

                Assert.That(changed, Is.GreaterThan(beforeUndo));
                Assert.That(AreafinderAuthoringSession.PreviewPath, Is.Null);
                Assert.That(AreafinderAuthoringSession.PreviewSummary, Is.Empty);
                Assert.That(HasErrors(), Is.False, FormatIssues());
                Assert.That(AreafinderAuthoringSession.AdjacencyPreview.Count, Is.EqualTo(2));

                AreafinderAuthoringSession.SetPathPreview(preview, "restored preview");
                int beforeRedo = changed;
                Undo.PerformRedo();

                Assert.That(changed, Is.GreaterThan(beforeRedo));
                Assert.That(AreafinderAuthoringSession.PreviewPath, Is.Null);
                Assert.That(AreafinderAuthoringSession.PreviewSummary, Is.Empty);
                Assert.That(HasCode(NavigationValidationCode.DuplicateVertex), Is.True, FormatIssues());
                Assert.That(HasCode(NavigationValidationCode.ZeroLengthEdge), Is.True, FormatIssues());
                Assert.That(AreafinderAuthoringSession.AdjacencyPreview, Is.Empty);
            }
            finally
            {
                AreafinderAuthoringSession.Changed -= OnChanged;
            }
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _objects.Add(value);
            return value;
        }

        private static NavigationPath EmptyPath()
        {
            return new NavigationPath(
                0d,
                Array.Empty<NavigationAreaSegment>(),
                Array.Empty<NavigationPortalTransition>(),
                Array.Empty<PolygonId>(),
                Array.Empty<NavigationCrossingSpan>(),
                Array.Empty<Vector3>(),
                Array.Empty<NavigationRevisionStamp>(),
                1UL);
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

        private static bool HasErrors()
        {
            for (int index = 0; index < AreafinderAuthoringSession.ValidationIssues.Count; index++)
            {
                if (AreafinderAuthoringSession.ValidationIssues[index].Severity ==
                    NavigationValidationSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCode(NavigationValidationCode code)
        {
            for (int index = 0; index < AreafinderAuthoringSession.ValidationIssues.Count; index++)
            {
                if (AreafinderAuthoringSession.ValidationIssues[index].Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatIssues()
        {
            var issues = new List<string>();
            for (int index = 0; index < AreafinderAuthoringSession.ValidationIssues.Count; index++)
            {
                NavigationValidationIssue issue = AreafinderAuthoringSession.ValidationIssues[index];
                issues.Add($"{issue.Severity}: {issue.Code}: {issue.Message}");
            }

            return string.Join(Environment.NewLine, issues);
        }
    }
}
