# Areafinder

Areafinder is an early-stage Unity package for custom navigation and pathfinding. This repository currently provides only the package, assembly, documentation, and test foundations; it does not implement navigation behavior yet.

The intended direction includes manually authored polygonal navigation surfaces, Areas and Portals for world-level topology, project-wide semantic navigation properties, asynchronous pathfinding, and a later voxel backend for flying agents.

## Requirements

- Unity 6.0 or newer

## Installation

To work on the package locally, open Unity's Package Manager, choose **Install package from disk**, and select this repository's `package.json`.

After the repository is published, it can also be installed from its Git URL through Package Manager.

## Repository layout

- `Runtime`: Player-safe runtime code and the `NotRealGames.Areafinder` assembly.
- `Editor`: Editor-only tooling and the `NotRealGames.Areafinder.Editor` assembly.
- `Tests/Runtime`: Play Mode tests.
- `Tests/Editor`: Edit Mode tests.
- `Documentation~`: Package documentation excluded from Unity asset import.
- `Samples~`: Future importable package samples.

## Testing

In the consuming test project's `Packages/manifest.json`, add the package name to `testables`:

```json
"testables": [
  "com.notrealgames.areafinder"
]
```

Run the `NotRealGames.Areafinder.Tests` Play Mode tests and `NotRealGames.Areafinder.Editor.Tests` Edit Mode tests from Unity Test Runner.

## Contributing

See [the contribution guide](.github/CONTRIBUTING.md).

## License

No license has been selected. See [LICENSE.md](LICENSE.md) before using or distributing this package.
