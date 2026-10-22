param(
    # Only report what is missing; exits with 1 if anything needs patching.
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root '.tools\python38\python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'The project Python is missing. Run the clean install first.' }

if (-not $Check) {
    $running = Get-Process -Name Cemu -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw 'Close the project Cemu instances before patching (scripts/Stop-Online.ps1).' }
}

$arguments = @((Join-Path $PSScriptRoot 'patch_mod.py'), '--root', $root)
if ($Check) { $arguments += '--check' }
& $python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
