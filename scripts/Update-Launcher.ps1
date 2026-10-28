# Applies a downloaded launcher update. Started by the launcher, which exits first, from a copy in %TEMP%.
# Runs the installer silently, checks the result and restarts the launcher. If anything fails, the one backup in
# .artifacts\update-backup puts the previous version back and a note is left for the launcher to show.
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory)][string]$Version,
    [int]$ProcessId = 0,
    [int]$Grace = 15
)

$ErrorActionPreference = 'Stop'
$backup = Join-Path $Root '.artifacts\update-backup'
$note = Join-Path $Root '.artifacts\update-failed.txt'

function Restore($from, $to) {
    if (-not (Test-Path $from)) { return }
    robocopy $from $to /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not restore $to." }
}

try {
    if ($ProcessId -gt 0) { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue }
    Remove-Item $note -Force -ErrorAction SilentlyContinue
    $install = Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$Root`"" -Wait -PassThru
    if ($install.ExitCode -ne 0) { throw "The installer exited with code $($install.ExitCode)." }
    $installed = (Get-Content (Join-Path $Root 'VERSION') -Raw).Trim()
    if ($installed -ne $Version) { throw "The installed version is $installed, not $Version." }
    $child = Start-Process -FilePath $Exe -PassThru
    # The new launcher deletes the backup once its window has opened, so a quick exit with the backup still there means it does not start.
    if ($child.WaitForExit($Grace * 1000) -and (Test-Path $backup)) { throw 'The new launcher closed right after it started.' }
}
catch {
    $reason = $_.Exception.Message
    try {
        Restore (Join-Path $backup 'app') (Split-Path $Exe)
        Restore (Join-Path $backup 'input') (Join-Path $Root '.artifacts\input')
        Restore (Join-Path $backup 'scripts') (Join-Path $Root 'scripts')
        Copy-Item (Join-Path $backup 'VERSION') (Join-Path $Root 'VERSION') -Force
        Remove-Item $backup -Recurse -Force
    }
    catch { $reason = "$reason Restoring the previous version also failed: $($_.Exception.Message)" }
    Set-Content -Path $note -Value $reason -Encoding UTF8
    Start-Process -FilePath $Exe
}