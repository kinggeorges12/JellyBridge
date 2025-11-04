# JellyBridge Plugin

A Jellyfin plugin that bridges Jellyfin with Jellyseerr for seamless movie and series discovery and download requests.

## Release Notes

After installing the new version, you may need to update the plugin configuration page using these instructions:
`Jellyfin uses caching for the plugin configuration pages, so you may need to do a hard refresh of the page to see the latest changes. In Chrome, Open Developer Tools (F12) → Right-click Refresh button → "Empty Cache and Hard Reload".`

### Version 2.0 Change Notes

1. To prevent errors after upgrading, I disabled this option by default: `Advanced → Auto-sync on Plugin Startup`. If you want this option enabled, please enable it again after upgrading.
2. If you use the "Use Network Folders" (previously called "Create separate libraries for streaming services"), review and re-enable those options in the configuration page after upgrading.
3. The `Manage Discover Library → Clean-up Requests from Favorites` was set to default enabled, because it was causing some ignored items to get requested after the requests were deleted in Jellyseerr. This change will probably be invisible to end users.

**⚠️ Compatibility Note**
This version is tested with Jellyfin 10.10.7 and 10.11.2 for compatibility.

### Version 1.0 Change Notes

**🙏 A Note to Early Testers**: Thank you! I have fixed a lot of bugs on the backend with the v1.3.0.* release, and some new features! I hope the flurry of issues does not stop you from checking out the new release. I tested this release on both 10.10.7 and 10.11.1 releases. Please continue submitting issues with new feature ideas and reporting bugs.

## Features

- **Native Jellyfin Support**: Whether you use Android TV or Kodi to sync videos and favorites with Jellyfin, this plugin has you covered
- **Make Jellyseerr Requests**: Enables requesting new movies and series directly from a Jellyfin library, making this accessible from mobile apps, Android TV, and even Kodi Sync Queue!
- **Automated Content Listing**: Automatically lists movies and series from various networks (Netflix, Prime Video, etc.) within Jellyfin as placeholders
- **Easy Downloads**: Sends downloads to Arr apps through Jellyseerr media requests when you mark movies or series as favorites in Jellyfin
- **Configure Network Discovery**: Allows selection of networks to fetch and display in Jellyfin
- **Scheduled Tasks**: Automatically syncs movies and series on a configurable schedule and on Jellyfin startup; optional automated sort task to reshuffle library order
- **Library Management**: Prevents placeholder movies and series from appearing in main libraries
- **Fine-grained Libraries**: Option to create separate directories for each network allowing you to group them into libraries
- **Smart Exclusion**: Uses native Jellyfin configuration files to exclude movies and series that already exist in your other Jellyfin libraries

## Jellyfin Integration

The plugin integrates seamlessly with Jellyfin, displaying discovered content as placeholder videos that users can browse and mark as favorites to request downloads.

