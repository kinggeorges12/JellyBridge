const JellyBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

export default function (view) {
    if (!view) {
        Dashboard.alert('❌ Jellyseerr Bridge: View parameter is undefined');
        return;
    }
    
    let isInitialized = false;
    
    view.addEventListener('viewshow', function () {
        if (isInitialized) {
            return; // Prevent duplicate initialization
        }
        
        Dashboard.showLoadingMsg();
        const page = this;
        
        // Use our custom endpoint to get the configuration
        fetch('/JellyBridge/PluginConfiguration')
            .then(response => response.json())
            .then(function (config) {
                // Store configuration globally for other functions to use
                window.configJellyBridge = config;
                
                // Initialize general settings including test connection
                initializeGeneralSettings(page);
                
                // Initialize library settings
                initializeLibrarySettings(page);
                
                // Initialize sync settings including network interface and sync buttons
                initializeSyncSettings(page);
                
                // Initialize advanced settings
                initializeAdvancedSettings(page);
                
                // Scroll to top of page after successful initialization
                scrollToElement('jellyBridgeConfigurationPage');
                
                // Start task status polling
                startTaskStatusPolling(page);
                
                isInitialized = true;
            })
            .catch(function (error) {
                Dashboard.alert('❌ Failed to load configuration: ' + error.message);
                scrollToElement('jellyBridgeConfigurationPage');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
    });
    
}

// ==========================================
// TASK STATUS FUNCTIONS
// ==========================================

let taskStatusInterval = null;

function startTaskStatusPolling(page) {
    // Clear any existing interval
    if (taskStatusInterval) {
        clearInterval(taskStatusInterval);
    }
    
    // Initial check
    checkTaskStatus(page);
    
    // Poll every 10 seconds
    taskStatusInterval = setInterval(() => {
        checkTaskStatus(page);
    }, 10000);
    
    // Add refresh button handler
    const refreshButton = page.querySelector('#refreshTaskStatus');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            checkTaskStatus(page);
        });
    }
}

function stopTaskStatusPolling() {
    if (taskStatusInterval) {
        clearInterval(taskStatusInterval);
        taskStatusInterval = null;
    }
}

function checkTaskStatus(page) {
    ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/TaskStatus'),
        type: 'GET',
        dataType: 'json'
    }).then(function(result) {
        updateTaskStatusDisplay(page, result);
    }).catch(function(error) {
        console.error('Failed to get task status:', error);
        updateTaskStatusDisplay(page, {
            isRunning: false,
            status: 'Error',
            progress: 0,
            message: 'Failed to get task status'
        });
    });
}

function updateTaskStatusDisplay(page, taskData) {
    const statusText = page.querySelector('#taskStatusText');
    const progressContainer = page.querySelector('#taskProgressContainer');
    const progressBar = page.querySelector('#taskProgressBar');
    const progressText = page.querySelector('#taskProgressText');
    const taskStatusTimes = page.querySelector('#taskStatusTimes');
    
    if (!statusText || !progressContainer || !progressBar || !progressText || !taskStatusTimes) {
        return;
    }

    // Set status bar
    if (taskData.isRunning) {
        statusText.textContent = '🔄 Running';
        statusText.style.color = '#00a4d6';
        progressContainer.style.display = 'block';
        
        const progress = taskData.progress !== null && taskData.progress !== undefined ? Math.round(taskData.progress) : 0;
        progressBar.style.width = progress + '%';
        progressText.textContent = `${progress}% - ${taskData.message || 'Syncing...'}`;
    } else {
        statusText.textContent = taskData.status === 'Error' ? '❌ Error' : '✅ Idle';
        statusText.style.color = taskData.status === 'Error' ? '#ff6b6b' : '#00d4aa';
        progressContainer.style.display = 'none';
    }
    
    // Set status times
    let runInfo = '';
    runInfo += `Refreshed at: ${new Date().toLocaleString()}`;

    if (taskData.lastRun) {
        if (runInfo) runInfo += ' • ';
        const src = taskData.lastRunSource ? ` (${taskData.lastRunSource.toLowerCase()})` : '';
        runInfo += `Last run${src}: ${new Date(taskData.lastRun).toLocaleString()}`;
    } else {
        if (runInfo) runInfo += ' • ';
        runInfo += 'No previous runs since startup';
    }
    if (taskData.nextRun) {
        if (runInfo) runInfo += ' • ';
        runInfo += `Next run: ${new Date(taskData.nextRun).toLocaleString()}`;
        
        // Add tooltip explaining next run time calculation
        if (!taskData.lastRun || taskData.lastRunSource === 'Startup') {
            taskStatusTimes.setAttribute('title', 'When there is no previous scheduled run, the next run is scheduled 1 hour after plugin load time. After the first run, it follows the configured sync interval.');
        } else {
            taskStatusTimes.removeAttribute('title');
        }
    } else {
        taskStatusTimes.removeAttribute('title');
    }

    taskStatusTimes.textContent = runInfo || 'No run information available';
}

// ==========================================
// GENERAL SETTINGS FUNCTIONS
// ==========================================

