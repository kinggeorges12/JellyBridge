# Development Guide

## Planned Features

1. Extract the request favorites function from the sync task. The favorites function can be triggered directly from the Favorite event handler in Jellyfin. See [Issue #12](https://github.com/kinggeorges12/JellyBridge/issues/12#issuecomment-3533119223).
2. Faster refreshes that target only the metadata items that have been changed.
3. Change the smart sort to include cast and directors as criteria
4. Allow users to upload a custom picture or video for placeholder videos.
5. Fetch additional content from Jellyseerr before the built-in Jellyfin metadata refresh.

## Completed Features
1. Support for Jellyfin 10.11.\*! See [Issue #1](https://github.com/kinggeorges12/JellyBridge/issues/1).
2. Change sort order based on user preference or implement a random sort order plugin.

## Contributing

We welcome contributions! Here's how to get started:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## Prerequisites

- .NET 8.0 SDK (for Jellyfin 10.10.7)
- .NET 9.0 SDK (for Jellyfin 10.11.0)
- Jellyfin 10.10.7 or 10.11.0
- PowerShell 7 or greater (for build scripts)
- Visual Studio 2022 or VS Code (optional)

## Building the Plugin

1. **Clone the repository**
   ```bash
   git clone https://github.com/kinggeorges12/JellyBridge.git
   cd JellyBridge
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj
   ```

3. **Build the plugin**
   
   For Jellyfin 10.10.7 (net8.0):
   ```bash
   dotnet build src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj --configuration Release --warnaserror -p:JellyfinVersion=10.10.7
   ```
   
   For Jellyfin 10.11.0 (net9.0):
   ```bash
   dotnet build src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj --configuration Release --warnaserror -p:JellyfinVersion=10.11.0
   ```

## Manual Installation

After building, copy the DLL to your Jellyfin plugins folder:
1. Navigate to the build output directory based on your Jellyfin version:
   - Jellyfin 10.10.7: `src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0\`
   - Jellyfin 10.11.0: `src\Jellyfin.Plugin.JellyBridge\bin\Release\net9.0\`
2. Copy the `JellyBridge.dll` file
3. Place it in your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/`
   - Windows: `C:\ProgramData\Jellyfin\plugins\`
   - Docker: volume mount to your plugins directory
4. Restart Jellyfin

## Scripts

The project includes several useful PowerShell scripts in the `scripts` directory:

### `check-models.ps1` - Model Validation Script
**Purpose**: Validates all generated C# files for missing classes, enums, and conversion issues.

**Usage**:
```powershell
pwsh -File scripts/check-models.ps1
```

**What it checks**:
- Empty or very small files
- Missing using statements
- Unresolved class references
- Type mismatches
- Missing required enums
- Duplicate class declarations
- Duplicate properties within classes
- Invalid property names

**When to use**: After running `convert-models.ps1` to ensure the conversion was successful.

---

### `convert-models.ps1` - TypeScript to C# Conversion Script
**Purpose**: Converts TypeScript models from the Jellyseerr source code to C# classes.

**Usage**:
```powershell
pwsh -File scripts/convert-models.ps1
```

**What it does**:
- Reads TypeScript files from `codebase/seerr-main`
- Converts them to C# classes
- Applies naming conventions and type mappings
- Outputs to `src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/`
- Uses configuration from `convert-config.psd1`

**When to use**: When you need to update the Jellyseerr models (e.g., after Jellyseerr releases a new version with API changes).

---

### `convert-config.psd1` - Model Conversion Configuration
**Purpose**: Configuration file for the model conversion script.

**Setup Required**:
Before running the convert-models script, you need to have the Jellyseerr source code:
```bash
# Clone the Jellyseerr repository
git clone https://github.com/jellyseerr/jellyseerr.git codebase/seerr-main
```

**Contents**:
- Input/output directory mappings (`codebase/seerr-main/server/*` → `JellyseerrModel/*`)
- Type conversion rules (e.g., number to double patterns)
- Blocked classes (classes that are too complex to convert)
- Namespace mapping
- JSON property generation for serialization

**Note**: Edit this file when you need to adjust how TypeScript models are converted to C#.

---

### `build-branch.ps1` - Branch Release Script
**Purpose**: Builds and releases the plugin for a specific branch with automatic versioning.

**Usage**:
```powershell
pwsh -File scripts/build-branch.ps1 -Changelog "Description of changes" -Branch "feature" -ReleaseType "patch"
```

**Parameters**:
- `-Changelog` (optional): Description of changes in this release
- `-Branch` (optional, default: "feature"): Git branch to build and release
- `-ReleaseType` (optional, default: "patch"): Version increment type - "major", "minor", or "patch"
- `-Version` (optional): Specific version number (if not provided, auto-increments from latest release)

**What it does**:
- Auto-calculates version from latest release if not provided
- Builds for both Jellyfin 10.10.7 (net8.0) and 10.11.0 (net9.0)
- Creates release ZIP files with proper ABI targeting
- Updates manifest.json with new version entries
- Commits and pushes changes to the specified branch

**When to use**: For regular development releases on feature branches.

---

### `build-local.ps1` - Local Development Build Script
**Purpose**: Builds the plugin DLL for local testing and optionally copies to Docker test instance.

**Usage**:
```powershell
pwsh -File scripts/build-local.ps1 [-Clean] [-Docker] [-DockerContainerName "name"] [-DockerPluginPath "path"]
```

**Parameters**:
- `-Clean`: Clean previous builds before building
- `-Docker`: Copy DLL to Docker test instance after build
- `-DockerContainerName`: Name of Docker container (default: "test-jellyfin")
- `-DockerPluginPath`: Path to plugins directory in Docker (default: Windows Docker path)

**What it does**:
- Builds the plugin for Jellyfin 10.10.7 (net8.0)
- Optionally copies DLL to Docker test instance
- Creates meta.json for local testing

**When to use**: For local development and testing.

---

### `build-release.ps1` - Main Branch Release Script
**Purpose**: Creates an official release for the main branch with GitHub release creation.

**Usage**:
```powershell
pwsh -File scripts/build-release.ps1 -Version "1.0.0" -Changelog "Release description" [-Release]
```

**Parameters**:
- `-Version` (required): Version number in format X.Y.Z
- `-Changelog` (required): Release description
- `-Release`: Create GitHub release (default: false, creates draft)
- `-GitHubUsername` (optional): GitHub username (default: "kinggeorges12")

**What it does**:
- Builds for both Jellyfin 10.10.7 and 10.11.0
- Creates release ZIP files
- Updates manifest.json
- Creates GitHub release (draft or published)
- Commits and pushes to main branch

**When to use**: For official releases on the main branch.

---

## Project Structure

```
JellyBridge/
├── Assets/                       # Image assets
│   ├── movie.png
│   ├── S00E00.png
│   ├── season.png
│   └── show.png
├── Attributes/                   # Plugin attributes
│   └── JellyfinVersionAttribute.cs
├── BridgeModels/                 # Data models
│   ├── BridgeConfiguration.cs
│   ├── JellyMatch.cs
│   ├── JellyseerrApiResults.cs
│   └── ... (other bridge models)
├── Configuration/                # Configuration classes
│   ├── ConfigurationPage.html
│   ├── ConfigurationPage.js
│   └── PluginConfiguration.cs
├── Controllers/                  # REST API controllers
│   ├── AdvancedSettingsController.cs
│   ├── GeneralSettingsController.cs
│   ├── ImportDiscoverContentController.cs
│   ├── ManageDiscoverLibraryController.cs
│   ├── PluginConfigurationController.cs
│   ├── SortDiscoverContentController.cs
│   └── TaskStatusController.cs
├── JellyfinModels/               # Jellyfin Internal API wrapper models
│   ├── DateTimeSerialization.cs
│   ├── IJellyfinItem.cs
│   ├── JellyfinILibraryManager.cs
│   ├── JellyfinIProviderManager.cs
│   ├── JellyfinIUserDataManager.cs
│   ├── JellyfinIUserManager.cs
│   ├── JellyfinMovie.cs
│   ├── JellyfinSeries.cs
│   ├── JellyfinTaskTrigger.cs
│   ├── JellyfinUser.cs
│   ├── JellyfinWrapperResult.cs
│   └── WrapperBase.cs
├── JellyseerrModel/              # Generated API models
│   ├── Api/                      # API interface definitions
│   ├── Common/                   # Common models
│   └── Server/                   # Server models
├── Services/                     # Business logic services
│   ├── ApiService.cs
│   ├── BridgeService.cs
│   ├── CleanupService.cs
│   ├── DiscoverService.cs
│   ├── FavoriteService.cs
│   ├── LibraryService.cs
│   ├── MetadataService.cs
│   ├── PlaceholderVideoGenerator.cs
│   ├── PluginServiceRegistrator.cs
│   ├── SortService.cs
│   └── SyncService.cs
├── Tasks/                        # Scheduled tasks
│   ├── SortTask.cs
│   ├── StartupTask.cs
│   └── SyncTask.cs
├── Utils/                        # Utility classes
│   ├── DebugLogger.cs
│   ├── FolderUtils.cs
│   └── JellyBridgeJsonSerializer.cs
├── JellyBridge.csproj           # Project file
├── Plugin.cs                     # Main plugin class
└── manifest.json                 # Plugin manifest (in project root)
```

## Dependencies

This plugin uses:
- **.NET 8.0** - Target framework for Jellyfin 10.10.7
- **.NET 9.0** - Target framework for Jellyfin 10.11.0
- **Jellyfin 10.10.7 or 10.11.0** - Plugin SDK packages (version-specific)
- **ASP.NET Core** - For API endpoints
- **Microsoft.Extensions** (8.0.1 for net8.0, 9.0.10 for net9.0) - For dependency injection and logging
- **Newtonsoft.Json** - For JSON serialization

The project automatically selects the correct target framework and package versions based on the `JellyfinVersion` build property.

