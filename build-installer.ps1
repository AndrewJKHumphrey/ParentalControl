#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds the ParentGuard MSI installer.
.DESCRIPTION
    1. Publishes all four projects (Service, Watchdog, NativeHost, UI) as self-contained win-x64.
    2. Merges the output into publish\combined\ (Service.exe and Watchdog.exe are kept
       in their own publish directories; non-Windows runtimes are excluded).
    3. Generates WiX component WXS files from the merged directory contents.
    4. Builds the WiX installer project.

    Output: ParentalControl.Installer\bin\Release\en-US\ParentGuard-Setup.msi

PREREQUISITES
    dotnet SDK 8+  (WixToolset.Sdk NuGet is downloaded automatically on first build)
#>

$ErrorActionPreference = "Stop"
$SolutionDir   = $PSScriptRoot
$InstallerDir  = Join-Path $SolutionDir "ParentalControl.Installer"
$CombinedDir   = Join-Path $SolutionDir "publish\combined"
$ExtensionDir  = Join-Path $SolutionDir "ParentalControl.Extension"

Write-Host "=== ParentGuard -- Build MSI ===" -ForegroundColor Cyan

# -- Helper: publish a project ------------------------------------------------
function Publish-Project {
    param([string]$Project, [string]$OutDir)
    Write-Host "`nPublishing $Project..." -ForegroundColor Yellow
    dotnet publish "$SolutionDir\$Project\$Project.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -o "$SolutionDir\publish\$OutDir"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Project" }
}

# -- 1. Publish all four projects ---------------------------------------------
Publish-Project "ParentalControl.Service"    "service"
Publish-Project "ParentalControl.Watchdog"   "watchdog"
Publish-Project "ParentalControl.NativeHost" "nativehost"
Publish-Project "ParentalControl.UI"         "ui"

# -- 2. Merge into publish\combined\ ------------------------------------------
# Exclude: service/watchdog EXEs (handled by explicit WiX service components)
# Exclude: non-Windows runtimes (linux-*, osx-*, maccatalyst-*, browser-wasm)
Write-Host "`nMerging publish outputs into publish\combined\..." -ForegroundColor Yellow

if (Test-Path $CombinedDir) { Remove-Item $CombinedDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $CombinedDir | Out-Null

$excludeExes    = @('ParentalControl.Service.exe', 'ParentalControl.Watchdog.exe')
$excludeRtPaths = 'runtimes\browser-wasm', 'runtimes\linux-', 'runtimes\osx-',
                  'runtimes\maccatalyst-', 'runtimes\android', 'runtimes\ios',
                  'runtimes\tvos', 'runtimes\watchos'

function ShouldExclude([string]$relPath) {
    foreach ($prefix in $excludeRtPaths) {
        if ($relPath.StartsWith($prefix)) { return $true }
    }
    return $false
}

foreach ($src in @('watchdog', 'nativehost', 'ui', 'service')) {
    $srcPath = "$SolutionDir\publish\$src"

    # Root-level files
    Get-ChildItem $srcPath -File | Where-Object {
        $_.Name -notin $excludeExes
    } | ForEach-Object {
        Copy-Item $_.FullName -Destination $CombinedDir -Force
    }

    # Subdirectories (with non-Windows runtime filtering)
    Get-ChildItem $srcPath -Directory -Recurse | ForEach-Object {
        $relDir = $_.FullName.Substring($srcPath.Length + 1)
        if (-not (ShouldExclude $relDir)) {
            $dest = Join-Path $CombinedDir $relDir
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory $dest | Out-Null }
        }
    }

    Get-ChildItem $srcPath -File -Recurse | Where-Object {
        $rel = $_.FullName.Substring($srcPath.Length + 1)
        $dir = [System.IO.Path]::GetDirectoryName($rel)
        -not (ShouldExclude $dir) -and $_.Name -notin $excludeExes
    } | ForEach-Object {
        $rel  = $_.FullName.Substring($srcPath.Length + 1)
        $dest = Join-Path $CombinedDir $rel
        Copy-Item $_.FullName -Destination $dest -Force
    }
}

$count = (Get-ChildItem $CombinedDir -Recurse -File).Count
Write-Host "  Combined: $count files" -ForegroundColor Green

