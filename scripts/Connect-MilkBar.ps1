param(
    [Parameter(Mandatory=$true)][string]$LauncherDirectory,
    # Leave the Milk Bar window where it opens (for debugging).
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('MilkBarWindow' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MilkBarWindow {
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    // A tool window far outside the desktop: not in the taskbar or Alt+Tab, and UI Automation still reaches its buttons.
    public static void Away(IntPtr h) {
        int style = GetWindowLong(h, -20);
        SetWindowLong(h, -20, (style | 0x80) & ~0x40000);
        SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0020);
    }
}
'@
}

$exe = Join-Path $LauncherDirectory 'Milk Bar Launcher.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Milk Bar Launcher.exe not found in $LauncherDirectory" }

$process = Start-Process -FilePath $exe -WorkingDirectory $LauncherDirectory -PassThru
$handle = [IntPtr]::Zero
$button = $null
$deadline = (Get-Date).AddSeconds(40)
while (-not $button -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()
    if ($process.HasExited) { throw 'The Milk Bar launcher closed before it could connect.' }
    if ($handle -eq [IntPtr]::Zero -and $process.MainWindowHandle -ne 0) {
        $handle = $process.MainWindowHandle
        if (-not $Show) { [MilkBarWindow]::Away($handle) }
    }
    if ($handle -ne [IntPtr]::Zero) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Connect')
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
}
if (-not $button) { throw 'The Connect button was not found. Close the launcher and try again.' }

Start-Sleep -Seconds 2
if (-not $Show) { [MilkBarWindow]::Away($handle) }
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Launcher $($process.Id) connected at $(Get-Date -Format HH:mm:ss)"
