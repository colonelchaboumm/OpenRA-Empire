param([switch]$Integration)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$medievalDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $medievalDotnet)) { $medievalDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:ENGINE_DIR = $PSScriptRoot
$medievalBin = Join-Path $PSScriptRoot 'bin-medieval'
& $medievalDotnet build -c Release --nologo -v minimal "-p:OutputPath=$medievalBin"
if ($LASTEXITCODE -ne 0) { throw 'Compilation OpenRA echouee. Le projet requiert le SDK .NET 10.' }
& $medievalDotnet "$medievalBin\OpenRA.Utility.dll" medieval --check-yaml
if ($LASTEXITCODE -ne 0) { throw 'Validation des regles medievales echouee.' }
& $medievalDotnet test OpenRA.Test\OpenRA.Test.csproj -c Release --no-build "-p:OutputPath=$medievalBin" --filter 'FullyQualifiedName~Medieval' --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Tests des stocks ou du recrutement echoues.' }
if ($Integration) {
  $medievalResult = Join-Path $PSScriptRoot 'analysis\medieval\integration\result.json'
  if (Test-Path -LiteralPath $medievalResult) { Remove-Item -LiteralPath $medievalResult }
  & $medievalDotnet "$medievalBin\OpenRA.dll" -- Engine.EngineDir=.. Game.Mod=medieval-test Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
  if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $medievalResult)) { throw 'Le scenario d integration n a pas termine.' }
  $medievalReport = Get-Content -LiteralPath $medievalResult -Raw | ConvertFrom-Json
  if (!$medievalReport.passed) { throw ($medievalReport.errors -join '; ') }
  Write-Output 'Scenario reel OpenRA : reussi.'
}
