#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the ParentalControl Windows Service.
.DESCRIPTION
    Builds the solution, installs the service, and configures auto-restart on failure.
    Must be run as Administrator.
#>

# 8. Register Windows Services
# Helper: stop and remove a service if it already exists so reinstalls work cleanly.
function Remove-ServiceIfExists([string]$Name)
{
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($svc)
    {
        if ($svc.Status -ne 'Stopped')
        {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
        }

        # Wait for the service process to fully exit before deleting
        $procName = [System.IO.Path]::GetFileNameWithoutExtension($Name)
        $svcProcess = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($svcProcess) {
            Write-Host "  Waiting for '$procName' process to exit..."
            $svcProcess | ForEach-Object { $_.WaitForExit(15000) | Out-Null }
        }

        sc.exe delete $Name | Out-Null

        # Wait until SCM fully removes the entry (up to 30 seconds)
        $waited = 0
        while ((Get-Service -Name $Name -ErrorAction SilentlyContinue) -and ($waited -lt 30)) {
            Start-Sleep -Seconds 1
            $waited++
        }
        if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
            Write-Warning "Service '$Name' still exists after $waited s - it may be marked for deletion pending a reboot."
        }
    }
}

$ErrorActionPreference = "Stop"
$SolutionDir  = $PSScriptRoot
$ServiceName  = "ParentalControlService"
$WatchdogName = "ParentalControlWatchdog"
$InstallDir   = "C:\Program Files\ParentalControl"
$DataDir      = "C:\ProgramData\ParentalControl"

Write-Host "=== ParentGuard Installer ===" -ForegroundColor Cyan

# 1. Stop and remove existing services FIRST so files are not locked during copy
$exePath = Join-Path $InstallDir "ParentalControl.Service.exe"

# Stop watchdog first so it doesn't restart the main service during reinstall
Write-Host "`nRemoving existing services (if any)..." -ForegroundColor Yellow
Remove-ServiceIfExists $WatchdogName
Remove-ServiceIfExists $ServiceName

# Wait for the actual exe processes to fully release file locks before copying.
# Service names differ from exe names (e.g. "ParentalControlService" vs "ParentalControl.Service"),
# so we wait on the real process names here rather than inside Remove-ServiceIfExists.
$processesToWait = @("ParentalControl.Service", "ParentalControl.Watchdog")
foreach ($pname in $processesToWait) {
    $procs = Get-Process -Name $pname -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "  Waiting for '$pname' to exit..." -ForegroundColor Yellow
        $procs | ForEach-Object { $_.WaitForExit(15000) | Out-Null }
        # Force-kill anything still alive after the timeout
        $procs | Where-Object { -not $_.HasExited } | ForEach-Object {
            Write-Host "  Force-killing '$pname' (pid $($_.Id))..." -ForegroundColor Yellow
            $_.Kill()
            $_.WaitForExit(5000) | Out-Null
        }
    }
}

# 2. Publish the service
Write-Host "`nBuilding and publishing service..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\ParentalControl.Service\ParentalControl.Service.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "$SolutionDir\publish\service"

# 3. Publish the watchdog
Write-Host "`nBuilding and publishing watchdog..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\ParentalControl.Watchdog\ParentalControl.Watchdog.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "$SolutionDir\publish\watchdog"

# 4. Publish the NativeHost
Write-Host "`nBuilding and publishing native messaging host..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\ParentalControl.NativeHost\ParentalControl.NativeHost.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "$SolutionDir\publish\nativehost"

# 5. Publish the UI
Write-Host "`nBuilding and publishing UI..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\ParentalControl.UI\ParentalControl.UI.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "$SolutionDir\publish\ui"

# 6. Copy files to Program Files
Write-Host "`nInstalling to $InstallDir..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir   | Out-Null

# Kill all ParentalControl processes that may be running from the install dir
# (UI, NativeHost, or any lingering service process) to release file locks.
$knownExes = @(
    "ParentalControl.Service",
    "ParentalControl.Watchdog",
    "ParentalControl.NativeHost",
    "ParentalControl.UI"
)
foreach ($pname in $knownExes) {
    $procs = Get-Process -Name $pname -ErrorAction SilentlyContinue |
             Where-Object { $_.Path -like "$InstallDir\*" }
    if ($procs) {
        Write-Host "  Stopping '$pname' to release file locks..." -ForegroundColor Yellow
        $procs | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
    }
}

