$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo '.artifacts\tests\stop-online'
$ping = Join-Path $env:SystemRoot 'System32\PING.EXE'
$failures = 0
$started = @()

function Check {
    param([string]$Name, [bool]$Expected, [bool]$Actual)
    if ($Expected -ne $Actual) { Write-Host "FAIL: $Name (expected $Expected, got $Actual)"; $script:failures++ }
    else { Write-Host "ok:   $Name" }
}

# Dummy processes are copies of ping.exe with the names Stop-Online looks for, so no real Cemu or launcher is touched.
function Start-Dummy {
    param([string]$Folder, [string]$Name)
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $exe = Join-Path $Folder $Name
    Copy-Item -LiteralPath $ping -Destination $exe
    $process = Start-Process -FilePath $exe -ArgumentList '-n', '300', '127.0.0.1' -WindowStyle Hidden -PassThru
    $script:started += $process
    $process
}

function Alive { param($Process) $null -ne (Get-Process -Id $Process.Id -ErrorAction SilentlyContinue) }

function Run-Stop {
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $work 'root\scripts\Stop-Online.ps1') 2>&1
    $script:stopExit = $LASTEXITCODE
    ($output | ForEach-Object { "$_" }) -join "`n"
}

try {
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $work 'root\scripts') | Out-Null
    Copy-Item (Join-Path $repo 'scripts\Stop-Online.ps1') (Join-Path $work 'root\scripts\Stop-Online.ps1')

    $cemu = Start-Dummy (Join-Path $work 'root\cemuA') 'Cemu.exe'
    $server = Start-Dummy (Join-Path $work 'root\server') 'MBL.DedicatedServer.exe'
    $router = Start-Dummy (Join-Path $work 'root\bin') 'InputRouter.exe'
    $other = Start-Dummy (Join-Path $work 'root\tools') 'Notes.exe'
    $sibling = Start-Dummy (Join-Path $work 'root-other\cemuA') 'Cemu.exe'
    $outside = Start-Dummy (Join-Path $work 'elsewhere') 'Cemu.exe'
    Start-Sleep -Milliseconds 700
    Check 'the dummy processes started' $true (($started | Where-Object { Alive $_ }).Count -eq $started.Count)

    $first = Run-Stop
    Start-Sleep -Milliseconds 700
    Check 'the script finishes without an error' $true ($stopExit -eq 0)
    Check 'Cemu from the project is stopped' $false (Alive $cemu)
    Check 'the dedicated server from the project is stopped' $false (Alive $server)
    Check 'the input router from the project is stopped' $false (Alive $router)
    Check 'each stopped process is reported' $true ($first -match 'Stopping Cemu \(' -and $first -match 'Stopping MBL\.DedicatedServer \(' -and $first -match 'Stopping InputRouter \(')
    Check 'other programs in the project are left alone' $true (Alive $other)
    Check 'a Cemu in a folder that only shares the project name prefix is left alone' $true (Alive $sibling)
    Check 'a Cemu from another folder is left alone' $true (Alive $outside)

    $second = Run-Stop
    Check 'running it again with nothing left to stop is harmless' $true ($stopExit -eq 0 -and $second -match 'Nothing to stop\.' -and $second -notmatch 'Stopping')
    Check 'the bystanders are still running after the second run' $true ((Alive $other) -and (Alive $sibling) -and (Alive $outside))
}
finally {
    foreach ($process in $started) {
        if (Alive $process) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Milliseconds 300
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { throw "$failures stop check(s) failed." }
