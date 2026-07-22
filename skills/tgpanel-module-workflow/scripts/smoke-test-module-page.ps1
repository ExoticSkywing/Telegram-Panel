[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$ModuleId,

    [Parameter(Mandatory = $false)]
    [string]$PageKey = "settings",

    # 模块实际返回 HTML 的地址；默认 /ext/{moduleId}/{pageKey}。
    [Parameter(Mandatory = $false)]
    [string]$ModulePagePath = "",

    # 需要额外验证的管理 API（GET），可传相对地址或与 BaseUrl 同源的绝对地址。
    [Parameter(Mandatory = $false)]
    [string[]]$ApiPath = @(),

    # 需要额外验证的同源静态资源；HTML 中发现的本地 JS/CSS 会自动加入。
    [Parameter(Mandatory = $false)]
    [string[]]$AssetPath = @(),

    [Parameter(Mandatory = $false)]
    [hashtable]$Headers = @{},

    [Parameter(Mandatory = $false)]
    [string]$BearerToken = "",

    [Parameter(Mandatory = $false)]
    [string]$Cookie = "",

    [Parameter(Mandatory = $false)]
    [string]$ExpectedPageText = "",

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 60)]
    [int]$TimeoutSeconds = 15,

    [Parameter(Mandatory = $false)]
    [switch]$SkipShellRoute,

    # 仅供没有任何外部 JS 的旧页面迁移使用；新静态 Vue 页应保留默认门禁。
    [Parameter(Mandatory = $false)]
    [switch]$AllowInlineOnly
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

