[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ModuleDir,

    [Parameter(Mandatory = $false)]
    [string]$Project = "",

    [Parameter(Mandatory = $false)]
    [string]$Manifest = "",

    [Parameter(Mandatory = $false)]
    [string]$HostRepo = "",

    # 生产宿主使用的准确 Tag 或 Commit，例如 v1.31.33。
    [Parameter(Mandatory = $false)]
    [string]$ExpectedHostRef = "",

    # 将要部署到的宿主版本；省略时会尝试从 ExpectedHostRef 推断。
    [Parameter(Mandatory = $false)]
    [string]$TargetHostVersion = "",

    # 可传 CI/部署工作流，校验其中包含当前 manifest 对应的 TPM 文件名。
    [Parameter(Mandatory = $false)]
    [string[]]$ReleaseDefinition = @(),

    [Parameter(Mandatory = $false)]
    [switch]$AllowDirtyHost,

    [Parameter(Mandatory = $false)]
    [switch]$AllowUtf8Bom,

    # 仅供旧模块迁移；直接依赖 Core/Data/Web 时，正式模块必须声明非 0.0.0 的 host.min。
    [Parameter(Mandatory = $false)]
    [switch]$AllowUnpinnedDirectHostReferences
)

$ErrorActionPreference = "Stop"

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name 不存在：$Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-SingleProject {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $projects = @(Get-ChildItem -LiteralPath $Directory -Filter "*.csproj" -File)
    if ($projects.Count -eq 0) {
        $projects = @(Get-ChildItem -LiteralPath $Directory -Filter "*.csproj" -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })
    }
    if ($projects.Count -eq 0) { throw "模块目录中未找到 csproj：$Directory" }
    if ($projects.Count -gt 1) {
        throw "模块目录中存在多个 csproj，请显式指定 -Project：$($projects.Name -join ', ')"
    }
    return $projects[0].FullName
}

function Read-StrictUtf8JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 `
        -and $bytes[0] -eq 0xEF `
        -and $bytes[1] -eq 0xBB `
        -and $bytes[2] -eq 0xBF
    if ($hasBom -and -not $AllowUtf8Bom) {
        throw "$Path 使用了 UTF-8 BOM；请保存为 UTF-8（无 BOM）"
    }

    $offset = if ($hasBom) { 3 } else { 0 }
    try {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        $text = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
        return $text | ConvertFrom-Json
    }
    catch {
        throw "无法读取 manifest：$($_.Exception.Message)"
    }
}

function Get-RequiredText {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $text = if ($null -eq $Value) { "" } else { [string]$Value }
    $text = $text.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { throw "manifest 缺少 $Name" }
    return $text
}

function ConvertTo-CoreVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $false)][switch]$AllowVPrefix
    )

    $trimmed = $Value.Trim()
    if ($AllowVPrefix -and $trimmed.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring(1)
    }
    $match = [regex]::Match($trimmed, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$')
    if (-not $match.Success) { throw "$Name 必须是宿主支持的 x.y.z 版本：$Value" }
    return [version]::new(
        [int]$match.Groups['major'].Value,
        [int]$match.Groups['minor'].Value,
        [int]$match.Groups['patch'].Value)
}

function Get-VersionText {
    param([Parameter(Mandatory = $true)][version]$Version)

    return "$($Version.Major).$($Version.Minor).$($Version.Build)"
}

function Get-HostProjectVersion {
    param([Parameter(Mandatory = $true)][string]$Repository)

    $propsPath = Join-Path $Repository "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) { return $null }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8
    $versionNode = $props.SelectSingleNode("//*[local-name()='Version']")
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) { return $null }
    return $versionNode.InnerText.Trim()
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Base,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $basePath = (Resolve-Path -LiteralPath $Base).Path
    $targetPath = (Resolve-Path -LiteralPath $Target).Path
    if ([System.IO.Path].GetMethods().Name -contains "GetRelativePath") {
        return [System.IO.Path]::GetRelativePath($basePath, $targetPath).Replace('\', '/')
    }

    $baseUri = [System.Uri]::new($basePath.TrimEnd('\') + '\')
    $targetUri = [System.Uri]::new($targetPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('\', '/')
}

function Get-PathStringComparison {
    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        return [System.StringComparison]::OrdinalIgnoreCase
    }
    return [System.StringComparison]::Ordinal
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Action
    )

    $output = @(& git -C $Repository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Action 失败：$($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

function Get-GitChangedPaths {
    param([Parameter(Mandatory = $true)][string]$Repository)

    $commands = @(
        @("diff", "--name-only", "--relative", "--"),
        @("diff", "--cached", "--name-only", "--relative", "--"),
        @("ls-files", "--others", "--exclude-standard")
    )
    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($arguments in $commands) {
        $output = @(& git -C $Repository @arguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "检查宿主工作区失败：$($output -join [Environment]::NewLine)"
        }
        foreach ($line in $output) {
            $path = ([string]$line).Trim().Replace('\', '/')
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $null = $paths.Add($path)
            }
        }
    }
    return @($paths | Sort-Object)
}

function Test-GitPathWithinDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][System.StringComparison]$Comparison
    )

    $normalizedPath = $Path.Trim().Replace('\', '/').TrimStart('/')
    $normalizedDirectory = $Directory.Trim().Replace('\', '/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($normalizedDirectory)) { return $false }
    return [string]::Equals($normalizedPath, $normalizedDirectory, $Comparison) `
        -or $normalizedPath.StartsWith("$normalizedDirectory/", $Comparison)
}

