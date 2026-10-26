$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Cemu-Stall.ps1')

$stalled = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures\cemu-stalled.log')
$healthy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures\cemu-healthy.log')
$failures = 0

function Check {
    param([string]$Name, [bool]$Expected, [bool]$Actual)
    if ($Expected -ne $Actual) { Write-Host "FAIL: $Name (expected $Expected, got $Actual)"; $script:failures++ }
    else { Write-Host "ok:   $Name" }
}

$stalledIosu = [datetime]'2026-10-22 22:48:35'
$healthyIosu = [datetime]'2026-10-22 21:43:25'

Check 'a Cemu that stopped after its first allocation is stalled' $true (Test-CemuStalled -LogLines $stalled -Now $stalledIosu.AddSeconds(120))
Check 'it is not judged before the stall time has passed' $false (Test-CemuStalled -LogLines $stalled -Now $stalledIosu.AddSeconds(40))
Check 'a healthy Cemu that reached the menu is never stalled' $false (Test-CemuStalled -LogLines $healthy -Now $healthyIosu.AddMinutes(10))
Check 'a log left over from the previous attempt is ignored' $false (Test-CemuStalled -LogLines $stalled -Now $stalledIosu.AddSeconds(300) -Since $stalledIosu.AddSeconds(150))
Check 'a log from this attempt still counts' $true (Test-CemuStalled -LogLines $stalled -Now $stalledIosu.AddSeconds(300) -Since $stalledIosu.AddSeconds(-30))
Check 'a Cemu that has not logged IOSU_ACT yet is still starting' $false (Test-CemuStalled -LogLines $stalled[0..4] -Now $stalledIosu.AddMinutes(10))
Check 'a Cemu with no Vulkan lines (another graphics API) is not judged' $false (Test-CemuStalled -LogLines ($stalled | Where-Object { $_ -notlike '*Vulkan-Info*' }) -Now $stalledIosu.AddMinutes(10))
Check 'an empty log is not stalled' $false (Test-CemuStalled -LogLines @() -Now $stalledIosu)
Check 'the stall time can be changed' $true (Test-CemuStalled -LogLines $stalled -Now $stalledIosu.AddSeconds(50) -StallSeconds 45)
Check 'a clock past midnight still measures from the log' $true (Test-CemuStalled -LogLines @('[23:58:00] IOSU_ACT: x', '[23:58:05] Vulkan-Info: Allocated additional memory for textures') -Now ([datetime]'2026-10-23 00:00:30'))

function Log { param([int]$Allocations) @('[10:00:00] IOSU_ACT: account') + @(1..$Allocations | Where-Object { $Allocations -gt 0 } | ForEach-Object { '[10:00:05] Vulkan-Info: Allocated additional memory for textures' }) }
$logStart = [datetime]'2026-10-22 10:00:00'

# A log that stops growing
Check 'a log that stops after one allocation is stalled once the time passes' $true (Test-CemuStalled -LogLines (Log 1) -Now $logStart.AddSeconds(100))
Check 'a log that stops after two allocations is still stalled' $true (Test-CemuStalled -LogLines (Log 2) -Now $logStart.AddSeconds(100))
Check 'three allocations mean the menu was reached' $false (Test-CemuStalled -LogLines (Log 3) -Now $logStart.AddMinutes(30))
Check 'just under the stall time is not judged yet' $false (Test-CemuStalled -LogLines (Log 1) -Now $logStart.AddSeconds(99))
Check 'exactly the stall time is judged' $true (Test-CemuStalled -LogLines (Log 1) -Now $logStart.AddSeconds(100))
Check 'a log that still grows later is no longer stalled' $false (Test-CemuStalled -LogLines ((Log 1) + @('[10:00:35] Vulkan-Info: Allocated additional memory for textures', '[10:00:36] Vulkan-Info: Allocated additional memory for textures')) -Now $logStart.AddSeconds(200))
Check 'lines that are not log entries are skipped' $true (Test-CemuStalled -LogLines (@('garbage', '') + (Log 1) + @('trailing text')) -Now $logStart.AddSeconds(150))
Check 'a second start in the same log restarts the count' $false (Test-CemuStalled -LogLines ((Log 1) + @('[10:05:00] IOSU_ACT: account')) -Now $logStart.AddMinutes(5).AddSeconds(30))

# Which player is restarted
$players = @{
    A = [pscustomobject]@{ Name = 'A'; Started = $logStart.AddSeconds(-10) }
    B = [pscustomobject]@{ Name = 'B'; Started = $logStart.AddSeconds(-10) }
}
$late = $logStart.AddSeconds(200)
Check 'player B is restarted when it stalls' $true (Test-RestartPlayer -Player $players.B -Restarts 0 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
Check 'player A is never restarted, even when it stalls' $false (Test-RestartPlayer -Player $players.A -Restarts 0 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
Check 'a healthy player B is left alone' $false (Test-RestartPlayer -Player $players.B -Restarts 0 -MaxRestarts 2 -LogLines (Log 3) -Now $late)
Check 'player B is restarted again after one stall' $true (Test-RestartPlayer -Player $players.B -Restarts 1 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
Check 'repeated stalls stop at the limit' $false (Test-RestartPlayer -Player $players.B -Restarts 2 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
Check 'a limit of zero never restarts' $false (Test-RestartPlayer -Player $players.B -Restarts 0 -MaxRestarts 0 -LogLines (Log 1) -Now $late)
Check 'no player means nothing to restart' $false (Test-RestartPlayer -Player $null -Restarts 0 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
$retried = [pscustomobject]@{ Name = 'B'; Started = $logStart.AddSeconds(150) }
Check 'the log of the attempt before a restart does not trigger another one' $false (Test-RestartPlayer -Player $retried -Restarts 1 -MaxRestarts 2 -LogLines (Log 1) -Now $late)
Check 'the new attempt is judged on its own log' $true (Test-RestartPlayer -Player $retried -Restarts 1 -MaxRestarts 2 -LogLines ((Log 3) + @('[10:02:30] IOSU_ACT: account', '[10:02:31] Vulkan-Info: Allocated additional memory for textures')) -Now $logStart.AddSeconds(300))
Check 'the stalled fixture restarts player B' $true (Test-RestartPlayer -Player ([pscustomobject]@{ Name = 'B'; Started = $stalledIosu.AddSeconds(-20) }) -Restarts 0 -MaxRestarts 2 -LogLines $stalled -Now $stalledIosu.AddSeconds(120))
Check 'the healthy fixture never restarts player B' $false (Test-RestartPlayer -Player ([pscustomobject]@{ Name = 'B'; Started = $healthyIosu.AddSeconds(-20) }) -Restarts 0 -MaxRestarts 2 -LogLines $healthy -Now $healthyIosu.AddMinutes(10))

if ($failures -gt 0) { throw "$failures stall check(s) failed." }
