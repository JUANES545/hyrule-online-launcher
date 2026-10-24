$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root '.artifacts\tests'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'LayoutTests.exe')" "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" "/reference:$(Join-Path $wpf 'PresentationCore.dll')" "/reference:$(Join-Path $wpf 'WindowsBase.dll')" /reference:System.Xaml.dll `
    (Join-Path $root 'tests\LayoutTests.cs') (Join-Path $root 'src\Launcher\ShellLayout.cs') (Join-Path $root 'src\Launcher\ShellFit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Layout test compilation failed.' }
& (Join-Path $output 'LayoutTests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Layout tests failed.' }
