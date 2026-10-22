param([string]$ZigPath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root '.tools'
$output = Join-Path $root '.artifacts\input'

function Get-VerifiedFile($Url, $Path, $Hash) {
    if ((Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path).Hash -eq $Hash) { return }
    Invoke-WebRequest $Url -OutFile $Path -UseBasicParsing -TimeoutSec 180
    if ((Get-FileHash -LiteralPath $Path).Hash -ne $Hash) { throw "Checksum mismatch: $Url" }
}

# Pinned SDL3 and controller database, the same versions as the local co-op launcher.
$sdl = Join-Path $tools 'input'
New-Item -ItemType Directory -Force -Path $sdl | Out-Null
$archive = Join-Path $sdl 'sdl.zip'
Get-VerifiedFile 'https://github.com/libsdl-org/SDL/releases/download/release-3.4.18/SDL3-3.4.18-win32-x64.zip' $archive '75c2c0fc74e7d1206aaedc22893a35887332f4f85f5c8b74fddc10d177985f2c'
& tar.exe -xf $archive -C $sdl
if ($LASTEXITCODE -ne 0) { throw 'SDL extraction failed.' }
$database = 'https://raw.githubusercontent.com/mdqinc/SDL_GameControllerDB/ff333823a85ae896e73f4c53670932cf0f728adb'
Get-VerifiedFile "$database/gamecontrollerdb.txt" (Join-Path $sdl 'gamecontrollerdb.txt') 'C6587CC65789539E61EC375AAD5BDBC14F974508B57E2AE87730AC75C4445DBA'
Get-VerifiedFile "$database/LICENSE" (Join-Path $sdl 'GameControllerDB-LICENSE.txt') '97F35B302B361680EC1E891E95D2D52097BB95ABFF361434916D99DC1305F127'

if (-not $ZigPath) {
    $ZigPath = Join-Path $tools 'zig\zig-windows-x86_64-0.13.0\zig.exe'
    if (-not (Test-Path -LiteralPath $ZigPath)) {
        $zigArchive = Join-Path $tools 'zig\download.zip'
        New-Item -ItemType Directory -Force -Path (Split-Path $zigArchive) | Out-Null
        Get-VerifiedFile 'https://ziglang.org/download/0.13.0/zig-windows-x86_64-0.13.0.zip' $zigArchive 'D859994725EF9402381E557C60BB57497215682E355204D754EE3DF75EE3C158'
        & tar.exe -xf $zigArchive -C (Split-Path $zigArchive)
        if ($LASTEXITCODE -ne 0) { throw 'Zig extraction failed.' }
    }
}
if (-not (Test-Path -LiteralPath $ZigPath)) { throw "Zig not found: $ZigPath" }

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { throw '.NET Framework 4.8 developer tools are required.' }
if (Get-Process -Name InputRouter -ErrorAction SilentlyContinue) { throw 'Close InputRouter before rebuilding.' }

New-Item -ItemType Directory -Force -Path (Join-Path $output 'Bridge') | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ "/out:$(Join-Path $output 'InputRouter.exe')" (Join-Path $root 'src\InputRouter\InputRouter.cs')
if ($LASTEXITCODE -ne 0) { throw 'Input router compilation failed.' }
& $ZigPath cc -target x86_64-windows-gnu -O2 -shared -Wno-dll-attribute-on-redeclaration (Join-Path $root 'src\InputBridge\xinput_bridge.c') -o (Join-Path $output 'Bridge\xinput1_4.dll')
if ($LASTEXITCODE -ne 0) { throw 'Input bridge compilation failed.' }
foreach ($name in 'SDL3.dll', 'gamecontrollerdb.txt', 'GameControllerDB-LICENSE.txt') {
    Copy-Item -LiteralPath (Join-Path $sdl $name) -Destination $output -Force
}
Copy-Item -LiteralPath (Join-Path $sdl 'LICENSE.txt') -Destination (Join-Path $output 'SDL-LICENSE.txt') -Force
Write-Output "Input router ready: $output"
