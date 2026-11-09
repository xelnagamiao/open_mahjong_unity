param([Parameter(Mandatory=$true)][string]$Project, [Parameter(Mandatory=$true)][string]$Work, [switch]$Validation, [int]$ValidationRelease = 0)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $Work 'HybridCLR-9.0.3'
$commit = '6cadd24d101f4cbd00cd80190106f6621132d24d'
if (!(Test-Path -LiteralPath (Join-Path $sdk 'package.json'))) {
    $archive = Join-Path $Work 'hybridclr-9.0.3.zip'
    Invoke-WebRequest -UseBasicParsing -Uri "https://api.github.com/repos/focus-creative-games/hybridclr_unity/zipball/$commit" -OutFile $archive
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            $relative = ($entry.FullName -split '/',2)[1]
            if (!$relative -or $relative.EndsWith('/') -or [IO.Path]::GetFileName($relative) -match '(?i)^README') { continue }
            $destination = [IO.Path]::GetFullPath((Join-Path $sdk $relative))
            if (!$destination.StartsWith([IO.Path]::GetFullPath($sdk) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid SDK archive path.' }
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
        }
    } finally { $zip.Dispose() }
}
$unity = (Select-String -LiteralPath "$Project/ProjectSettings/ProjectVersion.txt" -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
$family = ($unity -split '\.')[0..1] -join '.'
$versions = (Get-Content -LiteralPath "$sdk/Data~/hybridclr_version.json" -Raw | ConvertFrom-Json).versions
if (!($versions | Where-Object { $_.unity_version -like "$family.*" })) {
    throw "HybridCLR 9.0.3 does not provide a native runtime for Unity $unity. Supported families: $($versions.unity_version -join ', '). No APK or hot-update package was generated. Do not substitute another family's libil2cpp."
}
$runtime = Join-Path $Work 'NativeRuntimeSource'
if (!(Test-Path -LiteralPath "$runtime/libil2cpp/hybridclr/RuntimeApi.cpp")) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $selected = @($versions | Where-Object { $_.unity_version -like "$family.*" })[0]
    foreach ($repository in @('il2cpp_plus','hybridclr')) {
        $tag = if ($repository -eq 'hybridclr') { $selected.hybridclr.tag } else { $selected.il2cpp_plus.tag }
        $archive = Join-Path $Work "$repository-$tag.zip"
        if (!(Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest -UseBasicParsing -Uri "https://api.github.com/repos/focus-creative-games/$repository/zipball/$tag" -OutFile $archive
        }
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($entry in $zip.Entries) {
                $relative = ($entry.FullName -split '/',2)[1]
                if (!$relative -or $relative.EndsWith('/') -or [IO.Path]::GetFileName($relative) -match '(?i)^README') { continue }
                if ($repository -eq 'hybridclr') {
                    if (!$relative.StartsWith('hybridclr/')) { continue }
                    $relative = 'libil2cpp/' + $relative
                } elseif (!$relative.StartsWith('libil2cpp/')) { continue }
                $target = [IO.Path]::GetFullPath((Join-Path $runtime $relative))
                if (!$target.StartsWith([IO.Path]::GetFullPath($runtime) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid native runtime archive path.' }
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        } finally { $zip.Dispose() }
    }
}
# Array classes use their element assembly as their image. Injected IList<T>
# methods such as RemoveAt still refer to System.Array's AOT method metadata.
# Selecting parameter metadata by klass->image crashes reflection on hot DTO
# arrays (for example Newtonsoft's array contract initialization). Use the
# runtime's existing method-definition overload to select the actual owner.
$parameterFile = Join-Path $runtime 'libil2cpp/vm/GlobalMetadata.cpp'
$parameterText = [IO.File]::ReadAllText($parameterFile)
$parameterOld = 'GetParameterDefinitionFromIndex(klass->image, methodDefinition.parameterStart + paramIndex)'
$parameterNew = 'GetParameterDefinitionFromIndex(methodDefinition, methodDefinition.parameterStart + paramIndex)'
if ($parameterText.Contains($parameterOld)) {
    if ((Get-FileHash -LiteralPath $parameterFile -Algorithm SHA256).Hash -ne '99FE6CF20CFC0F2E3104F7E89A6BD4D8D29B9E121AF2DA628E5BE66A105E9388') {
        throw 'Unexpected HybridCLR parameter metadata source; review compatibility patch before building.'
    }
    [IO.File]::WriteAllText($parameterFile, $parameterText.Replace($parameterOld,$parameterNew), (New-Object Text.UTF8Encoding($false)))
} elseif (!$parameterText.Contains($parameterNew)) { throw 'HybridCLR parameter metadata compatibility check failed.' }
if ((Get-FileHash -LiteralPath $parameterFile -Algorithm SHA256).Hash -ne '26F1C8D7CE4AF08664A59CFE509E4EE37826E4E4BC1EAF20AAB59E41DC74BAE1') {
    throw 'Unexpected patched HybridCLR runtime fingerprint.'
}
$cachedParameterFile = Join-Path $Project 'Library/HybridCLR/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/vm/GlobalMetadata.cpp'
if (Test-Path -LiteralPath $cachedParameterFile) { Copy-Item -LiteralPath $parameterFile -Destination $cachedParameterFile -Force }
# NativeFingerprint includes this generated Android-only marker. A framework
# built without this runtime correction must never accept its content packages.
[IO.File]::WriteAllText((Join-Path $Project 'Assets/AndroidUpdate/runtime-compatibility.txt'),
    'hybridclr-9.0.3; parameter-owner-v1; ' + (Get-FileHash -LiteralPath $parameterFile -Algorithm SHA256).Hash.ToLowerInvariant(), (New-Object Text.UTF8Encoding($false)))
# Source synchronization removes the transient bootstrap scene saved by the
# previous APK build. Recreate it for content-only runs too, so bundle building
# never starts with a deleted last-opened scene in this cached Editor project.
$buildTool = Join-Path $Project 'Assets/Editor/AndroidContentBuild.cs'
$buildText = [IO.File]::ReadAllText($buildTool)
$buildAdapted = $buildText.Replace('if (apk) PrepareBootstrapScene();', 'PrepareBootstrapScene();')
if ($buildAdapted -ne $buildText) { [IO.File]::WriteAllText($buildTool, $buildAdapted, (New-Object Text.UTF8Encoding($false))) }
# Embed the pinned package only into the independent Android copy. Never rewrite
# the source manifest or globally replace the Editor's IL2CPP installation.
$destination = Join-Path $Project 'Packages/com.code-philosophy.hybridclr'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& robocopy.exe $sdk $destination /E /XJ /R:2 /W:1 /NP /NFL /NDL /XF 'README*' | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Cannot copy HybridCLR into Android build project.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
# Prepare the closed build copy before the Editor starts. Unity 6000.3's
# serialized values are IL2CPP=1 and NET_Unity_4_8=3 (UnityCsReference).
# Setting them up front avoids a Standard -> Framework Editor recompile on
# every build. Configure() rechecks the same settings through Unity's API.
function Set-AndroidMapValue([string]$Text, [string]$Name, [int]$Value) {
    $pattern = '(?m)^  ' + [regex]::Escape($Name) + ':(?: \{\})?\r?\n(?:    [^\r\n]*\r?\n)*'
    $match = [regex]::Match($Text, $pattern)
    if (!$match.Success) { throw "Missing serialized Android settings map: $Name" }
    $block = $match.Value.Replace(': {}', ':')
    if ($block -match '(?m)^    Android:') { $block = [regex]::Replace($block, '(?m)^    Android: [^\r\n]*', "    Android: $Value") }
    else { $block = $block.TrimEnd() + "`n    Android: $Value`n" }
    return $Text.Substring(0,$match.Index) + $block + $Text.Substring($match.Index + $match.Length)
}
$playerSettingsPath = "$Project/ProjectSettings/ProjectSettings.asset"
$playerSettings = [IO.File]::ReadAllText($playerSettingsPath)
$playerSettings = Set-AndroidMapValue $playerSettings 'scriptingBackend' 1
$playerSettings = Set-AndroidMapValue $playerSettings 'apiCompatibilityLevelPerPlatform' 3
$playerSettings = [regex]::Replace($playerSettings, '(?m)^  stripEngineCode: [01]', '  stripEngineCode: 0')
[IO.File]::WriteAllText($playerSettingsPath, $playerSettings, $utf8)
if ($Validation) {
    $config = Join-Path $Project 'Assets/Scripts/Config/ConfigManager.cs'
    $text = [IO.File]::ReadAllText($config).Replace('public static bool Debug = false;', 'public static bool Debug = true;')
    if ($ValidationRelease -gt 0) { $text = [regex]::Replace($text, 'releaseVersion = \d+;', "releaseVersion = $ValidationRelease;") }
    [IO.File]::WriteAllText($config, $text, $utf8)
    $release = [regex]::Match($text, 'releaseVersion = (\d+);').Groups[1].Value
    [IO.File]::WriteAllText("$Project/Assets/Resources/AndroidUpdateProbe.txt", "content-probe-$release", $utf8)
    $probe = @'
using UnityEngine;
public static class AndroidUpdateProbe {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Report() {
        Debug.Log("[AndroidUpdateProbe] code=" + ConfigManager.releaseVersion + " resource=" + Resources.Load<TextAsset>("AndroidUpdateProbe").text);
    }
}
'@
    [IO.File]::WriteAllText("$Project/Assets/AndroidUpdateProbe.cs", $probe, $utf8)
}
foreach ($file in (Get-ChildItem -LiteralPath "$Project/Assets" -Filter '*.cs' -Recurse -File)) {
    $text = [IO.File]::ReadAllText($file.FullName)
    # 6000.4 added overloads without a sorting argument. Preserve their unsorted
    # semantics when preparing 6000.3 sources, including editor/test helpers.
    $adapted = [regex]::Replace($text, 'FindObjectsByType(<[^;\r\n()]+>)\(\s*\)', 'FindObjectsByType$1(UnityEngine.FindObjectsSortMode.None)')
    $adapted = [regex]::Replace($adapted, 'FindObjectsByType(<[^;\r\n()]+>)\(\s*((?:UnityEngine\.)?FindObjectsInactive\.(?:Include|Exclude))\s*\)', 'FindObjectsByType$1($2, UnityEngine.FindObjectsSortMode.None)')
    if ($file.FullName.Replace('\','/').EndsWith('/Plugins/WebGLSupport/WebGLInput/WebGLInput.cs')) {
        # Native browser callbacks are stubs on Android. Their string signatures
        # must not generate Android reverse-P/Invoke wrappers in HybridCLR.
        $adapted = [regex]::Replace($adapted, '(?m)^\s*\[MonoPInvokeCallback\([^\r\n]+\)\][ \t]*\r?\n', '')
    }
    if ($file.FullName -notmatch '[\\/]Editor[\\/]|[\\/]AndroidUpdate[\\/]') {
        $adapted = [regex]::Replace($adapted, '(?<![\w.])(?:UnityEngine\.)?Resources\.', 'OmUpdate.ContentResources.')
    }
    if ($adapted -ne $text) { [IO.File]::WriteAllText($file.FullName, $adapted, $utf8) }
}
foreach ($file in (Get-ChildItem -LiteralPath "$Project/Assets" -Filter '*.asmdef' -Recurse -File)) {
    if ($file.FullName -match '[\\/]AndroidUpdate[\\/]|[\\/]Editor[\\/]') { continue }
    $assembly = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
    $platforms = $assembly.PSObject.Properties['includePlatforms']
    if ($platforms -and $platforms.Value -contains 'Editor') { continue }
    $referenceProperty = $assembly.PSObject.Properties['references']
    $references = @()
    if ($referenceProperty) { $references = @($referenceProperty.Value) }
    if ($references -notcontains 'OmUpdate.Runtime') {
        $assembly | Add-Member -NotePropertyName references -NotePropertyValue @($references + @('OmUpdate.Runtime')) -Force
        [IO.File]::WriteAllText($file.FullName, ($assembly | ConvertTo-Json -Depth 30), $utf8)
    }
}
# Preserve the HybridCLR reflection entry points and the independent input module.
$link = '<linker><assembly fullname="HybridCLR.Runtime" preserve="all"/><assembly fullname="OmUpdate.Core" preserve="all"/><assembly fullname="OmUpdate.Runtime" preserve="all"/><assembly fullname="Unity.InputSystem" preserve="all"/></linker>'
[IO.File]::WriteAllText("$Project/Assets/AndroidUpdate/link.xml", $link, $utf8)
