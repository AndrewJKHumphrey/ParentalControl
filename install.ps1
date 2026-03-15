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
            Write-Warning "Service '$Name' still exists after $waited s; it may be marked for deletion pending a reboot."
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

# Add Windows Defender / Smart App Control exclusions BEFORE building so that
# newly compiled DLLs in the publish output and install directories are trusted.
Write-Host "`nAdding Windows Defender exclusions..." -ForegroundColor Yellow
Add-MpPreference -ExclusionPath $InstallDir                        -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath "$SolutionDir\publish"             -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath "$SolutionDir\ParentalControl.Service\bin"   -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath "$SolutionDir\ParentalControl.Watchdog\bin"  -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath "$SolutionDir\ParentalControl.NativeHost\bin" -ErrorAction SilentlyContinue
Add-MpPreference -ExclusionPath "$SolutionDir\ParentalControl.UI\bin"        -ErrorAction SilentlyContinue
Write-Host "  Exclusions added." -ForegroundColor Green

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
            Write-Host "  Force killing '$pname' (pid $($_.Id))..." -ForegroundColor Yellow
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

# Close browsers -- the NativeHost is launched by the browser and holds Core DLL locks.
# We kill ALL browser processes (including background/headless ones) so their NativeHost
# child processes also exit and the enterprise extension policy takes effect on next launch.
$browsers = @("msedge", "chrome", "firefox", "brave", "opera")
$closedAny = $false
foreach ($b in $browsers) {
    if (Get-Process -Name $b -ErrorAction SilentlyContinue) {
        Write-Host "  Force killing all '$b' processes (including background)..." -ForegroundColor Yellow
        # Loop until no instances remain -- Stop-Process kills visible + background processes
        $attempts = 0
        while ((Get-Process -Name $b -ErrorAction SilentlyContinue) -and $attempts -lt 10) {
            Stop-Process -Name $b -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            $attempts++
        }
        if (Get-Process -Name $b -ErrorAction SilentlyContinue) {
            Write-Warning "  Some '$b' processes could not be killed after $attempts attempts."
        }
        $closedAny = $true
    }
}
if ($closedAny) {
    Write-Host "  Browsers closed. They will reopen normally after install." -ForegroundColor Green
    Start-Sleep -Seconds 1  # brief pause to let OS release file handles
}

# Delete existing database -- ensures a clean install with no leftover data
# from previous versions.  Must run AFTER browsers are closed so the NativeHost
# (which the browser spawns) has released any locks on the DB file.
Write-Host "`nDeleting existing database for clean install..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
Remove-Item -Path "$DataDir\data.db*" -Force -ErrorAction SilentlyContinue
Write-Host "  Database cleared." -ForegroundColor Green

Copy-Item "$SolutionDir\publish\service\*"    $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\watchdog\*"   $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\nativehost\*" $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\publish\ui\*"         $InstallDir -Recurse -Force
Copy-Item "$SolutionDir\ParentalControl.UI\help.html"     $InstallDir -Force
Copy-Item "$SolutionDir\ParentalControl.UI\dev-help.html" $InstallDir -Force

# Copy browser extension files
$ExtensionInstallDir = Join-Path $InstallDir "extension"
New-Item -ItemType Directory -Force -Path $ExtensionInstallDir | Out-Null
Copy-Item "$SolutionDir\ParentalControl.Extension\*" $ExtensionInstallDir -Recurse -Force

# Pack the extension into a .crx -- pure PowerShell CRX3, no browser required.
# (Modern Chrome/Edge dropped --pack-extension and refuse it when elevated.)
Write-Host "`nPacking browser extension..." -ForegroundColor Yellow
$ExtId   = "lackpoggaaeodfcagkfcglokeilcfokg"
$PemFile = Join-Path $SolutionDir "extension.pem"
$CrxDest = Join-Path $ExtensionInstallDir "parentguard.crx"

