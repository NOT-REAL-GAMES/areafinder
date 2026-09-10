# Areafinder

Areafinder is an independent, authored navigation and pathfinding framework for Unity. Its ground-navigation model combines explicitly authored Areas and Portals with manually authored polygon surfaces, project-defined semantic properties, agent-specific traversal policy, and asynchronous path requests.

> [!IMPORTANT]
> Areafinder is under active pre-1.0 development. Version 0.4 accelerates each local polygon search while preserving the 0.3 request, scheduler, snapshot, mutation, and result contracts. The public API, bake format, and editor workflow may still change before 1.0.

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

Areas and Portals are the canonical, manually authored world topology. Polygon connectivity solves local movement within an Area; it does not generate or replace that topology. Areafinder returns structured navigation guidance and deliberately does not own locomotion.

The current package pathfinds over Areafinder's own polygon representation and contains no Unity NavMesh dependency. A later flying-agent backend will validate that world topology remains independent of local spatial navigation.

## Current implementation

The package currently provides:

- stable 128-bit identities for semantic definitions, Areas, Portals, polygons, edges, vertices, and policies;
- an append-only project semantic registry with tombstones, dynamic `ulong` masks, and explicit destructive compaction;
- authored convex 2.5D polygons, automatic edge adjacency, persistent connection overrides, and Area-attached Portals;
- structured validation, deterministic flat bakes, and stale-bake rejection for play mode and player builds;
- policy-aware local polygon and cross-Area routing with corridors, crossing spans, and optional funnel steering targets;
- a disposable `NavigationWorld`, copy-on-write runtime snapshots, queued mutations, targeted revisions, bounded Portal-pair caches, cancellation, priorities, deferred callbacks, and structured result views;
- bounded concurrent Burst/Jobs local searches over persistent native arrays and independently owned scratch lanes;
- a fixed deterministic admissible A* production kernel, plus internal reference, heap-Dijkstra, bidirectional, and landmark strategies for differential testing and benchmarking; and
- an Undo-aware authoring window, Scene tool, overlays, policy preview, and an importable room-to-room sample.

Version 0.4 retains the complete 0.3 reliability and concurrency evidence, freezes the 0.3 Dijkstra scan as a correctness oracle, and adds deterministic heap Dijkstra, exact A*, bidirectional exact search, optional policy-specific ALT accelerators, generated differential tests, and per-strategy diagnostics. Normal consumers do not choose algorithms: `PathQuery` and every existing public request/result call remain unchanged, and bake schema 1 remains canonical.

Global Area/Portal route construction remains deterministic cooperative managed code, while each physical request runs at most one local Burst job and independent requests can keep multiple jobs in flight. Reverse adjacency and landmark tables are derived accelerators rather than canonical bake data, and missing accelerator data safely falls back to a non-ALT exact search. Areafinder returns guidance only: HORDE retains locomotion, collision, steering, avoidance, and replanning policy. Automatic strategy selection, intra-request fan-out, full-route allocation guarantees, shared-goal reuse, global-search refinement, broader editor UX, schema changes, and post-1.0 backends remain future work.

## Requirements

- Unity `6000.7.0a6` or a compatible newer `6000.7` release.

The alpha dependency is deliberate during early development. Compatibility will be reconsidered before a stable release.

## Installation

To work on the package locally, open Unity's Package Manager, choose **Install package from disk**, and select this repository's `package.json`.

To install directly from Git during development, use:

```text
https://github.com/NOT-REAL-GAMES/areafinder.git
```

Pin a release tag or commit when repeatable package resolution matters.

## Try the sample

After installation, import **Room to Room** from Areafinder's Package Manager page. Choose **Tools > Areafinder > Samples > Create Room-to-Room Demo**, then enter Play Mode with Gizmos enabled. The generated scene submits a cross-Area request and draws its Portal transition and steering guidance without adding locomotion behavior.

## Architecture

The 1.0 direction is governed by a few non-negotiable boundaries:

- Areas and Portals are authored independently of local geometry.
- Manual polygons are the canonical ground-navigation representation.
- A stable project registry describes meaning; agent policies interpret it.
- Authoring objects compile into contiguous runtime data before pathfinding.
- Global Area/Portal routing and local polygon routing remain separate searches.
- Requests are asynchronous and revision-aware; path results are guidance, not movement commands.
- Unity NavMesh, locomotion, voxel navigation, generated topology, runtime polygon cutting, and crowd avoidance are outside the standard 1.0 implementation.

Read the [architecture](Documentation~/architecture.md), [authoring](Documentation~/authoring.md), [runtime](Documentation~/runtime.md), [migration](Documentation~/migration.md), [testing](Documentation~/testing.md), and [performance baseline](Documentation~/performance-baseline.md) documents for the current guarantees and decision-complete 1.0 direction.

## Repository layout

- `Runtime`: Player-safe runtime code and the `NotRealGames.Areafinder` assembly.
- `Editor`: Editor-only tooling and the `NotRealGames.Areafinder.Editor` assembly.
- `Tests/Runtime`: Play Mode tests.
- `Tests/Editor`: Edit Mode tests.
- `Documentation~`: Package documentation excluded from Unity asset import.
- `Samples~`: Importable examples, currently the polygon-only Room-to-Room sample.
- `Tools~`: Disposable Unity test, sample, and executable-player runners plus dependency-free benchmarks.

## Testing

In the consuming test project's `Packages/manifest.json`, add the package name to `testables`:

```json
"testables": [
  "com.notrealgames.areafinder"
]
```

Run the `NotRealGames.Areafinder.Tests` Play Mode tests and `NotRealGames.Areafinder.Editor.Tests` Edit Mode tests from Unity Test Runner, or use the repository runner:

```powershell
./Tools~/Run-UnityTests.ps1
```

Package metadata, assembly boundaries, forbidden migration dependencies, `.meta` coverage, and repository hygiene can be checked without opening Unity:

```powershell
./Tools~/Test-PackageStructure.ps1
```

`Run-UnityTests.ps1` emits and validates JSON benchmark reports for the preserved concurrency baseline and the 0.4 local-search strategy matrix. `Run-RoomToRoomSmoke.ps1` imports and executes the sample setup in a disposable host. `Run-UnityAotSmoke.ps1` builds, launches, and verifies optimized same- and cross-Area routing, cancellation, mutation recovery, and accelerator fallback in both Windows Mono/Burst and Windows IL2CPP/Burst players; missing IL2CPP support is a hard failure. See [testing](Documentation~/testing.md) for the complete 0.4 gate and remaining 1.0 work.

## Contributing

See [the contribution guide](.github/CONTRIBUTING.md).

## License

Areafinder is available under the [MIT License](LICENSE.md).
