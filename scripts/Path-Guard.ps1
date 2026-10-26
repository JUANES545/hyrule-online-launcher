# Checks the folder the project runs from. Dot-sourced by Start-Online.ps1 and by tests\Path-Guard.Tests.ps1.
# The same rules and limit live in src\Launcher\PathGuard.cs and installer\HyruleOnline.iss; a test keeps the three limits equal.
# BCML, Cemu and the Milk Bar launcher break on paths over MAX_PATH: 260 - 1 (the NUL) - 166 (the deepest known file under the root) - 10 (headroom).
$MaxRootLength = 83
$SuggestedRoot = 'C:\HyruleOnline'

# Returns objects with Issue (TooLong, Network, ProgramFiles, OneDrive, NonAscii) and Blocking. Only TooLong and Network block.
function Get-RootPathIssues {
    param(
        [string]$Root,
        [string[]]$ProtectedFolders = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432),
        [string[]]$SyncedFolders = @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial),
        [switch]$NetworkDrive
    )
    $path = $Root.TrimEnd('\', '/')
    $inside = {
        param([string[]]$Folders)
        foreach ($folder in $Folders) {
            if (-not $folder) { continue }
            $folder = $folder.TrimEnd('\')
            if ($path -eq $folder -or $path.StartsWith("$folder\", [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        return $false
    }
    $found = @()
    if ($path.Length -gt $MaxRootLength) { $found += 'TooLong' }
    if ($NetworkDrive -or $path.StartsWith('\\')) { $found += 'Network' }
    if (& $inside $ProtectedFolders) { $found += 'ProgramFiles' }
    if ((& $inside $SyncedFolders) -or ($path.Split('\') | Where-Object { $_ -like 'OneDrive*' })) { $found += 'OneDrive' }
    if ($path -match '[^\x00-\x7F]') { $found += 'NonAscii' }
    foreach ($issue in $found) { [pscustomobject]@{ Issue = $issue; Blocking = $issue -in 'TooLong', 'Network' } }
}

function Get-RootPathMessage {
    param([string]$Root, $Issue)
    switch ($Issue) {
        'TooLong' { "The folder path is $($Root.TrimEnd('\').Length) characters long and the limit is $MaxRootLength. BCML, Cemu and the Milk Bar launcher fail on long paths." }
        'Network' { 'The folder is on a network location. The game and its tools need a local drive.' }
        'ProgramFiles' { 'The folder is under Program Files, which Windows protects from changes by regular programs.' }
        'OneDrive' { 'The folder is synced by OneDrive, which can lock or rewrite files while the game uses them.' }
        default { 'The folder path has characters that are not plain ASCII, which some of the tools may not handle.' }
    }
}

# Throws for a path that cannot work and prints a warning for one that probably causes trouble.
function Assert-RootPath {
    param([string]$Root)
    $networkDrive = $false
    if ($Root -match '^[A-Za-z]:') { try { $networkDrive = ([System.IO.DriveInfo]$Root.Substring(0, 1)).DriveType -eq 'Network' } catch { } }
    $issues = @(Get-RootPathIssues -Root $Root -NetworkDrive:$networkDrive)
    foreach ($issue in $issues) {
        $reason = Get-RootPathMessage -Root $Root -Issue $issue.Issue
        if ($issue.Blocking) { throw "Hyrule Online cannot run from $Root. $reason Move the whole folder to a short local path such as $SuggestedRoot." }
        Write-Output "Warning: $reason Moving the folder to $SuggestedRoot avoids problems."
    }
}
