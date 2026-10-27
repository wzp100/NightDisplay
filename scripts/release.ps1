param(
    [string]$Version = '1.3.1',
    [switch]$SkipBuild,
    [switch]$SkipUpload
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$taskRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Output "==> $Name"
    & $Body
    # .ps1 调用不设置 $LASTEXITCODE，只有原生命令才设置；两种情况都要检查。
    $ok = $?
    if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) { $ok = $false }
    if (-not $ok) { throw "$Name 失败。" }
}

if (-not $SkipBuild) {
    Invoke-Step 'build.ps1（主程序 + 集成测试）' { & (Join-Path $taskRoot 'build.ps1') -Test }
}
Invoke-Step 'package.ps1（便携版 zip）' { & (Join-Path $taskRoot 'scripts\package.ps1') -Version $Version }
Invoke-Step 'build-installer.ps1（安装版）' { & (Join-Path $taskRoot 'scripts\build-installer.ps1') -Version $Version }

$zip = Join-Path $taskRoot ("artifacts\NightDisplay-" + $Version + "-win-x64.zip")
$setup = Join-Path $taskRoot ("artifacts\NightDisplay-" + $Version + "-setup-x64.exe")
foreach ($file in @($zip, $setup)) {
    if (-not (Test-Path -LiteralPath $file)) { throw '发布产物缺失：' + $file }
}

Write-Output ''
Write-Output '发布产物：'
Write-Output ('  便携版  ' + $zip)
Write-Output ('  安装版  ' + $setup)

if ($SkipUpload) {
    Write-Output ''
    Write-Output '上传到 Release：'
    Write-Output ('  gh release upload v' + $Version + ' "' + $zip + '" "' + $setup + '" --clobber')
    return
}

$tag = 'v' + $Version
$notes = @(
    ('## NightDisplay ' + $Version),
    '',
    '让屏幕休息，让后台工作继续。',
    '',
    '### 下载',
    '',
    '| 方式 | 文件 |',
    '| --- | --- |',
    ('| **安装版（推荐）** | `' + (Split-Path -Leaf $setup) + '` |'),
    ('| 便携版 | `' + (Split-Path -Leaf $zip) + '` |'),
    '',
    '完整说明见仓库 README。'
)
Invoke-Step ('gh release ' + $tag) {
    gh release view $tag --json tagName > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        gh release edit $tag --notes ($notes -join "`n")
    } else {
        git rev-parse --verify ("refs/tags/" + $tag) > $null 2>&1
        if ($LASTEXITCODE -ne 0) {
            git tag -a $tag -m ("NightDisplay " + $Version)
            git push origin $tag
            if ($LASTEXITCODE -ne 0) { throw 'tag 推送失败。' }
        }
        gh release create $tag $zip $setup --title ("NightDisplay " + $Version) --notes ($notes -join "`n") --verify-tag
    }
    if ($LASTEXITCODE -ne 0) { throw 'gh release 失败。' }
}

Write-Output ''
Write-Output ('完成：Release ' + $tag + ' 已包含便携版与安装版。')
