# Installs what the launcher needs from the official upstream sources: Cemu, the Milk Bar Launcher, the Cemu graphic packs,
# Python 3.8.10 and BCML. Everything goes into this folder (.tools and .artifacts), nothing is installed for the whole machine,
# no administrator rights are needed and PATH is left alone. Each download is checked against the SHA-256 pinned in
# components.json before it is used. It can be run again at any time: finished components are skipped and an interrupted
# download continues where it stopped.
#
# The game, update, DLC, saves and shader caches belong to the user and are never downloaded here.
#
# -Check (alias -WhatIf) changes nothing and only reports. Output lines for the launcher, one per line:
#   SETUP|STATUS|<id>|OK/MISSING/BROKEN|<detail>      what is there, one line per component
#   SETUP|PROGRESS|all|<0-100>|<text>                  overall progress while installing
#   SETUP|STEP|<id>|start/done/failed|<text>           one component
#   SETUP|DONE|all|OK/PENDING/FAILED|<text>            always the last line; PENDING means -Check found work to do
# Exit code 0 when everything is fine, 1 when something is pending (-Check) or failed.
[CmdletBinding()]
param(
    [Alias('WhatIf')][switch]$Check,
    [string]$Root,
    [string]$CacheDir,
    [string]$ManifestPath,
    # Limit the run to these component ids.
    [string[]]$Only,
    # Only define the functions (used by the tests).
    [switch]$LibraryOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$package = Split-Path -Parent $PSScriptRoot
if (-not $Root) { $Root = $package }
$Root = [IO.Path]::GetFullPath($Root)
if (-not $ManifestPath) { $ManifestPath = Join-Path $PSScriptRoot 'components.json' }
if (-not $CacheDir) { $CacheDir = Join-Path $Root '.artifacts\downloads' }
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

function Write-Setup([string]$Kind, [string]$Id, [string]$State, [string]$Text) {
    [Console]::Out.WriteLine("SETUP|$Kind|$Id|$State|$Text")
    [Console]::Out.Flush()
}

function Resolve-Inside([string]$Base, [string]$Relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $Base ($Relative -replace '/', '\')))
    $prefix = $Base.TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path leaves the install folder: $Relative" }
    return $full
}

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}

# Downloads $Url to $Path and keeps it only when its SHA-256 is $Hash. A file that is already there and good is reused; a bad one
# is deleted. An interrupted download is kept as <Path>.part and continued next time. $Progress gets the finished fraction (0 to 1).
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Hash, [scriptblock]$Progress) {
    if (Test-Path -LiteralPath $Path) {
        if ((Get-Sha256 $Path) -eq $Hash) { return }
        Remove-Item -LiteralPath $Path -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $part = "$Path.part"
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $have = 0
        if (Test-Path -LiteralPath $part) { $have = (Get-Item -LiteralPath $part).Length }
        $request = [Net.WebRequest]::Create($Url)
        $request.Timeout = 60000
        if ($request -is [Net.HttpWebRequest]) {
            $request.UserAgent = 'HyruleOnline-Setup'
            $request.ReadWriteTimeout = 60000
            if ($have -gt 0) { $request.AddRange([long]$have) }
        }
        try { $response = $request.GetResponse() }
        catch [Net.WebException] {
            # 416: the kept part is not a prefix of the file (or is complete already). Start again without it.
            if ($have -gt 0 -and $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 416) { Remove-Item -LiteralPath $part -Force; continue }
            throw
        }
        try {
            $resumed = ($response -is [Net.HttpWebResponse]) -and ([int]$response.StatusCode -eq 206)
            if (-not $resumed) { $have = 0 }
            $total = $response.ContentLength
            if ($total -gt 0) { $total += $have }
            $mode = if ($resumed) { [IO.FileMode]::Append } else { [IO.FileMode]::Create }
            $source = $response.GetResponseStream()
            $target = New-Object IO.FileStream($part, $mode, [IO.FileAccess]::Write)
            try {
                $buffer = New-Object byte[] 262144
                $done = [long]$have
                $stamp = [Environment]::TickCount
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $target.Write($buffer, 0, $read)
                    $done += $read
                    if ($Progress -and $total -gt 0 -and ([Environment]::TickCount - $stamp) -gt 700) { $stamp = [Environment]::TickCount; & $Progress ([Math]::Min(1.0, $done / $total)) }
                }
            }
            finally { $target.Dispose(); $source.Dispose() }
        }
        finally { $response.Close() }
        break
    }
    $actual = Get-Sha256 $part
    if ($actual -ne $Hash) {
        Remove-Item -LiteralPath $part -Force
        throw "Checksum mismatch for $Url (expected $Hash, got $actual). The download was deleted."
    }
    Move-Item -LiteralPath $part -Destination $Path -Force
    if ($Progress) { & $Progress 1.0 }
}

