const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

// Helper function to filter out active networks
function getAvailableNetworks(page, networks) {
    const activeNetworks = Array.from(page.querySelector('#activeProviders').options).map(option => option.value);
    Dashboard.alert(`🔍 DEBUG: getAvailableNetworks - Active networks: [${activeNetworks.join(', ')}]`);
    Dashboard.alert(`🔍 DEBUG: getAvailableNetworks - Checking ${networks.length} networks`);
    
    const filtered = networks.filter(network => {
        const networkName = network.name || network;
        const isActive = activeNetworks.includes(networkName);
        if (isActive) {
            Dashboard.alert(`🔍 DEBUG: Filtering out active network: ${networkName}`);
        }
        return !isActive;
    });
    
    Dashboard.alert(`🔍 DEBUG: getAvailableNetworks - Filtered to ${filtered.length} available networks`);
    return filtered;
}

// Helper function to update placeholders with backend defaults
function updatePlaceholdersFromBackend(page, config) {
    // Update placeholders with backend default values
    const jellyseerrUrlField = page.querySelector('#JellyseerrUrl');
    if (jellyseerrUrlField && !jellyseerrUrlField.value) {
        jellyseerrUrlField.placeholder = config.JellyseerrUrl || 'http://localhost:5055';
    }
    
    const userIdField = page.querySelector('#UserId');
    if (userIdField && !userIdField.value) {
        userIdField.placeholder = (config.UserId || 1).toString();
    }
    
    const syncIntervalField = page.querySelector('#SyncIntervalHours');
    if (syncIntervalField && !syncIntervalField.value) {
        syncIntervalField.placeholder = (config.SyncIntervalHours || 24).toString();
    }
    
    const libraryDirectoryField = page.querySelector('#LibraryDirectory');
    if (libraryDirectoryField && !libraryDirectoryField.value) {
        libraryDirectoryField.placeholder = config.LibraryDirectory || '/data/Jellyseerr';
    }
    
    const libraryPrefixField = page.querySelector('#LibraryPrefix');
    if (libraryPrefixField && !libraryPrefixField.value) {
        libraryPrefixField.placeholder = config.LibraryPrefix || 'Streaming - ';
    }
    
    const requestTimeoutField = page.querySelector('#RequestTimeout');
    if (requestTimeoutField && !requestTimeoutField.value) {
        requestTimeoutField.placeholder = (config.RequestTimeout || 30).toString();
    }
    
    const retryAttemptsField = page.querySelector('#RetryAttempts');
    if (retryAttemptsField && !retryAttemptsField.value) {
        retryAttemptsField.placeholder = (config.RetryAttempts || 3).toString();
    }
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
    const networkMapping = config.NetworkMap || {};
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

function loadAvailableNetworks(page, config) {
    const region = page.querySelector('#WatchProviderRegion').value;
    const networkMapping = config?.NetworkMap || {};
    
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/WatchProviders', { region: region }),
        type: 'GET',
        dataType: 'json'
    }).then(function(response) {
        Dashboard.alert(`🔍 DEBUG: API Response received for networks. Success: ${response?.success}, Networks count: ${response?.networks?.length || 0}`);
        
        if (response && response.success && response.networks) {
            const availableNetworks = response.networks
                .filter(network => network && network.name)
                .sort((a, b) => a.name.localeCompare(b.name));
            
            // Create a map of available networks by name for quick lookup
            const availableNetworkMap = new Map();
            availableNetworks.forEach(network => {
                availableNetworkMap.set(network.name, network);
            });
            
            // Add default networks that are not found in the region
            const missingNetworks = Object.entries(networkMapping)
                .filter(([networkName]) => !availableNetworkMap.has(networkName))
                .map(([name, id]) => ({ name, id }));
            
            // Combine available networks with missing default networks
            const allNetworks = [...availableNetworks, ...missingNetworks]
                .sort((a, b) => a.name.localeCompare(b.name));
            
            // Return the networks instead of just resolving
            return Promise.resolve(allNetworks);
        } else {
            // Return default networks with actual IDs from NetworkMap
            return Promise.resolve(Object.entries(networkMapping).map(([name, id]) => ({ name, id })));
        }
    }).catch(function(error) {
        Dashboard.alert(`❌ DEBUG: API call failed for networks. Error: ${error?.message || 'Unknown error'}`);
        
        // Use default networks as fallback
        // Return default networks with actual IDs from NetworkMap
        return Promise.resolve(Object.entries(networkMapping).map(([name, id]) => ({ name, id })));
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
    Dashboard.alert(`🔍 DEBUG: populateSelectWithProviders called with ${providers.length} networks`);
    
    selectElement.innerHTML = '';
    
    providers.forEach(provider => {
        const option = document.createElement('option');
        option.value = provider.name; // Store just the name as value for compatibility
        option.textContent = provider.id ? `${provider.name} (${provider.id})` : provider.name;
        option.dataset.providerId = provider.id || ''; // Store ID separately
        selectElement.appendChild(option);
    });
    
    Dashboard.alert(`🔍 DEBUG: Added ${selectElement.options.length} network options to select element`);
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
    options.sort((a, b) => (a.textContent || '').localeCompare(b.textContent || ''));
    
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
    
    // Validate required fields
    const apiKey = form.querySelector('#ApiKey').value.trim();
    const jellyseerrUrl = form.querySelector('#JellyseerrUrl').value.trim() || 'http://localhost:5055';
    
    if (!apiKey) {
        Dashboard.alert('API Key is required. Please enter your Jellyseerr API key.');
        return Promise.reject('API Key is required');
    }
    
    // Use our custom endpoint to get the current configuration
    return fetch('/JellyseerrBridge/GetPluginConfiguration')
        .then(response => response.json())
        .then(function (config) {
            // Update config with current form values
            config.IsEnabled = form.querySelector('#IsEnabled').checked;
            config.JellyseerrUrl = jellyseerrUrl;
            config.ApiKey = apiKey;
            config.LibraryDirectory = form.querySelector('#LibraryDirectory').value.trim() || '/data/Jellyseerr';
            config.UserId = parseInt(form.querySelector('#UserId').value) || 1;
            config.SyncIntervalHours = parseInt(form.querySelector('#SyncIntervalHours').value) || 24;
            config.ExcludeFromMainLibraries = form.querySelector('#ExcludeFromMainLibraries').checked;
            config.CreateSeparateLibraries = form.querySelector('#CreateSeparateLibraries').checked;
            config.LibraryPrefix = form.querySelector('#LibraryPrefix').value.trim() || 'Streaming - ';
            config.AutoSyncOnStartup = form.querySelector('#AutoSyncOnStartup').checked;
            config.WatchProviderRegion = form.querySelector('#WatchProviderRegion').value;
            config.ActiveNetworks = getActiveNetworks(view);
            config.NetworkMap = getNetworkNameToIdMapping(view);
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
                page.querySelector('#JellyseerrUrl').value = config.JellyseerrUrl || '';
                page.querySelector('#ApiKey').value = config.ApiKey || '';
                page.querySelector('#LibraryDirectory').value = config.LibraryDirectory || '';
                page.querySelector('#UserId').value = config.UserId || '';
                page.querySelector('#SyncIntervalHours').value = config.SyncIntervalHours || '';
                page.querySelector('#ExcludeFromMainLibraries').checked = config.ExcludeFromMainLibraries !== false;
                page.querySelector('#CreateSeparateLibraries').checked = config.CreateSeparateLibraries || false;
                page.querySelector('#LibraryPrefix').value = config.LibraryPrefix || '';
                page.querySelector('#AutoSyncOnStartup').checked = config.AutoSyncOnStartup || false;
                page.querySelector('#RequestTimeout').value = config.RequestTimeout || '';
                page.querySelector('#RetryAttempts').value = config.RetryAttempts || '';
                page.querySelector('#EnableDebugLogging').checked = config.EnableDebugLogging || false;
                
                // Store the current watch provider region value
                const watchProviderSelect = page.querySelector('#WatchProviderRegion');
                watchProviderSelect.setAttribute('data-current-value', config.WatchProviderRegion || 'US');
                
                // Store configuration globally for other functions to use
                window.jellyseerrConfig = config;
                
                // Update placeholders with backend defaults
                updatePlaceholdersFromBackend(page, config);
                
                // Initialize the multi-select interface
                initializeMultiSelect(page, config);
                
                // Initialize Library Prefix field state
                updateLibraryPrefixState(page);
                
                Dashboard.hideLoadingMsg();
            })
            .catch(function (error) {
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

    // Add refresh providers button functionality
    const refreshButton = view.querySelector('#refreshProviders');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            Dashboard.showLoadingMsg();
            loadWatchProviderRegions(view).then(function() {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('✅ Watch provider regions refreshed successfully!');
            }).catch(function(error) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('❌ Failed to refresh regions: ' + (error?.message || 'Unknown error'));
            });
        });
    }

    // Add refresh available providers button functionality
    const refreshAvailableButton = view.querySelector('#refreshAvailableProviders');
    if (refreshAvailableButton) {
        refreshAvailableButton.addEventListener('click', function() {
            Dashboard.showLoadingMsg();
            loadAvailableNetworks(view, config).then(function(allNetworks) {
                // Now refresh the available networks list with the loaded data
                const availableProvidersSelect = view.querySelector('#availableProviders');
                const availableNetworks = getAvailableNetworks(view, allNetworks);
                
                populateSelectWithProviders(availableProvidersSelect, availableNetworks);
                
                Dashboard.hideLoadingMsg();
                const activeCount = view.querySelector('#activeProviders').options.length;
                const availableCount = availableNetworks.length;
                Dashboard.alert(`✅ Available networks refreshed successfully! Found ${allNetworks.length} total networks (${activeCount} active, ${availableCount} available).`);
            }).catch(function(error) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('❌ Failed to refresh available networks: ' + (error?.message || 'Unknown error'));
            });
        });
    }
}