function Resolve-CSharpStringConstant {
    param(
        [Parameter(Mandatory = $true)][string]$Identifier,
        [Parameter(Mandatory = $false)][string[]]$Sources = @()
    )

    $normalized = $Identifier.Replace('global::', '').Replace('::', '.')
    $parts = @($normalized.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries))
    if ($parts.Count -eq 0) { return $null }

    $constantName = $parts[-1]
    $declaringType = if ($parts.Count -gt 1) { $parts[-2] } else { "" }
    $constantPattern = '(?m)\b(?:const|static\s+readonly)\s+string\s+{0}\s*=\s*"(?<literal>[^"]*)"\s*;' -f `
        [regex]::Escape($constantName)
    $typePattern = if ([string]::IsNullOrWhiteSpace($declaringType)) {
        $null
    }
    else {
        '(?m)\b(?:class|struct|interface|record)\s+{0}\b' -f [regex]::Escape($declaringType)
    }

    $values = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        if ([string]::IsNullOrWhiteSpace($source)) { continue }
        if ($null -ne $typePattern -and -not [regex]::IsMatch($source, $typePattern)) { continue }

        foreach ($constant in [regex]::Matches($source, $constantPattern)) {
            $null = $values.Add($constant.Groups['literal'].Value)
        }
    }

    $distinctValues = @($values | Sort-Object)
    if ($distinctValues.Count -gt 1) {
        throw "代码 Manifest 常量 $Identifier 存在多个不同字面量，无法静态校验：$($distinctValues -join ', ')"
    }
    if ($distinctValues.Count -eq 1) { return $distinctValues[0] }
    return $null
}

function Get-CSharpAssignedText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Property,
        [Parameter(Mandatory = $false)][string[]]$ConstantSources = @()
    )

    $propertyPattern = [regex]::Escape($Property)
    $assignmentPattern = '(?m)\b{0}\s*=\s*(?:"(?<literal>[^"]*)"|(?<identifier>(?:global::)?[A-Za-z_]\w*(?:(?:::|\.)[A-Za-z_]\w*)*))\s*(?=[,}}\r\n])' -f `
        $propertyPattern
    $match = [regex]::Match(
        $Text,
        $assignmentPattern)
    if (-not $match.Success) { return $null }
    if ($match.Groups['literal'].Success) { return $match.Groups['literal'].Value }

    $identifier = $match.Groups['identifier'].Value
    # 未限定标识符只在入口文件内解析，避免误命中其他类型的同名常量；
    # 跨文件常量必须显式写成 Type.Member 或 Namespace.Type.Member。
    $sources = if ($identifier -match '(?:::|\.)') {
        @($Text) + @($ConstantSources)
    }
    else {
        @($Text)
    }
    return Resolve-CSharpStringConstant `
        -Identifier $identifier `
        -Sources $sources
}

