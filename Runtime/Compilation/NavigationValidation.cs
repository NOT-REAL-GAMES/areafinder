using System;

namespace NotRealGames.Areafinder
{
    public enum NavigationValidationSeverity : byte
    {
        Warning,
        Error
    }

    public enum NavigationValidationCode : byte
    {
        MissingRegistry,
        InvalidSettings,
        NullArea,
        InvalidAreaId,
        DuplicateAreaId,
        InvalidAreaFrame,
        NullPolygon,
        InvalidPolygonId,
        DuplicatePolygonId,
        TooFewVertices,
        InvalidVertexId,
        DuplicateVertexId,
        InvalidEdgeId,
        DuplicateEdgeId,
        NonFiniteVertex,
        DuplicateVertex,
        ZeroLengthEdge,
        DegeneratePolygon,
        NonPlanarPolygon,
        InvalidWinding,
        ConcavePolygon,
        SelfIntersection,
        SemanticSlotOutOfRange,
        TombstonedSemantic,
        InvalidAdjacencyOverride,
        DuplicateAdjacencyOverride,
        InvalidForcedConnection,
        OrphanPolygon,
        UnreachableIsland,
        NullPortal,
        InvalidPortalId,
        DuplicatePortalId,
        InvalidPortalArea,
        InvalidPortalPolygon,
        InvalidPortalEdge,
        InvalidPortalSpan,
        InvalidPortalCost,
        InvalidPortalTransform,
        NullPolicy,
        InvalidPolicy,
        DuplicatePolicyId
    }

    public enum NavigationValidationTargetKind : byte
    {
        World,
        Area,
        Polygon,
        Edge,
        Vertex,
        Portal,
        Semantic,
        Policy
    }

    [Serializable]
    public readonly struct NavigationValidationIssue
    {
        public NavigationValidationIssue(
            NavigationValidationSeverity severity,
            NavigationValidationCode code,
            NavigationValidationTargetKind targetKind,
            string targetId,
            string message,
            double measuredValue = 0d,
            double allowedValue = 0d)
        {
            Severity = severity;
            Code = code;
            TargetKind = targetKind;
            TargetId = targetId ?? string.Empty;
            Message = message ?? string.Empty;
            MeasuredValue = measuredValue;
            AllowedValue = allowedValue;
        }

        public NavigationValidationSeverity Severity { get; }
        public NavigationValidationCode Code { get; }
        public NavigationValidationTargetKind TargetKind { get; }
        public string TargetId { get; }
        public string Message { get; }
        public double MeasuredValue { get; }
        public double AllowedValue { get; }
    }
}
