param([switch]$Source)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$taskRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskStage = Join-Path $taskRoot ('artifacts\package-staging\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage -Force | Out-Null
if ($Source) {
    $taskNames = @('README.md','使用说明.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md','.gitignore','.gitattributes','.editorconfig','NightDisplay.sln','build.ps1','.github/workflows/ci.yml')
    foreach ($taskFolder in @('src','native','tests','tools','web','assets','driver','docs','scripts')) {
        $taskBase = Join-Path $taskRoot $taskFolder
        $taskFiles = Get-ChildItem -LiteralPath $taskBase -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            ($_.Extension -in @('.cs','.xaml','.cpp','.csproj','.html','.css','.js','.png','.ico','.inf','.dll','.cat','.xml','.txt','.md','.ps1') -or $_.Name -eq 'packages.lock.json')
        }
        foreach ($taskFile in $taskFiles) { $taskNames += $taskFile.FullName.Substring($taskRoot.Length + 1) }
    }
    $taskZip = Join-Path $taskRoot 'artifacts\NightDisplay-1.3.0-source.zip'
    foreach ($taskName in $taskNames) {
        $taskDestination = Join-Path $taskStage $taskName
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskDestination
    }
} else {
    $taskRelease = Join-Path $taskRoot 'artifacts\release'
    if (-not (Test-Path -LiteralPath (Join-Path $taskRelease 'NightDisplay.exe'))) { throw '请先运行 build.ps1。' }
    foreach ($taskName in @('NightDisplay.exe','NightDisplay.Guard.exe','driver','LICENSE','THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $taskRelease $taskName) -Destination $taskStage -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskStage
    $taskZip = Join-Path $taskRoot 'artifacts\NightDisplay-1.3.0-win-x64.zip'
}
$taskEntries = @(Get-ChildItem -LiteralPath $taskStage -Force | Select-Object -ExpandProperty FullName)
Compress-Archive -LiteralPath $taskEntries -DestinationPath $taskZip -Force
Write-Output $taskZip
