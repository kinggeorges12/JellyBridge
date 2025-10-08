const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

// Function to save plugin configuration
function initializeMultiSelect(page, config) {
    const activeProvidersSelect = page.querySelector('#activeProviders');
    const availableProvidersSelect = page.querySelector('#availableProviders');
    const activeSearch = page.querySelector('#activeSearch');
    const availableSearch = page.querySelector('#availableSearch');
    const addButton = page.querySelector('#addSelected');
    const removeButton = page.querySelector('#removeSelected');
    const clearActiveSearch = page.querySelector('#clearActiveSearch');
    const clearAvailableSearch = page.querySelector('#clearAvailableSearch');
    
    // Store all available providers globally
    window.allAvailableProviders = [];
    
    // Load active providers from config
    const activeNetworks = config.ActiveNetworks || [];
    populateSelect(activeProvidersSelect, activeNetworks);
    
    // Load available providers from Jellyseerr API
    loadAvailableProviders(page);
    
    // Search functionality
    activeSearch.addEventListener('input', function() {
        filterSelect(activeProvidersSelect, this.value);
        updateClearButtonVisibility(clearActiveSearch, this.value);
    });
    
    availableSearch.addEventListener('input', function() {
        filterSelect(availableProvidersSelect, this.value);
        updateClearButtonVisibility(clearAvailableSearch, this.value);
    });
    
    // Clear button functionality
    clearActiveSearch.addEventListener('click', function() {
        activeSearch.value = '';
        filterSelect(activeProvidersSelect, '');
        updateClearButtonVisibility(clearActiveSearch, '');
        activeSearch.focus();
    });
    
    clearAvailableSearch.addEventListener('click', function() {
        availableSearch.value = '';
        filterSelect(availableProvidersSelect, '');
        updateClearButtonVisibility(clearAvailableSearch, '');
        availableSearch.focus();
    });
    
    // Initialize clear button visibility
    updateClearButtonVisibility(clearActiveSearch, activeSearch.value);
    updateClearButtonVisibility(clearAvailableSearch, availableSearch.value);
    
    // Add/Remove functionality
    addButton.addEventListener('click', function() {
        moveProviders(availableProvidersSelect, activeProvidersSelect);
    });
    
    removeButton.addEventListener('click', function() {
        moveProviders(activeProvidersSelect, availableProvidersSelect);
    });
    
    // Double-click to move items
    availableProvidersSelect.addEventListener('dblclick', function() {
        moveProviders(availableProvidersSelect, activeProvidersSelect);
    });
    
    activeProvidersSelect.addEventListener('dblclick', function() {
        moveProviders(activeProvidersSelect, availableProvidersSelect);
    });
}

function updateClearButtonVisibility(clearButton, searchValue) {
    if (searchValue && searchValue.trim() !== '') {
        clearButton.style.display = 'flex';
    } else {
        clearButton.style.display = 'none';
    }
}

function loadAvailableProviders(page) {
    const availableProvidersSelect = page.querySelector('#availableProviders');
    const region = page.querySelector('#WatchProviderRegion').value || 'US';
    
    ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/WatchProviders', { region: region }),
        type: 'GET',
        dataType: 'json'
    }).then(function(response) {
        if (response && response.success && response.providers) {
            const providerNames = response.providers.map(provider => provider.name).sort();
            window.allAvailableProviders = providerNames;
            
            // Filter out providers that are already active
            const activeProviders = Array.from(page.querySelector('#activeProviders').options).map(option => option.value);
            const availableProviders = providerNames.filter(name => !activeProviders.includes(name));
            
            populateSelect(availableProvidersSelect, availableProviders);
        } else {
            // Fallback to default networks if API fails
            const defaultNetworks = [
                "Netflix", "Disney+", "Prime Video", "Apple TV+", "Hulu", "HBO", "Discovery+",
                "ABC", "FOX", "Cinemax", "AMC", "Showtime", "Starz", "The CW", "NBC", "CBS",
                "Paramount+", "BBC One", "Cartoon Network", "Adult Swim", "Nickelodeon", "Peacock"
            ];
            window.allAvailableProviders = defaultNetworks;
            
            const activeProviders = Array.from(page.querySelector('#activeProviders').options).map(option => option.value);
            const availableProviders = defaultNetworks.filter(name => !activeProviders.includes(name));
            
            populateSelect(availableProvidersSelect, availableProviders);
        }
    }).catch(function(error) {
        console.error('Failed to load available providers:', error);
        // Use default networks as fallback
        const defaultNetworks = [
            "Netflix", "Disney+", "Prime Video", "Apple TV+", "Hulu", "HBO", "Discovery+",
            "ABC", "FOX", "Cinemax", "AMC", "Showtime", "Starz", "The CW", "NBC", "CBS",
            "Paramount+", "BBC One", "Cartoon Network", "Adult Swim", "Nickelodeon", "Peacock"
        ];
        window.allAvailableProviders = defaultNetworks;
        
        const activeProviders = Array.from(page.querySelector('#activeProviders').options).map(option => option.value);
        const availableProviders = defaultNetworks.filter(name => !activeProviders.includes(name));
        
        populateSelect(availableProvidersSelect, availableProviders);
    });
}

