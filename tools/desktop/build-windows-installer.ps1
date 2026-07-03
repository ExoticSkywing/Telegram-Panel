param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactsRoot = Join-Path $repoRoot "artifacts\desktop"
$webOut = Join-Path $artifactsRoot "web"
$appOut = Join-Path $artifactsRoot "app"
$payloadOut = Join-Path $artifactsRoot "payload"
$payloadZip = Join-Path $artifactsRoot "payload.zip"
$setupOut = Join-Path $artifactsRoot "setup"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败，退出码：$LASTEXITCODE"
    }
}

Write-Host "清理桌面版构建目录..."
if (Test-Path $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $webOut, $appOut, $payloadOut, $setupOut | Out-Null

Write-Host "发布 Web 服务..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Web\TelegramPanel.Web.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $webOut `
        /p:PublishSingleFile=false
}

Write-Host "发布桌面壳..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Desktop\TelegramPanel.Desktop.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $appOut `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true
}

Write-Host "组装安装 payload..."
Copy-Item -Path (Join-Path $appOut "*") -Destination $payloadOut -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $payloadOut "web") | Out-Null
Copy-Item -Path (Join-Path $webOut "*") -Destination (Join-Path $payloadOut "web") -Recurse -Force

Write-Host "压缩 payload..."
Compress-Archive -Path (Join-Path $payloadOut "*") -DestinationPath $payloadZip -Force
if (!(Test-Path $payloadZip)) {
    throw "payload.zip 生成失败：$payloadZip"
}

Write-Host "发布安装器..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Setup\TelegramPanel.Setup.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $setupOut `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:SetupPayloadZip="$payloadZip"
}

$finalSetup = Join-Path $artifactsRoot "TelegramPanel.Setup.exe"
Copy-Item -LiteralPath (Join-Path $setupOut "TelegramPanel.Setup.exe") -Destination $finalSetup -Force

Write-Host ""
Write-Host "构建完成："
Write-Host "  安装器：$finalSetup"
Write-Host "  便携目录：$payloadOut"
