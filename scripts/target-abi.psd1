# Define targets:
# - JellyfinVersion (Normalized Jellyfin version for csproj compiler)
# - MinTargetAbi (Jellyfin compatibility resolver)
# - SubVersion (for JellyBridge patch and build version)
# Put these in order of highest to lowest Jellyfin version, so users see the most recent as their compatible version.
@(
    @{ JellyfinVersion = "12.0.0"; SubVersion = "12.0"; MinTargetAbi = "12.0.0"; },
    @{ JellyfinVersion = "10.11.9"; SubVersion = "11.9"; MinTargetAbi = "10.11.9.0"; },
    @{ JellyfinVersion = "10.11.0"; SubVersion = "11.0"; MinTargetAbi = "10.11.0.0"; },
    @{ JellyfinVersion = "10.10.7"; SubVersion = "10.7"; MinTargetAbi = "10.10.0.0"; }
)