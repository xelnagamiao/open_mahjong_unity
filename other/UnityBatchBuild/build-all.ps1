[CmdletBinding()]
param(
    [ValidateSet('Android', 'WebGL', 'Windows')]
    [string[]]$Targets = @('Android', 'WebGL', 'Windows'),
    [string]$UnityPath,
    [switch]$DryRun,
    [switch]$SyncOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'open_mahjong_unity'
$work = Join-Path $repo '.om_workspace/unity-batch-build'
$copy = Join-Path $work 'Project'
$version = (Select-String -LiteralPath "$source/ProjectSettings/ProjectVersion.txt" -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
if (!$UnityPath) { $UnityPath = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity $version not found: $UnityPath. Supply -UnityPath." }
$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
$config = Get-Content -LiteralPath "$PSScriptRoot/outputs.json" -Raw | ConvertFrom-Json
$profileConfig = Get-Content -LiteralPath "$PSScriptRoot/profiles.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$targetIds = @{ Android = 13; WebGL = 20; Windows = 19 }
$modules = @{ Android = 'AndroidPlayer'; WebGL = 'WebGLSupport'; Windows = 'windowsstandalonesupport' }
$plan = foreach ($target in ($Targets | Select-Object -Unique)) {
    $output = [string]$config.$target
    if (![IO.Path]::IsPathRooted($output)) { $output = Join-Path $repo $output }
    $output = [IO.Path]::GetFullPath($output)
    if ($target -eq 'Android' -and [IO.Path]::GetExtension($output) -ne '.apk') { throw 'Android output must end with .apk.' }
    if ($target -eq 'Windows' -and [IO.Path]::GetExtension($output) -ne '.exe') { throw 'Windows output must end with .exe.' }
    $module = Join-Path (Split-Path $UnityPath) "Data/PlaybackEngines/$($modules[$target])"
    if (!(Test-Path -LiteralPath $module)) { throw "Missing Unity module: $module" }
    $profile = [string]$profileConfig.$target
    $profilePath = [IO.Path]::GetFullPath((Join-Path $source $profile))
    if (!$profilePath.StartsWith($source + '\Assets\', [StringComparison]::OrdinalIgnoreCase)) { throw "Build profile must be inside source Assets: $profile" }
    if (!(Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw "Custom build profile not found: $profilePath. Update profiles.json if renamed/moved." }
    $yaml = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8
    if ($yaml -notmatch "m_BuildTarget: $($targetIds[$target])\s") { throw "Wrong target in profile: $profile" }
    if ($target -eq 'Android' -and $yaml -match 'm_(BuildAppBundle|ExportAsGoogleAndroidProject): 1') { throw 'Selected Android profile is in AAB/Gradle export mode; APK output requires APK mode.' }
    [pscustomobject]@{ Target = $target; Profile = $profile; Output = $output }
}
Write-Host "Unity: $UnityPath"
Write-Host "Source: $source"
Write-Host "Independent copy: $copy"
$plan | Format-Table Target, Output -AutoSize | Out-Host
$plan | Format-Table Target, Profile -AutoSize | Out-Host
if ($DryRun) { Write-Host 'Dry run: no files changed and no build started.'; return }

# No junctions/symlinks: mirror deletion must never reach the development project.
function Assert-NoLinks([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Linked path is not allowed: $cursor"
            }
        }
        $cursor = Split-Path $cursor -Parent
    }
    if (Test-Path -LiteralPath $Path) {
        $link = Get-ChildItem -LiteralPath $Path -Force -Recurse -Attributes ReparsePoint | Select-Object -First 1
        if ($link) { throw "Linked entry is not allowed: $($link.FullName)" }
    }
}
function Sync-Folder([string]$From, [string]$To, [string]$Log) {
    $full = [IO.Path]::GetFullPath($To)
    if (!$full.StartsWith($copy + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Mirror destination is outside the build copy: $full"
    }
    Assert-NoLinks $From
    Assert-NoLinks $To
    & robocopy.exe $From $To /MIR /XJ /R:2 /W:1 /NP /NFL /NDL "/LOG+:$Log" | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed ($LASTEXITCODE): $From. See $Log" }
}

Assert-NoLinks $work
New-Item -ItemType Directory -Path $work -Force | Out-Null
$lock = $null
try {
    try { $lock = [IO.File]::Open((Join-Path $work 'build.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another batch build is running. Wait for it to finish.' }
    # Also reject an independently opened build copy, including an orphaned build process.
    $running = Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($copy.Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0
    }
    if ($running) { throw 'The independent build copy is already open in Unity.' }
    $run = Join-Path $work ('logs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    Write-Host 'Syncing saved project files. Save scenes/assets before starting; avoid edits during this sync.'
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Sync-Folder "$source/$folder" "$copy/$folder" "$run/sync.log"
    }
    $profileSnapshot = foreach ($item in $plan) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source $item.Profile)).Hash
        $copyHash = (Get-FileHash -LiteralPath (Join-Path $copy $item.Profile)).Hash
        if ($sourceHash -ne $copyHash) { throw "Profile changed during sync: $($item.Profile). Save and retry." }
        [pscustomobject]@{ Target = $item.Target; Profile = $item.Profile; SHA256 = $copyHash; Output = $item.Output }
    }
    $profileSnapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$run/profiles.json" -Encoding UTF8
    # Keep the copy's expensive Library cache; transfer only current build preferences.
    New-Item -ItemType Directory -Path "$copy/Library" -Force | Out-Null
    foreach ($file in @('EditorUserBuildSettings.asset', 'BuildProfileContext.asset')) {
        if (Test-Path -LiteralPath "$source/Library/$file") {
            Copy-Item -LiteralPath "$source/Library/$file" -Destination "$copy/Library/$file" -Force
        }
    }
    if (Test-Path -LiteralPath "$source/Library/BuildProfiles") {
        Sync-Folder "$source/Library/BuildProfiles" "$copy/Library/BuildProfiles" "$run/sync.log"
    }
    # Reuse already resolved packages as real copies, never junctions to the source.
    # This avoids repeated downloads/extraction (and Windows UPM rename failures).
    if (Test-Path -LiteralPath "$source/Library/PackageCache") {
        Write-Host 'Syncing resolved package cache...'
        Sync-Folder "$source/Library/PackageCache" "$copy/Library/PackageCache" "$run/sync.log"
    }
    # Relative local packages would otherwise resolve against the copy's new location.
    $manifestPath = "$copy/Packages/manifest.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($dep in $manifest.dependencies.PSObject.Properties) {
        if ([string]$dep.Value -like 'file:*') {
            $local = ([string]$dep.Value).Substring(5)
            if (![IO.Path]::IsPathRooted($local)) {
                $dep.Value = 'file:' + [IO.Path]::GetFullPath((Join-Path "$source/Packages" $local)).Replace('\', '/')
            }
        }
    }
    # Windows PowerShell 5.1 Set-Content -Encoding UTF8 adds a BOM, rejected by UPM.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 50), $utf8NoBom)
    Write-Host "Sync complete. Logs: $run"
    if ($SyncOnly) { return }
    $results = @()
    foreach ($item in $plan) {
        $log = Join-Path $run "$($item.Target).log"
        $started = Get-Date
        Write-Host "Building $($item.Target) -> $($item.Output)"
        $arguments = @('-batchmode', '-quit', '-projectPath', $copy)
        $arguments += @('-activeBuildProfile', $item.Profile, '-build', $item.Output, '-logFile', $log)
        # Windows ProcessStartInfo uses a single command line; quote paths including spaces.
        $quoted = ($arguments | ForEach-Object {
            if ($_ -match '["\r\n]') { throw 'Invalid quote/newline in Unity argument.' }
            '"' + $_ + '"'
        }) -join ' '
        $exitCode = -1
        $errorText = $null
        $process = $null
        try {
            New-Item -ItemType Directory -Path (Split-Path $item.Output -Parent) -Force | Out-Null
            $savedJavaOptions = [Environment]::GetEnvironmentVariable('JAVA_TOOL_OPTIONS', 'Process')
            $savedTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
            $savedTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
            try {
                if ($item.Target -eq 'Android') {
                    $javaTemp = Join-Path $work 'java-tmp'
                    New-Item -ItemType Directory -Path $javaTemp -Force | Out-Null
                    # Avoid redirected Windows/MSIX TEMP breaking Java's AF_UNIX pipe.
                    $env:JAVA_TOOL_OPTIONS = ($savedJavaOptions + ' "-Djdk.net.unixdomain.tmpdir=' + $javaTemp.Replace('\', '/') + '"').Trim()
                    # Unity filters JAVA_TOOL_OPTIONS for Gradle; TEMP/TMP also cover
                    # Java's default socket directory without requiring JVM flags.
                    $env:TEMP = $javaTemp
                    $env:TMP = $javaTemp
                }
                $process = Start-Process -FilePath $UnityPath -ArgumentList $quoted -WindowStyle Hidden -PassThru
            } finally {
                [Environment]::SetEnvironmentVariable('JAVA_TOOL_OPTIONS', $savedJavaOptions, 'Process')
                [Environment]::SetEnvironmentVariable('TEMP', $savedTemp, 'Process')
                [Environment]::SetEnvironmentVariable('TMP', $savedTmp, 'Process')
            }
            $nextProgress = (Get-Date).AddSeconds(30)
            while (!$process.WaitForExit(1000)) {
                if ((Get-Date) -ge $nextProgress) {
                    Write-Host ("{0}: running, {1}s elapsed. Log: {2}" -f $item.Target, [int]((Get-Date) - $started).TotalSeconds, $log)
                    $nextProgress = (Get-Date).AddSeconds(30)
                }
            }
            $process.Refresh()
            $exitCode = $process.ExitCode
            $artifact = if ($item.Target -eq 'WebGL') { Join-Path $item.Output 'index.html' } else { $item.Output }
            if ($exitCode -ne 0) {
                if (Test-Path -LiteralPath $log) {
                    Write-Host "Unity failure log: $log"
                    Get-Content -LiteralPath $log -Tail 20 | Out-Host
                }
                throw "Unity exited with code $exitCode. See $log"
            }
            if (Test-Path -LiteralPath $log) {
                if (Select-String -LiteralPath $log -Pattern 'Build Finished, Result: Failure|Build completed with a result of .Failed' -Quiet) {
                    $details = Select-String -LiteralPath $log -Pattern 'What went wrong:' -Context 0,3 | Select-Object -First 1
                    if ($details) { Write-Host $details.ToString() }
                    throw "Unity reported build failure despite exit code 0. See $log"
                }
            }
            if (!(Test-Path -LiteralPath $artifact)) { throw "Missing output: $artifact" }
            # Incremental builds may reuse the launcher/template without changing its
            # timestamp. The fresh per-run Unity build result is authoritative.
            if (!(Test-Path -LiteralPath $log) -or !(Select-String -LiteralPath $log -Pattern 'Build Finished, Result: Success|Build completed with a result of .Succeeded' -Quiet)) {
                throw "No explicit successful build result in Unity log: $log"
            }
        } catch { $errorText = $_.Exception.Message }
        finally {
            if ($process) {
                if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
                $process.Dispose()
            }
        }
        $status = if ($errorText) { 'Failed' } else { 'Succeeded' }
        $results += [pscustomobject]@{ Target = $item.Target; Profile = $item.Profile; Status = $status; ExitCode = $exitCode; Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds); Output = $item.Output; Log = $log; Error = $errorText }
        $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$run/results.json" -Encoding UTF8
        Write-Host "$($item.Target): $status $errorText"
    }
    $results | Format-Table Target, Status, Seconds -AutoSize | Out-Host
    if ($results | Where-Object Status -eq 'Failed') { throw "One or more builds failed. See $run/results.json" }
} finally {
    if ($lock) { $lock.Dispose() }
}
