param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()]
    [string]$Version,
    
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()]
    [string]$Changelog,
    
    [string]$GitHubUsername = "kinggeorges12"
)

# Check PowerShell version - require PowerShell 7 or greater
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "This script requires PowerShell 7 or greater. Current version: $($PSVersionTable.PSVersion)"
    Write-Host "Please install PowerShell 7 from: https://github.com/PowerShell/PowerShell/releases" -ForegroundColor Yellow
    Write-Host "Or run with: pwsh -File scripts/publish.ps1 ..." -ForegroundColor Yellow
    exit 1
}

# Set base directory (project root) - fully resolved path
$BaseDir = Split-Path $PSScriptRoot -Parent

# Push to main project directory (assumes script is run from project root)
Push-Location $BaseDir

# GitHub API token - read from file
$GitHubToken = Get-Content "github-token.txt" -Raw | ForEach-Object { $_.Trim() }

# Ensure Version is treated as string
$Version = $Version.ToString()

# Validate version format (should be like 0.69.0.0)
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Write-Error '[X] Version must be in format X.Y.Z.W (e.g., 0.69.0.0)'
    exit 1
}

Write-Host "[~] Starting release process for version $Version" -ForegroundColor Green

# Set changelog text
if ([string]::IsNullOrWhiteSpace($Changelog)) {
    $ChangelogText = "v$Version - AUTO-GENERATED RELEASE: Automated release using publish.ps1 script. This release includes the latest fixes and improvements."
} else {
    $ChangelogText = "v$Version - $Changelog"
}

Write-Host "[~] Changelog: $ChangelogText" -ForegroundColor Cyan

# Step 1: Update version numbers in project file
Write-Host "Step 1: Updating version numbers in project file..." -ForegroundColor Yellow
$csprojPath = "src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj"
$csprojContent = Get-Content $csprojPath -Raw

# Update AssemblyVersion and FileVersion
$csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$Version</FileVersion>"

Set-Content $csprojPath -Value $csprojContent -NoNewline
Write-Host "[~] Updated version to $Version in project file" -ForegroundColor Green

# Step 2: Build the project
Write-Host "Step 2: Building the project..." -ForegroundColor Yellow
Write-Host "Running: dotnet build src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj --configuration Release --warnaserror" -ForegroundColor Cyan
$buildOutput = dotnet build src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj --configuration Release --warnaserror 2>&1
Write-Host "Build output: $buildOutput" -ForegroundColor Yellow
Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Yellow
if ($LASTEXITCODE -ne 0) {
    Write-Error "[X] Build failed!"
    exit 1
}
Write-Host "[~] Build successful" -ForegroundColor Green

# Get GMT timestamp (needed for meta.json)
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

# Step 3: Create meta.json file
# Read manifest to get plugin metadata
$manifestPath = "manifest.json"
$manifestArray = Get-Content $manifestPath -Raw | ConvertFrom-Json
$pluginInfo = $manifestArray[0]

Write-Host "Step 3: Creating meta.json..." -ForegroundColor Yellow
$metaJson = @{
    guid = $pluginInfo.guid
    name = $pluginInfo.name
    description = $pluginInfo.description
    owner = $pluginInfo.owner
    category = $pluginInfo.category
    version = $Version
    changelog = $ChangelogText
    targetAbi = "10.10.7.0"
    timestamp = $timestamp
    status = "Active"
    autoUpdate = $true
    imagePath = $pluginInfo.imageUrl
    assemblies = @("JellyBridge.dll")
} | ConvertTo-Json -Compress

$metaPath = "src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0\meta.json"
Set-Content -Path $metaPath -Value $metaJson -NoNewline
Write-Host "[~] Created meta.json" -ForegroundColor Green

# Step 4: Create ZIP file
Write-Host "Step 4: Creating release ZIP file..." -ForegroundColor Yellow
$zipPath = Join-Path $BaseDir "release\JellyBridge-$Version-DLL.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath
}
Compress-Archive -Path "src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0\JellyBridge.dll","src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0\meta.json" -DestinationPath $zipPath -Force
# Wait until the file is no longer locked
[GC]::Collect()
[GC]::WaitForPendingFinalizers()
$prevSize = 0
do {
    $newSize = (Get-Item $zipPath).Length
    Start-Sleep -Milliseconds 100
} while ($newSize -ne $prevSize -and ($prevSize = $newSize))
Start-Sleep -Seconds 1
Write-Host "[~] Created ZIP: $zipPath" -ForegroundColor Green

# Step 5: Calculate MD5 checksum
Write-Host "Step 5: Calculating MD5 checksum for ZIP file..." -ForegroundColor Yellow
$checksum = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash
Write-Host "[~] Checksum: $checksum" -ForegroundColor Green

