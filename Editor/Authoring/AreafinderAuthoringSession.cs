using System;
using System.Collections.Generic;
using UnityEditor;

namespace NotRealGames.Areafinder.Editor
{
    internal enum AreafinderAuthoringMode
    {
        Vertex,
        Edge,
        Polygon,
        Portal,
        Semantic
    }

    internal readonly struct AuthoringEdgeSelection : IEquatable<AuthoringEdgeSelection>
    {
        internal AuthoringEdgeSelection(NavigationAreaAsset area, PolygonId polygonId, int edgeIndex)
        {
            Area = area;
            PolygonId = polygonId;
            EdgeIndex = edgeIndex;
        }

        internal NavigationAreaAsset Area { get; }
        internal PolygonId PolygonId { get; }
        internal int EdgeIndex { get; }
        internal bool IsValid => Area != null && PolygonId.IsValid && EdgeIndex >= 0;

        public bool Equals(AuthoringEdgeSelection other)
        {
            return Area == other.Area && PolygonId == other.PolygonId && EdgeIndex == other.EdgeIndex;
        }

        public override bool Equals(object obj) => obj is AuthoringEdgeSelection other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Area != null ? Area.GetHashCode() : 0;
                hash = (hash * 397) ^ PolygonId.GetHashCode();
                return (hash * 397) ^ EdgeIndex;
            }
        }

        internal bool TryResolve(out NavigationPolygonRecord polygon, out NavigationVertexRecord vertex)
        {
            polygon = null;
            vertex = null;
            if (!IsValid || !Area.TryGetPolygon(PolygonId, out polygon) ||
                (uint)EdgeIndex >= (uint)polygon.Vertices.Count)
            {
                return false;
            }

            vertex = polygon.Vertices[EdgeIndex];
            return vertex != null;
        }
    }

    internal static class AreafinderAuthoringSession
    {
        private static readonly List<NavigationValidationIssue> Issues = new List<NavigationValidationIssue>();
        private static readonly HashSet<string> ErrorTargets = new HashSet<string>();
        private static readonly List<CompiledAdjacencyRecord> CompiledAdjacencies =
            new List<CompiledAdjacencyRecord>();
        private static NavigationWorldAsset _world;
        private static NavigationAreaAsset _area;
        private static PolygonId _polygonId;
        private static int _selectedVertexIndex = -1;
        private static int _selectedEdgeIndex = -1;
        private static AreafinderAuthoringMode _mode = AreafinderAuthoringMode.Polygon;
        private static int _activeSemanticSlot = -1;
        private static AuthoringEdgeSelection _firstEdge;
        private static AuthoringEdgeSelection _secondEdge;
        private static NavigationPath _previewPath;
        private static string _previewSummary;

        static AreafinderAuthoringSession()
        {
            Undo.undoRedoPerformed += NotifyAuthoringChanged;
        }

        internal static event Action Changed;

        internal static NavigationWorldAsset World => _world;
        internal static NavigationAreaAsset Area => _area;
        internal static PolygonId PolygonId => _polygonId;
        internal static int SelectedVertexIndex => _selectedVertexIndex;
        internal static int SelectedEdgeIndex => _selectedEdgeIndex;
        internal static AreafinderAuthoringMode Mode => _mode;
        internal static int ActiveSemanticSlot => _activeSemanticSlot;
        internal static AuthoringEdgeSelection FirstEdge => _firstEdge;
        internal static AuthoringEdgeSelection SecondEdge => _secondEdge;
        internal static IReadOnlyList<NavigationValidationIssue> ValidationIssues => Issues;
        internal static IReadOnlyList<CompiledAdjacencyRecord> AdjacencyPreview => CompiledAdjacencies;
        internal static NavigationPath PreviewPath => _previewPath;
        internal static string PreviewSummary => _previewSummary;

        internal static void SetWorld(NavigationWorldAsset world)
        {
            if (_world == world)
            {
                return;
            }

            _world = world;
            _area = world != null && world.Areas.Count > 0 ? world.Areas[0] : null;
            _polygonId = default;
            _selectedVertexIndex = -1;
            _selectedEdgeIndex = -1;
            ResetEdgePair();
            ClearPathPreview();
            RefreshValidation();
            RaiseChanged();
        }

        internal static void SetArea(NavigationAreaAsset area)
        {
            if (_area == area)
            {
                return;
            }

            _area = area;
            _polygonId = default;
            _selectedVertexIndex = -1;
            _selectedEdgeIndex = -1;
            if (_mode != AreafinderAuthoringMode.Portal)
            {
                ResetEdgePair();
            }

            RaiseChanged();
        }

        internal static void SetMode(AreafinderAuthoringMode mode)
        {
            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            ResetEdgePair();
            RaiseChanged();
        }

        internal static void SetActiveSemanticSlot(int slot)
        {
            _activeSemanticSlot = slot;
            RaiseChanged();
        }

        internal static void SelectPolygon(PolygonId polygonId)
        {
            _polygonId = polygonId;
            _selectedVertexIndex = -1;
            _selectedEdgeIndex = -1;
            RaiseChanged();
        }

        internal static void SelectVertex(PolygonId polygonId, int vertexIndex)
        {
            _polygonId = polygonId;
            _selectedVertexIndex = vertexIndex;
            _selectedEdgeIndex = vertexIndex;
            RaiseChanged();
        }

        internal static void SelectEdge(AuthoringEdgeSelection edge)
        {
            _area = edge.Area;
            _polygonId = edge.PolygonId;
            _selectedEdgeIndex = edge.EdgeIndex;
            _selectedVertexIndex = -1;

            if (!_firstEdge.IsValid || _secondEdge.IsValid)
            {
                _firstEdge = edge;
                _secondEdge = default;
            }
            else if (_firstEdge.Equals(edge))
            {
                ResetEdgePair();
            }
            else
            {
                _secondEdge = edge;
            }

            RaiseChanged();
        }

        internal static bool TryGetSelectedPolygon(out NavigationPolygonRecord polygon)
        {
            polygon = null;
            return _area != null && _polygonId.IsValid && _area.TryGetPolygon(_polygonId, out polygon);
        }

        internal static void ResetEdgePair()
        {
            _firstEdge = default;
            _secondEdge = default;
        }

        internal static void NotifyAuthoringChanged()
        {
            ClearPathPreview();
            RefreshValidation();
            RaiseChanged();
        }

        internal static void SetPathPreview(NavigationPath path, string summary)
        {
            _previewPath = path;
            _previewSummary = summary ?? string.Empty;
            RaiseChanged();
        }

        internal static void ClearPathPreview()
        {
            _previewPath = null;
            _previewSummary = string.Empty;
        }

        internal static bool HasValidationError(string targetId)
        {
            return !string.IsNullOrEmpty(targetId) && ErrorTargets.Contains(targetId);
        }

        internal static void RefreshValidation()
        {
            Issues.Clear();
            ErrorTargets.Clear();
            CompiledAdjacencies.Clear();
            if (_world == null)
            {
                return;
            }

            NavigationBakeResult result = NavigationBaker.Validate(_world);
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                Issues.Add(issue);
                if (issue.Severity == NavigationValidationSeverity.Error)
                {
                    ErrorTargets.Add(issue.TargetId);
                }
            }

            if (result.Succeeded)
            {
                NavigationBakeAsset preview = UnityEngine.ScriptableObject.CreateInstance<NavigationBakeAsset>();
                try
                {
                    if (NavigationBaker.Bake(_world, preview).Succeeded)
                    {
                        for (int index = 0; index < preview.Adjacencies.Count; index++)
                        {
                            CompiledAdjacencies.Add(preview.Adjacencies[index]);
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(preview);
                }
            }
        }

        private static void RaiseChanged()
        {
            Changed?.Invoke();
        }
    }
}
