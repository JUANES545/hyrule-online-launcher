$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root '.artifacts\tests'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'RouterTests.exe')" /reference:System.Xml.Linq.dll `
    (Join-Path $root 'tests\RouterTests.cs') (Join-Path $root 'tests\LanguageTests.cs') (Join-Path $root 'src\Input\Router.cs') (Join-Path $root 'src\Input\Sdl.cs') (Join-Path $root 'src\Input\ControllerMotion.cs') (Join-Path $root 'src\Launcher\Layout.cs') (Join-Path $root 'src\Launcher\CemuDisplay.cs') (Join-Path $root 'src\Launcher\RotationMod.cs') (Join-Path $root 'src\Launcher\FpsMod.cs') (Join-Path $root 'src\Launcher\GraphicsMod.cs') (Join-Path $root 'src\Launcher\Hardware.cs') (Join-Path $root 'src\Launcher\Navigation.cs')     (Join-Path $root 'src\Launcher\Characters.cs') (Join-Path $root 'src\Launcher\Lang.cs') (Join-Path $root 'src\Launcher\ServerRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $output 'RouterTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Router tests failed.' }

$wpf = Join-Path $framework 'WPF'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'BadgeTests.exe')" "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" "/reference:$(Join-Path $wpf 'PresentationCore.dll')" "/reference:$(Join-Path $wpf 'WindowsBase.dll')" /reference:System.Xaml.dll `
    (Join-Path $root 'tests\BadgeTests.cs') (Join-Path $root 'src\Launcher\PlayerBadge.cs')
if ($LASTEXITCODE -ne 0) { throw 'Badge test compilation failed.' }
& (Join-Path $output 'BadgeTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Badge tests failed.' }

# The model check needs the clean install (project Python and the extracted Milk Bar launcher); without it only the unit tests run.
if ((Test-Path -LiteralPath (Join-Path $root '.tools\python38\python.exe')) -and (Test-Path -LiteralPath (Join-Path $root '.artifacts\extract\milkbar\Milk Bar Launcher.dll'))) { & (Join-Path $PSScriptRoot 'Test-Characters.ps1') }
else { Write-Host 'Skipping the character check: the clean install is not here.' }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Cemu-Stall.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Stall detection tests failed.' }
