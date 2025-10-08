const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

// Helper function to filter out active providers
function getAvailableProviders(page, providers) {
    const activeProviders = Array.from(page.querySelector('#activeProviders').options).map(option => option.value);
    return providers.filter(provider => !activeProviders.includes(provider.name || provider));
}

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
    const networkMapping = config.NetworkNameToId || {};
    populateActiveProvidersWithIds(activeProvidersSelect, activeNetworks, networkMapping);
    
    // Don't load available providers on page load - only when Manual Sync is clicked
    populateSelectWithNetworkNames(availableProvidersSelect, []);
    
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
            const providers = response.providers.sort((a, b) => a.name.localeCompare(b.name));
            window.allAvailableProviders = providers;
            
            // Filter out providers that are already active
            const availableProviders = getAvailableProviders(page, providers);
            
            populateSelectWithProviders(availableProvidersSelect, availableProviders);
        } else {
            // Fallback to default networks if API fails
            window.allAvailableProviders = window.jellyseerrDefaultNetworks || [];
            
            const availableProviders = getAvailableProviders(page, window.jellyseerrDefaultNetworks || []);
            populateSelectWithNetworkNames(availableProvidersSelect, availableProviders);
        }
    }).catch(function(error) {
        console.error('Failed to load available providers:', error);
        // Use default networks as fallback
        window.allAvailableProviders = window.jellyseerrDefaultNetworks || [];
        
        const availableProviders = getAvailableProviders(page, window.jellyseerrDefaultNetworks || []);
        populateSelectWithNetworkNames(availableProvidersSelect, availableProviders);
    });
}

function populateActiveProvidersWithIds(selectElement, networkNames, networkMapping) {
    selectElement.innerHTML = '';
    
    networkNames.forEach(networkName => {
        const option = document.createElement('option');
        option.value = networkName;
        const networkId = networkMapping[networkName];
        option.textContent = networkId ? `${networkName} (${networkId})` : networkName;
        if (networkId) {
            option.dataset.providerId = networkId.toString();
        }
        selectElement.appendChild(option);
    });
}

function populateSelectWithProviders(selectElement, providers) {
    selectElement.innerHTML = '';
    
    providers.forEach(provider => {
        const option = document.createElement('option');
        option.value = provider.name; // Store just the name as value for compatibility
        option.textContent = provider.id ? `${provider.name} (${provider.id})` : provider.name;
        option.dataset.providerId = provider.id || ''; // Store ID separately
        selectElement.appendChild(option);
    });
}

function populateSelectWithNetworkNames(selectElement, networkNames) {
    selectElement.innerHTML = '';
    
    networkNames.forEach(networkName => {
        const option = document.createElement('option');
        option.value = networkName;
        option.textContent = networkName;
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
        // Check if this option already exists in the destination
        const existingOption = Array.from(toSelect.options).find(existing => existing.value === option.value);
        if (existingOption) {
            // Option already exists, just remove from source
            option.remove();
            return;
        }
        
        // Track the value of the moved item
        movedValues.push(option.value);
        
        // Add to destination
        const newOption = document.createElement('option');
        newOption.value = option.value;
        newOption.textContent = option.textContent;
        if (option.dataset.providerId) {
            newOption.dataset.providerId = option.dataset.providerId;
        }
        toSelect.appendChild(newOption);
        
        // Remove from source
        option.remove();
    });
    
    // Only sort the destination (available providers), maintain order in active providers
    if (toSelect.id === 'availableProviders') {
        sortSelectOptions(toSelect);
    }
    
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
    const networks = Array.from(activeProvidersSelect.options).map(option => option.value);
    // Return unique networks only (in case of any duplicates)
    return [...new Set(networks)];
}

function getNetworkNameToIdMapping(page) {
    const activeProvidersSelect = page.querySelector('#activeProviders');
    const mapping = {};
    
    Array.from(activeProvidersSelect.options).forEach(option => {
        if (option.dataset.providerId) {
            mapping[option.value] = parseInt(option.dataset.providerId);
        }
    });
    
    return mapping;
}

function savePluginConfiguration(view) {
    const form = view.querySelector('#jellyseerrBridgeConfigurationForm');
    
    // Use our custom endpoint to get the current configuration
    return fetch('/JellyseerrBridge/GetPluginConfiguration')
        .then(response => response.json())
        .then(function (config) {
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
            config.NetworkNameToId = getNetworkNameToIdMapping(view);
            config.RequestTimeout = parseInt(form.querySelector('#RequestTimeout').value) || 30;
            config.RetryAttempts = parseInt(form.querySelector('#RetryAttempts').value) || 3;
            config.EnableDebugLogging = form.querySelector('#EnableDebugLogging').checked;
            
            // Save the configuration using our custom endpoint
            return fetch('/JellyseerrBridge/UpdatePluginConfiguration', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(config)
            });
        })
        .then(response => response.json())
        .then(function (result) {
            if (result.success) {
                console.log('Configuration saved successfully');
                return result;
            } else {
                throw new Error(result.error || 'Failed to save configuration');
            }
        });
}

