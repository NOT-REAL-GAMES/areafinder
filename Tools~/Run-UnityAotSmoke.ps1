param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.7.0a6\Editor\Unity.exe',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('Mono', 'IL2CPP')]
    [string[]]$Backends = @('Mono', 'IL2CPP'),
    [ValidateRange(1, 600)]
    [int]$PlayerTimeoutSeconds = 120,
    [switch]$KeepProject
)

$ErrorActionPreference = 'Stop'

$resolvedEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$hostProject = [IO.Path]::GetFullPath(
    (Join-Path $temporaryRoot ('AreafinderAot-' + [Guid]::NewGuid().ToString('N'))))
$requestedBackends = @($Backends | Select-Object -Unique)

if (-not $hostProject.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create an AOT project outside the system temporary directory: $hostProject"
}

$variations = Join-Path (Split-Path -Parent $resolvedEditor) 'Data\PlaybackEngines\windowsstandalonesupport\Variations'
$hasIl2Cpp = (Test-Path -LiteralPath $variations -PathType Container) -and
    ($null -ne (Get-ChildItem -LiteralPath $variations -Directory -Filter '*il2cpp*' -ErrorAction SilentlyContinue |
        Select-Object -First 1))
if ($requestedBackends -contains 'IL2CPP' -and -not $hasIl2Cpp) {
    throw 'Windows IL2CPP support is required for the requested player proof but is not installed.'
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

function Invoke-PlayerProof {
    param(
        [string]$Backend,
        [string]$Executable,
        [string]$BuildDirectory
    )

    $marker = Join-Path $BuildDirectory 'areafinder-result.json'
    $log = Join-Path $BuildDirectory 'player.log'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Environment['AREAFINDER_RESULT_PATH'] = $marker
    foreach ($argument in @('-batchmode', '-nographics', '-logFile', $log)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "The $Backend player did not start."
    }

    if (-not $process.WaitForExit($PlayerTimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "The $Backend player did not finish within $PlayerTimeoutSeconds seconds."
    }

    if ($process.ExitCode -ne 0) {
        $tail = if (Test-Path -LiteralPath $log) {
            (Get-Content -LiteralPath $log -Tail 40) -join [Environment]::NewLine
        }
        else {
            'The player did not create a log.'
        }
        throw "$Backend player proof exited with code $($process.ExitCode).$([Environment]::NewLine)$tail"
    }

    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "The $Backend player did not write its navigation result marker: $marker"
    }

    $result = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
    if (-not $result.success -or $result.backend -ne $Backend -or
        [int]$result.areaCount -ne 2 -or [int]$result.portalCount -ne 1 -or
        [int]$result.polygonCount -lt 4 -or [int]$result.crossingCount -lt 2 -or
        [int]$result.steeringCount -lt 2 -or -not $result.current) {
        throw "$Backend player returned an invalid navigation result: $($result | ConvertTo-Json -Compress)"
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
using System;
using System.IO;
using NotRealGames.Areafinder;
using UnityEngine;

public sealed class AreafinderAotProbe : MonoBehaviour
{
    [Serializable]
    private sealed class ProbeResult
    {
        public bool success;
        public string backend;
        public int areaCount;
        public int portalCount;
        public int polygonCount;
        public int crossingCount;
        public int steeringCount;
        public bool current;
        public string error;
    }

    [SerializeField] private NavigationBakeAsset _bake;
    [SerializeField] private TraversalPolicyAsset _policy;
    [SerializeField] private AreaId _startArea;
    [SerializeField] private AreaId _goalArea;
    [SerializeField] private Vector3 _start;
    [SerializeField] private Vector3 _goal;
    [SerializeField] private string _backend;

    private NavigationWorld _world;
    private PathRequestHandle _request;
    private int _frames;
    private bool _finished;

    public void Configure(
        NavigationBakeAsset bake,
        TraversalPolicyAsset policy,
        AreaId startArea,
        AreaId goalArea,
        Vector3 start,
        Vector3 goal,
        string backend)
    {
        _bake = bake;
        _policy = policy;
        _startArea = startArea;
        _goalArea = goalArea;
        _start = start;
        _goal = goal;
        _backend = backend;
    }

    private void Awake()
    {
        Application.runInBackground = true;
        try
        {
            if (_bake == null || !_bake.IsUsable)
            {
                Fail("The serialized bake is missing or unusable.");
                return;
            }

            if (!CompiledTraversalPolicy.TryCompile(_policy, _bake, out CompiledTraversalPolicy policy, out string error))
            {
                Fail("Policy compilation failed: " + error);
                return;
            }

            _world = new NavigationWorld(_bake);
            _request = _world.Submit(new PathQuery(
                new NavigationLocation(_startArea, _start),
                new NavigationLocation(_goalArea, _goal),
                policy));
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private void Update()
    {
        if (_finished || _world == null)
        {
            return;
        }

        try
        {
            if (++_frames > 300)
            {
                Fail("The request did not finish within 300 frames.");
                return;
            }

            _world.Tick(64);
            PathRequestStatus status = _world.GetStatus(_request);
            if (status == PathRequestStatus.Completed)
            {
                if (!_world.TryGetPath(_request, out NavigationPathView path))
                {
                    Fail("The completed request did not expose a path.");
                    return;
                }

                var result = new ProbeResult
                {
                    success = path.IsValid && path.AreaCount == 2 && path.PortalTransitionCount == 1 &&
                              path.PolygonCount >= 4 && path.CrossingSpanCount >= 2 &&
                              path.SteeringTargetCount >= 2,
                    backend = _backend,
                    areaCount = path.AreaCount,
                    portalCount = path.PortalTransitionCount,
                    polygonCount = path.PolygonCount,
                    crossingCount = path.CrossingSpanCount,
                    steeringCount = path.SteeringTargetCount,
                    current = _world.IsCurrent(path)
                };
                result.success &= result.current;
                WriteResultAndQuit(result, result.success ? 0 : 1);
            }
            else if (status == PathRequestStatus.Failed || status == PathRequestStatus.Cancelled ||
                     status == PathRequestStatus.Stale || status == PathRequestStatus.Invalid)
            {
                _world.TryGetFailure(_request, out PathFailureReason failure);
                Fail("The request terminated as " + status + "/" + failure + ".");
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private void Fail(string error)
    {
        WriteResultAndQuit(new ProbeResult
        {
            success = false,
            backend = _backend,
            error = error
        }, 1);
    }

    private void WriteResultAndQuit(ProbeResult result, int exitCode)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        string path = Environment.GetEnvironmentVariable("AREAFINDER_RESULT_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            File.WriteAllText(path, JsonUtility.ToJson(result));
        }

        if (result.success)
        {
            Debug.Log("AREAFINDER_PLAYER_PROOF " + JsonUtility.ToJson(result));
        }
        else
        {
            Debug.LogError("AREAFINDER_PLAYER_PROOF " + JsonUtility.ToJson(result));
        }

        _world?.Dispose();
        _world = null;
        Application.Quit(exitCode);
    }

    private void OnDestroy()
    {
        _world?.Dispose();
    }
}
'@
    [IO.File]::WriteAllText(
        (Join-Path $assetsPath 'AreafinderAotProbe.cs'),
        $probeSource,
        [Text.UTF8Encoding]::new($false))

    $buildSource = @'
using System;
using System.Reflection;
using NotRealGames.Areafinder;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AreafinderAotBuild
{
    private const string GeneratedRoot = "Assets/AreafinderAotGenerated";
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Build()
    {
        string backend = Environment.GetEnvironmentVariable("AREAFINDER_SCRIPTING_BACKEND");
        string output = Environment.GetEnvironmentVariable("AREAFINDER_AOT_OUTPUT");
        if ((backend != "Mono" && backend != "IL2CPP") || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("The player backend or output path is not configured.");
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(GeneratedRoot);
        AssetDatabase.CreateFolder("Assets", "AreafinderAotGenerated");

        SemanticRegistryAsset registry = CreateAsset<SemanticRegistryAsset>("Registry.asset");
        registry.Add("Walkable");

        NavigationAreaAsset firstArea = CreateAsset<NavigationAreaAsset>("FirstArea.asset");
        AddPolygon(firstArea, Rectangle(0f, 1f));
        NavigationPolygonRecord firstExit = AddPolygon(firstArea, Rectangle(1f, 2f));

        NavigationAreaAsset secondArea = CreateAsset<NavigationAreaAsset>("SecondArea.asset");
        NavigationPolygonRecord secondEntry = AddPolygon(secondArea, Rectangle(0f, 1f));
        AddPolygon(secondArea, Rectangle(1f, 2f));

        TraversalPolicyAsset policy = CreateAsset<TraversalPolicyAsset>("Policy.asset");
        policy.SetRegistry(registry);

        NavigationWorldAsset world = CreateAsset<NavigationWorldAsset>("World.asset");
        Invoke(world, "SetSemanticRegistry", registry);
        Invoke(world, "AddArea", firstArea);
        Invoke(world, "AddArea", secondArea);
        Invoke(world, "AddPolicy", policy);
        Invoke(world, "AddPortal",
            Span(firstArea, firstExit, 2),
            Span(secondArea, secondEntry, 0),
            PortalDirection.Bidirectional,
            1d,
            new PortalTransform(new Double3(-2d, 0d, 0d), Quaternion.identity));

        NavigationBakeAsset bake = CreateAsset<NavigationBakeAsset>("Bake.asset");
        NavigationBakeResult bakeResult = NavigationBaker.Bake(world, bake);
        if (!bakeResult.Succeeded || !bake.IsUsable)
        {
            throw new InvalidOperationException("The generated player fixture did not bake successfully.");
        }

        var probe = new GameObject("Areafinder AOT Probe").AddComponent<AreafinderAotProbe>();
        probe.Configure(
            bake,
            policy,
            firstArea.Id,
            secondArea.Id,
            new Vector3(0.25f, 0f, 0.5f),
            new Vector3(1.75f, 0f, 0.5f),
            backend);
        string scenePath = GeneratedRoot + "/AreafinderAot.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorUtility.SetDirty(registry);
        EditorUtility.SetDirty(firstArea);
        EditorUtility.SetDirty(secondArea);
        EditorUtility.SetDirty(policy);
        EditorUtility.SetDirty(world);
        EditorUtility.SetDirty(bake);
        AssetDatabase.SaveAssets();

        ScriptingImplementation implementation = backend == "IL2CPP"
            ? ScriptingImplementation.IL2CPP
            : ScriptingImplementation.Mono2x;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, implementation);
        PlayerSettings.SetApiCompatibilityLevel(
            NamedBuildTarget.Standalone,
            ApiCompatibilityLevel.NET_Unity_4_8);

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
                $"AOT player build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");
        }

        Debug.Log($"Areafinder {backend}/Burst player build succeeded: {report.summary.totalSize} bytes.");
    }

    private static T CreateAsset<T>(string name) where T : ScriptableObject
    {
        T value = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(value, GeneratedRoot + "/" + name);
        return value;
    }

    private static NavigationPolygonRecord AddPolygon(NavigationAreaAsset area, Vector3[] vertices)
    {
        return (NavigationPolygonRecord)Invoke(area, "AddPolygon", (object)vertices);
    }

    private static object Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo member = target.GetType().GetMethod(method, HiddenInstance);
        if (member == null)
        {
            throw new MissingMethodException(target.GetType().FullName, method);
        }

        return member.Invoke(target, arguments);
    }

    private static Vector3[] Rectangle(float minimumX, float maximumX)
    {
        return new[]
        {
            new Vector3(minimumX, 0f, 0f),
            new Vector3(minimumX, 0f, 1f),
            new Vector3(maximumX, 0f, 1f),
            new Vector3(maximumX, 0f, 0f)
        };
    }

    private static PortalEntrySpan Span(
        NavigationAreaAsset area,
        NavigationPolygonRecord polygon,
        int edge)
    {
        NavigationVertexRecord first = polygon.Vertices[edge];
        NavigationVertexRecord second = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
        return new PortalEntrySpan(
            area.Id,
            polygon.Id,
            first.OutgoingEdgeId,
            first.Position,
            second.Position);
    }
}
'@
    [IO.File]::WriteAllText(
        (Join-Path $editorPath 'AreafinderAotBuild.cs'),
        $buildSource,
        [Text.UTF8Encoding]::new($false))

    foreach ($backend in $requestedBackends) {
        $buildDirectory = Join-Path $hostProject (Join-Path 'Build' $backend)
        New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
        $output = Join-Path $buildDirectory 'AreafinderAot.exe'
        $buildLog = Join-Path $buildDirectory 'aot-build.log'
        $previousOutput = [Environment]::GetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', 'Process')
        $previousBackend = [Environment]::GetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', $output, 'Process')
            [Environment]::SetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', $backend, 'Process')
            Invoke-Unity @(
                '-batchmode', '-nographics', '-quit', '-projectPath', $hostProject,
                '-executeMethod', 'AreafinderAotBuild.Build', '-logFile', $buildLog
            )
        }
        finally {
            [Environment]::SetEnvironmentVariable('AREAFINDER_AOT_OUTPUT', $previousOutput, 'Process')
            [Environment]::SetEnvironmentVariable('AREAFINDER_SCRIPTING_BACKEND', $previousBackend, 'Process')
        }

        if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
            throw "Unity reported success but did not produce the $backend player: $output"
        }

        $burstArtifact = Get-ChildItem -LiteralPath $buildDirectory -Recurse -File |
            Where-Object { $_.Name -match '(?i)burst.*generated' } |
            Select-Object -First 1
        if ($null -eq $burstArtifact) {
            throw "The $backend player did not contain a generated Burst native library."
        }

        $logText = Get-Content -Raw -LiteralPath $buildLog
        if ($logText -notmatch "Areafinder $backend/Burst player build succeeded") {
            throw "The $backend build log does not contain the expected success marker."
        }

        Invoke-PlayerProof -Backend $backend -Executable $output -BuildDirectory $buildDirectory
        Write-Host "Areafinder $backend/Burst executable player proof passed."
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
