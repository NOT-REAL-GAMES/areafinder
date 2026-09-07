# Areafinder

Areafinder is an authored navigation framework under active pre-1.0 development. Version 0.2 contains a reliability-tested polygon-native vertical slice: stable authoring identities, a project semantic registry, deterministic baking, policy-aware local and cross-Area routing, scheduler-owned structured result views, a cooperative request lifecycle, targeted revisions and caching, an initial Scene authoring workflow, and a Burst job for multi-polygon local search.

Version 0.2 proves real Unity persistence and domain reload, all current validation and request contracts, direct cache behavior, managed/Burst parity, a zero-allocation warmed idle tick, both Windows player backends, and Room-to-Room import/setup. The public API and bake schema are still pre-1.0. True concurrent job batches and snapshot ownership, full-route zero-allocation work, global-search refinement, broad editor polish, HORDE dogfooding, schema evolution, and post-1.0 backends remain deferred.

## Documentation

- [Architecture](architecture.md): system boundaries, core invariants, and 1.0 shape.
- [Authoring and baking](authoring.md): semantic registry, Areas, Portals, polygons, adjacency, validation, and editor workflow.
- [Runtime and pathfinding](runtime.md): compiled data, policy, global and local routing, requests, revisions, and results.
- [Semantic mask layout](semantic-mask-layout.md): benchmark method, recorded result, and the current fixed-stride decision.
- [Performance baseline](performance-baseline.md): repeatable route benchmark method and the 0.2 reference result.
- [Migration](migration.md): staged evolution from the working NavMesh prototype to the polygon backend.
- [Testing](testing.md): current smoke tests, required behavioral coverage, and acceptance gates.

## Install the package

Use Unity `6000.7.0a6` or a compatible newer `6000.7` release. Open Package Manager, choose **Install package from disk**, and select the repository's `package.json`. Alternatively, install the development package from:

```text
https://github.com/NOT-REAL-GAMES/areafinder.git
```

## Assembly boundaries

- Runtime code belongs to `NotRealGames.Areafinder` and must not reference `UnityEditor`.
- Editor-only code belongs to `NotRealGames.Areafinder.Editor`.
- Play Mode and Edit Mode tests remain in their corresponding test assemblies.

The package currently declares Collections `6.7.0` and Burst `1.8.25`; the multi-polygon local kernel uses Jobs, Burst, and the Unity Mathematics module. Unsafe code remains disabled. AI Navigation and High Precision are not package dependencies.

## Test the package

Add `com.notrealgames.areafinder` to the host project's `testables` list, then run the package's Edit Mode and Play Mode tests in Unity Test Runner. See [Testing](testing.md) for the evolving acceptance suite.
