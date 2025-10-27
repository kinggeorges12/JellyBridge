@{
    # TypeScript to C# Conversion Configuration
    # PowerShell Data file (.psd1) - standard format for configuration data
    
    # Base directory for the Jellyseerr Bridge project (where the script should run from)
    BridgeBaseDir = "D:\GitHub\JellyBridge"
    
    # Default parameters for the conversion script
    SeerrRootDir = "codebase/seerr-main"
    OutputDir = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel"
    BaseNamespace = "Jellyfin.Plugin.JellyBridge.JellyseerrModel"
    
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
            output = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Server"
            type = "Server"
        },
        @{
            input = "codebase/seerr-main/server/interfaces/api"
            output = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Api"
            type = "Api"
        },
        @{
            input = "codebase/seerr-main/server/constants"
            output = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Common"
            type = "Common"
        },
        @{
            input = "codebase/seerr-main/server/entity"
            output = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Common"
            type = "Common"
        }
    )
    
    # Global JSON property naming convention
    # "camelcase" = convert all to camelCase (e.g., "mediaType")
    # "snake_case" = convert all to snake_case (e.g., "media_type") 
    # null/empty = preserve original case from TypeScript source
    JsonPropertyNaming = $null
}