function initializeGeneralSettings(page) {
    // Set general settings form values with null handling
    setInputField(page, 'IsEnabled', true);
    setInputField(page, 'JellyseerrUrl');
    setInputField(page, 'ApiKey');
    setInputField(page, 'SyncIntervalHours');
    setInputField(page, 'AutoSyncOnStartup', true);
    
    // Test connection button functionality
    const testButton = page.querySelector('#testConnection');
    testButton.addEventListener('click', function () {
        performTestConnection(page);
    });
    
    // Library setup help button functionality
    const helpButton = page.querySelector('#librarySetupHelp');
    if (helpButton) {
        helpButton.addEventListener('click', function () {
            const instructionsDiv = page.querySelector('#librarySetupInstructions');
            if (instructionsDiv) {
                const isVisible = instructionsDiv.style.display !== 'none';
                instructionsDiv.style.display = isVisible ? 'none' : 'block';
            }
        });
    }
    
    // Add form submit event listener
    const form = page.querySelector('#jellyBridgeConfigurationForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            Dashboard.showLoadingMsg();
            // Use the reusable function to save configuration
            savePluginConfiguration(page).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
                checkTaskStatus(page);
            }).catch(function (error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
            e.preventDefault();
            return false;
        });
    }
}

function performTestConnection(page) {
    const testButton = page.querySelector('#testConnection');
    const url = page.querySelector('#JellyseerrUrl').value;
    const apiKey = page.querySelector('#ApiKey').value.trim();
    
    // Validate URL format if provided
    if (url && !validateField('JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
    
    // Validate API Key
    if (!validateField('ApiKey', validators.notNull, 'API Key is required for connection test').isValid) return;
    
    testButton.disabled = true;
    Dashboard.showLoadingMsg();
    
    const testData = {
        JellyseerrUrl: url,
        ApiKey: apiKey
    };

    ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/TestConnection'),
        type: 'POST',
        data: JSON.stringify(testData),
        contentType: 'application/json',
        dataType: 'json'
    }).then(function (data) {
        // HTTP 200 response means connection test was successful
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
                    savePluginConfiguration(page).then(function (result) {
                        Dashboard.processPluginConfigurationUpdateResult(result);
                        checkTaskStatus(page);
                    }).catch(function (error) {
                        Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                    }).finally(function() {
                        Dashboard.hideLoadingMsg();
                    });
                } else {
                    Dashboard.alert('🚫 Exited without saving');
                }
            });
    }).catch(function (error) {
        // Handle different types of errors
        let errorMessage = '❌ Connection test failed';
        
        if (error && error.responseJSON) {
            // Server returned structured error response
            const errorData = error.responseJSON;
            errorMessage = `❌ ${errorData.message || 'Connection test failed'}`;
            if (errorData.details) {
                errorMessage += `<br><br>Details: ${errorData.details}`;
            }
        } else if (error && error.status) {
            // HTTP status code error
            switch (error.status) {
                case 400:
                    errorMessage = '❌ Bad Request: Cannot reach URL';
                    break;
                case 401:
                    errorMessage = '❌ Unauthorized: Invalid API Key';
                    break;
                case 403:
                    errorMessage = '❌ Forbidden: Insufficient privileges - API key lacks required permissions';
                    break;
                case 500:
                    errorMessage = '❌ Server Error: Connection test failed';
                    break;
                default:
                    errorMessage = `❌ Connection test failed (HTTP ${error.status})`;
            }
        } else {
            // Generic error
            errorMessage = '❌ Connection test failed: ' + (error?.message || 'Unknown error');
        }
        
        Dashboard.alert(errorMessage);
    }).finally(function() {
        Dashboard.hideLoadingMsg();
        testButton.disabled = false;
    });
}

// ==========================================
// LIBRARY SETTINGS FUNCTIONS
// ==========================================

function initializeLibrarySettings(page) {
    // Set library settings form values with null handling
    setInputField(page, 'LibraryDirectory');
    setInputField(page, 'ExcludeFromMainLibraries', true);
    setInputField(page, 'CreateSeparateLibraries', true);
    setInputField(page, 'LibraryPrefix');
    setInputField(page, 'ManageJellyseerrLibrary', true);
    
    updateLibraryPrefixState();
    
    // Sync Favorites button functionality
    const syncFavoritesButton = page.querySelector('#syncFavorites');
    syncFavoritesButton.addEventListener('click', function() {
        performSyncFavorites(page);
    });
}

function updateLibraryPrefixState() {
    const createSeparateLibrariesCheckbox = document.querySelector('#CreateSeparateLibraries');
    const libraryPrefixInput = document.querySelector('#LibraryPrefix');
    const libraryPrefixContainer = document.querySelector('#LibraryPrefixContainer');
    const separateLibrariesWarning = document.querySelector('#separateLibrariesWarning');
    
    const isEnabled = createSeparateLibrariesCheckbox.checked;
    
    // Enable/disable the input
    libraryPrefixInput.disabled = !isEnabled;
    
    // Show/hide warning message
    if (separateLibrariesWarning) {
        separateLibrariesWarning.style.display = isEnabled ? 'block' : 'none';
    }
    
    // Add/remove disabled styling to both input and container
    if (isEnabled) {
        libraryPrefixInput.classList.remove('disabled');
        if (libraryPrefixContainer) {
            libraryPrefixContainer.classList.remove('disabled');
        }
    } else {
        libraryPrefixInput.classList.add('disabled');
        if (libraryPrefixContainer) {
            libraryPrefixContainer.classList.add('disabled');
        }
    }
}

