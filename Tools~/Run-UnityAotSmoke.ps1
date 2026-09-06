param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.7.0a6\Editor\Unity.exe',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$KeepProject
)

$ErrorActionPreference = 'Stop'

$resolvedEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$hostProject = [IO.Path]::GetFullPath(
    (Join-Path $temporaryRoot ('AreafinderAot-' + [Guid]::NewGuid().ToString('N'))))

if (-not $hostProject.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create an AOT project outside the system temporary directory: $hostProject"
}

function Invoke-Unity {
    param([string[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedEditor
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unity did not start.'
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unity exited with code $($process.ExitCode). Arguments: $($Arguments -join ' ')"
    }
}

try {
    Invoke-Unity @('-batchmode', '-nographics', '-quit', '-createProject', $hostProject, '-logFile', '-')

    $manifestPath = Join-Path $hostProject 'Packages\manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable
    $manifest.dependencies['com.notrealgames.areafinder'] =
        'file:' + $resolvedPackage.Replace('\', '/')
    $manifest | ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    $assetsPath = Join-Path $hostProject 'Assets'
    $editorPath = Join-Path $assetsPath 'Editor'
    New-Item -ItemType Directory -Force -Path $editorPath | Out-Null

    $probeSource = @'
using NotRealGames.Areafinder;
using UnityEngine;

public sealed class AreafinderAotProbe : MonoBehaviour
{
    [SerializeField] private NavigationBakeAsset _bake;

    private void Awake()
    {
        if (_bake == null || !_bake.IsUsable)
        {
            return;
        }

        using (var world = new NavigationWorld(_bake))
        {
            PathRequestHandle request = world.Submit(default);
            world.Tick();
            world.Cancel(request);
            world.Tick();
        }
    }
}
'@
    [IO.File]::WriteAllText(
        (Join-Path $assetsPath 'AreafinderAotProbe.cs'),
        $probeSource,
        [Text.UTF8Encoding]::new($false))

    $buildSource = @'
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AreafinderAotBuild
{
    public static void Build()
    {
        const string scenePath = "Assets/AreafinderAot.unity";
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Areafinder AOT Probe").AddComponent<AreafinderAotProbe>();
        EditorSceneManager.SaveScene(scene, scenePath);

        string backend = Environment.GetEnvironmentVariable("AREAFINDER_SCRIPTING_BACKEND");
        ScriptingImplementation implementation = backend == "IL2CPP"
            ? ScriptingImplementation.IL2CPP
            : ScriptingImplementation.Mono2x;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, implementation);
        PlayerSettings.SetApiCompatibilityLevel(
            NamedBuildTarget.Standalone,
            ApiCompatibilityLevel.NET_Unity_4_8);

        string output = Environment.GetEnvironmentVariable("AREAFINDER_AOT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("AREAFINDER_AOT_OUTPUT is not set.");
        }

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"AOT smoke build failed: {report.summary.result}, " +
                $"{report.summary.totalErrors} error(s).");
        }

        Debug.Log($"Areafinder {backend}/Burst smoke build succeeded: {report.summary.totalSize} bytes.");
    }
}
'@
    [IO.File]::WriteAllText(
        (Join-Path $editorPath 'AreafinderAotBuild.cs'),
        $buildSource,
        [Text.UTF8Encoding]::new($false))

    $buildDirectory = Join-Path $hostProject 'Build'
    New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
    $output = Join-Path $buildDirectory 'AreafinderAot.exe'
    $log = Join-Path $hostProject 'aot-build.log'
    $variations = Join-Path (Split-Path -Parent $resolvedEditor) 'Data\PlaybackEngines\windowsstandalonesupport\Variations'
    $hasIl2Cpp = (Test-Path -LiteralPath $variations -PathType Container) -and
        ($null -ne (Get-ChildItem -LiteralPath $variations -Directory -Filter '*il2cpp*' -ErrorAction SilentlyContinue |
            Select-Object -First 1))
    $backend = if ($hasIl2Cpp) { 'IL2CPP' } else { 'Mono' }
    $previousOutput = [Environment]::GetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', 'Process')
    $previousBackend = [Environment]::GetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', $output, 'Process')
        [Environment]::SetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', $backend, 'Process')
        Invoke-Unity @(
            '-batchmode', '-nographics', '-quit', '-projectPath', $hostProject,
            '-executeMethod', 'AreafinderAotBuild.Build', '-logFile', $log
        )
    }
    finally {
        [Environment]::SetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', $previousOutput, 'Process')
        [Environment]::SetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', $previousBackend, 'Process')
    }

    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw "Unity reported success but did not produce the player: $output"
    }

    $burstArtifact = Get-ChildItem -LiteralPath $buildDirectory -Recurse -File |
        Where-Object { $_.Name -match '(?i)burst.*generated' } |
        Select-Object -First 1
    if ($null -eq $burstArtifact) {
        throw 'The player build did not contain a generated Burst native library.'
    }

    $logText = Get-Content -Raw -LiteralPath $log
    if ($logText -notmatch "Areafinder $backend/Burst smoke build succeeded") {
        throw 'The Unity log does not contain the expected AOT success marker.'
    }

    Write-Host "Areafinder $backend/Burst standalone smoke build passed."
    if (-not $hasIl2Cpp) {
        Write-Warning 'Windows IL2CPP support is not installed; this run validates the Burst player pipeline with the Mono backend.'
    }
    if ($KeepProject) {
        Write-Host "Disposable AOT host retained at $hostProject"
    }
}
finally {
    if (-not $KeepProject -and (Test-Path -LiteralPath $hostProject)) {
        $resolvedHost = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $hostProject).Path)
        if (-not $resolvedHost.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolvedHost)).StartsWith(
                'AreafinderAot-',
                [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected AOT directory: $resolvedHost"
        }

        Remove-Item -LiteralPath $resolvedHost -Recurse -Force
    }
}
