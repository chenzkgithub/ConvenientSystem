$logFile = "e:\A-Chenzk\Code\MyProject\ConvenientSystem\build-log.txt"
try {
    Write-Output "Starting restore..." | Out-File $logFile -Encoding utf8
    $restoreOutput = & dotnet restore "e:\A-Chenzk\Code\MyProject\ConvenientSystem\ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj" 2>&1 | Out-String
    Write-Output "RESTORE OUTPUT:" | Out-File $logFile -Append -Encoding utf8
    Write-Output $restoreOutput | Out-File $logFile -Append -Encoding utf8
    
    Write-Output "Starting build..." | Out-File $logFile -Append -Encoding utf8
    $buildOutput = & dotnet build "e:\A-Chenzk\Code\MyProject\ConvenientSystem\ConvenientSystem.Desktop\ConvenientSystem.Desktop.csproj" --verbosity normal 2>&1 | Out-String
    Write-Output "BUILD OUTPUT:" | Out-File $logFile -Append -Encoding utf8
    Write-Output $buildOutput | Out-File $logFile -Append -Encoding utf8
} catch {
    Write-Output "ERROR: $_" | Out-File $logFile -Append -Encoding utf8
}
