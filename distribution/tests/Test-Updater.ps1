param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../.distribution/tests'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$testExe = Join-Path $OutputDirectory 'Empire.Tests.exe'
& (Join-Path $PSScriptRoot '../Build.Launcher.ps1') -Output $testExe -Tests
if ($LASTEXITCODE -ne 0) { throw 'Compilation des tests impossible.' }
& $testExe $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Tests du lanceur en echec.' }
