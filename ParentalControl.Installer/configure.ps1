<#
.SYNOPSIS
    Post-install configuration for ParentGuard.
    Run by the MSI installer via a deferred custom action after files are on disk.
#>
param(
    [Parameter(Mandatory)]
    [string] $InstallDir
)

# Ensure the trailing backslash is present
if (-not $InstallDir.EndsWith('\')) { $InstallDir += '\' }

# -- 1. Write native messaging manifest with the actual install path ----------
$manifestPath  = Join-Path $InstallDir "nativemessaging-manifest.json"
$nativeHostExe = Join-Path $InstallDir "ParentalControl.NativeHost.exe"

$manifest = [ordered]@{
    name            = "com.parentalcontrol.host"
    description     = "ParentalControl native messaging host"
    path            = $nativeHostExe
    type            = "stdio"
    allowed_origins = @("chrome-extension://lackpoggaaeodfcagkfcglokeilcfokg/")
}

$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Native messaging manifest written to: $manifestPath"

# -- 2. Rewrite update.xml with actual install path and set force-install policy --
$ExtDir    = Join-Path $InstallDir "extension"
$CrxPath   = Join-Path $ExtDir "parentguard.crx"
$UpdateXml = Join-Path $ExtDir "update.xml"
$ExtId     = "lackpoggaaeodfcagkfcglokeilcfokg"

if ((Test-Path $CrxPath) -and (Test-Path $UpdateXml)) {
    # Write update.xml using HTTP URL (file:// is unreliable in Edge enterprise policy)
    $xml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='$ExtId' status='ok'>
    <updatecheck status='ok' codebase='http://localhost:47252/extension/parentguard.crx' version='1.0.0' />
  </app>
</gupdate>
"@
    Set-Content -Path $UpdateXml -Value $xml -Encoding UTF8
    Write-Host "  update.xml rewritten (HTTP codebase URL)."

    $ExtEntry = "${ExtId};http://localhost:47252/extension/update.xml"

    # Chromium-based browsers: Chrome, Edge, Brave, Opera
    $chromiumPolicies = @(
        "HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
        "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist",
        "HKLM:\SOFTWARE\Policies\BraveSoftware\Brave-Browser\ExtensionInstallForcelist",
        "HKLM:\SOFTWARE\Policies\OperaSoftware\Opera\ExtensionInstallForcelist"
    )
    foreach ($pol in $chromiumPolicies) {
        New-Item -Path $pol -Force | Out-Null
        Set-ItemProperty -Path $pol -Name "1" -Value $ExtEntry
    }
    Write-Host "Extension policy set for Chrome, Edge, Brave, Opera (force install)."

    # Register the HTTP URL ACL so the service (LocalSystem) can bind the listener
    netsh http add urlacl url=http://localhost:47252/extension/ user=Everyone 2>&1 | Out-Null
    Write-Host "  URL ACL registered for extension HTTP server."

    # Firefox: pack a .xpi (zip of extension files) and set enterprise policy
    $XpiPath = Join-Path $ExtDir "parentguard.xpi"
    $ZipTemp = Join-Path $ExtDir "parentguard_tmp.zip"
    Remove-Item -Path $XpiPath, $ZipTemp -ErrorAction SilentlyContinue
    Compress-Archive -Path "$ExtDir\*" -DestinationPath $ZipTemp -Force -ErrorAction SilentlyContinue
    if (Test-Path $ZipTemp) { Rename-Item $ZipTemp $XpiPath -Force }
    if (Test-Path $XpiPath) {
        $XpiUrl   = "file:///$($XpiPath.Replace('\','/').Replace(' ','%20'))"
        $ffPolKey = "HKLM:\SOFTWARE\Policies\Mozilla\Firefox\Extensions\Install"
        New-Item -Path $ffPolKey -Force | Out-Null
        Set-ItemProperty -Path $ffPolKey -Name "1" -Value $XpiUrl
        Write-Host "Extension policy set for Firefox (force install)."
    }
} else {
    Write-Host "  No .crx found in $ExtDir; extension policy not set."
}

# -- 3a. Add Windows Defender exclusion so Smart App Control trusts installed files --
Add-MpPreference -ExclusionPath $InstallDir -ErrorAction SilentlyContinue
Write-Host "Windows Defender exclusion added for: $InstallDir"

# -- 3. Harden service DACLs ---------------------------------------------------
# Standard users (Interactive Users) can only query status -- not stop, start, or configure.
# This runs after WiX's InstallServices step so both service objects exist in the SCM.
$svcSddl = "D:(A;;CCLCSWRPWPDTLOCRWDRC;;;SY)(A;;CCLCSWRPWPDTLOCRWDRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"
sc.exe sdset ParentalControlService  $svcSddl | Out-Null
sc.exe sdset ParentalControlWatchdog $svcSddl | Out-Null
Write-Host "Service DACLs hardened."
