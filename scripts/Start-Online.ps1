param(
    # Native: each Cemu reads Windows XInput pads 0 and 1. Routed: scripts/Build-Input.ps1 output assigns controllers through SDL3.
    [ValidateSet('Native', 'Routed')][string]$InputMode = 'Native',
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

function Wait-JoinedServer {
    param([datetime]$Since, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $gameLog) {
            $lines = try { Get-Content -LiteralPath $gameLog -ErrorAction Stop } catch { @() }
            foreach ($line in $lines) {
                if ($line -match '^\[\w+\]\[(\d\d-\d\d-\d{4} \d\d:\d\d:\d\d)\].*Joined server') {
                    $stamp = [datetime]::ParseExact($Matches[1], 'dd-MM-yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
                    if ($stamp -ge $Since) { return $true }
                }
            }
        }
        Start-Sleep -Seconds 2
    }
    return $false
}

$inputRouter = Join-Path $root '.artifacts\input'
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -in 'Cemu', 'Milk Bar Launcher', 'MBL.DedicatedServer', 'InputRouter', 'HyruleCoop' }
if ($running) { throw 'Cemu, a Milk Bar launcher, the server or an input router is already running. Close it before starting an online session.' }
if ($InputMode -eq 'Routed' -and -not (Test-Path -LiteralPath (Join-Path $inputRouter 'InputRouter.exe'))) { throw 'Run scripts/Build-Input.ps1 first.' }
foreach ($player in $players) {
    foreach ($path in $player.Cemu, $player.Launcher) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path. Run the clean install first." }
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $serverDirectory 'MBL.DedicatedServer.exe'))) { throw 'The dedicated server is missing.' }

foreach ($player in $players) { Set-ControllerProfile -CemuDirectory $player.Cemu -Slot $player.Slot }

if ($InputMode -eq 'Routed') {
    foreach ($player in $players) { Copy-Item -LiteralPath (Join-Path $inputRouter 'Bridge\xinput1_4.dll') -Destination $player.Cemu -Force }
    $router = Start-Process -FilePath (Join-Path $inputRouter 'InputRouter.exe') -WorkingDirectory $inputRouter -WindowStyle Minimized -PassThru
    Write-Output "Input router started (pid $($router.Id))."
}
else {
    foreach ($player in $players) { Remove-Item -LiteralPath (Join-Path $player.Cemu 'xinput1_4.dll') -ErrorAction SilentlyContinue }
}

$server = Start-Process -FilePath (Join-Path $serverDirectory 'MBL.DedicatedServer.exe') -WorkingDirectory $serverDirectory -PassThru
Write-Output "Server started (pid $($server.Id))."
Start-Sleep -Seconds 5

foreach ($player in $players) {
    $started = Get-Date
    & (Join-Path $PSScriptRoot 'Connect-MilkBar.ps1') -LauncherDirectory $player.Launcher
    Write-Output "Player $($player.Name): wait for the game, then press Continue in its Cemu window (within ~60 s of the save loading)."
    if (-not (Wait-JoinedServer -Since $started -TimeoutSeconds $ContinueTimeoutSeconds)) {
        throw "Player $($player.Name) did not join the server within $ContinueTimeoutSeconds s. Check $gameLog."
    }
    Write-Output "Player $($player.Name) joined."
}
Write-Output 'Both players are connected.'
