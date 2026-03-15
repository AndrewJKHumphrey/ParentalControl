$ExtDir = "C:\Program Files\ParentalControl\extension"

Write-Host "=== CRX file ===" -ForegroundColor Cyan
$crx = Get-Item "$ExtDir\parentguard.crx" -ErrorAction SilentlyContinue
if ($crx) { Write-Host "  Size: $($crx.Length) bytes    Modified: $($crx.LastWriteTime)" }
else       { Write-Host "  MISSING" -ForegroundColor Red }

Write-Host "`n=== manifest.json ===" -ForegroundColor Cyan
$m = Get-Content "$ExtDir\manifest.json" -Raw | ConvertFrom-Json
Write-Host "  name:    $($m.name)"
Write-Host "  version: $($m.version)"
Write-Host "  key:     $(if ($m.key) { $m.key.Substring(0,40) + '...' } else { 'MISSING' })"

Write-Host "`n=== extension.pem (repo) ===" -ForegroundColor Cyan
$pem = Get-Item "C:\Users\humph\ParentalControl\extension.pem" -ErrorAction SilentlyContinue
if ($pem) { Write-Host "  Exists: $($pem.FullName)    Size: $($pem.Length)" }
else       { Write-Host "  MISSING" -ForegroundColor Red }

Write-Host "`n=== Edge policy ===" -ForegroundColor Cyan
$pol = Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist" -ErrorAction SilentlyContinue
if ($pol) { $pol | Format-List "1" }
else      { Write-Host "  Key not found" -ForegroundColor Red }

Write-Host "`n=== Edge processes still running? ===" -ForegroundColor Cyan
$edgeProcs = Get-Process msedge -ErrorAction SilentlyContinue
if ($edgeProcs) {
    Write-Host "  $($edgeProcs.Count) Edge process(es) still alive!" -ForegroundColor Yellow
    Write-Host "  Edge must be FULLY killed (all background processes) to pick up policy."
} else {
    Write-Host "  No Edge processes running." -ForegroundColor Green
}

Write-Host "`n=== update.xml codebase ===" -ForegroundColor Cyan
[xml]$xml = Get-Content "$ExtDir\update.xml"
Write-Host "  appid:    $($xml.gupdate.app.appid)"
Write-Host "  codebase: $($xml.gupdate.app.updatecheck.codebase)"

Write-Host "`n=== Files in extension dir ===" -ForegroundColor Cyan
Get-ChildItem $ExtDir | Select-Object Name, Length | Format-Table -AutoSize
