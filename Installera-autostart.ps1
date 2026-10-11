param([switch]$Avinstallera)
$ErrorActionPreference = 'Stop'
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try {
    if ($Avinstallera) {
        $runKey.DeleteValue('TumMakro', $false)
        Write-Output 'Autostarten är borttagen. TumMakro-filerna finns kvar.'
        return
    }
    $sourceExe = Join-Path $PSScriptRoot 'TumMakro.exe'
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Lägg skriptet i samma mapp som TumMakro.exe, eller bygg programmet först.' }
    $installFolder = Join-Path $env:LOCALAPPDATA 'TumMakro'
    $installedExe = Join-Path $installFolder 'TumMakro.exe'
    New-Item -ItemType Directory -Force -Path $installFolder | Out-Null
    if ([IO.Path]::GetFullPath($sourceExe) -ne [IO.Path]::GetFullPath($installedExe)) {
        Get-Process -Name TumMakro -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe } | Stop-Process
        Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
    }
    $command = '"' + $installedExe + '" --background'
    $runKey.SetValue('TumMakro', $command, [Microsoft.Win32.RegistryValueKind]::String)
    if ($runKey.GetValue('TumMakro') -ne $command) { throw 'Autostarten kunde inte verifieras.' }
    Start-Process -FilePath $installedExe -ArgumentList '--background' -WindowStyle Hidden
    Write-Output 'TumMakro startar nu i bakgrunden vid Windows-inloggning.'
} finally { $runKey.Dispose() }
