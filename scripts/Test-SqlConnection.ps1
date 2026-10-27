param(
    [string]$Server   = "nkecaaron.database.windows.net",
    [string]$Database = "master",
    [string]$User     = "mis",
    [string]$Password = $env:SQL_PASSWORD,
    [int]$Timeout     = 15
)

if (-not $Password) {
    $secure   = Read-Host "Password for $User" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# 1. TCP connectivity check on port 1433
Write-Host "Checking TCP $Server`:1433 ..."
$tcp = Test-NetConnection -ComputerName $Server -Port 1433 -WarningAction SilentlyContinue
if (-not $tcp.TcpTestSucceeded) {
    Write-Host "TCP 1433 unreachable. Check network / outbound firewall." -ForegroundColor Red
    exit 1
}
Write-Host "TCP OK ($($tcp.RemoteAddress))" -ForegroundColor Green

# 2. SQL login
$connStr = "Server=tcp:$Server,1433;Initial Catalog=$Database;User ID=$User;Password=$Password;" +
           "Encrypt=True;TrustServerCertificate=False;Connection Timeout=$Timeout;"

$conn = New-Object System.Data.SqlClient.SqlConnection $connStr
try {
    $conn.Open()
    Write-Host "SQL login OK" -ForegroundColor Green

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT @@VERSION AS Version, DB_NAME() AS Db, SUSER_SNAME() AS LoginName, SYSDATETIMEOFFSET() AS ServerTime"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Host "Login  : $($reader['LoginName'])"
        Write-Host "DB     : $($reader['Db'])"
        Write-Host "Time   : $($reader['ServerTime'])"
        Write-Host "Version: $(($reader['Version'] -split "`n")[0])"
    }
    $reader.Close()
}
catch [System.Data.SqlClient.SqlException] {
    Write-Host "SQL error $($_.Exception.Number): $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
finally {
    $conn.Dispose()
}