function Read-Manifest([string]$Path) {
    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1) { throw "Unsupported component manifest schema $($manifest.schema)." }
    return $manifest
}

# Same rules as Preflight.CheckMarker in src\Launcher\Setup.cs.
function Test-Marker([string]$File, $Marker) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) { return @('MISSING', "$($Marker.file) is missing") }
    if ($Marker.fileVersion) {
        $version = (Get-Item -LiteralPath $File).VersionInfo.ProductVersion
        if (-not ("$version".StartsWith($Marker.fileVersion))) { return @('BROKEN', "$($Marker.file) is version $version, $($Marker.fileVersion) is needed") }
    }
    if ($Marker.size -and (Get-Item -LiteralPath $File).Length -ne [long]$Marker.size) { return @('BROKEN', "$($Marker.file) is not the official file") }
    if ($Marker.sha256 -and (Get-Sha256 $File) -ne $Marker.sha256) { return @('BROKEN', "$($Marker.file) is not the official file") }
    return @('OK', '')
}

function Get-ComponentStatus([string]$Base, $Component) {
    $status = 'OK'
    $detail = $Component.targets[0]
    for ($i = 0; $i -lt $Component.targets.Count; $i++) {
        $target = Resolve-Inside $Base $Component.targets[$i]
        foreach ($marker in $Component.markers) {
            if ($marker.firstTargetOnly -and $i -gt 0) { continue }
            $result = Test-Marker (Join-Path $target ($marker.file -replace '/', '\')) $marker
            if ($result[0] -eq 'OK') { continue }
            if ($status -eq 'OK' -or ($result[0] -eq 'BROKEN' -and $status -eq 'MISSING')) { $status = $result[0]; $detail = $result[1] }
        }
    }
    return [pscustomobject]@{ Status = $status; Detail = $detail }
}

# Extracts below $Destination, refusing entries that would land outside it.
function Expand-Zip([string]$Zip, [string]$Destination, [string]$StripRoot) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name.EndsWith('/')) { continue }
            if ($StripRoot) {
                if (-not $name.StartsWith("$StripRoot/")) { continue }
                $name = $name.Substring($StripRoot.Length + 1)
            }
            $path = Resolve-Inside $Destination $name
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
        }
    }
    finally { $archive.Dispose() }
}

