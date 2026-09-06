using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    internal sealed class AreafinderAuthoringWindow : EditorWindow
    {
        private static readonly string[] ModeNames = Enum.GetNames(typeof(AreafinderAuthoringMode));

        [SerializeField] private NavigationWorldAsset _world;
        [SerializeField] private NavigationBakeAsset _bake;
        [SerializeField] private TraversalPolicyAsset _previewPolicy;
        [SerializeField] private NavigationAreaAsset _previewStartArea;
        [SerializeField] private NavigationAreaAsset _previewGoalArea;
        [SerializeField] private Vector3 _previewStart = new Vector3(-0.5f, 0f, 0f);
        [SerializeField] private Vector3 _previewGoal = new Vector3(0.5f, 0f, 0f);
        private Vector2 _scroll;
        private bool _showFrame;
        private bool _showInference;
        private bool _showSemantics = true;
        private int _splitFirstVertex;
        private int _splitSecondVertex = 2;
        private int _mergePolygonIndex = -1;
        private string _message;
        private MessageType _messageType;

        [MenuItem("Window/Areafinder/Authoring")]
        internal static AreafinderAuthoringWindow Open()
        {
            var window = GetWindow<AreafinderAuthoringWindow>();
            window.titleContent = new GUIContent("Areafinder");
            window.minSize = new Vector2(390f, 420f);
            window.Show();
            return window;
        }

        internal static void Open(NavigationWorldAsset world, NavigationBakeAsset bake = null)
        {
            AreafinderAuthoringWindow window = Open();
            window._world = world;
            window._bake = bake;
            AreafinderAuthoringSession.SetWorld(world);
        }

        private void OnEnable()
        {
            AreafinderAuthoringSession.Changed += OnSessionChanged;
            if (_world == null && Selection.activeObject is NavigationWorldAsset selectedWorld)
            {
                _world = selectedWorld;
            }

            AreafinderAuthoringSession.SetWorld(_world);
        }

        private void OnDisable()
        {
            AreafinderAuthoringSession.Changed -= OnSessionChanged;
        }

        private void OnSelectionChange()
        {
            switch (Selection.activeObject)
            {
                case NavigationWorldAsset selectedWorld:
                    _world = selectedWorld;
                    _bake = null;
                    AreafinderAuthoringSession.SetWorld(_world);
                    break;
                case NavigationAreaAsset selectedArea when ContainsArea(_world, selectedArea):
                    AreafinderAuthoringSession.SetArea(selectedArea);
                    break;
                case NavigationBakeAsset selectedBake:
                    _bake = selectedBake;
                    break;
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (AreafinderAuthoringSession.World != _world)
            {
                AreafinderAuthoringSession.SetWorld(_world);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawWorldSelector();
            if (_world != null)
            {
                DrawModeToolbar();
                DrawWorldSettings();
                DrawAreaControls();
                DrawEdgePairControls();
                DrawBakeControls();
                DrawPolicyPreview();
                DrawValidationIssues();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawWorldSelector()
        {
            EditorGUILayout.LabelField("Navigation World", EditorStyles.boldLabel);
            var selected = (NavigationWorldAsset)EditorGUILayout.ObjectField(
                "World",
                _world,
                typeof(NavigationWorldAsset),
                false);
            if (selected != _world)
            {
                _world = selected;
                _bake = null;
                AreafinderAuthoringSession.SetWorld(_world);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create World"))
                {
                    CreateWorld();
                }

                using (new EditorGUI.DisabledScope(_world == null))
                {
                    if (GUILayout.Button("Ping Asset"))
                    {
                        EditorGUIUtility.PingObject(_world);
                    }
                }
            }

            EditorGUILayout.Space();
        }

        private static void DrawModeToolbar()
        {
            AreafinderAuthoringMode mode = AreafinderAuthoringSession.Mode;
            int selected = GUILayout.Toolbar((int)mode, ModeNames);
            if (selected != (int)mode)
            {
                AreafinderAuthoringSession.SetMode((AreafinderAuthoringMode)selected);
            }

            if (GUILayout.Button("Activate Scene Tool"))
            {
                ToolManager.SetActiveTool<AreafinderSceneTool>();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
        }

        private void DrawWorldSettings()
        {
            SemanticRegistryAsset projectRegistry = SemanticRegistryProjectSettings.Registry;
            if (_world.SemanticRegistry == null)
            {
                EditorGUILayout.HelpBox("This world has no semantic registry.", MessageType.Error);
                using (new EditorGUI.DisabledScope(projectRegistry == null))
                {
                    if (GUILayout.Button("Assign Project Registry"))
                    {
                        ChangeWorld("Assign Areafinder Registry", () => _world.SetSemanticRegistry(projectRegistry));
                    }
                }
            }
            else if (projectRegistry != null && _world.SemanticRegistry != projectRegistry)
            {
                EditorGUILayout.HelpBox(
                    "This world does not use the registry selected in Project Settings > Areafinder.",
                    MessageType.Warning);
            }

            _showInference = EditorGUILayout.Foldout(_showInference, "Adjacency Inference", true);
            if (_showInference)
            {
                DrawInferenceSettings();
            }

            EditorGUILayout.Space();
        }

        private void DrawInferenceSettings()
        {
            AdjacencyInferenceSettings current = _world.InferenceSettings;
            EditorGUI.BeginChangeCheck();
            float position = EditorGUILayout.FloatField("Position tolerance", current.PositionTolerance);
            float overlap = EditorGUILayout.FloatField("Minimum overlap", current.MinimumOverlap);
            float height = EditorGUILayout.FloatField("Height tolerance", current.HeightTolerance);
            float angle = EditorGUILayout.FloatField("Normal angle", current.NormalAngleTolerance);
            float planarity = EditorGUILayout.FloatField("Planarity tolerance", current.PlanarityTolerance);
            float forcedGap = EditorGUILayout.FloatField("Maximum forced gap", current.MaximumForcedGap);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            var settings = new AdjacencyInferenceSettings(position, overlap, height, angle, planarity, forcedGap);
            if (settings.IsValid)
            {
                ChangeWorld("Edit Areafinder Inference Settings", () => _world.SetInferenceSettings(settings));
            }
            else
            {
                SetMessage("Inference tolerances must be finite, nonnegative, and use an angle no greater than 180 degrees.", MessageType.Error);
            }
        }

        private void DrawAreaControls()
        {
            EditorGUILayout.LabelField("Area", EditorStyles.boldLabel);
            NavigationAreaAsset area = AreafinderAuthoringSession.Area;
            int currentIndex = IndexOfArea(_world, area);
            string[] names = BuildAreaNames(_world);
            int selectedIndex = EditorGUILayout.Popup("Active area", currentIndex + 1, names);
            NavigationAreaAsset selectedArea = selectedIndex == 0 ? null : _world.Areas[selectedIndex - 1];
            if (selectedArea != area)
            {
                AreafinderAuthoringSession.SetArea(selectedArea);
                area = selectedArea;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create & Add Area"))
                {
                    CreateArea();
                    area = AreafinderAuthoringSession.Area;
                }

                using (new EditorGUI.DisabledScope(area == null))
                {
                    if (GUILayout.Button("Remove Reference"))
                    {
                        RemoveArea(area);
                        area = null;
                    }
                }
            }

            if (area == null)
            {
                EditorGUILayout.HelpBox("Add an Area asset to author local polygons.", MessageType.Info);
                return;
            }

            _showFrame = EditorGUILayout.Foldout(_showFrame, "Double-precision Area frame", true);
            if (_showFrame)
            {
                DrawAreaFrame(area);
            }

            DrawPolygonControls(area);
            DrawAreaSemantics(area);
            EditorGUILayout.Space();
        }

        private void DrawAreaFrame(NavigationAreaAsset area)
        {
            AreaFrame frame = area.Frame;
            EditorGUI.BeginChangeCheck();
            double x = EditorGUILayout.DoubleField("Universe X", frame.UniverseOrigin.X);
            double y = EditorGUILayout.DoubleField("Universe Y", frame.UniverseOrigin.Y);
            double z = EditorGUILayout.DoubleField("Universe Z", frame.UniverseOrigin.Z);
            Vector3 euler = EditorGUILayout.Vector3Field("Rotation", frame.Rotation.eulerAngles);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            try
            {
                ChangeArea(area, "Edit Areafinder Area Frame", () =>
                    area.SetFrame(new AreaFrame(new Double3(x, y, z), Quaternion.Euler(euler))));
            }
            catch (ArgumentException exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        private void DrawPolygonControls(NavigationAreaAsset area)
        {
            EditorGUILayout.LabelField("Polygons", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add 2m Quad"))
                {
                    ChangeArea(area, "Add Areafinder Polygon", () =>
                    {
                        NavigationPolygonRecord polygon = area.AddPolygon(new[]
                        {
                            new Vector3(-1f, 0f, -1f),
                            new Vector3(-1f, 0f, 1f),
                            new Vector3(1f, 0f, 1f),
                            new Vector3(1f, 0f, -1f)
                        });
                        AreafinderAuthoringSession.SelectPolygon(polygon.Id);
                    });
                }

                using (new EditorGUI.DisabledScope(!AreafinderAuthoringSession.TryGetSelectedPolygon(out _)))
                {
                    if (GUILayout.Button("Delete Selected"))
                    {
                        PolygonId id = AreafinderAuthoringSession.PolygonId;
                        ChangeArea(area, "Delete Areafinder Polygon", () => area.RemovePolygon(id));
                        AreafinderAuthoringSession.SelectPolygon(default);
                    }
                }
            }

            DrawPolygonPicker(area);
            if (!AreafinderAuthoringSession.TryGetSelectedPolygon(out NavigationPolygonRecord selectedPolygon))
            {
                return;
            }

            bool enabled = EditorGUILayout.Toggle("Enabled", selectedPolygon.Enabled);
            if (enabled != selectedPolygon.Enabled)
            {
                ChangeArea(area, "Toggle Areafinder Polygon", () =>
                {
                    selectedPolygon.SetEnabled(enabled);
                    area.Touch();
                });
            }

            EditorGUILayout.LabelField(
                $"Vertices: {selectedPolygon.Vertices.Count}    ID: {ShortId(selectedPolygon.Id.ToString())}",
                EditorStyles.miniLabel);

            int edgeIndex = AreafinderAuthoringSession.SelectedEdgeIndex;
            using (new EditorGUI.DisabledScope(edgeIndex < 0 || edgeIndex >= selectedPolygon.Vertices.Count))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Insert Vertex on Edge"))
                    {
                        Vector3 start = selectedPolygon.Vertices[edgeIndex].Position;
                        Vector3 end = selectedPolygon.Vertices[(edgeIndex + 1) % selectedPolygon.Vertices.Count].Position;
                        ChangeArea(area, "Insert Areafinder Vertex", () =>
                        {
                            selectedPolygon.InsertVertex(edgeIndex, (start + end) * 0.5f);
                            area.Touch();
                        });
                    }

                    using (new EditorGUI.DisabledScope(selectedPolygon.Vertices.Count <= 3))
                    {
                        if (GUILayout.Button("Remove Vertex"))
                        {
                            int removeIndex = AreafinderAuthoringSession.SelectedVertexIndex >= 0
                                ? AreafinderAuthoringSession.SelectedVertexIndex
                                : edgeIndex;
                            ChangeArea(area, "Remove Areafinder Vertex", () =>
                            {
                                selectedPolygon.RemoveVertex(removeIndex);
                                area.Touch();
                            });
                        }
                    }
                }
            }

            DrawSplitAndMergeControls(area, selectedPolygon);
        }

        private void DrawSplitAndMergeControls(
            NavigationAreaAsset area,
            NavigationPolygonRecord selectedPolygon)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Topology edits", EditorStyles.boldLabel);
            int maximumVertex = Math.Max(0, selectedPolygon.Vertices.Count - 1);
            _splitFirstVertex = EditorGUILayout.IntSlider("Split vertex A", _splitFirstVertex, 0, maximumVertex);
            _splitSecondVertex = EditorGUILayout.IntSlider("Split vertex B", _splitSecondVertex, 0, maximumVertex);
            using (new EditorGUI.DisabledScope(selectedPolygon.Vertices.Count < 4))
            {
                if (GUILayout.Button("Split Along Selected Diagonal"))
                {
                    bool split = false;
                    NavigationPolygonRecord preserved = null;
                    ChangeArea(area, "Split Areafinder Polygon", () =>
                        split = area.SplitPolygon(
                            selectedPolygon.Id,
                            _splitFirstVertex,
                            _splitSecondVertex,
                            out preserved,
                            out _));
                    if (split)
                    {
                        AreafinderAuthoringSession.SelectPolygon(preserved.Id);
                        SetMessage(
                            "Polygon split. Attachments to replaced edge identities remain visible as validation errors.",
                            MessageType.Info);
                    }
                    else
                    {
                        SetMessage("Choose two non-adjacent vertices to split this convex polygon.", MessageType.Error);
                    }
                }
            }

            string[] polygonNames = new string[area.Polygons.Count + 1];
            polygonNames[0] = "None";
            for (int index = 0; index < area.Polygons.Count; index++)
            {
                NavigationPolygonRecord candidate = area.Polygons[index];
                polygonNames[index + 1] = candidate == null
                    ? $"{index}: [null]"
                    : $"{index}: {ShortId(candidate.Id.ToString())}";
            }

            _mergePolygonIndex = EditorGUILayout.Popup("Merge with", _mergePolygonIndex + 1, polygonNames) - 1;
            bool validTarget = _mergePolygonIndex >= 0 && _mergePolygonIndex < area.Polygons.Count &&
                               area.Polygons[_mergePolygonIndex] != null &&
                               area.Polygons[_mergePolygonIndex].Id != selectedPolygon.Id;
            using (new EditorGUI.DisabledScope(!validTarget))
            {
                if (GUILayout.Button("Merge Compatible Polygons"))
                {
                    NavigationPolygonRecord other = area.Polygons[_mergePolygonIndex];
                    bool merged = false;
                    NavigationPolygonRecord result = null;
                    ChangeArea(area, "Merge Areafinder Polygons", () =>
                        merged = area.MergePolygons(
                            selectedPolygon.Id,
                            other.Id,
                            _world.InferenceSettings.PositionTolerance,
                            out result));
                    if (merged)
                    {
                        AreafinderAuthoringSession.SelectPolygon(result.Id);
                        _mergePolygonIndex = -1;
                        SetMessage(
                            "Polygons merged. Attachments to replaced edge identities remain visible as validation errors.",
                            MessageType.Info);
                    }
                    else
                    {
                        SetMessage(
                            "Polygons must share one complete reversed edge, produce a convex outline, and have matching semantics, capabilities, and enabled state.",
                            MessageType.Error);
                    }
                }
            }
        }

        private static void DrawPolygonPicker(NavigationAreaAsset area)
        {
            string[] names = new string[area.Polygons.Count + 1];
            names[0] = "None";
            int selected = 0;
            for (int index = 0; index < area.Polygons.Count; index++)
            {
                NavigationPolygonRecord polygon = area.Polygons[index];
                names[index + 1] = polygon == null ? $"{index}: [null]" : $"{index}: {ShortId(polygon.Id.ToString())}";
                if (polygon != null && polygon.Id == AreafinderAuthoringSession.PolygonId)
                {
                    selected = index + 1;
                }
            }

            int next = EditorGUILayout.Popup("Selected polygon", selected, names);
            if (next != selected)
            {
                NavigationPolygonRecord polygon = next == 0 ? null : area.Polygons[next - 1];
                AreafinderAuthoringSession.SelectPolygon(polygon?.Id ?? default);
            }
        }

        private void DrawAreaSemantics(NavigationAreaAsset area)
        {
            SemanticRegistryAsset registry = _world.SemanticRegistry;
            if (registry == null || !AreafinderAuthoringSession.TryGetSelectedPolygon(out NavigationPolygonRecord polygon))
            {
                return;
            }

            _showSemantics = EditorGUILayout.Foldout(_showSemantics, "Selected polygon semantics", true);
            if (!_showSemantics)
            {
                return;
            }

            SemanticMask mask = polygon.Semantics;
            int activeSlot = AreafinderAuthoringSession.ActiveSemanticSlot;
            for (int index = 0; index < registry.Definitions.Count; index++)
            {
                SemanticDefinition definition = registry.Definitions[index];
                if (definition == null || definition.IsDeleted)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool value = mask.Contains(definition.Slot);
                    bool next = EditorGUILayout.ToggleLeft(
                        $"{definition.Slot}: {definition.DisplayName}",
                        value);
                    if (next != value)
                    {
                        mask.Set(definition.Slot, next);
                        ChangeArea(area, "Paint Areafinder Semantic", () =>
                        {
                            polygon.SetSemantics(mask);
                            area.Touch();
                        });
                    }

                    bool isActive = activeSlot == definition.Slot;
                    if (GUILayout.Toggle(isActive, "Paint", EditorStyles.miniButton, GUILayout.Width(48f)) != isActive)
                    {
                        AreafinderAuthoringSession.SetActiveSemanticSlot(definition.Slot);
                    }
                }
            }
        }

        private void DrawEdgePairControls()
        {
            AreafinderAuthoringMode mode = AreafinderAuthoringSession.Mode;
            if (mode != AreafinderAuthoringMode.Edge && mode != AreafinderAuthoringMode.Portal)
            {
                return;
            }

            EditorGUILayout.LabelField(mode == AreafinderAuthoringMode.Edge
                ? "Adjacency Override"
                : "Portal Attachment", EditorStyles.boldLabel);
            AuthoringEdgeSelection first = AreafinderAuthoringSession.FirstEdge;
            AuthoringEdgeSelection second = AreafinderAuthoringSession.SecondEdge;
            EditorGUILayout.LabelField("First", DescribeEdge(first));
            EditorGUILayout.LabelField("Second", DescribeEdge(second));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!first.IsValid && !second.IsValid))
                {
                    if (GUILayout.Button("Clear Selection"))
                    {
                        AreafinderAuthoringSession.ResetEdgePair();
                        Repaint();
                        SceneView.RepaintAll();
                    }
                }

                bool pairValid = first.IsValid && second.IsValid;
                using (new EditorGUI.DisabledScope(!pairValid))
                {
                    if (mode == AreafinderAuthoringMode.Edge)
                    {
                        DrawAdjacencyButtons(first, second);
                    }
                    else if (GUILayout.Button("Create Bidirectional Portal"))
                    {
                        CreatePortal(first, second);
                    }
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawAdjacencyButtons(AuthoringEdgeSelection first, AuthoringEdgeSelection second)
        {
            bool compatible = first.Area == second.Area && first.PolygonId != second.PolygonId;
            using (new EditorGUI.DisabledScope(!compatible))
            {
                if (GUILayout.Button("Auto"))
                {
                    SetAdjacencyOverride(first, second, AdjacencyOverrideState.Automatic);
                }

                if (GUILayout.Button("Connect"))
                {
                    SetAdjacencyOverride(first, second, AdjacencyOverrideState.ForcedConnected);
                }

                if (GUILayout.Button("Disconnect"))
                {
                    SetAdjacencyOverride(first, second, AdjacencyOverrideState.ForcedDisconnected);
                }
            }
        }

        private void DrawBakeControls()
        {
            EditorGUILayout.LabelField("Validation & Bake", EditorStyles.boldLabel);
            _bake = (NavigationBakeAsset)EditorGUILayout.ObjectField(
                "Bake asset",
                _bake,
                typeof(NavigationBakeAsset),
                false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Bake Asset"))
                {
                    CreateBake();
                }

                if (GUILayout.Button("Validate"))
                {
                    AreafinderAuthoringSession.RefreshValidation();
                    SetMessage(
                        AreafinderAuthoringSession.ValidationIssues.Count == 0
                            ? "Validation passed."
                            : $"Validation found {AreafinderAuthoringSession.ValidationIssues.Count} issue(s).",
                        HasErrors() ? MessageType.Error : MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(_bake == null))
                {
                    if (GUILayout.Button("Bake"))
                    {
                        Bake();
                    }
                }
            }

            if (_bake != null)
            {
                string status = !_bake.IsUsable ? "Not baked" : _bake.IsStale(_world) ? "Stale" : "Current";
                EditorGUILayout.LabelField("Status", status);
            }

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, _messageType);
            }

            EditorGUILayout.Space();
        }

        private void DrawPolicyPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Agent-policy preview", EditorStyles.boldLabel);
            _previewPolicy = (TraversalPolicyAsset)EditorGUILayout.ObjectField(
                "Policy",
                _previewPolicy,
                typeof(TraversalPolicyAsset),
                false);
            _previewStartArea = (NavigationAreaAsset)EditorGUILayout.ObjectField(
                "Start Area",
                _previewStartArea,
                typeof(NavigationAreaAsset),
                false);
            _previewStart = EditorGUILayout.Vector3Field("Start local position", _previewStart);
            _previewGoalArea = (NavigationAreaAsset)EditorGUILayout.ObjectField(
                "Goal Area",
                _previewGoalArea,
                typeof(NavigationAreaAsset),
                false);
            _previewGoal = EditorGUILayout.Vector3Field("Goal local position", _previewGoal);

            bool validSelection = _previewPolicy != null &&
                                  ContainsArea(_world, _previewStartArea) &&
                                  ContainsArea(_world, _previewGoalArea);
            using (new EditorGUI.DisabledScope(!validSelection))
            {
                if (GUILayout.Button("Preview This Agent's Route"))
                {
                    PreviewPolicyRoute();
                }
            }

            if (_previewPolicy != null && !ContainsPolicy(_world, _previewPolicy))
            {
                EditorGUILayout.HelpBox(
                    "This policy is not yet tracked by the world fingerprint.",
                    MessageType.Warning);
                if (GUILayout.Button("Add Policy to World"))
                {
                    ChangeWorld("Add Areafinder Policy", () => _world.AddPolicy(_previewPolicy));
                }
            }

            NavigationPath path = AreafinderAuthoringSession.PreviewPath;
            if (path != null)
            {
                EditorGUILayout.LabelField($"Total cost: {path.TotalCost:0.###}", EditorStyles.boldLabel);
                for (int index = 0; index < path.Areas.Count; index++)
                {
                    NavigationAreaSegment segment = path.Areas[index];
                    EditorGUILayout.LabelField(
                        $"Area {ShortId(segment.AreaId.ToString())}: {segment.Cost:0.###} " +
                        $"({segment.PolygonCount} polygon(s))",
                        EditorStyles.miniLabel);
                    if (index < path.PortalTransitions.Count)
                    {
                        NavigationPortalTransition portal = path.PortalTransitions[index];
                        EditorGUILayout.LabelField(
                            $"  Portal {ShortId(portal.PortalId.ToString())}: {portal.Cost:0.###}",
                            EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void PreviewPolicyRoute()
        {
            if (_bake == null || _bake.Source != _world || !_bake.IsUsable)
            {
                SetMessage("Select or create a current bake before previewing a policy.", MessageType.Error);
                AreafinderAuthoringSession.ClearPathPreview();
                return;
            }

            if (!CompiledTraversalPolicy.TryCompile(
                    _previewPolicy,
                    _bake,
                    out CompiledTraversalPolicy policy,
                    out string error))
            {
                SetMessage(error, MessageType.Error);
                AreafinderAuthoringSession.ClearPathPreview();
                return;
            }

            using (var world = new NavigationWorld(_bake))
            {
                PathRequestHandle request = world.Submit(new PathQuery(
                    new NavigationLocation(_previewStartArea.Id, _previewStart),
                    new NavigationLocation(_previewGoalArea.Id, _previewGoal),
                    policy));
                for (int tick = 0; tick < 8 && !IsTerminal(world.GetStatus(request)); tick++)
                {
                    world.Tick(256);
                }

                if (!world.TryGetPath(request, out NavigationPathView view))
                {
                    world.TryGetFailure(request, out PathFailureReason failure);
                    SetMessage(
                        $"Policy preview rejected or could not route these endpoints: {failure}.",
                        MessageType.Warning);
                    AreafinderAuthoringSession.SetPathPreview(null, $"Preview: {failure}");
                    return;
                }

                NavigationPath path = view.ToManagedCopy();
                AreafinderAuthoringSession.SetPathPreview(
                    path,
                    $"Preview: {path.Areas.Count} Area(s), {path.PortalTransitions.Count} Portal(s), cost {path.TotalCost:0.###}");
                SetMessage(
                    "Policy preview uses the same compiled policy, global routing, local solver, and cost composition as runtime.",
                    MessageType.Info);
            }
        }

        private static void DrawValidationIssues()
        {
            IReadOnlyList<NavigationValidationIssue> issues = AreafinderAuthoringSession.ValidationIssues;
            if (issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("Validation Issues", EditorStyles.boldLabel);
            for (int index = 0; index < issues.Count; index++)
            {
                NavigationValidationIssue issue = issues[index];
                MessageType type = issue.Severity == NavigationValidationSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning;
                string measurements = issue.AllowedValue != 0d || issue.MeasuredValue != 0d
                    ? $"\nMeasured {issue.MeasuredValue:G5}; allowed {issue.AllowedValue:G5}."
                    : string.Empty;
                EditorGUILayout.HelpBox(
                    $"{issue.Code} [{ShortId(issue.TargetId)}]\n{issue.Message}{measurements}",
                    type);
            }
        }

        private void CreateWorld()
        {
            string path = SaveAssetPath("Create Areafinder Navigation World", "Areafinder World");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var world = CreateInstance<NavigationWorldAsset>();
            world.SetSemanticRegistry(SemanticRegistryProjectSettings.Registry);
            AssetDatabase.CreateAsset(world, path);
            Undo.RegisterCreatedObjectUndo(world, "Create Areafinder World");
            _world = world;
            _bake = null;
            Selection.activeObject = world;
            AreafinderAuthoringSession.SetWorld(world);
        }

        private void CreateArea()
        {
            string path = SaveAssetPath("Create Areafinder Navigation Area", "Areafinder Area");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var area = CreateInstance<NavigationAreaAsset>();
            AssetDatabase.CreateAsset(area, path);
            Undo.RegisterCreatedObjectUndo(area, "Create Areafinder Area");
            ChangeWorld("Add Areafinder Area", () => _world.AddArea(area));
            Selection.activeObject = area;
            AreafinderAuthoringSession.SetArea(area);
        }

        private void RemoveArea(NavigationAreaAsset area)
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove Area reference?",
                    "The Area asset will remain on disk. Portal references are preserved and will be reported as validation errors.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            ChangeWorld("Remove Areafinder Area", () => _world.RemoveArea(area));
            AreafinderAuthoringSession.SetArea(null);
        }

        private void CreateBake()
        {
            string path = SaveAssetPath("Create Areafinder Navigation Bake", $"{_world.name} Bake");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _bake = CreateInstance<NavigationBakeAsset>();
            AssetDatabase.CreateAsset(_bake, path);
            Undo.RegisterCreatedObjectUndo(_bake, "Create Areafinder Bake");
            Selection.activeObject = _bake;
        }

        private void Bake()
        {
            Undo.RegisterCompleteObjectUndo(_bake, "Bake Areafinder World");
            NavigationBakeResult result = NavigationBaker.Bake(_world, _bake);
            AreafinderAuthoringSession.RefreshValidation();
            if (result.Succeeded)
            {
                EditorUtility.SetDirty(_bake);
                AssetDatabase.SaveAssetIfDirty(_bake);
                SetMessage($"Bake completed: {_bake.Polygons.Count} polygons, {_bake.Portals.Count} portals.", MessageType.Info);
            }
            else
            {
                SetMessage($"Bake blocked by {CountErrors(result.Issues)} validation error(s). Previous bake bytes were left untouched.", MessageType.Error);
            }
        }

        private void CreatePortal(AuthoringEdgeSelection first, AuthoringEdgeSelection second)
        {
            if (first.Area == second.Area)
            {
                SetMessage("A Portal must connect two different Areas. Use adjacency inside one Area.", MessageType.Error);
                return;
            }

            if (!ContainsArea(_world, first.Area) || !ContainsArea(_world, second.Area) ||
                !TryCreateSpan(first, out PortalEntrySpan source) ||
                !TryCreateSpan(second, out PortalEntrySpan destination))
            {
                SetMessage("The selected Portal edges no longer resolve in this world.", MessageType.Error);
                return;
            }

            PortalTransform transform = BuildPortalTransform(first.Area, source, second.Area, destination);
            ChangeWorld("Create Areafinder Portal", () =>
                _world.AddPortal(source, destination, PortalDirection.Bidirectional, 0d, transform));
            AreafinderAuthoringSession.ResetEdgePair();
            SetMessage("Created a bidirectional Portal. Validation will flag incompatible spans or transforms.", MessageType.Info);
        }

        private void SetAdjacencyOverride(
            AuthoringEdgeSelection first,
            AuthoringEdgeSelection second,
            AdjacencyOverrideState state)
        {
            if (first.Area != second.Area || !first.TryResolve(out NavigationPolygonRecord firstPolygon, out NavigationVertexRecord firstVertex) ||
                !second.TryResolve(out NavigationPolygonRecord secondPolygon, out NavigationVertexRecord secondVertex))
            {
                SetMessage("Adjacency overrides require two valid edges from different polygons in the same Area.", MessageType.Error);
                return;
            }

            var firstReference = new PolygonEdgeReference(firstPolygon.Id, firstVertex.OutgoingEdgeId);
            var secondReference = new PolygonEdgeReference(secondPolygon.Id, secondVertex.OutgoingEdgeId);
            NavigationAreaAsset area = first.Area;
            ChangeArea(area, "Set Areafinder Adjacency Override", () =>
            {
                var retained = new List<AdjacencyOverrideRecord>();
                for (int index = 0; index < area.AdjacencyOverrides.Count; index++)
                {
                    AdjacencyOverrideRecord existing = area.AdjacencyOverrides[index];
                    if (existing != null && !SameEdgePair(existing.First, existing.Second, firstReference, secondReference))
                    {
                        retained.Add(existing);
                    }
                }

                area.ClearAdjacencyOverrides();
                for (int index = 0; index < retained.Count; index++)
                {
                    area.AddAdjacencyOverride(retained[index]);
                }

                if (state != AdjacencyOverrideState.Automatic)
                {
                    area.AddAdjacencyOverride(new AdjacencyOverrideRecord(firstReference, secondReference, state));
                }
            });
            AreafinderAuthoringSession.ResetEdgePair();
        }

        private void ChangeWorld(string undoName, Action mutation)
        {
            Undo.RegisterCompleteObjectUndo(_world, undoName);
            mutation();
            EditorUtility.SetDirty(_world);
            AreafinderAuthoringSession.NotifyAuthoringChanged();
        }

        private static void ChangeArea(NavigationAreaAsset area, string undoName, Action mutation)
        {
            Undo.RegisterCompleteObjectUndo(area, undoName);
            mutation();
            EditorUtility.SetDirty(area);
            AreafinderAuthoringSession.NotifyAuthoringChanged();
        }

        private void OnSessionChanged()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        private bool HasErrors()
        {
            IReadOnlyList<NavigationValidationIssue> issues = AreafinderAuthoringSession.ValidationIssues;
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].Severity == NavigationValidationSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountErrors(IReadOnlyList<NavigationValidationIssue> issues)
        {
            int count = 0;
            for (int index = 0; index < issues.Count; index++)
            {
                if (issues[index].Severity == NavigationValidationSeverity.Error)
                {
                    count++;
                }
            }

            return count;
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }

        private static bool TryCreateSpan(AuthoringEdgeSelection selection, out PortalEntrySpan span)
        {
            span = default;
            if (!selection.TryResolve(out NavigationPolygonRecord polygon, out NavigationVertexRecord start))
            {
                return false;
            }

            NavigationVertexRecord end = polygon.Vertices[(selection.EdgeIndex + 1) % polygon.Vertices.Count];
            span = new PortalEntrySpan(
                selection.Area.Id,
                polygon.Id,
                start.OutgoingEdgeId,
                start.Position,
                end.Position);
            return true;
        }

        private static PortalTransform BuildPortalTransform(
            NavigationAreaAsset sourceArea,
            PortalEntrySpan source,
            NavigationAreaAsset destinationArea,
            PortalEntrySpan destination)
        {
            Double3 sourceStart = sourceArea.Frame.ToUniverse(source.Start);
            Double3 sourceEnd = sourceArea.Frame.ToUniverse(source.End);
            Double3 destinationStart = destinationArea.Frame.ToUniverse(destination.Start);
            Double3 destinationEnd = destinationArea.Frame.ToUniverse(destination.End);
            Vector3 sourceDirection = ToVector3(sourceEnd - sourceStart).normalized;
            Vector3 destinationDirection = ToVector3(destinationEnd - destinationStart).normalized;
            Quaternion rotation = sourceDirection.sqrMagnitude > 0f && destinationDirection.sqrMagnitude > 0f
                ? Quaternion.FromToRotation(sourceDirection, destinationDirection)
                : Quaternion.identity;
            Double3 sourceMidpoint = (sourceStart + sourceEnd) * 0.5d;
            Double3 destinationMidpoint = (destinationStart + destinationEnd) * 0.5d;
            Double3 translation = destinationMidpoint - Double3.Rotate(rotation, sourceMidpoint);
            return new PortalTransform(translation, rotation);
        }

        private static Vector3 ToVector3(Double3 value)
        {
            return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        private static bool SameEdgePair(
            PolygonEdgeReference leftFirst,
            PolygonEdgeReference leftSecond,
            PolygonEdgeReference rightFirst,
            PolygonEdgeReference rightSecond)
        {
            return (leftFirst.Equals(rightFirst) && leftSecond.Equals(rightSecond)) ||
                   (leftFirst.Equals(rightSecond) && leftSecond.Equals(rightFirst));
        }

        private static string DescribeEdge(AuthoringEdgeSelection selection)
        {
            if (!selection.TryResolve(out NavigationPolygonRecord polygon, out NavigationVertexRecord vertex))
            {
                return "None";
            }

            return $"{selection.Area.name} / {ShortId(polygon.Id.ToString())} / {ShortId(vertex.OutgoingEdgeId.ToString())}";
        }

        private static bool ContainsArea(NavigationWorldAsset world, NavigationAreaAsset area)
        {
            return world != null && IndexOfArea(world, area) >= 0;
        }

        private static bool ContainsPolicy(NavigationWorldAsset world, TraversalPolicyAsset policy)
        {
            if (world == null || policy == null)
            {
                return false;
            }

            for (int index = 0; index < world.Policies.Count; index++)
            {
                if (world.Policies[index] == policy)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTerminal(PathRequestStatus status)
        {
            return status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
                   status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale;
        }

        private static int IndexOfArea(NavigationWorldAsset world, NavigationAreaAsset area)
        {
            if (world == null || area == null)
            {
                return -1;
            }

            for (int index = 0; index < world.Areas.Count; index++)
            {
                if (world.Areas[index] == area)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string[] BuildAreaNames(NavigationWorldAsset world)
        {
            string[] names = new string[world.Areas.Count + 1];
            names[0] = "None";
            for (int index = 0; index < world.Areas.Count; index++)
            {
                NavigationAreaAsset area = world.Areas[index];
                names[index + 1] = area == null ? $"{index}: [null]" : area.name;
            }

            return names;
        }

        private static string SaveAssetPath(string title, string defaultName)
        {
            return EditorUtility.SaveFilePanelInProject(
                title,
                defaultName,
                "asset",
                "Choose where to save the asset.");
        }

        private static string ShortId(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);
        }
    }
}
