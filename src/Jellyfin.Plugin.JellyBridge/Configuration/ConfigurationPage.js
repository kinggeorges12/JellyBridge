const JellyBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

export default function (view) {
    let isInitialized = false;
    
    view.addEventListener('viewshow', function () {
        if (isInitialized) {
            return; // Prevent duplicate initialization
        }
        
        Dashboard.showLoadingMsg();
        const page = this;
        
        // Use our custom endpoint to get the configuration via ApiClient
        ApiClient.ajax({
            url: ApiClient.getUrl('JellyBridge/PluginConfiguration'),
            type: 'GET',
            dataType: 'json'
        }).then(function (config) {
            // Store configuration globally for other functions to use
            window.configJellyBridge = config;

            // Initialize header
            initializePluginHeader(page);
            
            // Initialize general settings including test connection
            initializeGeneralSettings(page);
            
            // Initialize import discover content settings including network interface and sync buttons
            initializeImportContent(page);
            
            // Initialize manage discover library settings
            initializeManageLibrary(page);
            
            // Initialize sort content settings
            initializeSortContent(page);
            
            // Initialize advanced settings
            initializeAdvancedSettings(page);
            
            // Initialize global settings (including detail tab scroll functionality)
            initializeGlobalSettings(page);
            
            // Scroll to top of page after successful initialization
            scrollToElement('jellyBridgeConfigurationPage');
            
            isInitialized = true;
        }).catch(function (error) {
            Dashboard.alert('❌ Failed to load configuration: ' + (error?.message || error));
            scrollToElement('jellyBridgeConfigurationPage');
        }).finally(function() {
            Dashboard.hideLoadingMsg();
        });
    });
    
}

// ==========================================
// PLUGIN HEADER FUNCTIONS
// ==========================================

function cacheBuster() {
    const config = window.configJellyBridge;
    try {
        const version = config.PluginVersion;
        const base = Dashboard.getPluginUrl('JellyBridge'); // "configurationpage?name=JellyBridge"
        Dashboard.navigate(`${base}&v=${version}`);
    } catch (e) { /* ignore */ }
}

function initializePluginHeader(page) {
    const config = window.configJellyBridge;

    // Update header legend with plugin version
    if (config.PluginVersion) {
        page.querySelector('legend').textContent = `JellyBridge Configuration (plugin version: ${config.PluginVersion})`;
    }
    // Update header legend with plugin version
    if (config.PluginVersion) {
        page.querySelector('legend').textContent = `JellyBridge Configuration (plugin version: ${config.PluginVersion})`;
    }

    // Start task status polling
    startTaskStatusPolling(page);
}
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
    if (taskData.status === 'Disabled') {
        statusText.textContent = '⏸️ Disabled';
        statusText.style.color = '#888888';
        progressContainer.style.display = 'none';
    } else if (taskData.isRunning) {
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
            taskStatusTimes.setAttribute('title', 'When the scheduled sync has not run since installing this plugin, the next run is always 1 hour later. After that, it follows the Sync Interval setting.');
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
    setInputField(page, 'LibraryDirectory');
    setInputField(page, 'SyncIntervalHours');
    setInputField(page, 'EnableStartupSync', true);
    
    // Test connection button functionality
    const testButton = page.querySelector('#testConnection');
    testButton.addEventListener('click', function () {
        performTestConnection(page);
    });
    
    // Library setup help button functionality
    const helpButton = page.querySelector('#librarySetupHelp');
    const setupInstructions = page.querySelector('#librarySetupInstructions');
    if (helpButton && setupInstructions) {
        // Click event toggles visibility and class
        helpButton.addEventListener('click', function () {
            if (helpButton.classList.contains('clicked')) {
                setupInstructions.style.display = 'none';
            } else {
                scrollToElement('librarySetupInstructions', 250);
            }
        });

        // Callback function for the observer
        const handleDisplayChange = () => {
            if (setupInstructions.style.display === 'none') {
                helpButton.classList.remove('clicked');
            } else {
                helpButton.classList.add('clicked');
            }
        };

        // MutationObserver to watch for display changes
        const observer = new MutationObserver(handleDisplayChange);
        observer.observe(setupInstructions, { attributes: true, attributeFilter: ['style'] });
        handleDisplayChange(); // Set initial state
    }
    
    // Add form submit event listener
    const form = page.querySelector('#jellyBridgeConfigurationForm');
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

    // Disable dependent fields when automated task is disabled
    const isEnabledCheckbox = page.querySelector('#IsEnabled');
    if (isEnabledCheckbox) {
        isEnabledCheckbox.addEventListener('change', function() {
            updateAutoTaskDependencies();
        });
    }
    // Initialize dependency state on load
    updateAutoTaskDependencies();
    
    // Add click handlers to scroll to the IsEnabled checkbox when disabled fields are clicked
    const syncIntervalContainer = page.querySelector('#SyncIntervalHoursContainer');
    
    // Set up scroll handlers for containers that depend on IsEnabled
    const dependentContainers = [syncIntervalContainer].filter(Boolean);
    
    setupDisabledScrollHandlers('#IsEnabled', dependentContainers);
}

