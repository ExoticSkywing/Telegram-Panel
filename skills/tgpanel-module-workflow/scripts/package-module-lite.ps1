param(
    [Parameter(Mandatory = $false)]
    [string]$ModuleDir,

    [Parameter(Mandatory = $false)]
    [string]$Project,

    [Parameter(Mandatory = $false)]
    [string]$Manifest,

    [Parameter(Mandatory = $false)]
    [string]$OutDir = "artifacts/modules",

    [Parameter(Mandatory = $false)]
    [switch]$Full,

    [Parameter(Mandatory = $false)]
    [switch]$NoDocker,

    [Parameter(Mandatory = $false)]
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Base,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $basePath = (Resolve-Path -LiteralPath $Base).Path
    $targetPath = (Resolve-Path -LiteralPath $Target).Path

    if ([System.IO.Path].GetMethods().Name -contains "GetRelativePath")
    {
        return [System.IO.Path]::GetRelativePath($basePath, $targetPath).Replace('\', '/')
    }

    $baseUri = New-Object System.Uri(($basePath.TrimEnd('\') + '\'))
    $targetUri = New-Object System.Uri($targetPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('\', '/')
}

function Resolve-SingleFile {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $items = Get-ChildItem -LiteralPath $Directory -Filter $Pattern -File
    if ($items.Count -eq 0)
    {
        throw "$DisplayName 不存在：$Directory"
    }
    if ($items.Count -gt 1)
    {
        $names = $items | ForEach-Object { $_.Name }
        throw "$DisplayName 不唯一：$Directory -> $($names -join ', ')"
    }

    return $items[0].FullName
}

function Get-ScriptParameterNames {
    param([Parameter(Mandatory = $true)][string]$Path)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -gt 0)
    {
        $messages = $parseErrors | ForEach-Object { $_.Message }
        throw "打包脚本存在 PowerShell 语法错误：$($messages -join '；')"
    }

    if ($null -eq $ast.ParamBlock)
    {
        return @()
    }

    return @($ast.ParamBlock.Parameters | ForEach-Object {
        $_.Name.VariablePath.UserPath
    })
}

function Resolve-RepoOutputDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    if ([string]::IsNullOrWhiteSpace($Directory))
    {
        throw "OutDir 不能为空"
    }

    $repoPath = [System.IO.Path]::GetFullPath($Repository).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $candidate = if ([System.IO.Path]::IsPathRooted($Directory))
    {
        [System.IO.Path]::GetFullPath($Directory)
    }
    else
    {
        [System.IO.Path]::GetFullPath((Join-Path $repoPath $Directory))
    }

    $prefix = $repoPath + [System.IO.Path]::DirectorySeparatorChar
    $pathComparison = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)
    {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else
    {
        [System.StringComparison]::Ordinal
    }
    if (-not $candidate.StartsWith($prefix, $pathComparison))
    {
        throw "OutDir 必须位于当前仓库内：$Directory"
    }
    return $candidate.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-SafeModuleId {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value.Length -gt 100) { return $false }
    foreach ($character in $Value.ToCharArray())
    {
        if ([char]::IsLetterOrDigit($character)) { continue }
        if ($character -in @('.', '-', '_')) { continue }
        return $false
    }
    return $true
}

$repoRoot = (Resolve-Path -LiteralPath ".").Path
$packScript = Join-Path $repoRoot "tools/package-module.ps1"
$verifyScript = Join-Path $PSScriptRoot "verify-module-package.ps1"
if (-not (Test-Path -LiteralPath $packScript -PathType Leaf))
{
    throw "未找到打包脚本：tools/package-module.ps1。请在仓库根目录执行本脚本。"
}
if (-not (Test-Path -LiteralPath $verifyScript -PathType Leaf))
{
    throw "未找到 TPM 校验脚本：$verifyScript"
}

if ([string]::IsNullOrWhiteSpace($Project) -or [string]::IsNullOrWhiteSpace($Manifest))
{
    if ([string]::IsNullOrWhiteSpace($ModuleDir))
    {
        throw "请至少提供 -ModuleDir，或同时提供 -Project 与 -Manifest。"
    }

    $modulePath = (Resolve-Path -LiteralPath $ModuleDir).Path

    if ([string]::IsNullOrWhiteSpace($Project))
    {
        $projectPath = Resolve-SingleFile -Directory $modulePath -Pattern "*.csproj" -DisplayName "模块 csproj"
        $Project = Get-RepoRelativePath -Base $repoRoot -Target $projectPath
    }

    if ([string]::IsNullOrWhiteSpace($Manifest))
    {
        $manifestPath = Join-Path $modulePath "manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf))
        {
            throw "manifest.json 不存在：$modulePath"
        }
        $Manifest = Get-RepoRelativePath -Base $repoRoot -Target $manifestPath
    }
}

$projectAbs = if ([System.IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }
$manifestAbs = if ([System.IO.Path]::IsPathRooted($Manifest)) { $Manifest } else { Join-Path $repoRoot $Manifest }

if (-not (Test-Path -LiteralPath $projectAbs -PathType Leaf)) { throw "Project 不存在：$Project" }
if (-not (Test-Path -LiteralPath $manifestAbs -PathType Leaf)) { throw "Manifest 不存在：$Manifest" }

$Project = Get-RepoRelativePath -Base $repoRoot -Target (Resolve-Path -LiteralPath $projectAbs).Path
$Manifest = Get-RepoRelativePath -Base $repoRoot -Target (Resolve-Path -LiteralPath $manifestAbs).Path

$manifestBytes = [System.IO.File]::ReadAllBytes($manifestAbs)
$hasUtf8Bom = $manifestBytes.Length -ge 3 `
    -and $manifestBytes[0] -eq 0xEF `
    -and $manifestBytes[1] -eq 0xBB `
    -and $manifestBytes[2] -eq 0xBF
if ($hasUtf8Bom)
{
    throw "manifest.json 使用了 UTF-8 BOM；请在打包前保存为 UTF-8（无 BOM）：$manifestAbs"
}
try
{
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $manifestText = $utf8.GetString($manifestBytes)
    $manifestObj = $manifestText | ConvertFrom-Json
}
catch
{
    throw "manifest.json 不是有效的无 BOM UTF-8 JSON：$($_.Exception.Message)"
}
if ($null -eq $manifestObj.id -or [string]::IsNullOrWhiteSpace([string]$manifestObj.id))
{
    throw "manifest.json 缺少 id"
}
if ($null -eq $manifestObj.version -or [string]::IsNullOrWhiteSpace([string]$manifestObj.version))
{
    throw "manifest.json 缺少 version"
}
$moduleId = ([string]$manifestObj.id).Trim()
$moduleVersion = ([string]$manifestObj.version).Trim()
if (-not (Test-SafeModuleId -Value $moduleId))
{
    throw "manifest.json 的 id 格式无效：$moduleId"
}
if ($moduleVersion -notmatch '^\d+\.\d+\.\d+$')
{
    throw "manifest.json 的 version 必须是宿主支持的 x.y.z：$moduleVersion"
}

$packParameters = @(Get-ScriptParameterNames -Path $packScript)
$supportNoDocker = $packParameters -contains "NoDocker"
$supportKeepTemp = $packParameters -contains "KeepTemp"
$supportBoundaryOnlySlim = $packParameters -contains "Slim"
if ($Full -and -not $supportBoundaryOnlySlim)
{
    throw "当前仓库打包脚本的 -Full 会携带宿主边界程序集，无法生成安全 Full 包；请先为 tools/package-module.ps1 增加 -Slim 支持。"
}
$shellCommand = Get-Command "pwsh" -ErrorAction SilentlyContinue
if ($null -eq $shellCommand)
{
    $shellCommand = Get-Command "powershell" -ErrorAction SilentlyContinue
}
if ($null -eq $shellCommand)
{
    throw "未找到 PowerShell 可执行程序（pwsh/powershell）。"
}

$finalOutDir = Resolve-RepoOutputDirectory -Repository $repoRoot -Directory $OutDir
$null = New-Item -ItemType Directory -Path $finalOutDir -Force
$stageOutDir = Join-Path $finalOutDir (".tgpanel-staging-" + [Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $stageOutDir
$stageOutDirRelative = Get-RepoRelativePath -Base $repoRoot -Target $stageOutDir
$packageName = "$moduleId-$moduleVersion.tpm"
$stageFile = Join-Path $stageOutDir $packageName
$outFile = Join-Path $finalOutDir $packageName
$backupFile = Join-Path $stageOutDir (".previous-" + $packageName)
$failedPublishedFile = Join-Path $stageOutDir (".failed-" + $packageName)
$preserveStage = $false

try
{
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $packScript,
        "-Project", $Project,
        "-Manifest", $Manifest,
        "-OutDir", $stageOutDirRelative
    )

    # 对调用方保留 Full 语义（保留宿主内置第三方依赖），但宿主边界程序集仍必须剔除。
    if ($Full) { $args += "-Slim" }

    if ($NoDocker)
    {
        if ($supportNoDocker) { $args += "-NoDocker" }
        else { Write-Warning "当前仓库打包脚本不支持 -NoDocker，已忽略。" }
    }

    if ($KeepTemp)
    {
        if ($supportKeepTemp) { $args += "-KeepTemp" }
        else { Write-Warning "当前仓库打包脚本不支持 -KeepTemp，已忽略。" }
    }

    Write-Host "Project : $Project" -ForegroundColor Cyan
    Write-Host "Manifest: $Manifest" -ForegroundColor Cyan
    Write-Host "OutDir  : $OutDir" -ForegroundColor Cyan

    & $shellCommand.Source @args
    if ($LASTEXITCODE -ne 0)
    {
        throw "打包失败（退出码：$LASTEXITCODE）"
    }
    if (-not (Test-Path -LiteralPath $stageFile -PathType Leaf))
    {
        throw "打包命令执行完成，但未在预期路径发现产物：$stageFile"
    }

    $verifyArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $verifyScript,
        "-Package", $stageFile,
        "-SourceManifest", $manifestAbs,
        "-ModuleDir", (Split-Path -Parent $manifestAbs)
    )
    if ($Full) { $verifyArgs += "-AllowHostBuiltInAssemblies" }

    & $shellCommand.Source @verifyArgs
    if ($LASTEXITCODE -ne 0)
    {
        throw "TPM 产物校验失败（退出码：$LASTEXITCODE）"
    }

    $stageHash = (Get-FileHash -LiteralPath $stageFile -Algorithm SHA256).Hash
    try
    {
        if (Test-Path -LiteralPath $outFile -PathType Leaf)
        {
            # 临时包与正式包位于同一目录树；校验完成后才原子替换旧版本。
            [System.IO.File]::Replace($stageFile, $outFile, $backupFile, $true)
            $preserveStage = $true
        }
        else
        {
            [System.IO.File]::Move($stageFile, $outFile)
        }
    }
    catch
    {
        # 发布失败时保留已校验包及可能生成的旧包备份，便于无损恢复。
        $preserveStage = $true
        throw "发布已校验 TPM 失败，原有产物未主动删除；暂存目录已保留：$stageOutDir。$($_.Exception.Message)"
    }

    if (-not (Test-Path -LiteralPath $outFile -PathType Leaf))
    {
        throw "原子发布完成后未发现正式产物：$outFile"
    }
    $publishedHash = (Get-FileHash -LiteralPath $outFile -Algorithm SHA256).Hash
    if (-not [string]::Equals($stageHash, $publishedHash, [System.StringComparison]::OrdinalIgnoreCase))
    {
        if (Test-Path -LiteralPath $backupFile -PathType Leaf)
        {
            try
            {
                [System.IO.File]::Replace($backupFile, $outFile, $failedPublishedFile, $true)
            }
            catch
            {
                $preserveStage = $true
                throw "正式产物哈希异常且自动恢复旧包失败；暂存目录已保留：$stageOutDir。$($_.Exception.Message)"
            }
        }
        else
        {
            try
            {
                # 首次发布没有旧包可恢复时，也要把异常文件移回暂存区，保持正式目录干净。
                [System.IO.File]::Move($outFile, $failedPublishedFile)
            }
            catch
            {
                $preserveStage = $true
                throw "首次发布产物哈希异常且无法移回暂存区；暂存目录已保留：$stageOutDir。$($_.Exception.Message)"
            }
        }
        $preserveStage = $true
        throw "正式产物哈希与已校验临时包不一致：$outFile"
    }

    $preserveStage = $false
    Write-Host "产物: $outFile" -ForegroundColor Green
}
finally
{
    if (-not $preserveStage -and (Test-Path -LiteralPath $stageOutDir -PathType Container))
    {
        try
        {
            Remove-Item -LiteralPath $stageOutDir -Recurse -Force -ErrorAction Stop
        }
        catch
        {
            Write-Warning "无法清理包装脚本临时目录：$stageOutDir"
        }
    }
}
