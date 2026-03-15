# Check when the service exe was last built/deployed
Write-Host "=== Service exe timestamps ===" -ForegroundColor Cyan
Get-Item "C:\Program Files\ParentalControl\ParentalControl.Service.exe" | Select-Object Name, Length, LastWriteTime
Get-Item "C:\Program Files\ParentalControl\ParentalControl.Core.dll" | Select-Object Name, Length, LastWriteTime

# Check what's in the publish\service folder (what would be deployed)
Write-Host "`n=== publish\service folder ===" -ForegroundColor Cyan
Get-Item "C:\Users\humph\ParentalControl\publish\service\ParentalControl.Service.exe" -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime

# Check if ExtensionFileServer.cs was compiled into the publish
Write-Host "`n=== ExtensionFileServer in source ===" -ForegroundColor Cyan
Get-Item "C:\Users\humph\ParentalControl\ParentalControl.Service\Services\ExtensionFileServer.cs" | Select-Object Name, Length, LastWriteTime

# Check service event log for any startup errors
Write-Host "`n=== Service event log (last 10 entries) ===" -ForegroundColor Cyan
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='ParentalControl'} -MaxEvents 10 -ErrorAction SilentlyContinue |
    Select-Object TimeCreated, LevelDisplayName, Message | Format-List

# Quick test: can SYSTEM bind HttpListener on localhost:47252?
Write-Host "`n=== Test HttpListener on localhost:47252 ===" -ForegroundColor Cyan
try {
    $hl = New-Object System.Net.HttpListener
    $hl.Prefixes.Add("http://localhost:47252/test/")
    $hl.Start()
    Write-Host "HttpListener started OK on localhost:47252" -ForegroundColor Green
    $hl.Stop()
    $hl.Close()
} catch {
    Write-Host "HttpListener FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