function Assert-CSharpManifestValue {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Property,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $false)][string[]]$ConstantSources = @()
    )

    $actual = Get-CSharpAssignedText `
        -Text $Source `
        -Property $Property `
        -ConstantSources $ConstantSources
    if ($null -eq $actual) {
        throw "$SourcePath 中的代码 Manifest 缺少可静态校验的 $Property"
    }
    if (-not [string]::Equals($actual, $Expected, [System.StringComparison]::Ordinal)) {
        throw "$SourcePath 中代码 Manifest 的 $Property 不一致：'$actual' != '$Expected'"
    }
}

function Get-CSharpDeclaredFullTypeName {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$ClassName
    )

    $classPattern = '(?m)\bclass\s+{0}\b' -f [regex]::Escape($ClassName)
    $classMatch = [regex]::Match($Text, $classPattern)
    if (-not $classMatch.Success) { return $null }

    $namespacePattern = '(?m)^\s*namespace\s+(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*[;{]'
    $namespace = ""
    foreach ($match in [regex]::Matches($Text, $namespacePattern)) {
        if ($match.Index -gt $classMatch.Index) { break }
        $namespace = $match.Groups['name'].Value
    }

    if ([string]::IsNullOrWhiteSpace($namespace)) { return $ClassName }
    return "$namespace.$ClassName"
}

function Get-CSharpTypeofFullName {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$DeclaredFullTypeName
    )

    $match = [regex]::Match(
        $Text,
        '(?m)\bType\s*=\s*typeof\s*\(\s*(?<type>(?:global::)?[A-Za-z_]\w*(?:(?:::|\.)[A-Za-z_]\w*)*)\s*\)\.FullName')
    if (-not $match.Success) { return $null }

    $typeName = $match.Groups['type'].Value.Replace('global::', '').Replace('::', '.')
    if ($typeName.Contains('.')) { return $typeName }

    $declaredClass = ($DeclaredFullTypeName -split '\.')[-1]
    if ([string]::Equals($typeName, $declaredClass, [System.StringComparison]::Ordinal)) {
        return $DeclaredFullTypeName
    }
    return $typeName
}

$modulePath = (Resolve-Path -LiteralPath $ModuleDir).Path
if (-not (Test-Path -LiteralPath $modulePath -PathType Container)) {
    throw "模块目录不存在：$ModuleDir"
}

$projectPath = if ([string]::IsNullOrWhiteSpace($Project)) {
    Resolve-SingleProject -Directory $modulePath
}
else {
    $candidate = if ([System.IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $modulePath $Project }
    Resolve-ExistingFile -Path $candidate -Name "Project"
}
$manifestPath = if ([string]::IsNullOrWhiteSpace($Manifest)) {
    Resolve-ExistingFile -Path (Join-Path $modulePath "manifest.json") -Name "Manifest"
}
else {
    $candidate = if ([System.IO.Path]::IsPathRooted($Manifest)) { $Manifest } else { Join-Path $modulePath $Manifest }
    Resolve-ExistingFile -Path $candidate -Name "Manifest"
}

$manifestObject = Read-StrictUtf8JsonFile -Path $manifestPath
$moduleId = Get-RequiredText -Value $manifestObject.id -Name "id"
$moduleName = Get-RequiredText -Value $manifestObject.name -Name "name"
$moduleVersion = Get-RequiredText -Value $manifestObject.version -Name "version"
$hostMin = Get-RequiredText -Value $manifestObject.host.min -Name "host.min"
$hostMax = if ($null -eq $manifestObject.host.max) { "" } else { ([string]$manifestObject.host.max).Trim() }
$entryAssembly = Get-RequiredText -Value $manifestObject.entry.assembly -Name "entry.assembly"
$entryType = Get-RequiredText -Value $manifestObject.entry.type -Name "entry.type"

$null = ConvertTo-CoreVersion -Value $moduleVersion -Name "manifest.version"
$minVersion = ConvertTo-CoreVersion -Value $hostMin -Name "manifest.host.min"
$maxVersion = if ([string]::IsNullOrWhiteSpace($hostMax)) {
    $null
}
else {
    ConvertTo-CoreVersion -Value $hostMax -Name "manifest.host.max"
}
if ($null -ne $maxVersion -and $minVersion -gt $maxVersion) {
    throw "manifest.host 范围无效：$hostMin > $hostMax"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$assemblyNameNode = $projectXml.SelectSingleNode("//*[local-name()='AssemblyName']")
$projectAssemblyName = if ($null -ne $assemblyNameNode -and -not [string]::IsNullOrWhiteSpace($assemblyNameNode.InnerText)) {
    $assemblyNameNode.InnerText.Trim()
}
else {
    [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
}
$expectedAssembly = "$projectAssemblyName.dll"
if (-not [string]::Equals($entryAssembly, $expectedAssembly, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "manifest.entry.assembly 与项目输出不一致：$entryAssembly != $expectedAssembly"
}

$entryClass = ($entryType -split '\.')[-1]
$sourceFiles = @(Get-ChildItem -LiteralPath $modulePath -Filter "*.cs" -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|publish|package)[\\/]' })
$sourceDocuments = @($sourceFiles | ForEach-Object {
    [pscustomobject]@{
        File = $_
        Text = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    }
})
$constantSourceTexts = @($sourceDocuments | ForEach-Object { $_.Text })
$entrySource = $null
$entryText = $null
$declaredEntryType = $null
foreach ($sourceDocument in $sourceDocuments) {
    $candidateType = Get-CSharpDeclaredFullTypeName -Text $sourceDocument.Text -ClassName $entryClass
    if ([string]::Equals($candidateType, $entryType, [System.StringComparison]::Ordinal)) {
        $entrySource = $sourceDocument.File
        $entryText = $sourceDocument.Text
        $declaredEntryType = $candidateType
        break
    }
}
if ($null -eq $entrySource) {
    throw "未找到完整类型名与 manifest.entry.type 一致的入口类源码：$entryType"
}

$configureIndex = $entryText.IndexOf("ConfigureServices", [System.StringComparison]::Ordinal)
$manifestRegion = if ($configureIndex -gt 0) { $entryText.Substring(0, $configureIndex) } else { $entryText }
Assert-CSharpManifestValue -Source $manifestRegion -Property "Id" -Expected $moduleId -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts
Assert-CSharpManifestValue -Source $manifestRegion -Property "Name" -Expected $moduleName -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts
Assert-CSharpManifestValue -Source $manifestRegion -Property "Version" -Expected $moduleVersion -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts
Assert-CSharpManifestValue -Source $manifestRegion -Property "Min" -Expected $hostMin -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts
$codeHostMax = Get-CSharpAssignedText -Text $manifestRegion -Property "Max" -ConstantSources $constantSourceTexts
if ([string]::IsNullOrWhiteSpace($hostMax)) {
    if (-not [string]::IsNullOrWhiteSpace($codeHostMax)) {
        throw "$($entrySource.FullName) 中代码 Manifest 的 Max 不一致：'$codeHostMax' != 空"
    }
}
else {
    Assert-CSharpManifestValue -Source $manifestRegion -Property "Max" -Expected $hostMax -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts
}
Assert-CSharpManifestValue -Source $manifestRegion -Property "Assembly" -Expected $entryAssembly -SourcePath $entrySource.FullName -ConstantSources $constantSourceTexts

$typeLiteral = Get-CSharpAssignedText -Text $manifestRegion -Property "Type" -ConstantSources $constantSourceTexts
$typeByTypeof = Get-CSharpTypeofFullName -Text $manifestRegion -DeclaredFullTypeName $declaredEntryType
$codeEntryType = if (-not [string]::IsNullOrWhiteSpace($typeByTypeof)) { $typeByTypeof } else { $typeLiteral }
if (-not [string]::Equals($codeEntryType, $entryType, [System.StringComparison]::Ordinal)) {
    throw "$($entrySource.FullName) 中代码 Manifest 的 Type 与 $entryType 不一致"
}

$projectReferences = @($projectXml.SelectNodes("//*[local-name()='ProjectReference']") |
    ForEach-Object { [string]$_.Include } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$directHostReferences = @($projectReferences |
    Where-Object {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($_)
        $name -in @("TelegramPanel.Core", "TelegramPanel.Data", "TelegramPanel.Web")
    } |
    ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) } |
    Sort-Object -Unique)
if ($directHostReferences.Count -gt 0 `
    -and $minVersion -eq [version]::new(0, 0, 0) `
    -and -not $AllowUnpinnedDirectHostReferences) {
    throw "模块直接引用 $($directHostReferences -join ', ')，但 host.min 仍为 0.0.0；请提升到首次提供所用 ABI 的宿主版本"
}

$repoRootText = Invoke-GitText -Repository $modulePath -Arguments @("rev-parse", "--show-toplevel") -Action "定位模块仓库"
$repoRoot = (Resolve-Path -LiteralPath $repoRootText).Path
if ([string]::IsNullOrWhiteSpace($HostRepo)) {
    $upstreamCandidate = Join-Path $repoRoot "upstream/Telegram-Panel"
    $selfCandidate = Join-Path $repoRoot "src/TelegramPanel.Modules.Abstractions"
    if (Test-Path -LiteralPath $upstreamCandidate -PathType Container) {
        $HostRepo = $upstreamCandidate
    }
    elseif (Test-Path -LiteralPath $selfCandidate -PathType Container) {
        $HostRepo = $repoRoot
    }
    else {
        throw "无法自动定位宿主仓库，请传入 -HostRepo"
    }
}
$hostPath = (Resolve-Path -LiteralPath $HostRepo).Path
$hostCommit = Invoke-GitText -Repository $hostPath -Arguments @("rev-parse", "HEAD") -Action "读取宿主提交"
$pathComparison = Get-PathStringComparison
$dirtyPaths = @(Get-GitChangedPaths -Repository $hostPath)
if ($dirtyPaths.Count -gt 0 -and -not $AllowDirtyHost) {
    $blockingPaths = $dirtyPaths
    if ([string]::Equals($repoRoot, $hostPath, $pathComparison)) {
        $moduleRelativePath = Get-RepoRelativePath -Base $hostPath -Target $modulePath
        if ($moduleRelativePath -ne "." `
            -and -not $moduleRelativePath.StartsWith('../') `
            -and -not [System.IO.Path]::IsPathRooted($moduleRelativePath)) {
            $blockingPaths = @($dirtyPaths | Where-Object {
                -not (Test-GitPathWithinDirectory `
                    -Path $_ `
                    -Directory $moduleRelativePath `
                    -Comparison $pathComparison)
            })
        }
    }
    if ($blockingPaths.Count -gt 0) {
        $preview = ($blockingPaths | Select-Object -First 10) -join ', '
        if ($blockingPaths.Count -gt 10) { $preview += ", ..." }
        throw "宿主仓库存在模块目录外的未提交修改，编译基线无法复现：$preview"
    }
}

