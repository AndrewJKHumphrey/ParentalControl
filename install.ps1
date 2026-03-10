#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the ParentalControl Windows Service.
.DESCRIPTION
    Builds the solution, installs the service, and configures auto-restart on failure.
    Must be run as Administrator.
#>

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
$existingWatchdog = Get-Service -Name $WatchdogName -ErrorAction SilentlyContinue
if ($existingWatchdog) {
    Write-Host "`nStopping existing watchdog..." -ForegroundColor Yellow
    Stop-Service -Name $WatchdogName -Force -ErrorAction SilentlyContinue
    sc.exe delete $WatchdogName | Out-Null
    Start-Sleep -Seconds 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "`nStopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

    # Wait for the service process to fully exit (not just for SCM to report Stopped)
    $svcProcess = Get-Process -Name "ParentalControl.Service" -ErrorAction SilentlyContinue
    if ($svcProcess) {
        Write-Host "  Waiting for service process to exit..."
        $svcProcess | ForEach-Object { $_.WaitForExit(10000) | Out-Null }
    }

    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
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

# Set ACLs on data directory (allow Users to read/write the database)
$dataAcl = Get-Acl $DataDir
$dataAcl.SetAccessRuleProtection($true, $false)
$dataSystemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$dataAdminsRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$dataUsersRule  = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Users", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$dataAcl.AddAccessRule($dataSystemRule)
$dataAcl.AddAccessRule($dataAdminsRule)
$dataAcl.AddAccessRule($dataUsersRule)
Set-Acl $DataDir $dataAcl

# 8. Register Windows Services
Write-Host "`nRegistering Windows Service '$ServiceName'..." -ForegroundColor Yellow

New-Service -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName "ParentGuard Control Service" `
            -Description "Enforces parental controls: app blocking, screen time limits, and website filtering." `
            -StartupType Manual

# Configure auto-restart on failure
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

$watchdogExe = Join-Path $InstallDir "ParentalControl.Watchdog.exe"
Write-Host "`nRegistering Watchdog Service '$WatchdogName'..." -ForegroundColor Yellow
New-Service -Name $WatchdogName `
            -BinaryPathName "`"$watchdogExe`"" `
            -DisplayName "ParentGuard Watchdog Service" `
            -Description "Monitors and restarts the ParentalControl enforcement service if it stops unexpectedly." `
            -StartupType Automatic

sc.exe failure $WatchdogName reset= 86400 actions= restart/5000/restart/10000/restart/30000

# 9. Register native messaging host for all supported browsers
Write-Host "`nRegistering native messaging host..." -ForegroundColor Yellow
$NativeHostManifest = Join-Path $InstallDir "nativemessaging-manifest.json"

# Update the path inside the manifest to match the install directory
$manifestContent = Get-Content "$SolutionDir\ParentalControl.NativeHost\nativemessaging-manifest.json" -Raw
$manifestContent = $manifestContent -replace 'C:\\\\Program Files\\\\ParentalControl\\\\ParentalControl.NativeHost.exe',
                                               ($InstallDir + '\ParentalControl.NativeHost.exe').Replace('\', '\\')
Set-Content -Path $NativeHostManifest -Value $manifestContent -Encoding UTF8

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
