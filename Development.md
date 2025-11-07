# Development Guide

## Planned Features

1. ~~Support for Jellyfin 10.11.*! See [Issue #1](https://github.com/kinggeorges12/JellyBridge/issues/1).~~
2. ~~Change sort order based on user preference or implement a random sort order plugin.~~
3. Change the smart sort to include cast and directors as criteria
4. Allow users to upload a custom picture or video for placeholder videos.
5. Fetch additional content from Jellyseerr before the built-in Jellyfin metadata refresh.

## Contributing

We welcome contributions! Here's how to get started:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## Prerequisites

- .NET 8.0 SDK
- Jellyfin 10.10.7 or later
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
   ```bash
   dotnet build src\Jellyfin.Plugin.JellyBridge\JellyBridge.csproj --configuration Release --warnaserror
   ```

## Manual Installation

After building, copy the DLL to your Jellyfin plugins folder:
1. Navigate to the build output: `src\Jellyfin.Plugin.JellyBridge\bin\Release\net8.0\`
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

## Project Structure

```
JellyBridge/
├── Assets/                       # Image assets
├── Attributes/                   # Plugin attributes
├── BridgeModels/                 # Data models
├── Configuration/                # Configuration classes
│   ├── ConfigurationPage.html
│   ├── ConfigurationPage.js
│   └── PluginConfiguration.cs
├── Controllers/                  # REST API controllers
│   └── RouteController.cs
├── JellyseerrModel/              # Generated API models
├── Services/                     # Business logic services
│   ├── ApiService.cs
│   ├── BridgeService.cs
│   ├── LibraryService.cs
│   ├── PlaceholderVideoGenerator.cs
│   ├── PluginServiceRegistrator.cs
│   └── SyncService.cs
├── Tasks/                        # Scheduled tasks
│   └── SyncTask.cs
├── Utils/                        # Utility classes
├── JellyBridge.csproj           # Project file
├── Plugin.cs                     # Main plugin class
├── manifest.json                 # Plugin manifest
└── README.md                    # This file
```

## Dependencies

This plugin uses:
- **.NET 8.0** - Target framework
- **Jellyfin 10.10.7** - Plugin SDK packages
- **ASP.NET Core** - For API endpoints
- **Microsoft.Extensions** - For dependency injection and logging
- **Newtonsoft.Json** - For JSON serialization

