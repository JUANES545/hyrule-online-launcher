param(
    # Native: each Cemu reads Windows XInput pads 0 and 1. Routed: scripts/Build-Input.ps1 output assigns controllers through SDL3.
    [ValidateSet('Native', 'Routed')][string]$InputMode = 'Native',
    # The launcher already runs the router in its own process, so do not start InputRouter.exe.
    [switch]$ExternalRouter,
    [int]$ContinueTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$extract = Join-Path $root '.artifacts\extract'
$serverDirectory = Join-Path $extract 'milkbar\DedicatedServer'
$players = @(
    @{ Name = 'A'; Slot = 0; Cemu = Join-Path $root '.tools\instances\cemuA'; Launcher = Join-Path $extract 'milkbar' },
    @{ Name = 'B'; Slot = 1; Cemu = Join-Path $root '.tools\instances\cemuB'; Launcher = Join-Path $extract 'milkbarB' }
)
$gameLog = Join-Path $env:APPDATA 'BOTWM\LatestLog.txt'

# Same Pro Controller bindings as the local co-op launcher (Configure-Cemu.ps1).
$mappings = [ordered]@{
    1=13; 2=12; 3=15; 4=14; 5=8; 6=9; 7=42; 8=43; 9=4; 10=5;
    12=0; 13=1; 14=2; 15=3; 16=6; 17=7;
    18=39; 19=45; 20=44; 21=38; 22=41; 23=47; 24=46; 25=40
}

function Set-ControllerProfile {
    param([string]$CemuDirectory, [int]$Slot)
    $entries = ($mappings.GetEnumerator() | ForEach-Object { "<entry><mapping>$($_.Key)</mapping><button>$($_.Value)</button></entry>" }) -join ''
    $xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<emulated_controller>
  <type>Wii U Pro Controller</type><profile>Online P$($Slot + 1)</profile>
  <controller>
    <api>XInput</api><uuid>$Slot</uuid><display_name>Controller $($Slot + 1)</display_name>
    <rumble>0.65</rumble>
    <axis><deadzone>0.15</deadzone><range>1</range></axis>
    <rotation><deadzone>0.15</deadzone><range>1</range></rotation>
    <trigger><deadzone>0.05</deadzone><range>1</range></trigger>
    <mappings>$entries</mappings>
  </controller>
</emulated_controller>
"@
    $profiles = Join-Path $CemuDirectory 'controllerProfiles'
    New-Item -ItemType Directory -Force -Path $profiles | Out-Null
    Set-Content -LiteralPath (Join-Path $profiles 'controller0.xml') -Value $xml -Encoding UTF8
}

function Get-LogCount {
    param([string]$Pattern, [datetime]$Since)
    if (-not (Test-Path -LiteralPath $gameLog)) { return 0 }
    $lines = try { Get-Content -LiteralPath $gameLog -ErrorAction Stop } catch { @() }
    $count = 0
    foreach ($line in $lines) {
        if ($line -match '^\[\w+\]\[(\d\d-\d\d-\d{4} \d\d:\d\d:\d\d)\]') {
            $text = $Matches[1]
            if ($line -notmatch $Pattern) { continue }
            $stamp = [datetime]::ParseExact($text, 'dd-MM-yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
            if ($stamp -ge $Since.AddSeconds(-1)) { $count++ }
        }
    }
    return $count
}

function Get-CemuProcess {
    param([string]$Folder)
    foreach ($process in Get-Process -Name Cemu -ErrorAction SilentlyContinue) {
        try { if ($process.Path -like "$Folder\*") { $process.Refresh(); return $process } } catch { }
    }
    return $null
}

# Waits until a log line shows up after $Since. Returns $false on timeout.
function Wait-LogLine {
    param([string]$Pattern, [datetime]$Since, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-LogCount -Pattern $Pattern -Since $Since) -gt 0) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# Cemu shows "loading..." in its title until the game is running and its shaders are ready.
function Wait-GameLoaded {
    param([string]$Folder, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process = Get-CemuProcess -Folder $Folder
        if (-not $process) { throw "Cemu closed while loading ($Folder)." }
        if ($process.MainWindowTitle -match 'FPS:') { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

$inputRouter = Join-Path $root '.artifacts\input'
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -in 'Cemu', 'Milk Bar Launcher', 'MBL.DedicatedServer', 'InputRouter', 'HyruleCoop' }
if ($running) { throw 'Cemu, a Milk Bar launcher, the server or an input router is already running. Close it before starting an online session.' }
if ($InputMode -eq 'Routed' -and -not (Test-Path -LiteralPath (Join-Path $inputRouter 'Bridge\xinput1_4.dll'))) { throw 'Run scripts/Build-Input.ps1 first.' }
foreach ($player in $players) {
    foreach ($path in $player.Cemu, $player.Launcher) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path. Run the clean install first." }
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $serverDirectory 'MBL.DedicatedServer.exe'))) { throw 'The dedicated server is missing.' }

foreach ($player in $players) { Set-ControllerProfile -CemuDirectory $player.Cemu -Slot $player.Slot }

if ($InputMode -eq 'Routed') {
    foreach ($player in $players) { Copy-Item -LiteralPath (Join-Path $inputRouter 'Bridge\xinput1_4.dll') -Destination $player.Cemu -Force }
    if (-not $ExternalRouter) {
        $router = Start-Process -FilePath (Join-Path $inputRouter 'InputRouter.exe') -WorkingDirectory $inputRouter -WindowStyle Minimized -PassThru
        Write-Output "Input router started (pid $($router.Id))."
    }
}
else {
    foreach ($player in $players) { Remove-Item -LiteralPath (Join-Path $player.Cemu 'xinput1_4.dll') -ErrorAction SilentlyContinue }
}

$server = Start-Process -FilePath (Join-Path $serverDirectory 'MBL.DedicatedServer.exe') -WorkingDirectory $serverDirectory -PassThru
Write-Output "Server started (pid $($server.Id))."
Start-Sleep -Seconds 5

$sessionStart = Get-Date
foreach ($player in $players) {
    $started = Get-Date
    & (Join-Path $PSScriptRoot 'Connect-MilkBar.ps1') -LauncherDirectory $player.Launcher
    Write-Output "Player $($player.Name): wait for the game, then press Continue in its Cemu window (within ~60 s of the save loading)."
    # The server numbers players in the order their DLLs connect, and every DLL takes whichever launcher pipe is free,
    # so player B starts only after player A is linked. B then loads while the user presses Continue for A.
    if (-not (Wait-LogLine -Pattern 'Assigned to player' -Since $started -TimeoutSeconds 90)) {
        throw "Player $($player.Name) did not reach the server. Check $gameLog."
    }
    if ($player.Name -eq 'A') {
        Write-Output 'Player B starts as soon as player A has finished loading.'
        $loading = Get-Date
        if (Wait-GameLoaded -Folder $player.Cemu -TimeoutSeconds 300) { Write-Output "Player A finished loading after $([int]((Get-Date) - $loading).TotalSeconds) s." }
        else { Write-Output 'Player A is still loading; starting player B anyway.' }
    }
}

# Both DLLs write to the same log and its lines do not say which player they belong to, so count the joins.
$deadline = (Get-Date).AddSeconds($ContinueTimeoutSeconds)
$seen = 0
while ($seen -lt 2) {
    foreach ($player in $players) {
        if (-not (Get-CemuProcess -Folder $player.Cemu)) { throw "Cemu $($player.Name) closed before the session was ready. Check $gameLog." }
    }
    $count = Get-LogCount -Pattern 'Displayed "Joined server' -Since $sessionStart
    if ($count -gt $seen) { $seen = $count; Write-Output "$seen of 2 players joined." }
    if ($seen -ge 2) { break }
    if ((Get-Date) -gt $deadline) { throw "Only $seen of 2 players joined within $ContinueTimeoutSeconds s. Check $gameLog." }
    Start-Sleep -Seconds 2
}
Write-Output 'Both players are connected.'