if (-not [string]::Equals($repoRoot, $hostPath, $pathComparison)) {
    $relativeHostPath = Get-RepoRelativePath -Base $repoRoot -Target $hostPath
    if (-not $relativeHostPath.StartsWith('../') -and -not [System.IO.Path]::IsPathRooted($relativeHostPath)) {
        $gitLink = Invoke-GitText `
            -Repository $repoRoot `
            -Arguments @("ls-files", "--stage", "--", $relativeHostPath) `
            -Action "读取宿主子模块锁定提交"
        if ($gitLink -match '^160000\s+(?<commit>[0-9a-fA-F]{40})\s+\d+\s+') {
            $lockedCommit = $Matches['commit']
            if (-not [string]::Equals($hostCommit, $lockedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "宿主子模块检出提交未写入父仓库：当前 $hostCommit，Gitlink $lockedCommit"
            }
        }
        elseif ([string]::Equals($relativeHostPath, "upstream/Telegram-Panel", $pathComparison)) {
            throw "upstream/Telegram-Panel 未作为 Git 子模块锁定到父仓库"
        }
    }
}

$hostProjectVersionText = Get-HostProjectVersion -Repository $hostPath
$hostProjectVersion = if ([string]::IsNullOrWhiteSpace($hostProjectVersionText)) {
    $null
}
else {
    ConvertTo-CoreVersion -Value $hostProjectVersionText -Name "宿主 Directory.Build.props Version"
}
$expectedRefVersion = $null

if (-not [string]::IsNullOrWhiteSpace($ExpectedHostRef)) {
    $expectedCommit = Invoke-GitText `
        -Repository $hostPath `
        -Arguments @("rev-parse", "--verify", "$ExpectedHostRef^{commit}") `
        -Action "解析宿主引用 $ExpectedHostRef"
    if (-not [string]::Equals($hostCommit, $expectedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "宿主提交不匹配：当前 $hostCommit，期望 $ExpectedHostRef ($expectedCommit)"
    }
    # 看起来像版本号的引用必须是纯 v?x.y.z；不能把预发布 Tag 当作普通 commitish 绕过版本一致性。
    if ($ExpectedHostRef -match '^v?\d+\.\d+\.\d+') {
        $expectedRefVersion = ConvertTo-CoreVersion `
            -Value $ExpectedHostRef `
            -Name "ExpectedHostRef" `
            -AllowVPrefix
        if ([string]::IsNullOrWhiteSpace($TargetHostVersion)) {
            $TargetHostVersion = Get-VersionText -Version $expectedRefVersion
        }
    }
}

$target = $null
if ([string]::IsNullOrWhiteSpace($TargetHostVersion) -and $null -ne $hostProjectVersion) {
    $TargetHostVersion = Get-VersionText -Version $hostProjectVersion
}
if (-not [string]::IsNullOrWhiteSpace($TargetHostVersion)) {
    $target = ConvertTo-CoreVersion -Value $TargetHostVersion -Name "TargetHostVersion"
    if ($null -ne $expectedRefVersion -and $target -ne $expectedRefVersion) {
        throw "目标宿主版本与宿主引用不一致：TargetHostVersion=$TargetHostVersion，ExpectedHostRef=$ExpectedHostRef"
    }
    if ($null -ne $hostProjectVersion -and $target -ne $hostProjectVersion) {
        throw "目标宿主版本与检出源码不一致：TargetHostVersion=$TargetHostVersion，源码版本=$hostProjectVersionText"
    }
    if ($target -lt $minVersion -or ($null -ne $maxVersion -and $target -gt $maxVersion)) {
        $hostRange = if ($null -eq $maxVersion) { "$hostMin ~ 无上限" } else { "$hostMin ~ $hostMax" }
        throw "目标宿主 $TargetHostVersion 不在模块声明范围 $hostRange 内"
    }
}

$expectedPackageName = "$moduleId-$moduleVersion.tpm"
foreach ($definition in $ReleaseDefinition) {
    $definitionPath = Resolve-ExistingFile -Path $definition -Name "发布定义"
    $definitionText = Get-Content -LiteralPath $definitionPath -Raw -Encoding UTF8
    if ($definitionText.IndexOf($expectedPackageName, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "发布定义未引用当前版本产物 $expectedPackageName：$definitionPath"
    }
}

Write-Host "OK: $moduleId $moduleVersion" -ForegroundColor Green
Write-Host "宿主提交: $hostCommit" -ForegroundColor Cyan
if (-not [string]::IsNullOrWhiteSpace($hostProjectVersionText)) {
    Write-Host "宿主版本: $hostProjectVersionText" -ForegroundColor Cyan
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedHostRef)) {
    Write-Host "宿主引用: $ExpectedHostRef" -ForegroundColor Cyan
}
$hostRange = if ($null -eq $maxVersion) { "$hostMin ~ 无上限" } else { "$hostMin ~ $hostMax" }
Write-Host "兼容范围: $hostRange" -ForegroundColor Cyan
if ($directHostReferences.Count -gt 0) {
    Write-Host "直接宿主依赖: $($directHostReferences -join ', ')" -ForegroundColor Yellow
}
Write-Host "预期产物: $expectedPackageName" -ForegroundColor Cyan
