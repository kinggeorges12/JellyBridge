# JellyBridge Plugin

A Jellyfin plugin that bridges Jellyfin with Jellyseerr for seamless show discovery and download requests.

## Features

- **Automated Show Listing**: Automatically lists TV shows from various streaming services (Netflix, Prime Video, etc.) within Jellyfin as placeholders
- **Easy Downloads**: Sends download requests to Jellyseerr when you mark a show as a favorite in Jellyfin
- **Customizable**: Allows selection of streaming service shows to fetch and display in Jellyfin
- **Scheduled Sync**: Automatically syncs shows on a configurable schedule
- **Library Management**: Prevents placeholder shows from appearing in main libraries
- **Separate Libraries**: Option to create dedicated libraries for each streaming service
- **Placeholder Protection**: Uses `.nomedia` files to prevent Jellyfin from scanning incomplete shows

## Installation

### Method 1: Automatic Installation (Recommended)

1. **Add Plugin Repository to Jellyfin:**
   - Go to Jellyfin Admin Dashboard → Plugins → Catalog
   - Click "Settings" (gear icon)
   - Click "Add Repository"
   - Enter Repository URL: `https://raw.githubusercontent.com/kinggeorges12/JellyBridge/refs/heads/main/manifest.json`
   - Click "Add"

2. **Install Plugin:**
   - Go to Plugins → Catalog
   - Find "JellyBridge"
   - Click "Install"
   - Restart Jellyfin when prompted

3. **Configure the plugin** through the admin interface

### Method 2: Manual Installation

1. Download the plugin ZIP file from the [releases](../../releases)
2. Extract the contents to your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure the plugin through the web interface

## Configuration

Access the plugin configuration at: `http://your-jellyfin-server/Plugins/JellyBridge/ConfigurationPage`

The plugin provides a comprehensive web-based configuration interface with the following sections:

### 🔗 Connection Settings
- **Jellyseerr URL**: The base URL of your Jellyseerr instance (default: `http://localhost:5055`)
- **API Key**: Your Jellyseerr API key (found in Settings → General)
- **Test Connection**: Button to validate your Jellyseerr connection

### 📁 Library Configuration
- **Library Directory**: Path to JellyBridge's library directory (default: `/data/JellyBridge`)
- **Manage JellyBridge Library**: Automatically manage library folders (default: enabled)
- **Create Separate Libraries**: Creates dedicated libraries for each streaming service (default: disabled)
- **Library Prefix**: Prefix for streaming service libraries (default: empty)
- **Exclude from Main Libraries**: Prevents placeholder shows from appearing in main libraries (default: enabled)

### ⚙️ Sync Settings
- **Enable Plugin**: Enable or disable the plugin (default: disabled)
- **Sync Interval**: How often to sync shows in hours (default: 24)
- **Auto Sync on Startup**: Automatically sync when Jellyfin starts (default: enabled)
- **Startup Delay**: Seconds to wait before startup sync (default: 60)
- **Max Discover Pages**: Maximum pages to fetch from discover endpoint per network (default: 1)
- **Max Retention Days**: Days to retain content before cleanup (default: 30)
- **Region**: Watch network region ISO 3166-1 country code (default: US)

### 🌐 Network Configuration
Configure which streaming services to sync:
- **Available Networks**: Netflix, Disney Plus, Amazon Prime Video, Apple TV+, Hulu, HBO Max, and many more
- **Network Mapping**: Automatically maps service names to directories
- Select networks from the multi-select interface

### 🎮 Interactive Controls
- **💾 Save Configuration**: Saves all settings
- **🔍 Test Connection**: Validates Jellyseerr connection
- **🔄 Trigger Sync**: Manual sync trigger
- **📊 Sync Status**: Real-time status showing what's being synced
- **🧹 Recycle Library**: Delete all library data and refresh

### 🔧 Advanced Features

