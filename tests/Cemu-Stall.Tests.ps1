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

if ($failures -gt 0) { throw "$failures stall check(s) failed." }
