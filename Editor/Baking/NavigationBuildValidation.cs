using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    internal static class NavigationBakeValidator
    {
        internal static List<string> FindInvalidProjectBakes()
        {
            string[] guids = AssetDatabase.FindAssets("t:NavigationBakeAsset");
            var invalid = new List<string>();
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                NavigationBakeAsset bake = AssetDatabase.LoadAssetAtPath<NavigationBakeAsset>(path);
                if (bake == null || !bake.IsUsable)
                {
                    invalid.Add(path);
                }
            }

            invalid.Sort(StringComparer.Ordinal);
            return invalid;
        }

        internal static string FormatFailure(IReadOnlyList<string> paths)
        {
            return "Areafinder refuses to run with stale or invalid committed bakes:" +
                   Environment.NewLine + string.Join(Environment.NewLine, paths);
        }
    }

    internal sealed class NavigationBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            List<string> invalid = NavigationBakeValidator.FindInvalidProjectBakes();
            if (invalid.Count > 0)
            {
                throw new BuildFailedException(NavigationBakeValidator.FormatFailure(invalid));
            }
        }
    }

    [InitializeOnLoad]
    internal static class NavigationPlayModeValidation
    {
        static NavigationPlayModeValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            List<string> invalid = NavigationBakeValidator.FindInvalidProjectBakes();
            if (invalid.Count == 0)
            {
                return;
            }

            EditorApplication.isPlaying = false;
            Debug.LogError(NavigationBakeValidator.FormatFailure(invalid));
        }
    }
}
