# Associates .nvx with NuvexaDB Explorer for the current user.
param(
    [Parameter(Mandatory = $true)]
    [string] $ExplorerExe
)

$exe = (Resolve-Path $ExplorerExe).Path
$ext = ".nvx"
$progId = "Nuventra.NuvexaDB"

New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name "(default)" -Value $progId
New-Item -Path "HKCU:\Software\Classes\$progId" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "(default)" -Value "NuvexaDB Database"
New-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Name "(default)" -Value "`"$exe`" `"%1`""
Write-Host "Associated $ext with $exe. Open a .nvx file to launch Explorer."
