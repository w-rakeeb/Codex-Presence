[CmdletBinding()]
param([switch] $SkipEngine, [string] $OutputDirectory)
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
Get-ChildItem -LiteralPath (Join-Path $taskProject 'releases\windows') -File | Copy-Item -Destination $taskRuntime
$taskIcon = Get-ChildItem -LiteralPath (Join-Path $taskProject '.build\target') -Filter 'codex-app.ico' -Recurse -File | Select-Object -First 1
if ($null -eq $taskIcon) { throw 'Build the engine first to generate its application icon.' }
Copy-Item -LiteralPath $taskIcon.FullName -Destination (Join-Path $taskProject 'desktop\app.ico')
Copy-Item -LiteralPath $taskIcon.FullName -Destination (Join-Path $taskRuntime 'app.ico')
dotnet publish (Join-Path $taskProject 'desktop\CodexPresence.csproj') -c Release -o $taskOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Copy-Item -LiteralPath (Join-Path $taskProject 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskProject 'desktop\Guide.md') -Destination (Join-Path $taskOutput 'Guide.md')
Write-Host "Ready: $(Join-Path $taskOutput 'Codex Presence.exe')"
