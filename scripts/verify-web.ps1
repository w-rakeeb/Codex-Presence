[CmdletBinding()]
param([Parameter(Mandatory)][string] $AppRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($AppRoot)
$taskHome = Join-Path $taskRoot ('Data\Verification\WebFixtures\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path (Join-Path $taskHome 'sessions') -Force | Out-Null
$taskNow = [DateTime]::UtcNow.ToString('o')
$taskEvents = @(
    @{type='session_meta'; timestamp=$taskNow; payload=@{id='web-interface-fixture'; cwd='D:\Projects\Presence Studio'; originator='codex_work_desktop'; source='vscode'}},
    @{type='turn_context'; timestamp=$taskNow; payload=@{model='gpt-5.4'; effort='high'}},
    @{type='event_msg'; timestamp=$taskNow; payload=@{type='task_started'}},
    @{type='response_item'; timestamp=$taskNow; payload=@{type='function_call'; name='exec_command'; arguments='{"cmd":"verify interface"}'; call_id='interface-fixture'}},
    @{type='event_msg'; timestamp=$taskNow; payload=@{type='token_count'; info=@{total_token_usage=@{input_tokens=840000; cached_input_tokens=720000; output_tokens=24000; total_tokens=864000}; last_token_usage=@{input_tokens=36000;cached_input_tokens=24000;output_tokens=4000;total_tokens=40000};model_context_window=272000};rate_limits=@{primary=@{used_percent=18;window_minutes=300;resets_at=[DateTimeOffset]::UtcNow.AddHours(2).ToUnixTimeSeconds()};secondary=@{used_percent=32;window_minutes=10080;resets_at=[DateTimeOffset]::UtcNow.AddDays(3).ToUnixTimeSeconds()}}}}
)
$taskJsonl = ($taskEvents | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 20 -Compress }) -join "`n"
[IO.File]::WriteAllText((Join-Path $taskHome 'sessions\fixture.jsonl'), $taskJsonl + "`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $taskHome 'session_index.jsonl'), '{"id":"web-interface-fixture","thread_name":"Presence Studio","updated_at":"' + $taskNow + '"}' + "`n", [Text.UTF8Encoding]::new($false))
$taskStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $taskRoot 'Codex Presence.exe'))
$taskStart.UseShellExecute = $false
$taskStart.WorkingDirectory = $taskRoot
$taskStart.ArgumentList.Add('--web-test')
$taskStart.ArgumentList.Add('--test-home')
$taskStart.ArgumentList.Add($taskHome)
$taskProcess = [Diagnostics.Process]::Start($taskStart)
if (-not $taskProcess.WaitForExit(120000)) { throw 'Web interface verification did not finish within 120 seconds.' }
if ($taskProcess.ExitCode -ne 0) { throw (Get-Content (Join-Path $taskRoot 'Data\Verification\Web\failure.txt') -Raw) }
$taskResults = Get-Content (Join-Path $taskRoot 'Data\Verification\Web\results.json') -Raw | ConvertFrom-Json
Write-Output ('Web interface verification: ' + $taskResults.passed + ' checks passed.')
