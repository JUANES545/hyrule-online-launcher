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
    "/resource:$(Join-Path $source 'Launcher\Launcher.xaml'),Launcher.xaml" `
    "/reference:$(Join-Path $framework 'WPF\PresentationFramework.dll')" `
    "/reference:$(Join-Path $framework 'WPF\PresentationCore.dll')" `
    "/reference:$(Join-Path $framework 'WPF\WindowsBase.dll')" /reference:System.Xaml.dll `
    (Join-Path $source 'Launcher\Launcher.cs') (Join-Path $source 'Input\Router.cs') (Join-Path $source 'Input\Sdl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
foreach ($name in 'SDL3.dll', 'gamecontrollerdb.txt', 'GameControllerDB-LICENSE.txt', 'SDL-LICENSE.txt') {
    Copy-Item -LiteralPath (Join-Path $input $name) -Destination $output -Force
}
Write-Output "Launcher ready: $output"
