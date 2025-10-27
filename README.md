# JellyBridge Plugin

A Jellyfin plugin that bridges Jellyfin with Jellyseerr for seamless show discovery and download requests.

**⚠️ Compatibility Note**: This plugin has been tested with Jellyfin 10.10.7 and Jellyseerr 2.7.3. It may not be compatible with Jellyfin 10.11.* or different Jellyseerr versions.

## Features

- **Automated Content Listing**: Automatically lists movies and TV shows from various networks (Netflix, Prime Video, etc.) within Jellyfin as placeholders
- **Easy Downloads**: Sends download requests to Jellyseerr when you mark movies or shows as favorites in Jellyfin
- **Customizable**: Allows selection of networks to fetch and display in Jellyfin
- **Scheduled Sync**: Automatically syncs movies and shows on a configurable schedule and on Jellyfin startup
- **Library Management**: Prevents placeholder movies and shows from appearing in main libraries
- **Separate Libraries**: Option to create dedicated libraries for each network
- **Smart Exclusion**: Uses `.ignore` files to exclude movies and shows that already exist in your main Jellyfin libraries

## Usage

The plugin includes a modern, responsive web interface accessible at:
`http://your-jellyfin-server/Plugins/JellyBridge/ConfigurationPage`

1. **Configure the plugin** through the web interface with your Jellyseerr credentials and directory paths
2. **Create JellyBridge Library** in Jellyfin with the options suggested on the plugin configuration page
3. **Test the connection** to ensure Jellyseerr is accessible
4. **Enable the plugin** and trigger an initial sync
5. **Mark shows as favorites** in Jellyfin to automatically request downloads

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

### Main Configuration
- **Enable Plugin**: Enable or disable the automated sync task (default: disabled)
- **Sync Interval**: How often to sync in hours, allowing partial hours (default: 24)
- **Jellyseerr URL**: The base URL of your Jellyseerr instance (default: `http://localhost:5055`)
- **API Key**: Your Jellyseerr API key (found in Settings → General)
- **Test Connection**: Button to validate your Jellyseerr connection

### 📁 Library Settings (Collapsible)
- **Library Directory**: Path to JellyBridge's library directory (default: `/data/JellyBridge`) - *Note*: Due to frequent rewrites, consider using an SSD for the library location to minimize wear on HDDs
- **Manage JellyBridge Library**: After syncing, refreshes libraries containing the Library Directory path
- **Exclude from Main Libraries**: Excludes streaming movies/shows that appear in Jellyfin libraries via .ignore files
- **Create Separate Libraries**: Creates dedicated libraries for each network (default: disabled)
- **Library Prefix**: Prefix for network library names (default: empty)

### 🔍 Discover Settings (Collapsible)
- **Max Discover Pages**: Maximum pages to fetch from discover endpoint per network (default: 10, set to 0 for unlimited)
- **Max Retention Days**: Days to retain items before cleanup (default: 30)
- **Region**: Watch network region for determining available networks (default: US)
- **Network Services**: Multi-select interface to choose which networks to sync

### ⚙️ Advanced (Collapsible)
- **Auto Sync on Startup**: Automatically perform sync when plugin starts (default: enabled)
- **Startup Delay**: Seconds to wait before auto-sync on startup (default: 60)
- **Request Timeout**: Timeout for API requests in seconds (default: 60)
- **Retry Attempts**: Number of retry attempts for failed requests (default: 3)
- **Enable Debug Logging**: Enable detailed debug logging (default: disabled)


#### Placeholder Show Filtering
The plugin intelligently detects and filters placeholder shows (shows that are requested but don't have actual media files yet):

- **Automatic Detection**: Identifies shows with no media files (only .nfo, .txt, etc.)
- **Smart Filtering**: Excludes placeholders from main libraries when enabled
- **Streaming Libraries**: Always includes placeholders in dedicated streaming libraries
- **Configurable**: Enable/disable exclusion via the configuration interface

#### Library Management
- **Separate Libraries**: Creates dedicated libraries for each network
- **Library Prefixing**: Customizable prefix for network libraries
- **Path Detection**: Automatically detects library types based on directory structure
- **Content Filtering**: Filters content based on actual media file presence

### Advanced Settings

- **Sync Interval**: How often to sync movies and shows (in hours)
- **Request Timeout**: Timeout for API requests in seconds (default: 60)
- **Retry Attempts**: Number of retry attempts for failed requests (default: 3)
- **Enable Debug Logging**: Enable detailed debug logging (default: disabled)
- **Placeholder Duration**: Duration of placeholder videos in seconds (default: 10)

## Logging

The plugin integrates with Jellyfin's logging system. Enable debug logging from the advanced options to record detailed logs. Check Jellyfin logs for debugging information:

- Plugin initialization and configuration
- API calls to Jellyseerr
- Show sync operations
- Library management
- Error messages and warnings

## Troubleshooting

If you encounter any issues with the plugin, please leave a comment in the [GitHub Discussions](https://github.com/kinggeorges12/JellyBridge/discussions).

## Development

For detailed development instructions, including building, testing, and contributing, see [Development.md](Development.md).

## License

This project is open source and available under the GNU General Public License v3.0.

## Acknowledgments

Thank you to the creator of the [Overseer-Jellyfin Bridge Script](https://github.com/geekfreak21/Overseer-and-Jellyfin-Bridged) for the inspiration. Special thanks to the developers of the [**Intro Skipper**](https://github.com/intro-skipper) and [**Custom Tabs**](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) plugins for reusing their GPL-licensed code in the UI styling and configuration patterns.

And of course, thanks to the developers of [**Jellyfin**](https://jellyfin.org/) and [**Jellyseerr**](https://seerr.dev/) for making it all possible.