#### Placeholder Show Filtering
The plugin intelligently detects and filters placeholder shows (shows that are requested but don't have actual media files yet):

- **Automatic Detection**: Identifies shows with no media files (only .nfo, .txt, etc.)
- **Smart Filtering**: Excludes placeholders from main libraries when enabled
- **Streaming Libraries**: Always includes placeholders in dedicated streaming libraries
- **Configurable**: Enable/disable exclusion via the configuration interface

#### Library Management
- **Separate Libraries**: Creates dedicated libraries for each streaming service
- **Library Prefixing**: Customizable prefix for streaming service libraries
- **Path Detection**: Automatically detects library types based on directory structure
- **Content Filtering**: Filters content based on actual media file presence

### Service Configuration

Configure which streaming services to sync by providing:

- **Services to Fetch**: Comma-separated list of service names (e.g., "Netflix, Prime Video")
- **Service Directories**: JSON mapping of service names to directory paths
- **Service IDs**: JSON mapping of service names to Jellyseerr network IDs

Example service configuration:
```json
{
  "Service Directories": {
    "Netflix": "/path/to/shows/Netflix",
    "Prime Video": "/path/to/shows/Prime Video"
  },
  "Service IDs": {
    "Netflix": 213,
    "Prime Video": 1024
  }
}
```

### Advanced Settings

- **Sync Interval**: How often to sync shows (in hours)
- **Request Timeout**: Timeout for API requests in seconds (default: 60)
- **Retry Attempts**: Number of retry attempts for failed requests (default: 3)
- **Enable Debug Logging**: Enable detailed debug logging (default: disabled)
- **Placeholder Duration**: Duration of placeholder videos in seconds (default: 10)

## Usage

1. **Configure the plugin** through the web interface with your Jellyseerr credentials and directory paths
2. **Test the connection** to ensure Jellyseerr is accessible
3. **Enable the plugin** and trigger an initial sync
4. **Create separate libraries** (recommended) for each streaming service to avoid confusion
5. **Scan your Jellyfin library** to see the placeholder shows
6. **Mark shows as favorites** in Jellyfin to automatically request downloads

## Web Interface

The plugin includes a modern, responsive web interface accessible at:
`http://your-jellyfin-server/Plugins/JellyBridge/ConfigurationPage`

### Features:
- **Real-time Configuration**: Update settings without restarting Jellyfin
- **Connection Testing**: Validate Jellyseerr connection before saving
- **Manual Sync**: Trigger immediate synchronization
- **Status Feedback**: Clear success/error messages
- **Mobile Friendly**: Responsive design works on all devices

### Library Setup Recommendations

For the best experience, it's recommended to create separate libraries for streaming services:

1. **Create Streaming Libraries**: In Jellyfin Admin Dashboard → Libraries, create new libraries:
   - "Streaming - Netflix" pointing to your Netflix directory
   - "Streaming - Prime Video" pointing to your Prime Video directory
   - etc.

2. **Exclude from Main Libraries**: Ensure your main Movies and TV Shows libraries don't include the streaming service directories

3. **Use Library Validation**: The plugin provides library validation to check for conflicts and accessibility issues

## Logging

The plugin integrates with Jellyfin's logging system. Check Jellyfin logs for debugging information:

- Plugin initialization and configuration
- API calls to Jellyseerr
- Show sync operations
- Library management
- Error messages and warnings

## Troubleshooting

### Common Issues

1. **Configuration validation fails**: Ensure all required fields are filled
2. **Authentication errors**: Verify Jellyseerr credentials and URL
3. **No shows appearing**: Check network configuration and directory paths
4. **Library not updating**: Check that library management is enabled and directories are accessible

### Debug Steps

1. Check Jellyfin logs for error messages
2. Test Jellyseerr connection using the "Test Connection" button
3. Verify directory permissions for show directories
4. Ensure Jellyseerr is accessible from Jellyfin

## Development

### Prerequisites

- .NET 8.0 SDK
- Jellyfin 10.10.7 or later
- Visual Studio 2022 or VS Code (optional)

### Building the Plugin

1. **Clone the repository**
   ```bash
   git clone https://github.com/kinggeorges12/JellyBridge.git
   cd JellyBridge
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the plugin**
   ```bash
   dotnet build --configuration Release
   ```

4. **Create release package**

   Use the provided PowerShell script to create a release:
   ```powershell
   pwsh -File scripts/build-release.ps1 -Version "0.1.0.0" -Changelog "Your release description"
   ```

   This script will:
   - Update version numbers in the project file
   - Build the project in Release configuration
   - Create a ZIP file in the `release` directory
   - Generate an MD5 checksum
   - Update manifest.json with the new version
   - Commit changes to Git and push to GitHub
   - Create a GitHub release
   - Upload the ZIP file as a release asset

### GitHub Token Setup

The release script requires a GitHub token:

1. Create a file named `github-token.txt` in the project root
2. Add your GitHub Personal Access Token to the file (with `repo` scope)
3. The file is git-ignored to protect your credentials

**Get a GitHub Token:**
- Go to GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
- Generate a token with `repo` permissions
- Copy the token into `github-token.txt`

### Scripts

The project includes several useful PowerShell scripts in the `scripts` directory:

#### `build-release.ps1` - Automated Release Script
**Purpose**: Creates a complete release including building, packaging, and publishing to GitHub.

**Usage**:
```powershell
pwsh -File scripts/build-release.ps1 -Version "0.1.0.0" -Changelog "Release description"
```

**What it does**:
- Updates version numbers in the project file
- Builds the project in Release configuration
- Creates a ZIP package in the `release` directory
- Generates MD5 checksum
- Updates manifest.json with the new version
- Commits and pushes changes to Git
- Creates a GitHub release
- Uploads the ZIP file as a release asset

**Requirements**: Requires `github-token.txt` file with a GitHub Personal Access Token.

---

#### `check-models.ps1` - Model Validation Script
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

#### `convert-models.ps1` - TypeScript to C# Conversion Script
**Purpose**: Converts TypeScript models from the Jellyseerr source code to C# classes.

**Usage**:
```powershell
pwsh -File scripts/convert-models.ps1
```

**What it does**:
- Reads TypeScript files from `codebase/seerr-main`
- Converts them to C# classes
- Applies naming conventions and type mappings
- Outputs to `src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel/`
- Uses configuration from `convert-config.psd1`

**When to use**: When you need to update the Jellyseerr models (e.g., after Jellyseerr releases a new version with API changes).

---

#### `convert-config.psd1` - Model Conversion Configuration
**Purpose**: Configuration file for the model conversion script.

**Contents**:
- Input/output directory mappings
- Type conversion rules (e.g., number to double patterns)
- Blocked classes (classes that shouldn't be converted)
- Namespace settings
- JSON property naming conventions

**Note**: Edit this file when you need to adjust how TypeScript models are converted to C#.

### Version Numbering

Releases use a 4-part version format: `X.Y.Z.W`
- Increment `X` for major releases
- Increment `Y` for minor releases  
- Increment `Z` for patch releases
- Increment `W` for build numbers

### Project Structure

```
JellyBridge/
├── Controllers/                  # REST API controllers
│   └── RouteController.cs
├── Configuration/                # Configuration classes
│   ├── PluginConfiguration.cs
│   ├── ConfigurationPage.html
│   └── ConfigurationPage.js
├── Services/                     # Business logic services
│   ├── SyncService.cs
│   ├── LibraryService.cs
│   ├── BridgeService.cs
│   └── ApiService.cs
├── Tasks/                        # Scheduled tasks
│   └── SyncTask.cs
├── BridgeModels/                 # Data models
│   ├── JellyseerrMovie.cs
│   └── JellyseerrShow.cs
├── Plugin.cs                     # Main plugin class
├── JellyseerrBridge.csproj       # Project file
├── manifest.json                 # Plugin manifest
├── nuget.config                 # NuGet configuration
└── README.md                    # This file
```

### Dependencies

This plugin uses:
- **.NET 8.0** - Target framework
- **Jellyfin 10.10.7** - Plugin SDK packages
- **ASP.NET Core** - For API endpoints
- **Microsoft.Extensions** - For dependency injection and logging
- **Newtonsoft.Json** - For JSON serialization

### Development Setup

1. **Install Jellyfin development environment**
2. **Configure NuGet sources** (nuget.config is included)
3. **Build and test** using the commands above
4. **Deploy to Jellyfin** for testing

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## License

This project is open source and available under the GNU General Public License v3.0.

## Acknowledgments

Thank you to the creator of the [Overseer-Jellyfin Bridge Script](https://github.com/geekfreak21/Overseer-and-Jellyfin-Bridged) for the inspiration. Special thanks to the developers of the [**Intro Skipper**](https://github.com/intro-skipper) and [**Custom Tabs**](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) plugins for reusing their GPL-licensed code in the UI styling and configuration patterns.