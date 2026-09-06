# Testing and acceptance

Tests are added with each implementation milestone. The current suite covers stable IDs and large-coordinate transforms; semantic boundaries, tombstones, restoration, and compaction; core polygon validation and adjacency cases; deterministic baking; policy eligibility/cost; local and three-Area routing; rigid bidirectional Portals; lifecycle, fairness, cancellation, stale results, revisions, release, and resolver ambiguity; plus editor settings and compaction smoke coverage.

The lists below remain the full acceptance target. Serialization/domain-reload matrices, every geometry tolerance edge, prototype parity, concurrent Burst batches, player AOT, warmed end-to-end allocation checks, and manual HORDE acceptance are not yet complete.

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

Run both `NotRealGames.Areafinder.Editor.Tests` in Edit Mode and `NotRealGames.Areafinder.Tests` in Play Mode. Disposable hosts keep Unity-generated `Library`, `Logs`, `Temp`, and result artifacts outside the package repository.

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

## Jobs, Burst, and allocation

The multi-polygon local kernel now runs as a Burst job with persistent native input and scratch arrays after the managed behavior suite established its contract. Managed-versus-Burst parity expansion, player AOT compilation, truly concurrent batches, and zero managed allocations in the warmed scheduler and full request/result path remain required.

The deterministic [semantic mask layout benchmark and decision](semantic-mask-layout.md) covers fixed-stride, trimmed pooled, and interned storage at 1, 2, 8, and 16 words across 256, 4,096, and 65,536 polygons. Schema 1 retains fixed stride because no alternative passes the memory and access gate across all distributions. A Unity player/Burst rerun on real authored bakes is still required before freezing the 1.0 schema.

## Manual 1.0 acceptance

In HORDE, author two rooms from scratch, inspect inferred and overridden adjacency, attach an authored Portal, assign policies that choose different routes, bake, issue concurrent Rigidbody-enemy requests, cancel and stale requests safely, and traverse using steering guidance without exact waypoint arrival. Run the final automated host without AI Navigation and confirm the package source contains no NavMesh or High Precision dependency.
