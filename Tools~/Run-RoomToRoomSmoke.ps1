param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.7.0a6\Editor\Unity.exe',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$KeepProject
)

$ErrorActionPreference = 'Stop'

$resolvedEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$hostProject = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('AreafinderSample-' + [Guid]::NewGuid().ToString('N'))))
if (-not $hostProject.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create a sample project outside the system temporary directory: $hostProject"
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

$probeSource = @'
using System;
using System.IO;
using NotRealGames.Areafinder;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

public static class AreafinderRoomToRoomProbe
{
    private const string PackageName = "com.notrealgames.areafinder";
    private const string SuccessMarker = "AREAFINDER_ROOM_TO_ROOM_SUCCESS";

    [Serializable]
    private sealed class Manifest
    {
        public string version;
    }

    [Serializable]
    private sealed class Result
    {
        public string marker;
        public string packageVersion;
        public string importPath;
        public int areaCount;
        public int portalCount;
        public int polygonCount;
    }

    public static void Import()
    {
        try
        {
            string version = ReadVersion();
            Sample selected = default;
            bool found = false;
            foreach (Sample sample in Sample.FindByPackage(PackageName, version))
            {
                if (sample.displayName == "Room to Room")
                {
                    selected = sample;
                    found = true;
                    break;
                }
            }

            if (!found || !selected.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                throw new InvalidOperationException("The Room to Room package sample could not be imported.");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void Verify()
    {
        try
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/Areafinder/Samples/Create Room-to-Room Demo"))
            {
                throw new InvalidOperationException("The imported sample setup command was not registered.");
            }

            AssetDatabase.SaveAssets();
            NavigationWorldAsset world = FindAsset<NavigationWorldAsset>("Room-to-Room World");
            NavigationBakeAsset bake = FindAsset<NavigationBakeAsset>("Room-to-Room World Bake");
            TraversalPolicyAsset policy = FindAsset<TraversalPolicyAsset>("Walker Policy");
            if (world == null || bake == null || policy == null ||
                world.Areas.Count != 2 || world.Portals.Count != 1 || world.Policies.Count != 1 ||
                !bake.IsUsable || bake.IsStale(world))
            {
                throw new InvalidOperationException("The generated sample assets are incomplete or stale.");
            }

            if (!CompiledTraversalPolicy.TryCompile(policy, bake, out CompiledTraversalPolicy compiled, out string error))
            {
                throw new InvalidOperationException(error);
            }

            using (var runtime = new NavigationWorld(bake))
            {
                var query = new PathQuery(
                    new NavigationLocation(world.Areas[0].Id, new Vector3(-1f, 0f, 0f)),
                    new NavigationLocation(world.Areas[1].Id, new Vector3(1f, 0f, 0f)),
                    compiled);
                PathRequestHandle handle = runtime.Submit(query);
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10d);
                while (!IsTerminal(runtime.GetStatus(handle)) && DateTime.UtcNow < deadline)
                {
                    runtime.Tick(64);
                    System.Threading.Thread.Yield();
                }

                if (runtime.GetStatus(handle) != PathRequestStatus.Completed ||
                    !runtime.TryGetPath(handle, out NavigationPathView path) ||
                    path.AreaCount != 2 || path.PortalTransitionCount != 1 ||
                    path.PolygonCount != 2 || !runtime.IsCurrent(path))
                {
                    throw new InvalidOperationException("The generated sample did not complete its cross-Area route.");
                }
            }

            string worldPath = AssetDatabase.GetAssetPath(world);
            string demoFolder = Path.GetDirectoryName(worldPath).Replace('\\', '/');
            if (!File.Exists($"{demoFolder}/RoomToRoom.unity") ||
                GameObject.Find("Areafinder Path Preview") == null)
            {
                throw new InvalidOperationException("The generated sample scene or preview object is missing.");
            }

            string output = Environment.GetEnvironmentVariable("AREAFINDER_SAMPLE_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                throw new InvalidOperationException("AREAFINDER_SAMPLE_OUTPUT is not set.");
            }

            File.WriteAllText(output, JsonUtility.ToJson(new Result
            {
                marker = SuccessMarker,
                packageVersion = ReadVersion(),
                importPath = SampleImportPath(),
                areaCount = world.Areas.Count,
                portalCount = world.Portals.Count,
                polygonCount = bake.Polygons.Count
            }, true));
            Debug.Log(SuccessMarker);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static bool IsTerminal(PathRequestStatus status)
    {
        return status == PathRequestStatus.Completed || status == PathRequestStatus.Failed ||
               status == PathRequestStatus.Cancelled || status == PathRequestStatus.Stale;
    }

    private static string ReadVersion()
    {
        string json = File.ReadAllText($"Packages/{PackageName}/package.json");
        Manifest manifest = JsonUtility.FromJson<Manifest>(json);
        if (manifest == null || string.IsNullOrEmpty(manifest.version))
        {
            throw new InvalidOperationException("The installed package version could not be read.");
        }

        return manifest.version;
    }

    private static string SampleImportPath()
    {
        return $"Assets/Samples/Areafinder/{ReadVersion()}/Room to Room";
    }

    private static T FindAsset<T>(string name) where T : UnityEngine.Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets" }))
        {
            T candidate = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.name == name)
            {
                return candidate;
            }
        }

        return null;
    }
}
'@

try {
    Invoke-Unity @('-batchmode', '-nographics', '-quit', '-createProject', $hostProject, '-logFile', '-')

    $manifestPath = Join-Path $hostProject 'Packages\manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable
    $manifest.dependencies['com.notrealgames.areafinder'] = 'file:' + $resolvedPackage.Replace('\', '/')
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    $editorFolder = Join-Path $hostProject 'Assets\Editor'
    New-Item -ItemType Directory -Force -Path $editorFolder | Out-Null
    Set-Content -LiteralPath (Join-Path $editorFolder 'AreafinderRoomToRoomProbe.cs') -Value $probeSource -Encoding utf8NoBOM
    $logs = Join-Path $hostProject 'Logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null

    Invoke-Unity @(
        '-batchmode', '-nographics', '-projectPath', $hostProject,
        '-executeMethod', 'AreafinderRoomToRoomProbe.Import',
        '-logFile', (Join-Path $logs 'sample-import.log')
    )

    $resultPath = Join-Path $hostProject 'room-to-room-result.json'
    $previousOutput = [Environment]::GetEnvironmentVariable('AREAFINDER_SAMPLE_OUTPUT', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('AREAFINDER_SAMPLE_OUTPUT', $resultPath, 'Process')
        Invoke-Unity @(
            '-batchmode', '-nographics', '-projectPath', $hostProject,
            '-executeMethod', 'AreafinderRoomToRoomProbe.Verify',
            '-logFile', (Join-Path $logs 'sample-verify.log')
        )
    }
    finally {
        [Environment]::SetEnvironmentVariable('AREAFINDER_SAMPLE_OUTPUT', $previousOutput, 'Process')
    }

    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "The Room-to-Room probe did not produce a result: $resultPath"
    }

    $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    if ($result.marker -ne 'AREAFINDER_ROOM_TO_ROOM_SUCCESS' -or
        [int]$result.areaCount -ne 2 -or [int]$result.portalCount -ne 1 -or
        [int]$result.polygonCount -ne 2) {
        throw "The Room-to-Room probe result is invalid: $resultPath"
    }

    Write-Host "Areafinder Room-to-Room import/setup proof passed for package $($result.packageVersion)."
    if ($KeepProject) {
        Write-Host "Disposable sample host retained at $hostProject"
    }
}
finally {
    if (-not $KeepProject -and (Test-Path -LiteralPath $hostProject)) {
        $resolvedHost = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $hostProject).Path)
        if (-not $resolvedHost.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolvedHost)).StartsWith('AreafinderSample-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected sample directory: $resolvedHost"
        }

        Remove-Item -LiteralPath $resolvedHost -Recurse -Force
    }
}
