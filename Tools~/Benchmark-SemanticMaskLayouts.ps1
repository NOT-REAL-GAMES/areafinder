param(
    [int[]]$WordCounts = @(1, 2, 8, 16),
    [int[]]$PolygonCounts = @(256, 4096, 65536),
    [ValidateRange(3, 31)]
    [int]$Samples = 7,
    [ValidateRange(65536, 16777216)]
    [int]$TargetWordReadsPerSample = 4194304,
    [string]$CsvPath
)

$ErrorActionPreference = 'Stop'

if (-not ('AreafinderSemanticLayoutBenchmark' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;

public static class AreafinderSemanticLayoutBenchmark
{
    public sealed class Result
    {
        public string Scenario;
        public int PolygonCount;
        public int WordCount;
        public long FixedBytes;
        public long TrimmedBytes;
        public long InternedBytes;
        public double FixedNanoseconds;
        public double TrimmedNanoseconds;
        public double InternedNanoseconds;
    }

    public static Result Measure(string scenario, int polygonCount, int wordCount, int samples, int targetReads)
    {
        if (polygonCount <= 0 || wordCount <= 0)
            throw new ArgumentOutOfRangeException();

        ulong[] logical = CreateMasks(scenario, polygonCount, wordCount);
        int[] trimmedOffsets;
        int[] trimmedCounts;
        ulong[] trimmed = CreateTrimmed(logical, polygonCount, wordCount, out trimmedOffsets, out trimmedCounts);
        int[] internedOffsets;
        ulong[] interned = CreateInterned(logical, polygonCount, wordCount, out internedOffsets);

        int repetitions = Math.Max(1, targetReads / checked(polygonCount * wordCount));
        ulong expected = ScanFixed(logical, polygonCount, wordCount, 1);
        if (ScanTrimmed(trimmed, trimmedOffsets, trimmedCounts, polygonCount, wordCount, 1) != expected ||
            ScanInterned(interned, internedOffsets, polygonCount, wordCount, 1) != expected)
            throw new InvalidOperationException("A benchmark layout changed mask contents.");

        // Warm JIT and caches before the measured samples.
        ScanFixed(logical, polygonCount, wordCount, 2);
        ScanTrimmed(trimmed, trimmedOffsets, trimmedCounts, polygonCount, wordCount, 2);
        ScanInterned(interned, internedOffsets, polygonCount, wordCount, 2);

        return new Result
        {
            Scenario = scenario,
            PolygonCount = polygonCount,
            WordCount = wordCount,
            FixedBytes = checked((long)logical.Length * sizeof(ulong)),
            // All layouts use the existing per-record semantic offset. Only trimmed adds a word count.
            TrimmedBytes = checked((long)trimmed.Length * sizeof(ulong) + (long)polygonCount * sizeof(int)),
            InternedBytes = checked((long)interned.Length * sizeof(ulong)),
            FixedNanoseconds = MedianNanoseconds(samples, polygonCount, repetitions,
                () => ScanFixed(logical, polygonCount, wordCount, repetitions)),
            TrimmedNanoseconds = MedianNanoseconds(samples, polygonCount, repetitions,
                () => ScanTrimmed(trimmed, trimmedOffsets, trimmedCounts, polygonCount, wordCount, repetitions)),
            InternedNanoseconds = MedianNanoseconds(samples, polygonCount, repetitions,
                () => ScanInterned(interned, internedOffsets, polygonCount, wordCount, repetitions))
        };
    }

    private static ulong[] CreateMasks(string scenario, int polygonCount, int wordCount)
    {
        ulong[] words = new ulong[checked(polygonCount * wordCount)];
        ulong state = 0x9e3779b97f4a7c15UL;
        ulong[] archetypes = null;
        if (scenario == "SharedArchetypes")
        {
            archetypes = new ulong[checked(16 * wordCount)];
            for (int index = 0; index < archetypes.Length; index++)
                archetypes[index] = Next(ref state) | 1UL;
        }

        for (int polygon = 0; polygon < polygonCount; polygon++)
        {
            int usedWords;
            if (scenario == "DenseUnique")
                usedWords = wordCount;
            else if (scenario == "TrailingSparse")
                usedWords = 1 + ((polygon * 17) % wordCount);
            else if (scenario == "SharedArchetypes")
                usedWords = wordCount;
            else
                throw new ArgumentException("Unknown scenario: " + scenario, "scenario");

            int offset = polygon * wordCount;
            if (archetypes != null)
            {
                Array.Copy(archetypes, (polygon % 16) * wordCount, words, offset, wordCount);
                continue;
            }

            for (int word = 0; word < usedWords; word++)
                words[offset + word] = Next(ref state) | 1UL;

            // Guarantee that otherwise-random dense and sparse masks remain distinct.
            words[offset] ^= ((ulong)(uint)polygon << 32) | (uint)polygon;
            if (words[offset] == 0UL)
                words[offset] = 1UL;
        }

        return words;
    }

    private static ulong[] CreateTrimmed(
        ulong[] logical,
        int polygonCount,
        int wordCount,
        out int[] offsets,
        out int[] counts)
    {
        offsets = new int[polygonCount];
        counts = new int[polygonCount];
        List<ulong> packed = new List<ulong>(logical.Length);
        for (int polygon = 0; polygon < polygonCount; polygon++)
        {
            int source = polygon * wordCount;
            int count = wordCount;
            while (count > 0 && logical[source + count - 1] == 0UL)
                count--;

            offsets[polygon] = packed.Count;
            counts[polygon] = count;
            for (int word = 0; word < count; word++)
                packed.Add(logical[source + word]);
        }

        return packed.ToArray();
    }

    private static ulong[] CreateInterned(
        ulong[] logical,
        int polygonCount,
        int wordCount,
        out int[] offsets)
    {
        offsets = new int[polygonCount];
        List<ulong> packed = new List<ulong>(logical.Length);
        Dictionary<string, int> known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int polygon = 0; polygon < polygonCount; polygon++)
        {
            int source = polygon * wordCount;
            string key = MaskKey(logical, source, wordCount);
            int offset;
            if (!known.TryGetValue(key, out offset))
            {
                offset = packed.Count;
                known.Add(key, offset);
                for (int word = 0; word < wordCount; word++)
                    packed.Add(logical[source + word]);
            }

            offsets[polygon] = offset;
        }

        return packed.ToArray();
    }

    private static string MaskKey(ulong[] words, int offset, int count)
    {
        char[] key = new char[count * 4];
        for (int word = 0; word < count; word++)
        {
            ulong value = words[offset + word];
            int target = word * 4;
            key[target] = (char)value;
            key[target + 1] = (char)(value >> 16);
            key[target + 2] = (char)(value >> 32);
            key[target + 3] = (char)(value >> 48);
        }

        return new string(key);
    }

    private static double MedianNanoseconds(int samples, int polygonCount, int repetitions, Func<ulong> scan)
    {
        long[] elapsed = new long[samples];
        ulong guard = 0UL;
        for (int sample = 0; sample < samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            guard ^= scan();
            elapsed[sample] = Stopwatch.GetTimestamp() - start;
        }

        GC.KeepAlive(guard);
        Array.Sort(elapsed);
        return elapsed[samples / 2] * (1000000000.0 / Stopwatch.Frequency) /
               ((double)polygonCount * repetitions);
    }

    private static ulong ScanFixed(ulong[] words, int polygonCount, int wordCount, int repetitions)
    {
        ulong checksum = 14695981039346656037UL;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            for (int polygon = 0; polygon < polygonCount; polygon++)
            {
                int offset = polygon * wordCount;
                for (int word = 0; word < wordCount; word++)
                    checksum = (checksum ^ words[offset + word]) * 1099511628211UL;
            }
        }

        return checksum;
    }

    private static ulong ScanTrimmed(
        ulong[] words,
        int[] offsets,
        int[] counts,
        int polygonCount,
        int wordCount,
        int repetitions)
    {
        ulong checksum = 14695981039346656037UL;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            for (int polygon = 0; polygon < polygonCount; polygon++)
            {
                int offset = offsets[polygon];
                int count = counts[polygon];
                for (int word = 0; word < wordCount; word++)
                {
                    ulong value = word < count ? words[offset + word] : 0UL;
                    checksum = (checksum ^ value) * 1099511628211UL;
                }
            }
        }

        return checksum;
    }

    private static ulong ScanInterned(
        ulong[] words,
        int[] offsets,
        int polygonCount,
        int wordCount,
        int repetitions)
    {
        ulong checksum = 14695981039346656037UL;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            for (int polygon = 0; polygon < polygonCount; polygon++)
            {
                int offset = offsets[polygon];
                for (int word = 0; word < wordCount; word++)
                    checksum = (checksum ^ words[offset + word]) * 1099511628211UL;
            }
        }

        return checksum;
    }

    private static ulong Next(ref ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 2685821657736338717UL;
    }
}
'@
}

