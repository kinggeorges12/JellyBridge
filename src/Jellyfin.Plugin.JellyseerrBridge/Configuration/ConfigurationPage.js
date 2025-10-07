const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

export default function (view) {
    if (!view) {
        Dashboard.alert('❌ Jellyseerr Bridge: View parameter is undefined');
        return;
    }
    
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        ApiClient.getPluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId).then(function (config) {
            page.querySelector('#IsEnabled').checked = config.IsEnabled || true;
            page.querySelector('#JellyseerrUrl').value = config.JellyseerrUrl || 'http://localhost:5055';
            page.querySelector('#ApiKey').value = config.ApiKey || '';
            page.querySelector('#LibraryDirectory').value = config.LibraryDirectory || '/data/Jellyseerr';
            page.querySelector('#UserId').value = config.UserId || 1;
            page.querySelector('#SyncIntervalHours').value = config.SyncIntervalHours || 24;
            page.querySelector('#ExcludeFromMainLibraries').checked = config.ExcludeFromMainLibraries !== false;
            page.querySelector('#CreateSeparateLibraries').checked = config.CreateSeparateLibraries || false;
            page.querySelector('#LibraryPrefix').value = config.LibraryPrefix || 'Streaming - ';
            page.querySelector('#AutoSyncOnStartup').checked = config.AutoSyncOnStartup || false;
            page.querySelector('#RequestTimeout').value = config.RequestTimeout || 30;
            page.querySelector('#RetryAttempts').value = config.RetryAttempts || 3;
            page.querySelector('#EnableDebugLogging').checked = config.EnableDebugLogging || false;
            // Store the current watch provider region value
            const watchProviderSelect = page.querySelector('#WatchProviderRegion');
            watchProviderSelect.setAttribute('data-current-value', config.WatchProviderRegion || 'US');
            
            Dashboard.hideLoadingMsg();
        });
    });
    
    const form = view.querySelector('#jellyseerrBridgeConfigurationForm');
    if (!form) {
        Dashboard.alert('❌ Jellyseerr Bridge: Configuration form not found');
        return;
    }
    
    form.addEventListener('submit', function (e) {
        Dashboard.showLoadingMsg();
        const form = this;
        ApiClient.getPluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId).then(function (config) {
            config.IsEnabled = form.querySelector('#IsEnabled').checked;
            config.JellyseerrUrl = form.querySelector('#JellyseerrUrl').value;
            config.ApiKey = form.querySelector('#ApiKey').value;
            config.LibraryDirectory = form.querySelector('#LibraryDirectory').value;
            config.UserId = parseInt(form.querySelector('#UserId').value) || 1;
            config.SyncIntervalHours = parseInt(form.querySelector('#SyncIntervalHours').value) || 24;
            config.ExcludeFromMainLibraries = form.querySelector('#ExcludeFromMainLibraries').checked;
            config.CreateSeparateLibraries = form.querySelector('#CreateSeparateLibraries').checked;
            config.LibraryPrefix = form.querySelector('#LibraryPrefix').value;
            config.AutoSyncOnStartup = form.querySelector('#AutoSyncOnStartup').checked;
            config.RequestTimeout = parseInt(form.querySelector('#RequestTimeout').value) || 30;
            config.RetryAttempts = parseInt(form.querySelector('#RetryAttempts').value) || 3;
            config.EnableDebugLogging = form.querySelector('#EnableDebugLogging').checked;
            config.WatchProviderRegion = form.querySelector('#WatchProviderRegion').value || 'US';

            ApiClient.updatePluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        });
        e.preventDefault();
        return false;
    });

    const testButton = view.querySelector('#testConnection');
    if (!testButton) {
        Dashboard.alert('❌ Jellyseerr Bridge: Test connection button not found');
        return;
    }
    
    testButton.addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        
        const url = view.querySelector('#JellyseerrUrl').value;
        const apiKey = view.querySelector('#ApiKey').value;
        
        const testData = {
            JellyseerrUrl: url,
            ApiKey: apiKey
        };

        ApiClient.ajax({
            url: ApiClient.getUrl('JellyseerrBridge/TestConnection'),
            type: 'POST',
            data: JSON.stringify(testData),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (data) {
            Dashboard.hideLoadingMsg();
            
            const debugInfo = 'CONNECTION RESPONSE DEBUG:\n' +
                'Response exists: ' + (data ? 'YES' : 'NO') + '\n' +
                'Response type: ' + typeof data + '\n' +
                'Response success: ' + (data?.success ? 'YES' : 'NO') + '\n' +
                'Response message: ' + (data?.message || 'UNDEFINED') + '\n' +
                'Response status: ' + (data?.status || 'UNDEFINED') + '\n' +
                'Full response: ' + JSON.stringify(data) + '\n' +
                'Response keys: ' + (data ? Object.keys(data).join(', ') : 'NONE');
            
            if (data && data.success) {
                Dashboard.alert('✅ CONNECTION SUCCESS!\n\n' + debugInfo);
            } else {
                Dashboard.alert('❌ CONNECTION FAILED!\n\n' + debugInfo);
            }
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'CONNECTION ERROR DEBUG:\n' +
                'Error exists: ' + (error ? 'YES' : 'NO') + '\n' +
                'Error type: ' + typeof error + '\n' +
                'Error message: ' + (error?.message || 'UNDEFINED') + '\n' +
                'Error name: ' + (error?.name || 'UNDEFINED') + '\n' +
                'Error status: ' + (error?.status || 'UNDEFINED') + '\n' +
                'Full error: ' + JSON.stringify(error);
            
            Dashboard.alert('❌ CONNECTION ERROR!\n\n' + debugInfo);
        });
    });

    const syncButton = view.querySelector('#manualSync');
    if (!syncButton) {
        Dashboard.alert('❌ Jellyseerr Bridge: Manual sync button not found');
        return;
    }
    
    syncButton.addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        
        // Load watch provider regions first
        loadWatchProviderRegions(view);
        
        ApiClient.ajax({
            url: ApiClient.getUrl('JellyseerrBridge/Sync'),
            type: 'POST',
            data: '{}',
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (data) {
            Dashboard.hideLoadingMsg();
            
            const debugInfo = 'SYNC RESPONSE DEBUG:\n' +
                'Response exists: ' + (data ? 'YES' : 'NO') + '\n' +
                'Response type: ' + typeof data + '\n' +
                'Response success: ' + (data?.success ? 'YES' : 'NO') + '\n' +
                'Response message: ' + (data?.message || 'UNDEFINED') + '\n' +
                'Full response: ' + JSON.stringify(data) + '\n' +
                'Response keys: ' + (data ? Object.keys(data).join(', ') : 'NONE');
            
            if (data && data.success) {
                Dashboard.alert('✅ SYNC SUCCESS!\n\n' + debugInfo);
            } else {
                Dashboard.alert('❌ SYNC FAILED!\n\n' + debugInfo);
            }
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'SYNC ERROR DEBUG:\n' +
                'Error exists: ' + (error ? 'YES' : 'NO') + '\n' +
                'Error type: ' + typeof error + '\n' +
                'Error message: ' + (error?.message || 'UNDEFINED') + '\n' +
                'Error name: ' + (error?.name || 'UNDEFINED') + '\n' +
                'Error status: ' + (error?.status || 'UNDEFINED') + '\n' +
                'Full error: ' + JSON.stringify(error);
            
            Dashboard.alert('❌ SYNC ERROR!\n\n' + debugInfo);
        });
    });
}

