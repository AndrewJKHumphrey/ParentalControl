# Check registry policy
Write-Host "=== Edge force-install policy ===" -ForegroundColor Cyan
Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist" -ErrorAction SilentlyContinue

# Check files exist
Write-Host "`n=== Extension install dir ===" -ForegroundColor Cyan
Get-ChildItem "C:\Program Files\ParentalControl\extension" -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime

# Show update.xml contents
Write-Host "`n=== update.xml ===" -ForegroundColor Cyan
Get-Content "C:\Program Files\ParentalControl\extension\update.xml" -ErrorAction SilentlyContinue

# Check manifest key
Write-Host "`n=== manifest.json key/name/version ===" -ForegroundColor Cyan
$m = Get-Content "C:\Program Files\ParentalControl\extension\manifest.json" -ErrorAction SilentlyContinue | ConvertFrom-Json
Write-Host "key:     $($m.key)"
Write-Host "name:    $($m.name)"
Write-Host "version: $($m.version)"

# Check if CRX exists and has content
Write-Host "`n=== CRX file ===" -ForegroundColor Cyan
$crx = "C:\Program Files\ParentalControl\extension\parentguard.crx"
if (Test-Path $crx) {
    $info = Get-Item $crx
    Write-Host "EXISTS: $($info.Length) bytes, modified $($info.LastWriteTime)"
    # Show first 4 bytes (CRX magic: Cr24)
    $bytes = [System.IO.File]::ReadAllBytes($crx)
    $magic = [System.Text.Encoding]::ASCII.GetString($bytes[0..3])
    Write-Host "Magic bytes: $magic (should be 'Cr24')"
} else {
    Write-Host "NOT FOUND" -ForegroundColor Red
}

# Check edge://policy can see it -- show policy file if group policy is file-based
Write-Host "`n=== Edge policy via registry (raw) ===" -ForegroundColor Cyan
reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist" 2>&1
