# Migration from the prototype

The prototype under `VICIOUS CYCLE/Assets/Areafinder` remains a read-only behavioral oracle. The new package is an evolutionary replacement, not a source-compatible port and not a clean-room rewrite.

The current 0.3 package is already polygon-native and contains no NavMesh or High Precision runtime code. Portable behavior is captured in package tests; no migration adapter has been added because the polygon path does not currently need one.

## Behavior to preserve

- Authored Area/Portal topology and deterministic global routing.
- Double-precision universe coordinates with small Area-local query frames.
- Rigid rotated and teleported Portal transitions, including reverse traversal.
- Per-Area revisions and revision-sensitive Portal-distance caching.
- Queued work, fair progress, cancellation isolation, and deferred exactly-once callbacks.

Characterization tests move before the corresponding implementation. New assets recreate small fixtures in the new format; no serialized-asset importer, obsolete wrappers, or public-API compatibility layer is planned.

## Deliberate omissions

The package does not port the prototype's locomotion agent, High Precision conversion tool, project-specific sample builder, dead graph types, compatibility shims, or abstractions with only one internal implementation. HORDE supplies its own Rigidbody locomotion and consumes navigation guidance.

## NavMesh transition

Unity NavMesh is temporary, optional migration infrastructure:

1. Preserve portable prototype behavior through tests.
2. Implement polygon authoring, compilation, and local routing alongside a reference adapter.
3. Compare reachability, Area/Portal sequence, transition pose, revision invalidation, request lifecycle, and cancellation. Exact corner positions and distances need not match.
4. Make manual polygons the standard ground backend.
5. Remove runtime NavMesh dependence.
6. Delete the adapter and obstacle bridge once no supported behavior relies on them.

If later comparison needs an adapter, it belongs in a separate `NotRealGames.Areafinder.UnityNavMesh` assembly behind an AI Navigation `2.0.14` version define. No such assembly exists today. AI Navigation is not added to the package manifest, and High Precision integration remains in HORDE rather than the package.

Completion requires static checks that core package source contains no references to `UnityEngine.AI`, `Unity.AI.Navigation`, NavMesh types, or High Precision Framework.

## HORDE dogfood

HORDE is the manual integration project for authoring, policy preview, side-by-side reference scenes, and Rigidbody consumers. Package tests remain authoritative and run in disposable Unity projects so HORDE state cannot hide package dependencies.

The 0.3 dogfood gate creates two navigable Areas and runs 72 seeded agents through concurrent cancellation, mutation staleness, closed-Portal failure, reopening, replanning, destruction/respawn, and a longer diagnostic soak, then feeds recovered steering guidance to project-owned kinematic Rigidbody consumers. This integration deliberately leaves locomotion, collisions, steering ownership, avoidance, and replanning policy in HORDE; Areafinder adds no package-level agent abstraction.

## Post-1.0 validation

Only after polygon navigation is mature should a simple voxel-grid backend prove the abstraction with a ground Area, Takeoff Portal, voxel Air Area, Landing Portal, and second ground Area. Octrees, sparse bricks, and hierarchy follow only if profiling justifies them.
