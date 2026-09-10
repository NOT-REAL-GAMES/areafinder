# Runtime and pathfinding

This document separates current 0.4 behavior from remaining 1.0 requirements. Names already present are usable for development, but none are compatibility-stable before 1.0.

## Current 0.4 implementation

- A disposable, non-singleton `NavigationWorld` loads a valid bake and exposes submission, batch submission, status, cancellation, release, ticking, resolution, and currency checks.
- Policies compile from assets or a programmatic builder into flat mask predicates and cost rules.
- Global routing follows authored Areas and Portals. Local routing returns polygon corridors, crossing spans, and optional funnel-derived steering targets.
- Managed global search is resumable and each active request schedules at most one local Burst `IJob` at a time. Independent requests can occupy separate persistent scratch lanes and execute concurrently; result construction, scheduler control, and callback publication remain managed.
- Requests advance through explicit queued/global/local states, then publish a terminal result on a later tick. Per-Area revisions and bounded local Portal-pair caches support targeted invalidation.
- Runtime polygon, adjacency, and Portal flags live in immutable copy-on-write snapshots. Every scheduled job acquires its snapshot and keeps that lease until physical completion, even if the logical request is cancelled or released.
- Both batch overloads, every lifecycle state, handle reuse, all output-flag combinations, weighted fairness, cancellation timing, callback reentrancy/exception isolation, result lifetime, and host-owned request cleanup are characterized.
- Direct cache tests cover hits, unreachable entries, zero and bounded capacity, deterministic LRU behavior, policy/revision separation, and unrelated-Area preservation.
- The behavior-frozen 0.3 strict-float Dijkstra scan remains an internal reference oracle: its relaxation order, costs, stop rule, and tie-breaking are unchanged. Heap Dijkstra, A*, bidirectional Dijkstra, bidirectional A*, ALT A*, and bidirectional ALT are selected only by internal tests and benchmarks and are differentially checked against it.
- Normal requests use deterministic admissible A*. The public query and result API remains algorithm-agnostic, and existing same-polygon and valid Portal-pair cache fast paths still bypass the local kernel.

Version 0.5 may use the 0.4 benchmark evidence to choose strategies automatically; 0.4 deliberately has no workload thresholds or public strategy control. Still required before 1.0 are broader production dogfooding, zero-allocation guarantees for the complete request path, and API/schema stabilization. Version 0.4 adds no locomotion, collision, steering, crowd, avoidance, or replanning abstraction.

## Runtime ownership

`NavigationWorld` is disposable and non-singleton. It owns one compiled world, immutable runtime snapshots, revisions, scheduler, caches, persistent native search data, and a bounded set of native scratch lanes. Multiple independent worlds may coexist, but a 1.0 path never crosses between world assets. Current result arrays are managed and scheduler-owned; pooling the complete result-construction path remains pre-1.0 work.

Each admitted local search moves into a generation-safe physical work record that owns its scratch lane, snapshot lease, compatible optional accelerator lease, output state, admission sequence, and original handle identity. The logical request slot may be cancelled, released, and reused without transferring or invalidating those resources. Bidirectional strategies keep both frontiers inside that one record and still consume exactly one physical lane. Disposal fences all remaining physical jobs before releasing native storage.

An optional `NavigationWorldHost` component loads a bake, advances `NavigationWorld.Tick`, applies queued mutations, tracks Unity request owners, and disposes the world. Core runtime code never searches through authoring objects.

## Current public concepts

- `NavigationLocation`: an Area ID and Area-local position.
- `PathQuery`: start, goal, compiled policy, priority, and requested output detail.
- `PathRequestHandle`: a reusable request slot and generation.
- `PathRequestStatus`: `Queued`, `RunningGlobal`, `RunningLocal`, `Completed`, `Failed`, `Cancelled`, or `Stale`.
- `NavigationPathView`: a read-only view over scheduler-owned Area segments, Portal transitions, polygon corridors, crossing spans, revision stamps, and optional steering targets.
- `NavigationWorld.Submit`, `SubmitBatch`, `GetStatus`, `TryGetPath`, `Cancel`, `Release`, `Tick`, and `IsCurrent`.
- `NavigationWorld.EffectiveMaxConcurrentSearches`: the requested local-job cap after clamping to at least one and to Unity's available job-worker capacity.

Search strategy is intentionally absent from `PathQuery` and the public world API. Friend assemblies can override it for deterministic comparison and inspect generation-safe per-request counters, but ordinary consumers neither choose nor interpret the implementation used.

Canonical requests use `NavigationLocation`. A convenience resolver maps a double-precision universe position into candidate local backends. An Area hint resolves overlaps; without a unique match it reports `NotFound` or `Ambiguous` rather than selecting silently.

Successful result storage remains owned by the scheduler until `Release`; a view is invalid after release or world disposal. Callers that need independent lifetime can request an explicitly allocating managed copy.

## Traversal policy

Policy assets and programmatic builders compile to the same immutable flat `CompiledTraversalPolicy`. It currently owns managed arrays; the Burst local kernel caches an equivalent persistent native copy by policy fingerprint. Optional landmark tables are also keyed by policy fingerprint because arbitrary nonnegative multipliers, including zero, prevent one useful distance table from being assumed admissible for every policy. Moving all repeated policy evaluation and result construction to allocation-free unmanaged paths remains a 1.0 gate.

