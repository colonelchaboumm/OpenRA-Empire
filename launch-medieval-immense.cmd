@echo off
setlocal
cd /d "%~dp0"
echo.
echo Les grandes terres medievales
echo.
echo [1] Les Grands Meandres   - 256 x 512  - 16 fois le village
echo [2] Les Marches Boisees  - 384 x 768  - 36 fois le village
echo [3] Les Terres sans Fin  - 512 x 1024 - 64 fois le village
echo.
choice /C 123 /N /M "Choisir la carte (1, 2 ou 3) : "
set "MEDIEVAL_MAP_CHOICE=%ERRORLEVEL%"
set "MEDIEVAL_LARGE_MAP=grands-meandres"
if "%MEDIEVAL_MAP_CHOICE%"=="2" set "MEDIEVAL_LARGE_MAP=marches-boisees"
if "%MEDIEVAL_MAP_CHOICE%"=="3" set "MEDIEVAL_LARGE_MAP=terres-sans-fin"
call "%~dp0launch-medieval.cmd" Launch.Map=%MEDIEVAL_LARGE_MAP% %*
exit /b %ERRORLEVEL%
