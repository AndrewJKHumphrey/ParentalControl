#Requires -RunAsAdministrator
$files = @("data.db", "data.db-wal", "data.db-shm")
foreach ($f in $files) {
    $path = "C:\ProgramData\ParentalControl\$f"
    if (Test-Path $path) {
        $acl = Get-Acl $path
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule("Users", "Modify", "Allow")
        $acl.AddAccessRule($rule)
        Set-Acl $path $acl
        Write-Host "Fixed: $f"
    } else {
        Write-Host "Not found: $f (will be covered by directory ACL when created)"
    }
}
Write-Host "Done."
