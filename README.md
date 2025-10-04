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

1. Download the plugin DLL file
2. Place it in your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure the plugin through the admin interface

## Configuration

Access the plugin configuration at: `http://your-jellyfin-server/Plugins/JellyseerrBridge`

### Required Settings

- **Jellyseerr URL**: The base URL of your Jellyseerr instance (e.g., `http://localhost:5055`)
- **API Key**: Your Jellyseerr API key
- **Email**: Your Jellyseerr login email
- **Password**: Your Jellyseerr login password
- **Shows Directory**: Base directory where show placeholders will be created

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

1. **Configure the plugin** with your Jellyseerr credentials and directory paths
2. **Enable the plugin** and trigger an initial sync
3. **Create separate libraries** (recommended) for each streaming service to avoid confusion
4. **Scan your Jellyfin library** to see the placeholder shows
5. **Mark shows as favorites** in Jellyfin to automatically request downloads

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

This plugin is built using:
- .NET 6.0
- Jellyfin Plugin SDK
- ASP.NET Core for API endpoints
- Newtonsoft.Json for JSON handling

## License

This project is open source and available under the MIT License.