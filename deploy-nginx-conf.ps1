# Push web-nginx-default.conf into the convenient-web container (immediate effect)
# and sync to the image build source /opt/convenient/nginx/nginx.conf (survives rebuild).
# Prerequisite: nginx-op.ps1 (SSH via Posh-SSH + DPAPI-stored credentials).
$conf = Get-Content "$PSScriptRoot\web-nginx-default.conf" -Raw
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($conf))
$remote = "echo $b64 | base64 -d > /tmp/default.conf.new && " +
  "docker exec convenient-web cp /etc/nginx/conf.d/default.conf /etc/nginx/conf.d/default.conf.bak && " +
  "docker cp /tmp/default.conf.new convenient-web:/etc/nginx/conf.d/default.conf && " +
  "cp /tmp/default.conf.new /opt/convenient/nginx/nginx.conf && " +
  "docker exec convenient-web nginx -t && " +
  "docker exec convenient-web nginx -s reload && echo RELOAD_OK"
powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\nginx-op.ps1" $remote
