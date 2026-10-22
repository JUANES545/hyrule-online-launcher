param(
    [string]$Folder = [Environment]::GetFolderPath('Desktop'),
    [string]$Name = 'Hyrule Online'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root '.artifacts\app'
$exe = Join-Path $app 'HyruleOnline.exe'
if (-not (Test-Path $exe)) { throw 'Run scripts/Build.ps1 first.' }

$icon = Join-Path $root '.tools\instances\cemuA\cemu.exe'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $Folder "$Name.lnk"))
$link.TargetPath = $exe
$link.WorkingDirectory = $app
$link.Description = 'Two-player Breath of the Wild launcher and settings'
if (Test-Path $icon) { $link.IconLocation = "$icon,0" }
$link.Save()
Write-Host "Shortcut ready: $(Join-Path $Folder "$Name.lnk")"
