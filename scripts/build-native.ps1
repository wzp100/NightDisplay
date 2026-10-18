param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
$OutputEncoding=[Console]::OutputEncoding
$taskRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $taskRoot 'artifacts\release' }
$taskOut=[System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $taskVsWhere)) { throw '原生守护构建需要 Visual Studio C++ Build Tools。' }
$taskVs=(& $taskVsWhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if (-not $taskVs) { throw '没有找到 Visual Studio C++ x64 工具。' }
$taskBatch=Join-Path $taskOut 'compile-guard.cmd'
$taskLines=@(
    '@chcp 65001 >nul',
    ('@call "'+$taskVs+'\VC\Auxiliary\Build\vcvars64.bat" >nul'),
    '@if errorlevel 1 exit /b 1',
    ('@cl /nologo /O1 /Oi /GS- /Zl /W4 /WX /utf-8 /DUNICODE /D_UNICODE /Fe:"'+$taskOut+'\NightDisplay.Guard.exe" /Fo:"'+$taskOut+'\guard.obj" "'+$taskRoot+'\native\Guard\main.cpp" /link /SUBSYSTEM:WINDOWS /ENTRY:GuardMain /NODEFAULTLIB kernel32.lib')
)
[System.IO.File]::WriteAllLines($taskBatch,$taskLines,[System.Text.UTF8Encoding]::new($false))
& $env:COMSPEC /d /c $taskBatch
if ($LASTEXITCODE -ne 0) { throw '原生守护构建失败。' }
