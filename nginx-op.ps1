# SSH to remote server via Posh-SSH using DPAPI-stored credentials (password never printed).
# Usage: powershell -File nginx-op.ps1 "<remote command>"
#        or write the command into remote-cmd.txt and call with no args.
param(
  [Parameter(Mandatory = $false)][string]$Command
)

if (-not $Command) {
  $txt = Join-Path $PSScriptRoot 'remote-cmd.txt'
  if (Test-Path $txt) { $Command = ((Get-Content $txt -Raw) -replace "`r", '').Trim() }
}
if (-not $Command) { throw 'no remote command provided (arg or remote-cmd.txt)' }

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

# Decrypt SSH credential stored by SshCredentialStore (DPAPI CurrentUser + fixed entropy)
$credFile = Join-Path $PSScriptRoot 'ConvenientSystem.Desktop\bin\Debug\net10.0-windows\ssh-credentials.json'
$entries = Get-Content $credFile -Raw | ConvertFrom-Json
$entry = $entries | Where-Object { $_.Host -eq '123.56.68.132' -and $_.UserName -eq 'root' }
if (-not $entry) { throw 'credential not found' }

$entropy = [Text.Encoding]::UTF8.GetBytes('ConvenientSystem.SshCredential.v1')
$bytes = [Convert]::FromBase64String($entry.EncPassword)
$pw = [Text.Encoding]::UTF8.GetString(
  [Security.Cryptography.ProtectedData]::Unprotect($bytes, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser))

$sec = ConvertTo-SecureString $pw -AsPlainText -Force
$cred = [Management.Automation.PSCredential]::new('root', $sec)
$pw = $null; $sec = $null

# Execute remote command
$session = New-SSHSession -ComputerName '123.56.68.132' -Credential $cred -AcceptKey -Force
try {
  $result = Invoke-SSHCommand -SessionId $session.SessionId -Command $Command -TimeOut 120
  $result.Output
  if ($result.Error) { Write-Host '[STDERR]' $result.Error }
  exit $result.ExitStatus
} finally {
  Remove-SSHSession -SessionId $session.SessionId | Out-Null
}
