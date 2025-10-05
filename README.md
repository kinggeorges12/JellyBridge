# Jellyseerr Bridge Plugin

A Jellyfin plugin that bridges Jellyfin with Jellyseerr for seamless show discovery and download requests.

## Features

- **Automated Show Listing**: Automatically lists TV shows from various streaming services (Netflix, Prime Video, etc.) within Jellyfin as placeholders
- **Easy Downloads**: Sends download requests to Jellyseerr when you mark a show as a favorite in Jellyfin
- **Customizable**: Allows selection of streaming service shows to fetch and display in Jellyfin
- **Scheduled Sync**: Automatically syncs shows on a configurable schedule
- **Webhook Integration**: Handles Jellyfin webhook events for favorite shows
- **Library Management**: Prevents placeholder shows from appearing in main libraries
- **Separate Libraries**: Option to create dedicated libraries for each streaming service
- **Placeholder Protection**: Uses `.nomedia` files to prevent Jellyfin from scanning incomplete shows

## Installation

### Method 1: Automatic Installation (Recommended)

1. **Add Plugin Repository to Jellyfin:**
   - Go to Jellyfin Admin Dashboard → Plugins → Catalog
   - Click "Settings" (gear icon)
   - Click "Add Repository"
   - Enter Repository URL: `https://raw.githubusercontent.com/kinggeorges12/Jellyseerr-Bridge/refs/heads/main/manifest.json`
   - Click "Add"

2. **Install Plugin:**
   - Go to Plugins → Catalog
   - Find "Jellyseerr Bridge"
   - Click "Install"
   - Restart Jellyfin when prompted

3. **Configure the plugin** through the admin interface

### Method 2: Manual Installation

1. Download the plugin ZIP file from the [releases](../../releases)
2. Extract the contents to your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure the plugin through the web interface

## Configuration

Access the plugin configuration at: `http://your-jellyfin-server/Plugins/JellyseerrBridge/ConfigurationPage`

The plugin provides a comprehensive web-based configuration interface with the following sections:

### 🔗 Jellyseerr Connection Settings
- **Jellyseerr URL**: The base URL of your Jellyseerr instance (e.g., `http://localhost:5055`)
- **API Key**: Your Jellyseerr API key (found in Settings → General)
- **Email**: Your Jellyseerr login email
- **Password**: Your Jellyseerr login password
- **Test Connection**: Button to validate your Jellyseerr connection

### 📁 Library Configuration
- **Library Directory**: Path to Jellyseerr's library directory (default: `/data/Jellyseerr`)
- **Create Separate Libraries**: Creates dedicated libraries for each streaming service
- **Library Prefix**: Prefix for streaming service libraries (default: `Streaming - `)
- **Exclude from Main Libraries**: Prevents placeholder shows from appearing in main libraries

### ⚙️ Plugin Settings
- **Enable Plugin**: Enable or disable the plugin
- **Sync Interval**: How often to sync shows (1-168 hours, default: 24)
- **Webhook Port**: Port for webhook events (1024-65535, default: 5000)
- **Jellyseerr User ID**: Jellyseerr user ID for making requests (found in Jellyseerr user management)

### 🎮 Interactive Controls
- **💾 Save Configuration**: Saves all settings
- **🔍 Test Connection**: Validates Jellyseerr connection
- **🔄 Trigger Sync**: Manual sync trigger
- **Real-time Status**: Success/error messages with auto-dismiss

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
- **Webhook Port**: Port for webhook handling
- **User ID**: Jellyseerr user ID for requests
- **Root Folder**: Download destination folder
- **Request 4K**: Whether to request 4K content
- **Create Separate Libraries**: Create dedicated libraries for each streaming service
- **Library Prefix**: Prefix for streaming service library names (e.g., "Streaming - ")
- **Exclude from Main Libraries**: Prevent placeholder shows from appearing in main Movies/TV libraries

## Usage

1. **Configure the plugin** through the web interface with your Jellyseerr credentials and directory paths
2. **Test the connection** to ensure Jellyseerr is accessible
3. **Enable the plugin** and trigger an initial sync
4. **Create separate libraries** (recommended) for each streaming service to avoid confusion
5. **Scan your Jellyfin library** to see the placeholder shows
6. **Mark shows as favorites** in Jellyfin to automatically request downloads

## Web Interface

The plugin includes a modern, responsive web interface accessible at:
`http://your-jellyfin-server/Plugins/JellyseerrBridge/ConfigurationPage`

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

## Webhook Setup

To enable automatic download requests when marking shows as favorites:

1. Go to Jellyfin Admin Dashboard
2. Navigate to "Notifications" → "Webhooks"
3. Add a new webhook with URL: `http://your-jellyfin-server:5000/Plugins/JellyseerrBridge/Webhook`
4. Select "Item Added to Favorites" as the notification type

## Logging

The plugin integrates with Jellyfin's logging system. Check Jellyfin logs for debugging information:

- Plugin initialization and configuration
- API calls to Jellyseerr
- Show sync operations
- Webhook event handling
- Error messages and warnings

## Troubleshooting

### Common Issues

1. **Configuration validation fails**: Ensure all required fields are filled
2. **Authentication errors**: Verify Jellyseerr credentials and URL
3. **No shows appearing**: Check service IDs and directory paths
4. **Webhook not working**: Verify webhook URL and Jellyfin webhook configuration

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
   git clone https://github.com/yourusername/Jellyseerr-Bridge.git
   cd Jellyseerr-Bridge
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
   ```powershell
   # Create release directory (if it doesn't exist)
   if (-not (Test-Path release\JellyseerrBridge)) { mkdir release\JellyseerrBridge }
   
   # Copy built files
   copy bin\Release\net8.0\JellyseerrBridge.dll release\JellyseerrBridge\
   copy bin\Release\net8.0\JellyseerrBridge.deps.json release\JellyseerrBridge\
   copy manifest.json release\JellyseerrBridge\
   
   # Create ZIP package (overwrites existing)
   Compress-Archive -Path release\JellyseerrBridge\* -DestinationPath release\JellyseerrBridge-0.2.zip -Force
   ```

### Publishing to GitHub Packages

1. **Create a GitHub Personal Access Token (Classic)**
   - Go to GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
   - Generate a token with `write:packages` and `read:packages` permissions
   - Copy the token for use in configuration

2. **Configure nuget.config**
   Copy the template and add your credentials:
   ```bash
   # Copy the template
   copy nuget.config.template nuget.config
   ```
   
   Then edit `nuget.config` with your details:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
       <packageSources>
           <clear />
           <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
           <add key="github" value="https://nuget.pkg.github.com/NAMESPACE/index.json" />
       </packageSources>
       <packageSourceCredentials>
           <github>
               <add key="Username" value="USERNAME" />
               <add key="ClearTextPassword" value="TOKEN" />
           </github>
       </packageSourceCredentials>
   </configuration>
   ```
   
   Replace:
   - `NAMESPACE` with your GitHub username or organization name
   - `USERNAME` with your GitHub username
   - `TOKEN` with your personal access token
   
   **⚠️ Security Note:** The `nuget.config` file is ignored by git to protect your credentials. Only the template (`nuget.config.template`) is committed.

3. **Pack the plugin**
   ```bash
   dotnet pack --configuration Release
   ```

4. **Publish to GitHub Packages**
   ```bash
   dotnet nuget push bin\Release\JellyseerrBridge.0.1.0.nupkg --source github
   ```

### Installing from GitHub Packages

#### For Jellyfin Users (Automatic Installation)
Add the plugin repository to Jellyfin:
1. **Jellyfin Admin Dashboard** → **Plugins** → **Catalog** → **Settings** (gear icon)
2. **Add Repository URL:** `https://raw.githubusercontent.com/kinggeorges12/Jellyseerr-Bridge/refs/heads/main/manifest.json`
3. **Install** "Jellyseerr Bridge" from the catalog

#### For Developers (Manual Installation)
Configure your `nuget.config` for development:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
        <add key="github" value="https://nuget.pkg.github.com/kinggeorges12/index.json" />
    </packageSources>
    <packageSourceCredentials>
        <github>
            <add key="Username" value="kinggeorges12" />
            <add key="ClearTextPassword" value="YOUR_GITHUB_TOKEN" />
        </github>
    </packageSourceCredentials>
</configuration>
```

Then install the package:
```bash
dotnet add package JellyseerrBridge.Plugin --source github
```

### GitHub Actions Integration

For automated publishing in GitHub Actions, use the `GITHUB_TOKEN`:

```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v3
  with:
    dotnet-version: '8.0.x'

- name: Add GitHub Packages source
  run: |
    dotnet nuget add source --username ${{ github.actor }} --password ${{ secrets.GITHUB_TOKEN }} --store-password-in-clear-text --name github "https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json"

- name: Pack
  run: dotnet pack --configuration Release

- name: Push to GitHub Packages
  run: dotnet nuget push bin/Release/*.nupkg --source github
```

### Project Structure

```
Jellyseerr-Bridge/
├── Api/                          # REST API controllers
│   ├── ConfigurationController.cs
│   ├── ConfigurationPageController.cs
│   └── WebhookController.cs
├── Configuration/                 # Configuration classes
│   └── PluginConfiguration.cs
├── Services/                      # Business logic services
│   ├── ConfigurationService.cs
│   ├── JellyseerrApiService.cs
│   ├── LibraryManagementService.cs
│   ├── ShowSyncService.cs
│   └── WebhookHandlerService.cs
├── Tasks/                        # Scheduled tasks
│   └── ShowSyncTask.cs
├── JellyseerrBridgePlugin.cs     # Main plugin class
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

This project is open source and available under the MIT License.