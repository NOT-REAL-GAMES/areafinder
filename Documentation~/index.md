# Areafinder

Areafinder is currently a development scaffold. It does not provide navigation or pathfinding behavior yet.

## Install the package

In Unity 6.0 or newer, open Package Manager, choose **Install package from disk**, and select the repository's `package.json`. A Git URL can be used after the repository is published.

## Assembly boundaries

- Runtime code belongs to `NotRealGames.Areafinder` and must not reference `UnityEditor`.
- Editor-only code belongs to `NotRealGames.Areafinder.Editor`.
- Play Mode and Edit Mode tests remain in their corresponding test assemblies.

No Burst, Jobs, Collections, or Mathematics dependency is declared yet. Add one only when implemented functionality requires it.

## Test the package

Add `com.notrealgames.areafinder` to the host project's `testables` list, then run the package's Edit Mode and Play Mode tests in Unity Test Runner.
