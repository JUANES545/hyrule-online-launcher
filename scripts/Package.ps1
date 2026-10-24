param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -TotalCount 1).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'." }

$app = Join-Path $root '.artifacts\app'
$input = Join-Path $root '.artifacts\input'
foreach ($path in (Join-Path $app 'HyruleOnline.exe'), (Join-Path $input 'InputRouter.exe'), (Join-Path $input 'Bridge\xinput1_4.dll')) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path. Run scripts\Build.ps1 first." }
}

$dist = Join-Path $root '.artifacts\dist'
$stage = Join-Path $dist 'HyruleOnline'
if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage '.artifacts\app'), (Join-Path $stage '.artifacts\input'), (Join-Path $stage 'scripts') | Out-Null

# Only what the launcher needs at run time. Preferences, logs and third-party downloads stay out.
foreach ($name in 'HyruleOnline.exe', 'SDL3.dll', 'gamecontrollerdb.txt', 'GameControllerDB-LICENSE.txt', 'SDL-LICENSE.txt') {
    Copy-Item -LiteralPath (Join-Path $app $name) -Destination (Join-Path $stage '.artifacts\app')
}
Copy-Item -LiteralPath (Join-Path $app 'Fonts') -Destination (Join-Path $stage '.artifacts\app') -Recurse
Copy-Item -Path (Join-Path $input '*') -Destination (Join-Path $stage '.artifacts\input') -Recurse
Get-ChildItem -LiteralPath (Join-Path $stage '.artifacts\input') -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.lib' } | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
foreach ($name in 'Start-Online.ps1', 'Stop-Online.ps1', 'Cemu-Stall.ps1', 'Connect-MilkBar.ps1', 'New-Shortcut.ps1', 'Patch-Mod.ps1', 'patch_mod.py') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'scripts')
}
Copy-Item -LiteralPath (Join-Path $root 'VERSION') -Destination $stage

$zip = Join-Path $dist "HyruleOnline-$Version-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output "Portable package: $zip"

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    foreach ($folder in "${env:ProgramFiles(x86)}\Inno Setup 6", "$env:ProgramFiles\Inno Setup 6", "$env:LOCALAPPDATA\Programs\Inno Setup 6") {
        if (Test-Path -LiteralPath (Join-Path $folder 'ISCC.exe')) { $iscc = Get-Item -LiteralPath (Join-Path $folder 'ISCC.exe'); break }
    }
}
if (-not $iscc) { Write-Warning 'Inno Setup was not found; only the portable package was created.'; return }
$compiler = if ($iscc.PSObject.Properties['Source']) { $iscc.Source } else { $iscc.FullName }
& $compiler /Q "/DAppVersion=$Version" "/DSourceDir=$stage" "/O$dist" "/FHyruleOnline-$Version-setup" (Join-Path $root 'installer\HyruleOnline.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Output "Installer: $(Join-Path $dist "HyruleOnline-$Version-setup.exe")"
