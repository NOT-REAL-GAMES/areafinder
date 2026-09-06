# Areafinder

Areafinder is an independent, authored navigation and pathfinding framework for Unity. Its ground-navigation model combines explicitly authored Areas and Portals with manually authored polygon surfaces, project-defined semantic properties, agent-specific traversal policy, and asynchronous path requests.

> [!IMPORTANT]
> Areafinder is under active pre-1.0 development. Version 0.1 contains a working polygon-native vertical slice, but its public API, bake format, and editor workflow may change before 1.0.

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
- a disposable `NavigationWorld`, queued mutations, targeted revisions, bounded Portal-pair caches, cancellation, priorities, deferred callbacks, and structured result views;
- a Burst/Jobs local-search kernel over persistent native arrays; and
- an Undo-aware authoring window, Scene tool, overlays, policy preview, and an importable room-to-room sample.

The scheduler and global route construction are still cooperative managed code, multi-polygon jobs complete within their `RunningLocal` tick, and full request-result allocation elimination and concurrent job batches remain 1.0 work.

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

Read the [architecture](Documentation~/architecture.md), [authoring](Documentation~/authoring.md), [runtime](Documentation~/runtime.md), [migration](Documentation~/migration.md), and [testing](Documentation~/testing.md) documents for the decision-complete target and delivery sequence.

## Repository layout

- `Runtime`: Player-safe runtime code and the `NotRealGames.Areafinder` assembly.
- `Editor`: Editor-only tooling and the `NotRealGames.Areafinder.Editor` assembly.
- `Tests/Runtime`: Play Mode tests.
- `Tests/Editor`: Edit Mode tests.
- `Documentation~`: Package documentation excluded from Unity asset import.
- `Samples~`: Importable examples, currently the polygon-only Room-to-Room sample.
- `Tools~`: Disposable Unity test/build runners and the semantic-layout benchmark.

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

`Run-UnityAotSmoke.ps1` builds a disposable Windows player and verifies that it contains a generated Burst native library. It uses IL2CPP when that Unity module is installed and otherwise reports that it validated the Mono/Burst player pipeline. See [testing](Documentation~/testing.md) for the complete current evidence and remaining 1.0 gates.

## Contributing

See [the contribution guide](.github/CONTRIBUTING.md).

## License

Areafinder is available under the [MIT License](LICENSE.md).
