# Installs NuvexaDB Explorer for the current user: copy, Start Menu, .nvx association.
param(
    [string] $ExplorerExe = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExplorerExe) {
    $ExplorerExe = Join-Path $here "NuvexaDB Explorer.exe"
}
if (-not (Test-Path $ExplorerExe)) {
    throw "Explorer executable not found: $ExplorerExe"
}

$exe = (Resolve-Path $ExplorerExe).Path
$destDir = Join-Path $env:LOCALAPPDATA "Programs\NuvexaDB"
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$destExe = Join-Path $destDir "NuvexaDB Explorer.exe"
Copy-Item -Force $exe $destExe

$startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $startDir | Out-Null
$shortcut = Join-Path $startDir "NuvexaDB Explorer.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $destExe
$link.WorkingDirectory = $destDir
$link.Description = "NuvexaDB Explorer"
$link.Save()

$ext = ".nvx"
$progId = "Nuventra.NuvexaDB"
New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name "(default)" -Value $progId
New-Item -Path "HKCU:\Software\Classes\$progId" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "(default)" -Value "NuvexaDB Database"
New-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Name "(default)" -Value "`"$destExe`" `"%1`""

Write-Host "Installed $destExe"
Write-Host "Start Menu: NuvexaDB Explorer"
Write-Host "Associated $ext with Explorer."
