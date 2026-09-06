# Changelog

All notable changes to this package will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-06

### Added

- Added the initial Unity Package Manager repository structure, runtime/editor/test assemblies, package documentation, and GitHub contribution templates.
- Added stable 128-bit semantic, Area, Portal, polygon, edge, vertex, and policy identities; append-only semantic slots with tombstones; dynamic semantic masks; and explicit compaction tooling.
- Added polygon and Portal authoring assets, structured validation, deterministic flat bakes, stale-bake rejection, and an initial Undo-aware authoring window and Scene tool.
- Added compiled traversal policies, authored Area/Portal routing, polygon corridors and steering guidance, a cooperative asynchronous request lifecycle, targeted revisions, and bounded Portal-pair caches.
- Added a Burst/Jobs multi-polygon local-search kernel backed by persistent native data and scratch storage.
- Added runtime and editor coverage for core semantics, authoring, baking, routing, policy, revision, scheduling, cancellation, and identity behavior.
- Added a reproducible semantic-mask layout benchmark and decision record covering fixed-stride, trimmed pooled, and interned storage.

### Changed

- Raised the deliberate development baseline to Unity `6000.7.0a6`.
- Added Collections `6.7.0` and Burst `1.8.25` after the managed behavior contract was established.
- Retained fixed-stride semantic storage for bake schema 1; player/Burst evidence remains required before freezing the 1.0 schema.
- Licensed Areafinder under the MIT License.

### Fixed

- Made scheduler queue entries generation-aware so reused request slots cannot service stale work.
- Made request freshness track every Area evaluated during route selection, including losing alternatives.
- Kept geometry bake freshness independent of traversal-policy content while retaining policy validation during baking.
- Rejected unexpected top-level package entries during repository structure validation and made the Unity runner enforce baseline test discovery counts.

### Documentation

- Defined the polygon-native Areafinder 1.0 architecture, authoring model, runtime contracts, NavMesh migration stages, and continuous test strategy.
- Distinguished implemented 0.1 behavior from the remaining 1.0 acceptance work.