function populateSelect(selectElement, items) {
    selectElement.innerHTML = '';
    items.forEach(item => {
        const option = document.createElement('option');
        option.value = item;
        option.textContent = item;
        selectElement.appendChild(option);
    });
}

function filterSelect(selectElement, searchTerm) {
    const options = Array.from(selectElement.options);
    options.forEach(option => {
        const text = option.textContent.toLowerCase();
        const search = searchTerm.toLowerCase();
        option.style.display = text.includes(search) ? 'block' : 'none';
    });
}

function moveProviders(fromSelect, toSelect) {
    const selectedOptions = Array.from(fromSelect.selectedOptions);
    const movedValues = [];
    
    selectedOptions.forEach(option => {
        // Track the value of the moved item
        movedValues.push(option.value);
        
        // Add to destination
        const newOption = document.createElement('option');
        newOption.value = option.value;
        newOption.textContent = option.textContent;
        toSelect.appendChild(newOption);
        
        // Remove from source
        option.remove();
    });
    
    // Sort both selects
    sortSelectOptions(fromSelect);
    sortSelectOptions(toSelect);
    
    // Select the newly moved items in the destination
    movedValues.forEach(value => {
        const newOption = Array.from(toSelect.options).find(option => option.value === value);
        if (newOption) {
            newOption.selected = true;
        }
    });
}

function sortSelectOptions(selectElement) {
    const options = Array.from(selectElement.options);
    options.sort((a, b) => a.textContent.localeCompare(b.textContent));
    
    // Clear and re-add sorted options
    selectElement.innerHTML = '';
    options.forEach(option => {
        selectElement.appendChild(option);
    });
}

function getActiveNetworks(page) {
    const activeProvidersSelect = page.querySelector('#activeProviders');
    return Array.from(activeProvidersSelect.options).map(option => option.value);
}

