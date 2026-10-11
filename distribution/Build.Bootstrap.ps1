param([Parameter(Mandatory = $true)][string]$Output)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Compilateur .NET Framework Windows introuvable.' }
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output)))
& $compiler /nologo /target:winexe /optimize+ /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll "/win32manifest:$PSScriptRoot/Windows.manifest" "/out:$Output" (Join-Path $PSScriptRoot 'Empire.Bootstrap.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation du lanceur impossible.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Framework.config') -Destination ($Output + '.config')