# Step 6: Update manifest.json with checksum and timestamp
Write-Host "Step 6: Updating manifest.json with new version entry..." -ForegroundColor Yellow
$manifestPath = "manifest.json"
$manifestArray = Get-Content $manifestPath -Raw | ConvertFrom-Json

# Create new version entry with proper field ordering
$newVersion = @{
    version = $Version
    changelog = $ChangelogText
    targetAbi = "10.10.7.0"
    sourceUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/download/v$Version/JellyBridge-$Version-DLL.zip"
    checksum = $checksum
    timestamp = $timestamp
    dependencies = @()
}

# Insert new version at the beginning of versions array
$manifestArray[0].versions = @($newVersion) + $manifestArray[0].versions

# Save manifest back to file with proper formatting (force array wrapper)
$manifestArray | ConvertTo-Json -Depth 10 -AsArray | Set-Content $manifestPath -NoNewline
Write-Host "[~] Updated manifest.json with new version entry" -ForegroundColor Green

# Step 8: Commit changes to Git
Write-Host "Step 8: Committing changes to Git..." -ForegroundColor Yellow
git add .
if ($LASTEXITCODE -ne 0) {
    Write-Error "Git add failed!"
    exit 1
}

$commitMessage = "Release $ChangelogText"

git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) {
    Write-Error "Git commit failed!"
    exit 1
}
Write-Host "[~] Committed changes" -ForegroundColor Green

# Step 9: Push changes to GitHub
Write-Host "Step 9: Pushing changes to GitHub..." -ForegroundColor Yellow

# Configure git remote with token for authentication
$token = Get-Content "github-token.txt" -Raw | ForEach-Object { $_.Trim() }
if ($token) {
    git remote set-url origin "https://kinggeorges12:$token@github.com/kinggeorges12/JellyBridge.git"
}

git push
if ($LASTEXITCODE -ne 0) {
    Write-Error "[X] Git push failed!"
    exit 1
}
Write-Host "[~] Pushed to GitHub" -ForegroundColor Green

# Step 10: Create GitHub release
Write-Host "Step 10: Creating GitHub release..." -ForegroundColor Yellow
$headers = @{
    "Authorization" = "token $GitHubToken"
    "Accept" = "application/vnd.github.v3+json"
}

$releaseBody = "## v$Version - Release`n`n$ChangelogText`n`n### Changes:`n- Updated version to $Version`n- Built and packaged DLL`n- Updated manifest.json with new version entry`n- Generated checksum and timestamp`n`n### Installation:`n1. Download the JellyBridge-$Version-DLL.zip file`n2. Install through Jellyfin plugin catalog or manually place in plugins folder`n3. Restart Jellyfin to load the new version"

$body = @{
    tag_name = "v$Version"
    target_commitish = "main"
    name = "Jellyseerr Bridge v$Version"
    body = $releaseBody
    draft = $false
    prerelease = $false
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "https://api.github.com/repos/$GitHubUsername/JellyBridge/releases" -Method Post -Headers $headers -Body $body -ContentType "application/json"
    Write-Host "[~] Created GitHub release: $($response.html_url)" -ForegroundColor Green
    $releaseId = $response.id
} catch {
    Write-Error "[X] Failed to create GitHub release: $($_.Exception.Message)"
    exit 1
}

# Step 11: Upload ZIP file as release asset
Write-Host "Step 11: Uploading ZIP file as release asset..." -ForegroundColor Yellow
$uploadHeaders = @{
    "Authorization" = "token $GitHubToken"
    "Content-Type" = "application/zip"
}

$uploadUrl = "https://uploads.github.com/repos/$GitHubUsername/JellyBridge/releases/$releaseId/assets?name=JellyBridge-$Version-DLL.zip"

try {
    # Read the file as binary data
    $fileBytes = [System.IO.File]::ReadAllBytes($zipPath)
    
    # Upload as binary data
    $uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders -Body $fileBytes
    Write-Host "[~] Uploaded ZIP file: $($uploadResponse.browser_download_url)" -ForegroundColor Green
} catch {
    Write-Error "[X] Failed to upload ZIP file: $($_.Exception.Message)"
    exit 1
}

Write-Host "[!] Release v$Version completed successfully!" -ForegroundColor Green
$downloadUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/download/v$Version/JellyBridge-$Version-DLL.zip"
$releaseUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/tag/v$Version"
Write-Host "[~] Download URL: $downloadUrl" -ForegroundColor Cyan
Write-Host "[~] Release URL: $releaseUrl" -ForegroundColor Cyan

# Pop back to original directory
Pop-Location
