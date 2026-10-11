@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_EXE=dotnet"
if exist ".tools\dotnet\dotnet.exe" (
  set "DOTNET_EXE=%~dp0.tools\dotnet\dotnet.exe"
  set "DOTNET_ROOT=%~dp0.tools\dotnet"
)
if not exist "bin-medieval\OpenRA.Mods.Medieval.dll" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-medieval.ps1"
  if errorlevel 1 goto failed
)
"%DOTNET_EXE%" bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 %*
if errorlevel 1 goto failed
exit /b 0
:failed
echo Le lancement a echoue. Voir les journaux OpenRA et mods\medieval\README.md.
pause
exit /b 1