# Close browsers — the NativeHost is launched by the browser and holds Core DLL locks.
# We close all supported browsers so their NativeHost child processes also exit.
$browsers = @("msedge", "chrome", "firefox", "brave", "opera")
$closedAny = $false
foreach ($b in $browsers) {
    $procs = Get-Process -Name $b -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "  Closing '$b' to release NativeHost file locks..." -ForegroundColor Yellow
        $procs | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        # Force-kill any that didn't close gracefully
        $procs | Where-Object { -not $_.HasExited } | ForEach-Object {
            $_.Kill(); $_.WaitForExit(5000) | Out-Null
        }
        $closedAny = $true
    }
}
if ($closedAny) {
    Write-Host "  Browsers closed. They will reopen normally after install." -ForegroundColor Green
}

Copy-Item "$SolutionDir\publish\service\*"    $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\watchdog\*"   $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\nativehost\*" $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\ui\*"         $InstallDir -Recurse -Force

# Copy browser extension files
$ExtensionInstallDir = Join-Path $InstallDir "extension"
New-Item -ItemType Directory -Force -Path $ExtensionInstallDir | Out-Null
Copy-Item "$SolutionDir\ParentalControl.Extension\*" $ExtensionInstallDir -Recurse -Force

# 7. Set ACLs on install directory (deny write to Users)
Write-Host "`nHardening file permissions..." -ForegroundColor Yellow
$acl = Get-Acl $InstallDir
$acl.SetAccessRuleProtection($true, $false)
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$adminsRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$usersRule  = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Users", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($systemRule)
$acl.AddAccessRule($adminsRule)
$acl.AddAccessRule($usersRule)
Set-Acl $InstallDir $acl

# Set ACLs on data directory — SYSTEM and Admins only.
# The service (SYSTEM) writes all DB data on behalf of users; the parent UI runs
# as Administrator. Children have no legitimate reason to touch the DB files directly.
$dataAcl = New-Object System.Security.AccessControl.DirectorySecurity
$dataAcl.SetAccessRuleProtection($true, $false)  # break inheritance, no inherited copy
$dataAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    "SYSTEM",         "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
$dataAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
Set-Acl $DataDir $dataAcl



Write-Host "`nRegistering Windows Service '$ServiceName'..." -ForegroundColor Yellow
Remove-ServiceIfExists $ServiceName
New-Service -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName "ParentGuard Control Service" `
            -Description "Enforces parental controls: app blocking, screen time limits, and website filtering." `
            -StartupType Manual

# Configure auto-restart on failure
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

$watchdogExe = Join-Path $InstallDir "ParentalControl.Watchdog.exe"
Write-Host "`nRegistering Watchdog Service '$WatchdogName'..." -ForegroundColor Yellow
Remove-ServiceIfExists $WatchdogName
New-Service -Name $WatchdogName `
            -BinaryPathName "`"$watchdogExe`"" `
            -DisplayName "ParentGuard Watchdog Service" `
            -Description "Monitors and restarts the ParentalControl enforcement service if it stops unexpectedly." `
            -StartupType Automatic

sc.exe failure $WatchdogName reset= 86400 actions= restart/5000/restart/10000/restart/30000

# Harden service DACLs — standard users (Interactive Users) can only query status;
# they cannot stop, start, or reconfigure either service.
Write-Host "`nHardening service DACLs..." -ForegroundColor Yellow
$svcSddl = "D:(A;;CCLCSWRPWPDTLOCRSDWDRC;;;SY)(A;;CCLCSWRPWPDTLOCRSDWDRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"
sc.exe sdset $ServiceName  $svcSddl | Out-Null
sc.exe sdset $WatchdogName $svcSddl | Out-Null
Write-Host "  Service DACLs hardened." -ForegroundColor Green

