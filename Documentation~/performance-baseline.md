# Route performance baseline

Areafinder 0.3 includes a dependency-free concurrency benchmark in `NavigationBenchmarkTests`. It uses `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread` after 32 warmups. Each fixture records seven samples containing 128 complete/retrieve/release request cycles at requested concurrency caps one and four.

The fixtures are deterministic:

- one Area containing a 32×32 polygon grid; and
- three Areas containing 16×16 grids connected by two directional Portals.

The warmed three-Area fixture intentionally exercises the bounded Portal-pair cache. Its higher throughput is therefore not directly comparable to the uncached endpoint-to-endpoint same-Area route.

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

After 32 warmups, 1,024 idle `Tick(0)` calls reported zero allocated bytes. That idle result is a release assertion. Route allocation values are observational baselines only; 0.3 does not promise a zero-allocation complete route, even when a backend reports zero through the requested thread-allocation API.

Run `Tools~/Run-UnityTests.ps1 -BenchmarkReportPath <path>` to retain the machine-readable JSON report. Omitting the path still creates and validates a report inside the disposable host.
