[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [Alias("TpmPath")]
    [string]$Package,

    [Parameter(Mandatory = $false)]
    [string]$SourceManifest = "",

    [Parameter(Mandatory = $false)]
    [string]$ModuleDir = "",

    # 默认拒绝 BOM，避免 Linux/Python 部署脚本使用普通 UTF-8 解码时失败。
    [Parameter(Mandatory = $false)]
    [switch]$AllowUtf8Bom,

    # Full 包可保留宿主内置第三方依赖，但宿主边界程序集仍始终禁止。
    [Parameter(Mandatory = $false)]
    [switch]$AllowHostBuiltInAssemblies
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-RequiredText {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $text = if ($null -eq $Value) { "" } else { [string]$Value }
    $text = $text.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "manifest 缺少 $Name"
    }
    return $text
}

function Get-OptionalText {
    param([Parameter(Mandatory = $false)]$Value)

    if ($null -eq $Value) { return "" }
    return ([string]$Value).Trim()
}

function Test-SafeModuleId {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value.Length -gt 100) { return $false }
    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsLetterOrDigit($character)) { continue }
        if ($character -in @('.', '-', '_')) { continue }
        return $false
    }
    return $true
}

function Test-CoreVersion {
    param([Parameter(Mandatory = $true)][string]$Value)

    return $Value -match '^\d+\.\d+\.\d+$'
}