function updateLibraryPrefixState(page) {
    const createSeparateLibrariesCheckbox = page.querySelector('#CreateSeparateLibraries');
    const libraryPrefixInput = page.querySelector('#LibraryPrefix');
    const libraryPrefixLabel = page.querySelector('label[for="LibraryPrefix"]');
    
    if (!createSeparateLibrariesCheckbox || !libraryPrefixInput) {
        return;
    }
    
    const isEnabled = createSeparateLibrariesCheckbox.checked;
    
    // Enable/disable the input
    libraryPrefixInput.disabled = !isEnabled;
    
    // Add/remove disabled styling
    if (isEnabled) {
        libraryPrefixInput.classList.remove('disabled');
        libraryPrefixLabel.classList.remove('disabled');
    } else {
        libraryPrefixInput.classList.add('disabled');
        libraryPrefixLabel.classList.add('disabled');
    }
}

export default function (view) {
    if (!view) {
        Dashboard.alert('❌ Jellyseerr Bridge: View parameter is undefined');
        return;
    }
    
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        
        // Use our custom endpoint to get the configuration
        fetch('/JellyseerrBridge/GetPluginConfiguration')
            .then(response => response.json())
            .then(function (config) {
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
                
                // Store default networks globally for fallback use
                window.jellyseerrDefaultNetworks = config.JellyseerrDefaultNetworks || [];
                
                // Initialize the multi-select interface
                initializeMultiSelect(page, config);
                
                // Initialize Library Prefix field state
                updateLibraryPrefixState(page);
                
                Dashboard.hideLoadingMsg();
            })
            .catch(function (error) {
                console.error('Error loading configuration:', error);
                Dashboard.hideLoadingMsg();
                Dashboard.alert('❌ Failed to load configuration: ' + error.message);
            });
    });
    
    // Add event listener for Create Separate Libraries checkbox
    const createSeparateLibrariesCheckbox = view.querySelector('#CreateSeparateLibraries');
    if (createSeparateLibrariesCheckbox) {
        createSeparateLibrariesCheckbox.addEventListener('change', function() {
            updateLibraryPrefixState(view);
        });
    }
    
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
        performManualSync(view);
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

// Step 2: Retrieve providers for a specific region
function loadProvidersForRegion(page, region) {
    return ApiClient.ajax({
        url: ApiClient.getUrl(`JellyseerrBridge/WatchProviders?region=${region}`),
        type: 'GET',
        dataType: 'json'
    }).then(function(response) {
        if (response && response.success && response.providers) {
            const providers = response.providers.sort((a, b) => a.name.localeCompare(b.name));
            window.allAvailableProviders = providers;
            
            // Filter out providers that are already active
            const availableProviders = getAvailableProviders(page, providers);
            
            const availableProvidersSelect = page.querySelector('#availableProviders');
            populateSelectWithProviders(availableProvidersSelect, availableProviders);
            
            return providers;
        } else {
            throw new Error('Failed to load watch providers');
        }
    });
}

// Step 3: Retrieve movies and TV shows from active providers
function loadMoviesAndTvFromProviders(page) {
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/Sync'),
        type: 'POST',
        data: '{}',
        contentType: 'application/json',
        dataType: 'json'
    }).then(function(syncData) {
        if (syncData && syncData.success) {
            return syncData;
        } else {
            throw new Error(syncData?.message || 'Sync failed');
        }
    });
}

// Complete manual sync workflow
function performManualSync(page) {
    Dashboard.showLoadingMsg();
    
    // Step 1: Save current configuration
    return savePluginConfiguration(page).then(function(result) {
        Dashboard.hideLoadingMsg();
        Dashboard.processPluginConfigurationUpdateResult(result);
        
        // Step 2: Load regions
        return loadWatchProviderRegions(page);
    }).then(function(regions) {
        // Step 3: Get selected region and load providers
        const selectedRegion = page.querySelector('#WatchProviderRegion')?.value || 'US';
        return loadProvidersForRegion(page, selectedRegion);
    }).then(function(providers) {
        // Step 4: Load movies and TV shows
        return loadMoviesAndTvFromProviders(page);
    }).then(function(syncData) {
        Dashboard.hideLoadingMsg();
        Dashboard.alert('✅ Manual sync completed successfully!<br>' +
            `Loaded ${syncData?.message || 'data'} from Jellyseerr`);
    }).catch(function(error) {
        Dashboard.hideLoadingMsg();
        Dashboard.alert('❌ Manual sync failed: ' + (error?.message || 'Unknown error'));
    });
}
