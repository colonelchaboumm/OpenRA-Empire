@echo off
setlocal
cd /d "%~dp0"
echo Publication d'une nouvelle version d'OpenRA Empire.
echo La compilation et les tests seront effectues avant la mise en ligne.
py -3 distribution\release.py publish %*
if errorlevel 1 (
  echo.
  echo La publication n'a pas termine. Voir le message ci-dessus.
) else (
  echo.
  echo La nouvelle version est disponible pour les joueurs.
)
pause
