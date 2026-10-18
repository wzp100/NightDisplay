param([switch]$Test)
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$taskOut = Join-Path $PSScriptRoot 'artifacts\release'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src\NightDisplay') -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$taskResources = @(
    "/resource:$PSScriptRoot\web\index.html,NightDisplay.Web.index.html",
    "/resource:$PSScriptRoot\web\app.css,NightDisplay.Web.app.css",
    "/resource:$PSScriptRoot\web\app.js,NightDisplay.Web.app.js",
    "/resource:$PSScriptRoot\assets\icons\nightdisplay.png,NightDisplay.Web.icon.png",
    "/resource:$PSScriptRoot\assets\icons\nightdisplay.ico,NightDisplay.Icons.nightdisplay.ico",
    "/resource:$PSScriptRoot\assets\icons\tray.ico,NightDisplay.Icons.tray.ico",
    "/resource:$PSScriptRoot\src\NightDisplay\UI\Theme.xaml,NightDisplay.UI.Theme.xaml",
    "/resource:$PSScriptRoot\src\NightDisplay\UI\Panel.xaml,NightDisplay.UI.Panel.xaml"
)
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /warn:4 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll "/lib:$taskWpf" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll "/win32icon:$PSScriptRoot\assets\icons\nightdisplay.ico" "/out:$taskOut\NightDisplay.exe" @taskResources @taskSources
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'driver') -Destination $taskOut -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $taskOut -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskOut -Force
& (Join-Path $PSScriptRoot 'scripts\build-native.ps1') -OutputDirectory $taskOut
if ($Test) {
    & $taskCompiler /nologo /target:exe /platform:x64 /codepage:65001 /warnaserror+ "/r:$taskOut\NightDisplay.exe" "/out:$taskOut\NightDisplay.Tests.exe" (Join-Path $PSScriptRoot 'tests\IntegrationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & (Join-Path $taskOut 'NightDisplay.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
}
Write-Output "构建完成：$taskOut\NightDisplay.exe"
