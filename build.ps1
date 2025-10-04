# Build script for Jellyseerr Bridge Plugin

# Clean previous builds
if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Green
dotnet restore

# Build the plugin
Write-Host "Building plugin..." -ForegroundColor Green
dotnet build --configuration Release --no-restore

# Create output directory
$outputDir = "output"
if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
New-Item -ItemType Directory -Path $outputDir

# Copy plugin files
Write-Host "Copying plugin files..." -ForegroundColor Green
Copy-Item "bin\Release\net6.0\JellyseerrBridge.dll" -Destination $outputDir
Copy-Item "manifest.json" -Destination $outputDir

# Create zip package
Write-Host "Creating plugin package..." -ForegroundColor Green
Compress-Archive -Path "$outputDir\*" -DestinationPath "JellyseerrBridge.zip" -Force

Write-Host "Build completed! Plugin package: JellyseerrBridge.zip" -ForegroundColor Green