function loadWatchProviderRegions(page) {
    return ApiClient.ajax({
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
            return Promise.resolve();
        } else {
            // Failed to load regions - keep default US option
            return Promise.resolve();
        }
    }).catch(function (error) {
        // Re-throw the error so the calling function can handle it
        throw error;
    });
}

// Shared sync function
function performSync() {
    Dashboard.showLoadingMsg();
    
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/Sync'),
        type: 'POST',
        data: '{}',
        contentType: 'application/json',
        dataType: 'json'
    }).then(function(syncData) {
        Dashboard.hideLoadingMsg();
        
        if (syncData && syncData.success) {
            // Parse the sync results for better user feedback
            const message = syncData.message || 'Sync completed successfully';
            
            // Build detailed information if available
            let details = '';
            if (syncData.details) {
                details = `<br><br>Details:<br>${syncData.details.replace(/\n/g, '<br>')}`;
            } else if (syncData.moviesProcessed !== undefined || syncData.tvShowsProcessed !== undefined) {
                details = '<br><br>Summary:<br>';
                if (syncData.moviesProcessed !== undefined) {
                    details += `Movies: ${syncData.moviesProcessed} processed`;
                    if (syncData.moviesCreated !== undefined && syncData.moviesUpdated !== undefined) {
                        details += ` (${syncData.moviesCreated} created, ${syncData.moviesUpdated} updated)`;
                    }
                    details += '<br>';
                }
                if (syncData.tvShowsProcessed !== undefined) {
                    details += `TV Shows: ${syncData.tvShowsProcessed} processed`;
                    if (syncData.tvShowsCreated !== undefined && syncData.tvShowsUpdated !== undefined) {
                        details += ` (${syncData.tvShowsCreated} created, ${syncData.tvShowsUpdated} updated)`;
                    }
                    details += '<br>';
                }
                if (syncData.requestsProcessed !== undefined) {
                    details += `Requests: ${syncData.requestsProcessed} processed<br>`;
                }
            }
            
            Dashboard.alert('✅ Manual sync completed successfully!<br>' +
                `${message}${details}`);
        } else {
            throw new Error(syncData?.message || 'Sync failed');
        }
    }).catch(function(error) {
        Dashboard.hideLoadingMsg();
        Dashboard.alert('❌ Manual sync failed: ' + (error?.message || 'Unknown error'));
    });
}

// Complete manual sync workflow
function performManualSync(page) {
    // Show confirmation dialog for saving settings before sync
    Dashboard.confirm({
        title: 'Manual Sync',
        text: 'Save current settings before starting sync?',
        confirmText: 'Save & Sync',
        cancelText: 'Sync',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            // Save settings first, then sync
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                Dashboard.hideLoadingMsg();
                Dashboard.processPluginConfigurationUpdateResult(result);
                
                // Save completed, sync will happen after this block
            }).catch(function(error) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
            });
        }
        
        // Always sync after the if statement
        performSync();
    });
}



