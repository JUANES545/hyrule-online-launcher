# Offline check of every player model the launcher offers. Needs the clean install; never starts the game.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root '.tools\python38\python.exe'
$dll = Join-Path $root '.artifacts\extract\milkbar\Milk Bar Launcher.dll'
if (-not (Test-Path -LiteralPath $python)) { throw 'The project Python is missing. Run the clean install first.' }
if (-not (Test-Path -LiteralPath $dll)) { throw 'The extracted Milk Bar launcher is missing. Run scripts/Build.ps1 or the clean install first.' }

$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
$stream = $assembly.GetManifestResourceStream('Breath_of_the_Wild_Multiplayer.Resources.NpcData.json')
$npcs = Join-Path ([IO.Path]::GetTempPath()) 'hyrule-npcs.json'
try {
    [IO.File]::WriteAllText($npcs, (New-Object IO.StreamReader($stream)).ReadToEnd())
    & $python (Join-Path $PSScriptRoot 'check_characters.py') --root $root --npcs $npcs
    $code = $LASTEXITCODE
}
finally { Remove-Item -LiteralPath $npcs -Force -ErrorAction SilentlyContinue }
if ($code -ne 0) { throw 'Character checks failed.' }
