Stop-Service ParentalControlService -ErrorAction SilentlyContinue
Start-Sleep 1

New-Item -ItemType Directory -Path "C:\Temp" -Force | Out-Null
$proc = Start-Process -FilePath "C:\Program Files\ParentalControl\ParentalControl.Service.exe" `
    -PassThru -RedirectStandardOutput "C:\Temp\svc_out.txt" -RedirectStandardError "C:\Temp\svc_err.txt" -NoNewWindow
Start-Sleep 5
$proc.Refresh()
Write-Host "Process HasExited: $($proc.HasExited)  ExitCode: $(if ($proc.HasExited) { $proc.ExitCode } else { 'still running' })"
if (-not $proc.HasExited) { $proc.Kill() }

Write-Host "`n=== STDOUT ===" -ForegroundColor Cyan
if (Test-Path "C:\Temp\svc_out.txt") { Get-Content "C:\Temp\svc_out.txt" } else { Write-Host "(none)" }

Write-Host "`n=== STDERR ===" -ForegroundColor Red
if (Test-Path "C:\Temp\svc_err.txt") { Get-Content "C:\Temp\svc_err.txt" } else { Write-Host "(none)" }

Write-Host "`n=== App Event Log (ParentalControl source) ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='ParentalControl'} -MaxEvents 20 -ErrorAction SilentlyContinue |
    Select-Object TimeCreated, LevelDisplayName, Message | Format-List

Write-Host "`n=== All App Errors last 5 min ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='Application'; Level=2; StartTime=(Get-Date).AddMinutes(-5)} -MaxEvents 30 -ErrorAction SilentlyContinue |
    Select-Object TimeCreated, ProviderName, Message | Format-List
