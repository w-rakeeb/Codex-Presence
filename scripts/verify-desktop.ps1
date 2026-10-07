[CmdletBinding()]
param([switch] $CrossVolume, [switch] $Profile, [string] $AppRoot)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskRoot = Join-Path $taskProject '.build\desktop'
if ($AppRoot) { $taskRoot = [IO.Path]::GetFullPath($AppRoot) }
$taskEvidence = Join-Path $taskRoot 'Data\Verification'
$taskRun = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$taskHome = if ($CrossVolume) { Join-Path $env:TEMP ('CodexPresence-Verification-' + $taskRun + '\CodexHome') } else { Join-Path $taskEvidence ('Runs\' + $taskRun + '\CodexHome') }
if ($CrossVolume -and [IO.Path]::GetPathRoot($taskHome) -eq [IO.Path]::GetPathRoot($taskRoot)) { throw 'Cross-volume verification requires TEMP on a different drive from the app.' }
$taskSessionFolder = Join-Path $taskHome 'sessions'
New-Item -ItemType Directory -Path $taskSessionFolder -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $taskHome 'session_index.jsonl'), '{"id":"desktop-verification","thread_name":"Verification chat title","updated_at":"2026-10-04T00:00:00Z"}' + "`n", [Text.UTF8Encoding]::new($false))
$taskNow = [DateTime]::UtcNow.ToString('o')
$taskEvents = @(
    @{ type = 'session_meta'; timestamp = $taskNow; payload = @{ id = 'desktop-verification'; cwd = $taskRoot; originator = 'codex_work_desktop'; source = 'vscode' } },
    @{ type = 'turn_context'; timestamp = $taskNow; payload = @{ model = 'gpt-5.4'; effort = 'high'; service_tier = 'fast' } },
    @{ type = 'event_msg'; timestamp = $taskNow; payload = @{ type = 'task_started' } },
    @{ type = 'response_item'; timestamp = $taskNow; payload = @{ type = 'function_call'; name = 'exec_command'; arguments = '{"cmd":"verify fixture"}'; call_id = 'fixture-command' } },
    @{ type = 'event_msg'; timestamp = $taskNow; payload = @{ type = 'token_count'; info = @{ total_token_usage = @{ input_tokens = 8000; cached_input_tokens = 2000; output_tokens = 1200; total_tokens = 9200 }; last_token_usage = @{ input_tokens = 8000; cached_input_tokens = 2000; output_tokens = 1200; total_tokens = 9200 }; model_context_window = 272000 }; rate_limits = @{ primary = @{ used_percent = 12; window_minutes = 300; resets_at = [DateTimeOffset]::UtcNow.AddHours(2).ToUnixTimeSeconds() }; secondary = @{ used_percent = 24; window_minutes = 10080; resets_at = [DateTimeOffset]::UtcNow.AddDays(3).ToUnixTimeSeconds() }; credits = @{ has_credits = $true; unlimited = $false; balance = '42.00' } } } }
)
$taskJsonl = ($taskEvents | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 20 -Compress }) -join "`n"
[IO.File]::WriteAllText((Join-Path $taskSessionFolder 'fixture.jsonl'), $taskJsonl + "`n", [Text.UTF8Encoding]::new($false))
$taskStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $taskRoot 'Codex Presence.exe'))
$taskStart.UseShellExecute = $false
$taskStart.WorkingDirectory = $taskRoot
$taskStart.ArgumentList.Add('--self-test')
if ($Profile) { $taskStart.ArgumentList.Add('--profile-quality') }
$taskStart.ArgumentList.Add('--test-home')
$taskStart.ArgumentList.Add($taskHome)
$taskProcess = [Diagnostics.Process]::Start($taskStart)
if (-not $taskProcess.WaitForExit(90000)) { throw 'Desktop verification did not finish within 90 seconds; inspect its window.' }
if ($taskProcess.ExitCode -ne 0) { throw (Get-Content (Join-Path $taskEvidence 'failure.txt') -Raw) }
$taskResult = Get-Content (Join-Path $taskEvidence 'results.json') -Raw | ConvertFrom-Json
$taskResultPath = Join-Path $taskEvidence $(if ($CrossVolume) { 'results-cross-volume.json' } else { 'results-same-volume.json' })
Copy-Item -LiteralPath (Join-Path $taskEvidence 'results.json') -Destination $taskResultPath
Write-Host "Desktop verification: $($taskResult.passed) checks passed."
$taskResult.checks | ForEach-Object { Write-Host "PASS $_" }
Write-Host "Evidence: $taskEvidence"
