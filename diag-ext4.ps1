# 1. Service status
Write-Host "=== Service status ===" -ForegroundColor Cyan
Get-Service ParentalControlService, ParentalControlWatchdog -ErrorAction SilentlyContinue | Select-Object Name, Status

# 2. Is port 47252 listening?
Write-Host "`n=== Port 47252 listener ===" -ForegroundColor Cyan
$tcp = netstat -ano | Select-String ":47252"
if ($tcp) { $tcp } else { Write-Host "NOT LISTENING" -ForegroundColor Red }

# 3. Can we fetch update.xml over HTTP?
Write-Host "`n=== HTTP fetch update.xml ===" -ForegroundColor Cyan
try {
    $resp = Invoke-WebRequest -Uri "http://localhost:47252/extension/update.xml" -TimeoutSec 5 -UseBasicParsing
    Write-Host "HTTP 200 OK -- content:" -ForegroundColor Green
    Write-Host $resp.Content
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Can we fetch the CRX?
Write-Host "`n=== HTTP fetch parentguard.crx ===" -ForegroundColor Cyan
try {
    $resp = Invoke-WebRequest -Uri "http://localhost:47252/extension/parentguard.crx" -TimeoutSec 5 -UseBasicParsing
    Write-Host "HTTP 200 OK -- $($resp.RawContentLength) bytes" -ForegroundColor Green
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

# 5. Current registry policy
Write-Host "`n=== Edge policy registry ===" -ForegroundColor Cyan
reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist" 2>&1

# 6. Installed update.xml (check it was updated to localhost URL)
Write-Host "`n=== Installed update.xml ===" -ForegroundColor Cyan
Get-Content "C:\Program Files\ParentalControl\extension\update.xml" -ErrorAction SilentlyContinue