function New-CrxPackage {
    param(
        [string]$ExtensionDir,
        [string]$PrivateKeyPem,
        [string]$OutputCrx
    )

    Add-Type -AssemblyName System.IO.Compression

    # Compatible with Windows PowerShell 5.1 (.NET Framework) and PowerShell 7+.
    # RSA.ImportFromPem / ExportSubjectPublicKeyInfo require .NET 5+, so we parse
    # the PEM/DER manually and use RSACryptoServiceProvider which works everywhere.

    function ConvertTo-Varint([long]$v) {
        $buf = New-Object System.Collections.Generic.List[byte]
        do {
            $b = [byte]($v -band 0x7F)
            $v = [long]($v -shr 7)
            if ($v -gt 0) { $b = $b -bor 0x80 }
            $buf.Add($b)
        } while ($v -gt 0)
        return ,$buf.ToArray()
    }

    function ConvertTo-ProtoField([int]$fieldNum, [byte[]]$data) {
        $tag = ConvertTo-Varint (($fieldNum -shl 3) -bor 2)
        $len = ConvertTo-Varint ([long]$data.Length)
        return ,[byte[]]($tag + $len + $data)
    }

    function Join-Bytes {
        param([byte[][]]$Chunks)
        $ms2 = New-Object System.IO.MemoryStream
        foreach ($c in $Chunks) { $ms2.Write($c, 0, $c.Length) }
        return ,$ms2.ToArray()
    }

    function Read-DerLen([byte[]]$buf, [ref]$pos) {
        $b = $buf[$pos.Value]; $pos.Value++
        if ($b -lt 0x80) { return [int]$b }
        $n = [int]($b -band 0x7F); $len = 0
        for ($x = 0; $x -lt $n; $x++) { $len = ($len -shl 8) -bor ([int]$buf[$pos.Value]); $pos.Value++ }
        return $len
    }

    function Read-DerInt([byte[]]$buf, [ref]$pos) {
        $pos.Value++
        $len  = Read-DerLen $buf $pos
        $data = [byte[]]$buf[$pos.Value..($pos.Value + $len - 1)]
        $pos.Value += $len
        if ($data.Length -gt 1 -and $data[0] -eq 0x00) { $data = [byte[]]$data[1..($data.Length - 1)] }
        return ,$data
    }

    function Write-DerLen([int]$len) {
        if ($len -lt 0x80)    { return ,[byte[]]@([byte]$len) }
        if ($len -lt 0x100)   { return ,[byte[]]@([byte]0x81, [byte]$len) }
        if ($len -lt 0x10000) { return ,[byte[]]@([byte]0x82, [byte]($len -shr 8), [byte]($len -band 0xFF)) }
        return ,[byte[]]@([byte]0x83, [byte]($len -shr 16), [byte](($len -shr 8) -band 0xFF), [byte]($len -band 0xFF))
    }

    function Write-DerSeq([byte[]]$content) {
        return ,[byte[]](@([byte]0x30) + (Write-DerLen $content.Length) + $content)
    }

    function Write-DerIntField([byte[]]$data) {
        if ($data[0] -band 0x80) { $data = [byte[]](@([byte]0x00) + $data) }
        return ,[byte[]](@([byte]0x02) + (Write-DerLen $data.Length) + $data)
    }

    # Step 1: ZIP extension files (no parent folder in archive)
    $ms = New-Object System.IO.MemoryStream
    $zipArch = New-Object System.IO.Compression.ZipArchive -ArgumentList $ms, ([System.IO.Compression.ZipArchiveMode]::Create), $true
    $rootLen = $ExtensionDir.TrimEnd('\', '/').Length + 1
    foreach ($f in (Get-ChildItem $ExtensionDir -File -Recurse | Sort-Object FullName)) {
        if ($f.Extension -in @('.crx', '.pem', '.xpi')) { continue }
        $entryName = $f.FullName.Substring($rootLen).Replace('\', '/')
        $entry = $zipArch.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $dst = $entry.Open()
        $src = [System.IO.File]::OpenRead($f.FullName)
        $src.CopyTo($dst); $src.Dispose(); $dst.Dispose()
    }
    $zipArch.Dispose()
    [byte[]]$zipBytes = $ms.ToArray()
    $ms.Dispose()

    # Step 2: Parse PEM -> RSA parameters (handles PKCS#1 and PKCS#8)
    $pemText = [System.IO.File]::ReadAllText($PrivateKeyPem)
    $isPkcs8 = $pemText -match 'BEGIN PRIVATE KEY'
    $b64     = ($pemText -replace '-----[^-]+-----', '' -replace '[\r\n ]', '')
    [byte[]]$der = [System.Convert]::FromBase64String($b64)

    $pos = [ref]0
    $pos.Value++; Read-DerLen $der $pos | Out-Null   # outer SEQUENCE

    if ($isPkcs8) {
        $pos.Value++; $vl = Read-DerLen $der $pos; $pos.Value += $vl    # version
        $pos.Value++; $al = Read-DerLen $der $pos; $pos.Value += $al    # AlgId SEQUENCE
        $pos.Value++; Read-DerLen $der $pos | Out-Null                  # OCTET STRING
        $pos.Value++; Read-DerLen $der $pos | Out-Null                  # inner SEQUENCE
        $pos.Value++; $vl2 = Read-DerLen $der $pos; $pos.Value += $vl2 # inner version
    } else {
        $pos.Value++; $vl = Read-DerLen $der $pos; $pos.Value += $vl   # version
    }

    $modulus  = Read-DerInt $der $pos
    $exponent = Read-DerInt $der $pos
    $privExp  = Read-DerInt $der $pos
    $p1       = Read-DerInt $der $pos
    $q1       = Read-DerInt $der $pos
    $dp1      = Read-DerInt $der $pos
    $dq1      = Read-DerInt $der $pos
    $invQ1    = Read-DerInt $der $pos

    $rsaParams          = New-Object System.Security.Cryptography.RSAParameters
    $rsaParams.Modulus  = $modulus;  $rsaParams.Exponent  = $exponent
    $rsaParams.D        = $privExp;  $rsaParams.P         = $p1
    $rsaParams.Q        = $q1;       $rsaParams.DP        = $dp1
    $rsaParams.DQ       = $dq1;      $rsaParams.InverseQ  = $invQ1

    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.PersistKeyInCsp = $false
    $rsa.ImportParameters($rsaParams)

    # Step 3: Build SubjectPublicKeyInfo DER (Chrome uses SHA-256(SPKI) for the extension ID)
    [byte[]]$rsaPubKeySeq = Write-DerSeq (Join-Bytes @((Write-DerIntField $modulus), (Write-DerIntField $exponent)))
    [byte[]]$bsContent    = [byte[]](@([byte]0x00) + $rsaPubKeySeq)
    [byte[]]$bitString    = [byte[]](@([byte]0x03) + (Write-DerLen $bsContent.Length) + $bsContent)
    [byte[]]$algOid       = @(0x06,0x09,0x2a,0x86,0x48,0x86,0xf7,0x0d,0x01,0x01,0x01,0x05,0x00)
    [byte[]]$spki         = Write-DerSeq (Join-Bytes @((Write-DerSeq $algOid), $bitString))

    # Step 4: crx_id = first 16 bytes of SHA-256(spki)
    $sha256a = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
    [byte[]]$crxId = ($sha256a.ComputeHash($spki))[0..15]
    $sha256a.Dispose()

    # Step 5-6: SignedData proto + data-to-sign
    [byte[]]$signedHeaderBytes = ConvertTo-ProtoField 1 $crxId
    [byte[]]$prefix = Join-Bytes @(([System.Text.Encoding]::ASCII.GetBytes("CRX3 SignedData")), ([byte[]]@([byte]0x00)))
    [byte[]]$lenLE  = [System.BitConverter]::GetBytes([uint32]$signedHeaderBytes.Length)
    [byte[]]$toSign = Join-Bytes @($prefix, $lenLE, $signedHeaderBytes, $zipBytes)

    # Step 7: Sign
    $sha256b = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
    [byte[]]$sig = $rsa.SignData($toSign, $sha256b)
    $sha256b.Dispose(); $rsa.Dispose()

    # Step 8-9: Assemble CrxFileHeader and write CRX3 binary
    [byte[]]$proof     = Join-Bytes @((ConvertTo-ProtoField 1 $spki), (ConvertTo-ProtoField 2 $sig))
    [byte[]]$hdrF2     = ConvertTo-ProtoField 2 $proof
    [byte[]]$hdrF10000 = Join-Bytes @((ConvertTo-Varint 80002L), (ConvertTo-Varint ([long]$signedHeaderBytes.Length)), $signedHeaderBytes)
    [byte[]]$header    = Join-Bytes @($hdrF2, $hdrF10000)

    $out = [System.IO.File]::OpenWrite($OutputCrx)
    foreach ($chunk in @(
        [byte[]]@(0x43, 0x72, 0x32, 0x34),
        [System.BitConverter]::GetBytes([uint32]3),
        [System.BitConverter]::GetBytes([uint32]$header.Length),
        $header, $zipBytes
    )) { $out.Write($chunk, 0, $chunk.Length) }
    $out.Dispose()
}

$ExtSource = Join-Path $SolutionDir "ParentalControl.Extension"
if (Test-Path $PemFile) {
    try {
        New-CrxPackage -ExtensionDir $ExtSource -PrivateKeyPem $PemFile -OutputCrx $CrxDest
        Write-Host "  Extension packed: $CrxDest" -ForegroundColor Green
    } catch {
        Write-Warning "  CRX packing failed: $_ -- auto-install policies will be skipped."
        $CrxDest = $null
    }
} else {
    Write-Warning "  extension.pem not found - skipping CRX pack. Extension auto-install will be unavailable."
    $CrxDest = $null
}

# Write update.xml and set browser policies if we have a CRX
if ($CrxDest -and (Test-Path $CrxDest)) {
    $UpdateXml = Join-Path $ExtensionInstallDir "update.xml"
    $xml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='$ExtId' status='ok'>
    <updatecheck status='ok' codebase='http://localhost:47252/extension/parentguard.crx' version='1.0.0' />
  </app>
</gupdate>
"@
    Set-Content -Path $UpdateXml -Value $xml -Encoding UTF8
    Write-Host "  update.xml written (HTTP codebase URL)." -ForegroundColor Green
}

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

# Set ACLs on data directory.
# SYSTEM and Admins get FullControl.
# Users need read+write (without delete) so the NativeHost -- which the browser
# launches as the logged-in (non-elevated) user -- can query the DB and log blocked
# URLs.  SQLite also requires write access to the directory to create WAL/journal
# files even for read operations, so ReadAndExecute alone is not sufficient.
$dataAcl = New-Object System.Security.AccessControl.DirectorySecurity
$dataAcl.SetAccessRuleProtection($true, $false)  # break inheritance, no inherited copy
$dataAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    "SYSTEM",         "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
$dataAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
# Read+Write without Delete -- lets NativeHost use the DB but prevents users
# from deleting the database file directly.
$usersDataRights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute -bor `
                   [System.Security.AccessControl.FileSystemRights]::Write
$dataAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    "Users", $usersDataRights, "ContainerInherit,ObjectInherit", "None", "Allow")))
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

# Harden service DACLs -- standard users (Interactive Users) can only query status;
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

# 10. Set browser extension auto-install policies
Write-Host "`nConfiguring browser extension auto-install policies..." -ForegroundColor Yellow

# Register HTTP URL ACL so the service can bind its extension file server
netsh http delete urlacl url=http://localhost:47252/extension/ 2>$null | Out-Null
netsh http add urlacl url=http://localhost:47252/extension/ user=Everyone 2>&1 | Out-Null
Write-Host "  URL ACL registered for extension HTTP server." -ForegroundColor Green

$UpdateXml = Join-Path $ExtensionInstallDir "update.xml"
if ($CrxDest -and (Test-Path $CrxDest) -and (Test-Path $UpdateXml)) {
    $ExtEntry = "${ExtId};http://localhost:47252/extension/update.xml"

    # Chromium-based: Chrome, Edge, Brave, Opera
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
    Write-Host "  Force-install policy set for Chrome, Edge, Brave, Opera." -ForegroundColor Green

    # Edge blocks non-store extensions from ExtensionInstallForcelist via cloud policy on
    # consumer devices. ExtensionSettings is higher-priority and explicitly authorizes it.
    $extSettingsJson = "{`"$ExtId`":{`"installation_mode`":`"force_installed`",`"update_url`":`"http://localhost:47252/extension/update.xml`",`"toolbar_pin`":`"force_pinned`"}}"
    Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Edge" -Name "ExtensionSettings" -Value $extSettingsJson
    Write-Host "  ExtensionSettings policy written (overrides cloud blocklist)." -ForegroundColor Green

    # Firefox: pack a .xpi (zip of extension files) and set enterprise policy
    $XpiPath = Join-Path $ExtensionInstallDir "parentguard.xpi"
    $ZipTemp = Join-Path $ExtensionInstallDir "parentguard_tmp.zip"
    Remove-Item -Path $XpiPath, $ZipTemp -ErrorAction SilentlyContinue
    Compress-Archive -Path "$ExtensionInstallDir\*" -DestinationPath $ZipTemp -Force -ErrorAction SilentlyContinue
    if (Test-Path $ZipTemp) { Rename-Item $ZipTemp $XpiPath -Force }
    if (Test-Path $XpiPath) {
        $XpiUrl   = "file:///$($XpiPath.Replace('\','/').Replace(' ','%20'))"
        $ffPolKey = "HKLM:\SOFTWARE\Policies\Mozilla\Firefox\Extensions\Install"
        New-Item -Path $ffPolKey -Force | Out-Null
        Set-ItemProperty -Path $ffPolKey -Name "1" -Value $XpiUrl
        Write-Host "  Force-install policy set for Firefox." -ForegroundColor Green
    }

    Write-Host "`nExtension auto-install configured. Open any supported browser to complete installation." -ForegroundColor Cyan
} else {
    Write-Host "  No CRX available - skipping browser policies." -ForegroundColor Yellow
    Write-Host "  Open the Web Filter page in the ParentGuard UI for manual install instructions." -ForegroundColor Cyan
}

# 11. Start the watchdog (it will start the main service)
Write-Host "`nStarting watchdog (which will start the main service)..." -ForegroundColor Yellow
Start-Service -Name $WatchdogName

Write-Host "`n=== Installation complete! ===" -ForegroundColor Green
Write-Host "Service status: $((Get-Service $ServiceName).Status)" -ForegroundColor Green
Write-Host "Admin UI: $InstallDir\ParentalControl.UI.exe" -ForegroundColor Green
Write-Host "`nDefault password: parent1234  (change this immediately in Settings!)" -ForegroundColor Red
