param([Parameter(Mandatory=$true)][string]$LauncherDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$exe = Join-Path $LauncherDirectory 'Milk Bar Launcher.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Milk Bar Launcher.exe not found in $LauncherDirectory" }

$process = Start-Process -FilePath $exe -WorkingDirectory $LauncherDirectory -PassThru
$button = $null
for ($i = 0; $i -lt 40 -and -not $button; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.MainWindowHandle -ne 0) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Connect')
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
}
if (-not $button) { throw 'The Connect button was not found. Close the launcher and try again.' }

Start-Sleep -Seconds 2
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Launcher $($process.Id) connected at $(Get-Date -Format HH:mm:ss)"
