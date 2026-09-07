# Route performance baseline

Areafinder 0.2 includes a dependency-free benchmark in `NavigationBenchmarkTests`. It uses `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread` after 32 warmups. Each fixture records the median of seven samples containing 128 complete/receive/release request cycles.

The fixtures are deterministic:

- one Area containing a 16×16 polygon grid; and
- three Areas containing 8×8 grids connected by two directional Portals.

The warmed three-Area fixture intentionally exercises the bounded Portal-pair cache. Its higher throughput is therefore not directly comparable to the uncached endpoint-to-endpoint same-Area route.

## 0.2 reference run

Recorded 2026-09-06 on:

- AMD Ryzen 9 7900X, 24 logical processors;
- 130,187 MB reported system memory;
- Windows 11 `10.0.26220`;
- Unity `6000.7.0a6`, Editor/Mono2x backend;
- Collections `6.7.0`; and
- Burst `2.0.0` resolved by this Unity alpha. The package declaration remains Burst `1.8.25`; Unity selected its newer built-in implementation.

| Fixture | Median routes/second | API-reported bytes/request |
| --- | ---: | ---: |
| 16×16, one Area | 4,122.4 | 0.0 |
| 3×8×8, two Portals | 15,195.0 | 0.0 |

After 32 warmups, 1,024 idle `Tick(0)` calls reported zero allocated bytes. That idle result is a release assertion. Route throughput and bytes/request are observational baselines only; 0.2 does not promise a zero-allocation complete route, even when a backend reports zero through the requested thread-allocation API.

Run `Tools~/Run-UnityTests.ps1 -BenchmarkReportPath <path>` to retain the machine-readable JSON report. Omitting the path still creates and validates a report inside the disposable host.
