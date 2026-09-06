> **Areafinder is an authored description of navigational possibility, not an automated interpretation of level geometry. Areas describe world topology, polygons describe local traversability, semantic variables describe meaning, agent policy supplies preference, and locomotion decides how to act on the resulting guidance.**

# Architecture

This document defines the target Areafinder 1.0 architecture. Version 0.1 already implements the core authoring records, flat bake, polygon routing, policies, structured requests/results, revisions, caches, initial editor tooling, and a Burst local-search job. Delivery-gate and 1.0 statements still describe acceptance work and must not be read as a stability promise.

## System model

```text
Project semantic registry
        ↓
Authored Areas + Portals
        ↓
Manual polygon navigation
        ↓
Agent-specific traversal policy
        ↓
Asynchronous path requests
        ↓
Locomotion-specific guidance
```

An Area is an authored semantic world partition. A Portal is an authored transition between Areas. Together they form canonical world topology, independent of the geometry used to navigate locally inside each Area.

The first local representation is a manually authored, convex 2.5D polygon surface. Polygon adjacency may be inferred, but polygon geometry never generates canonical Areas or Portals. A later voxel backend must fit behind the same world topology without changing its meaning.

Version 1.0 requires manual topology authoring. Any future generator is optional and must emit ordinary editable Area and Portal records rather than creating a second canonical topology model.

## Architectural boundaries

- **Semantics versus behavior:** semantic bits describe geometry and transitions. A compiled agent policy separately decides eligibility and cost.
- **Global versus local routing:** global search chooses Areas and Portals. Local search finds a route between locations or Portal sides inside one Area.
- **Pathfinding versus locomotion:** Areafinder returns corridors, transitions, and optional steering targets. It neither moves agents nor requires exact intermediate waypoint arrival.
- **Authoring versus runtime:** Unity objects are convenient source data. Baking converts them to deterministic flat data, and searches never walk `GameObject`, `MonoBehaviour`, `ScriptableObject`, or `List<T>` graphs.
- **Correctness before optimization:** the compiled representation and managed behavior tests preceded the current Burst local-search job. Further batching and memory-layout changes remain gated by parity and allocation evidence.

## Canonical identities

Semantic definitions, Areas, Portals, polygons, vertices, and edges use persistent authoring identities. Runtime bakes map these identities to dense indices for locality. Reordering assets or rebuilding a bake must not change the authored identities.

The semantic registry uses stable 128-bit IDs and append-only integer slots. Renaming preserves identity. Deletion tombstones a slot; it does not renumber or reuse it. Destructive registry compaction is an explicit project-wide operation with a complete preview and transactional remapping.

## Runtime shape

A `NavigationWorldAsset` owns topology and references `NavigationAreaAsset` data. An explicit bake produces primitive arrays for Areas, polygons, vertices, adjacency, Portals, semantic words, and identity lookup. At runtime, a disposable non-singleton `NavigationWorld` owns the compiled view, mutable state, scheduler, revisions, caches, and result pools. An optional `NavigationWorldHost` component supplies Unity lifecycle integration.

Each semantic mask addresses a range in contiguous `ulong` storage. The runtime does not allocate one managed array per polygon. Bake schema 1 uses fixed stride; the [semantic mask layout decision](semantic-mask-layout.md) records why neither trimmed pooling nor interning passed the cross-workload replacement gate. Player/Burst measurements on real bakes are still required before the 1.0 schema is frozen.

## Version 1.0 boundary

Version 1.0 includes manual Areas, Portals, and polygon surfaces; inferred adjacency with explicit overrides; a stable semantic registry; compiled policy; global and local search; asynchronous requests; local revisions and caching; authoring and debug tooling; and automated tests.

It excludes generated topology, automatic navigation-mesh generation, concave polygons and holes, runtime polygon cutting, voxel flight navigation, traffic and crowd systems, avoidance, locomotion, procedural animation, and public third-party backend APIs.

Unity NavMesh is a temporary migration oracle only. The final package must not require `UnityEngine.AI`, AI Navigation, NavMesh surfaces or paths, NavMesh obstacles, or High Precision Framework.

## Delivery gates

1. Capture portable behavior from the working prototype in characterization tests.
2. Implement stable authoring identities, semantic storage, validation, and deterministic baking.
3. Complete a synchronous polygon-routing vertical slice.
4. Add the explicit request lifecycle, revisions, and local cache refinement.
5. Complete the editor workflow, diagnostics, and agent-policy preview.
6. Move proven search kernels to Jobs and Burst, then benchmark storage layouts.
7. With the polygon cutover complete and no NavMesh adapter or runtime code present, finish remaining prototype characterization and publish the 1.0 documentation and sample.
