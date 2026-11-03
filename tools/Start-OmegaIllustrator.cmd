@echo off
setlocal
set "ASPNETCORE_URLS=http://127.0.0.1:5190"
set "APP_DIR=%~dp0Application"
if not exist "%APP_DIR%\OmegaIllustrator.Web.exe" set "APP_DIR=%~dp0..\dist\OmegaIllustrator-Ready-2026-11-03\Application"
set "APP_EXE=%APP_DIR%\OmegaIllustrator.Web.exe"
if not exist "%APP_EXE%" (
  echo Omega Illustrator executable was not found.
  echo Expected: %APP_EXE%
  pause
  exit /b 1
)
pushd "%APP_DIR%" || (
  echo Could not enter the application directory.
  pause
  exit /b 1
)
echo Starting Omega Illustrator on http://127.0.0.1:5190
echo Keep this window open while using the application.
echo.
"%APP_EXE%"
set "APP_EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %APP_EXIT_CODE%