# 9. Register native messaging host for all supported browsers
Write-Host "`nRegistering native messaging host..." -ForegroundColor Yellow
$NativeHostManifest = Join-Path $InstallDir "nativemessaging-manifest.json"

# Write the native messaging manifest with the correct install path
$sourceManifest = Get-Content "$SolutionDir\ParentalControl.NativeHost\nativemessaging-manifest.json" | ConvertFrom-Json
$sourceManifest.path = Join-Path $InstallDir "ParentalControl.NativeHost.exe"
$sourceManifest | ConvertTo-Json | Set-Content -Path $NativeHostManifest -Encoding UTF8

$NativeHostName = "com.parentalcontrol.host"
$browsers = @(
    "HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$NativeHostName",
    "HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\$NativeHostName",
    "HKLM:\SOFTWARE\Mozilla\NativeMessagingHosts\$NativeHostName",
    "HKLM:\SOFTWARE\BraveSoftware\Brave-Browser\NativeMessagingHosts\$NativeHostName",
    "HKLM:\SOFTWARE\Opera Software\NativeMessagingHosts\$NativeHostName"
)
foreach ($regPath in $browsers) {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name "(Default)" -Value $NativeHostManifest
}
Write-Host "  Native messaging host registered for Chrome, Edge, Firefox, Brave, Opera" -ForegroundColor Green

# 10. Register browser extension via enterprise policy (force-install without store)
# NOTE: Replace PARENTALCONTROL_EXTENSION_ID with the actual packed extension ID.
#       The extension must be packed as a .crx with a fixed private key before deployment.
Write-Host "`nRegistering browser extension policy..." -ForegroundColor Yellow
$ExtId = "PARENTALCONTROL_EXTENSION_ID"  # TODO: replace with real ID after packing

if (Test-Path "$ExtensionInstallDir\update.xml") {
    $ExtUpdateUrl = "file:///$($ExtensionInstallDir.Replace('\','/'))/update.xml"
    $ExtEntry     = "${ExtId};${ExtUpdateUrl}"

    # Edge force-install
    $edgePolicyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist"
    New-Item -Path $edgePolicyPath -Force | Out-Null
    Set-ItemProperty -Path $edgePolicyPath -Name "1" -Value $ExtEntry

    # Chrome force-install
    $chromePolicyPath = "HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist"
    New-Item -Path $chromePolicyPath -Force | Out-Null
    Set-ItemProperty -Path $chromePolicyPath -Name "1" -Value $ExtEntry

    Write-Host "  Extension force-install policy set for Edge and Chrome" -ForegroundColor Green
} else {
    Write-Host "  Skipping extension force-install: update.xml not found in $ExtensionInstallDir" -ForegroundColor Yellow
    Write-Host "  To enable auto-install, pack the extension as a .crx and place update.xml in $ExtensionInstallDir" -ForegroundColor Yellow
}

# Firefox enterprise policy
$ffDir = "C:\Program Files\Mozilla Firefox\distribution"
if (Test-Path "C:\Program Files\Mozilla Firefox") {
    New-Item -ItemType Directory -Force -Path $ffDir | Out-Null
    $ffPolicy = @{
        policies = @{
            Extensions = @{
                Install = @("file:///$($ExtensionInstallDir.Replace('\','/'))/parentalcontrol.xpi")
            }
        }
    } | ConvertTo-Json -Depth 4
    Set-Content -Path "$ffDir\policies.json" -Value $ffPolicy -Encoding UTF8
    Write-Host "  Firefox enterprise policy written" -ForegroundColor Green
}

# 11. Start the watchdog (it will start the main service)
Write-Host "`nStarting watchdog (which will start the main service)..." -ForegroundColor Yellow
Start-Service -Name $WatchdogName

Write-Host "`n=== Installation complete! ===" -ForegroundColor Green
Write-Host "Service status: $((Get-Service $ServiceName).Status)" -ForegroundColor Green
Write-Host "Admin UI: $InstallDir\ParentalControl.UI.exe" -ForegroundColor Green
Write-Host "`nDefault password: parent1234  (change this immediately in Settings!)" -ForegroundColor Red
