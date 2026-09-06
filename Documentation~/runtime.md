# Runtime and pathfinding

This document separates current 0.1 behavior from remaining 1.0 requirements. Names already present are usable for development, but none are compatibility-stable before 1.0.

## Current 0.1 implementation

- A disposable, non-singleton `NavigationWorld` loads a valid bake and exposes submission, batch submission, status, cancellation, release, ticking, resolution, and currency checks.
- Policies compile from assets or a programmatic builder into flat mask predicates and cost rules.
- Global routing follows authored Areas and Portals. Local routing returns polygon corridors, crossing spans, and optional funnel-derived steering targets.
- Multi-polygon local searches schedule and complete a Burst `IJob` over persistent native arrays and reusable native scratch. The surrounding global solver, result construction, scheduler, and callback publication remain managed and cooperative.
- Requests advance through explicit queued/global/local states, then publish a terminal result on a later tick. Per-Area revisions and bounded local Portal-pair caches support targeted invalidation.

Still required before 1.0 are a profiled concurrent-job pipeline, a refined optimistic global search if profiling justifies it, exhaustive prototype parity, warmed zero-allocation proof for the complete request path, Burst/AOT player validation, and API/schema stabilization.

## Runtime ownership

`NavigationWorld` is disposable and non-singleton. It owns one compiled world, runtime state, revisions, scheduler, caches, persistent native search data, and native scratch. Multiple independent worlds may coexist, but a 1.0 path never crosses between world assets. Current result arrays are managed and scheduler-owned; pooling the complete result-construction path remains pre-1.0 work.

An optional `NavigationWorldHost` component loads a bake, advances `NavigationWorld.Tick`, applies queued mutations, tracks Unity request owners, and disposes the world. Core runtime code never searches through authoring objects.

## Current public concepts

- `NavigationLocation`: an Area ID and Area-local position.
- `PathQuery`: start, goal, compiled policy, priority, and requested output detail.
- `PathRequestHandle`: a reusable request slot and generation.
- `PathRequestStatus`: `Queued`, `RunningGlobal`, `RunningLocal`, `Completed`, `Failed`, `Cancelled`, or `Stale`.
- `NavigationPathView`: a read-only view over scheduler-owned Area segments, Portal transitions, polygon corridors, crossing spans, revision stamps, and optional steering targets.
- `NavigationWorld.Submit`, `SubmitBatch`, `GetStatus`, `TryGetPath`, `Cancel`, `Release`, `Tick`, and `IsCurrent`.

Canonical requests use `NavigationLocation`. A convenience resolver maps a double-precision universe position into candidate local backends. An Area hint resolves overlaps; without a unique match it reports `NotFound` or `Ambiguous` rather than selecting silently.

Successful result storage remains owned by the scheduler until `Release`; a view is invalid after release or world disposal. Callers that need independent lifetime can request an explicitly allocating managed copy.

## Traversal policy

Policy assets and programmatic builders compile to the same immutable flat `CompiledTraversalPolicy`. It currently owns managed arrays; the Burst local kernel caches an equivalent persistent native copy by policy fingerprint. Moving all repeated policy evaluation and result construction to allocation-free unmanaged paths remains a 1.0 gate.

Eligibility is a cheap mask operation using required-all, required-any, and forbidden-any semantic predicates plus agent capability checks against Areas, polygons, and Portals.

Preference remains separate. Active per-semantic distance multipliers combine multiplicatively and entry penalties add. Area penalties apply once per Area segment, polygon penalties once on entry, and Portal penalties once per transition. Compilation rejects negative, NaN, or infinite values. A stable policy identity, revision, and content fingerprint participate in cache keys.

## Global and local search

Global routing searches only the authored Area/Portal graph. The current multi-polygon kernel uses a deterministic, allocation-free-in-kernel Dijkstra scan over polygon adjacency and persistent native scratch. Managed code reconstructs the corridor and crossing spans, then derives optional Area-up funnel/string-pulling steering targets. Replacing the scan with a measured priority-queue A* remains an optimization, not an API change.

The current solver evaluates exact local costs while expanding authored Portal states and caches successful or unreachable ordered Portal-side pairs. The 1.0 design permits admissible lower-bound refinement if profiling shows that eagerly exact local work is too expensive; that refinement loop is not current behavior.

The path result is structured guidance, not an exact waypoint contract. A locomotion system may consume the complete corridor or only the next useful steering target, and remains free to skip intermediate recommendations.

## Requests and scheduling

The scheduler uses a cooperative main-thread work budget. Low, Normal, and High queues use FIFO ordering within a priority and weighted round-robin service at 1:2:4. Requests span ticks and can be submitted individually or in batches; local jobs currently complete within their `RunningLocal` work item rather than running concurrently across ticks.

Terminal callbacks are deferred to a later main-thread tick and run at most once. Cancellation wins until publication. The host can associate a request with a Unity owner, cancel it when that owner disappears, and suppress its callback.

The first local kernel has moved behind Jobs and Burst without changing request semantics. Concurrent batches, job-safe snapshots, player AOT verification, and end-to-end warmed allocation checks remain later gates.

## Revisions, caching, and mutation

Each Area has a `ulong` navigation revision. Polygon or adjacency changes advance that Area. Portal changes advance both endpoint Areas and a world-topology revision. Every request and result records the revisions it depends upon.

A dependency change before publication makes a request terminally `Stale`; completed results remain queryable but `IsCurrent` becomes false. One Area's change invalidates only cache entries that depend on that Area.

The Portal-distance cache keys an ordered pair of Portal sides by Area, policy fingerprint, and Area revision. It stores exact distance and reusable local-corridor guidance. Each Area defaults to 256 entries with deterministic least-recently-used eviction.

Version 1 runtime mutation can enable or disable polygons, adjacency, and Portals, or explicitly dirty an Area. Mutations are queued and applied at tick boundaries. Because current local jobs complete inside one work item, native read snapshots do not yet outlive a tick; snapshot ownership must be extended before concurrent jobs are enabled. Runtime polygon CSG and obstacle carving are outside 1.0.
