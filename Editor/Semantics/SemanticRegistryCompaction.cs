using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    internal static class SemanticRegistryCompactor
    {
        internal static void CompactProject(SemanticRegistryAsset registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            NavigationWorldAsset[] worlds = FindAssets<NavigationWorldAsset>();
            NavigationAreaAsset[] areas = FindAssets<NavigationAreaAsset>();
            TraversalPolicyAsset[] policies = FindAssets<TraversalPolicyAsset>();
            Compact(registry, registry.BuildCompactionMap(), worlds, areas, policies);
            AssetDatabase.SaveAssets();
        }

        internal static void Compact(
            SemanticRegistryAsset registry,
            int[] oldToNew,
            IReadOnlyList<NavigationWorldAsset> worlds,
            IReadOnlyList<NavigationAreaAsset> areas,
            IReadOnlyList<TraversalPolicyAsset> policies)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (oldToNew == null || oldToNew.Length != registry.SlotCapacity)
            {
                throw new ArgumentException("The compaction map must contain every allocated slot.", nameof(oldToNew));
            }

            int newCapacity = 0;
            for (int index = 0; index < oldToNew.Length; index++)
            {
                newCapacity = Math.Max(newCapacity, oldToNew[index] + 1);
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Compact Areafinder Semantic Registry");
            try
            {
                Undo.RegisterCompleteObjectUndo(registry, "Compact Areafinder Semantic Registry");

                for (int index = 0; index < areas.Count; index++)
                {
                    NavigationAreaAsset area = areas[index];
                    if (area == null)
                    {
                        continue;
                    }

                    Undo.RegisterCompleteObjectUndo(area, "Remap Areafinder Area Semantics");
                    area.RemapSemanticSlots(oldToNew, newCapacity);
                    EditorUtility.SetDirty(area);
                }

                for (int index = 0; index < worlds.Count; index++)
                {
                    NavigationWorldAsset world = worlds[index];
                    if (world == null || world.SemanticRegistry != registry)
                    {
                        continue;
                    }

                    Undo.RegisterCompleteObjectUndo(world, "Remap Areafinder Portal Semantics");
                    for (int portalIndex = 0; portalIndex < world.Portals.Count; portalIndex++)
                    {
                        world.Portals[portalIndex]?.RemapSemanticSlots(oldToNew, newCapacity);
                    }

                    world.Touch();
                    EditorUtility.SetDirty(world);
                }

                for (int index = 0; index < policies.Count; index++)
                {
                    TraversalPolicyAsset policy = policies[index];
                    if (policy == null || !policy.UsesRegistry(registry))
                    {
                        continue;
                    }

                    Undo.RegisterCompleteObjectUndo(policy, "Remap Areafinder Policy Semantics");
                    policy.RemapSemanticSlots(oldToNew, newCapacity);
                    EditorUtility.SetDirty(policy);
                }

                registry.ApplyCompaction(oldToNew);
                EditorUtility.SetDirty(registry);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        internal static T[] FindAssets<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            var assets = new List<T>(guids.Length);
            for (int index = 0; index < guids.Length; index++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets.ToArray();
        }
    }

    internal sealed class SemanticRegistryCompactionWindow : EditorWindow
    {
        [SerializeField] private SemanticRegistryAsset _registry;
        private Vector2 _scroll;

        internal static void Open(SemanticRegistryAsset registry)
        {
            var window = GetWindow<SemanticRegistryCompactionWindow>(true, "Semantic Slot Compaction");
            window._registry = registry;
            window.minSize = new Vector2(430f, 320f);
            window.Show();
        }

        private void OnGUI()
        {
            if (_registry == null)
            {
                EditorGUILayout.HelpBox("The registry no longer exists.", MessageType.Error);
                return;
            }

            int[] map = _registry.BuildCompactionMap();
            EditorGUILayout.HelpBox(
                "This removes tombstones and rewrites semantic slots in all Areafinder Areas, Portals, and policies. Stable semantic IDs are retained; existing bakes become stale.",
                MessageType.Warning);
            EditorGUILayout.LabelField("Slot remap preview", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int oldSlot = 0; oldSlot < map.Length; oldSlot++)
            {
                _registry.TryGet(oldSlot, out SemanticDefinition definition);
                string name = definition?.DisplayName ?? "[missing]";
                string destination = map[oldSlot] >= 0 ? map[oldSlot].ToString() : "removed";
                EditorGUILayout.LabelField($"{oldSlot}  →  {destination}", name);
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Compact Registry"))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Compact Areafinder Semantic Registry?",
                    "Every supported semantic-bearing asset will be remapped. This is destructive and existing bakes will be stale.",
                    "Compact",
                    "Cancel");
                if (!confirmed)
                {
                    return;
                }

                try
                {
                    SemanticRegistryCompactor.CompactProject(_registry);
                    Close();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("Compaction failed", exception.Message, "OK");
                }
            }
        }
    }
}
