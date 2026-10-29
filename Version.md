# 🖇️ Compatibility

\* [Seerr v3.0.0 has recently released](https://docs.seerr.dev/blog/seerr-release) as the next iteration of Jellyseerr v2.7.3. The API appears to be unchanged, and my tests have not found any compatibility problems. Enjoy the new release! Please submit any issues with Seerr to this project, and I will try to expedite a fix. As for our documentation, I will wait until the next major update to switch to the new name.

This plugin has been *fully tested using Jellyfin 10.10.7 and 10.11.\* with Jellyseerr 2.7.3 and Seerr 3.0.1. Previous versions lacked compatibility with Jellyfin 10.11.\*, but that has been resolved as of the plugin version 1.3.0.\*! Unknown compatibility with Jellyfin versions before 10.10.0 or after 10.11.6, or Jellyseerr versions before 2.7.3.

## 📣 Release Notes

### Version 4.0 🆕
- Updated plugin for compatibility with Jellyfin v12.0!

### Version 3.3 ✨

- Fixed localization [Issue #46](https://github.com/kinggeorges12/JellyBridge/issues/46) by correctly analyzing the media IDs instead of the media name.
- Adding new feature from [Issue #37](https://github.com/kinggeorges12/JellyBridge/issues/37) "Mixed Movies and Shows" to allow users to switch from a single library to separate Movies and Shows libraries.
- Splitting "Manage Discover Library" tab into "Organize Discover Library" and "Manage Favorite Requests".
- Included new feature Create Discover Library to generate a default JellyBridge library in Jellyfin. Removed the existing button for Generate Network Folders, as the new feature addresses this.
- Fixing bugs in Generate Promo Videos, Enable the Automated Task to Sort Discover Content, and Recycle JellyBridge Library Data.

### Version 3.2 ✨

- New Promo Videos feature submitted in [PR #26](https://github.com/kinggeorges12/JellyBridge/pull/26).
- Updated the network selection UI in the Import Discover Content section with a more user-friendly design.
- The Manage Discover Library section has a new option Request 4k Content that examines user-level permissions to determine whether to request 4k content from Jellyseerr.
- Added folder buttons to open paths on the configuration page using the Jellyfin file browser.

### Version 3.0 🎉

- Fixed compatibility bug causing crashes with Jellyfin 10.11.9 and later.
- Bugs listed in [PR #31](https://github.com/kinggeorges12/JellyBridge/pull/31).
- Revised the versioning schema to use the last two version numbers to indicate major and minor versions of Jellyfin, e.g., v3.0.**11.9** indicates compatibility with Jellyfin 10.**11.9** and later.

### Version 2.3 ✨

- Request Only First Season: Option to request only the first season of a TV series instead of the entire series. This can be enabled in the Manage Discover Library section of the plugin configuration.

### Version 2.2 ✨

- Responsive Favorite Requests: An option, enabled by default, to immediately request favorites from Jellyseerr upon favoriting a media item.

### Version 2.0 🎉

- Sort Discover Content: automated task with interval and algorithm selection (None, Random, Smart, Smartish), on-demand "Refresh Discover Library Sort Order", and optional "Mark Media Played".
- Manage Discover Library: "Network Folders" setup instructions with "Generate Library Folders" action, optional "Show Discover Content in Multiple Libraries", and configurable "Library Prefix"; fixed content exclusions across multiple libraries; changed favorites cleanup.
- Advanced & UX: startup run of enabled tasks with delay, task/request timeouts, new "Enable Trace Logging", and an Automated Sync Status bar (running/idle, progress, last/next run).

**⚠️ Version Notes**
This version is tested with Jellyfin 10.10.7 and 10.11.\* (up to 10.11.4) for compatibility.
- To prevent errors after upgrading, the option `Advanced → Auto-sync on Plugin Startup` is disabled by default. Re-enable it if desired.
- If you use "Network Folders" (previously "Create separate libraries for streaming services"), review and re-enable the related options after upgrading.
- `Manage Discover Library → Favorite Cleanup` functionality changed to always ignore and unmark played for requested favorites. The option now only affects whether the favorited items are unfavorited.

After installing the new version, you may need to update the plugin configuration page using these instructions:
`Jellyfin uses caching for the plugin configuration pages, so you may need to do a hard refresh of the page to see the latest changes. In Chrome, Open Developer Tools (F12) → Right-click Refresh button → "Empty Cache and Hard Reload".`

### Version 1.0

**🙏 A Note to Early Testers**: Thank you! I have fixed a lot of bugs on the backend with the v1.3.0.\* release, and some new features! I hope the flurry of issues does not stop you from checking out the new release. I tested this release on both 10.10.7 and 10.11.1 releases. Please continue submitting issues with new feature ideas and reporting bugs.