function loadWatchProviderRegions(page) {
    ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/WatchProviderRegions'),
        type: 'GET',
        dataType: 'json'
    }).then(function (data) {
        // Debug logging
        const debugInfo = 'REGIONS API RESPONSE DEBUG:\n' +
            'Response exists: ' + (data ? 'YES' : 'NO') + '\n' +
            'Response type: ' + typeof data + '\n' +
            'Response success: ' + (data?.success ? 'YES' : 'NO') + '\n' +
            'Regions count: ' + (data?.regions ? data.regions.length : 'UNDEFINED') + '\n' +
            'Full response: ' + JSON.stringify(data);
        
        Dashboard.alert('🔍 REGIONS API DEBUG:\n\n' + debugInfo);
        
        if (data && data.success && data.regions) {
            const select = page.querySelector('#WatchProviderRegion');
            if (select) {
                // Clear existing options
                select.innerHTML = '';
                
                // Sort regions by English name (with null safety)
                const sortedRegions = data.regions
                    .filter(region => region.englishName && region.englishName.trim() !== '')
                    .sort((a, b) => a.englishName.localeCompare(b.englishName));
                
                // Add options for each region
                sortedRegions.forEach(region => {
                    const option = document.createElement('option');
                    option.value = region.iso31661;
                    option.textContent = `${region.englishName} (${region.nativeName})`;
                    select.appendChild(option);
                });
                
                // Ensure US is selected by default if no current value is set
                const currentValue = select.getAttribute('data-current-value') || 'US';
                select.value = currentValue;
                
                // If the current value doesn't exist in the list, default to US
                if (!select.value || select.value === '') {
                    select.value = 'US';
                }
            }
        } else {
            // Failed to load regions - keep default US option
        }
    }).catch(function (error) {
        // Error loading regions - keep default US option
        const debugInfo = 'REGIONS API ERROR DEBUG:\n' +
            'Error exists: ' + (error ? 'YES' : 'NO') + '\n' +
            'Error type: ' + typeof error + '\n' +
            'Error message: ' + (error?.message || 'UNDEFINED') + '\n' +
            'Full error: ' + JSON.stringify(error);
        
        Dashboard.alert('❌ REGIONS API ERROR:\n\n' + debugInfo);
    });
}
