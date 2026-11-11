[CmdletBinding()]
param(
    [string[]]$Targets,
    [string]$UnityPath,
    [string]$AndroidUnityPath,
    [string]$OutputRoot,
    [string]$WorkRoot,
    [switch]$Validation,
    [int]$ValidationRelease = 0,
    [switch]$DryRun,
    [switch]$SyncOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'open_mahjong_unity'
$work = if ($WorkRoot) { [IO.Path]::GetFullPath($WorkRoot) } else { Join-Path $repo '.om_workspace/unity-batch-build' }
if (!$work.Replace('\','/').Contains('/.om_workspace/')) { throw 'WorkRoot must be inside a .om_workspace directory.' }
$copy = Join-Path $work 'Project'
$version = (Select-String -LiteralPath "$source/ProjectSettings/ProjectVersion.txt" -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
if (!$UnityPath) { $UnityPath = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (!$AndroidUnityPath) { $AndroidUnityPath = $UnityPath }
$config = Get-Content -LiteralPath "$PSScriptRoot/outputs.json" -Raw | ConvertFrom-Json
$profileConfig = Get-Content -LiteralPath "$PSScriptRoot/profiles.json" -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$Targets) {
    Write-Host '1. Windows'
    Write-Host '2. Android APK + complete hot-update package'
    Write-Host '3. WebGL'
    Write-Host '4. Android complete hot-update package only (existing APK baseline required)'
    $selection = Read-Host 'Choose one or more numbers, separated by commas (Enter = 1,2,3)'
    if (!$selection) { $selection = '1,2,3' }
    $menu = @{ '1'='Windows'; '2'='Android'; '3'='WebGL'; '4'='AndroidHotUpdate' }
    $Targets = foreach ($number in ($selection -split '[,\s]+')) {
        if (!$menu.ContainsKey($number)) { throw "Unknown menu option: $number" }
        $menu[$number]
    }
}
$Targets = @($Targets | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
foreach ($target in $Targets) { if ($target -notin @('Windows','Android','WebGL','AndroidHotUpdate')) { throw "Unknown target: $target" } }
if ($Targets -contains 'Android' -and $Targets -contains 'AndroidHotUpdate') {
    Write-Host 'Android already includes the complete hot-update package; skipping the duplicate content build.'
    $Targets = @($Targets | Where-Object { $_ -ne 'AndroidHotUpdate' })
}
if (($Targets -contains 'Windows' -or $Targets -contains 'WebGL') -and !$DryRun -and !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $version not found: $UnityPath. Supply -UnityPath."
}
if (Test-Path -LiteralPath $UnityPath -PathType Leaf) { $UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path }
if ($Targets -contains 'AndroidHotUpdate' -and !$DryRun -and !$SyncOnly) {
    $gameConfig = [IO.File]::ReadAllText((Join-Path $source 'Assets/Scripts/Config/ConfigManager.cs'))
    $base = [regex]::Match($gameConfig, 'base_version\s*=\s*(\d+)').Groups[1].Value
    $baselineFolder = if ($Validation) { 'validation-baselines' } else { 'baselines' }
    if (!$base -or !(Test-Path -LiteralPath (Join-Path $work "$baselineFolder/$base/baseline.json"))) {
        throw 'No Android framework baseline exists. Build Android APK + content first; then use AndroidHotUpdate.'
    }
}
$targetIds = @{ Android = 13; AndroidHotUpdate = 13; WebGL = 20; Windows = 19 }
$modules = @{ Android = 'AndroidPlayer'; AndroidHotUpdate = 'AndroidPlayer'; WebGL = 'WebGLSupport'; Windows = 'windowsstandalonesupport' }
$plan = foreach ($target in ($Targets | Select-Object -Unique)) {
    $editor = if ($target -in @('Android','AndroidHotUpdate')) { $AndroidUnityPath } else { $UnityPath }
    if (!(Test-Path -LiteralPath $editor -PathType Leaf) -and !$DryRun) { throw "Required Unity $version editor is missing: $editor. Supply -UnityPath (or -AndroidUnityPath for an explicit override)." }
    $output = if ($target -eq 'AndroidHotUpdate') { Split-Path ([string]$config.Android) -Parent } else { [string]$config.$target }
    if ($OutputRoot) {
        $output = switch ($target) {
            'Android' { Join-Path $OutputRoot 'Android/open_mahjong_unity.apk' }
            'AndroidHotUpdate' { Join-Path $OutputRoot 'Android' }
            'WebGL' { Join-Path $OutputRoot 'WebGL' }
            'Windows' { Join-Path $OutputRoot 'Windows/open_mahjong_unity.exe' }
        }
    }
    if (![IO.Path]::IsPathRooted($output)) { $output = Join-Path $repo $output }
    $output = [IO.Path]::GetFullPath($output)
    if ($target -eq 'Android' -and [IO.Path]::GetExtension($output) -ne '.apk') { throw 'Android output must end with .apk.' }
    if ($target -eq 'Windows' -and [IO.Path]::GetExtension($output) -ne '.exe') { throw 'Windows output must end with .exe.' }
    $module = Join-Path (Split-Path $editor) "Data/PlaybackEngines/$($modules[$target])"
    if (!(Test-Path -LiteralPath $module) -and !$DryRun) { throw "Missing Unity module: $module" }
    $profile = if ($target -eq 'AndroidHotUpdate') { [string]$profileConfig.Android } else { [string]$profileConfig.$target }
    $profilePath = [IO.Path]::GetFullPath((Join-Path $source $profile))
    if (!$profilePath.StartsWith($source + '\Assets\', [StringComparison]::OrdinalIgnoreCase)) { throw "Build profile must be inside source Assets: $profile" }
    if (!(Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw "Custom build profile not found: $profilePath. Update profiles.json if renamed/moved." }
    $yaml = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8
    if ($yaml -notmatch "m_BuildTarget: $($targetIds[$target])\s") { throw "Wrong target in profile: $profile" }
    if ($target -eq 'Android' -and $yaml -match 'm_(BuildAppBundle|ExportAsGoogleAndroidProject): 1') { throw 'Selected Android profile is in AAB/Gradle export mode; APK output requires APK mode.' }
    [pscustomobject]@{ Target = $target; Profile = $profile; Output = $output; Editor = $editor }
}
$copy = if ($plan[0].Target -in @('Android','AndroidHotUpdate')) { Join-Path $work 'AndroidProject' } else { Join-Path $work 'Project' }
Write-Host "Unity: $UnityPath"
Write-Host "Source: $source"
Write-Host "Independent copy: $copy"
$plan | Format-Table Target, Output -AutoSize | Out-Host
$plan | Format-Table Target, Profile -AutoSize | Out-Host
$plan | Format-Table Target, Editor -AutoSize | Out-Host
if ($DryRun) { Write-Host 'Dry run: no files changed and no build started.'; return }

# An initial IL2CPP/HybridCLR export temporarily duplicates the native player.
# Stop before syncing or compiling when the cache volume cannot hold it.
if ($Targets -contains 'Android' -or $Targets -contains 'AndroidHotUpdate') {
    $cacheDrive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($work))
    $minimumFree = if ($Targets -contains 'Android') { 24GB } else { 8GB }
    if ($cacheDrive.AvailableFreeSpace -lt $minimumFree) {
        throw ('Insufficient cache disk space: {0:N1} GB free; {1:N0} GB required. Use -WorkRoot on a larger drive, inside .om_workspace.' -f ($cacheDrive.AvailableFreeSpace / 1GB), ($minimumFree / 1GB))
    }
}

# Do not rely only on finally: closing the console can terminate PowerShell
# without unwinding it, leaving Start-Process children building in the background.
if (-not ('UnityBatchBuild.OwnedProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'OwnedProcess.cs')
}

# No junctions/symlinks: mirror deletion must never reach the development project.
function Assert-NoLinks([string]$Path, [bool]$RootOnly = $false) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Linked path is not allowed: $cursor"
            }
        }
        $cursor = Split-Path $cursor -Parent
    }
    if (!$RootOnly -and (Test-Path -LiteralPath $Path)) {
        $link = Get-ChildItem -LiteralPath $Path -Force -Recurse -Attributes ReparsePoint | Select-Object -First 1
        if ($link) { throw "Linked entry is not allowed: $($link.FullName)" }
    }
}
function Sync-Folder([string]$From, [string]$To, [string]$Log) {
    $full = [IO.Path]::GetFullPath($To)
    if (!$full.StartsWith($copy + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Mirror destination is outside the build copy: $full"
    }
    Assert-NoLinks $From $true
    Assert-NoLinks $To
    # Local test junctions must not be followed, copied, or removed. Explicitly
    # exclude both directory and file reparse points in addition to /XJ.
    $links = @(Get-ChildItem -LiteralPath $From -Force -Recurse -Attributes ReparsePoint)
    $directories = @($links | Where-Object { $_.PSIsContainer } | ForEach-Object FullName)
    $files = @($links | Where-Object { !$_.PSIsContainer } | ForEach-Object FullName)
    $copyArgs = @($From, $To, '/MIR', '/XJ', '/R:2', '/W:1', '/NP', '/NFL', '/NDL', '/XF', 'README*') + $files
    if ($directories.Count) { $copyArgs += @('/XD') + $directories }
    $copyArgs += "/LOG+:$Log"
    & robocopy.exe @copyArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed ($LASTEXITCODE): $From. See $Log" }
}

function Initialize-ApkVersionCounter([string]$Counter, [string[]]$PreviousApks) {
    $floor = 0
    if (Test-Path -LiteralPath $Counter -PathType Leaf) {
        $floor = [int]([IO.File]::ReadAllText($Counter).Trim())
        if ($floor -lt 1) { throw 'Invalid cached Android APK version code.' }
    }
    foreach ($apk in ($PreviousApks | Select-Object -Unique)) {
        if (!(Test-Path -LiteralPath $apk -PathType Leaf)) { continue }
        $sidecar = Join-Path (Split-Path $apk -Parent) 'android-update.json'
        if (!(Test-Path -LiteralPath $sidecar -PathType Leaf)) { throw "Cannot determine the previous APK version code: $sidecar is missing." }
        $previous = Get-Content -LiteralPath $sidecar -Raw -Encoding UTF8 | ConvertFrom-Json
        $apkInfo = Get-Item -LiteralPath $apk
        if ($previous.schemaVersion -ne 1 -or $previous.versionCode -lt 1 -or
            $previous.versionCode -ge [int]::MaxValue -or $previous.versionCode -ne [math]::Truncate($previous.versionCode) -or
            $previous.file -ne $apkInfo.Name -or $previous.sizeBytes -ne $apkInfo.Length -or
            $previous.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
            (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash -ne $previous.sha256) {
            throw "Previous APK and update manifest do not agree: $apk"
        }
        $floor = [math]::Max($floor, [int]$previous.versionCode)
    }
    if ($floor -gt 0) {
        [IO.File]::WriteAllText($Counter, [string]$floor, (New-Object Text.UTF8Encoding($false)))
        Write-Host "Android APK version code floor: $floor"
    }
}

Assert-NoLinks $work $true
New-Item -ItemType Directory -Path $work -Force | Out-Null
$lock = $null
try {
    try { $lock = [IO.File]::Open((Join-Path $work 'build.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another batch build is running. Wait for it to finish.' }
    # Also reject an independently opened build copy, including an orphaned build process.
    $running = Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Where-Object {
        $_.CommandLine -and ($_.CommandLine.Replace('/', '\').IndexOf((Join-Path $work 'Project').Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $_.CommandLine.Replace('/', '\').IndexOf((Join-Path $work 'AndroidProject').Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0)
    }
    if ($running) { throw 'The independent build copy is already open in Unity.' }
    $run = Join-Path $work ('logs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    # Editor bakers locate these shared inputs by walking project ancestors.
    # Copy the inputs too when WorkRoot is on another volume; never edit them.
    foreach ($relative in @('other/麻将规则支持表.xlsx', 'open_mahjong_server/server/database/statistics_rules.json')) {
        $inputFile = Join-Path $repo $relative
        $copiedInput = Join-Path $work $relative
        Assert-NoLinks $copiedInput $true
        New-Item -ItemType Directory -Path (Split-Path $copiedInput -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $inputFile -Destination $copiedInput -Force
        if ((Get-FileHash -LiteralPath $inputFile).Hash -ne (Get-FileHash -LiteralPath $copiedInput).Hash) {
            throw "Shared build input changed while copying: $relative"
        }
    }
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
    if ((Test-Path -LiteralPath "$source/Library/PackageCache") -and
        !($copy.EndsWith('\AndroidProject') -and (Test-Path -LiteralPath "$copy/Library/PackageCache"))) {
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
    $syncedProjects = @{}
    $syncedProjects[$copy] = $true
    foreach ($item in $plan) {
        $copy = if ($item.Target -in @('Android','AndroidHotUpdate')) { Join-Path $work 'AndroidProject' } else { Join-Path $work 'Project' }
        # Every target gets a fresh source sync. Android adaptation must never
        # leak rewritten Resources calls or its embedded SDK into Windows/WebGL.
        if (!$syncedProjects.ContainsKey($copy)) {
            foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
                Sync-Folder "$source/$folder" "$copy/$folder" "$run/sync.log"
            }
            $syncedProjects[$copy] = $true
        }
        # Resolve local packages again after each target's fresh source sync.
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
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 50), $utf8NoBom)
        if ($item.Target -in @('Android','AndroidHotUpdate') -and !(Test-Path -LiteralPath "$copy/Library/PackageCache")) {
            Sync-Folder "$source/Library/PackageCache" "$copy/Library/PackageCache" "$run/sync.log"
        }
        if ($item.Target -in @('Android', 'AndroidHotUpdate')) {
            # All targets use the source project's Editor version. Android-only
            # SDK preparation still stays in its isolated build copy.
            & "$PSScriptRoot/prepare-android-content.ps1" -Project $copy -Work $work -Validation:$Validation -ValidationRelease $ValidationRelease
        }
        if ($item.Target -eq 'Android' -and !$Validation) {
            $previousApk = [string]$config.Android
            if (![IO.Path]::IsPathRooted($previousApk)) { $previousApk = Join-Path $repo $previousApk }
            Initialize-ApkVersionCounter (Join-Path $work 'apk-version-code.txt') @($previousApk, $item.Output)
        }
        $log = Join-Path $run "$($item.Target).log"
        $started = Get-Date
        Write-Host "Building $($item.Target) -> $($item.Output)"
        $arguments = @('-batchmode', '-quit', '-projectPath', $copy)
        $arguments += @('-activeBuildProfile', $item.Profile, '-build', $item.Output, '-logFile', $log)
        if ($item.Target -in @('Android', 'AndroidHotUpdate')) {
            $method = if ($item.Target -eq 'Android') { 'AndroidContentBuild.BuildApk' } else { 'AndroidContentBuild.BuildContent' }
            $androidOutput = if ($item.Target -eq 'Android') { Split-Path $item.Output -Parent } else { $item.Output }
            $arguments = @('-batchmode', '-quit', '-projectPath', $copy, '-buildTarget', 'Android',
                '-activeBuildProfile', $item.Profile, '-executeMethod', $method, '-omWork', $work, '-omOutput', $androidOutput, '-logFile', $log)
            if ($Validation) { $arguments += '-omValidation' }
        }
        $exitCode = -1
        $errorText = $null
        $process = $null
        try {
            New-Item -ItemType Directory -Path (Split-Path $item.Output -Parent) -Force | Out-Null
            $savedJavaOptions = [Environment]::GetEnvironmentVariable('JAVA_TOOL_OPTIONS', 'Process')
            $savedTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
            $savedTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
            try {
                if ($item.Target -in @('Android', 'AndroidHotUpdate')) {
                    $javaTemp = Join-Path $work 'java-tmp'
                    New-Item -ItemType Directory -Path $javaTemp -Force | Out-Null
                    # Avoid redirected Windows/MSIX TEMP breaking Java's AF_UNIX pipe.
                    $env:JAVA_TOOL_OPTIONS = ($savedJavaOptions + ' "-Djdk.net.unixdomain.tmpdir=' + $javaTemp.Replace('\', '/') + '"').Trim()
                    # Unity filters JAVA_TOOL_OPTIONS for Gradle; TEMP/TMP also cover
                    # Java's default socket directory without requiring JVM flags.
                    $env:TEMP = $javaTemp
                    $env:TMP = $javaTemp
                }
                $process = [UnityBatchBuild.OwnedProcess]::Start($item.Editor, [string[]]$arguments, (Get-Location).ProviderPath)
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
            $exitCode = $process.ExitCode
            $artifact = if ($item.Target -eq 'WebGL') { Join-Path $item.Output 'index.html' }
                elseif ($item.Target -eq 'AndroidHotUpdate') { Join-Path $item.Output 'android-content-build-result.json' }
                else { $item.Output }
            if ($exitCode -ne 0) {
                if (Test-Path -LiteralPath $log) {
                    Write-Host "Unity failure log: $log"
                    Get-Content -LiteralPath $log -Tail 20 | Out-Host
                }
                throw "Unity exited with code $exitCode. See $log"
            }
            if (Test-Path -LiteralPath $log) {
                # AndroidContentBuild checks API results for its auxiliary bundle
                # exports and the final APK, then emits its own success marker.
                # Unity can log Failure for a cached scene-bundle report with no
                # errors; that auxiliary status must not override the final result.
                if ($item.Target -notin @('Android','AndroidHotUpdate') -and
                    (Select-String -LiteralPath $log -Pattern 'Build Finished, Result: Failure|Build completed with a result of .Failed' -Quiet)) {
                    $details = Select-String -LiteralPath $log -Pattern 'What went wrong:' -Context 0,3 | Select-Object -First 1
                    if ($details) { Write-Host $details.ToString() }
                    throw "Unity reported build failure despite exit code 0. See $log"
                }
            }
            if (!(Test-Path -LiteralPath $artifact)) { throw "Missing output: $artifact" }
            # Incremental builds may reuse the launcher/template without changing its
            # timestamp. The fresh per-run Unity build result is authoritative.
            $successPattern = if ($item.Target -in @('Android', 'AndroidHotUpdate')) { 'ANDROID_CONTENT_BUILD_SUCCEEDED' } else { 'Build Finished, Result: Success|Build completed with a result of .Succeeded' }
            if (!(Test-Path -LiteralPath $log) -or !(Select-String -LiteralPath $log -Pattern $successPattern -Quiet)) {
                throw "No explicit successful build result in Unity log: $log"
            }
        } catch [System.Management.Automation.PipelineStoppedException] {
            # Cancellation must not be treated as a failed target followed by
            # starting the next target. The finally block closes the process job.
            throw
        } catch { $errorText = $_.Exception.Message }
        finally {
            if ($process) {
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
