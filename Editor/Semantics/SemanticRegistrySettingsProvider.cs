using UnityEditor;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    internal sealed class SemanticRegistrySettingsProvider : SettingsProvider
    {
        private Vector2 _scroll;
        private string _newDisplayName = string.Empty;
        private string _newDescription = string.Empty;
        private string _error;

        private SemanticRegistrySettingsProvider()
            : base("Project/Areafinder", SettingsScope.Project)
        {
            label = "Areafinder";
            keywords = new[] { "Areafinder", "navigation", "semantic", "registry" };
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SemanticRegistrySettingsProvider();
        }

        public override void OnGUI(string searchContext)
        {
            DrawRegistrySelector();

            SemanticRegistryAsset registry = SemanticRegistryProjectSettings.Registry;
            if (registry == null)
            {
                EditorGUILayout.HelpBox(
                    "Select or create the one semantic registry used by this project.",
                    MessageType.Info);
                if (GUILayout.Button("Create Semantic Registry", GUILayout.Width(190f)))
                {
                    CreateRegistry();
                }

                return;
            }

            DrawRegistry(registry);
        }

        private void DrawRegistrySelector()
        {
            EditorGUILayout.LabelField("Project Semantic Registry", EditorStyles.boldLabel);
            SemanticRegistryAsset current = SemanticRegistryProjectSettings.Registry;
            var selected = (SemanticRegistryAsset)EditorGUILayout.ObjectField(
                "Registry",
                current,
                typeof(SemanticRegistryAsset),
                false);
            if (selected != current && !SemanticRegistryProjectSettings.TrySetRegistry(selected, out _error))
            {
                selected = current;
            }

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            EditorGUILayout.Space();
        }

        private void DrawRegistry(SemanticRegistryAsset registry)
        {
            EditorGUILayout.LabelField(
                $"Allocated slots: {registry.SlotCapacity}    Words: {registry.RequiredWordCount}    Revision: {registry.Revision}",
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120f));
            for (int index = 0; index < registry.Definitions.Count; index++)
            {
                SemanticDefinition definition = registry.Definitions[index];
                if (definition != null)
                {
                    DrawDefinition(registry, definition);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            DrawAddDefinition(registry);

            bool hasDeleted = false;
            for (int index = 0; index < registry.Definitions.Count; index++)
            {
                hasDeleted |= registry.Definitions[index] != null && registry.Definitions[index].IsDeleted;
            }

            using (new EditorGUI.DisabledScope(!hasDeleted))
            {
                if (GUILayout.Button("Preview Destructive Slot Compaction", GUILayout.Width(240f)))
                {
                    SemanticRegistryCompactionWindow.Open(registry);
                }
            }

            EditorGUILayout.HelpBox(
                "Deletion leaves a tombstone and never reuses a slot. Compaction is an explicit destructive migration.",
                MessageType.None);

            if (GUILayout.Button("Open Areafinder Authoring", GUILayout.Width(190f)))
            {
                AreafinderAuthoringWindow.Open();
            }
        }

        private static void DrawDefinition(SemanticRegistryAsset registry, SemanticDefinition definition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(definition.Slot.ToString(), GUILayout.Width(34f));
                    using (new EditorGUI.DisabledScope(definition.IsDeleted))
                    {
                        string displayName = EditorGUILayout.DelayedTextField(definition.DisplayName);
                        if (displayName != definition.DisplayName && !string.IsNullOrWhiteSpace(displayName))
                        {
                            Undo.RecordObject(registry, "Rename Areafinder Semantic");
                            registry.Rename(definition.Id, displayName);
                            EditorUtility.SetDirty(registry);
                        }
                    }

                    if (GUILayout.Button(definition.IsDeleted ? "Restore" : "Delete", GUILayout.Width(62f)))
                    {
                        Undo.RecordObject(registry, definition.IsDeleted
                            ? "Restore Areafinder Semantic"
                            : "Delete Areafinder Semantic");
                        if (definition.IsDeleted)
                        {
                            registry.Restore(definition.Id);
                        }
                        else
                        {
                            registry.Delete(definition.Id);
                        }

                        EditorUtility.SetDirty(registry);
                    }
                }

                using (new EditorGUI.DisabledScope(definition.IsDeleted))
                {
                    string description = EditorGUILayout.DelayedTextField("Description", definition.Description);
                    if (description != definition.Description)
                    {
                        Undo.RecordObject(registry, "Edit Areafinder Semantic Description");
                        registry.SetDescription(definition.Id, description);
                        EditorUtility.SetDirty(registry);
                    }
                }

                if (definition.IsDeleted)
                {
                    EditorGUILayout.LabelField("Tombstoned", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawAddDefinition(SemanticRegistryAsset registry)
        {
            EditorGUILayout.LabelField("Add Semantic", EditorStyles.boldLabel);
            _newDisplayName = EditorGUILayout.TextField("Display name", _newDisplayName);
            _newDescription = EditorGUILayout.TextField("Description", _newDescription);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newDisplayName)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(80f)))
                {
                    Undo.RecordObject(registry, "Add Areafinder Semantic");
                    registry.Add(_newDisplayName, _newDescription);
                    EditorUtility.SetDirty(registry);
                    _newDisplayName = string.Empty;
                    _newDescription = string.Empty;
                }
            }
        }

        private void CreateRegistry()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Areafinder Semantic Registry",
                "Areafinder Semantic Registry",
                "asset",
                "Choose where to save the project semantic registry.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
            AssetDatabase.CreateAsset(registry, path);
            SemanticRegistryProjectSettings.TrySetRegistry(registry, out _error);
            Selection.activeObject = registry;
        }
    }
}