Eligibility is a cheap mask operation using required-all, required-any, and forbidden-any semantic predicates plus agent capability checks against Areas, polygons, and Portals.

Preference remains separate. Active per-semantic distance multipliers combine multiplicatively and entry penalties add. Area penalties apply once per Area segment, polygon penalties once on entry, and Portal penalties once per transition. Compilation rejects negative, NaN, or infinite values. A stable policy identity, revision, and content fingerprint participate in cache keys.

## Global and local search

Global routing searches only the authored Area/Portal graph and remains resumable managed code. Local routing uses deterministic admissible A* over polygon adjacency and persistent native heap scratch. Its geometric lower bound is expressed in the active policy's cost domain; when no positive safe bound exists, it becomes zero and the search behaves as heap Dijkstra. Managed code still reconstructs the corridor and crossing spans, then derives optional Area-up funnel/string-pulling steering targets.

Internal exact alternatives share the same data-oriented substrate: the frozen linear-scan reference, heap Dijkstra, A*, bidirectional Dijkstra, bidirectional A*, ALT A*, and bidirectional ALT. Deterministic queue keys, complete optimal-cost plateaus, and canonical predecessor selection preserve 0.3 equal-cost results. Bidirectional kernels use directed reverse adjacency, a formally valid lower-bound stopping rule rather than first intersection, and both frontiers inside one physical job.

Reverse adjacency is flat derived data that retains each original forward adjacency index. ALT tables store directed distances to and from a deterministic, policy-specific landmark set and combine only finite admissible bounds. Both are replaceable accelerators outside bake schema 1. Missing, incompatible, or invalid ALT data falls back to ordinary A* or bidirectional A* rather than affecting correctness.

The current resumable solver evaluates exact local costs while expanding authored Portal states and caches successful or unreachable ordered Portal-side pairs. A request has no intra-request local-job fan-out. The 1.0 design permits admissible lower-bound refinement if profiling shows that eagerly exact local work is too expensive; that refinement loop is not current behavior.

The path result is structured guidance, not an exact waypoint contract. A locomotion system may consume the complete corridor or only the next useful steering target, and remains free to skip intermediate recommendations.

## Requests and scheduling

The scheduler uses a cooperative main-thread work budget. Low, Normal, and High queues use FIFO ordering within a priority and weighted round-robin service at 1:2:4. This policy controls advancement and admission, not physical completion: concurrently running requests may finish in a different order. Route contents and equal-cost tie-breaking remain deterministic.

The existing constructor requests four concurrent local searches by default. The three-argument constructor accepts an explicit requested cap, rejects values below one, and clamps the effective value to Unity's available job-worker capacity. `EffectiveMaxConcurrentSearches` exposes the value used by the world.

Each positive-budget tick performs deterministic phases in this order:

1. Apply all queued mutations through one copy-on-write snapshot, advancing every affected Area and topology revision at most once.
2. Publish terminal work made ready by an earlier tick and invoke its deferred callbacks.
3. Harvest physically complete jobs, choosing the lowest admission sequence among jobs that are ready without waiting for an earlier unfinished job.
4. Advance weighted-FIFO logical work and admit local jobs while scratch lanes are available.
5. Flush newly scheduled jobs once.

`Tick(0)` performs the mutation and publication phases but neither harvests nor admits work. Harvested or otherwise newly terminal work is published on a later tick.

Terminal callbacks are deferred to a later main-thread tick and run at most once. Cancellation becomes the logical status immediately and wins until publication, but it does not interrupt a scheduled Burst job or free its concurrency lane. That physical work continues to own capacity and resources until completion and harvest. The host can associate a request with a Unity owner, cancel it when that owner disappears, and suppress its callback.

Version 0.4 extends the Windows Mono/Burst and Windows IL2CPP/Burst probes with optimized same- and cross-Area routes, cancellation, dependency mutation, stale work, Portal closure, reopening, recovery, and accelerator fallback. End-to-end allocation guarantees, intra-request fan-out, and automatic strategy selection remain later gates.

## Revisions, caching, and mutation

Each Area has a `ulong` navigation revision. Polygon or adjacency changes advance that Area. Portal changes advance both endpoint Areas and a world-topology revision. Every request and result records the revisions it depends upon.

A dependency change before publication makes a request terminally `Stale`; completed results remain queryable but `IsCurrent` becomes false. One Area's change invalidates only cache entries that depend on that Area.

The Portal-distance cache keys an ordered pair of Portal sides by Area, policy fingerprint, and Area revision. It stores exact distance and reusable local-corridor guidance. Each Area defaults to 256 entries with deterministic least-recently-used eviction.

Version 1 runtime mutation can enable or disable polygons, adjacency, and Portals, or explicitly dirty an Area. Mutations are queued and applied at tick boundaries. All mutations in one tick produce at most one new immutable snapshot, and each affected Area and the topology revision advance at most once. Older snapshots stay alive until their scheduled jobs finish. A dependent job becomes stale; work depending only on unrelated Areas remains current.

The 0.4 landmark tables are computed over maximal authored, policy-eligible Area connectivity while ignoring runtime enabled bits. Enable/disable mutations therefore restrict that graph without invalidating its lower bounds, and Portal mutations do not affect the intra-Area tables. A future geometry, weight, or canonical-adjacency mutation must reject incompatible tables and use exact non-ALT fallback; 0.4 does not add a second asynchronous rebuild scheduler. Runtime polygon CSG and obstacle carving are outside 1.0.
