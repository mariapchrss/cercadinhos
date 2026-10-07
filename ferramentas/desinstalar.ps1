# Tira o Cercadinhos do computador. As caixinhas salvas (%AppData%\Cercadinhos) ficam,
# para voltarem se instalar de novo; os arquivos da area de trabalho nunca sao tocados.
# Sem acentos de proposito: o PowerShell 5.1 le .ps1 sem BOM como ANSI.
$destino = Join-Path $env:LOCALAPPDATA 'Programs\Cercadinhos'
Get-Process Cercadinhos -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Cercadinhos' -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Cercadinhos.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Cercadinhos' -Recurse -Force -ErrorAction SilentlyContinue
# a pasta do programa some depois que este script (que mora nela) terminar
Start-Process cmd.exe -ArgumentList ('/c timeout /t 2 /nobreak >nul & rmdir /s /q "' + $destino + '"') -WindowStyle Hidden
