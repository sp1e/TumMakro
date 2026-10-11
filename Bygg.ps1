$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'src\TumMakro.cs'
$binary = Join-Path $PSScriptRoot 'TumMakro.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$binary" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Bygget misslyckades.' }
Write-Output "Klart: $binary"