function performTestConnection(page) {
    const testButton = page.querySelector('#testConnection');
    const url = safeParseString(page.querySelector('#JellyseerrUrl'));
    const apiKey = safeParseString(page.querySelector('#ApiKey'));
    const libraryDirectory = safeParseString(page.querySelector('#LibraryDirectory'));
    
    // Validate URL format if provided
    if (!validateField(page, 'JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
    
    // Validate API Key
    if (!validateField(page, 'ApiKey', validators.notNull, 'API Key is required for connection test').isValid) return;

    // Validate Library Directory
    if (!validateField(page, 'LibraryDirectory', validators.windowsFolder, 'Library Directory contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;

    testButton.disabled = true;
    Dashboard.showLoadingMsg();
    
    const testData = {
        JellyseerrUrl: url,
        ApiKey: apiKey,
        LibraryDirectory: libraryDirectory
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
                title: '✅ Connection Success!',
                text: 'Save connection settings now?',
                confirmText: '💾 Save',
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
    }).catch(async function (error) {
        let message = null;
        try {
            let errorResponse = await error.json();
            if (errorResponse) {
                message = '❌ ';
                if (errorResponse.message) {
                    message += errorResponse.message;
                } else {
                    message += `Request failed (${errorResponse.status}): ${errorResponse.statusText}`;
                }
            } else {
                message = '❓ Cannot communicate with Jellyfin plugin endpoint';
            }
        } catch (e) {
            let rawText = await error.text();
            if (rawText) {
                message = `🚫 ${rawText}`;
            } else {
                message = '⛔ Cannot communicate with Jellyfin plugin endpoint';
            }
        }
        try{
        // Show confirmation dialog for opening troubleshooting
        Dashboard.confirm({
                title: '🚧 Connection Test Failed',
                text: `Do you want to try troubleshooting? Error: ${message}`,
                confirmText: '🤖 Troubleshooting',
                cancelText: 'Close',
                primary: "confirm"
            }, 'Title', (confirmed) => {
                if (confirmed) {
                    scrollToElement('troubleshootingDetails');
                }
            });
        } finally {
            // Something went wrong
            Dashboard.alert(message);
        }
    }).finally(function() {
        Dashboard.hideLoadingMsg();
        testButton.disabled = false;
    });
}

// ==========================================
// IMPORT DISCOVER CONTENT FUNCTIONS
// ==========================================

function initializeImportContent(page) {
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
    const regionSelect = config.Region || config.ConfigDefaults.Region;
    populateRegion(page, [{ iso_3166_1: regionSelect }], regionSelect);
    
    // Load active networks from saved configuration
    // If NetworkMap is null/undefined, fall back to defaults; if it's an empty array, keep it empty
    const defaultNetworkMap = (config.ConfigDefaults && Array.isArray(config.ConfigDefaults.NetworkMap)) ? config.ConfigDefaults.NetworkMap : [];
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
            const config = window.configJellyBridge || {};
            if(config.JellyseerrUrl != page.querySelector('#JellyseerrUrl').value){
                Dashboard.alert('❗ Jellyseerr connection information has changed. Please save your settings and try again.');
                scrollToElement('saveConfig');
                return;
            }
            Dashboard.showLoadingMsg();
            loadAvailableNetworks(page)
            .then(function(availableNetworks) {
                if (availableNetworks) {
                    Dashboard.alert(`✅ Refreshed available networks`);
                    scrollToElement('availableNetworksSelectBox');
                }
            }).catch(function() {
                Dashboard.alert('❌ Failed to refresh available networks (try Test Connection to Jellyseerr first)');
                scrollToElement('testConnection');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
    
    // Set Max Discover Pages and Max Retention Days
    setInputField(page, 'MaxDiscoverPages');
    setInputField(page, 'MaxRetentionDays');

    // Add sync discover button functionality
    const syncButton = page.querySelector('#syncDiscover');
    syncButton.addEventListener('click', function () {
        performSyncImportContent(page);
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
    const refreshButton = page.querySelector('#refreshWatchRegions');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            const config = window.configJellyBridge || {};
            if(config.JellyseerrUrl != page.querySelector('#JellyseerrUrl').value){
                Dashboard.alert('❗ Jellyseerr connection information has changed. Please save your settings and try again.');
                scrollToElement('saveConfig');
                return;
            }
            Dashboard.showLoadingMsg();
            loadRegions(page).then(function() {
                Dashboard.alert('✅ Refreshed regions');
                scrollToElement('selectWatchRegion');
            }).catch(function() {
                Dashboard.alert('❌ Failed to refresh available networks (try Test Connection to Jellyseerr first)');
                scrollToElement('testConnection');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
}

function performSyncImportContent(page) {
    const syncButton = page.querySelector('#syncDiscover');
    
    // Show confirmation dialog for saving settings before sync
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting discover sync.',
        confirmText: '💾 Save & Sync 📥',
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
                appendToResultBox(syncDiscoverResult, '🔄 Syncing library...', true);
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
                    appendToResultBox(syncDiscoverResult, '\n' + (syncData.result || 'No result available'));
                    scrollToElement('syncDiscoverResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Sync failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nDiscover Sync Results:\n`;
                    resultText += `❌ Folder structure creation failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(syncDiscoverResult, resultText);
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

// Helper functions for Import Discover Content
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
    const defaultNetworkMap = config?.ConfigDefaults?.NetworkMap || [];
    
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
            // Use updateAvailableNetworks to handle the rest
            return Promise.resolve(updateAvailableNetworks(page, response));
        }
        // do not catch errors, let the caller handle them
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
            // When loading regions, set the current value to the selected value
            return Promise.resolve(populateRegion(page, data.regions, page.querySelector('#selectWatchRegion').value));
        }
    });
}

// ==========================================
// SORT CONTENT FUNCTIONS
// ==========================================

function initializeSortContent(page) {
    const config = window.configJellyBridge || {};
    
    // Populate SortOrder dropdown from enum values
    const sortOrderSelect = page.querySelector('#selectSortOrder');
    if (sortOrderSelect) {
        sortOrderSelect.innerHTML = '';
        
        // Use the name as the value
        config.ConfigOptions.SortOrderOptions.forEach(option => {
            const optionElement = document.createElement('option');
            optionElement.value = option.Name;
            optionElement.textContent = option.Name;
            sortOrderSelect.appendChild(optionElement);
        });
        
        // Store selected value
        const sortOrderValue = config.SortOrder ?? config.ConfigDefaults?.SortOrder;
        sortOrderSelect.value = sortOrderValue;
    }
    
    // Set sort content form values with null handling
    setInputField(page, 'EnableAutomatedSortTask', true);
    setInputField(page, 'MarkMediaPlayed', true);
    setInputField(page, 'SortTaskIntervalHours');

    // Initialize sort task dependency state
    updateSortTaskDependencies();
    
    // Add event listener for EnableAutomatedSortTask checkbox
    const enableAutomatedSortTaskCheckbox = page.querySelector('#EnableAutomatedSortTask');
    if (enableAutomatedSortTaskCheckbox) {
        enableAutomatedSortTaskCheckbox.addEventListener('change', function() {
            updateSortTaskDependencies();
        });
    }
    
    // Add scroll handler for SortTaskIntervalHours
    const sortTaskIntervalContainer = page.querySelector('#SortTaskIntervalHoursContainer');
    if (sortTaskIntervalContainer) {
        setupDisabledScrollHandlers('#EnableAutomatedSortTask', [sortTaskIntervalContainer]);
    }

    // Add sort content button functionality
    const sortButton = page.querySelector('#sortContent');
    sortButton.addEventListener('click', function () {
        performSortContent(page);
    });
}

function performSortContent(page) {
    const sortButton = page.querySelector('#sortContent');
    
    // Show confirmation dialog for saving settings before sort
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting sort content.',
        confirmText: '💾 Save & Sort 🎲',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            sortButton.disabled = true;
            // Save settings first, then sort
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the sort result textbox
                const sortContentResult = page.querySelector('#sortContentResult');
                const sortOrderSelect = page.querySelector('#selectSortOrder');
                const selectedOption = sortOrderSelect ? sortOrderSelect.options[sortOrderSelect.selectedIndex] : null;
                const algorithmName = selectedOption ? selectedOption.textContent : 'Sort';
                appendToResultBox(sortContentResult, `🔄 Applying ${algorithmName} algorithm to sort order...`, true);
                sortContentResult.style.display = 'block';
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // Sort if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/SortLibrary'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(sortResult) {
                    appendToResultBox(sortContentResult, '\n' + (sortResult.result || 'No result available'));
                    scrollToElement('sortContentResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Sort content failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nSort Content Results:\n`;
                    resultText += `❌ Sort failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(sortContentResult, resultText);
                    scrollToElement('sortContentResult');
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
                sortButton.disabled = false;
            });
        }
    });
}

function performCleanupMetadata(page) {
    const cleanupButton = page.querySelector('#cleanupMetadata');
    
    // Show confirmation dialog for saving settings before cleanup
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting cleanup.',
        confirmText: '💾 Save & Cleanup 🧹',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            cleanupButton.disabled = true;
            // Save settings first, then cleanup
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the cleanup result textbox
                const cleanupResult = page.querySelector('#cleanupMetadataResult');
                appendToResultBox(cleanupResult, '🔄 Cleaning up metadata...', true);
                cleanupResult.style.display = 'block';
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // Cleanup if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/CleanupMetadata'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(cleanupData) {
                    appendToResultBox(cleanupResult, '\n' + (cleanupData?.result || 'No result available'));
                    scrollToElement('cleanupMetadataResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Cleanup failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nCleanup Results:\n`;
                    resultText += `❌ Cleanup failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(cleanupResult, resultText);
                    scrollToElement('cleanupMetadataResult');
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
                cleanupButton.disabled = false;
            });
        }
    });
}

// ==========================================
// MANAGE DISCOVER LIBRARY FUNCTIONS
// ==========================================

function initializeManageLibrary(page) {
    // Set library settings form values with null handling
    setInputField(page, 'ExcludeFromMainLibraries', true);
    setInputField(page, 'RemoveRequestedFromFavorites', true);
    setInputField(page, 'UseNetworkFolders', true);
    setInputField(page, 'AddDuplicateContent', true);
    setInputField(page, 'LibraryPrefix');
    setInputField(page, 'ManageJellyseerrLibrary', true);
    
    updateNetworkFolderOptionsState();
    updateAddDuplicateContentState();
    
    // Add event listener for UseNetworkFolders checkbox
    const useNetworkFoldersCheckbox = page.querySelector('#UseNetworkFolders');
    if (useNetworkFoldersCheckbox) {
        useNetworkFoldersCheckbox.addEventListener('change', function() {
            updateNetworkFolderOptionsState();
            updateAddDuplicateContentState();
        });
    }
    
    // Add event listener for AddDuplicateContent checkbox
    const addDuplicateContentCheckbox = page.querySelector('#AddDuplicateContent');
    if (addDuplicateContentCheckbox) {
        addDuplicateContentCheckbox.addEventListener('change', function() {
            updateAddDuplicateContentState();
        });
    }
    
    // Add scroll handler for AddDuplicateContent
    const addDuplicateContentContainer = page.querySelector('#AddDuplicateContentContainer');
    if (addDuplicateContentContainer) {
        setupDisabledScrollHandlers('#UseNetworkFolders', [addDuplicateContentContainer]);
    }
    
    // Request JellyBridge Library Favorites in Jellyseerr button functionality
    const syncFavoritesButton = page.querySelector('#syncFavorites');
    syncFavoritesButton.addEventListener('click', function() {
        performSyncManageLibrary(page);
    });
    
    // Generate Network Folders button functionality
    const generateNetworkFoldersButton = page.querySelector('#generateNetworkFolders');
    if (generateNetworkFoldersButton) {
        generateNetworkFoldersButton.addEventListener('click', function(e) {
            // If button is disabled, let the scroll handler take over
            if (this.disabled) {
                e.preventDefault();
                e.stopPropagation();
                return;
            }
            performGenerateNetworkFolders(page);
        });
    }
}

function updateNetworkFolderOptionsState() {
    const useNetworkFoldersCheckbox = document.querySelector('#UseNetworkFolders');
    const libraryPrefixInput = document.querySelector('#LibraryPrefix');
    const libraryPrefixContainer = document.querySelector('#LibraryPrefixContainer');
    const networkFolderOptionsDetails = document.querySelector('#networkFolderOptionsDetails');
    const generateNetworkFoldersContainer = document.querySelector('#generateNetworkFoldersContainer');
    const generateNetworkFoldersButton = document.querySelector('#generateNetworkFolders');
    const addDuplicateContentCheckbox = document.querySelector('#AddDuplicateContent');
    const addDuplicateContentContainer = document.querySelector('#AddDuplicateContentContainer');
    
    const isEnabled = useNetworkFoldersCheckbox.checked;
    
    // Apply disabled state to the details element (makes it grayish)
    if (networkFolderOptionsDetails) {
        if (isEnabled) {
            networkFolderOptionsDetails.classList.remove('disabled');
        } else {
            networkFolderOptionsDetails.classList.add('disabled');
        }
    }
    
    // Disable/enable Generate Network Folders button
    if (generateNetworkFoldersButton) {
        generateNetworkFoldersButton.disabled = !isEnabled;
        // Apply disabled styling to the container
        if (generateNetworkFoldersContainer) {
            if (isEnabled) {
                generateNetworkFoldersContainer.classList.remove('disabled');
            } else {
                generateNetworkFoldersContainer.classList.add('disabled');
            }
        }
    }
    
    // Apply disabled state styling (this will handle the disabled property and styling)
    applyDisabledState(libraryPrefixInput, libraryPrefixContainer, isEnabled);
    // addDuplicateContent disabled state is handled by updateAddDuplicateContentState
    
    // Add click handler to scroll to required checkbox when disabled field is clicked
    if (libraryPrefixContainer && useNetworkFoldersCheckbox) {
        addScrollToCheckboxHandler(libraryPrefixContainer, useNetworkFoldersCheckbox);
    }
    
    // Add click handler for add duplicate content container
    if (addDuplicateContentContainer && useNetworkFoldersCheckbox) {
        addScrollToCheckboxHandler(addDuplicateContentContainer, useNetworkFoldersCheckbox);
    }
    
    // Add click handler for generate network folders container/button
    if (generateNetworkFoldersContainer && useNetworkFoldersCheckbox) {
        addScrollToCheckboxHandler(generateNetworkFoldersContainer, useNetworkFoldersCheckbox);
    }
}

// Update controls that depend on UseNetworkFolders being enabled
function updateAddDuplicateContentState() {
    const useNetworkFoldersCheckbox = document.querySelector('#UseNetworkFolders');
    const addDuplicateContentCheckbox = document.querySelector('#AddDuplicateContent');
    const addDuplicateContentContainer = document.querySelector('#AddDuplicateContentContainer');
    const addDuplicateContentWarning = document.querySelector('#addDuplicateContentWarning');
    
    const isUseNetworkFoldersEnabled = useNetworkFoldersCheckbox ? !!useNetworkFoldersCheckbox.checked : false;
    const isAddDuplicateContentEnabled = addDuplicateContentCheckbox ? !!addDuplicateContentCheckbox.checked : false;
    
    // Show/hide warning message
    if (addDuplicateContentWarning) {
        addDuplicateContentWarning.style.display = isAddDuplicateContentEnabled ? 'block' : 'none';
    }
    
    // Apply disabled state styling
    applyDisabledState(addDuplicateContentCheckbox, addDuplicateContentContainer, isUseNetworkFoldersEnabled);
}

function performGenerateNetworkFolders(page) {
    const generateNetworkFoldersButton = page.querySelector('#generateNetworkFolders');
    
    // Show confirmation dialog for saving settings before generating folders
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before generating network folders.',
        confirmText: '💾 Save & Generate 📁',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            generateNetworkFoldersButton.disabled = true;
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
                
                // Call GenerateNetworkFolders endpoint
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/GenerateNetworkFolders'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(response) {
                    const ok = !!(response && response.success === true);
                    
                    if (ok) {
                        Dashboard.alert('✅ Network folders created successfully');
                    } else {
                        let message = response?.message || 'Network folder generation completed';
                        Dashboard.alert('⚠️ ' + message);
                    }
                    
                    generateNetworkFoldersButton.disabled = false;
                }).catch(function(error) {
                    Dashboard.alert('❌ Failed to generate network folders: ' + (error?.message || 'Unknown error'));
                    generateNetworkFoldersButton.disabled = false;
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
                generateNetworkFoldersButton.disabled = false;
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        }
    });
}

function performSyncManageLibrary(page) {
    const syncFavoritesButton = page.querySelector('#syncFavorites');
    
    // Show confirmation dialog for saving settings before requesting content
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before requesting JellyBridge Library favorites in Jellyseerr.',
        confirmText: '💾 Save & Request ⭐',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            syncFavoritesButton.disabled = true;
            // Save settings first, then request content
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the request result textbox
                const syncFavoritesResult = page.querySelector('#syncFavoritesResult');
                appendToResultBox(syncFavoritesResult, '🔄 Requesting JellyBridge Library Favorites in Jellyseerr...', true);
                syncFavoritesResult.style.display = 'block';
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // Request content if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/SyncFavorites'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(syncResult) {
                    appendToResultBox(syncFavoritesResult, '\n' + (syncResult.result || 'No result available'));
                    scrollToElement('syncFavoritesResult');
                }).catch(function(error) {
                    Dashboard.alert('❌ Request JellyBridge Library Favorites in Jellyseerr failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nRequest JellyBridge Library Favorites in Jellyseerr Results:\n`;
                    resultText += `❌ Request failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(syncFavoritesResult, resultText);
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
// ADVANCED SETTINGS FUNCTIONS
// ==========================================

function initializeAdvancedSettings(page) {
    // Set advanced settings form values with null handling
    setInputField(page, 'RequestTimeout');
    setInputField(page, 'RetryAttempts');
    setInputField(page, 'PlaceholderDurationSeconds');
    const placeholderDurationInput = page.querySelector('#PlaceholderDurationSeconds');
    if (placeholderDurationInput) {
        placeholderDurationInput.addEventListener('input', function() {
            if (this.value && parseInt(this.value) < 1) {
                this.value = '1';
            }
        });
    }
    setInputField(page, 'EnableStartupSync', true);
    setInputField(page, 'StartupDelaySeconds');
    setInputField(page, 'TaskTimeoutMinutes');
    setInputField(page, 'EnableDebugLogging', true);
    setInputField(page, 'EnableTraceLogging', true);
    
    // Initialize startup delay state
    updateStartupDelayState();
    
    // Initialize trace logging state
    updateTraceLoggingState();
    
    // Initialize startup sync description
    updateStartupSyncDescription();
    
    // Add event listener for AutoSyncOnStartup checkbox
    const autoSyncOnStartupCheckbox = page.querySelector('#EnableStartupSync');
    if (autoSyncOnStartupCheckbox) {
        autoSyncOnStartupCheckbox.addEventListener('change', function() {
            updateStartupDelayState();
        });
    }
    
    // Set up scroll handler for startup delay container
    const startupDelaySecondsContainer = page.querySelector('#StartupDelaySecondsContainer');
    const enableStartupSyncCheckboxForHandler = page.querySelector('#EnableStartupSync');
    if (startupDelaySecondsContainer && enableStartupSyncCheckboxForHandler) {
        addScrollToCheckboxHandler(startupDelaySecondsContainer, enableStartupSyncCheckboxForHandler);
    }
    
    // Add event listener for EnableDebugLogging checkbox
    const enableDebugLoggingCheckbox = page.querySelector('#EnableDebugLogging');
    if (enableDebugLoggingCheckbox) {
        enableDebugLoggingCheckbox.addEventListener('change', function() {
            updateTraceLoggingState();
        });
    }
    
    // Add event listener for Use Network Folders checkbox
    const useNetworkFoldersCheckbox = page.querySelector('#UseNetworkFolders');
    if (useNetworkFoldersCheckbox) {
        useNetworkFoldersCheckbox.addEventListener('change', function() {
            updateNetworkFolderOptionsState();
        });
    }
    
    // Library Prefix real-time validation
    const libraryPrefixInput = page.querySelector('#LibraryPrefix');
    if (libraryPrefixInput) {
        libraryPrefixInput.addEventListener('input', function() {
            validateField(page, 'LibraryPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot start with a space or contain: \\ / : * ? " < > |');
        });
    }
    
    // Add cleanup metadata button functionality
    const cleanupButton = page.querySelector('#cleanupMetadata');
    if (cleanupButton) {
        cleanupButton.addEventListener('click', function() {
            performCleanupMetadata(page);
        });
    }
}

function updateStartupDelayState() {
    const autoSyncOnStartupCheckbox = document.querySelector('#EnableStartupSync');
    const startupDelaySecondsInput = document.querySelector('#StartupDelaySeconds');
    const startupDelaySecondsContainer = document.querySelector('#StartupDelaySecondsContainer');
    
    const isAutoSyncEnabled = autoSyncOnStartupCheckbox && autoSyncOnStartupCheckbox.checked;
    
    // Apply disabled state styling
    applyDisabledState(startupDelaySecondsInput, startupDelaySecondsContainer, isAutoSyncEnabled);
}

function updateTraceLoggingState() {
    const enableDebugLoggingCheckbox = document.querySelector('#EnableDebugLogging');
    const enableTraceLoggingCheckbox = document.querySelector('#EnableTraceLogging');
    const enableTraceLoggingContainer = document.querySelector('#EnableTraceLoggingContainer');
    
    const isDebugLoggingEnabled = enableDebugLoggingCheckbox && enableDebugLoggingCheckbox.checked;
    
    // Apply disabled state styling
    applyDisabledState(enableTraceLoggingCheckbox, enableTraceLoggingContainer, isDebugLoggingEnabled);
    
    // Add click handler to scroll to required checkbox when disabled field is clicked
    if (enableTraceLoggingContainer && enableDebugLoggingCheckbox) {
        addScrollToCheckboxHandler(enableTraceLoggingContainer, enableDebugLoggingCheckbox);
    }
}

// Update controls that depend on the automated sort task being enabled
function updateSortTaskDependencies() {
    const enableAutomatedSortTaskCheckbox = document.querySelector('#EnableAutomatedSortTask');
    const sortTaskIntervalInput = document.querySelector('#SortTaskIntervalHours');
    const sortTaskIntervalContainer = document.querySelector('#SortTaskIntervalHoursContainer');
    
    const isSortTaskEnabled = enableAutomatedSortTaskCheckbox ? !!enableAutomatedSortTaskCheckbox.checked : true;
    
    // Apply disabled state styling
    applyDisabledState(sortTaskIntervalInput, sortTaskIntervalContainer, isSortTaskEnabled);
    
    // Update startup sync description to show enabled tasks
    updateStartupSyncDescription();
}

// Update controls that depend on the automated task being enabled
function updateAutoTaskDependencies() {
    const pluginEnabledCheckbox = document.querySelector('#IsEnabled');
    const syncIntervalInput = document.querySelector('#SyncIntervalHours');
    const syncIntervalContainer = document.querySelector('#SyncIntervalHoursContainer');

    const isPluginEnabled = pluginEnabledCheckbox ? !!pluginEnabledCheckbox.checked : true;

    // Apply disabled state styling
    applyDisabledState(syncIntervalInput, syncIntervalContainer, isPluginEnabled);

    // Update startup delay to reflect current state
    updateStartupDelayState();
    
    // Update startup sync description to show enabled tasks
    updateStartupSyncDescription();
}

// Update the startup sync description to list only enabled tasks
function updateStartupSyncDescription() {
    const descriptionElement = document.querySelector('#enableStartupSyncDescription');
    if (!descriptionElement) return;
    
    const pluginEnabledCheckbox = document.querySelector('#IsEnabled');
    const sortTaskEnabledCheckbox = document.querySelector('#EnableAutomatedSortTask');
    
    const isSyncEnabled = pluginEnabledCheckbox ? !!pluginEnabledCheckbox.checked : false;
    const isSortEnabled = sortTaskEnabledCheckbox ? !!sortTaskEnabledCheckbox.checked : false;
    
    const enabledTasks = [];
    if (isSyncEnabled) {
        enabledTasks.push('🔄 <span class="link" data-target-page="IsEnabledContainer"><i>Enable the Automated Task to Sync Jellyseerr and Jellyfin</i></span>');
    }
    if (isSortEnabled) {
        enabledTasks.push('🔀 <span class="link" data-target-page="EnableAutomatedSortTaskContainer"><i>Enable the Automated Task to Sort Discover Content</i></span>');
    }
    
    let descriptionText = 'Automatically run all enabled automated tasks when the plugin starts up or when Jellyfin restarts.';
    
    if (enabledTasks.length > 0) {
        descriptionText += ' These tasks will run at Jellyfin startup: ' + enabledTasks.join(', ') + '.';
    } else {
        descriptionText += ' No automated tasks are currently enabled.';
    }
    
    descriptionElement.innerHTML = descriptionText;
    
    // Bind click handlers for newly added links
    initializeLinkSpans(descriptionElement);
}

function performPluginReset(page) {
    // Single confirmation for configuration reset
    Dashboard.confirm({
        title: '⚠️ Reset Plugin Configuration',
        text: 'This will reset ALL plugin settings to their default values and refresh the page. Jellyfin library data will be left unchanged. Are you sure you want to continue?',
        confirmText: '♻️ Reset & Refresh ⟳',
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
                EnableAutomatedSortTask: null,
                SortOrder: null,
                MarkMediaPlayed: null,
                SortTaskIntervalHours: null,
                IsEnabled: null,
                UseNetworkFolders: null,
                AddDuplicateContent: null,
                ExcludeFromMainLibraries: null,
                EnableStartupSync: null,
                StartupDelaySeconds: null,
                TaskTimeoutMinutes: null,
                EnableDebugLogging: null,
                EnableTraceLogging: null,
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
                Dashboard.alert('✅ Plugin configuration has been reset to defaults! ⟳ Refreshing the page...');
                
                // Reload the page to show default values
                setTimeout(() => {
                    window.location.reload();
                }, 2000);
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
    const recycleLibraryButton = page.querySelector('#recycleLibraryData');
    const currentLibraryDir = safeParseString(page.querySelector('#LibraryDirectory')) || config.ConfigDefaults?.LibraryDirectory;
    
    // First confirmation: save configuration
    Dashboard.confirm({
        title: '❗ Save Before Deleting Library Data',
        text: `This will save your current configuration settings, then confirm again to delete Jellyseerr library data. Library Directory: ${currentLibraryDir}`,
        confirmText: '💾 Save & Continue ❗',
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
                title: '🚨 FINAL CONFIRMATION TO DELETE LIBRARY DATA',
                text: `This next step will delete ALL JellyBridge library data including folders and generated content. If "Manage Jellyseerr Library" option is enabled, it will also refresh the Jellyfin library to remove metadata. ⚠️ This action CANNOT be undone! Library Directory: ${currentLibraryDir}`,
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
                    Dashboard.alert('✅ All JellyBridge library data has been deleted successfully.');
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

function savePluginConfiguration(page) {
    // Get current library directory, fallback to default if empty
    const config = window.configJellyBridge || {};
    const form = {};
    
    // Validate all fields - returns true if all pass, undefined/null if any fail
    function validateInputs() {
        if (!validateField(page, 'JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
        if (!validateField(page, 'ApiKey', validators.notNull, 'API Key is required').isValid) return;
        if (!validateField(page, 'LibraryDirectory', validators.windowsFolder, 'Library Directory contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;
        if (!validateField(page, 'SyncIntervalHours', validators.double, 'Sync Interval must be a positive decimal number').isValid) return;
        if (!validateField(page, 'SortTaskIntervalHours', validators.double, 'Sort Task Interval must be a positive decimal number').isValid) return;
        if (!validateField(page, 'RequestTimeout', validators.int, 'Request Timeout must be a positive integer').isValid) return;
        if (!validateField(page, 'RetryAttempts', validators.int, 'Retry Attempts must be a positive integer').isValid) return;
        if (!validateField(page, 'MaxDiscoverPages', validators.int, 'Max Discover Pages must be a positive integer').isValid) return;
        if (!validateField(page, 'MaxRetentionDays', validators.int, 'Max Retention Days must be a positive integer').isValid) return;
        if (!validateField(page, 'StartupDelaySeconds', validators.int, 'Startup Delay must be a positive integer').isValid) return;
        if (!validateField(page, 'TaskTimeoutMinutes', validators.int, 'Task Timeout must be a positive integer').isValid) return;
        if (!validateField(page, 'PlaceholderDurationSeconds', validators.int, 'Placeholder Duration must be a positive integer').isValid) return;
        if (!validateField(page, 'LibraryPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot start with a space or contain: \\ / : * ? " < > |').isValid) return;
        return true;
    }
    
    // Return early if validation fails
    if (!validateInputs()) return Promise.reject(new Error('Validation failed'));
    
    // Update config with current form values
    // Only include checkbox values if they differ from defaults
    form.IsEnabled = nullIfDefault(page.querySelector('#IsEnabled').checked, config.ConfigDefaults.IsEnabled);
    form.JellyseerrUrl = safeParseString(page.querySelector('#JellyseerrUrl'));
    form.ApiKey = safeParseString(page.querySelector('#ApiKey'));
    form.LibraryDirectory = safeParseString(page.querySelector('#LibraryDirectory'));
    form.SyncIntervalHours = safeParseDouble(page.querySelector('#SyncIntervalHours'));
    form.ExcludeFromMainLibraries = nullIfDefault(page.querySelector('#ExcludeFromMainLibraries').checked, config.ConfigDefaults.ExcludeFromMainLibraries);
    form.RemoveRequestedFromFavorites = nullIfDefault(page.querySelector('#RemoveRequestedFromFavorites').checked, config.ConfigDefaults.RemoveRequestedFromFavorites);
    form.UseNetworkFolders = nullIfDefault(page.querySelector('#UseNetworkFolders').checked, config.ConfigDefaults.UseNetworkFolders);
    form.AddDuplicateContent = nullIfDefault(page.querySelector('#AddDuplicateContent').checked, config.ConfigDefaults.AddDuplicateContent);
    form.LibraryPrefix = safeParseString(page.querySelector('#LibraryPrefix'), false);
    form.EnableStartupSync = nullIfDefault(page.querySelector('#EnableStartupSync').checked, config.ConfigDefaults.EnableStartupSync);
    form.StartupDelaySeconds = safeParseInt(page.querySelector('#StartupDelaySeconds'));
    form.TaskTimeoutMinutes = safeParseInt(page.querySelector('#TaskTimeoutMinutes'));
    form.Region = nullIfDefault(page.querySelector('#selectWatchRegion').value, config.ConfigDefaults.Region);
    form.NetworkMap = parseNetworkOptions(page.querySelector('#activeNetworks').options);
    form.RequestTimeout = safeParseInt(page.querySelector('#RequestTimeout'));
    form.RetryAttempts = safeParseInt(page.querySelector('#RetryAttempts'));
    form.MaxDiscoverPages = safeParseInt(page.querySelector('#MaxDiscoverPages'));
    form.MaxRetentionDays = safeParseInt(page.querySelector('#MaxRetentionDays'));
    form.EnableAutomatedSortTask = nullIfDefault(page.querySelector('#EnableAutomatedSortTask').checked, config.ConfigDefaults.EnableAutomatedSortTask);
    form.SortOrder = nullIfDefault(page.querySelector('#selectSortOrder').value, config.ConfigDefaults.SortOrder);
    form.MarkMediaPlayed = nullIfDefault(page.querySelector('#MarkMediaPlayed').checked, config.ConfigDefaults.MarkMediaPlayed);
    form.SortTaskIntervalHours = safeParseDouble(page.querySelector('#SortTaskIntervalHours'));
    form.PlaceholderDurationSeconds = safeParseInt(page.querySelector('#PlaceholderDurationSeconds'));
    form.EnableDebugLogging = nullIfDefault(page.querySelector('#EnableDebugLogging').checked, config.ConfigDefaults.EnableDebugLogging);
    form.EnableTraceLogging = nullIfDefault(page.querySelector('#EnableTraceLogging').checked, config.ConfigDefaults.EnableTraceLogging);
    form.ManageJellyseerrLibrary = nullIfDefault(page.querySelector('#ManageJellyseerrLibrary').checked, config.ConfigDefaults.ManageJellyseerrLibrary);
    
    // Save the configuration using ApiClient
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/PluginConfiguration'),
        type: 'POST',
        data: JSON.stringify(form),
        contentType: 'application/json',
        dataType: 'json'
    }).then(function (result) {
        if (result && result.success) {
            form.ConfigDefaults = config.ConfigDefaults;
            window.configJellyBridge = form;
            return result;
        } else {
            throw new Error(result?.error || 'Failed to save configuration');
        }
    });
}

// ==========================================
// GLOBAL SETTINGS FUNCTIONS
// ==========================================

// Initialize global settings for the configuration page
function initializeGlobalSettings(page) {
    // Initialize detail tab scroll functionality
    initializeDetailTabScroll(page);
    // Initialize link spans
    initializeLinkSpans(page);
    // Initialize number input scroll prevention
    initializeNumberInputScrollPrevention(page);
}

// Initialize scroll-to functionality for detail tabs
function initializeDetailTabScroll(page) {
    // List of detail section IDs
    const detailIds = ['librarySetupInstructions', 'troubleshootingDetails', 'syncSettings', 'manageLibrarySettings', 'sortContentSettings', 'networkFolderOptionsDetails', 'advancedSettings'];
    
    detailIds.forEach(detailId => {
        const detailsElement = page.querySelector(`#${detailId}`);
        if (detailsElement) {
            const summaryElement = detailsElement.querySelector('summary');
            if (summaryElement) {
                summaryElement.addEventListener('click', function(e) {
                    // Check if the details is being opened (will be open after the click)
                    // We need to check before the state changes, so we check if it's currently closed
                    const wasClosed = !detailsElement.hasAttribute('open');
                    
                    // Wait a brief moment for the details to open/close, then check and scroll only if opening
                    setTimeout(() => {
                        // Only scroll if the details was closed before (meaning it's being opened)
                        if (wasClosed && detailsElement.hasAttribute('open')) {
                            scrollToElement(detailId);
                        }
                    }, 50);
                });
            }
        }
    });
}

// Initialize number input scroll prevention
// Prevents scroll events from changing number input values when focused
// Instead, scrolls the page when a number input is focused and user scrolls
function initializeNumberInputScrollPrevention(page) {
    // Find all number input elements
    const numberInputs = page.querySelectorAll('input[type="number"]');
    
    // Add wheel event listener to each number input
    numberInputs.forEach(input => {
        input.addEventListener('wheel', function(e) {
            // Only block scroll-change while focused
            if (document.activeElement === this) {
                // Stop number increment/decrement
                e.preventDefault();
                // Scroll the page instead
                requestAnimationFrame(() => window.scrollBy({ top: e.deltaY, behavior: 'smooth' }));
            }
        }, { passive: false });
    });
}

// ==========================================
// UTILITY FUNCTIONS
// ==========================================

// Apply disabled state styling to an input element and optionally its container
// element: The input/checkbox element to disable/enable
// container: Optional container element to also apply disabled styling to
// isEnabled: Whether the element should be enabled (true) or disabled (false)
function applyDisabledState(element, container, isEnabled) {
    if (!element) return;
    
    element.disabled = !isEnabled;
    
    // Apply/remove disabled class styling
    if (isEnabled) {
        element.classList.remove('disabled');
        if (container) {
            container.classList.remove('disabled');
        }
    } else {
        element.classList.add('disabled');
        if (container) {
            container.classList.add('disabled');
        }
    }
}

// Set up scroll handlers for multiple dependent containers that scroll to the same target checkbox
// targetCheckboxSelector: Query selector string or element for the checkbox to scroll to
// dependentContainers: Array of container elements that should trigger scroll when disabled
function setupDisabledScrollHandlers(targetCheckboxSelector, dependentContainers) {
    if (!Array.isArray(dependentContainers)) {
        dependentContainers = [dependentContainers];
    }
    
    dependentContainers.forEach(container => {
        if (container) {
            addScrollToCheckboxHandler(container, targetCheckboxSelector);
        }
    });
}

// Scroll to a checkbox and highlight it
// targetCheckbox: Query selector string or element for the checkbox to scroll to
function scrollToCheckboxAndHighlight(targetCheckbox) {
    // Get the target checkbox (either from selector string or element)
    const checkbox = typeof targetCheckbox === 'string' 
        ? document.querySelector(targetCheckbox)
        : targetCheckbox;
    
    if (checkbox) {
        checkbox.scrollIntoView({ behavior: 'smooth', block: 'center' });
        
        // Briefly highlight the checkbox
        const container = checkbox.closest('.checkboxContainer');
        if (container) {
            container.style.transition = 'background-color 0.3s ease';
            container.style.backgroundColor = 'rgba(33, 150, 243, 0.2)';
            setTimeout(() => {
                container.style.backgroundColor = '';
                setTimeout(() => {
                    container.style.transition = '';
                }, 300);
            }, 1000);
        }
    }
}

// Add a scroll-to-checkbox handler when a disabled container is clicked
// containerElement: The container element that should trigger the scroll when disabled
// targetCheckboxSelector: Query selector string or element for the checkbox to scroll to
function addScrollToCheckboxHandler(containerElement, targetCheckboxSelector) {
    if (!containerElement || containerElement.hasAttribute('data-scroll-handler')) {
        return;
    }
    
    containerElement.setAttribute('data-scroll-handler', 'true');
    containerElement.addEventListener('click', function(e) {
        // Check if the container is disabled or contains disabled form elements
        const isDisabled = containerElement.classList.contains('disabled') || 
                          containerElement.querySelector('input:disabled, select:disabled, textarea:disabled, button:disabled');
        
        if (isDisabled) {
            // Don't trigger if clicking on a help icon (which should still work)
            if (e.target.closest('.helpIcon')) {
                return;
            }
            
            e.preventDefault();
            e.stopPropagation();
            scrollToCheckboxAndHighlight(targetCheckboxSelector);
        }
    });
}

// Append text to a result box
function appendToResultBox(element, text, newLine = false) {
    if (!element) return;
    
    // Get current content and split into lines
    const currentText = element.textContent || '';
    const isEmpty = currentText.trim().length === 0;
    const lines = currentText ? currentText.split('\n') : [];
    
    // Only add empty line separator if newLine is true AND the box is not empty
    if (newLine && !isEmpty) {
        lines.push('');
    }
    
    // Add new text and split into lines (preserves newlines within the text)
    const newLines = text.split('\n');
    lines.push(...newLines);
    
    // Join back and set content
    element.textContent = lines.join('\n');
    
    // Scroll to bottom when new results appear
    setTimeout(() => {
        element.scrollTop = element.scrollHeight;
    }, 0);
}

// Scroll to a specific element by ID with smooth scrolling
function scrollToElement(elementId, offset = 60) {
    const element = document.getElementById(elementId);
    if (element) {
        // Find and open all parent details elements without triggering onclick events
        let nextElement = element;
        const detailsToOpen = [];
        while (nextElement) {
            if (nextElement.tagName === 'DETAILS') {
                detailsToOpen.push(nextElement);
            }
            // Iterate over parents elements to open all containing parent sections
            nextElement = nextElement.parentElement;
        }
        
        // Open all parent details elements (in reverse order to open outer ones first)
        detailsToOpen.reverse().forEach(details => {
            const isVisible = details.style.display !== 'none';
            if (!isVisible) details.style.display = 'block';
            details.setAttribute('open', '');
        });
        
        // Wait a brief moment for details to open before scrolling
        setTimeout(() => {
            if (scrollTo) {
                const elementPosition = element.getBoundingClientRect().top;
                const pluginContainerHeight = 48; // Plugin container bar height
                const offsetPosition = elementPosition + window.pageYOffset - offset - pluginContainerHeight;
                
                window.scrollTo({
                    top: offsetPosition,
                    behavior: 'smooth'
                });
            }
            
            // Add a temporary highlight effect
            element.style.transition = 'box-shadow 0.3s ease';
            element.style.boxShadow = '0 0 10px rgba(0, 123, 255, 0.5)';
            setTimeout(() => {
                element.style.boxShadow = '';
            }, 2000);
        }, detailsToOpen.length > 0 ? 100 : 0); // Small delay only if we opened details
    }
}

// Initialize link spans - finds spans with class "link" and scrolls to elements with matching text
// Can accept either a page element or a container element
function initializeLinkSpans(pageOrContainer) {
    if (!window.JellyBridgeActions) {
        window.JellyBridgeActions = {};
    }
    // Register actions used by data-target-script
    window.JellyBridgeActions.cacheBuster = () => cacheBuster();

    const linkSpans = pageOrContainer.querySelectorAll('span.link');
    linkSpans.forEach(span => {
        // Skip if already initialized (has data-link-initialized attribute)
        if (span.hasAttribute('data-link-initialized')) {
            return;
        }
        
        // Mark as initialized to prevent duplicate bindings
        span.setAttribute('data-link-initialized', 'true');
        
        span.addEventListener('click', function() {
            // Handle router navigation
            const routerTarget = span.getAttribute('data-target-router');
            if (routerTarget) {
                Dashboard.navigate(routerTarget);
                return;
            }

            // Handle script execution (expects a global function name)
            const scriptTarget = span.getAttribute('data-target-script');
            if (scriptTarget) {
                const actions = window.JellyBridgeActions;
                const fn = actions[scriptTarget];
                fn();
                return;
            }

            // Handle page scrolling
            const pageTarget = span.getAttribute('data-target-page');
            if (pageTarget) {
                scrollToElement(pageTarget);
                return;
            }

            // No fallback text matching to avoid accidental scroll to top
            return;
        });
    });
}

// Global validators object
const validators = (() => {
    const windowsFolder = (value) => {
        if (!value) return true; // Allow empty values
        // Check for invalid Windows filename characters: \ / :
        const invalidChars = /[*?"<>|]/;
        // Windows folders cannot start with a space
        const invalidFolder = /^ |\/ |\\ /;
        return !invalidChars.test(value) && !invalidFolder.test(value);
    };

    return {
        notNull: (value) => {
            value = value.trim();
            return !!value && value !== '';
        },
        url: (value) => {
            value = value.trim();
            return !value || /^https?:\/\/.+/.test(value);
        },
        int: (value) => {
            value = value.trim();
            if (!value) return true; // Allow empty values
            const num = parseInt(value);
            return !isNaN(num) && num >= 0 && num <= 2147483647; // C# int max value
        },
        double: (value) => {
            value = value.trim();
            if (!value) return true; // Allow empty values
            const num = parseFloat(value);
            return !isNaN(num) && num >= 0 && num <= Number.MAX_VALUE;
        },
        windowsFolder: windowsFolder,
        windowsFilename: (value) => {
            if (!value) return true; // Allow empty values
            // Windows filenames cannot start with a space
            if (value.length > 0 && value[0] === ' ') return false;
            // Check for invalid Windows filename characters: * ? " < > |
            const invalidChars = /[\\/:]/;
            return windowsFolder(value) && !invalidChars.test(value);
        }
    };
})();

// Central field validation function
function validateField(form, fieldId, validator = null, errorMessage = null) {
    const field = form.querySelector(`#${fieldId}`);
    if (!field) {
        console.warn(`Field with ID "${fieldId}" not found`);
        return { isValid: false, error: `Field "${fieldId}" not found` };
    }
    
    // Check validator function if provided
    if (validator && !validator(field.value)) {
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
    const defaults = config.ConfigDefaults || {};
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
    const value = element.value.trim();
    if (value === null || value === undefined || value === '') {
        return null;
    }
    return parseInt(value);
}

function safeParseString(element, trim = true) {
    const value = element.value;
    if (value === null || value === undefined) {
        return '';
    }
    return trim ? value.trim() : value;
}

function safeParseDouble(element) {
    const value = element.value.trim();
    if (value === null || value === undefined || value === '') {
        return null;
    }
    return parseFloat(value);
}


