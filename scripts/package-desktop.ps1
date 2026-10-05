[CmdletBinding()]
param([Parameter(Mandatory)][string] $AppRoot, [string] $Version = '1.6.0', [string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathFullyQualified($AppRoot)) { throw 'AppRoot must be absolute.' }
$taskApp = [IO.Path]::GetFullPath($AppRoot)
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskProject ('releases\desktop-v' + $Version) }
$taskPackage = Join-Path $taskOutput ('Codex-Presence-' + $Version + '-windows-x64')
if (Test-Path -LiteralPath $taskPackage) { throw 'The package folder already exists. Choose a new output directory.' }
New-Item -ItemType Directory -Path $taskPackage -Force | Out-Null
foreach ($taskName in @('Codex Presence.exe', 'Guide.md', 'LICENSE.txt')) {
    $taskFile = Join-Path $taskApp $taskName
    if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw ('Missing package file: ' + $taskName) }
    Copy-Item -LiteralPath $taskFile -Destination $taskPackage
}
Copy-Item -LiteralPath (Join-Path $taskApp 'Runtime') -Destination (Join-Path $taskPackage 'Runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $taskProject 'NOTICE.md') -Destination $taskPackage
$taskPaths = Get-ChildItem -LiteralPath $taskPackage -File -Recurse | Sort-Object FullName
$taskSums = foreach ($taskFile in $taskPaths) { (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $taskFile.FullName.Substring($taskPackage.Length + 1).Replace('\', '/') }
[IO.File]::WriteAllLines((Join-Path $taskPackage 'SHA256SUMS.txt'), [string[]]$taskSums, [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskZip = $taskPackage + '.zip'
if (Test-Path -LiteralPath $taskZip) { throw 'The zip already exists. Published assets should not be replaced.' }
[IO.Compression.ZipFile]::CreateFromDirectory($taskPackage, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$taskZipHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256SUMS.txt'), $taskZipHash + '  ' + [IO.Path]::GetFileName($taskZip) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host ('Package: ' + $taskZip)
Write-Host ('SHA-256: ' + $taskZipHash)
