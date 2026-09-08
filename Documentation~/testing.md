# Testing and acceptance

Tests are added with each implementation milestone. The 0.3 suite retains coverage for stable IDs and large-coordinate transforms; semantic boundaries, tombstones, restoration, and compaction; all 41 emitted validation codes and every configured tolerance boundary; deterministic AssetDatabase/domain-reload persistence and baking; policy eligibility/cost; local and three-Area routing; rigid bidirectional Portals; the complete request lifecycle; direct cache behavior; managed/Burst parity; revisions and mutation; Undo refresh; editor settings; and sample setup.

Version 0.3 additionally characterizes bounded concurrent jobs, generation-safe physical work ownership, copy-on-write mutation snapshots, admission versus completion ordering, cancellation capacity, disposal fencing, and deterministic cap-one/cap-four parity. It builds and launches real Windows Mono/Burst and IL2CPP/Burst players, records concurrency baselines, and runs 72 seeded project-owned Rigidbody consumers in HORDE. Full-route allocation guarantees, A*/optimistic global refinement, intra-request job fan-out, schema changes, broad editor UX, and post-1.0 backends remain deferred.

## Run package tests

Use Unity `6000.7.0a6`. Install Areafinder into a disposable host project through a local `file:` dependency and add its package ID to `testables`:

```json
{
  "dependencies": {
    "com.notrealgames.areafinder": "file:C:/Projects/Areafinder"
  },
  "testables": [
    "com.notrealgames.areafinder"
  ]
}
```

Run both `NotRealGames.Areafinder.Editor.Tests` in Edit Mode and `NotRealGames.Areafinder.Tests` in Play Mode. `Tools~/Run-UnityTests.ps1` creates the disposable host, enforces discovery floors of 9 Edit Mode and 191 Play Mode cases, and validates its JSON benchmark report. Disposable hosts keep Unity-generated `Library`, `Logs`, `Temp`, and result artifacts outside the package repository.

## Authoring and serialization

- Exercise semantic slots 0, 1, 63, 64, 65, 127, and 128.
- Delete slot 12 and prove slot 13 retains identity and meaning; also cover restore, registry mismatch, GUID preservation, and explicit compaction remapping.
- Cover each polygon validation rule, every tolerance boundary, partial edge overlap, override precedence, forced-link hard limits, orphaned references, and diagnostic measurements.
- Replace all polygon data in an Area and verify Area/Portal topology survives while broken Portal attachments make the bake stale until repaired.
- Save, reload, and domain-reload without changing registry slots or Area, Portal, polygon, edge, and vertex identities.
- Prove identical source data produces identical bake bytes and stale bakes are rejected.

## Routing and policy

- Preserve prototype transform round trips, large-coordinate behavior, rigid Portal traversal in both directions, deterministic tie-breaking, and accumulated costs.
- Infer matching edges, remove inference outside tolerance, preserve forced disconnection despite coincidence, and allow only legal forced connections.
- Exercise the deterministic local Dijkstra scan, crossing spans, funnel output, unreachable internal Portal pairs, and a three-Area/two-Portal route.
- Give identical endpoints to two policies and verify one selects a sidewalk route while the other selects a road shortcut without encoding either concept in Areafinder.
- Cover eligibility masks, capability requirements, multiplier and penalty composition, and rejection of negative or non-finite costs.

## Revisions and asynchronous work

- Change one Area and prove only dependent cache records and results become stale.
- Cover polygon, adjacency, Portal, and explicit dirty revisions plus completed-result `IsCurrent` behavior.
- Exercise every request state, FIFO ordering, weighted-priority fairness, batch submission, callback reentrancy, invalid handles, result release, and exactly-once main-thread publication.
- Cancel queued and running requests and destroy an associated owner; no successful callback may arrive afterward.
- Change a dependency during a running request and require `Stale` rather than silently publishing an authoritative result.
- Prove that cancellation and logical release do not interrupt a physical job or free its lane, snapshot, or output before completion, even when the handle slot is reused.
- Distinguish deterministic weighted-FIFO admission from physical completion order and compare cap-one/cap-four route contents and equal-cost choices.
- Batch same-tick mutations into one snapshot, stale only dependent work, preserve unrelated-Area currentness, and fence every remaining job during world disposal.

## Jobs, Burst, players, and allocation

The local kernel runs as a Burst job with persistent native input and one scratch lane per effective concurrency slot. Managed-reference parity covers same-polygon, multi-polygon, unreachable, semantic-policy, runtime-mutation, and equal-cost-tie cases. After 32 warmups, 1,024 idle `Tick(0)` calls must allocate zero bytes according to `GC.GetAllocatedBytesForCurrentThread`. Full complete/retrieve/release cycles are recorded as baselines, not universal release thresholds and not zero-allocation guarantees.

`Tools~/Run-UnityAotSmoke.ps1` creates a valid two-Area fixture, compiles a policy, submits concurrent routes, cancels and mutates while work is running, verifies stale and closed-Portal outcomes, reopens the Portal, and validates recovered corridor/Portal/guidance/currentness. It builds both Windows backends, finds each native Burst artifact, launches each executable with a bounded timeout, and requires a backend-specific success marker. Missing IL2CPP support is a hard failure. `Tools~/Run-RoomToRoomSmoke.ps1` independently imports the package sample, runs its setup command, and executes its route.

The [route performance baseline](performance-baseline.md) records seven samples of 128 complete/retrieve/release cycles at requested caps one and four on a 32×32 same-Area grid and a three-Area/two-Portal fixture with 16×16 grids. On the named Ryzen 9 7900X reference host, the same-Area cap-four median must be at least 1.5 times cap one when four workers are available. The separate deterministic [semantic mask layout benchmark and decision](semantic-mask-layout.md) covers fixed-stride, trimmed pooled, and interned storage. Bake schema 1 retains fixed stride.

The 0.3 local gate is:

```powershell
./Tools~/Test-PackageStructure.ps1
./Tools~/Run-UnityTests.ps1
./Tools~/Run-RoomToRoomSmoke.ps1
./Tools~/Run-UnityAotSmoke.ps1 -Backends Mono,IL2CPP
```

GitHub Actions remains a static package-structure signal. For 0.3, the complete disposable-host local gate is authoritative and publication is not blocked by hosted workflow state.

## HORDE dogfood and deferred 1.0 acceptance

The 0.3 HORDE gate runs 72 non-kinematic Rigidbody agents with colliders against a multi-Area fixture, exercises mixed priorities and policies, staggered and simultaneous submissions, cancellation and immediate handle reuse, dependency staleness, Portal closure, reopening, replanning, agent destruction/respawn, and guidance traversal, then requires every project-owned consumer to recover. The host runs without AI Navigation, and static checks reject NavMesh or High Precision dependencies in package source.

The longer diagnostic soak continuously moves and replans agents with deterministic seeded randomness, periodically mutates topology, cancels requests, destroys and respawns owners, and checks bounded native/managed memory and completion latency. Reproducible leaks or lifetime corruption block release, but this messy-gameplay gate is deliberately separate from the synthetic numerical benchmark.

Broader manual acceptance before 1.0 still includes authoring production scenes from scratch, inspecting inferred and overridden adjacency, comparing materially different policies, and validating locomotion-specific guidance consumption under real gameplay load. Movement, collisions, steering decisions, crowd behavior, avoidance, and replanning policy stay in HORDE or another consuming project.
