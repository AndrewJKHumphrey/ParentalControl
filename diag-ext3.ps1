# Compute the ACTUAL extension ID from the manifest.json key and compare to the hardcoded one
Write-Host "=== Extension ID verification ===" -ForegroundColor Cyan
$manifestPath = "C:\Program Files\ParentalControl\extension\manifest.json"
$manifest = Get-Content $manifestPath | ConvertFrom-Json
$keyB64 = $manifest.key
$keyBytes = [Convert]::FromBase64String($keyB64)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha256.ComputeHash($keyBytes)
$computedId = -join ($hash[0..15] | ForEach-Object {
    $high = ($_ -shr 4) + [int][char]'a'
    $low  = ($_ -band 0x0f) + [int][char]'a'
    [char]$high
    [char]$low
})
Write-Host "Computed extension ID: $computedId"
Write-Host "Hardcoded in policy:   lackpoggaaeodfcagkfcglokeilcfokg"
if ($computedId -eq "lackpoggaaeodfcagkfcglokeilcfokg") {
    Write-Host "IDs MATCH" -ForegroundColor Green
} else {
    Write-Host "IDs DO NOT MATCH -- this is the bug!" -ForegroundColor Red
    Write-Host "Policy must be updated to use: $computedId" -ForegroundColor Yellow
}

# Check if Edge can reach the file URL (simulate what Edge does)
Write-Host "`n=== File URL accessibility ===" -ForegroundColor Cyan
$updateXmlPath = "C:\Program Files\ParentalControl\extension\update.xml"
$crxPath = "C:\Program Files\ParentalControl\extension\parentguard.crx"
Write-Host "update.xml exists: $(Test-Path $updateXmlPath)"
Write-Host "parentguard.crx exists: $(Test-Path $crxPath)"
try {
    $content = (New-Object System.Net.WebClient).DownloadString("file:///$($updateXmlPath.Replace('\','/').Replace(' ','%20'))")
    Write-Host "update.xml readable via file:// URL: YES" -ForegroundColor Green
} catch {
    Write-Host "update.xml readable via file:// URL: NO -- $($_.Exception.Message)" -ForegroundColor Red
}

# Show current policy registry state
Write-Host "`n=== Registry policy ===" -ForegroundColor Cyan
reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist" 2>&1
reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v "ExtensionSettings" 2>&1

Write-Host "`n=== NEXT STEPS ===" -ForegroundColor Yellow
Write-Host "1. Open Edge and go to: edge://policy/"
Write-Host "   Look for 'ExtensionInstallForcelist' -- is it listed there?"
Write-Host "   If NOT listed, Edge is not reading HKLM policies (run 'gpupdate /force' and retry)"
Write-Host ""
Write-Host "2. Open Edge and go to: edge://extensions/"
Write-Host "   Look for any ParentGuard entry (even if not installed) -- check for error messages"
Write-Host ""
Write-Host "3. If policy IS listed in edge://policy/ but extension not installing,"
Write-Host "   the file:// URL is likely being blocked by Edge's update fetcher."
