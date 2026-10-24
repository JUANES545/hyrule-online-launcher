# Spots a Cemu that froze while loading the game. Dot-sourced by Start-Online.ps1 and by tests\Cemu-Stall.Tests.ps1.
#
# A healthy Cemu logs one Vulkan texture allocation right after "IOSU_ACT", then at least two more about half a minute
# later, when the main menu shows up. A stuck one stops after the first. $Since ignores a log left over from a
# previous attempt, because Cemu writes log.txt only once it has started again.
function Test-CemuStalled {
    param(
        [string[]]$LogLines,
        [datetime]$Now,
        [datetime]$Since = [datetime]'2000-01-01',
        [int]$StallSeconds = 100
    )
    $start = $null
    $allocations = 0
    foreach ($line in $LogLines) {
        if ($line -notmatch '^\[(\d\d:\d\d:\d\d)\] (.*)$') { continue }
        $stamp = $Now.Date + [timespan]::Parse($Matches[1])
        if ($stamp -gt $Now) { $stamp = $stamp.AddDays(-1) }
        if ($Matches[2] -like 'IOSU_ACT*') {
            if ($stamp -lt $Since.AddSeconds(-2)) { continue }
            $start = $stamp
            $allocations = 0
        }
        elseif ($start -and $Matches[2] -like 'Vulkan-Info: Allocated additional memory*') { $allocations++ }
    }
    if (-not $start -or $allocations -eq 0) { return $false }
    return $allocations -lt 3 -and ($Now - $start).TotalSeconds -ge $StallSeconds
}

# Only player B is restarted, and only a limited number of times before giving up.
# $Since is when this attempt of the player started, so the log of the attempt before it does not count.
function Test-RestartPlayer {
    param($Player, [int]$Restarts, [int]$MaxRestarts, [string[]]$LogLines, [datetime]$Now)
    if (-not $Player -or $Player.Name -ne 'B' -or $Restarts -ge $MaxRestarts) { return $false }
    return Test-CemuStalled -LogLines $LogLines -Now $Now -Since $Player.Started
}
