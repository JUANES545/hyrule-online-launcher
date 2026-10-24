param([string]$ZigPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$input = Join-Path $root '.artifacts\input'
$output = Join-Path $root '.artifacts\app'

if ($ZigPath) { & (Join-Path $PSScriptRoot 'Build-Input.ps1') -ZigPath $ZigPath } else { & (Join-Path $PSScriptRoot 'Build-Input.ps1') }

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (Get-Process -Name HyruleOnline -ErrorAction SilentlyContinue) { throw 'Close Hyrule Online before rebuilding.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $root 'src'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ "/out:$(Join-Path $output 'HyruleOnline.exe')" `
    "/win32manifest:$(Join-Path $source 'Launcher\app.manifest')" `
    "/resource:$(Join-Path $source 'Launcher\Launcher.xaml'),Launcher.xaml" `
    "/reference:$(Join-Path $framework 'WPF\PresentationFramework.dll')" `
    "/reference:$(Join-Path $framework 'WPF\PresentationCore.dll')" `
    "/reference:$(Join-Path $framework 'WPF\WindowsBase.dll')" /reference:System.Xaml.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll `
    (Join-Path $source 'Launcher\Launcher.cs') (Join-Path $source 'Launcher\Avatars.cs') (Join-Path $source 'Launcher\PadCursors.cs') (Join-Path $source 'Launcher\Layout.cs') (Join-Path $source 'Launcher\ShellLayout.cs') (Join-Path $source 'Launcher\ShellFit.cs') (Join-Path $source 'Launcher\CemuDisplay.cs') (Join-Path $source 'Launcher\ShaderCache.cs') (Join-Path $source 'Launcher\RotationMod.cs') (Join-Path $source 'Launcher\FpsMod.cs') (Join-Path $source 'Launcher\GraphicsMod.cs') (Join-Path $source 'Launcher\Hardware.cs') (Join-Path $source 'Launcher\Navigation.cs') (Join-Path $source 'Launcher\PadIcons.cs') (Join-Path $source 'Launcher\PlayerBadge.cs') (Join-Path $source 'Launcher\Characters.cs') (Join-Path $source 'Launcher\Lang.cs') (Join-Path $source 'Launcher\Setup.cs') (Join-Path $source 'Launcher\SetupWizard.cs') (Join-Path $source 'Launcher\Translator.cs') (Join-Path $source 'Launcher\ServerHost.cs') (Join-Path $source 'Launcher\ServerRules.cs') (Join-Path $source 'Launcher\PathGuard.cs') (Join-Path $source 'Launcher\Prefs.cs') (Join-Path $source 'Input\Router.cs') (Join-Path $source 'Input\Sdl.cs') (Join-Path $source 'Input\ControllerMotion.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
foreach ($name in 'SDL3.dll', 'gamecontrollerdb.txt', 'GameControllerDB-LICENSE.txt', 'SDL-LICENSE.txt') {
    Copy-Item -LiteralPath (Join-Path $input $name) -Destination $output -Force
}
Copy-Item -LiteralPath (Join-Path $source 'Launcher\Fonts') -Destination $output -Recurse -Force
Write-Output "Launcher ready: $output"