function Resolve-HttpUri {
    param(
        [Parameter(Mandatory = $true)][System.Uri]$Root,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $absolute = $null
    if ([System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$absolute)) {
        if ($absolute.Scheme -notin @("http", "https")) {
            throw "仅支持 HTTP/HTTPS 地址：$Value"
        }
        return $absolute
    }

    return [System.Uri]::new($Root, $Value)
}

function Assert-SameOriginUri {
    param(
        [Parameter(Mandatory = $true)][System.Uri]$Root,
        [Parameter(Mandatory = $true)][System.Uri]$Uri,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    $sameOrigin = [string]::Equals(
            $Root.Scheme,
            $Uri.Scheme,
            [System.StringComparison]::OrdinalIgnoreCase) `
        -and [string]::Equals(
            $Root.IdnHost,
            $Uri.IdnHost,
            [System.StringComparison]::OrdinalIgnoreCase) `
        -and $Root.Port -eq $Uri.Port
    if (-not $sameOrigin) {
        throw "$Kind 必须与 BaseUrl 同源，避免把登录凭据发送到外部地址：$Uri"
    }
}

function Invoke-SmokeGet {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][System.Uri]$Uri,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $false)][string[]]$ExpectedContentType = @()
    )

    try {
        $response = $Client.GetAsync($Uri).GetAwaiter().GetResult()
    }
    catch {
        throw "$Kind 请求失败：$Uri -> $($_.Exception.Message)"
    }

    try {
        $statusCode = [int]$response.StatusCode
        if ($statusCode -lt 200 -or $statusCode -ge 300) {
            $location = if ($null -ne $response.Headers.Location) { "，Location=$($response.Headers.Location)" } else { "" }
            throw "$Kind 返回 HTTP $statusCode$location：$Uri"
        }

        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $contentType = if ($null -ne $response.Content.Headers.ContentType) {
            [string]$response.Content.Headers.ContentType.MediaType
        }
        else {
            ""
        }
        $contentTypeMatched = $ExpectedContentType.Count -eq 0
        foreach ($expected in $ExpectedContentType) {
            if (-not [string]::IsNullOrWhiteSpace($expected) `
                -and $contentType.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
                $contentTypeMatched = $true
                break
            }
        }
        if (-not $contentTypeMatched) {
            throw "$Kind Content-Type 无效：$contentType，期望 $($ExpectedContentType -join ' 或 ')（$Uri）"
        }
        if ([string]::IsNullOrWhiteSpace($body)) {
            throw "$Kind 返回了空内容：$Uri"
        }

        Write-Host "OK [$statusCode] $Kind -> $Uri" -ForegroundColor Green
        return [pscustomobject]@{
            Uri = $Uri
            Body = $body
            ContentType = $contentType
            StatusCode = $statusCode
        }
    }
    finally {
        $response.Dispose()
    }
}

function Add-UniqueUri {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string, System.Uri]]$Map,
        [Parameter(Mandatory = $true)][System.Uri]$Uri
    )

    $key = $Uri.AbsoluteUri
    if (-not $Map.ContainsKey($key)) { $Map.Add($key, $Uri) }
}

function Test-SafeModuleId {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 100) { return $false }
    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsLetterOrDigit($character)) { continue }
        if ($character -in @('.', '-', '_')) { continue }
        return $false
    }
    return $true
}

$rootText = $BaseUrl.Trim()
if (-not $rootText.EndsWith('/')) { $rootText += '/' }
$rootUri = $null
if (-not [System.Uri]::TryCreate($rootText, [System.UriKind]::Absolute, [ref]$rootUri) `
    -or $rootUri.Scheme -notin @("http", "https")) {
    throw "BaseUrl 必须是 HTTP/HTTPS 绝对地址：$BaseUrl"
}
if (-not (Test-SafeModuleId -Value $ModuleId)) {
    throw "ModuleId 格式无效：$ModuleId"
}
if ([string]::IsNullOrWhiteSpace($PageKey)) { throw "PageKey 不能为空" }
$apiPathsToProbe = @($ApiPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ([string]::IsNullOrWhiteSpace($ExpectedPageText) -and $apiPathsToProbe.Count -eq 0) {
    throw "必须通过 -ExpectedPageText 或至少一个 -ApiPath 证明返回的是目标模块页面"
}

if ([string]::IsNullOrWhiteSpace($ModulePagePath)) {
    $ModulePagePath = "/ext/$ModuleId/$PageKey"
}
$modulePageUri = Resolve-HttpUri -Root $rootUri -Value $ModulePagePath
Assert-SameOriginUri -Root $rootUri -Uri $modulePageUri -Kind "模块 HTML"
$pageBuilder = [System.UriBuilder]::new($modulePageUri)
$existingQuery = $pageBuilder.Query.TrimStart('?')
$pageBuilder.Query = if ([string]::IsNullOrWhiteSpace($existingQuery)) {
    "legacy=1&embed=1"
}
else {
    "$existingQuery&legacy=1&embed=1"
}
$pageBuilder.Fragment = ""
$pageUri = $pageBuilder.Uri
Assert-SameOriginUri -Root $rootUri -Uri $pageUri -Kind "模块 HTML"

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip `
    -bor [System.Net.DecompressionMethods]::Deflate
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
try {
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("TGPanel-Module-SmokeTest/1.0")
    foreach ($name in $Headers.Keys) {
        $value = [string]$Headers[$name]
        if (-not $client.DefaultRequestHeaders.TryAddWithoutValidation([string]$name, $value)) {
            throw "无法添加请求头：$name"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $client.DefaultRequestHeaders.Authorization = `
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)
    }
    if (-not [string]::IsNullOrWhiteSpace($Cookie)) {
        $null = $client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $Cookie)
    }

    $shellPage = $null
    if (-not $SkipShellRoute) {
        $shellPath = "/ui/ext/$([System.Uri]::EscapeDataString($ModuleId))/$([System.Uri]::EscapeDataString($PageKey))"
        $shellUri = Resolve-HttpUri -Root $rootUri -Value $shellPath
        $shellPage = Invoke-SmokeGet `
            -Client $client `
            -Uri $shellUri `
            -Kind "宿主内嵌路由" `
            -ExpectedContentType "text/html"
    }

    $page = Invoke-SmokeGet `
        -Client $client `
        -Uri $pageUri `
        -Kind "模块 HTML" `
        -ExpectedContentType "text/html"
    if ($null -ne $shellPage `
        -and [string]::Equals($shellPage.Body.Trim(), $page.Body.Trim(), [System.StringComparison]::Ordinal)) {
        throw "模块 HTML 与宿主壳页面完全相同，可能返回了登录页或 SPA 回退页"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPageText) `
        -and $page.Body.IndexOf($ExpectedPageText, [System.StringComparison]::Ordinal) -lt 0) {
        throw "模块 HTML 未包含期望文本：$ExpectedPageText"
    }

    $assets = [System.Collections.Generic.Dictionary[string, System.Uri]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $moduleAssetPrefix = "/ext/$([System.Uri]::EscapeDataString($ModuleId))/assets/"
    foreach ($path in $AssetPath) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $assetUri = Resolve-HttpUri -Root $page.Uri -Value $path
        Assert-SameOriginUri -Root $rootUri -Uri $assetUri -Kind "静态资源"
        if (-not $assetUri.AbsolutePath.StartsWith($moduleAssetPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "模块静态资源不在模块自有路径 $moduleAssetPrefix 下：$assetUri"
        }
        Add-UniqueUri -Map $assets -Uri $assetUri
    }

    # 同时覆盖 src/href、ES module import 和 importmap 中的本地资源地址。
    $assetMatches = [regex]::Matches(
        $page.Body,
        '(?i)["''](?<path>(?:https?://|/|\./|\.\./)?[^"''?#\s]+\.(?:js|mjs|css)(?:\?[^"'']*)?)["'']')
    foreach ($match in $assetMatches) {
        $assetUri = Resolve-HttpUri -Root $page.Uri -Value $match.Groups['path'].Value
        Assert-SameOriginUri -Root $rootUri -Uri $assetUri -Kind "模块静态资源"
        if (-not $assetUri.AbsolutePath.StartsWith($moduleAssetPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "模块静态资源不在模块自有路径 $moduleAssetPrefix 下：$assetUri"
        }
        Add-UniqueUri -Map $assets -Uri $assetUri
    }

    $scriptAssets = @($assets.Values | Where-Object { $_.AbsolutePath -match '\.(?:js|mjs)$' })
    if ($scriptAssets.Count -eq 0 -and -not $AllowInlineOnly) {
        throw "模块 HTML 未发现本地 JS/MJS 资源；新模块必须自带 Vue 运行时并可独立探测"
    }

    foreach ($assetUri in $assets.Values) {
        $expectedType = if ($assetUri.AbsolutePath -match '\.css$') {
            @("text/css")
        }
        elseif ($assetUri.AbsolutePath -match '\.(?:js|mjs)$') {
            @("text/javascript", "application/javascript")
        }
        else {
            @()
        }
        $null = Invoke-SmokeGet `
            -Client $client `
            -Uri $assetUri `
            -Kind "静态资源" `
            -ExpectedContentType $expectedType
    }

    foreach ($path in $apiPathsToProbe) {
        $apiUri = Resolve-HttpUri -Root $rootUri -Value $path
        Assert-SameOriginUri -Root $rootUri -Uri $apiUri -Kind "模块 API"
        $null = Invoke-SmokeGet `
            -Client $client `
            -Uri $apiUri `
            -Kind "模块 API" `
            -ExpectedContentType "application/json"
    }

    Write-Host "模块页面冒烟验证通过：$ModuleId/$PageKey" -ForegroundColor Cyan
    Write-Host "已验证静态资源：$($assets.Count) 个" -ForegroundColor Cyan
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
