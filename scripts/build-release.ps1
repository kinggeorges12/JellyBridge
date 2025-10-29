param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()]
    [string]$Version,
    
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()]
    [string]$Changelog,
    
    [string]$GitHubUsername = "kinggeorges12",
    
    [switch]$IsPrerelease
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

# Validate version format (should be like 0.69.0)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error '[X] Version must be in format X.Y.Z (e.g., 1.2.3)'
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

## Step 2: Build, package and register BOTH ABIs (10.10 and 10.11)
Write-Host "Step 2: Building and packaging for Jellyfin 10.10 and 10.11..." -ForegroundColor Yellow

# Common data
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

# Choose manifest file based on prerelease flag
$manifestPath = if ($IsPrerelease) { "manifest-prerelease.json" } else { "manifest.json" }

$manifestArray = Get-Content $manifestPath -Raw | ConvertFrom-Json
$pluginInfo = $manifestArray[0]

# Ensure release directory exists
$releaseDir = Join-Path $BaseDir "release"
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

# Define targets: JellyfinVersion, MinTargetAbi, expected framework output folder
# Put these in order of Jellyfin version, from highest to lowest so users see the most recent as their compatible version.
$targets = @(
    @{ JellyfinVersion = "10.11.0"; SubVersion = "11"; MinTargetAbi = "10.11.0.0"; Framework = "net9.0"; },
    @{ JellyfinVersion = "10.10.7"; SubVersion = "10"; MinTargetAbi = "10.10.0.0"; Framework = "net8.0"; }
)

$createdZips = @()
$newManifestEntries = @()

foreach ($t in $targets) {
    $jf = $t.JellyfinVersion
    $fw = $t.Framework
    $minAbi = $t.MinTargetAbi
    $ver_sub = $Version + $t.SubVersion

    Write-Host "\n[~] Building for Jellyfin $jf ($fw)" -ForegroundColor Cyan
    $buildArgs = @(
        "build",
        "src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj",
        "--configuration", "Release",
        "--warnaserror",
        "-p:JellyfinVersion=$jf"
    )
    $buildOutput = dotnet @buildArgs 2>&1
    Write-Host "Build output: $buildOutput" -ForegroundColor DarkGray
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[X] Build failed for Jellyfin $jf"
        exit 1
    }
    Write-Host "[~] Build successful for $jf" -ForegroundColor Green

    # Paths
    $outDir = "src\Jellyfin.Plugin.JellyBridge\bin\Release\$fw"
    $dllPath = Join-Path $outDir "JellyBridge.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Error "[X] Expected DLL not found: $dllPath"
        exit 1
    }

    # Create meta.json with correct MINIMUM targetAbi
    Write-Host "[~] Creating meta.json for $jf (targetAbi=$minAbi)" -ForegroundColor Yellow
    $metaJson = @{
        guid = $pluginInfo.guid
        name = $pluginInfo.name
        description = $pluginInfo.description
        owner = $pluginInfo.owner
        category = $pluginInfo.category
        version = $ver_sub
        changelog = $ChangelogText
        targetAbi = $minAbi
        timestamp = $timestamp
        status = "Active"
        autoUpdate = $true
        assemblies = @("JellyBridge.dll")
    } | ConvertTo-Json -Compress

    $metaPath = Join-Path $outDir "meta.json"
    Set-Content -Path $metaPath -Value $metaJson -NoNewline
    Write-Host "[~] Created meta.json at $metaPath" -ForegroundColor Green

    # Zip artifact (suffix by ABI)
    $zipName = "JellyBridge-$ver_sub.zip"
    $zipPath = Join-Path $releaseDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Write-Host "[~] Creating ZIP: $zipName" -ForegroundColor Yellow
    Compress-Archive -Path $dllPath, $metaPath -DestinationPath $zipPath -Force
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); Start-Sleep -Milliseconds 200
    $createdZips += $zipPath

    # MD5 checksum
    $checksum = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash
    Write-Host "[~] Checksum ($ver_sub): $checksum" -ForegroundColor Green

    # Prepare manifest entry for this ABI
    $entry = @{
        version = $ver_sub
        changelog = "Jellyfin $($t.JellyfinVersion): $ChangelogText"
        targetAbi = $minAbi
        sourceUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/download/v$Version/$zipName"
        checksum = $checksum
        timestamp = $timestamp
        dependencies = @()
    }
    $newManifestEntries += $entry
}

# Update manifest file: prepend both entries
Write-Host "\nStep 3: Updating $manifestPath with both ABI entries..." -ForegroundColor Yellow
$manifestArray[0].versions = @($newManifestEntries) + $manifestArray[0].versions
$manifestArray | ConvertTo-Json -Depth 10 -AsArray | Set-Content $manifestPath -NoNewline
Write-Host "[~] Updated $manifestPath with multi-ABI entries" -ForegroundColor Green

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
    name = "JellyBridge v$Version"
    body = $releaseBody
    draft = $false
    prerelease = [bool]$IsPrerelease
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "https://api.github.com/repos/$GitHubUsername/JellyBridge/releases" -Method Post -Headers $headers -Body $body -ContentType "application/json"
    Write-Host "[~] Created GitHub release: $($response.html_url)" -ForegroundColor Green
    $releaseId = $response.id
} catch {
    Write-Error "[X] Failed to create GitHub release: $($_.Exception.Message)"
    exit 1
}

# Step 11: Upload both ZIP files as release assets
Write-Host "Step 11: Uploading ZIP files as release assets..." -ForegroundColor Yellow
$uploadHeaders = @{
    "Authorization" = "token $GitHubToken"
    "Content-Type" = "application/zip"
}

foreach ($zip in $createdZips) {
    $name = [System.IO.Path]::GetFileName($zip)
    $uploadUrl = "https://uploads.github.com/repos/$GitHubUsername/JellyBridge/releases/$releaseId/assets?name=$name"
    try {
        $fileBytes = [System.IO.File]::ReadAllBytes($zip)
        $uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders -Body $fileBytes
        Write-Host "[~] Uploaded ZIP file: ${name}: $($uploadResponse | ConvertTo-Json -Depth 10)" -ForegroundColor Green
    } catch {
        Write-Error "[X] Failed to upload ZIP file '$name': $($_.Exception.Message)"
        exit 1
    }
}

Write-Host "[!] Release v$Version completed successfully!" -ForegroundColor Green
$downloadUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/download/v$Version/JellyBridge-$Version-DLL.zip"
$releaseUrl = "https://github.com/$GitHubUsername/JellyBridge/releases/tag/v$Version"
Write-Host "[~] Download URL: $downloadUrl" -ForegroundColor Cyan
Write-Host "[~] Release URL: $releaseUrl" -ForegroundColor Cyan

# Pop back to original directory
Pop-Location
