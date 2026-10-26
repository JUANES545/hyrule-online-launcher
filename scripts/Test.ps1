$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root '.artifacts\tests'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'RouterTests.exe')" /reference:System.Xml.Linq.dll `
    (Join-Path $root 'tests\RouterTests.cs') (Join-Path $root 'tests\LanguageTests.cs') (Join-Path $root 'src\Input\Router.cs') (Join-Path $root 'src\Input\Sdl.cs') (Join-Path $root 'src\Input\ControllerMotion.cs') (Join-Path $root 'src\Launcher\Layout.cs') (Join-Path $root 'src\Launcher\CemuDisplay.cs') (Join-Path $root 'src\Launcher\RotationMod.cs') (Join-Path $root 'src\Launcher\FpsMod.cs') (Join-Path $root 'src\Launcher\GraphicsMod.cs') (Join-Path $root 'src\Launcher\Hardware.cs') (Join-Path $root 'src\Launcher\Navigation.cs') (Join-Path $root 'src\Launcher\ShellFit.cs') (Join-Path $root 'src\Launcher\Characters.cs') (Join-Path $root 'src\Launcher\Lang.cs') (Join-Path $root 'src\Launcher\ServerRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $output 'RouterTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Router tests failed.' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'StartupTests.exe')" /reference:System.Xml.Linq.dll `
    (Join-Path $root 'tests\StartupTests.cs') (Join-Path $root 'src\Launcher\PathGuard.cs') (Join-Path $root 'src\Launcher\Prefs.cs') (Join-Path $root 'src\Launcher\ServerRules.cs') (Join-Path $root 'src\Launcher\Layout.cs') (Join-Path $root 'src\Launcher\GraphicsMod.cs') (Join-Path $root 'src\Launcher\FpsMod.cs') (Join-Path $root 'src\Launcher\Characters.cs') (Join-Path $root 'src\Launcher\Lang.cs') (Join-Path $root 'src\Input\Sdl.cs') (Join-Path $root 'src\Input\ControllerMotion.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup test compilation failed.' }
& (Join-Path $output 'StartupTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Startup tests failed.' }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'SetupTests.exe')" /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    (Join-Path $root 'tests\SetupTests.cs') (Join-Path $root 'src\Launcher\Setup.cs') (Join-Path $root 'src\Launcher\GraphicsMod.cs') (Join-Path $root 'src\Launcher\Hardware.cs') (Join-Path $root 'src\Launcher\Lang.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup test compilation failed.' }
& (Join-Path $output 'SetupTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Setup tests failed.' }

$wpf = Join-Path $framework 'WPF'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'BadgeTests.exe')" "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" "/reference:$(Join-Path $wpf 'PresentationCore.dll')" "/reference:$(Join-Path $wpf 'WindowsBase.dll')" /reference:System.Xaml.dll `
    (Join-Path $root 'tests\BadgeTests.cs') (Join-Path $root 'src\Launcher\PlayerBadge.cs')
if ($LASTEXITCODE -ne 0) { throw 'Badge test compilation failed.' }
& (Join-Path $output 'BadgeTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Badge tests failed.' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'ShaderCacheTests.exe')" (Join-Path $root 'tests\ShaderCacheTests.cs') (Join-Path $root 'src\Launcher\ShaderCache.cs')
if ($LASTEXITCODE -ne 0) { throw 'Shader cache test compilation failed.' }
& (Join-Path $output 'ShaderCacheTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Shader cache tests failed.' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'ServerEventsTests.exe')" (Join-Path $root 'tests\ServerEventsTests.cs') (Join-Path $root 'src\Launcher\ServerEvents.cs')
if ($LASTEXITCODE -ne 0) { throw 'Server events test compilation failed.' }
& (Join-Path $output 'ServerEventsTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Server events tests failed.' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'NetworkHintTests.exe')" (Join-Path $root 'tests\NetworkHintTests.cs') (Join-Path $root 'src\Launcher\NetworkHint.cs')
if ($LASTEXITCODE -ne 0) { throw 'Network hint test compilation failed.' }
& (Join-Path $output 'NetworkHintTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Network hint tests failed.' }

& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'ServerFinderTests.exe')" /reference:System.Web.Extensions.dll (Join-Path $root 'tests\ServerFinderTests.cs') (Join-Path $root 'src\Launcher\ServerFinder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Server finder test compilation failed.' }
& (Join-Path $output 'ServerFinderTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Server finder tests failed.' }

# Keeps steamos\parity.json and the SteamOS launcher in step with the Windows launcher; a failure says what to update.
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'ParityTests.exe')" /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll (Join-Path $root 'tests\ParityTests.cs') (Join-Path $root 'src\Launcher\Lang.cs') (Join-Path $root 'src\Launcher\ServerRules.cs') (Join-Path $root 'src\Launcher\ServerFinder.cs') (Join-Path $root 'src\Launcher\Characters.cs')
if ($LASTEXITCODE -ne 0) { throw 'Parity test compilation failed.' }
& (Join-Path $output 'ParityTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Windows and SteamOS launchers are out of step (see the parity test output above and "Keeping the Windows and SteamOS launchers in sync" in CONTRIBUTING.md).' }

& (Join-Path $PSScriptRoot 'Test-Layout.ps1')

# The model check needs the clean install (project Python and the extracted Milk Bar launcher); without it only the unit tests run.
if ((Test-Path -LiteralPath (Join-Path $root '.tools\python38\python.exe')) -and (Test-Path -LiteralPath (Join-Path $root '.artifacts\extract\milkbar\Milk Bar Launcher.dll'))) { & (Join-Path $PSScriptRoot 'Test-Characters.ps1') }
else { Write-Host 'Skipping the character check: the clean install is not here.' }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Cemu-Stall.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Stall detection tests failed.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Path-Guard.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Path guard script tests failed.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Stop-Online.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Clean stop tests failed.' }