$scenarios = @('DenseUnique', 'TrailingSparse', 'SharedArchetypes')
$results = foreach ($scenario in $scenarios) {
    foreach ($polygonCount in $PolygonCounts) {
        foreach ($wordCount in $WordCounts) {
            $measurement = [AreafinderSemanticLayoutBenchmark]::Measure(
                $scenario,
                $polygonCount,
                $wordCount,
                $Samples,
                $TargetWordReadsPerSample)

            [pscustomobject]@{
                Scenario = $scenario
                Polygons = $polygonCount
                Words = $wordCount
                FixedBytes = $measurement.FixedBytes
                TrimmedBytes = $measurement.TrimmedBytes
                InternedBytes = $measurement.InternedBytes
                TrimmedSavingPercent = [Math]::Round(100 * (1 - $measurement.TrimmedBytes / $measurement.FixedBytes), 2)
                InternedSavingPercent = [Math]::Round(100 * (1 - $measurement.InternedBytes / $measurement.FixedBytes), 2)
                FixedNsPerMask = [Math]::Round($measurement.FixedNanoseconds, 2)
                TrimmedRegressionPercent = [Math]::Round(100 * ($measurement.TrimmedNanoseconds / $measurement.FixedNanoseconds - 1), 2)
                InternedRegressionPercent = [Math]::Round(100 * ($measurement.InternedNanoseconds / $measurement.FixedNanoseconds - 1), 2)
            }
        }
    }
}