# Copies the staged files over the target, the marker files last, so an interrupted copy never looks finished.
function Copy-Staged([string]$Stage, [string]$Target, $Markers) {
    $last = @{}
    foreach ($marker in $Markers) { $last[($marker.file -replace '/', '\').ToLowerInvariant()] = $true }
    $files = Get-ChildItem -LiteralPath $Stage -Recurse -File
    $ordered = @($files | Where-Object { -not $last.ContainsKey($_.FullName.Substring($Stage.Length + 1).ToLowerInvariant()) }) + @($files | Where-Object { $last.ContainsKey($_.FullName.Substring($Stage.Length + 1).ToLowerInvariant()) })
    foreach ($file in $ordered) {
        $destination = Join-Path $Target $file.FullName.Substring($Stage.Length + 1)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}

function Install-Component($Component, [scriptblock]$Progress) {
    $archive = $null
    if ($Component.url) {
        $archive = Join-Path $CacheDir (Split-Path -Leaf ([Uri]$Component.url).AbsolutePath)
        $overall = $Progress
        Get-VerifiedFile $Component.url $archive $Component.sha256 ({ param($fraction) & $overall ($fraction * 0.7) }.GetNewClosure())
    }
    switch ($Component.kind) {
        'zip' {
            $stage = Join-Path $CacheDir ("stage-" + $Component.id)
            Expand-Zip $archive $stage $Component.stripRoot
            try {
                foreach ($relative in $Component.targets) { Copy-Staged $stage (Resolve-Inside $Root $relative) $Component.markers }
                if ($Component.versionFile) {
                    foreach ($relative in $Component.targets) { [IO.File]::WriteAllText((Join-Path (Resolve-Inside $Root $relative) $Component.versionFile), $Component.versionText, (New-Object Text.UTF8Encoding($false))) }
                }
            }
            finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
        }
        'python-installer' {
            # Per-user install into the project folder: no administrator rights, no PATH change, no file associations or shortcuts.
            $target = Resolve-Inside $Root $Component.targets[0]
            $arguments = "/quiet InstallAllUsers=0 TargetDir=`"$target`" PrependPath=0 Include_launcher=0 InstallLauncherAllUsers=0 Include_test=0 Include_doc=0 Shortcuts=0 AssociateFiles=0 Include_pip=1"
            $process = Start-Process -FilePath $archive -ArgumentList $arguments -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw "The Python installer failed with exit code $($process.ExitCode)." }
        }
        'pip' {
            $python = Join-Path (Resolve-Inside $Root $Component.targets[0]) 'python.exe'
            if (-not (Test-Path -LiteralPath $python)) { throw 'Python must be installed before BCML.' }
            $requirements = Resolve-Inside $package $Component.requirements
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & $python -m pip install --no-deps --require-hashes --only-binary=:all: --disable-pip-version-check --no-warn-script-location -r $requirements 2>&1 | ForEach-Object { [Console]::Out.WriteLine("$_") }
            $code = $LASTEXITCODE
            $ErrorActionPreference = $previous
            if ($code -ne 0) { throw "pip failed with exit code $code." }
            if ($Component.sdistRequirements) {
                $sdist = Resolve-Inside $package $Component.sdistRequirements
                $ErrorActionPreference = 'Continue'
                & $python -m pip install --no-deps --require-hashes --no-build-isolation --disable-pip-version-check --no-warn-script-location -r $sdist 2>&1 | ForEach-Object { [Console]::Out.WriteLine("$_") }
                $code = $LASTEXITCODE
                $ErrorActionPreference = $previous
                if ($code -ne 0) { throw "pip failed with exit code $code." }
            }
        }
        default { throw "Unknown component kind $($Component.kind)." }
    }
    & $Progress 1.0
}

if ($LibraryOnly) { return }

$manifest = Read-Manifest $ManifestPath
$components = @($manifest.components | Where-Object { -not $Only -or $Only -contains $_.id })
$statuses = @{}
foreach ($component in $components) { $statuses[$component.id] = Get-ComponentStatus $Root $component }
$pending = @($components | Where-Object { $statuses[$_.id].Status -ne 'OK' })

if ($Check) {
    foreach ($component in $components) { Write-Setup 'STATUS' $component.id $statuses[$component.id].Status $statuses[$component.id].Detail }
    if ($pending.Count -eq 0) { Write-Setup 'DONE' 'all' 'OK' 'Everything is installed.'; exit 0 }
    Write-Setup 'DONE' 'all' 'PENDING' "$($pending.Count) of $($components.Count) components need to be installed."
    exit 1
}

try {
    $busy = Get-Process -Name Cemu, 'Milk Bar Launcher' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase) }
    if ($busy) { throw 'Close the Cemu and Milk Bar windows of this install first (scripts\Stop-Online.ps1).' }
    $weights = @{}
    foreach ($component in $pending) { $weights[$component.id] = if ($component.size) { [double]$component.size * 2 } else { 200MB } }
    $total = ($weights.Values | Measure-Object -Sum).Sum
    $finished = 0.0
    foreach ($component in $pending) {
        $label = "$($component.name) $($component.version)"
        Write-Setup 'STEP' $component.id 'start' $label
        $share = $weights[$component.id]
        $base = $finished
        $report = { param($fraction) $percent = [int](($base + $share * $fraction) / $total * 100); Write-Setup 'PROGRESS' 'all' "$([Math]::Min(99, $percent))" $label }.GetNewClosure()
        Install-Component $component $report
        $now = Get-ComponentStatus $Root $component
        if ($now.Status -ne 'OK') { throw "$label was installed but is still not right: $($now.Detail)" }
        $finished += $share
        Write-Setup 'STEP' $component.id 'done' $label
    }
    foreach ($component in $components) { $now = Get-ComponentStatus $Root $component; Write-Setup 'STATUS' $component.id $now.Status $now.Detail }
    Write-Setup 'PROGRESS' 'all' '100' 'Finished'
    Write-Setup 'DONE' 'all' 'OK' 'Everything is installed.'
}
catch {
    Write-Setup 'STEP' 'all' 'failed' $_.Exception.Message
    Write-Setup 'DONE' 'all' 'FAILED' $_.Exception.Message
    exit 1
}
