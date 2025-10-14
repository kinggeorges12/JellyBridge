@{
    # TypeScript to C# Conversion Configuration
    # PowerShell Data file (.psd1) - standard format for configuration data
    
    # Base directory for the Jellyseerr Bridge project (where the script should run from)
    BridgeBaseDir = "D:\GitHub\Jellyseerr-Bridge"
    
    # Default parameters for the conversion script
    SeerrRootDir = "codebase/seerr-main"
    OutputDir = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel"
    BaseNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel"
    
    # Regex pattern to identify properties that should be double instead of int
    NumberToDoublePattern = '(?:.*average$|^popularity$|^percent$)'
    
    # Define blocked classes that should not be converted
    BlockedClasses = @(
        'ExternalAPI',
        'ServarrBase',
        'CacheResponse',
        'CacheResponseImageCache',
        'ImageCache',
        'DownloadTracker',
        'Settings',
        'UserSettings'
    )
    
    # Define input/output directory pairs for conversion
    DirectoryPairs = @(
        @{
            input = "codebase/seerr-main/server/models"
            output = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel/Server"
            type = "Server"
        },
        @{
            input = "codebase/seerr-main/server/interfaces/api"
            output = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel/Api"
            type = "Api"
        },
        @{
            input = "codebase/seerr-main/server/constants"
            output = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel/Common"
            type = "Common"
        },
        @{
            input = "codebase/seerr-main/server/entity"
            output = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel/Common"
            type = "Common"
        }
    )
    
    # Global JSON property naming convention
    # true = use camelCase (e.g., "mediaType"), false = use snake_case (e.g., "media_type")
    JsonCamelCase = $true
}
