param(
    [string]$source_username,
    [string]$source_password,
    [string]$target_hostname,
    [string]$target_username,
    [string]$target_password,
    [string]$target_database,
    [string]$target_port
)

# Load MySql.Data.dll assembly
Add-Type -Path "C:\Program Files (x86)\MySQL\MySQL Connector NET 8.0.32\Assemblies\v4.5.2\MySql.Data.dll"

# Set the source (SQL Server) and target (MySQL) database information
$source_hostname = "CERNSQL21"
$source_database = "RemoteDesktop"

# Get the list of tables from the source database
$tables = Invoke-Sqlcmd -ServerInstance $source_hostname -Database $source_database -Query "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"

# Create a connection to the target MySQL database
$target_conn = New-Object MySql.Data.MySqlClient.MySqlConnection("Server=$target_hostname;Port=$target_port;Uid=$target_username;Pwd=$target_password;Database=$target_database;")
$target_conn.Open()

foreach ($table in $tables) {
    $tableName = $table.TABLE_NAME
    if ($tableName -ne "RAP" -and $tableName -ne "RAP_Resource") {
        continue
    }

    $source_data = Invoke-Sqlcmd -ServerInstance $source_hostname -Database $source_database -Query "SELECT * FROM $($tableName)"

    foreach ($row in $source_data) {
        # Check if the row exists in the target MySQL table
        
        if ($tableName -eq "RAP") {
            $name = $row.name
            $exist_query = "SELECT COUNT(*) FROM ``$tableName`` WHERE ``name`` = '$name'"
        } else {
            $name = $row.RAPName
            $exist_query = "SELECT COUNT(*) FROM ``$tableName`` WHERE ``RAPName`` = '$name'"
        }
        $cmd = New-Object MySql.Data.MySqlClient.MySqlCommand($exist_query, $target_conn)
        $exist = $cmd.ExecuteScalar()

        # If it doesn't exist, insert it
        if ($exist -eq 0) {
            $columns = ($row | Get-Member -MemberType Properties).Name
            $values = $columns | ForEach-Object { $row.$_ }
            $escaped_values = @()

            # Escape each value in $values and add to $escaped_values
            # foreach ($value in $values) {
            for (($i = 0), ($v=0); $i -lt $columns.Count; ($i++), ($v++)) {
                $columnName = $columns[$i]
                $value = $values[$v]

                if ($columnName -eq "resourceOwner") {
                    $value = $value.Substring(0, 4) + '\' + $value.Substring(4)
                }
                if ($columnName -eq "lastModified" -and $value -is [byte]) {
                    $byteArray = [byte[]]::new(8)
                    for ($j = 0; $j -lt 8; $j++) {
                        $byteArray[7 - $j] = [byte]$values[$i + $j]
                    }
                    $longValue = [BitConverter]::ToInt64($byteArray, 0)
                    try {
                        #$timestamp_diff = [DateTime]::FromFileTimeUtc($longValue).ToString("yyyy-MM-dd HH:mm:ss.fff")
                        $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
                        #$sum = $timestamp1.Add($timestamp_diff)
                    } catch {
                        Write-Host "Invalid FileTime value: $longValue"
                        $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff") # Set a default value for the timestamp
                    }
                    $escaped_values += "'$timestamp'"
                    $v += 7
                } elseif (($columnName -eq "createDate" -or $columnName -eq "updateDate") -and ($value -ne "NULL" -and $value -ne [System.DBNull]::Value)) {
                    try{
                        $value = ($value).ToString("yyyy-MM-dd HH:mm:ss.fffffff")
                        $timestamp = [DateTime]::ParseExact($value, "yyyy-MM-dd HH:mm:ss.fffffff", $null)
                        $escaped_values += "'" + $timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") + "'" 
                    } catch {
                        Write-Host "Invalid FileTime value: $longValue"
                        $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff") # Set a default value for the timestamp
                        $escaped_values += "'$timestamp'"
                    }
                } elseif ($value -eq $null) {
                    $escaped_values += "NULL"
                } elseif ($columnName -eq "enabled" -or $columnName -eq "synchronized") {
                     $bitValue = if ($value -eq "True") { 1 } else { 0 }
                    $escaped_values += $bitValue
                } else {
                    if ($value -eq 'True' -or $value -eq $True){
                        $escaped_values += 1
                    } elseif ($value -eq 'False' -or $value -eq $False){
                        $escaped_values += 0
                    } elseif (($columnName -eq "createDate" -or $columnName -eq "updateDate") -and $value -eq [System.DBNull]::Value){
                        $escaped_values += 'NULL'
                    } else{
                        $escaped_values += "'" + ($value -replace "'", "''") + "'"
                    }
                }



               # $escaped_value = [MySql.Data.MySqlClient.MySqlHelper]::EscapeString("$value")
               # $escaped_values += "'$escaped_value'"
            }
    
            # Add toDelete = false if the tableName is RAP

            $escaped_values += "false"
            $column_names = $columns + "toDelete"


            $insert_query = "INSERT INTO $($tableName) (" + ($column_names -join ", ") + ") VALUES (" + ($escaped_values -join ", ") + ")"
            $insert_cmd = New-Object MySql.Data.MySqlClient.MySqlCommand($insert_query, $target_conn)
            $insert_cmd.ExecuteNonQuery()
            $insert_query
        }

    }
}
$target_conn.Close()
