using System.IO;
using UnityEditor;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    internal static class NavigationBakeCommands
    {
        [MenuItem("Assets/Areafinder/Bake Selected World", false, 2000)]
        private static void BakeSelectedWorld()
        {
            var world = Selection.activeObject as NavigationWorldAsset;
            NavigationBakeResult validation = NavigationBaker.Validate(world);
            if (!validation.Succeeded)
            {
                LogIssues(world, validation);
                AreafinderAuthoringWindow.Open(world);
                return;
            }

            NavigationBakeAsset bake = GetOrCreateBake(world);
            Undo.RegisterCompleteObjectUndo(bake, "Bake Areafinder World");
            NavigationBakeResult result = NavigationBaker.Bake(world, bake);
            if (!result.Succeeded)
            {
                LogIssues(world, result);
                return;
            }

            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssetIfDirty(bake);
            Selection.activeObject = bake;
            EditorGUIUtility.PingObject(bake);
            AreafinderAuthoringWindow.Open(world, bake);
            Debug.Log(
                $"Baked Areafinder world '{world.name}' to '{AssetDatabase.GetAssetPath(bake)}' " +
                $"({FormatCount(bake.Polygons.Count, "polygon")}, {FormatCount(bake.Portals.Count, "portal")}).",
                bake);
        }

        [MenuItem("Assets/Areafinder/Bake Selected World", true)]
        private static bool CanBakeSelectedWorld()
        {
            return Selection.activeObject is NavigationWorldAsset;
        }

        [MenuItem("Assets/Areafinder/Open Authoring", false, 2001)]
        private static void OpenSelectedWorld()
        {
            AreafinderAuthoringWindow.Open(Selection.activeObject as NavigationWorldAsset);
        }

        [MenuItem("Assets/Areafinder/Open Authoring", true)]
        private static bool CanOpenSelectedWorld()
        {
            return Selection.activeObject is NavigationWorldAsset;
        }

        private static NavigationBakeAsset GetOrCreateBake(NavigationWorldAsset world)
        {
            string worldPath = AssetDatabase.GetAssetPath(world);
            string directory = Path.GetDirectoryName(worldPath)?.Replace('\\', '/') ?? "Assets";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{world.name} Bake.asset");

            string[] candidates = AssetDatabase.FindAssets($"{world.name} Bake t:NavigationBakeAsset", new[] { directory });
            if (candidates.Length > 0)
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(candidates[0]);
                NavigationBakeAsset existing = AssetDatabase.LoadAssetAtPath<NavigationBakeAsset>(existingPath);
                if (existing != null)
                {
                    return existing;
                }
            }

            var created = ScriptableObject.CreateInstance<NavigationBakeAsset>();
            AssetDatabase.CreateAsset(created, path);
            Undo.RegisterCreatedObjectUndo(created, "Create Areafinder Bake");
            return created;
        }

        private static void LogIssues(NavigationWorldAsset world, NavigationBakeResult result)
        {
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                string message = $"Areafinder {issue.Code} [{issue.TargetId}]: {issue.Message}";
                if (issue.Severity == NavigationValidationSeverity.Error)
                {
                    Debug.LogError(message, world);
                }
                else
                {
                    Debug.LogWarning(message, world);
                }
            }
        }

        private static string FormatCount(int count, string noun)
        {
            return $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
        }
    }
}
