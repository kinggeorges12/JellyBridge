# Build Local DLL Script for JellyBridge Plugin
# This script builds the plugin DLL for local installation and manages Docker test instance

param(
    [switch]$Clean,
    [switch]$Verbose,
    [switch]$Docker,
    [string]$DockerContainerName = "test-jellyfin",
    [string]$DockerPluginPath = "C:\Docker-Test\Jellyfin\.config\plugins"
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Get script directory and project root
$ScriptDir = Split-Path $PSScriptRoot -Parent
$ProjectRoot = $ScriptDir

# Set working directory to project root
Push-Location $ProjectRoot

try {
    Write-Host "=== JellyBridge Plugin Local Build ===" -ForegroundColor Green
    Write-Host "Project Root: $ProjectRoot" -ForegroundColor Cyan
    
    # Define paths
    $ProjectPath = "src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj"
    $OutputDir = "src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0"
    $ManifestPath = "manifest.json"
    
    # Step 1: Clean previous builds if requested
    if ($Clean) {
        Write-Host "`nStep 1: Cleaning previous builds..." -ForegroundColor Yellow
        if (Test-Path $OutputDir) {
            Remove-Item $OutputDir -Recurse -Force
            Write-Host "✓ Cleaned output directory" -ForegroundColor Green
        }
        
        # Clean obj directory
        $ObjDir = "src\Jellyfin.Plugin.JellyBridge\obj"
        if (Test-Path $ObjDir) {
            Remove-Item $ObjDir -Recurse -Force
            Write-Host "✓ Cleaned obj directory" -ForegroundColor Green
        }
    }
    
    # Step 2: Restore dependencies
    Write-Host "`nStep 2: Restoring dependencies..." -ForegroundColor Yellow
    $restoreOutput = dotnet restore $ProjectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to restore dependencies: $restoreOutput"
        exit 1
    }
    Write-Host "✓ Dependencies restored" -ForegroundColor Green
    
    # Step 3: Read manifest and get version info
    Write-Host "`nStep 3: Reading manifest and version info..." -ForegroundColor Yellow
    
    if (-not (Test-Path $ManifestPath)) {
        Write-Error "Manifest file not found: $ManifestPath"
        exit 1
    }
    
    # Read manifest to get plugin metadata
    $manifestArray = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $pluginInfo = $manifestArray[0]
    
    # Get current version from project file
    $csprojContent = Get-Content $ProjectPath -Raw
    $versionMatch = [regex]::Match($csprojContent, '<AssemblyVersion>([^<]*)</AssemblyVersion>')
    $version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "1.0.0.0" }
    
    Write-Host "✓ Plugin version: $version" -ForegroundColor Green
    
    # Step 4: Build the project for both target ABIs
    Write-Host "`nStep 4: Building the project for both target ABIs..." -ForegroundColor Yellow
    
    # Define target ABIs and their corresponding frameworks
    $targetABIs = @(
        @{ Version = "10.10.7"; Framework = "net8.0"; OutputDir = "src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0" },
        @{ Version = "10.11.0"; Framework = "net9.0"; OutputDir = "src\Jellyfin.Plugin.JellyBridge\bin\Release\net9.0" }
    )
    
    $buildResults = @()
    
    foreach ($target in $targetABIs) {
        Write-Host "`nBuilding for Jellyfin $($target.Version) ($($target.Framework))..." -ForegroundColor Cyan
        
        $buildArgs = @(
            "build", 
            $ProjectPath, 
            "--configuration", "Release", 
            "--warnaserror",
            "-p:JellyfinVersion=$($target.Version)"
        )
        
        if ($Verbose) {
            $buildArgs += "--verbosity", "detailed"
        }
        
        $buildOutput = & dotnet $buildArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed for Jellyfin $($target.Version): $buildOutput"
            exit 1
        }
        
        Write-Host "✓ Build successful for Jellyfin $($target.Version)" -ForegroundColor Green
        
        # Store build result for later use
        $buildResults += @{
            Version = $target.Version
            Framework = $target.Framework
            OutputDir = $target.OutputDir
            DllPath = Join-Path $target.OutputDir "JellyBridge.dll"
            MetaPath = Join-Path $target.OutputDir "meta.json"
        }
    }
    
    # Step 4: Verify DLLs were created and create meta.json files
    Write-Host "`nStep 4: Verifying builds and creating meta.json files..." -ForegroundColor Yellow
    
    foreach ($result in $buildResults) {
        # Verify DLL was created
        if (-not (Test-Path $result.DllPath)) {
            Write-Error "DLL not found at expected location: $($result.DllPath)"
            exit 1
        }
        
        # Get DLL info
        $DllInfo = Get-Item $result.DllPath
        Write-Host "✓ DLL created for $($result.Version): $($DllInfo.Name) ($([math]::Round($DllInfo.Length / 1KB, 2)) KB)" -ForegroundColor Green
        
        # Create meta.json for this version
        $metaJson = @{
            guid = $pluginInfo.guid
            name = $pluginInfo.name
            description = $pluginInfo.description
            owner = $pluginInfo.owner
            category = $pluginInfo.category
            version = $version
            changelog = "Local build - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            targetAbi = $result.Version
            timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
            status = "Active"
            autoUpdate = $false
            assemblies = @("JellyBridge.dll")
        } | ConvertTo-Json -Compress
        
        Set-Content -Path $result.MetaPath -Value $metaJson -NoNewline
        Write-Host "✓ Created meta.json for $($result.Version)" -ForegroundColor Green
    }
    
    # Step 6: Docker management (if requested)
    if ($Docker) {
        Write-Host "`nStep 6: Managing Docker container..." -ForegroundColor Yellow
        
        # Check if Docker is running
        try {
            $dockerVersion = docker --version 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Docker is not available or not running. Skipping Docker operations."
            } else {
                Write-Host "✓ Docker is available: $dockerVersion" -ForegroundColor Green
                
                # Stop the container
                Write-Host "Stopping container '$DockerContainerName'..." -ForegroundColor Cyan
                $stopOutput = docker stop $DockerContainerName 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "✓ Container stopped" -ForegroundColor Green
                } else {
                    Write-Warning "Container stop failed or container not running: $stopOutput"
                }
                
                # Create plugin directory if it doesn't exist
                if (-not (Test-Path $DockerPluginPath)) {
                    Write-Host "Creating plugin directory: $DockerPluginPath" -ForegroundColor Cyan
                    New-Item -ItemType Directory -Path $DockerPluginPath -Force | Out-Null
                    Write-Host "✓ Plugin directory created" -ForegroundColor Green
                }
                
                # Copy DLL and meta.json to Docker plugin directory (only 10.11.0)
                Write-Host "Copying files to Docker plugin directory..." -ForegroundColor Cyan
                
                # Find the 10.11.0 build result
                $target1011 = $buildResults | Where-Object { $_.Version -eq "10.11.0" }
                if (-not $target1011) {
                    Write-Error "Jellyfin 10.11.0 build not found!"
                    exit 1
                }
                
                # Create plugin directory for 10.11.0
                $versionedPath = Join-Path $DockerPluginPath "JellyBridge_$($target1011.Version)"
                if (-not (Test-Path $versionedPath)) {
                    New-Item -ItemType Directory -Path $versionedPath -Force | Out-Null
                    Write-Host "Created directory: $versionedPath" -ForegroundColor Gray
                }
                
                Copy-Item $target1011.DllPath $versionedPath -Force
                Copy-Item $target1011.MetaPath $versionedPath -Force
                Write-Host "✓ Copied files for $($target1011.Version) to $versionedPath" -ForegroundColor Green
                
                # Start the container
                Write-Host "Starting container '$DockerContainerName'..." -ForegroundColor Cyan
                $startOutput = docker start $DockerContainerName 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "✓ Container started" -ForegroundColor Green
                    
                    # Wait a moment for container to fully start
                    Write-Host "Waiting for container to fully start..." -ForegroundColor Cyan
                    Start-Sleep -Seconds 3
                    
                    # Check container status
                    $statusOutput = docker ps --filter "name=$DockerContainerName" --format "table {{.Names}}\t{{.Status}}" 2>&1
                    Write-Host "Container status:" -ForegroundColor Cyan
                    Write-Host $statusOutput -ForegroundColor White
                } else {
                    Write-Error "Failed to start container: $startOutput"
                }
            }
        } catch {
            Write-Warning "Docker operations failed: $($_.Exception.Message)"
        }
    }
    
    # Step 7: Show installation instructions
    Write-Host "`n=== BUILD COMPLETE ===" -ForegroundColor Green
    Write-Host "Built for both Jellyfin versions:" -ForegroundColor Cyan
    foreach ($result in $buildResults) {
        Write-Host "  - Jellyfin $($result.Version) ($($result.Framework)): $($result.OutputDir)" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "Files created for each version:" -ForegroundColor Yellow
    Write-Host "  - JellyBridge.dll" -ForegroundColor White
    Write-Host "  - meta.json" -ForegroundColor White
    Write-Host ""
    Write-Host "Installation paths:" -ForegroundColor Yellow
    if ($Docker) {
        Write-Host "  Docker:  $DockerPluginPath" -ForegroundColor Green
        Write-Host "    └── JellyBridge_10.11.1/ (✓ Files copied)" -ForegroundColor Green
    } else {
        Write-Host "  Windows: C:\ProgramData\Jellyfin\plugins\" -ForegroundColor White
        Write-Host "  Linux:   /var/lib/jellyfin/plugins/" -ForegroundColor White
        Write-Host "  Docker:  Mount to your plugins volume" -ForegroundColor White
    }
    Write-Host ""
    if ($Docker) {
        Write-Host "Docker container '$DockerContainerName' has been restarted with Jellyfin 10.11.0 plugin." -ForegroundColor Green
    } else {
        Write-Host "After copying files, restart Jellyfin to load the plugin." -ForegroundColor Cyan
    }
    
    # Show file sizes for both versions
    Write-Host ""
    Write-Host "File sizes:" -ForegroundColor Yellow
    foreach ($result in $buildResults) {
        $DllSize = [math]::Round((Get-Item $result.DllPath).Length / 1KB, 2)
        $MetaSize = [math]::Round((Get-Item $result.MetaPath).Length / 1KB, 2)
        Write-Host "  Jellyfin $($result.Version):" -ForegroundColor Cyan
        Write-Host "    JellyBridge.dll: $DllSize KB" -ForegroundColor White
        Write-Host "    meta.json: $MetaSize KB" -ForegroundColor White
    }
    
} catch {
    Write-Error "Build failed with error: $($_.Exception.Message)"
    exit 1
} finally {
    # Return to original directory
    Pop-Location
}
