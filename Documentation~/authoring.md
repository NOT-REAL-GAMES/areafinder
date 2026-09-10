# Authoring and baking

This document specifies the complete target 1.0 authoring model. Version 0.4 implements the source assets, stable identities, semantic registry and compaction flow, adjacency inference and overrides, structured validation, deterministic explicit baking, stale-bake guards, and an initial Undo-aware authoring window and Scene tool. Search acceleration adds no authoring format or bake-schema fields.

The current editor can create and edit polygons, insert or remove vertices, split and merge compatible cells, paint semantics, author adjacency overrides and Portals, inspect compiled links, bake, and preview a policy route. Undo/redo now refreshes validation, inferred-adjacency previews, and route previews, including after domain reload. Real AssetDatabase persistence is covered across registry, Area, world, Portal, policy, and bake data. A polished selection workflow, richer visual explanations, a repair-command library, and production-scale usability work remain before 1.0.

## Semantic registry

Each project selects one authoritative `SemanticRegistryAsset` in Areafinder Project Settings. A definition contains a persistent 128-bit ID, append-only slot, display name, optional description, and tombstone state.

Mask bit `i` is stored at word `i / 64`, bit `i % 64`. Word count is based on the highest allocated slot plus one:

```text
wordCount = (highestAllocatedSlot + 64) / 64
```

Deleting a definition preserves its slot and every serialized bit. A separately invoked compaction tool previews the entire slot remap, updates supported World, Area, Portal, and policy assets transactionally, invalidates affected bakes, and aborts rather than applying a partial result.

Definitions describe project meaning; Areafinder does not ship concepts such as sidewalks, trespassing, or police access.

## World, Areas, and Portals

A `NavigationWorldAsset` references Area assets and centrally owns stable Portal records. Replacing an Area's local polygon surface does not alter its Area or Portal identity.

Each `NavigationAreaAsset` contains:

- A persistent 128-bit Area ID.
- A double-precision universe origin and rigid rotation.
- Area-local float polygon geometry.
- Semantic and capability masks.
- Explicit adjacency overrides.

A Portal contains source and destination Areas, attached entry spans on both sides, directionality, enabled state, semantic and capability masks, nonnegative base cost, and a rigid source-to-destination rotation and translation. Scale and hard-coded transition kinds are excluded from 1.0. Games can recognize special traversal through stable Portal identity and project semantics.

## Polygon model

Version-1 polygons are convex, hole-free, Area-up 2.5D cells. Sloped surfaces are valid; walls and ceilings are not. Vertices, edges, and polygons have persistent IDs in authoring data and dense indices only in the compiled bake.

The editor must support creating and deleting polygons; adding, removing, moving, and edge-inserting vertices; convex split; compatible merge; semantic painting; Portal attachment; and adjacency inspection. Split and merge retain only identities that the operation explicitly identifies as surviving. Broken Portal attachments or overrides become validation errors and are never silently retargeted.

## Adjacency

Reversed compatible boundary edges infer a crossing span without exact floating-point equality. Project settings begin with these defaults:

| Setting | Default |
| --- | ---: |
| Position tolerance | 1 cm |
| Minimum edge overlap | 1 cm |
| Height tolerance | 5 cm |
| Surface-normal tolerance | 15° |
| Planarity tolerance | 5 mm |
| Forced-connection hard gap | 0.5 m |

Persisted override records use stable polygon and edge IDs and have three states: `Automatic`, `ForcedConnected`, and `ForcedDisconnected`. A forced connection can bypass soft tolerances but must have positive projected overlap, stay within the hard gap, and connect geometry inside one Area. Cross-Area movement always uses a Portal.

The adjacency inspector reports the measured reason that an automatic edge pair connected or failed, and recompilation preserves explicit overrides.

## Validation

Validation emits structured issues containing severity, stable issue code, measurements, and target identity. Errors block baking; warnings do not. Scene overlays must make issues selectable and visibly locate:

- Degenerate or self-intersecting polygons.
- Invalid winding, duplicate vertices, and zero-length edges.
- Non-planarity beyond tolerance.
- Invalid automatic or forced adjacency.
- Portals outside valid navigable regions or with broken attachments.
- Orphan polygons and unreachable islands.

Validation reports author intent faithfully. Geometry or topology is repaired only through an explicit, Undoable repair command where the correction is unambiguous.

## Editor workflow

One Areafinder authoring window and one Scene `EditorTool` provide Vertex, Edge, Polygon, Portal, and Semantic modes. Shared Undo-aware commands perform every persistent edit; transient selection is editor state rather than serialized navigation data.

Overlays expose polygon and edge IDs, inferred links, forced links and cuts, Portal spans and direction, selected semantic variables, validation issues, unreachable islands, route corridors, and steering targets. Policy preview selects a compiled agent policy and two endpoints, then runs the same compiler and solver as runtime while showing rejection and cost reasons.

## Baking

Baking is explicit and writes a committed `NavigationBakeAsset`. A deterministic geometry source fingerprint covers the semantic-registry schema, adjacency settings, Areas, polygons, adjacency overrides, and Portals; it deliberately excludes policy content. Tracked policies remain a bake-validation gate, but policy-only edits or validation failures do not stale previously compiled geometry. A failed bake leaves previous bytes untouched, and geometry source changes leave those bytes stale. Editor play mode and player builds reject stale bakes rather than silently using them.
