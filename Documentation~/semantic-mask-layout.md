# Semantic mask layout decision

## Outcome

Areafinder 0.3 retains fixed-stride semantic words in bake schema 1. Every compiled mask stores the registry-wide word count in one flat `ulong` pool and existing records address their mask by offset.

Neither trimmed pooled masks nor interned masks met the replacement gate across the complete representative workload matrix. This is a decision for the current pre-1.0 schema, not permission to freeze the 1.0 bake format without player/Burst measurements from real projects.

## Gate

An alternative replaces fixed stride only if it:

1. saves at least 25% semantic storage; and
2. regresses a warmed full-mask scan by no more than 5%;
3. satisfies both conditions in every representative workload, rather than depending on one project's sparsity or repetition.

## Method

[`Tools~/Benchmark-SemanticMaskLayouts.ps1`](../Tools~/Benchmark-SemanticMaskLayouts.ps1) creates the same deterministic logical masks in three layouts:

- **Fixed stride:** every mask occupies the registry-wide word count.
- **Trimmed pooled:** trailing zero words are omitted and each record adds a 32-bit stored word count.
- **Interned:** identical full-width masks share one pool range.

The matrix uses 1, 2, 8, and 16 words; 256, 4,096, and 65,536 polygons; and three distributions:

- `DenseUnique`: full-width masks with no reusable values.
- `TrailingSparse`: unique masks whose last nonzero word is distributed across the available width.
- `SharedArchetypes`: sixteen repeated, full-width masks.

The byte model excludes the semantic-offset field already present in every compiled record because all three layouts use it. It includes the additional per-record word count required by trimming. Interning's transient bake-time dictionary is excluded from runtime memory.

The timing loop reads every logical word for every polygon, just as an eligibility predicate can. It verifies identical checksums before timing, performs two warm-up scans, and reports the median of seven samples. The pseudo-random input seed and iteration budget are fixed.

Run the default matrix from the repository root:

```powershell
./Tools~/Benchmark-SemanticMaskLayouts.ps1
```

Pass `-CsvPath <path>` to retain all rows for comparison. The script writes no benchmark artifact into the repository by default.

## Recorded result

The following storage results are from the default 65,536-polygon cases. Negative saving means the alternative uses more memory than fixed stride.

| Distribution | Words | Fixed storage | Trimmed saving | Interned saving |
| --- | ---: | ---: | ---: | ---: |
| Dense unique | 1 | 0.50 MiB | -50.00% | 0.00% |
| Dense unique | 2 | 1.00 MiB | -25.00% | 0.00% |
| Dense unique | 8 | 4.00 MiB | -6.25% | 0.00% |
| Dense unique | 16 | 8.00 MiB | -3.12% | 0.00% |
| Trailing sparse | 1 | 0.50 MiB | -50.00% | 0.00% |
| Trailing sparse | 2 | 1.00 MiB | 0.00% | 0.00% |
| Trailing sparse | 8 | 4.00 MiB | 37.50% | 0.00% |
| Trailing sparse | 16 | 8.00 MiB | 43.75% | 0.00% |
| 16 shared archetypes | 1 | 0.50 MiB | -50.00% | 99.98% |
| 16 shared archetypes | 2 | 1.00 MiB | -25.00% | 99.98% |
| 16 shared archetypes | 8 | 4.00 MiB | -6.25% | 99.98% |
| 16 shared archetypes | 16 | 8.00 MiB | -3.12% | 99.98% |

One reference timing run on 2026-09-04 used an AMD Ryzen 9 7900X, PowerShell 7.6.5, and .NET 10.0.11. Across the complete matrix, trimmed access ranged from 26.79% faster to 45.98% slower than fixed stride; interned access ranged from 1.64% faster to 11.65% slower. These small managed-code timings are sensitive to runtime and machine noise, but they are sufficient to reject any claim of a universal 5% ceiling.

Trimmed pooling saves enough memory only for wide masks with trailing sparsity and costs extra space for one- and two-word registries. Interning is excellent for repeated archetypes but saves nothing for unique masks. Fixed stride is therefore the only representation with distribution-independent storage and direct addressing.

## Before the 1.0 schema freeze

Repeat the same workload shapes inside warmed Unity player batches using the Burst search predicates and representative authored bakes. Record player target, Burst version, mask distribution, allocation result, and route throughput. Change schema 1 only if one alternative passes the same memory and performance gate across that expanded evidence set.