Get Jellyfin here: [jellyfin.org](https://jellyfin.org/). For setup, see the official [installation guide](https://jellyfin.org/docs/general/installation/).

### Library View
![Jellyfin Library](Screenshots/Library.png)

The plugin manages libraries and folders in Jellyfin, creating structure for discovered content.

### Items View
![Jellyfin Items with Placeholders](Screenshots/Items.png)

Browse all discovered movies and series from Jellyseerr networks with thumbnails and metadata.

### Sorting

To see the intended order, set the JellyBridge library sort to **Play count** (Ascending). This works using the task to sort discover content, which updates play counts to create a dynamic browsing order.
![JellyBridge Sorting](Screenshots/Sorting.png)

### Placeholder Videos

The plugin generates placeholder videos for movies and series that aren't yet available in your Jellyfin libraries. These placeholder videos are created using FFmpeg with configurable duration settings.

![Placeholder Videos](Screenshots/Placeholder.png)

- **Smart Detection**: Jellyfin automatically identifies and displays placeholder videos
- **Automatic Generation**: Creates placeholder videos for movies and series not yet in your Jellyfin library
- **Configurable Duration**: Control the length of placeholder videos
- **Retry Logic**: Includes retry attempts to ensure FFmpeg availability before generating placeholders
- **Efficient Caching**: Cached placeholder videos are reused to minimize generation overhead

### Jellyseerr Integration

The plugin seamlessly integrates with Jellyseerr to manage download requests and track availability of movies and series.

![Jellyseerr Requests](Screenshots/Requests.png)

When users mark movies or series as favorites in Jellyfin, the plugin automatically sends download requests to Jellyseerr for processing. The user who requested the content is shown in Jellyseerr as the requestor, all you have to do is import the Jellyfin users. Any integrations with Jellyseerr like Radarr and Sonarr will manage the content creation in Jellyfin. After content is found in other Jellyfin libraries, the movie or series is hidden from the JellyBridge library.

Get Jellyseerr here: [seerr.dev](https://seerr.dev). For setup, see the official [installation guide](https://docs.seerr.dev/getting-started).

After installing Jellyseerr, disable the JellyBridge library to prevent requests from being marked as Available.
![Jellyseerr Requests](Screenshots/Jellyseerr.png)

### Kodi Sync Plugin for Jellyfin

Browse with Kodi and request content from Jellyfin via the JellyCon add-on. Incompatible with Jellyfin for Kodi.

![JellyCon](Screenshots/JellyCon.png)

- **Native integration**: Favorite items directly from the Kodi interface
- **Discover Recommended**: Use the "Recommended Items" or "Random Items" links to discover content
 
For native Jellyfin support, install [Kodi Sync Queue](https://github.com/jellyfin/jellyfin-plugin-kodisyncqueue). For plugin setup, see the official [Jellyfin Kodi client docs](https://jellyfin.org/docs/general/clients/kodi/).


## Installation

### Method 1: Automatic Installation (Recommended)

![Jellyfin Catalog](Screenshots/Catalog.png)

1. **Add Plugin Repository to Jellyfin:**
   - Go to Jellyfin Admin Dashboard → Plugins → Catalog
   - Click ⚙️ icon (Jellyfin 11: "Manage Repositories" button)
   - Click "Add Repository" button (Jellyfin 11: "New Repository" button)
   - Enter Repository Name: JellyBridge
   - Enter Repository URL: `https://raw.githubusercontent.com/kinggeorges12/JellyBridge/refs/heads/main/manifest.json`
   - Click "Add"

2. **Install Plugin:**
   - Go to Plugins → Catalog (Jellyfin 11: Plugins → Available)
   - Find "JellyBridge" under Metadata (Jellyfin 11: Other)
   - Click "Install"
   - Restart Jellyfin from the Dashboard

   Note on versions:
   - For Jellyfin 10.11.* users only, install the version ending in `.11` from the plugin page.
   - Versions ending in `.10` or `.0` are for Jellyfin 10.10.* only, although the wonky Jellyfin plugin versioning does not let me change the visibility.
   ![Versioning guide](Screenshots/Versioning.png)

3. **Configure the plugin** through the admin interface

### Method 2: Manual Installation

1. Download the plugin ZIP file from the [releases](../../releases)
2. Extract the contents to your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure the plugin through the web interface

## Configuration

Access the plugin configuration from the host machine at: `http://localhost:8096/web/#/configurationpage?name=JellyBridge`

### Usage

The plugin includes a modern, responsive web interface for configuration. Follow these steps to get started:

1. **Configure the plugin** through the web interface with your Jellyseerr credentials and directory paths
2. **Create JellyBridge Library** in Jellyfin with the options suggested on the plugin configuration page
3. **Test the connection** to ensure Jellyseerr is accessible
4. **Enable the plugin** and trigger an initial sync (or use the Import button)
5. **Mark movies or series as favorites** in Jellyfin to automatically request downloads

The plugin provides a comprehensive web-based configuration interface with the following sections:

### Main Configuration

![Plugin Configuration - General Settings](Screenshots/General.png)

- **Jellyseerr URL**: The web address where your Jellyseerr instance is running
- **API Key**: Authentication key that allows the plugin to communicate with Jellyseerr
- **Library Directory**: Where JellyBridge stores placeholder videos and metadata files on your system. Placeholder videos are short video files that represent content not yet downloaded. Metadata files contain information about movies and shows. Use a dedicated path for JellyBridge.
- **Enable the Automated Task to Sync Jellyseerr and Jellyfin**: Turns on automatic syncing on a schedule. Syncing transfers content information between Jellyseerr and Jellyfin.
- **Sync Interval (Hours)**: How often the plugin automatically syncs content (hours). Supports decimals (e.g., 1.5). Set to 0 to disable automatic syncing.
- **Test Connection with Jellyseerr**: Verifies that the plugin can successfully connect to your Jellyseerr instance
- **Automated Sync Status**: Shows whether the scheduled sync is running/idle/disabled, progress, and last/next run times


Notes:
- For Docker, `Jellyseerr URL` can be `http://host.docker.internal:5055` on the host network.
- After first-time install or some upgrades, Jellyfin delays scheduled tasks by ~1 hour before the first run.

### 🔍 Import Discover Content

![Import Discover Content](Screenshots/Import.png)

- **Region**: Which geographic region to use for discovering streaming content. Different regions show different available networks.
- **Network Services**: Choose which streaming networks to pull content from and include in your library. Use the two-pane picker to move networks from Available → Synced. Search boxes help filter; use ↻ to refresh lists.
- **Max Discover Pages**: Controls how much discover content is pulled from each network during sync. Discover content includes available movies and shows from streaming networks. Each page contains 20 items. Applies to both movies and TV shows. Set to 0 to retrieve all pages (start small if testing).
- **Max Retention Days**: How long to keep discover content in the library before automatically removing it. Older items are cleaned up during sync.
- **Import Discover Content from Jellyseerr into JellyBridge Library**: Manually import discover content immediately without waiting for the scheduled task.

### 📁 Manage Discover Library

![Manage Discover Library](Screenshots/Manage.png)

 - **Manage JellyBridge Library**: Automatically refreshes your JellyBridge library in Jellyfin after each sync to show newly added or updated content. Performs a quick refresh for new items, or a full refresh if content was removed from Jellyseerr.
 - **Exclude Jellyfin library media from streaming libraries**: Prevents duplicate content by hiding movies and shows in the JellyBridge library if they already exist in your other Jellyfin libraries.
 - **Clean-up Requests from Favorites**: Automatically removes items from everyone's favorites list once they've been successfully requested in Jellyseerr, and hides them from the library. This prevents conflicts with other plugins like <a target="_blank" href="https://github.com/stefanbohacek/MediaCleaner">Media Cleaner for Jellyfin</a>. If the request is denied in Jellyseerr, the item will reappear on the next sync.
- **Request JellyBridge Library Favorites in Jellyseerr**: Manually trigger requests for all favorited items in the JellyBridge library to be sent to Jellyseerr. This button processes favorites immediately without waiting for the scheduled sync task.
- **Use Network Folders**: Creates separate folders for each selected network (e.g., Netflix, Prime Video, FOX) so you can map each to its own Jellyfin library.
- **Network Folder Setup**: Use the “🗂️ Generate Network Folders” button after selecting networks. Then create one Jellyfin library per network folder. Prefer adding the network subfolders (not the base JellyBridge folder) to avoid folder icons in the UI.
- **Library Prefix**: Prefix applied to generated network library names, e.g., `Network - FOX`.
- **Duplicate Discover Content for JellyBridge Libraries**: When using network folders, allows the same title to exist in multiple JellyBridge libraries if each library’s network selection is unique (e.g., “Friends” in both HBO and Hulu network libraries). Caution: Do not include the same network in multiple libraries or you’ll create duplicates within a library.

### 🔀 Sort Discover Content
![Sort Discover Content](Screenshots/Sort.png)

- **Enable the Automated Task to Sort Discover Content**: Periodically updates play counts to drive sort order for all users.
- **Sort Task Interval (Hours)**: How often to run the sort task; supports decimals. Set to 0 to disable automatic sort randomization.
- **Sort Order**: Choose algorithm:
  - None: set all play counts to 0
  - Random: randomize play counts
  - Smart: semi-intelligent ordering based on user library genres
  - Smartish: smart ordering with some randomness
- **Mark Media Played**: Toggle whether items appear played (affects Jellyfin badges). Disabling sets items to unplayed before sorting.
- **Refresh Discover Library Sort Order**: Manually apply the selected algorithm immediately.

Tip: Each user should set the JellyBridge library sort to “Play count” (Ascending) in the Jellyfin UI to see the intended order. This hides the blue “1” play-count badge clutter.

### ⚙️ Advanced Settings

![Advanced Settings](Screenshots/Advanced.png)
- **Run Automated Tasks on Plugin Startup**: Automatically runs enabled tasks when the plugin starts or when Jellyfin restarts.
- **Startup Delay (seconds)**: How many seconds to wait before running the startup sync, in addition to Jellyfin's built-in 1-minute delay.
- **Task Timeout (minutes)**: How long to wait for plugin tasks (sync, sort, etc.) to finish before cancelling. This timeout applies after acquiring the task lock. Default is 10 minutes.
- **Request Timeout (seconds)**: How long to wait for responses from Jellyseerr before considering the request failed.
- **Retry Attempts**: How many times to retry failed requests to Jellyseerr before giving up.
- **Placeholder Video Duration (seconds)**: How long the placeholder videos should be. Placeholder videos are short video files created for movies and shows that aren't yet available in your library, allowing them to appear in Jellyfin.
- **Enable Debug Logging**: Provides more detailed information in the logs to help troubleshoot issues.
- **Enable Trace Logging**: The most verbose logs; only available when Debug Logging is enabled.

Note: The Advanced section also includes a destructive action "Recycle JellyBridge Library Data" to purge all generated JellyBridge data from the configured library directory. Use with extreme caution. After deletion, remove any JellyBridge libraries that point to the path and recreate them per the setup instructions.

## Logging & Troubleshooting

The plugin integrates with Jellyfin's logging system. Enable debug logging from the advanced options to record detailed logs. Check Jellyfin logs for debugging information:

- Plugin initialization and configuration
- API calls to Jellyseerr
- Series sync operations
- Library management
- Error messages and warnings

If you encounter any issues with the plugin, please leave a comment in the [GitHub Discussions](https://github.com/kinggeorges12/JellyBridge/discussions).

**⚠️ Compatibility Note**: This plugin has been *fully tested using Jellyfin 10.10.7 and 10.11.2* with Jellyseerr 2.7.3. Previous versions lacked compatibility with Jellyfin 10.11.\*, but that has been resolved as of the plugin version 1.3.0.\*! Unknown compatibility with Jellyfin versions before 10.10.0 or after 10.11.2, or Jellyseerr versions before 2.7.3.

Tips:
- If the config UI doesn’t reflect the latest changes after an update, force-refresh the page (see Release Notes for steps).
- If favorites cannot be requested from the Manage Library page, temporarily disable CSRF protection in Jellyseerr (Settings → Network → uncheck “Enable CSRF Protection”).

## Development

For detailed development instructions, including building, testing, and contributing, see [Development.md](Development.md).

## License

This project is open source and available under the GNU General Public License v3.0.

## Acknowledgments

Thank you to the creator of the [Overseer-Jellyfin Bridge Script](https://github.com/geekfreak21/Overseer-and-Jellyfin-Bridged) for the inspiration. Special thanks to the developers of the [**Intro Skipper**](https://github.com/intro-skipper) and [**Custom Tabs**](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) plugins for reusing their GPL-licensed code in the UI styling and configuration patterns.

And of course, thanks to the developers of [**Jellyfin**](https://jellyfin.org/) and [**Jellyseerr**](https://seerr.dev/) for making it all possible.