# -- 2b. Pack the browser extension as a .crx ---------------------------------
# Pure-PowerShell CRX3 packer -- no browser required.
# Browsers dropped --pack-extension support in recent versions and refuse it
# when the process is elevated (which installers always are).
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

    #-- Protobuf varint --#
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

    #-- Protobuf length-delimited field (wire type 2) --#
    function ConvertTo-ProtoField([int]$fieldNum, [byte[]]$data) {
        $tag = ConvertTo-Varint (($fieldNum -shl 3) -bor 2)
        $len = ConvertTo-Varint ([long]$data.Length)
        return ,[byte[]]($tag + $len + $data)
    }

    #-- Byte-array concatenation via MemoryStream --#
    function Join-Bytes {
        param([byte[][]]$Chunks)
        $ms2 = New-Object System.IO.MemoryStream
        foreach ($c in $Chunks) { $ms2.Write($c, 0, $c.Length) }
        return ,$ms2.ToArray()
    }

    #-- DER helpers (read) --#
    function Read-DerLen([byte[]]$buf, [ref]$pos) {
        $b = $buf[$pos.Value]; $pos.Value++
        if ($b -lt 0x80) { return [int]$b }
        $n = [int]($b -band 0x7F); $len = 0
        for ($x = 0; $x -lt $n; $x++) { $len = ($len -shl 8) -bor ([int]$buf[$pos.Value]); $pos.Value++ }
        return $len
    }

    function Read-DerInt([byte[]]$buf, [ref]$pos) {
        $pos.Value++  # skip 0x02 tag
        $len  = Read-DerLen $buf $pos
        $data = [byte[]]$buf[$pos.Value..($pos.Value + $len - 1)]
        $pos.Value += $len
        if ($data.Length -gt 1 -and $data[0] -eq 0x00) { $data = [byte[]]$data[1..($data.Length - 1)] }
        return ,$data
    }

    #-- DER helpers (write) --#
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

    # ---- Step 1: ZIP extension files (no parent folder) ----
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

    # ---- Step 2: Parse PEM -> RSA key parameters ----
    $pemText  = [System.IO.File]::ReadAllText($PrivateKeyPem)
    $isPkcs8  = $pemText -match 'BEGIN PRIVATE KEY'
    $b64      = ($pemText -replace '-----[^-]+-----', '' -replace '[\r\n ]', '')
    [byte[]]$der = [System.Convert]::FromBase64String($b64)

    $pos = [ref]0
    $pos.Value++                                   # skip outer SEQUENCE tag 0x30
    Read-DerLen $der $pos | Out-Null               # outer length

    if ($isPkcs8) {
        # PKCS#8: version | AlgorithmIdentifier | OCTET STRING { PKCS#1 }
        $pos.Value++; $vl = Read-DerLen $der $pos; $pos.Value += $vl    # skip version
        $pos.Value++; $al = Read-DerLen $der $pos; $pos.Value += $al    # skip AlgId SEQUENCE
        $pos.Value++; Read-DerLen $der $pos | Out-Null                  # skip OCTET STRING tag+len
        $pos.Value++; Read-DerLen $der $pos | Out-Null                  # skip inner SEQUENCE tag+len
        $pos.Value++; $vl2 = Read-DerLen $der $pos; $pos.Value += $vl2 # skip inner version
    } else {
        # PKCS#1: skip version INTEGER
        $pos.Value++; $vl = Read-DerLen $der $pos; $pos.Value += $vl
    }

    $modulus  = Read-DerInt $der $pos
    $exponent = Read-DerInt $der $pos
    $privExp  = Read-DerInt $der $pos
    $p1       = Read-DerInt $der $pos
    $q1       = Read-DerInt $der $pos
    $dp1      = Read-DerInt $der $pos
    $dq1      = Read-DerInt $der $pos
    $invQ1    = Read-DerInt $der $pos

    $rsaParams           = New-Object System.Security.Cryptography.RSAParameters
    $rsaParams.Modulus   = $modulus
    $rsaParams.Exponent  = $exponent
    $rsaParams.D         = $privExp
    $rsaParams.P         = $p1
    $rsaParams.Q         = $q1
    $rsaParams.DP        = $dp1
    $rsaParams.DQ        = $dq1
    $rsaParams.InverseQ  = $invQ1

    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.PersistKeyInCsp = $false
    $rsa.ImportParameters($rsaParams)

    # ---- Step 3: Build SubjectPublicKeyInfo DER manually ----
    # Chrome uses SHA-256(SPKI) to derive the extension ID.
    # SPKI = SEQUENCE { AlgId SEQUENCE { OID rsaEncryption, NULL }, BIT STRING { RSAPublicKey } }
    [byte[]]$rsaPubKeySeq = Write-DerSeq (Join-Bytes @(
        (Write-DerIntField $modulus),
        (Write-DerIntField $exponent)
    ))
    [byte[]]$bsContent = [byte[]](@([byte]0x00) + $rsaPubKeySeq)           # 0x00 = no unused bits
    [byte[]]$bitString = [byte[]](@([byte]0x03) + (Write-DerLen $bsContent.Length) + $bsContent)
    [byte[]]$algOid    = @(0x06,0x09,0x2a,0x86,0x48,0x86,0xf7,0x0d,0x01,0x01,0x01,0x05,0x00)
    [byte[]]$spki      = Write-DerSeq (Join-Bytes @((Write-DerSeq $algOid), $bitString))

    # ---- Step 4: crx_id = first 16 bytes of SHA-256(spki) ----
    $sha256a = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
    [byte[]]$crxId = ($sha256a.ComputeHash($spki))[0..15]
    $sha256a.Dispose()

    # ---- Step 5: SignedData proto { crx_id (field 1) } ----
    [byte[]]$signedHeaderBytes = ConvertTo-ProtoField 1 $crxId

    # ---- Step 6: Build data-to-sign ----
    # "CRX3 SignedData" (16 bytes) + 0x00 + uint32-LE(hdr len) + hdr + zip
    [byte[]]$prefix = Join-Bytes @(([System.Text.Encoding]::ASCII.GetBytes("CRX3 SignedData")), ([byte[]]@([byte]0x00)))
    [byte[]]$lenLE  = [System.BitConverter]::GetBytes([uint32]$signedHeaderBytes.Length)
    [byte[]]$toSign = Join-Bytes @($prefix, $lenLE, $signedHeaderBytes, $zipBytes)

    # ---- Step 7: RSA-PKCS1v15-SHA256 signature ----
    $sha256b = New-Object System.Security.Cryptography.SHA256CryptoServiceProvider
    [byte[]]$sig = $rsa.SignData($toSign, $sha256b)
    $sha256b.Dispose(); $rsa.Dispose()

    # ---- Step 8: CrxFileHeader proto ----
    # AsymmetricKeyProof { public_key(f1), signature(f2) }
    [byte[]]$proof     = Join-Bytes @((ConvertTo-ProtoField 1 $spki), (ConvertTo-ProtoField 2 $sig))
    # CrxFileHeader { sha256_with_rsa(f2), signed_header_data(f10000) }
    # Field 10000 varint tag = (10000<<3)|2 = 80002
    [byte[]]$hdrF2     = ConvertTo-ProtoField 2 $proof
    [byte[]]$hdrF10000 = Join-Bytes @((ConvertTo-Varint 80002L), (ConvertTo-Varint ([long]$signedHeaderBytes.Length)), $signedHeaderBytes)
    [byte[]]$header    = Join-Bytes @($hdrF2, $hdrF10000)

    # ---- Step 9: Write CRX3 binary  "Cr24" | ver=3 | hdrlen | header | zip ----
    $out = [System.IO.File]::OpenWrite($OutputCrx)
    foreach ($chunk in @(
        [byte[]]@(0x43, 0x72, 0x32, 0x34),
        [System.BitConverter]::GetBytes([uint32]3),
        [System.BitConverter]::GetBytes([uint32]$header.Length),
        $header,
        $zipBytes
    )) { $out.Write($chunk, 0, $chunk.Length) }
    $out.Dispose()
}

