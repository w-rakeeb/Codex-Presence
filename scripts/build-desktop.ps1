[CmdletBinding()]
param([switch] $SkipEngine, [string] $OutputDirectory, [string] $EngineDirectory)
$ErrorActionPreference = "Stop"
$taskProject = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskProject '.build\desktop'
if ($OutputDirectory) { if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }; $taskOutput = [IO.Path]::GetFullPath($OutputDirectory) }
if (-not $SkipEngine) {
    $taskRelease = Join-Path $taskProject 'releases\windows'
    if ((Test-Path -LiteralPath $taskRelease) -and (Get-ChildItem -LiteralPath $taskRelease -Force | Select-Object -First 1)) {
        $taskArchive = Join-Path $taskProject ('.build\release-archive\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
        $taskResolvedProject = [IO.Path]::GetFullPath($taskProject)
        if (-not ([IO.Path]::GetFullPath($taskRelease)).StartsWith($taskResolvedProject + '\releases\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release archive source.' }
        if (-not ([IO.Path]::GetFullPath($taskArchive)).StartsWith($taskResolvedProject + '\.build\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release archive destination.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskArchive) -Force | Out-Null
        Move-Item -LiteralPath $taskRelease -Destination $taskArchive
    }
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Architecture x64
}
$taskRuntime = Join-Path $taskOutput 'Runtime'
New-Item -ItemType Directory -Path $taskRuntime -Force | Out-Null
if ($EngineDirectory -and -not $SkipEngine) { throw 'EngineDirectory is only supported with SkipEngine.' }
$taskEngines = if ($EngineDirectory) { [IO.Path]::GetFullPath($EngineDirectory) } else { Join-Path $taskProject 'releases\windows' }
Get-ChildItem -LiteralPath $taskEngines -File | Copy-Item -Destination $taskRuntime
$taskDesktopIcon = Join-Path $taskProject 'desktop\Assets\app.ico'
Copy-Item -LiteralPath $taskDesktopIcon -Destination (Join-Path $taskProject 'desktop\app.ico') -Force
Copy-Item -LiteralPath $taskDesktopIcon -Destination (Join-Path $taskRuntime 'app.ico')
Copy-Item -LiteralPath (Join-Path $taskProject 'desktop\Assets') -Destination $taskOutput -Recurse -Force
Push-Location (Join-Path $taskProject 'desktop\Web')
try {
    & npm.cmd ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Interface dependencies could not be restored.' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Interface build failed.' }
}
finally { Pop-Location }
dotnet publish (Join-Path $taskProject 'desktop\CodexPresence.csproj') -c Release -o $taskOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Copy-Item -LiteralPath (Join-Path $taskProject 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskProject 'desktop\Guide.md') -Destination (Join-Path $taskOutput 'Guide.md')
Write-Host "Ready: $(Join-Path $taskOutput 'Codex Presence.exe')"