Write-Host 'Memory (bytes and saving relative to fixed stride)'
$memoryColumns = @(
    'Scenario', 'Polygons', 'Words', 'FixedBytes', 'TrimmedBytes',
    'TrimmedSavingPercent', 'InternedBytes', 'InternedSavingPercent'
)
$results | Format-Table -Property $memoryColumns -AutoSize

Write-Host 'Warmed full-mask scan (nanoseconds per polygon and regression relative to fixed stride)'
$performanceColumns = @(
    'Scenario', 'Polygons', 'Words', 'FixedNsPerMask',
    'TrimmedRegressionPercent', 'InternedRegressionPercent'
)
$results | Format-Table -Property $performanceColumns -AutoSize

$trimmedQualifies = @($results | Where-Object {
    $_.TrimmedSavingPercent -ge 25 -and $_.TrimmedRegressionPercent -le 5
}).Count -eq $results.Count
$internedQualifies = @($results | Where-Object {
    $_.InternedSavingPercent -ge 25 -and $_.InternedRegressionPercent -le 5
}).Count -eq $results.Count
$decision = if ($trimmedQualifies) { 'TrimmedPooled' } elseif ($internedQualifies) { 'Interned' } else { 'FixedStride' }

Write-Host "Decision: $decision"
Write-Host 'Gate: at least 25% memory saved and no more than 5% warmed scan regression in every representative workload.'

if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
    $resolvedCsvPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CsvPath)
    $results | Export-Csv -LiteralPath $resolvedCsvPath -NoTypeInformation -Encoding utf8NoBOM
    Write-Host "CSV: $resolvedCsvPath"
}
