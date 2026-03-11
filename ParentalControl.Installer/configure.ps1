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

# ── 1. Write native messaging manifest with the actual install path ──────────
$manifestPath  = Join-Path $InstallDir "nativemessaging-manifest.json"
$nativeHostExe = Join-Path $InstallDir "ParentalControl.NativeHost.exe"

$manifest = [ordered]@{
    name            = "com.parentalcontrol.host"
    description     = "ParentalControl native messaging host"
    path            = $nativeHostExe
    type            = "stdio"
    allowed_origins = @("chrome-extension://miigkkfhaopclbnfgfmbdbilkelfllgm/")
}

$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Native messaging manifest written to: $manifestPath"

# ── 2. Harden service DACLs ───────────────────────────────────────────────────
# Standard users (Interactive Users) can only query status — not stop, start, or configure.
# This runs after WiX's InstallServices step so both service objects exist in the SCM.
$svcSddl = "D:(A;;CCLCSWRPWPDTLOCRWDRC;;;SY)(A;;CCLCSWRPWPDTLOCRWDRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"
sc.exe sdset ParentalControlService  $svcSddl | Out-Null
sc.exe sdset ParentalControlWatchdog $svcSddl | Out-Null
Write-Host "Service DACLs hardened."