function savePluginConfiguration(view) {
    const form = view.querySelector('#jellyseerrBridgeConfigurationForm');
    return ApiClient.getPluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId).then(function (config) {
        // Update config with current form values
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
        config.WatchProviderRegion = form.querySelector('#WatchProviderRegion').value;
        config.ActiveNetworks = getActiveNetworks(view);
        config.DefaultNetworks = config.ActiveNetworks.join('\n'); // Keep for backward compatibility
        config.RequestTimeout = parseInt(form.querySelector('#RequestTimeout').value) || 30;
        config.RetryAttempts = parseInt(form.querySelector('#RetryAttempts').value) || 3;
        config.EnableDebugLogging = form.querySelector('#EnableDebugLogging').checked;
        
        // Save the configuration
        return ApiClient.updatePluginConfiguration(JellyseerrBridgeConfigurationPage.pluginUniqueId, config);
    });
}

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
            
            // Initialize the multi-select interface
            initializeMultiSelect(page, config);
            
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
        // Use the reusable function to save configuration
        savePluginConfiguration(view).then(function (result) {
            Dashboard.hideLoadingMsg();
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
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
            
            const debugInfo = 'CONNECTION RESPONSE DEBUG:<br>' +
                'Response exists: ' + (data ? 'YES' : 'NO') + '<br>' +
                'Response type: ' + typeof data + '<br>' +
                'Response success: ' + (data?.success ? 'YES' : 'NO') + '<br>' +
                'Response message: ' + (data?.message || 'UNDEFINED') + '<br>' +
                'Response status: ' + (data?.status || 'UNDEFINED') + '<br>' +
                'Full response: ' + JSON.stringify(data) + '<br>' +
                'Response keys: ' + (data ? Object.keys(data).join(', ') : 'NONE');
            
            if (data && data.success) {
                Dashboard.alert('✅ Connected to Jellyseerr!');
                // Show confirmation dialog for saving settings
                Dashboard.confirm({
                        title: 'Connection Success!',
                        text: 'Save connection settings now?',
                        confirmText: 'Confirm',
                        cancelText: 'Cancel',
                        primary: "confirm"
                    }, 'Title', (confirmed) => {
                        if (confirmed) {
                            // Save the current settings using the reusable function
                            savePluginConfiguration(view).then(function (result) {
                                Dashboard.hideLoadingMsg();
                                Dashboard.processPluginConfigurationUpdateResult(result);
                            }).catch(function (error) {
                                Dashboard.hideLoadingMsg();
                                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                            });
                        } else {
                            Dashboard.alert('🚫 Exited without saving');
                        }
                    });
            } else {
                Dashboard.alert('❌ CONNECTION FAILED!<br>' + debugInfo);
            }
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'CONNECTION ERROR DEBUG:<br>' +
                'Error exists: ' + (error ? 'YES' : 'NO') + '<br>' +
                'Error type: ' + typeof error + '<br>' +
                'Error message: ' + (error?.message || 'UNDEFINED') + '<br>' +
                'Error name: ' + (error?.name || 'UNDEFINED') + '<br>' +
                'Error status: ' + (error?.status || 'UNDEFINED') + '<br>' +
                'Full error: ' + JSON.stringify(error);
            
            Dashboard.alert('❌ CONNECTION ERROR!<br>' + debugInfo);
        });
    });

    const syncButton = view.querySelector('#manualSync');
    if (!syncButton) {
        Dashboard.alert('❌ Jellyseerr Bridge: Manual sync button not found');
        return;
    }
    
    syncButton.addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        
        // First save the current settings using the reusable function
        savePluginConfiguration(view).then(function (result) {
            Dashboard.hideLoadingMsg();
            Dashboard.processPluginConfigurationUpdateResult(result);
            // Settings saved, now load watch provider regions
            loadWatchProviderRegions(view);
            
            // Get selected region for watch providers
            const selectedRegion = view.querySelector('#WatchProviderRegion')?.value || 'US';
            
            // Fetch watch providers for the selected region
            return ApiClient.ajax({
                url: ApiClient.getUrl(`JellyseerrBridge/WatchProviders?region=${selectedRegion}`),
                type: 'GET',
                dataType: 'json'
            });
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
            return Promise.reject(error);
        }).then(function (providersData) {
            // Now do the sync using saved plugin settings
            return ApiClient.ajax({
                url: ApiClient.getUrl('JellyseerrBridge/Sync'),
                type: 'POST',
                data: '{}',
                contentType: 'application/json',
                dataType: 'json'
            }).then(function (syncData) {
                Dashboard.hideLoadingMsg();
                
                // Create debug info with both sync and providers data
                let debugInfo = 'SYNC RESPONSE DEBUG:<br>' +
                    'Response exists: ' + (syncData ? 'YES' : 'NO') + '<br>' +
                    'Response type: ' + typeof syncData + '<br>' +
                    'Response success: ' + (syncData?.success ? 'YES' : 'NO') + '<br>' +
                    'Response message: ' + (syncData?.message || 'UNDEFINED') + '<br>';
                
                debugInfo += 'WATCH PROVIDERS DEBUG:<br>' +
                    'Region: ' + (view.querySelector('#WatchProviderRegion')?.value || 'US') + '<br>' +
                    'Providers response exists: ' + (providersData ? 'YES' : 'NO') + '<br>' +
                    'Providers success: ' + (providersData?.success ? 'YES' : 'NO') + '<br>' +
                    'Providers count: ' + (providersData?.providers ? providersData.providers.length : 'UNDEFINED') + '<br>';
                
                if (providersData?.success && providersData.providers) {
                    debugInfo += 'PROVIDERS LIST:<br>';
                    providersData.providers.slice(0, 10).forEach(provider => {
                        debugInfo += `- ${provider.name} (ID: ${provider.id})<br>`;
                    });
                    if (providersData.providers.length > 10) {
                        debugInfo += `... and ${providersData.providers.length - 10} more<br>`;
                    }
                }
                
                if (syncData && syncData.success) {
                    Dashboard.alert('✅ SYNC SUCCESS!<br>' + debugInfo);
                } else {
                    Dashboard.alert('❌ SYNC FAILED!<br>' + debugInfo);
                }
            });
        }).catch(function (error) {
            Dashboard.hideLoadingMsg();
            const debugInfo = 'SYNC ERROR DEBUG:<br>' +
                'Error exists: ' + (error ? 'YES' : 'NO') + '<br>' +
                'Error type: ' + typeof error + '<br>' +
                'Error message: ' + (error?.message || 'UNDEFINED') + '<br>' +
                'Error name: ' + (error?.name || 'UNDEFINED') + '<br>' +
                'Error status: ' + (error?.status || 'UNDEFINED') + '<br>' +
                'Full error: ' + JSON.stringify(error);
            
            Dashboard.alert('❌ SYNC ERROR!<br>' + debugInfo);
        });
    });
}

function loadWatchProviderRegions(page) {
    ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/WatchProviderRegions'),
        type: 'GET',
        dataType: 'json'
    }).then(function (data) {
        if (data && data.success && data.regions) {
            const select = page.querySelector('#WatchProviderRegion');
            if (select) {
                // Clear existing options
                select.innerHTML = '';
                
                // Sort regions by English name (with null safety)
                const sortedRegions = data.regions
                    .filter(region => region.english_name && region.english_name.trim() !== '')
                    .sort((a, b) => a.english_name.localeCompare(b.english_name));
                
                // Add options for each region
                sortedRegions.forEach(region => {
                    const option = document.createElement('option');
                    option.value = region.iso_3166_1;
                    option.textContent = `${region.english_name} (${region.native_name})`;
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
        const debugInfo = 'REGIONS API ERROR DEBUG:<br>' +
            'Error exists: ' + (error ? 'YES' : 'NO') + '<br>' +
            'Error type: ' + typeof error + '<br>' +
            'Error message: ' + (error?.message || 'UNDEFINED') + '<br>' +
            'Full error: ' + JSON.stringify(error);
        
        Dashboard.alert('❌ REGIONS API ERROR!<br>' + debugInfo);
    });
}
