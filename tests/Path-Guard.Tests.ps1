$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Path-Guard.ps1')
$failures = 0

function Check {
    param([string]$Name, $Expected, $Actual)
    if ("$Expected" -ne "$Actual") { Write-Host "FAIL: $Name (expected '$Expected', got '$Actual')"; $script:failures++ }
    else { Write-Host "ok:   $Name" }
}

function Issues {
    param([string]$Root, [string[]]$Protected = @(), [string[]]$Synced = @(), [switch]$Network)
    (@(Get-RootPathIssues -Root $Root -ProtectedFolders $Protected -SyncedFolders $Synced -NetworkDrive:$Network) | ForEach-Object { $_.Issue }) -join ','
}

$atLimit = 'C:\' + ('a' * ($MaxRootLength - 3))
Check 'the suggested folder is fine' '' (Issues 'C:\HyruleOnline')
Check 'a folder at the limit is fine, with or without a closing backslash' '' ((Issues $atLimit) + (Issues "$atLimit\"))
Check 'one character over the limit is too long' 'TooLong' (Issues "${atLimit}a")
Check 'UNC paths are network folders' 'Network' (Issues '\\server\share\Hyrule')
Check 'a network drive is a network folder' 'Network' (Issues 'Z:\Hyrule' -Network)
Check 'Program Files is found in any case' 'ProgramFiles' (Issues 'c:\program files (x86)\Hyrule' -Protected 'C:\Program Files', 'C:\Program Files (x86)\')
Check 'a folder that only starts with the same letters is not inside' '' (Issues 'C:\Program Files Tools\Hyrule' -Protected 'C:\Program Files')
Check 'OneDrive is found by name' 'OneDrive' (Issues 'C:\Users\Ann\OneDrive - Contoso\Hyrule')
Check 'OneDrive is found by the synced folders' 'OneDrive' (Issues 'D:\Cloud\Hyrule' -Synced 'D:\Cloud')
Check 'a folder next to the synced one is fine' '' (Issues 'C:\Users\Ann\Documents\Hyrule' -Synced 'C:\Users\Ann\OneDrive')
Check 'non-ASCII characters are reported' 'NonAscii' (Issues ('C:\Usuarios\C' + [char]0xE1 + 'mara\Hyrule'))
Check 'several problems come in a fixed order' 'TooLong,Network,NonAscii' (Issues ('\\server\C' + [char]0xE1 + 'mara\' + ('a' * 90)))
Check 'only too long and network block' 'True,True,False,False,False' ((@(Get-RootPathIssues -Root ('\\server\C' + [char]0xE1 + 'mara\OneDrive\' + ('a' * 90)) -ProtectedFolders '\\server' -SyncedFolders @()) | ForEach-Object { $_.Blocking }) -join ',')

$threw = $null
try { Assert-RootPath -Root ('C:\' + ('a' * 100)) } catch { $threw = $_.Exception.Message }
Check 'a path that is too long is refused with the suggested folder' $true ($threw -like '*cannot run from*limit is 83*C:\HyruleOnline*')
$threw = $null
try { Assert-RootPath -Root '\\server\share\Hyrule' } catch { $threw = $_.Exception.Message }
Check 'a network folder is refused' $true ($threw -like '*network location*')
$threw = $null
$output = $null
try { $output = Assert-RootPath -Root 'C:\Users\Ann\OneDrive\Hyrule' } catch { $threw = $_.Exception.Message }
Check 'a OneDrive folder only warns' $true (-not $threw -and "$output" -like 'Warning:*OneDrive*C:\HyruleOnline*')
$threw = $null
$output = $null
try { $output = Assert-RootPath -Root 'C:\HyruleOnline' } catch { $threw = $_.Exception.Message }
Check 'a good folder passes silently' $true (-not $threw -and -not $output)

if ($failures -gt 0) { throw "$failures path guard check(s) failed." }
