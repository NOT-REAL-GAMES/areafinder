using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace NotRealGames.Areafinder.Editor
{
    [EditorTool("Areafinder Authoring")]
    internal sealed class AreafinderSceneTool : EditorTool
    {
        private GUIContent _icon;

        public override GUIContent toolbarIcon => _icon ?? (_icon = new GUIContent(
            EditorGUIUtility.IconContent("Grid.PaintTool").image,
            "Areafinder Authoring"));

        public override void OnToolGUI(UnityEditor.EditorWindow window)
        {
            if (!(window is SceneView))
            {
                return;
            }

            NavigationWorldAsset world = AreafinderAuthoringSession.World;
            if (world == null)
            {
                DrawOverlay("Open Window > Areafinder > Authoring and select a Navigation World.");
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                AreafinderAuthoringSession.ResetEdgePair();
                currentEvent.Use();
            }

            for (int areaIndex = 0; areaIndex < world.Areas.Count; areaIndex++)
            {
                NavigationAreaAsset area = world.Areas[areaIndex];
                if (area != null && area.Frame.IsValid)
                {
                    DrawArea(area);
                }
            }

            DrawOverrides(world);
            DrawInferredAdjacency(world);
            DrawPortals(world);
            DrawPathPreview(world);
            DrawOverlay(BuildOverlayText());
        }

        private static void DrawArea(NavigationAreaAsset area)
        {
            bool activeArea = area == AreafinderAuthoringSession.Area;
            for (int polygonIndex = 0; polygonIndex < area.Polygons.Count; polygonIndex++)
            {
                NavigationPolygonRecord polygon = area.Polygons[polygonIndex];
                if (polygon == null || polygon.Vertices.Count < 1)
                {
                    continue;
                }

                if (!TryGetScenePoints(area, polygon, out Vector3[] points))
                {
                    continue;
                }

                Vector3 centroid = Average(points);
                bool selected = activeArea && polygon.Id == AreafinderAuthoringSession.PolygonId;
                bool invalid = AreafinderAuthoringSession.HasValidationError(polygon.Id.ToString());
                Handles.color = PolygonColor(polygon, selected, invalid);
                if (points.Length >= 3)
                {
                    Handles.DrawAAConvexPolygon(points);
                }

                DrawBoundary(points, selected ? 4f : 2f);
                Handles.color = invalid ? Color.red : Color.white;
                Handles.Label(centroid, $"P {ShortId(polygon.Id.ToString())}");

                switch (AreafinderAuthoringSession.Mode)
                {
                    case AreafinderAuthoringMode.Polygon:
                        DrawPolygonSelector(area, polygon, centroid);
                        break;
                    case AreafinderAuthoringMode.Semantic:
                        DrawSemanticSelector(area, polygon, centroid);
                        break;
                    case AreafinderAuthoringMode.Vertex when selected:
                        DrawVertices(area, polygon, points);
                        break;
                    case AreafinderAuthoringMode.Edge when activeArea:
                    case AreafinderAuthoringMode.Portal:
                        DrawEdges(area, polygon, points);
                        break;
                }
            }
        }

        private static void DrawPolygonSelector(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            Vector3 centroid)
        {
            float size = HandleUtility.GetHandleSize(centroid) * 0.07f;
            Handles.color = Color.white;
            if (Handles.Button(centroid, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap))
            {
                AreafinderAuthoringSession.SetArea(area);
                AreafinderAuthoringSession.SelectPolygon(polygon.Id);
            }
        }

        private static void DrawSemanticSelector(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            Vector3 centroid)
        {
            int slot = AreafinderAuthoringSession.ActiveSemanticSlot;
            bool assigned = slot >= 0 && polygon.Semantics.Contains(slot);
            float size = HandleUtility.GetHandleSize(centroid) * 0.085f;
            Handles.color = assigned ? new Color(0.2f, 1f, 0.35f, 1f) : Color.gray;
            if (!Handles.Button(centroid, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap))
            {
                return;
            }

            AreafinderAuthoringSession.SetArea(area);
            AreafinderAuthoringSession.SelectPolygon(polygon.Id);
            if (slot < 0)
            {
                return;
            }

            SemanticMask mask = polygon.Semantics;
            Undo.RegisterCompleteObjectUndo(area, "Paint Areafinder Semantic");
            mask.Set(slot, !mask.Contains(slot));
            polygon.SetSemantics(mask);
            area.Touch();
            EditorUtility.SetDirty(area);
            AreafinderAuthoringSession.NotifyAuthoringChanged();
        }

        private static void DrawVertices(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            Vector3[] points)
        {
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 point = points[index];
                float size = HandleUtility.GetHandleSize(point) * 0.06f;
                bool selected = AreafinderAuthoringSession.SelectedVertexIndex == index;
                Handles.color = selected ? Color.yellow : Color.white;
                if (Handles.Button(point, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap))
                {
                    AreafinderAuthoringSession.SelectVertex(polygon.Id, index);
                }

                if (!selected)
                {
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(point, area.Frame.Rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RegisterCompleteObjectUndo(area, "Move Areafinder Vertex");
                    polygon.MoveVertex(index, area.Frame.ToLocal(ToDouble3(moved)));
                    area.Touch();
                    EditorUtility.SetDirty(area);
                    AreafinderAuthoringSession.NotifyAuthoringChanged();
                }
            }
        }

        private static void DrawEdges(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            Vector3[] points)
        {
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 midpoint = (points[index] + points[(index + 1) % points.Length]) * 0.5f;
                var edge = new AuthoringEdgeSelection(area, polygon.Id, index);
                bool first = AreafinderAuthoringSession.FirstEdge.Equals(edge);
                bool second = AreafinderAuthoringSession.SecondEdge.Equals(edge);
                Handles.color = first ? Color.yellow : second ? Color.cyan : Color.white;
                float size = HandleUtility.GetHandleSize(midpoint) * 0.055f;
                if (Handles.Button(midpoint, Quaternion.identity, size, size * 1.5f, Handles.RectangleHandleCap))
                {
                    AreafinderAuthoringSession.SelectEdge(edge);
                }
            }
        }

        private static void DrawOverrides(NavigationWorldAsset world)
        {
            for (int areaIndex = 0; areaIndex < world.Areas.Count; areaIndex++)
            {
                NavigationAreaAsset area = world.Areas[areaIndex];
                if (area == null || !area.Frame.IsValid)
                {
                    continue;
                }

                for (int index = 0; index < area.AdjacencyOverrides.Count; index++)
                {
                    AdjacencyOverrideRecord adjacency = area.AdjacencyOverrides[index];
                    if (adjacency == null || !TryGetEdgeMidpoint(area, adjacency.First, out Vector3 first) ||
                        !TryGetEdgeMidpoint(area, adjacency.Second, out Vector3 second))
                    {
                        continue;
                    }

                    Handles.color = adjacency.State == AdjacencyOverrideState.ForcedConnected
                        ? new Color(0.1f, 0.9f, 1f, 1f)
                        : new Color(1f, 0.15f, 0.15f, 1f);
                    Handles.DrawDottedLine(first, second, 4f);
                }
            }
        }

        private static void DrawPortals(NavigationWorldAsset world)
        {
            for (int index = 0; index < world.Portals.Count; index++)
            {
                NavigationPortalRecord portal = world.Portals[index];
                if (portal == null || !TryGetArea(world, portal.Source.AreaId, out NavigationAreaAsset sourceArea) ||
                    !TryGetArea(world, portal.Destination.AreaId, out NavigationAreaAsset destinationArea) ||
                    !sourceArea.Frame.IsValid || !destinationArea.Frame.IsValid)
                {
                    continue;
                }

                Vector3 source = ToScene(sourceArea, portal.Source.Midpoint);
                Vector3 destination = ToScene(destinationArea, portal.Destination.Midpoint);
                Handles.color = portal.Enabled ? new Color(1f, 0.2f, 1f, 1f) : Color.gray;
                Handles.DrawAAPolyLine(4f, source, destination);
                Handles.Label((source + destination) * 0.5f, $"Portal {ShortId(portal.Id.ToString())}");
            }
        }

        private static void DrawInferredAdjacency(NavigationWorldAsset world)
        {
            IReadOnlyList<CompiledAdjacencyRecord> adjacencies = AreafinderAuthoringSession.AdjacencyPreview;
            for (int index = 0; index < adjacencies.Count; index++)
            {
                CompiledAdjacencyRecord adjacency = adjacencies[index];
                if (adjacency.FromPolygon > adjacency.ToPolygon ||
                    !TryGetAreaForEdge(world, adjacency.FromEdgeId, out NavigationAreaAsset area))
                {
                    continue;
                }

                Vector3 start = ToScene(area, adjacency.SpanStart);
                Vector3 end = ToScene(area, adjacency.SpanEnd);
                Handles.color = adjacency.Source == AdjacencyOverrideState.ForcedConnected
                    ? new Color(0.05f, 1f, 1f, 1f)
                    : new Color(0.2f, 1f, 0.35f, 0.9f);
                Handles.DrawAAPolyLine(3f, start, end);
            }
        }

        private static void DrawPathPreview(NavigationWorldAsset world)
        {
            NavigationPath path = AreafinderAuthoringSession.PreviewPath;
            if (path == null)
            {
                return;
            }

            for (int segmentIndex = 0; segmentIndex < path.Areas.Count; segmentIndex++)
            {
                NavigationAreaSegment segment = path.Areas[segmentIndex];
                if (!TryGetArea(world, segment.AreaId, out NavigationAreaAsset area))
                {
                    continue;
                }

                var points = new Vector3[segment.SteeringCount + 1];
                points[0] = ToScene(area, segment.Start.LocalPosition);
                for (int index = 0; index < segment.SteeringCount; index++)
                {
                    points[index + 1] = ToScene(area, path.SteeringTargets[segment.SteeringStart + index]);
                }

                Handles.color = Color.yellow;
                if (points.Length > 1)
                {
                    Handles.DrawAAPolyLine(5f, points);
                }

                for (int index = 0; index < segment.CrossingCount; index++)
                {
                    NavigationCrossingSpan crossing = path.CrossingSpans[segment.CrossingStart + index];
                    Handles.color = new Color(1f, 0.55f, 0.05f, 1f);
                    Handles.DrawAAPolyLine(4f, ToScene(area, crossing.Start), ToScene(area, crossing.End));
                }
            }
        }

        private static Color PolygonColor(
            NavigationPolygonRecord polygon,
            bool selected,
            bool invalid)
        {
            if (invalid)
            {
                return new Color(1f, 0.1f, 0.1f, selected ? 0.32f : 0.18f);
            }

            if (!polygon.Enabled)
            {
                return new Color(0.4f, 0.4f, 0.4f, 0.12f);
            }

            if (AreafinderAuthoringSession.Mode == AreafinderAuthoringMode.Semantic &&
                AreafinderAuthoringSession.ActiveSemanticSlot >= 0 &&
                polygon.Semantics.Contains(AreafinderAuthoringSession.ActiveSemanticSlot))
            {
                return new Color(0.15f, 0.9f, 0.35f, selected ? 0.32f : 0.2f);
            }

            return selected
                ? new Color(1f, 0.75f, 0.1f, 0.3f)
                : new Color(0.1f, 0.65f, 1f, 0.14f);
        }

        private static void DrawBoundary(Vector3[] points, float width)
        {
            if (points.Length < 2)
            {
                return;
            }

            var closed = new Vector3[points.Length + 1];
            for (int index = 0; index < points.Length; index++)
            {
                closed[index] = points[index];
            }

            closed[points.Length] = points[0];
            Handles.DrawAAPolyLine(width, closed);
        }

        private static bool TryGetScenePoints(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            out Vector3[] points)
        {
            points = new Vector3[polygon.Vertices.Count];
            for (int index = 0; index < points.Length; index++)
            {
                NavigationVertexRecord vertex = polygon.Vertices[index];
                if (vertex == null)
                {
                    points = null;
                    return false;
                }

                points[index] = ToScene(area, vertex.Position);
            }

            return true;
        }

        private static bool TryGetEdgeMidpoint(
            NavigationAreaAsset area,
            PolygonEdgeReference reference,
            out Vector3 midpoint)
        {
            midpoint = default;
            if (!area.TryGetPolygon(reference.PolygonId, out NavigationPolygonRecord polygon))
            {
                return false;
            }

            for (int index = 0; index < polygon.Vertices.Count; index++)
            {
                NavigationVertexRecord vertex = polygon.Vertices[index];
                if (vertex != null && vertex.OutgoingEdgeId == reference.EdgeId)
                {
                    NavigationVertexRecord next = polygon.Vertices[(index + 1) % polygon.Vertices.Count];
                    if (next == null)
                    {
                        return false;
                    }

                    Vector3 local = (vertex.Position + next.Position) * 0.5f;
                    midpoint = ToScene(area, local);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetArea(NavigationWorldAsset world, AreaId id, out NavigationAreaAsset area)
        {
            for (int index = 0; index < world.Areas.Count; index++)
            {
                NavigationAreaAsset candidate = world.Areas[index];
                if (candidate != null && candidate.Id == id)
                {
                    area = candidate;
                    return true;
                }
            }

            area = null;
            return false;
        }

        private static bool TryGetAreaForEdge(
            NavigationWorldAsset world,
            EdgeId edgeId,
            out NavigationAreaAsset area)
        {
            area = null;
            for (int areaIndex = 0; areaIndex < world.Areas.Count; areaIndex++)
            {
                NavigationAreaAsset candidate = world.Areas[areaIndex];
                if (candidate == null)
                {
                    continue;
                }

                for (int polygonIndex = 0; polygonIndex < candidate.Polygons.Count; polygonIndex++)
                {
                    NavigationPolygonRecord polygon = candidate.Polygons[polygonIndex];
                    if (polygon == null)
                    {
                        continue;
                    }

                    for (int vertexIndex = 0; vertexIndex < polygon.Vertices.Count; vertexIndex++)
                    {
                        if (polygon.Vertices[vertexIndex].OutgoingEdgeId == edgeId)
                        {
                            area = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static Vector3 ToScene(NavigationAreaAsset area, Vector3 local)
        {
            Double3 universe = area.Frame.ToUniverse(local);
            return new Vector3((float)universe.X, (float)universe.Y, (float)universe.Z);
        }

        private static Double3 ToDouble3(Vector3 value)
        {
            return new Double3(value.x, value.y, value.z);
        }

        private static Vector3 Average(Vector3[] points)
        {
            Vector3 sum = Vector3.zero;
            for (int index = 0; index < points.Length; index++)
            {
                sum += points[index];
            }

            return points.Length == 0 ? sum : sum / points.Length;
        }

        private static string BuildOverlayText()
        {
            int errors = 0;
            int warnings = 0;
            for (int index = 0; index < AreafinderAuthoringSession.ValidationIssues.Count; index++)
            {
                if (AreafinderAuthoringSession.ValidationIssues[index].Severity == NavigationValidationSeverity.Error)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                }
            }

            string instruction;
            switch (AreafinderAuthoringSession.Mode)
            {
                case AreafinderAuthoringMode.Vertex:
                    instruction = "Click a vertex, then drag its position handle.";
                    break;
                case AreafinderAuthoringMode.Edge:
                    instruction = "Select two edges, then choose an override in the Authoring window.";
                    break;
                case AreafinderAuthoringMode.Portal:
                    instruction = "Select one edge in each Area, then create the Portal in the Authoring window.";
                    break;
                case AreafinderAuthoringMode.Semantic:
                    instruction = "Choose a Paint semantic in the Authoring window, then click polygons.";
                    break;
                default:
                    instruction = "Click a polygon to select it.";
                    break;
            }

            string preview = string.IsNullOrEmpty(AreafinderAuthoringSession.PreviewSummary)
                ? string.Empty
                : $"\n{AreafinderAuthoringSession.PreviewSummary}";
            return $"Areafinder — {AreafinderAuthoringSession.Mode}\n{instruction}\nValidation: {errors} error(s), {warnings} warning(s){preview}";
        }

        private static void DrawOverlay(string text)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 390f, 72f), GUI.skin.window);
            GUILayout.Label(text, EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static string ShortId(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);
        }
    }
}
