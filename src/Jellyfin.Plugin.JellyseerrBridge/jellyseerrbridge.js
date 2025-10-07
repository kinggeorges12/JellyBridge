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
        
        if (!url) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Please enter a Jellyseerr URL first');
            return;
        }

        const testData = {
            JellyseerrUrl: url,
            ApiKey: apiKey
        };

        ApiClient.ajax({
            url: ApiClient.getUrl('JellyseerrBridge/TestConnection'),
            type: 'POST',
            data: JSON.stringify(testData),
            contentType: 'application/json'
        }).then(function (response) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'RESPONSE DEBUG:\n' +
                'Response exists: ' + (response ? 'YES' : 'NO') + '\n' +
                'Response type: ' + typeof response + '\n' +
                'Response success: ' + (response?.success ? 'YES' : 'NO') + '\n' +
                'Response message: ' + (response?.message || 'UNDEFINED') + '\n' +
                'Full response: ' + JSON.stringify(response);
            
            if (response && response.success) {
                Dashboard.alert('✅ CONNECTION SUCCESS!\n\n' + debugInfo);
            } else {
                Dashboard.alert('❌ CONNECTION FAILED!\n\n' + debugInfo);
            }
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'ERROR DEBUG:\n' +
                'Error exists: ' + (error ? 'YES' : 'NO') + '\n' +
                'Error type: ' + typeof error + '\n' +
                'Error message: ' + (error?.message || 'UNDEFINED') + '\n' +
                'Error name: ' + (error?.name || 'UNDEFINED') + '\n' +
                'Full error: ' + JSON.stringify(error);
            
            Dashboard.alert('❌ CONNECTION ERROR!\n\n' + debugInfo);
        });
    });
}
