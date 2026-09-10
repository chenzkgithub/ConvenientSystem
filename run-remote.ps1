# 从 remote-cmd.txt 读取远程命令原文执行（避免任何引号转义问题）
$cmd = Get-Content "$PSScriptRoot\remote-cmd.txt" -Raw
powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\nginx-op.ps1" $cmd.Trim()
