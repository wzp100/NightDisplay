param([string]$Version = '1.3.0')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$taskRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskRelease = Join-Path $taskRoot 'artifacts\release'
if (-not (Test-Path -LiteralPath (Join-Path $taskRelease 'NightDisplay.exe'))) { throw '请先运行 build.ps1。' }
$taskCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$taskIscc = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $taskIscc) { throw '未找到 Inno Setup 6 的 ISCC.exe。安装：winget install JRSoftware.InnoSetup' }
$taskScript = Join-Path $taskRoot 'installer\NightDisplay.iss'
& $taskIscc "/DAppVersion=$Version" "/O$taskRoot\artifacts" $taskScript
if ($LASTEXITCODE -ne 0) { throw '安装包构建失败。' }
$taskOutput = Join-Path $taskRoot ('artifacts\NightDisplay-' + $Version + '-setup-x64.exe')
if (-not (Test-Path -LiteralPath $taskOutput)) { throw '安装包未生成。' }
Write-Output $taskOutput
