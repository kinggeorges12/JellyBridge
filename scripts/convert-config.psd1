@{
    # TypeScript to C# Conversion Configuration
    # PowerShell Data file (.psd1) - standard format for configuration data
    
    # Base directory for the Jellyseerr Bridge project (where the script should run from)
    BridgeBaseDir = "D:\GitHub\Jellyseerr-Bridge"
    
    # Default parameters for the conversion script
    SeerrRootDir = "codebase/seerr-main"
    OutputDir = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel"
    
    # Regex pattern to identify properties that should be double instead of int
    DoublePropertyPattern = '.*(?:average|rating|score|percent|ratio|rate|popularity|runtime|duration|weight|price|cost|amount|value|percentage|probability|frequency).*'
    
    # Define blocked classes that should not be converted
    BlockedClasses = @(
        'ServarrBase',
        'ExternalAPI',
        'CacheResponse',
        'CacheResponseImageCache',
        'ImageCache',
        'DownloadTracker',
        'Settings'
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
        }
    )
}
