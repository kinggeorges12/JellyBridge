const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

export default function (view) {
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
            Dashboard.hideLoadingMsg();
        });
    });
    
    view.querySelector('#jellyseerrBridgeConfigurationForm').addEventListener('submit', function (e) {
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

            ApiClient.updatePluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        });
        e.preventDefault();
        return false;
    });

    view.querySelector('#testConnection').addEventListener('click', function () {
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
            if (response.success) {
                Dashboard.alert('✅ ' + response.message);
            } else {
                Dashboard.alert('❌ ' + response.message);
            }
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('❌ Connection test failed: ' + error.message);
        });
    });
}