Write-Host "`nPacking browser extension..." -ForegroundColor Yellow
$ExtPem    = Join-Path $SolutionDir "extension.pem"
$ExtSrcDir = Join-Path $SolutionDir "ParentalControl.Extension"
$ExtId     = "lackpoggaaeodfcagkfcglokeilcfokg"
$CrxDest   = Join-Path $ExtSrcDir "parentguard.crx"

if (Test-Path $ExtPem) {
    try {
        New-CrxPackage -ExtensionDir $ExtSrcDir -PrivateKeyPem $ExtPem -OutputCrx $CrxDest
        Write-Host "  parentguard.crx written." -ForegroundColor Green

        $updateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='$ExtId'>
    <updatecheck codebase='PLACEHOLDER' version='1.0.0' />
  </app>
</gupdate>
"@
        Set-Content -Path "$ExtSrcDir\update.xml" -Value $updateXml -Encoding UTF8
        Write-Host "  update.xml written." -ForegroundColor Green
    } catch {
        Write-Warning "  CRX packing failed: $_"
        Write-Warning "  MSI will not include a packed extension."
    }
} else {
    Write-Warning "  extension.pem not found -- skipping .crx pack."
}

# -- 3. Generate WXS harvest files --------------------------------------------
# WiX v4 doesn't support MSBuild HarvestDirectory items in 4.0.5.
# We generate the ComponentGroup WXS files from PowerShell instead.
# WiX auto-includes all *.wxs files in the installer project directory.

