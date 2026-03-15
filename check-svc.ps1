sc.exe qfailure ParentalControlService
Write-Host "---"
sc.exe qc ParentalControlService
Write-Host "---"

# Check for recent service failure events
$events = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Service Control Manager'} -MaxEvents 200 -ErrorAction SilentlyContinue
$events | Where-Object { $_.Message -match 'ParentalControl' } | Select-Object TimeCreated, Id, Message | Format-List

# Try starting the service and watching what happens
Write-Host "=== Attempting manual service start ===" -ForegroundColor Cyan
try {
    $svc = Get-Service ParentalControlService
    if ($svc.Status -ne 'Running') {
        Start-Service ParentalControlService -ErrorAction Stop
        Write-Host "Service start command sent. Waiting 5 seconds..." -ForegroundColor Yellow
        Start-Sleep 5
        $svc.Refresh()
        Write-Host "Status after 5s: $($svc.Status)" -ForegroundColor Cyan
        Start-Sleep 5
        $svc.Refresh()
        Write-Host "Status after 10s: $($svc.Status)" -ForegroundColor Cyan
    } else {
        Write-Host "Service already running" -ForegroundColor Green
    }
} catch {
    Write-Host "Error starting service: $_" -ForegroundColor Red
}

# Check event log again for new entries
Write-Host "=== Events after start attempt ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Service Control Manager'} -MaxEvents 20 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'ParentalControl' } |
    Select-Object TimeCreated, Id, Message | Format-List
