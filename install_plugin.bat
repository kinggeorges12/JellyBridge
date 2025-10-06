@echo off
echo Installing Jellyseerr Bridge Plugin v0.19 manually...
echo.

REM Stop Jellyfin service
echo Stopping Jellyfin service...
net stop JellyfinServer
echo.

REM Create plugin directory
set PLUGIN_DIR=C:\ProgramData\Jellyfin\Server\plugins\8ecc808c-d6e9-432f-9219-b638fbfb37e6
echo Creating plugin directory: %PLUGIN_DIR%
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"
echo.

REM Copy files
echo Copying plugin files...
copy "bin\Release\net8.0\JellyseerrBridge.dll" "%PLUGIN_DIR%\"
copy "bin\Release\net8.0\JellyseerrBridge.deps.json" "%PLUGIN_DIR%\"
copy "bin\Release\net8.0\JellyseerrBridge.pdb" "%PLUGIN_DIR%\"
echo.

REM Start Jellyfin service
echo Starting Jellyfin service...
net start JellyfinServer
echo.

echo Installation complete! Check Jellyfin Dashboard -> Plugins for version 0.19.0.0
pause
