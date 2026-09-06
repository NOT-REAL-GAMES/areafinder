# Contributing

Thanks for contributing to Areafinder.

Areafinder deliberately targets Unity `6000.7.0a6` while its 1.0 architecture is being developed.

1. Read the [architecture](../Documentation~/architecture.md) and discuss significant behavior, data-format, or public-API changes in an issue before implementation.
2. Preserve authored Area/Portal topology, polygon-native ground navigation, semantic/behavior separation, and the global/local routing boundary.
3. Keep runtime code independent of `UnityEditor`. Do not add AI Navigation or High Precision as core dependencies.
4. Add focused tests with each behavior change rather than deferring coverage until the end of the refactor.
5. Run both Edit Mode and Play Mode tests and update documentation and `CHANGELOG.md` for user-visible changes.