function New-HarvestWxs {
    param(
        [string] $SourceDir,   # Absolute path to the directory to harvest
        [string] $RelSource,   # Relative source path used in <File Source="...\xxx"> e.g. "publish\combined"
        [string] $DirRefId,    # WiX Directory Id for the root (e.g. INSTALLDIR)
        [string] $GroupName,   # ComponentGroup name
        [string] $IdPrefix,    # Unique prefix for component/file Ids
        [string] $OutFile      # Output .wxs file absolute path
    )

    $allFiles = Get-ChildItem $SourceDir -File -Recurse | Sort-Object FullName
    $xml = [System.Text.StringBuilder]::new()
    [void]$xml.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$xml.AppendLine('<!-- AUTO-GENERATED by build-installer.ps1 -- do not edit manually -->')
    [void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$xml.AppendLine('  <Fragment>')
    [void]$xml.AppendLine("    <ComponentGroup Id=""$GroupName"">")

    $idx = 0
    foreach ($f in $allFiles) {
        $relFile = $f.FullName.Substring($SourceDir.Length + 1)
        $relDir  = [System.IO.Path]::GetDirectoryName($relFile)
        $srcAttr = "..\$RelSource\$relFile"
        $fid     = "${IdPrefix}_f$idx"

        if ($relDir) {
            [void]$xml.AppendLine("      <Component Directory=""$DirRefId"" Subdirectory=""$relDir"">")
        } else {
            [void]$xml.AppendLine("      <Component Directory=""$DirRefId"">")
        }
        [void]$xml.AppendLine("        <File Id=""$fid"" Source=""$srcAttr"" />")
        [void]$xml.AppendLine("      </Component>")
        $idx++
    }

    [void]$xml.AppendLine("    </ComponentGroup>")
    [void]$xml.AppendLine('  </Fragment>')
    [void]$xml.AppendLine('</Wix>')

    [System.IO.File]::WriteAllText($OutFile, $xml.ToString(), [System.Text.Encoding]::UTF8)
    Write-Host "  Generated: $(Split-Path $OutFile -Leaf) ($idx components)" -ForegroundColor Green
}

Write-Host "`nGenerating WXS harvest files..." -ForegroundColor Yellow

New-HarvestWxs `
    -SourceDir  $CombinedDir `
    -RelSource  "publish\combined" `
    -DirRefId   "INSTALLDIR" `
    -GroupName  "AppFiles" `
    -IdPrefix   "app" `
    -OutFile    (Join-Path $InstallerDir "AppFiles.generated.wxs")

New-HarvestWxs `
    -SourceDir  $ExtensionDir `
    -RelSource  "ParentalControl.Extension" `
    -DirRefId   "ExtDir" `
    -GroupName  "ExtensionFiles" `
    -IdPrefix   "ext" `
    -OutFile    (Join-Path $InstallerDir "ExtFiles.generated.wxs")

# -- 4. Build the WiX project -------------------------------------------------
Write-Host "`nBuilding WiX installer project..." -ForegroundColor Yellow
dotnet build "$InstallerDir\ParentalControl.Installer.wixproj" `
    --configuration Release `
    -p:Platform=x64

if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }

# -- 5. Report location of the MSI --------------------------------------------
$msi = Get-ChildItem "$InstallerDir\bin" -Filter "*.msi" -Recurse -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($msi) {
    Write-Host "`n=== Build complete! ===" -ForegroundColor Green
    Write-Host "MSI: $($msi.FullName)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To install:    msiexec /i `"$($msi.FullName)`"" -ForegroundColor White
    Write-Host "To uninstall:  msiexec /x `"$($msi.FullName)`"" -ForegroundColor White
    Write-Host "Silent install: msiexec /i `"$($msi.FullName)`" /qn" -ForegroundColor White
} else {
    Write-Warning "Build completed but MSI not found under $InstallerDir\bin\"
}
