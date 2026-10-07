# Compila o Cercadinhos e instala (ou atualiza) como aplicativo do Windows, so para este usuario:
# copia para %LocalAppData%\Programs\Cercadinhos, cria o atalho no Menu Iniciar e registra em "Aplicativos instalados".
# Sem acentos de proposito: o PowerShell 5.1 le .ps1 sem BOM como ANSI.
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$destino = Join-Path $env:LOCALAPPDATA 'Programs\Cercadinhos'
$exe = Join-Path $destino 'Cercadinhos.exe'

Write-Host 'Compilando...'
Push-Location $raiz
try {
    Get-Process Cercadinhos -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    dotnet publish -c Release -o app --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Deu erro ao compilar.' }

    Write-Host 'Instalando...'
    New-Item -ItemType Directory -Force $destino | Out-Null
    Copy-Item 'app\Cercadinhos.exe' $destino -Force
    Copy-Item 'assets\Cercadinhos.ico' $destino -Force
    Copy-Item 'ferramentas\desinstalar.ps1' $destino -Force
}
finally { Pop-Location }

# atalho no Menu Iniciar
$atalho = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Cercadinhos.lnk'
$shell = New-Object -ComObject WScript.Shell
$a = $shell.CreateShortcut($atalho)
$a.TargetPath = $exe
$a.WorkingDirectory = $destino
$a.IconLocation = "$exe,0"
$a.Description = 'Caixinhas para organizar a area de trabalho'
$a.Save()

# aparece em Configuracoes > Aplicativos instalados, com Desinstalar
$versao = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $versao) { $versao = '1.0.0' }
$chave = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Cercadinhos'
New-Item $chave -Force | Out-Null
Set-ItemProperty $chave DisplayName 'Cercadinhos'
Set-ItemProperty $chave DisplayVersion ($versao -replace '\+.*$', '')
Set-ItemProperty $chave Publisher 'Cercadinhos'
Set-ItemProperty $chave DisplayIcon (Join-Path $destino 'Cercadinhos.ico')
Set-ItemProperty $chave InstallLocation $destino
Set-ItemProperty $chave UninstallString ('powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $destino 'desinstalar.ps1') + '"')
Set-ItemProperty $chave NoModify 1 -Type DWord
Set-ItemProperty $chave NoRepair 1 -Type DWord
Set-ItemProperty $chave EstimatedSize ([int]((Get-Item $exe).Length / 1KB)) -Type DWord

Start-Process $exe
Write-Host "Pronto! Instalado em $destino e aberto."