function performSyncFavorites(page) {
    const syncFavoritesButton = page.querySelector('#syncFavorites');
    
    // Show confirmation dialog for saving settings before sync
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting favorites sync.',
        confirmText: 'Save & Sync',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            syncFavoritesButton.disabled = true;
            // Save settings first, then sync
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the sync result textbox
                const syncFavoritesResult = page.querySelector('#syncFavoritesResult');
                syncFavoritesResult.textContent = '🔄 Syncing to Jellyseerr...';
                syncFavoritesResult.style.display = 'block';
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // sync if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/SyncFavorites'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(syncResult) {
                    let resultText = `Sync to Jellyseerr Results:\n`;
                    resultText += `${syncResult.message || 'No message'}\n`;
                    
                    if (syncResult.details) {
                        resultText += `\nDetails: ${syncResult.details}\n`;
                    }
                    
                    resultText += `\nMovies Result:\n`;
                    resultText += `  Processed: ${syncResult.moviesResult?.moviesProcessed || 0}\n`;
                    resultText += `  Updated: ${syncResult.moviesResult?.moviesUpdated || 0}\n`;
                    resultText += `  Created: ${syncResult.moviesResult?.moviesCreated || 0}\n`;
                    
                    resultText += `\nShows Result:\n`;
                    resultText += `  Processed: ${syncResult.showsResult?.showsProcessed || 0}\n`;
                    resultText += `  Updated: ${syncResult.showsResult?.showsUpdated || 0}\n`;
                    resultText += `  Created: ${syncResult.showsResult?.showsCreated || 0}\n`;
                    
                    syncFavoritesResult.textContent = resultText;
                    scrollToElement('syncFavoritesResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Sync favorites failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `Sync to Jellyseerr Results:\n`;
                    resultText += `❌ Sync failed: ${error?.message || 'Unknown error'}\n`;
                    
                    syncFavoritesResult.textContent = resultText;
                    scrollToElement('syncFavoritesResult');
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
                syncFavoritesButton.disabled = false;
            });
        }
    });
}

// ==========================================
// DISCOVER SETTINGS FUNCTIONS
// ==========================================

