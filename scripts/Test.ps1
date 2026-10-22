$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root '.artifacts\tests'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$(Join-Path $output 'RouterTests.exe')" `
    (Join-Path $root 'tests\RouterTests.cs') (Join-Path $root 'src\Input\Router.cs') (Join-Path $root 'src\Input\Sdl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $output 'RouterTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Router tests failed.' }
