param([Parameter(Mandatory = $true)][string]$Output, [switch]$Tests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Compilateur .NET Framework Windows introuvable.' }
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output)))
$entry = Join-Path $PSScriptRoot 'Empire.Launcher.cs'
$kind = '/target:winexe'
if ($Tests) { $entry = Join-Path $PSScriptRoot 'tests/Empire.Tests.cs'; $kind = '/target:exe' }
& $compiler /nologo $kind /optimize+ /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32manifest:$PSScriptRoot/Windows.manifest" "/out:$Output" (Join-Path $PSScriptRoot 'Empire.Update.cs') $entry
if ($LASTEXITCODE -ne 0) { throw 'Compilation du lanceur impossible.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Framework.config') -Destination ($Output + '.config')