function initializeSyncSettings(page) {
    const config = window.configJellyBridge || {};
    
    const activeNetworksSelect = page.querySelector('#activeNetworks');
    const availableNetworksSelect = page.querySelector('#availableNetworks');
    const activeNetworkSearch = page.querySelector('#activeNetworkSearch');
    const availableNetworkSearch = page.querySelector('#availableNetworkSearch');
    const addSelectedNetworksButton = page.querySelector('#addSelectedNetworks');
    const removeSelectedNetworksButton = page.querySelector('#removeSelectedNetworks');
    const clearActiveNetworkSearch = page.querySelector('#clearActiveNetworkSearch');
    const clearAvailableNetworkSearch = page.querySelector('#clearAvailableNetworkSearch');
    
    // Populate region settings
    const regionSelect = config.Region || config.DefaultValues.Region;
    populateRegion(page, [{ iso_3166_1: regionSelect }], regionSelect);
    
    // Load active networks from saved configuration
	// If NetworkMap is null/undefined, fall back to defaults; if it's an empty array, keep it empty
	const defaultNetworkMap = (config.DefaultValues && Array.isArray(config.DefaultValues.NetworkMap)) ? config.DefaultValues.NetworkMap : [];
	const activeNetworksSource = Array.isArray(config.NetworkMap) ? config.NetworkMap : defaultNetworkMap;
	populateSelectWithNetworks(activeNetworksSelect, activeNetworksSource);
    sortSelectOptions(activeNetworksSelect);
    
    // Update available networks with default networks that aren't already active
    updateAvailableNetworks(page);
    
    // Search functionality
    activeNetworkSearch.addEventListener('input', function() {
        filterSelect(activeNetworksSelect, this.value);
        updateClearButtonVisibility(clearActiveNetworkSearch, this.value);
    });
    
    availableNetworkSearch.addEventListener('input', function() {
        filterSelect(availableNetworksSelect, this.value);
        updateClearButtonVisibility(clearAvailableNetworkSearch, this.value);
    });
    
    // Clear button functionality
    clearActiveNetworkSearch.addEventListener('click', function() {
        activeNetworkSearch.value = '';
        filterSelect(activeNetworksSelect, '');
        updateClearButtonVisibility(clearActiveNetworkSearch, '');
        activeNetworkSearch.focus();
    });
    
    clearAvailableNetworkSearch.addEventListener('click', function() {
        availableNetworkSearch.value = '';
        filterSelect(availableNetworksSelect, '');
        updateClearButtonVisibility(clearAvailableNetworkSearch, '');
        availableNetworkSearch.focus();
    });
    
    // Initialize clear button visibility
    updateClearButtonVisibility(clearActiveNetworkSearch, activeNetworkSearch.value);
    updateClearButtonVisibility(clearAvailableNetworkSearch, availableNetworkSearch.value);
    
    // Add/Remove functionality
    addSelectedNetworksButton.addEventListener('click', function() {
        moveNetworks(availableNetworksSelect, activeNetworksSelect);
    });
    
    removeSelectedNetworksButton.addEventListener('click', function() {
        moveNetworks(activeNetworksSelect, availableNetworksSelect);
    });
    
    // Double-click to move items
    availableNetworksSelect.addEventListener('dblclick', function() {
        moveNetworks(availableNetworksSelect, activeNetworksSelect);
    });
    
    activeNetworksSelect.addEventListener('dblclick', function() {
        moveNetworks(activeNetworksSelect, availableNetworksSelect);
    });
    
    // Add refresh available networks button functionality
    const refreshAvailableButton = page.querySelector('#refreshAvailableNetworks');
    if (refreshAvailableButton) {
        refreshAvailableButton.addEventListener('click', function() {
            Dashboard.showLoadingMsg();
            loadAvailableNetworks(page).then(function(availableNetworks) {
                Dashboard.alert(`✅ Refreshed available networks.`);
                scrollToElement('availableNetworksSelectBox');
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to refresh available networks: ' + (error?.message || 'Unknown error'));
                scrollToElement('syncSettings');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
    
    // Add Run full sync button functionality (matches scheduled task behavior)
    const runButton = page.querySelector('#runSyncTask');
    if (runButton) {
        runButton.addEventListener('click', function () {
            // Prompt to save config before running
            Dashboard.confirm({
                title: 'Confirm Save',
                text: 'Settings will be saved before starting full sync.',
                confirmText: 'Save & Run',
                cancelText: 'Cancel',
                primary: 'confirm'
            }, 'Title', (confirmed) => {
                if (!confirmed) return;

                runButton.disabled = true;
                Dashboard.showLoadingMsg();

                // Save settings first
                savePluginConfiguration(page)
                    .then(function (result) {
                        Dashboard.processPluginConfigurationUpdateResult(result);
                        // Then run full sync
                        return ApiClient.ajax({
                            url: ApiClient.getUrl('JellyBridge/RunSync'),
                            type: 'POST',
                            data: '{}',
                            contentType: 'application/json',
                            dataType: 'json'
                        });
                    })
                    .then(function (data) {
                        const ok = !!(data && data.success === true);
                        const msg = data?.message || (ok ? 'Full sync completed successfully' : 'Full sync completed');
                        Dashboard.alert((ok ? '✅ ' : '⚠️ ') + msg);
                        // Refresh task status after run
                        const refreshBtn = page.querySelector('#refreshTaskStatus');
                        if (refreshBtn) refreshBtn.click();
                        if (ok) {
                            runButton.disabled = false;
                        }
                    })
                    .catch(function (error) {
                        Dashboard.alert('❌ Full sync failed: ' + (error?.message || 'Unknown error'));
                    })
                    .finally(function () {
                        Dashboard.hideLoadingMsg();
                    });
            });
        });
    }

    // Add sync discover button functionality
    const syncButton = page.querySelector('#syncDiscover');
    syncButton.addEventListener('click', function () {
        performSyncDiscover(page);
    });

    // Add reset plugin config button functionality
    const resetPluginConfigButton = page.querySelector('#resetPluginConfig');
    resetPluginConfigButton.addEventListener('click', function () {
        performPluginReset(page);
    });

    // Add recycle library data button functionality
    const recycleLibraryButton = page.querySelector('#recycleLibraryData');
    recycleLibraryButton.addEventListener('click', function () {
        performRecycleLibraryData(page);
    });

    // Add refresh networks button functionality
    const refreshButton = page.querySelector('#refreshNetworks');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            Dashboard.showLoadingMsg();
            loadRegions(page).then(function() {
                Dashboard.alert('✅ Regions refreshed successfully!');
                scrollToElement('selectWatchRegion');
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to refresh regions: ' + (error?.message || 'Unknown error'));
                scrollToElement('syncSettings');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
}

function performSyncDiscover(page) {
    const syncButton = page.querySelector('#syncDiscover');
    
    // Show confirmation dialog for saving settings before sync
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting discover sync.',
        confirmText: 'Save & Sync',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            syncButton.disabled = true;
            // Save settings first, then sync
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the sync result textbox
                const syncDiscoverResult = page.querySelector('#syncDiscoverResult');
                syncDiscoverResult.textContent = '🔄 Syncing library...';
                syncDiscoverResult.style.display = 'block';
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // sync if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/SyncDiscover'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(syncData) {
                    // Parse the sync results for better user feedback
                    const message = syncData.message || 'Folder structure creation completed successfully';
                    
                    // Build detailed information if available
                    let resultText = `Discover Sync Results:\n`;
                    resultText += `✅ ${message}\n\n`;
                    
                    if (syncData.details) {
                        resultText += `Details:\n${syncData.details}`;
                    }
                    
                    syncDiscoverResult.textContent = resultText;
                    scrollToElement('syncDiscoverResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Sync failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `Discover Sync Results:\n`;
                    resultText += `❌ Folder structure creation failed: ${error?.message || 'Unknown error'}\n`;
                    
                    syncDiscoverResult.textContent = resultText;
                    scrollToElement('syncDiscoverResult');
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
                syncButton.disabled = false;
            });
        }
    });
}

// Helper functions for Discover Settings
function parseNetworkOptions(options) {
    return Array.from(options).map(option => {
        const networkObj = {};
        // Extract all data attributes
        Array.from(option.attributes).forEach(attr => {
            if (attr.name.startsWith('data-int-')) {
                const propName = attr.name.replace('data-int-', '');
                networkObj[propName] = parseInt(attr.value);
            } else if (attr.name.startsWith('data-str-')) {
                const propName = attr.name.replace('data-str-', '');
                networkObj[propName] = attr.value;
            }
        });
        return networkObj;
    });
}

function updateAvailableNetworks(page, networkMap = []) {
    const config = window.configJellyBridge || {};
    
    const availableNetworksSelect = page.querySelector('#availableNetworks');
    
    // Get currently active network objects by extracting data attributes from options
    const activeNetworksSelect = page.querySelector('#activeNetworks');
    const activeNetworks = parseNetworkOptions(activeNetworksSelect.options);
    
    // Get default network map from global config
    const defaultNetworkMap = config?.DefaultValues?.NetworkMap || [];
    
    // Combine default networks with API networks from parameter
    const combinedNetworks = [...defaultNetworkMap, ...networkMap];
    
    // Filter out active networks by ID
    const availableNetworks = combinedNetworks.filter(network => 
        network && network.id && !activeNetworks.some(active => active.id === network.id)
    );
    
    // Update the available networks select
    populateSelectWithNetworks(availableNetworksSelect, availableNetworks);
    
    // Sort the available networks by name (value)
    sortSelectOptions(availableNetworksSelect);

    return availableNetworks;
}

function updateClearButtonVisibility(clearButton, searchValue) {
    if (searchValue && searchValue !== '') {
        clearButton.style.display = 'flex';
    } else {
        clearButton.style.display = 'none';
    }
}

function loadAvailableNetworks(page) {
    const region = page.querySelector('#selectWatchRegion').value;
    
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/Networks', { region: region }),
        type: 'GET',
        dataType: 'json'
    }).then(function(response) {
        if (response && Array.isArray(response)) {
            // Store the full network objects for later use
            window.availableNetworksData = response;
                        
            // Use updateAvailableNetworks to handle the rest
            return Promise.resolve(updateAvailableNetworks(page, response));
        } else {
            // Use updateAvailableNetworks with empty map to show defaults
            return Promise.resolve(updateAvailableNetworks(page));
        }
    }).catch(function(error) {
        Dashboard.alert(`❌ DEBUG: API call failed for networks. Error: ${error?.message || 'Unknown error'}`);
        
        // Use updateAvailableNetworks with empty map to show defaults
        return Promise.resolve(updateAvailableNetworks(page));
    });
}

function populateSelectWithNetworks(selectElement, networks) {
    selectElement.innerHTML = '';
    
    // Handle different input formats
    const networkList = Array.isArray(networks) ? networks : [];
    const seenIds = new Set();
    
    networkList.forEach(network => {
        const option = document.createElement('option');
        
        // Check if network has the expected format
        if (network && network.id !== undefined) {
            // Skip if we've already seen this ID
            if (seenIds.has(network.id)) {
                return;
            }
            seenIds.add(network.id);

            // Network object with id
            option.value = network.id.toString();
            const displayText = `${network.displayPriority.toString().padStart(3, ' ')}. ${network.name} (${network.id})${network.country ? ` [${network.country}]` : ''}`;
            option.textContent = displayText;
            option.title = displayText; // Add hover tooltip
            
            // Store ALL properties as data attributes with proper prefixes
            Object.keys(network).forEach(key => {
                if (network[key] !== undefined && network[key] !== null) {
                    // Determine if it's an integer or string
                    if (typeof network[key] === 'number' && Number.isInteger(network[key])) {
                        option.setAttribute(`data-int-${key}`, network[key].toString());
                    } else {
                        option.setAttribute(`data-str-${key}`, network[key]);
                    }
                }
            });
            
            selectElement.appendChild(option);
        } else {
            // Invalid network format
            Dashboard.alert(`❌ ERROR: Invalid network format: ${JSON.stringify(network)}. Expected format: { id: number, name: string }`);
        }
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

function moveNetworks(fromSelect, toSelect) {
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
        
        // Copy ALL data attributes generically
        Object.keys(option.dataset).forEach(dataKey => {
            newOption.dataset[dataKey] = option.dataset[dataKey];
        });
        
        toSelect.appendChild(newOption);
        
        // Remove from source
        option.remove();
    });
    
    // Sort destination select to maintain alphabetical order
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
    options.sort((a, b) => {
        // Sort by the display values (textContent)
        return (a.textContent || '') > (b.textContent || '') ? 1 : -1;
    });
    
    // Clear and re-add sorted options
    selectElement.innerHTML = '';
    options.forEach(option => {
        selectElement.appendChild(option);
    });
}

function populateRegion(page, regionValues, initialValue = null) {
    const config = window.configJellyBridge || {};
    const regionSelect = page.querySelector('#selectWatchRegion');

    // Create a Map to store unique regions (ISO code as key, region object as value)
    const uniqueRegions = new Map();
    
    // First, add existing options to the map
    Array.from(regionSelect.options).forEach(option => {
        uniqueRegions.set(option.value, {
            iso_3166_1: option.value,
            textContent: option.textContent
        });
    });
    
    // Then, overlay new region values over the existing set (this will overwrite duplicates)
    regionValues.forEach(region => {
        uniqueRegions.set(region.iso_3166_1, region);
    });
    
    // Clear existing options and rebuild from unique regions
    regionSelect.innerHTML = '';
    uniqueRegions.forEach(region => {
        const option = document.createElement('option');
        option.value = region.iso_3166_1;
        // Use existing textContent if available, otherwise format from region object
        option.textContent = region.textContent || (region.native_name ? `${region.native_name} (${region.iso_3166_1})` : region.iso_3166_1);
        regionSelect.appendChild(option);
    });
    
    // Sort the region options using our standard sorting function
    sortSelectOptions(regionSelect);
    
    // Set current value back to the original value
    regionSelect.value = initialValue;
}

function loadRegions(page) {
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/Regions'),
        type: 'GET',
        dataType: 'json'
    }).then(function (data) {
        if (data && data.success && data.regions) {
            const select = page.querySelector('#selectWatchRegion');
            if (select) {
                // When loading regions, set the current value to the selected value
                populateRegion(page, data.regions, select.value);
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

// ==========================================
// ADVANCED SETTINGS FUNCTIONS
// ==========================================

function initializeAdvancedSettings(page) {
    // Set advanced settings form values with null handling
    setInputField(page, 'RequestTimeout');
    setInputField(page, 'RetryAttempts');
    setInputField(page, 'MaxDiscoverPages');
    setInputField(page, 'MaxRetentionDays');
    setInputField(page, 'PlaceholderDurationSeconds');
    const placeholderDurationInput = page.querySelector('#PlaceholderDurationSeconds');
    if (placeholderDurationInput) {
        placeholderDurationInput.addEventListener('input', function() {
            if (this.value && parseInt(this.value) < 1) {
                this.value = '1';
            }
        });
    }
    setInputField(page, 'AutoSyncOnStartup', true);
    setInputField(page, 'StartupDelaySeconds');
    setInputField(page, 'EnableDebugLogging', true);
    
    // Initialize startup delay state
    updateStartupDelayState();
    
    // Add event listener for AutoSyncOnStartup checkbox
    const autoSyncOnStartupCheckbox = page.querySelector('#AutoSyncOnStartup');
    if (autoSyncOnStartupCheckbox) {
        autoSyncOnStartupCheckbox.addEventListener('change', function() {
            updateStartupDelayState();
        });
    }
    
    // Add event listener for Create Separate Libraries checkbox
    const createSeparateLibrariesCheckbox = page.querySelector('#CreateSeparateLibraries');
    if (createSeparateLibrariesCheckbox) {
        createSeparateLibrariesCheckbox.addEventListener('change', function() {
            updateLibraryPrefixState();
        });
    }
    
    // Library Prefix real-time validation
    const libraryPrefixInput = page.querySelector('#LibraryPrefix');
    if (libraryPrefixInput) {
        libraryPrefixInput.addEventListener('input', function() {
            validateField('LibraryPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot contain: \\ / : * ? " < > |');
        });
    }
}

function updateStartupDelayState() {
    const autoSyncOnStartupCheckbox = document.querySelector('#AutoSyncOnStartup');
    const startupDelaySecondsInput = document.querySelector('#StartupDelaySeconds');
    const startupDelaySecondsContainer = document.querySelector('#StartupDelaySecondsContainer');
    
    const isEnabled = autoSyncOnStartupCheckbox && autoSyncOnStartupCheckbox.checked;
    
    // Enable/disable the input
    startupDelaySecondsInput.disabled = !isEnabled;
    
    // Add/remove disabled styling
    if (isEnabled) {
        startupDelaySecondsInput.classList.remove('disabled');
        if (startupDelaySecondsContainer) {
            startupDelaySecondsContainer.classList.remove('disabled');
        }
    } else {
        startupDelaySecondsInput.classList.add('disabled');
        if (startupDelaySecondsContainer) {
            startupDelaySecondsContainer.classList.add('disabled');
        }
    }
}

function performPluginReset(page) {
    // Single confirmation for configuration reset
    Dashboard.confirm({
        title: '⚠️ Reset Plugin Configuration',
        text: 'This will reset ALL plugin settings to their default values. Jellyfin library data will be left unchanged. Are you sure you want to continue?',
        confirmText: 'Yes, Reset Settings',
        cancelText: 'Cancel',
        primary: "cancel"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            // Reset configuration to defaults
            Dashboard.showLoadingMsg();
            
            // Create reset configuration with null/empty values
            const resetConfig = {
                JellyseerrUrl: '',
                ApiKey: '',
                LibraryDirectory: '',
                SyncIntervalHours: null,
                LibraryPrefix: '',
                RequestTimeout: null,
                RetryAttempts: null,
                MaxDiscoverPages: null,
                MaxRetentionDays: null,
                IsEnabled: null,
                CreateSeparateLibraries: null,
                ExcludeFromMainLibraries: null,
                AutoSyncOnStartup: null,
                EnableDebugLogging: null,
                Region: '',
                NetworkMap: null
            };
            
            // Send reset configuration to the plugin
            ApiClient.ajax({
                url: ApiClient.getUrl('JellyBridge/PluginConfiguration'),
                type: 'POST',
                data: JSON.stringify(resetConfig),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function(result) {
                Dashboard.alert('✅ Plugin configuration has been reset to defaults! Please refresh the page to see the changes.');
                
                // Reload the page to show default values
                window.location.reload();
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to reset configuration: ' + (error?.message || 'Unknown error'));
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        }
    });
}

function performRecycleLibraryData(page) {
    // Get current library directory, fallback to default if empty
    const config = window.configJellyBridge || {};
    const currentLibraryDir = page.querySelector('#LibraryDirectory').value || config.DefaultValues?.LibraryDirectory;
    // Get the button
    const recycleLibraryButton = page.querySelector('#recycleLibraryData');
    
    // First confirmation: save configuration
    Dashboard.confirm({
        title: '❗ Save Configuration',
        text: `This will save your current configuration settings, then confirm again to delete Jellyseerr library data. Library Directory: ${currentLibraryDir}`,
        confirmText: 'Save & Continue',
        cancelText: 'Cancel',
        primary: "cancel"
    }, 'Title', (confirmed1) => {
        if (!confirmed1) {
            return;
        }
        
        // Save configuration first
        Dashboard.showLoadingMsg();
        
        // Disable button to prevent multiple clicks
        recycleLibraryButton.disabled = true;
        
        savePluginConfiguration(page).then(function(result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            
            // After saving, show second confirmation
            Dashboard.confirm({
                title: '🚨 FINAL CONFIRMATION - DELETE LIBRARY',
                text: `This next step will delete ALL Jellyseerr library data including folders and generated content. If "Manage Jellyseerr Library" option is enabled, it will also refresh the Jellyfin library to remove metadata. ⚠️ This action CANNOT be undone! Library Directory: ${currentLibraryDir}`,
                confirmText: '🚩 YES, DELETE EVERYTHING',
                cancelText: 'Cancel',
                primary: "cancel"
            }, 'Title', (confirmed2) => {
                if (!confirmed2) {
                    // User cancelled, the finally block will handle re-enabling the button
                    return Promise.resolve();
                }
                
                Dashboard.showLoadingMsg();
                
                // Proceed with library data deletion
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/RecycleLibrary'),
                    type: 'POST',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(result) {
                    Dashboard.alert('✅ All Jellyseerr library data has been deleted successfully.');
                }).catch(function(error) {
                    Dashboard.alert('❌ Failed to delete library data: ' + (error?.message || 'Unknown error'));
                }).finally(function() {
                    Dashboard.hideLoadingMsg();
                    recycleLibraryButton.disabled = false;
                });
            });
        }).catch(function(error) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
            scrollToElement('jellyBridgeConfigurationForm');
        }).finally(function() {
            Dashboard.hideLoadingMsg();
            recycleLibraryButton.disabled = false;
        });
    });
}

// ==========================================
// SAVE CONFIGURATION FUNCTION
// ==========================================

function savePluginConfiguration(view) {
    const form = view.querySelector('#jellyBridgeConfigurationForm');
    
    // Validate URL format if provided
    const url = form.querySelector('#JellyseerrUrl').value;
    if (url && !validateField('JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
    
    // Validate API Key
    if (!validateField('ApiKey', validators.notNull, 'API Key is required').isValid) return;
    
    // Validate number fields with appropriate types
    if (!validateField('SyncIntervalHours', validators.double, 'Sync Interval must be a valid number between 0 and 17976931348623157').isValid) return;
    if (!validateField('RequestTimeout', validators.int, 'Request Timeout must be an integer between 1 and 2147483647').isValid) return;
    if (!validateField('RetryAttempts', validators.int, 'Retry Attempts must be an integer between 0 and 2147483647').isValid) return;
    if (!validateField('MaxDiscoverPages', validators.int, 'Max Discover Pages must be an integer between 0 and 2147483647').isValid) return;
    if (!validateField('MaxRetentionDays', validators.int, 'Max Retention Days must be an integer between 1 and 2147483647').isValid) return;
    if (!validateField('StartupDelaySeconds', validators.int, 'Startup Delay must be an integer between 0 and 2147483647').isValid) return;
    if (!validateField('PlaceholderDurationSeconds', validators.int, 'Placeholder Duration must be an integer between 1 and 2147483647').isValid) return;
    
    // Validate Library Prefix for Windows filename compatibility
    if (!validateField('LibraryPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot contain: \\ / : * ? " < > |').isValid) return;
    
    // Use our custom endpoint to get the current configuration
    return fetch('/JellyBridge/PluginConfiguration')
        .then(response => response.json())
        .then(function (config) {
            // Update config with current form values
            // Only include checkbox values if they differ from defaults
            config.IsEnabled = nullIfDefault(form.querySelector('#IsEnabled').checked, config.DefaultValues.IsEnabled);
            config.JellyseerrUrl = form.querySelector('#JellyseerrUrl').value;
            config.ApiKey = form.querySelector('#ApiKey').value.trim();
            config.LibraryDirectory = form.querySelector('#LibraryDirectory').value;
            config.SyncIntervalHours = safeParseDouble(form.querySelector('#SyncIntervalHours'));
            config.ExcludeFromMainLibraries = nullIfDefault(form.querySelector('#ExcludeFromMainLibraries').checked, config.DefaultValues.ExcludeFromMainLibraries);
            config.CreateSeparateLibraries = nullIfDefault(form.querySelector('#CreateSeparateLibraries').checked, config.DefaultValues.CreateSeparateLibraries);
            config.LibraryPrefix = form.querySelector('#LibraryPrefix').value;
            config.AutoSyncOnStartup = nullIfDefault(form.querySelector('#AutoSyncOnStartup').checked, config.DefaultValues.AutoSyncOnStartup);
            config.StartupDelaySeconds = safeParseInt(form.querySelector('#StartupDelaySeconds'));
            config.Region = nullIfDefault(form.querySelector('#selectWatchRegion').value, config.DefaultValues.Region);
            config.NetworkMap = parseNetworkOptions(form.querySelector('#activeNetworks').options);
            config.RequestTimeout = safeParseInt(form.querySelector('#RequestTimeout'));
            config.RetryAttempts = safeParseInt(form.querySelector('#RetryAttempts'));
            config.MaxDiscoverPages = safeParseInt(form.querySelector('#MaxDiscoverPages'));
            config.MaxRetentionDays = safeParseInt(form.querySelector('#MaxRetentionDays'));
            config.PlaceholderDurationSeconds = safeParseInt(form.querySelector('#PlaceholderDurationSeconds'));
            config.EnableDebugLogging = nullIfDefault(form.querySelector('#EnableDebugLogging').checked, config.DefaultValues.EnableDebugLogging);
            config.ManageJellyseerrLibrary = nullIfDefault(form.querySelector('#ManageJellyseerrLibrary').checked, config.DefaultValues.ManageJellyseerrLibrary);
            
            // Save the configuration using our custom endpoint
            return fetch('/JellyBridge/PluginConfiguration', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(config)
            });
        })
        .then(async response => {
            const result = await response.json();
            if (result.success) {
                return result;
            } else {
                throw new Error(result.error || 'Failed to save configuration');
        }
    });
}

// ==========================================
// UTILITY FUNCTIONS
// ==========================================

// Scroll to a specific element by ID with smooth scrolling
function scrollToElement(elementId, offset = 20) {
    const element = document.getElementById(elementId);
    if (element) {
        const elementPosition = element.getBoundingClientRect().top;
        const pluginContainerHeight = 48; // Plugin container bar height
        const offsetPosition = elementPosition + window.pageYOffset - offset - pluginContainerHeight;
        
        window.scrollTo({
            top: offsetPosition,
            behavior: 'smooth'
        });
        
        // Add a temporary highlight effect
        element.style.transition = 'box-shadow 0.3s ease';
        element.style.boxShadow = '0 0 10px rgba(0, 123, 255, 0.5)';
        setTimeout(() => {
            element.style.boxShadow = '';
    }, 2000);
    }
}

// Global validators object
const validators = {
    notNull: (value) => !!value,
    url: (value) => /^https?:\/\/.+/.test(value),
    int: (value) => {
        if (!value) return true; // Allow empty values
        const num = parseInt(value);
        return !isNaN(num) && num >= 0 && num <= 2147483647; // C# int max value
    },
    double: (value) => {
        if (!value) return true; // Allow empty values
        const num = parseFloat(value);
        return !isNaN(num) && num >= 0 && num <= Number.MAX_VALUE;
    },
    windowsFilename: (value) => {
        if (!value) return true; // Allow empty values
        // Check for invalid Windows filename characters: \ / : * ? " < > |
        const invalidChars = /[\\/:*?"<>|]/;
        return !invalidChars.test(value);
    }
};

// Central field validation function
function validateField(fieldId, validator = null, errorMessage = null) {
    const field = document.querySelector(`#${fieldId}`);
    if (!field) {
        console.warn(`Field with ID "${fieldId}" not found`);
        return { isValid: false, error: `Field "${fieldId}" not found` };
    }
    
    const value = field.value.trim();
    
    // Check validator function if provided
    if (validator && !validator(value)) {
        const message = errorMessage || `${fieldId} is invalid`;
        Dashboard.alert(`❌ ${message}`);
        scrollToElement(fieldId);
        return { isValid: false, error: message };
    }
    
    return { isValid: true, error: null };
}

// Helper function to set input field value and placeholder
function setInputField(page, propertyName, isCheckbox = false) {
    const field = page.querySelector(`#${propertyName}`);
    if (!field) {
        Dashboard.alert(`❌ Field with ID "${propertyName}" not found`);
        return;
    }
    
    const config = window.configJellyBridge || {};
    const defaults = config.DefaultValues || {};
    const configValue = config[propertyName];
    const defaultValue = defaults[propertyName];
    
    if (isCheckbox) {
        field.checked = configValue ?? defaultValue;
        } else {
        field.value = configValue ?? '';
        if (defaultValue !== undefined) {
            field.placeholder = defaultValue.toString();
        }
    }
}

// Helper function to check if a value is different from default
function nullIfDefault(value, defaultValue) {
    return value !== defaultValue ? value : null;
}

// Helper function to safely parse integers with user feedback
function safeParseInt(element) {
    const value = element.value;
    if (value === null || value === undefined || value === '') {
        return null;
    }
    return parseInt(value);
}

function safeParseDouble(element) {
    const value = element.value;
    if (value === null || value === undefined || value === '') {
        return null;
    }
    return parseFloat(value);
}