function Test-SafeArchivePath {
    param([Parameter(Mandatory = $true)][string]$Value)

    $path = $Value.Replace('\', '/')
    $platformPath = $path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if ($path.StartsWith('/')) { return $false }
    if ($path -match '^[A-Za-z]:') { return $false }
    if ([System.IO.Path]::IsPathRooted($platformPath)) { return $false }
    if ($path -match '(^|/)\.\.(/|$)') { return $false }
    return $true
}

function Read-StreamBytes {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $memory = [System.IO.MemoryStream]::new()
    try {
        $Stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
    }
}

function ConvertFrom-StrictUtf8JsonBytes {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $hasBom = $Bytes.Length -ge 3 `
        -and $Bytes[0] -eq 0xEF `
        -and $Bytes[1] -eq 0xBB `
        -and $Bytes[2] -eq 0xBF
    if ($hasBom -and -not $AllowUtf8Bom) {
        throw "$DisplayName 使用了 UTF-8 BOM；请保存为 UTF-8（无 BOM）"
    }

    $offset = if ($hasBom) { 3 } else { 0 }
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $text = $encoding.GetString($Bytes, $offset, $Bytes.Length - $offset)
    }
    catch {
        throw "$DisplayName 不是有效 UTF-8：$($_.Exception.Message)"
    }

    try {
        return $text | ConvertFrom-Json
    }
    catch {
        throw "$DisplayName 不是有效 JSON：$($_.Exception.Message)"
    }
}

function Read-ManifestFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return ConvertFrom-StrictUtf8JsonBytes -Bytes $bytes -DisplayName $Path
}

function Read-ZipManifest {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchiveEntry]$Entry,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $stream = $Entry.Open()
    try {
        $bytes = Read-StreamBytes -Stream $stream
        return ConvertFrom-StrictUtf8JsonBytes -Bytes $bytes -DisplayName $DisplayName
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ManifestIdentity {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    try {
        $identity = [ordered]@{
            Id = Get-RequiredText -Value $Manifest.id -Name "id"
            Name = Get-RequiredText -Value $Manifest.name -Name "name"
            Version = Get-RequiredText -Value $Manifest.version -Name "version"
            HostMin = Get-RequiredText -Value $Manifest.host.min -Name "host.min"
            HostMax = Get-OptionalText -Value $Manifest.host.max
            EntryAssembly = Get-RequiredText -Value $Manifest.entry.assembly -Name "entry.assembly"
            EntryType = Get-RequiredText -Value $Manifest.entry.type -Name "entry.type"
        }
    }
    catch {
        throw "$DisplayName：$($_.Exception.Message)"
    }

    if (-not (Test-SafeModuleId -Value $identity.Id)) {
        throw "$DisplayName：id 格式无效：$($identity.Id)"
    }
    if (-not (Test-CoreVersion -Value $identity.Version)) {
        throw "$DisplayName：version 必须是宿主支持的 x.y.z：$($identity.Version)"
    }
    if (-not (Test-CoreVersion -Value $identity.HostMin)) {
        throw "$DisplayName：host.min 必须是宿主支持的 x.y.z：$($identity.HostMin)"
    }
    if (-not [string]::IsNullOrWhiteSpace($identity.HostMax) `
        -and -not (Test-CoreVersion -Value $identity.HostMax)) {
        throw "$DisplayName：host.max 必须是宿主支持的 x.y.z：$($identity.HostMax)"
    }
    if (-not [string]::IsNullOrWhiteSpace($identity.HostMax) `
        -and [version]$identity.HostMin -gt [version]$identity.HostMax) {
        throw "$DisplayName：host 范围无效：$($identity.HostMin) > $($identity.HostMax)"
    }
    if ($identity.EntryAssembly -notmatch '^[^/\\]+\.dll$') {
        throw "$DisplayName：entry.assembly 必须是 DLL 文件名，不能包含路径：$($identity.EntryAssembly)"
    }

    return [pscustomobject]$identity
}

function Assert-ManifestIdentityEqual {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$ActualName
    )

    foreach ($property in @("Id", "Name", "Version", "HostMin", "HostMax", "EntryAssembly", "EntryType")) {
        if (-not [string]::Equals(
            [string]$Expected.$property,
            [string]$Actual.$property,
            [System.StringComparison]::Ordinal)) {
            throw "$ActualName 的 $property 与包根 manifest 不一致：'$($Actual.$property)' != '$($Expected.$property)'"
        }
    }
}

function Test-HostBoundaryAssembly {
    param([Parameter(Mandatory = $true)][string]$FileName)

    $patterns = @(
        "Microsoft.AspNetCore.*.dll",
        "Microsoft.Extensions.*.dll",
        "Microsoft.JSInterop*.dll",
        "TelegramPanel.*.dll",
        "MudBlazor*.dll"
    )
    foreach ($pattern in $patterns) {
        if ($FileName -like $pattern) { return $true }
    }
    return $false
}

function Test-HostBuiltInAssembly {
    param([Parameter(Mandatory = $true)][string]$FileName)

    $patterns = @(
        "Microsoft.EntityFrameworkCore*.dll",
        "Microsoft.Data.Sqlite*.dll",
        "SQLitePCLRaw*.dll",
        "WTelegramClient*.dll",
        "SixLabors.ImageSharp*.dll",
        "PhoneNumbers*.dll"
    )
    foreach ($pattern in $patterns) {
        if ($FileName -like $pattern) { return $true }
    }
    return $false
}

if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) {
    throw "TPM 文件不存在：$Package"
}
$packagePath = (Resolve-Path -LiteralPath $Package).Path
if ([System.IO.Path]::GetExtension($packagePath) -ine ".tpm") {
    throw "模块包扩展名必须是 .tpm：$packagePath"
}

$modulePath = ""
if (-not [string]::IsNullOrWhiteSpace($ModuleDir)) {
    if (-not (Test-Path -LiteralPath $ModuleDir -PathType Container)) {
        throw "模块目录不存在：$ModuleDir"
    }
    $modulePath = (Resolve-Path -LiteralPath $ModuleDir).Path
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entryMap = @{}
    foreach ($entry in $archive.Entries) {
        $rawPath = [string]$entry.FullName
        $path = $rawPath.Replace('\', '/')
        if (-not (Test-SafeArchivePath -Value $rawPath)) {
            throw "TPM 包含不安全路径：$rawPath"
        }
        if ($entryMap.ContainsKey($path)) {
            throw "TPM 包含重复路径：$path"
        }
        $entryMap[$path] = $entry
    }

    if (-not $entryMap.ContainsKey("manifest.json")) {
        throw "TPM 根目录缺少 manifest.json"
    }

    $rootManifest = Read-ZipManifest `
        -Entry $entryMap["manifest.json"] `
        -DisplayName "$packagePath!manifest.json"
    $identity = Get-ManifestIdentity -Manifest $rootManifest -DisplayName "包根 manifest.json"

    $expectedFileName = "$($identity.Id)-$($identity.Version).tpm"
    $actualFileName = [System.IO.Path]::GetFileName($packagePath)
    if (-not [string]::Equals(
        $actualFileName,
        $expectedFileName,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "TPM 文件名与 manifest 不一致：$actualFileName，应为 $expectedFileName"
    }

    $entryAssemblyPath = "lib/$($identity.EntryAssembly)"
    if (-not $entryMap.ContainsKey($entryAssemblyPath)) {
        throw "TPM 缺少入口程序集：$entryAssemblyPath"
    }
    if ($entryMap[$entryAssemblyPath].Length -lt 2) {
        throw "TPM 入口程序集为空或损坏：$entryAssemblyPath"
    }
    $entryStream = $entryMap[$entryAssemblyPath].Open()
    try {
        if ($entryStream.ReadByte() -ne 0x4D -or $entryStream.ReadByte() -ne 0x5A) {
            throw "TPM 入口程序集不是有效 PE 文件：$entryAssemblyPath"
        }
    }
    finally {
        $entryStream.Dispose()
    }

    if ($entryMap.ContainsKey("lib/manifest.json")) {
        $libManifest = Read-ZipManifest `
            -Entry $entryMap["lib/manifest.json"] `
            -DisplayName "$packagePath!lib/manifest.json"
        $libIdentity = Get-ManifestIdentity -Manifest $libManifest -DisplayName "lib/manifest.json"
        Assert-ManifestIdentityEqual `
            -Expected $identity `
            -Actual $libIdentity `
            -ActualName "lib/manifest.json"
    }

    if ([string]::IsNullOrWhiteSpace($SourceManifest) -and -not [string]::IsNullOrWhiteSpace($modulePath)) {
        $SourceManifest = Join-Path $modulePath "manifest.json"
    }

    if (-not [string]::IsNullOrWhiteSpace($SourceManifest)) {
        if (-not (Test-Path -LiteralPath $SourceManifest -PathType Leaf)) {
            throw "源码 manifest 不存在：$SourceManifest"
        }
        $sourceManifestPath = (Resolve-Path -LiteralPath $SourceManifest).Path
        $source = Read-ManifestFile -Path $sourceManifestPath
        $sourceIdentity = Get-ManifestIdentity -Manifest $source -DisplayName $sourceManifestPath
        Assert-ManifestIdentityEqual `
            -Expected $identity `
            -Actual $sourceIdentity `
            -ActualName $sourceManifestPath

        if ([string]::IsNullOrWhiteSpace($modulePath)) {
            $modulePath = Split-Path -Parent $sourceManifestPath
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($modulePath)) {
        $sourceWwwroot = Join-Path $modulePath "wwwroot"
        if (Test-Path -LiteralPath $sourceWwwroot -PathType Container) {
            $sourceRootPrefix = $sourceWwwroot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
            $sourceResources = @(Get-ChildItem -LiteralPath $sourceWwwroot -File -Recurse)
            foreach ($resource in $sourceResources) {
                $relative = $resource.FullName.Substring($sourceRootPrefix.Length).Replace('\', '/')
                $packageResource = "lib/wwwroot/$relative"
                if (-not $entryMap.ContainsKey($packageResource)) {
                    throw "TPM 缺少模块静态资源：$packageResource"
                }
            }
        }
    }

    $libraryAssemblies = @($entryMap.Keys |
        Where-Object { $_ -match '^lib/.+\.dll$' -and $_ -ne $entryAssemblyPath } |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_
                Name = [System.IO.Path]::GetFileName($_)
            }
        })
    $boundaryAssemblies = @($libraryAssemblies |
        Where-Object { Test-HostBoundaryAssembly -FileName $_.Name } |
        ForEach-Object { $_.Path } |
        Sort-Object -Unique)
    if ($boundaryAssemblies.Count -gt 0) {
        throw "TPM 重复携带宿主边界程序集：$($boundaryAssemblies -join ', ')"
    }

    if (-not $AllowHostBuiltInAssemblies) {
        $hostBuiltInAssemblies = @($libraryAssemblies |
            Where-Object { Test-HostBuiltInAssembly -FileName $_.Name } |
            ForEach-Object { $_.Path } |
            Sort-Object -Unique)
        if ($hostBuiltInAssemblies.Count -gt 0) {
            throw "轻量 TPM 重复携带宿主内置程序集：$($hostBuiltInAssemblies -join ', ')"
        }
    }

    Write-Host "OK: $actualFileName" -ForegroundColor Green
    Write-Host "模块: $($identity.Id) $($identity.Version)" -ForegroundColor Cyan
    Write-Host "入口: $entryAssemblyPath" -ForegroundColor Cyan
}
finally {
    $archive.Dispose()
}
