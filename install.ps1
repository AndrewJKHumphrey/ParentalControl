#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the ParentalControl Windows Service.
.DESCRIPTION
    Builds the solution, installs the service, and configures auto-restart on failure.
    Must be run as Administrator.
#>

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot
$ServiceName = "ParentalControlService"
#$ServiceExe  = Join-Path $SolutionDir "publish\service\ParentalControl.Service.exe"
$InstallDir  = "C:\Program Files\ParentalControl"
$DataDir     = "C:\ProgramData\ParentalControl"

Write-Host "=== ParentGuard Installer ===" -ForegroundColor Cyan

# 1. Stop and remove existing service FIRST so files are not locked during copy
$exePath = Join-Path $InstallDir "ParentalControl.Service.exe"
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

# 3. Publish the UI
Write-Host "`nBuilding and publishing UI..." -ForegroundColor Yellow
dotnet publish "$SolutionDir\ParentalControl.UI\ParentalControl.UI.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "$SolutionDir\publish\ui"

# 4. Copy files to Program Files
Write-Host "`nInstalling to $InstallDir..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir   | Out-Null
Copy-Item "$SolutionDir\publish\service\*" $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\ui\*"      $InstallDir -Recurse -Force

# 5. Set ACLs on install directory (deny write to Users)
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

# 6. Register Windows Service
Write-Host "`nRegistering Windows Service '$ServiceName'..." -ForegroundColor Yellow

New-Service -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName "ParentGuard Control Service" `
            -Description "Enforces parental controls: app blocking, screen time limits, and website filtering." `
            -StartupType Manual

# Configure auto-restart on failure
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

# 7. Start the service
Write-Host "`nStarting service..." -ForegroundColor Yellow
Start-Service -Name $ServiceName

Write-Host "`n=== Installation complete! ===" -ForegroundColor Green
Write-Host "Service status: $((Get-Service $ServiceName).Status)" -ForegroundColor Green
Write-Host "Admin UI: $InstallDir\ParentalControl.UI.exe" -ForegroundColor Green
Write-Host "`nDefault password: parent1234  (change this immediately in Settings!)" -ForegroundColor Red
