
# Check event IDs 7031, 7034, 7036 for service start/stop/unexpected stop
Write-Host "=== Service start/stop events (7031/7034/7036) ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='System'; Id=7031,7034,7036} -MaxEvents 50 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'ParentalControl' } |
    Select-Object TimeCreated, Id, Message | Format-List

# Check the DB state
Write-Host "`n=== DB settings (via sqlite if available) ===" -ForegroundColor Cyan
$dbPath = "C:\ProgramData\ParentalControl\data.db"
if (Test-Path $dbPath) {
    Write-Host "DB exists, size: $((Get-Item $dbPath).Length) bytes"
} else {
    Write-Host "DB does not exist!" -ForegroundColor Red
}

# Check ProcessProtection behavior by looking at current process protections
Write-Host "`n=== Service exe exists ===" -ForegroundColor Cyan
Get-Item "C:\Program Files\ParentalControl\ParentalControl.Service.exe" | Select-Object Name, Length, LastWriteTime

# Start service and tail event log in real time
Write-Host "`n=== Starting service and monitoring for 15 seconds ===" -ForegroundColor Cyan
Start-Service ParentalControlService -ErrorAction SilentlyContinue

$end = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $end) {
    $s = (Get-Service ParentalControlService).Status
    $w = (Get-Service ParentalControlWatchdog).Status
    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] Main=$s  Watchdog=$w"
    Start-Sleep 1
}

Write-Host "`n=== Final event log after test ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='System'; Id=7031,7034,7036; StartTime=(Get-Date).AddMinutes(-5)} -MaxEvents 20 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'ParentalControl' } |
    Select-Object TimeCreated, Id, Message | Format-List
