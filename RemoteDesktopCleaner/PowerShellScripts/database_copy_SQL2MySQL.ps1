param(
    [string]$source_username,
    [string]$source_password,
    [string]$target_hostname,
    [string]$target_username,
    [string]$target_password,
    [string]$target_database,
    [string]$target_port,
    [switch]$Add_toDelete_column
)


# Load the MySql.Data.dll assembly
Add-Type -Path "C:\Program Files (x86)\MySQL\MySQL Connector NET 8.0.32\Assemblies\v4.5.2\MySql.Data.dll"


# Set the source (SQL Server) and target (MySQL) database information
$source_hostname = "CERNSQL21"
$source_database = "RemoteDesktop"

# Get the list of tables from the source database
$tables = Invoke-Sqlcmd -ServerInstance $source_hostname -Database $source_database -Query "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"

# Create a connection to the target MySQL database
$target_conn = New-Object MySql.Data.MySqlClient.MySqlConnection("Server=$target_hostname;Port=$target_port;Uid=$target_username;Pwd=$target_password;Database=$target_database;")
$target_conn.Open()


# Function to generate and execute CREATE TABLE statement in target MySQL database
function CreateTableInMySQL($table) {
    $tableName = $table.TABLE_NAME

    $checkTableExistsQuery = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$($target_database)' AND table_name = '$($tableName)'"
    $tableExistsCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($checkTableExistsQuery, $target_conn)
    $tableExists = $tableExistsCmd.ExecuteScalar() -gt 0

    if ($tableExists) {
        $dropTableQuery = "DROP TABLE $($tableName)"
        Write-Host "Drop Table Query: $dropTableQuery"
        $dropTableCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($dropTableQuery, $target_conn)
        $dropTableCmd.ExecuteNonQuery()
    }

    $schemaQuery = "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '$($tableName)' AND TABLE_CATALOG = '$($source_database)'"
    $columnsInfo = Invoke-Sqlcmd -ServerInstance $source_hostname -Database $source_database -Query $schemaQuery

    $createTableQuery = ("CREATE TABLE IF NOT EXISTS `{0}` (" -f $tableName)

    foreach ($columnInfo in $columnsInfo) {
        $columnName = $columnInfo.COLUMN_NAME
        $dataType = $columnInfo.DATA_TYPE
        if ($dataType -eq "datetime2") {
            $dataType = "DateTime"
        }
        $charMaxLength = $columnInfo.CHARACTER_MAXIMUM_LENGTH
        $numPrecision = $columnInfo.NUMERIC_PRECISION
        $numScale = $columnInfo.NUMERIC_SCALE
        $isNullable = $columnInfo.IS_NULLABLE

        $columnDef = "`{0}` {1}" -f $columnName, $dataType


        if ($dataType -in @("char", "varchar", "nvarchar", "nchar")) {
            $columnDef += "($($charMaxLength))"
        } elseif ($dataType -in @("decimal", "numeric")) {
            $columnDef += "($($numPrecision), $($numScale))"
        }

        if ($isNullable -eq "NO") {
            $columnDef += " NOT NULL"
        }

        #$createTableQuery += $columnDef + ", "
        $createTableQuery += ("{0}, " -f $columnDef)

    }
    
    if ($Add_toDelete_column){
        $createTableQuery += "toDelete BIT NOT NULL, "
        $createTableQuery += "unsynchronizedGateways VARCHAR(255), "
    }
    $createTableQuery = $createTableQuery.TrimEnd(", ") + ")"
    Write-Host "Create Table Query: $createTableQuery" # Add this line to print the query
    $createTableCmd = New-Object MySql.Data.MySqlClient.MySqlCommand($createTableQuery, $target_conn)
    $createTableCmd.ExecuteNonQuery()
}



# Loop through each table and copy the data from the source to the target database
foreach ($table in $tables) {
    $tableName = $table.TABLE_NAME
    if ($tableName -ne "RAP" -and $tableName -ne "RAP_Resource") {
        continue
    }
    CreateTableInMySQL $table
    # Get the data from the source table
    $source_data = Invoke-Sqlcmd -ServerInstance $source_hostname -Database $source_database -Query "SELECT * FROM $($tableName)"

    # Loop through each row of data and insert it into the target table
    foreach ($row in $source_data) {
        $columns = ($row | Get-Member -MemberType Properties).Name
        $values = $columns | ForEach-Object { $row.$_ }


        $escaped_values = @()
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
        }
        if ($Add_toDelete_column){
             $escaped_values += 0
             $column_names = $columns + "toDelete"
        } else {
            $column_names = $columns
        }

        if ($Add_toDelete_column){
             $escaped_values += "''"
             $column_names = $column_names + "unsynchronizedGateways"
        }

        try{
            $insert_query = "INSERT INTO $($tableName) (" + ($column_names -join ", ") + ") VALUES (" + ($escaped_values -join ", ") + ")"
            Write-Host "Insert Query: $insert_query" # Add this line to print the query
            $cmd = New-Object MySql.Data.MySqlClient.MySqlCommand($insert_query, $target_conn)
            $cmd.ExecuteNonQuery()

        } catch {
            Write-Host "Invalid FileTime value: $longValue"
            $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff") # Set a default value for the timestamp
        }
    }
}

# Close the target MySQL database connection
$target_conn.Close()

Write-Host "Database copy complete."
