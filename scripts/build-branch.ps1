param(
    [string]$Version,
    
    [string]$Changelog = "Automated branch release",
    
    [string]$GitHubUsername = "kinggeorges12",
    
    [string]$Branch = "feature",
    
    [ValidateSet("major", "minor", "patch")]
    [string]$ReleaseType = "patch"
)

# Check PowerShell version - require PowerShell 7 or greater
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "This script requires PowerShell 7 or greater. Current version: $($PSVersionTable.PSVersion)"
    Write-Host "Please install PowerShell 7 from: https://github.com/PowerShell/PowerShell/releases" -ForegroundColor Yellow
    Write-Host "Or run with: pwsh -File scripts/publish.ps1 ..." -ForegroundColor Yellow
    exit 1
}

if($Branch -eq "main") {
    Write-Error '[X] main branch is not allowed to be released'
    exit 1
}

# Set base directory (project root) - fully resolved path
$BaseDir = Split-Path $PSScriptRoot -Parent

# Where to save zip files
$releaseDir = Join-Path $BaseDir "release"

# Push to main project directory (assumes script is run from project root)
Push-Location $BaseDir

# GitHub API token - read from file
$GitHubToken = Get-Content "github-token.txt" -Raw | ForEach-Object { $_.Trim() }

# Auto-determine version if not provided
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "[~] Version not provided, calculating from latest release..." -ForegroundColor Yellow
    
    # Find latest version from release folder
    $releaseFiles = Get-ChildItem ".\release\*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    $latestVersion = "1.0.0"
    
    if ($releaseFiles.Count -gt 0) {
        # Extract version from filename (format: JellyBridge-X.Y.Z.10.zip or JellyBridge-X.Y.Z.11.zip)
        # Remove .10 or .11 suffix as those are Jellyfin version suffixes
        $versionRegex = 'JellyBridge-(\d+\.\d+\.\d+)\.(?:10|11)\.zip'
        $versions = @()
        
        foreach ($file in $releaseFiles) {
            if ($file.Name -match $versionRegex) {
                $baseVersion = $matches[1]
                if ($versions -notcontains $baseVersion) {
                    $versions += $baseVersion
                }
            }
        }
        
        if ($versions.Count -gt 0) {
            # Sort versions numerically (not alphabetically)
            $sortedVersions = $versions | ForEach-Object {
                $parts = $_ -split '\.'
                [PSCustomObject]@{
                    Version = $_
                    Major = [int]$parts[0]
                    Minor = [int]$parts[1]
                    Patch = [int]$parts[2]
                }
            } | Sort-Object -Property Major, Minor, Patch -Descending
            
            $latest = $sortedVersions[0]
            
            # Increment version based on release type
            $major = $latest.Major
            $minor = $latest.Minor
            $patch = $latest.Patch
            
            switch ($ReleaseType) {
                "major" {
                    $major++
                    $minor = 0
                    $patch = 0
                }
                "minor" {
                    $minor++
                    $patch = 0
                }
                "patch" {
                    $patch++
                }
            }
            
            $latestVersion = "$major.$minor.$patch"
            Write-Host "[~] Latest release found: $($latest.Version), incrementing $ReleaseType -> $latestVersion" -ForegroundColor Green
        }
    } else {
        Write-Host "[~] No existing releases found, using default version: $latestVersion" -ForegroundColor Green
    }
    
    $Version = $latestVersion
}

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

# Switch to the specified branch
Write-Host "[~] Switching to branch: $Branch" -ForegroundColor Yellow
git checkout $Branch
if ($LASTEXITCODE -ne 0) {
    Write-Error "[X] Failed to switch to branch: $Branch"
    exit 1
}
Write-Host "[~] Switched to branch: $Branch" -ForegroundColor Green

# Common data
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

# Always use manifest.json from the current branch
$manifestPath = "manifest.json"

$manifestArray = Get-Content $manifestPath -Raw | ConvertFrom-Json
$pluginInfo = $manifestArray[0]

# Ensure release directory exists
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
    $ver_sub = $Version + "." + $t.SubVersion

    # Step 1: Update version numbers in project file
    Write-Host "Step 1: Updating version numbers in project file..." -ForegroundColor Yellow
    $csprojPath = "src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj"
    $csprojContent = Get-Content $csprojPath -Raw

    # Update AssemblyVersion and FileVersion
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$ver_sub</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$ver_sub</FileVersion>"

    Set-Content $csprojPath -Value $csprojContent -NoNewline
    Write-Host "[~] Updated version to $ver_sub in project file" -ForegroundColor Green

    ## Step 2: Build, package and register BOTH ABIs (10.10 and 10.11)
    Write-Host "Step 2: Building and packaging for Jellyfin 10.10 and 10.11..." -ForegroundColor Yellow

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
        sourceUrl = "https://raw.githubusercontent.com/$GitHubUsername/JellyBridge/$Branch/release/$zipName"
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

# Explicitly add all changes including release files
git add .
if ($LASTEXITCODE -ne 0) {
    Write-Error "Git add failed!"
    exit 1
}

# Verify release files are staged
$stagedReleaseFiles = git diff --cached --name-only -- "release/*.zip"
if ($stagedReleaseFiles) {
    Write-Host "[~] Staged release files:" -ForegroundColor Green
    $stagedReleaseFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Cyan }
} else {
    Write-Host "[~] No new release files to commit (may already be committed)" -ForegroundColor Yellow
}

$commitMessage = "Release $ChangelogText"

git commit -m $commitMessage
if ($LASTEXITCODE -ne 0) {
    Write-Error "Git commit failed!"
    exit 1
}
Write-Host "[~] Committed changes" -ForegroundColor Green

# Step 9: Push changes to GitHub
Write-Host "Step 9: Pushing changes to GitHub branch '$Branch'..." -ForegroundColor Yellow

# Configure git remote with token for authentication
if ($GitHubToken) {
    git remote set-url origin "https://kinggeorges12:$GitHubToken@github.com/kinggeorges12/JellyBridge.git"
}

# Push to the specified branch, set upstream if needed
Write-Host "[~] Pushing to origin/$Branch..." -ForegroundColor Cyan
git push --set-upstream origin $Branch
if ($LASTEXITCODE -ne 0) {
    Write-Error "[X] Git push failed!"
    exit 1
}
Write-Host "[~] Successfully pushed to GitHub branch '$Branch'" -ForegroundColor Green

# Pop back to original directory
Pop-Location
