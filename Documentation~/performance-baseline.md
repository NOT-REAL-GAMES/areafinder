# Route performance baseline

Areafinder 0.4 preserves the dependency-free 0.3 concurrency benchmark and adds a local-search strategy matrix. Both use `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread` after 32 warmups and record seven samples. The primary 0.4 comparison runs with requested concurrency cap one so concurrency cannot hide the cost of an individual search; cap four is a later composition check.

The retained 0.3 fixtures are deterministic:

- one Area containing a 32×32 polygon grid; and
- three Areas containing 16×16 grids connected by two directional Portals.

The warmed three-Area fixture intentionally exercises the bounded Portal-pair cache. Its higher throughput is therefore not directly comparable to the uncached endpoint-to-endpoint same-Area route.

## 0.4 algorithm matrix

The internal benchmark override compares the frozen 0.3 scan, heap Dijkstra, A*, bidirectional Dijkstra, bidirectional A*, ALT A*, and bidirectional ALT. Normal consumers cannot select a strategy; production requests use A* in 0.4.

The matrix retains the 32×32 same-Area and three-Area 16×16 fixtures and adds deterministic small, large-local, long-thin, branch-heavy, equal-cost, directed, policy-divergent, and mutation workloads. Landmark runs compare 4, 8, and 16 tables and report preprocessing and memory separately. Each applicable row records:

- routes/second and median/p95 completion latency;
- nodes discovered and expanded, edges examined, heap pushes/pops, maximum frontier size, and heuristic evaluations;
- API-reported bytes/request, scratch bytes, accelerator bytes, preprocessing time, and mutation rebuild time; and
- CPU, logical processors, Unity workers, RAM, OS, Unity, Burst, Collections, backend, and effective Areafinder concurrency cap.

The report keeps every strategy row so heap, heuristic, bidirectional, and landmark gains remain separately attributable. Generation-stamped scratch is retained only if the isolated large-graph median improves by at least 5%, warmed managed allocation does not increase, and small-graph p95 regresses by no more than 2%.

### 0.4 reference results

Recorded 2026-09-10 on ARBITER with an AMD Ryzen 9 7900X, 24 logical processors, 23 Unity job workers, 130,187 MB reported memory, Windows 11 `10.0.26220`, Unity `6000.7.0a6`, Editor/Mono2x, Collections `6.7.0`, and Burst `2.0.0` as resolved by this Unity alpha. The package declaration remains Burst `1.8.25`. The machine-readable 110-row report is retained as a `v0.4.0` release asset.

The following cap-one results use the 32×32 same-Area fixture. ALT rows use eight landmarks so they are directly comparable; the retained report also contains the 4- and 16-landmark runs and every other fixture.

| Strategy | Median routes/s | Median latency | p95 latency | Expanded/request | Edges/request | Scratch bytes | Accelerator bytes | Preprocess |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Reference03 | 349.5 | 191.68 ms | 352.21 ms | 777.7 | 3,026.0 | 17,573 | 0 | 0 ms |
| Heap Dijkstra | 2,854.2 | 28.74 ms | 43.22 ms | 777.7 | 3,026.0 | 42,125 | 0 | 0 ms |
| A* | 3,883.2 | 23.93 ms | 34.24 ms | 323.2 | 1,275.3 | 42,125 | 0 | 0 ms |
| Bidirectional Dijkstra | 1,997.0 | 39.22 ms | 62.24 ms | 1,384.9 | 5,391.1 | 75,884 | 0 | 0 ms |
| Bidirectional A* | 2,647.0 | 30.85 ms | 47.56 ms | 665.7 | 2,628.6 | 75,884 | 0 | 0 ms |
| ALT A*, 8 landmarks | 3,830.6 | 24.07 ms | 32.95 ms | 260.0 | 1,030.3 | 42,125 | 131,104 | 209.49 ms |
| Bidirectional ALT, 8 landmarks | 2,540.9 | 31.87 ms | 47.77 ms | 519.0 | 2,060.6 | 75,884 | 131,104 | 205.79 ms |

This progression attributes the large Reference03-to-heap improvement to the priority queue, then shows the additional pruning from A*. ALT reduced explored work further on this fixture but did not consistently recover its evaluation and preprocessing overhead on the larger matrix. The bidirectional variants also regressed these workloads because their second frontier plus the required canonical forward plateau outweighed discovery savings. Those results support fixed A* as the conservative 0.4 production strategy and leave request-by-request selection to 0.5.

The retained continuity measurement recorded 3,780.0 routes/s at cap one and 5,538.2 at cap four for production A*, a 1.465× composition speedup. The three-Area cache fixture recorded 5,265.5 and 8,614.2 routes/s respectively, a 1.636× speedup. Both effective caps matched the requested 1/4 values and peak physical jobs never exceeded them. Consecutive runs of the much cheaper A* job produced 1.373×–1.560× same-Area speedups as host scheduling load varied, so the 0.4 reference-host composition gate requires improvement over cap one rather than retaining the old scan kernel's 1.5× threshold.

Generation-stamped scratch was not added: the required isolated evidence for its conditional adoption was not established, while the preallocated heap/A* substrate already met the warmed allocation contract. This avoids carrying an unproven state mechanism into the release.

## 0.3 reference run

Recorded 2026-09-08 on:

- AMD Ryzen 9 7900X, 24 logical processors and 23 Unity job workers;
- 130,187 MB reported system memory;
- Windows 11 `10.0.26220`;
- Unity `6000.7.0a6`, Editor/Mono2x backend;
- Collections `6.7.0`; and
- Burst `2.0.0` resolved by this Unity alpha. The package declaration remains Burst `1.8.25`; Unity selected its newer built-in implementation.

| Fixture | Cap | Effective cap | Peak jobs | Median routes/s | Median latency | p95 latency | API-reported B/request |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 32×32, one Area | 1 | 1 | 1 | 365.2 | 186.0 ms | 334.8 ms | 0.0 |
| 32×32, one Area | 4 | 4 | 4 | 1,250.0 | 58.4 ms | 97.8 ms | 0.0 |
| 3×16×16, two Portals | 1 | 1 | 1 | 1,518.2 | 63.2 ms | 84.1 ms | 0.0 |
| 3×16×16, two Portals | 4 | 4 | 4 | 5,027.1 | 19.9 ms | 25.2 ms | 0.0 |

Cap four delivered 3.42× the cap-one same-Area median and 3.31× the cap-one cross-Area median. The benchmark asserts at least 1.5× for the same-Area workload on this named Ryzen 9 7900X reference host when four worker slots are available; other hardware records evidence without inheriting that machine-specific threshold.

After 32 warmups, 1,024 idle `Tick(0)` calls reported zero allocated bytes. That idle result remains a 0.4 release assertion. Route allocation values are observational baselines only; 0.4 does not promise a zero-allocation complete route, even when a backend reports zero through the requested thread-allocation API.

Run `Tools~/Run-UnityTests.ps1 -BenchmarkReportPath <path>` to retain the machine-readable JSON report. Omitting the path still creates and validates a report inside the disposable host.
