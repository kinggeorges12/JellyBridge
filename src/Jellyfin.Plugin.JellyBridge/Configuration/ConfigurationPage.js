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
            
            // Initialize manage discover library settings
            initializeOrganizeLibrary(page);
            
            // Initialize import discover content settings including network interface and sync buttons
            initializeImportContent(page);
            
            // Initialize manage discover library settings
            initializeManageFavorites(page);
            
            // Initialize upload promo video
            initializeUploadPromo(page);
            
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
            DisplayMessage('❌ Failed to load configuration: ' + (error?.message || error));
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
        page.querySelector('#legend').textContent = `JellyBridge Configuration (plugin version: ${config.PluginVersion})`;
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
    setInputField(page, 'JellyseerrUrl');
    setInputField(page, 'ApiKey');
    setInputField(page, 'LibraryDirectory');
    setInputField(page, 'IsEnabled', true);
    setInputField(page, 'EnableInMainMenu', true);
    setInputField(page, 'SyncIntervalHours');
    setInputField(page, 'EnableStartupSync', true);
    
    const fileInputField = page.querySelector('#LibraryDirectory');
    const fileBrowserButton = page.querySelector('#browseLibraryDirectory');
    fileBrowserButton.addEventListener("click", function() {
        browseFolder(fileInputField, "Select JellyBridge Library Directory");
    });

    
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
            DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
            scrollToElement('jellyBridgeConfigurationForm');
        }).finally(function() {
            Dashboard.hideLoadingMsg();
        });
        e.preventDefault();
        return false;
    });
}

function performTestConnection(page) {
    const testButton = page.querySelector('#testConnection');
    const url = safeParseString(page.querySelector('#JellyseerrUrl'));
    const apiKey = safeParseString(page.querySelector('#ApiKey'));
    const libraryDirectory = safeParseString(page.querySelector('#LibraryDirectory'));
    const CustomMoviesPromo = safeParseString(page.querySelector('#CustomMoviesPromo'));
    const CustomSeriesPromo = safeParseString(page.querySelector('#CustomSeriesPromo'));
    const jellyBridgeTempDirectory = safeParseString(page.querySelector('#JellyBridgeTempDirectory'));

    // Validate URL format if provided
    if (!validateField(page, 'JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
    
    // Validate API Key
    if (!validateField(page, 'ApiKey', validators.notNull, 'API Key is required for connection test').isValid) return;

    // Validate Library Directory
    if (!validateField(page, 'LibraryDirectory', validators.windowsFolder, 'Library Directory contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;

    // Validate Custom Movies promo
    let isDefaultMoviesPromo = page.querySelector('#DefaultMoviesPromo').checked;
    if (!isDefaultMoviesPromo && !validateField(page, 'CustomMoviesPromo', validators.windowsFolder, 'Custom Movies promo contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;

    // Validate Custom Series promo
    let isDefaultSeriesPromo = page.querySelector('#DefaultSeriesPromo').checked;
    if (!isDefaultSeriesPromo && !validateField(page, 'CustomSeriesPromo', validators.windowsFolder, 'Custom Series promo contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;

    // Validate Temp Directory
    if (!validateField(page, 'JellyBridgeTempDirectory', validators.windowsFolder, 'Temp Directory contains invalid characters. Folders cannot start with a space or contain: * ? " < > |').isValid) return;

    testButton.disabled = true;
    Dashboard.showLoadingMsg();
    
    const testData = {
        JellyseerrUrl: url,
        ApiKey: apiKey,
        LibraryDirectory: libraryDirectory,
        CustomMoviesPromo: isDefaultMoviesPromo ? null : CustomMoviesPromo,
        CustomSeriesPromo: isDefaultSeriesPromo ? null : CustomSeriesPromo,
        JellyBridgeTempDirectory: jellyBridgeTempDirectory
    };

    ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/TestConnection'),
        type: 'POST',
        data: JSON.stringify(testData),
        contentType: 'application/json',
        dataType: 'json'
    }).then(function (data) {
        // HTTP 200 response means connection test was successful
        let message = '✅ Connection test successful!';
        if (data && data.details) {
            message = '✅ ' + data.details;
        } else if (data && data.message) {
            message = '✅ ' + data.message;
        }
        DisplayMessage(message);
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
                        DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                    }).finally(function() {
                        Dashboard.hideLoadingMsg();
                    });
                } else {
                    DisplayMessage('🚫 Exited without saving');
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
            DisplayMessage(message);
        }
    }).finally(function() {
        Dashboard.hideLoadingMsg();
        testButton.disabled = false;
    });
}

// ==========================================
// ORGANIZE DISCOVER LIBRARY FUNCTIONS
// ==========================================

function initializeOrganizeLibrary(page) {
    // Add create default library event listener
    const createButton = page.querySelector('#createDefaultLibrary');
    createButton.addEventListener('click', function () {
        createDefaultLibrary(page);
    });
}

function getDefaultLibrarySettings(page, directories) {
    // These are default options that need to be validated from the server options
    let libraryJson = {
        "LibraryOptions": {
            "Enabled": true,
            "PathInfos": directories.map(dir => ({ "Path": dir })),
            "EnableRealtimeMonitor": false,
            "MetadataSavers": ["Nfo"],
            "LocalMetadataReaderOrder": ["Nfo"],
            "SaveLocalMetadata": true,
            "AllowEmbeddedSubtitles": "AllowNone",
            "DisabledSubtitleFetchers": [],
            "SubtitleFetcherOrder": [],
            "EnableTrickplayImageExtraction": false,
            "TypeOptions": [
                {
                    "Type": "Series",
                    "MetadataFetchers": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "MetadataFetcherOrder": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "ImageFetchers": ["TheMovieDb", "TheTVDB"],
                    "ImageFetcherOrder": ["TheMovieDb", "TheTVDB"]
                },
                {
                    "Type": "Season",
                    "MetadataFetchers": ["TheMovieDb", "TheTVDB"],
                    "MetadataFetcherOrder": ["TheMovieDb", "TheTVDB"],
                    "ImageFetchers": ["TheMovieDb", "TheTVDB"],
                    "ImageFetcherOrder": ["TheMovieDb", "TheTVDB"]
                },
                {
                    "Type": "Episode",
                    "MetadataFetchers": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "MetadataFetcherOrder": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "ImageFetchers": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "ImageFetcherOrder": ["TheMovieDb", "TheTVDB", "The Open Movie Database"]
                },
                {
                    "Type": "Movie",
                    "MetadataFetchers": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "MetadataFetcherOrder": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "ImageFetchers": ["TheMovieDb", "TheTVDB", "The Open Movie Database"],
                    "ImageFetcherOrder": ["TheMovieDb", "TheTVDB", "The Open Movie Database"]
                }
            ]
        }
    };

    // Validate library settings
    ApiClient.fetch({
        'url': ApiClient.getUrl('Libraries/AvailableOptions', {'IsNewLibrary': true}),
        'headers': {'accept': 'application/json'},
        'dataType': 'json'
    }).then(function(response) {
        if (!response) return;
        
        // Clean MetadataSavers
        if (response.MetadataSavers) {
            const validSaverNames = response.MetadataSavers.map(s => s.Name);
            libraryJson.LibraryOptions.MetadataSavers = libraryJson.LibraryOptions.MetadataSavers
                .filter(saver => validSaverNames.includes(saver));
        }
        
        // Clean LocalMetadataReaderOrder
        if (response.MetadataReaders) {
            const validReaderNames = response.MetadataReaders.map(r => r.Name);
            libraryJson.LibraryOptions.LocalMetadataReaderOrder = libraryJson.LibraryOptions.LocalMetadataReaderOrder
                .filter(reader => validReaderNames.includes(reader));
        }

        // Disable all subtitle fetchers
        if (response.SubtitleFetchers) {
            let subtitleFetcherNames = response.SubtitleFetchers.map(f => f.Name);
            libraryJson.LibraryOptions.DisabledSubtitleFetchers = subtitleFetcherNames;
            libraryJson.LibraryOptions.SubtitleFetcherOrder = subtitleFetcherNames;
        }
        
        // Clean TypeOptions - remove unavailable fetchers
        if (response.TypeOptions) {
            libraryJson.LibraryOptions.TypeOptions = libraryJson.LibraryOptions.TypeOptions
                .map(typeOption => {
                    // Find matching server type
                    const serverType = response.TypeOptions.find(t => t.Type === typeOption.Type);
                    if (!serverType) return null; // Type not available on server
                    
                    // Get available fetchers
                    const availableFetchers = serverType.MetadataFetchers.map(f => f.Name);
                    const availableImageFetchers = serverType.ImageFetchers.map(f => f.Name);
                    
                    // Filter MetadataFetchers
                    typeOption.MetadataFetchers = typeOption.MetadataFetchers
                        .filter(f => availableFetchers.includes(f));
                    typeOption.MetadataFetcherOrder = typeOption.MetadataFetcherOrder
                        .filter(f => availableFetchers.includes(f));
                    
                    // Filter ImageFetchers
                    typeOption.ImageFetchers = typeOption.ImageFetchers
                        .filter(f => availableImageFetchers.includes(f));
                    typeOption.ImageFetcherOrder = typeOption.ImageFetcherOrder
                        .filter(f => availableImageFetchers.includes(f));
                    
                    return typeOption;
                })
                .filter(type => type !== null); // Remove unavailable types
        }
    }).catch(function(error) {
        console.error('❌ Error fetching library options:', error);
        return Promise.reject(error);
    });

    return libraryJson;
}

function createVirtualFolders(page, libraryName, libraryType, directories) {
    return ApiClient.ajax({
        url: ApiClient.getUrl('Library/VirtualFolders', {
                refreshLibrary: true,
                name: libraryName,
                collectionType: libraryType
            }),
        type: 'POST',
        data: JSON.stringify(getDefaultLibrarySettings(page, directories)),
        contentType: 'application/json',
    }).then(function(response) {
        DisplayMessage("✅ Default library '" + libraryName + "' created successfully!");
    }).catch(function(error) {
        DisplayMessage('❌ Failed to create default library: ' + (error?.message || 'Unknown error'));
        return Promise.reject(error);
    });
}

function createDefaultLibrary(page) {
    const createButton = page.querySelector('#createDefaultLibrary');
    const config = window.configJellyBridge || {};
    const UseMixedMediaLibrary = nullIfDefault(page.querySelector('#UseMixedMediaLibrary').checked, config.ConfigDefaults.UseMixedMediaLibrary);
    const libraryDisplayNameInput = page.querySelector('#LibraryDisplayName');
    const movieLibraryDisplayNameInput = page.querySelector('#MovieLibraryDisplayName');
    const showLibraryDisplayNameInput = page.querySelector('#ShowLibraryDisplayName');
    const libraryDisplayName = safeParseString(libraryDisplayNameInput) || libraryDisplayNameInput.placeholder;
    const movieLibraryDisplayName = safeParseString(movieLibraryDisplayNameInput) || movieLibraryDisplayNameInput.placeholder;
    const showLibraryDisplayName = safeParseString(showLibraryDisplayNameInput) || showLibraryDisplayNameInput.placeholder;
    const libraryDisplayMessage = UseMixedMediaLibrary === false ?
        `new libraries "${movieLibraryDisplayName}" (Movies) and "${showLibraryDisplayName}" (Shows)`:
        `a new library "${libraryDisplayName}"`;

    // Show confirmation dialog for confirming library creation
    Dashboard.confirm({
        title: 'Confirm New Library',
        text: 'Save the current configuration and create ' + libraryDisplayMessage + ' with the recommended settings.',
        confirmText: '💾 Save & Create ✨',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            createButton.disabled = true;
            Dashboard.showLoadingMsg();

            savePluginConfiguration(page).catch(function(error) {
                // Catch config save errors
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                return Promise.reject(error);
            }).then(function() {
                // Generate folders before generating libraries
                return generateLibraryFolders(page)
                    .catch(function(error) {
                        DisplayMessage('❌ Failed to create network folders: ' + (error?.message || 'Unknown error'));
                        return Promise.reject(error);
                    });
            }).then(function (response) {
                // Send request for a new library
                let apiCalls;
                if (UseMixedMediaLibrary === false) {
                    apiCalls = Promise.all([
                        createVirtualFolders(page, libraryDisplayName + ' Movies', 'movies', response.movieFolders),
                        createVirtualFolders(page, libraryDisplayName + ' Shows', 'tvshows', response.showFolders)
                    ]);
                } else {
                    apiCalls = createVirtualFolders(page, libraryDisplayName, null, response.mixedFolders);
                }
                return apiCalls;
            }).finally(function() {
                scrollToElement('librarySetupInstructions');
                Dashboard.hideLoadingMsg();
                createButton.disabled = false;
            });
        }
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
                DisplayMessage('❗ Jellyseerr connection information has changed. Please save your settings and try again.');
                scrollToElement('saveConfig');
                return;
            }
            Dashboard.showLoadingMsg();
            loadAvailableNetworks(page)
            .then(function(availableNetworks) {
                if (availableNetworks) {
                    DisplayMessage(`✅ Refreshed available networks`);
                    scrollToElement('availableNetworksSelectBox');
                }
            }).catch(function() {
                DisplayMessage('❌ Failed to refresh available networks (try Test Connection to Jellyseerr first)');
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
    if (syncButton) {
        syncButton.addEventListener('click', function () {
            performSyncImportContent(page);
        });
    }

    // Add reset plugin config button functionality
    const resetPluginConfigButton = page.querySelector('#resetPluginConfig');
        if (resetPluginConfigButton) {
        resetPluginConfigButton.addEventListener('click', function () {
            performPluginReset(page);
        });
    }

    // Add recycle library data button functionality
    const recycleLibraryButton = page.querySelector('#recycleLibraryData');
        if (recycleLibraryButton) {
        recycleLibraryButton.addEventListener('click', function () {
            performRecycleLibraryData(page);
        });
    }

    // Add refresh networks button functionality
    const refreshButton = page.querySelector('#refreshWatchRegions');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            const config = window.configJellyBridge || {};
            if(config.JellyseerrUrl != page.querySelector('#JellyseerrUrl').value){
                DisplayMessage('❗ Jellyseerr connection information has changed. Please save your settings and try again.');
                scrollToElement('saveConfig');
                return;
            }
            Dashboard.showLoadingMsg();
            loadRegions(page).then(function() {
                DisplayMessage('✅ Refreshed regions');
                scrollToElement('selectWatchRegion');
            }).catch(function() {
                DisplayMessage('❌ Failed to refresh available networks (try Test Connection to Jellyseerr first)');
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
            
            const syncDiscoverResult = page.querySelector('#syncDiscoverResult');
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the sync result textbox
                syncDiscoverResult.style.display = 'block';
                appendToResultBox(syncDiscoverResult, '🔄 Syncing library...', true);
                appendToResultBox(syncDiscoverResult, "⏳ " + new Date().toLocaleTimeString());
                
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
                    DisplayMessage('❌ Sync failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nDiscover Sync Results:\n`;
                    resultText += `❌ Folder structure creation failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(syncDiscoverResult, resultText);
                    scrollToElement('syncDiscoverResult');
                });
            }).catch(function(error) {
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                appendToResultBox(syncDiscoverResult, "⏰ " + new Date().toLocaleTimeString());
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
            DisplayMessage(`❌ ERROR: Invalid network format: ${JSON.stringify(network)}. Expected format: { id: number, name: string }`);
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
// CUSTOMIZE PROMO VIDEO FUNCTIONS
// ==========================================


function initializeUploadPromo(page) {
    // Load initial status
    setInputField(page, 'CustomMoviesPromo');
    const CustomMoviesPromo = page.querySelector('#CustomMoviesPromo');
    const browseCustomMoviesPromo = page.querySelector('#browseCustomMoviesPromo');
    browseCustomMoviesPromo.addEventListener("click", function() {
        browseFolder(CustomMoviesPromo, "Select an Image File for the Movie Promo Video", true);
    });
    setInputField(page, 'DefaultMoviesPromo', true);

    setInputField(page, 'CustomSeriesPromo');
    const CustomSeriesPromo = page.querySelector('#CustomSeriesPromo');
    const browseCustomSeriesPromo = page.querySelector('#browseCustomSeriesPromo');
    browseCustomSeriesPromo.addEventListener("click", function() {
        browseFolder(CustomSeriesPromo, "Select an Image File for the Series Promo Video", true);
    });
    setInputField(page, 'DefaultSeriesPromo', true);

    setInputField(page, 'JellyBridgeTempDirectory');
    const JellyBridgeTempDirectory = page.querySelector('#JellyBridgeTempDirectory');
    const browseJellyBridgeTempDirectory = page.querySelector('#browseJellyBridgeTempDirectory');
    browseJellyBridgeTempDirectory.addEventListener("click", function() {
        browseFolder(JellyBridgeTempDirectory, "Select Promo Video Temp Directory");
    });

    loadPromoVideos(page);

    const inputPromoVideoDurationSeconds = page.querySelector('#PromoVideoDurationSeconds');
    if (inputPromoVideoDurationSeconds) {
        inputPromoVideoDurationSeconds.addEventListener('input', function() {
            if (this.value && parseInt(this.value) < 1) {
                this.value = '1';
            }
        });
    }
    
    // Send Discover Library Favorite Requests to Jellyseerr button functionality
    const customizePromoButton = page.querySelector('#generatePromoVideos');
    customizePromoButton.addEventListener('click', function() {
        performCustomizePromo(page);
    });
}

function loadPromoVideos(page) {
    const promoUrl = ApiClient.getUrl('JellyBridge/PromoVideos');
    
    const moviesPromoVideo = document.getElementById('moviesPromoVideo');
    // Add cache-busting parameter
    const moviesUrl = `${promoUrl}/movies?t=${Date.now()}`;
    moviesPromoVideo.innerHTML = `<source src="${moviesUrl}" type="video/mp4">`
        + 'Your browser does not support the video tag.';
    moviesPromoVideo.load();

    const seriesPromoVideo = document.getElementById('seriesPromoVideo');
    // Add cache-busting parameter
    const seriesUrl = `${promoUrl}/series?t=${Date.now()}`;
    seriesPromoVideo.innerHTML = `<source src="${seriesUrl}" type="video/mp4">`
        + 'Your browser does not support the video tag.';
    seriesPromoVideo.load();
}

function performCustomizePromo(page) {
    const customizePromoButton = page.querySelector('#generatePromoVideos');
    
    // Show confirmation dialog for saving settings before requesting content
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before generating JellyBridge Library promo videos.',
        confirmText: '💾 Save & Generate 🎥',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            customizePromoButton.disabled = true;
            // Save settings first, then request content
            Dashboard.showLoadingMsg();
            
            const generatePromoVideosResult = page.querySelector('#generatePromoVideosResult');
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the request result textbox
                generatePromoVideosResult.style.display = 'block';
                appendToResultBox(generatePromoVideosResult, '🔄 Generating JellyBridge Library Promo Videos...', true);
                appendToResultBox(generatePromoVideosResult, "⏳ " + new Date().toLocaleTimeString());
                
                Dashboard.processPluginConfigurationUpdateResult(result);
                // Request content if confirmed
                Dashboard.showLoadingMsg();
                return ApiClient.ajax({
                    url: ApiClient.getUrl('JellyBridge/PromoVideos'),
                    type: 'POST',
                    data: '{}',
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function(syncResult) {
                    appendToResultBox(generatePromoVideosResult, '\n' + (syncResult.result || 'No result available'));
                    scrollToElement('generatePromoVideosResult');
                    loadPromoVideos(page);
                }).catch(function(error) {
                    DisplayMessage('❌ Request JellyBridge Library Promo Videos failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nRequest JellyBridge Library Promo Videos Results:\n`;
                    resultText += `❌ Request failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(generatePromoVideosResult, resultText);
                    scrollToElement('generatePromoVideosResult');
                });
            }).catch(function(error) {
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                appendToResultBox(generatePromoVideosResult, "⏰ " + new Date().toLocaleTimeString());
                Dashboard.hideLoadingMsg();
                customizePromoButton.disabled = false;
            });
        }
    });
}

// ==========================================
// SORT DISCOVER CONTENT FUNCTIONS
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
    setInputField(page, 'SortTaskIntervalHours');
    setInputField(page, 'MarkMediaPlayed', true);
    setInputField(page, 'EnableSortLibraryRefresh', true);

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
            
            const sortContentResult = page.querySelector('#sortContentResult');
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the sort result textbox
                const sortOrderSelect = page.querySelector('#selectSortOrder');
                const selectedOption = sortOrderSelect ? sortOrderSelect.options[sortOrderSelect.selectedIndex] : null;
                const algorithmName = selectedOption ? selectedOption.textContent : 'Sort';
                sortContentResult.style.display = 'block';
                appendToResultBox(sortContentResult, `🔄 Applying ${algorithmName} algorithm to sort order...`, true);
                appendToResultBox(sortContentResult, "⏳ " + new Date().toLocaleTimeString());
                
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
                    DisplayMessage('❌ Sort content failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nSort Content Results:\n`;
                    resultText += `❌ Sort failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(sortContentResult, resultText);
                    scrollToElement('sortContentResult');
                });
            }).catch(function(error) {
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                appendToResultBox(sortContentResult, "⏰ " + new Date().toLocaleTimeString());
                Dashboard.hideLoadingMsg();
                sortButton.disabled = false;
            });
        }
    });
}

// ==========================================
// MANAGE DISCOVER LIBRARY FUNCTIONS
// ==========================================

function initializeManageFavorites(page) {
    // Set library settings form values with null handling
    setInputField(page, 'ManageJellyBridgeLibrary', true);
    setInputField(page, 'ExcludeFromMainLibraries', true);
    setInputField(page, 'ResponsiveFavoriteRequests', true);
    setInputField(page, 'RemoveRequestedFromFavorites', true);
    setInputField(page, 'UserPermissionRequest4k', true);
    setInputField(page, 'RequestFirstSeason', true);
    setInputField(page, 'UseMixedMediaLibrary', true);
    setInputField(page, 'UseNetworkFolders', true);
    setInputField(page, 'AddDuplicateContent', true);
    setInputField(page, 'NetworkFolderPrefix');
    
    // Send Discover Library Favorite Requests to Jellyseerr button functionality
    const syncFavoritesButton = page.querySelector('#syncFavorites');
    syncFavoritesButton.addEventListener('click', function() {
        performSyncFavorites(page);
    });
}

function generateLibraryFolders(page) {
    // Call GenerateLibraryFolders endpoint
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyBridge/GenerateLibraryFolders'),
        type: 'POST',
        data: '{}',
        contentType: 'application/json',
        dataType: 'json'
    });
}

function performSyncFavorites(page) {
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
            
            const syncFavoritesResult = page.querySelector('#syncFavoritesResult');
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the request result textbox
                syncFavoritesResult.style.display = 'block';
                appendToResultBox(syncFavoritesResult, '🔄 Requesting JellyBridge Library Favorites in Jellyseerr...', true);
                appendToResultBox(syncFavoritesResult, "⏳ " + new Date().toLocaleTimeString());
                
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
                    DisplayMessage('❌ Send Discover Library Favorite Requests to Jellyseerr failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nSend Discover Library Favorite Requests to Jellyseerr Results:\n`;
                    resultText += `❌ Request failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(syncFavoritesResult, resultText);
                    scrollToElement('syncFavoritesResult');
                });
            }).catch(function(error) {
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                appendToResultBox(syncFavoritesResult, "⏰ " + new Date().toLocaleTimeString());
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
    setInputField(page, 'PromoVideoDurationSeconds');
    setInputField(page, 'EnableStartupSync', true);
    setInputField(page, 'StartupDelaySeconds');
    setInputField(page, 'TaskTimeoutMinutes');
    setInputField(page, 'EnableDebugLogging', true);
    setInputField(page, 'EnableTraceLogging', true);
    
    // Library Prefix real-time validation
    const networkFolderPrefixInput = page.querySelector('#NetworkFolderPrefix');
    if (networkFolderPrefixInput) {
        networkFolderPrefixInput.addEventListener('input', function() {
            validateField(page, 'NetworkFolderPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot start with a space or contain: \\ / : * ? " < > |');
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
            
            const cleanupMetadataResult = page.querySelector('#cleanupMetadataResult');
            savePluginConfiguration(page).then(function(result) {
                // Show loading message in the cleanup result textbox
                cleanupMetadataResult.style.display = 'block';
                appendToResultBox(cleanupMetadataResult, '🔄 Cleaning up metadata...', true);
                appendToResultBox(cleanupMetadataResult, "⏳ " + new Date().toLocaleTimeString());
                
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
                    appendToResultBox(cleanupMetadataResult, '\n' + (cleanupData?.result || 'No result available'));
                    scrollToElement('cleanupMetadataResult');
                }).catch(function(error) {
                    DisplayMessage('❌ Cleanup failed: ' + (error?.message || 'Unknown error'));
                    
                    let resultText = `\nCleanup Results:\n`;
                    resultText += `❌ Cleanup failed: ${error?.message || 'Unknown error'}\n`;
                    
                    appendToResultBox(cleanupMetadataResult, resultText);
                    scrollToElement('cleanupMetadataResult');
                });
            }).catch(function(error) {
                DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyBridgeConfigurationForm');
            }).finally(function() {
                appendToResultBox(cleanupMetadataResult, "⏰ " + new Date().toLocaleTimeString());
                Dashboard.hideLoadingMsg();
                cleanupButton.disabled = false;
            });
        }
    });
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
                IsEnabled: null,
                EnableInMainMenu: null,
                SyncIntervalHours: null,
                RequestTimeout: null,
                RetryAttempts: null,
                MaxDiscoverPages: null,
                MaxRetentionDays: null,
                ExcludeFromMainLibraries: null,
                ResponsiveFavoriteRequests: null,
                RemoveRequestedFromFavorites: null,
                UserPermissionRequest4k: null,
                RequestFirstSeason: null,
                UseMixedMediaLibrary: null,
                UseNetworkFolders: null,
                NetworkFolderPrefix: '',
                AddDuplicateContent: null,
                ManageJellyBridgeLibrary: null,
                CustomMoviesPromo: null,
                DefaultMoviesPromo: null,
                CustomSeriesPromo: null,
                DefaultSeriesPromo: null,
                PromoVideoDurationSeconds: null,
                EnableAutomatedSortTask: null,
                SortTaskIntervalHours: null,
                SortOrder: null,
                MarkMediaPlayed: null,
                JellyBridgeTempDirectory: '',
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
                DisplayMessage('✅ Plugin configuration has been reset to defaults! ⟳ Refreshing the page...');
                
                // Reload the page to show default values
                setTimeout(() => {
                    window.location.reload();
                }, 2000);
            }).catch(function(error) {
                DisplayMessage('❌ Failed to reset configuration: ' + (error?.message || 'Unknown error'));
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
                    DisplayMessage('✅ All JellyBridge library data has been deleted successfully.');
                }).catch(function(error) {
                    DisplayMessage('❌ Failed to delete library data: ' + (error?.message || 'Unknown error'));
                }).finally(function() {
                    Dashboard.hideLoadingMsg();
                    recycleLibraryButton.disabled = false;
                });
            });
        }).catch(function(error) {
            Dashboard.hideLoadingMsg();
            DisplayMessage('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
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
        if (!validateField(page, 'PromoVideoDurationSeconds', validators.int, 'Promo Video Duration must be a positive integer').isValid) return;
        if (!validateField(page, 'NetworkFolderPrefix', validators.windowsFilename, 'Library Prefix contains invalid characters. Cannot start with a space or contain: \\ / : * ? " < > |').isValid) return;
        return true;
    }
    
    // Return early if validation fails
    if (!validateInputs()) return Promise.reject(new Error('Validation failed'));
    
    // Update config with current form values
    // Only include checkbox values if they differ from defaults
    form.JellyseerrUrl = safeParseString(page.querySelector('#JellyseerrUrl'));
    form.ApiKey = safeParseString(page.querySelector('#ApiKey'));
    form.LibraryDirectory = safeParseString(page.querySelector('#LibraryDirectory'));
    form.IsEnabled = nullIfDefault(page.querySelector('#IsEnabled').checked, config.ConfigDefaults.IsEnabled);
    form.EnableInMainMenu = nullIfDefault(page.querySelector('#EnableInMainMenu').checked, config.ConfigDefaults.EnableInMainMenu);
    form.SyncIntervalHours = safeParseDouble(page.querySelector('#SyncIntervalHours'));
    form.Region = nullIfDefault(page.querySelector('#selectWatchRegion').value, config.ConfigDefaults.Region);
    form.NetworkMap = parseNetworkOptions(page.querySelector('#activeNetworks').options);
    form.RequestTimeout = safeParseInt(page.querySelector('#RequestTimeout'));
    form.RetryAttempts = safeParseInt(page.querySelector('#RetryAttempts'));
    form.MaxDiscoverPages = safeParseInt(page.querySelector('#MaxDiscoverPages'));
    form.MaxRetentionDays = safeParseInt(page.querySelector('#MaxRetentionDays'));
    form.ManageJellyBridgeLibrary = nullIfDefault(page.querySelector('#ManageJellyBridgeLibrary').checked, config.ConfigDefaults.ManageJellyBridgeLibrary);
    form.ExcludeFromMainLibraries = nullIfDefault(page.querySelector('#ExcludeFromMainLibraries').checked, config.ConfigDefaults.ExcludeFromMainLibraries);
    form.ResponsiveFavoriteRequests = nullIfDefault(page.querySelector('#ResponsiveFavoriteRequests').checked, config.ConfigDefaults.ResponsiveFavoriteRequests);
    form.RemoveRequestedFromFavorites = nullIfDefault(page.querySelector('#RemoveRequestedFromFavorites').checked, config.ConfigDefaults.RemoveRequestedFromFavorites);
    form.UserPermissionRequest4k = nullIfDefault(page.querySelector('#UserPermissionRequest4k').checked, config.ConfigDefaults.UserPermissionRequest4k);
    form.RequestFirstSeason = nullIfDefault(page.querySelector('#RequestFirstSeason').checked, config.ConfigDefaults.RequestFirstSeason);
    form.UseMixedMediaLibrary = nullIfDefault(page.querySelector('#UseMixedMediaLibrary').checked, config.ConfigDefaults.UseMixedMediaLibrary);
    form.UseNetworkFolders = nullIfDefault(page.querySelector('#UseNetworkFolders').checked, config.ConfigDefaults.UseNetworkFolders);
    form.AddDuplicateContent = nullIfDefault(page.querySelector('#AddDuplicateContent').checked, config.ConfigDefaults.AddDuplicateContent);
    form.NetworkFolderPrefix = safeParseString(page.querySelector('#NetworkFolderPrefix'), false);
    form.EnableStartupSync = nullIfDefault(page.querySelector('#EnableStartupSync').checked, config.ConfigDefaults.EnableStartupSync);
    form.CustomMoviesPromo = safeParseString(page.querySelector('#CustomMoviesPromo'));
    form.DefaultMoviesPromo = nullIfDefault(page.querySelector('#DefaultMoviesPromo').checked, config.ConfigDefaults.DefaultMoviesPromo);
    form.CustomSeriesPromo = safeParseString(page.querySelector('#CustomSeriesPromo'));
    form.DefaultSeriesPromo = nullIfDefault(page.querySelector('#DefaultSeriesPromo').checked, config.ConfigDefaults.DefaultSeriesPromo);
    form.PromoVideoDurationSeconds = safeParseInt(page.querySelector('#PromoVideoDurationSeconds'));
    form.JellyBridgeTempDirectory = safeParseString(page.querySelector('#JellyBridgeTempDirectory'));
    form.EnableAutomatedSortTask = nullIfDefault(page.querySelector('#EnableAutomatedSortTask').checked, config.ConfigDefaults.EnableAutomatedSortTask);
    form.SortTaskIntervalHours = safeParseDouble(page.querySelector('#SortTaskIntervalHours'));
    form.SortOrder = nullIfDefault(page.querySelector('#selectSortOrder').value, config.ConfigDefaults.SortOrder);
    form.MarkMediaPlayed = nullIfDefault(page.querySelector('#MarkMediaPlayed').checked, config.ConfigDefaults.MarkMediaPlayed);
    form.EnableSortLibraryRefresh = nullIfDefault(page.querySelector('#EnableSortLibraryRefresh').checked, config.ConfigDefaults.EnableSortLibraryRefresh);
    form.StartupDelaySeconds = safeParseInt(page.querySelector('#StartupDelaySeconds'));
    form.TaskTimeoutMinutes = safeParseInt(page.querySelector('#TaskTimeoutMinutes'));
    form.EnableDebugLogging = nullIfDefault(page.querySelector('#EnableDebugLogging').checked, config.ConfigDefaults.EnableDebugLogging);
    form.EnableTraceLogging = nullIfDefault(page.querySelector('#EnableTraceLogging').checked, config.ConfigDefaults.EnableTraceLogging);
    
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
// DEPENDENCY CHECKBOXES
// ==========================================

function initDependentCheckboxes(page) {
    // Find all dependent elements
    const dependentElementsOn = page.querySelectorAll('[data-depends-on]');
    const dependentElementsOff = page.querySelectorAll('[data-depends-off]');
    
    // Loop through each dependent element
    dependentElementsOn.forEach(function(element) {
        const checkboxId = element.getAttribute('data-depends-on');
        const checkbox = page.querySelector(`#${checkboxId}`) || document.getElementById(checkboxId);
        
        if (!checkbox) {
            console.warn(`Checkbox "${checkboxId}" not found`);
            return;
        }
        
        // Add change listener to the checkbox
        checkbox.addEventListener('change', function() {
            updateStartupSyncDescription();
            toggleDependentElement(element, this.checked);
        });
        
        // Add click listener to the dependent element when disabled
        element.addEventListener('click', function(e) {
            if (this.classList.contains('disabled')) {
                e.preventDefault();
                e.stopPropagation();
                scrollToCheckboxAndHighlight(checkbox);
            }
        });
        
        // Set initial state
        toggleDependentElement(element, checkbox.checked);
    });
    
    // Loop through each dependent element
    dependentElementsOff.forEach(function(element) {
        const checkboxId = element.getAttribute('data-depends-off');
        const checkbox = page.querySelector(`#${checkboxId}`) || document.getElementById(checkboxId);
        
        if (!checkbox) {
            console.warn(`Checkbox "${checkboxId}" not found`);
            return;
        }
        
        // Add change listener to the checkbox
        checkbox.addEventListener('change', function() {
            updateStartupSyncDescription();
            toggleDependentElement(element, !this.checked);
        });
        
        // Add click listener to the dependent element when disabled
        element.addEventListener('click', function(e) {
            if (this.classList.contains('disabled')) {
                e.preventDefault();
                e.stopPropagation();
                scrollToCheckboxAndHighlight(checkbox);
            }
        });
        
        // Set initial state
        toggleDependentElement(element, !checkbox.checked);
    });
}

// Simple toggle function
function toggleDependentElement(element, isChecked) {
    const shouldDisable = !isChecked; // Disabled when unchecked
    
    if (shouldDisable) {
        element.classList.add('disabled');
    } else {
        element.classList.remove('disabled');
    }
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
    // Initialize dependent checkboxes
    initDependentCheckboxes(page);
}

// Initialize scroll-to functionality for detail tabs
function initializeDetailTabScroll(page) {
    // get all detail sections
    page.querySelectorAll('details').forEach((detailsElement) => {
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
                        scrollToElement(detailsElement.id);
                    }
                }, 50);
            });
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

function DisplayMessage(message){
    Dashboard.alert(message);
    console.log(message);
}

// Open a directory browser dialog to select a folder and set the input value
function browseFolder(inputElement, title, includeFiles = false) {
    // Create the dialog box
    const directoryBrowser = new Dashboard.DirectoryBrowser();

    // Get the current value from the input element
    let currentPath = inputElement.value || inputElement.placeholder || "";
    // If includeFiles is true, we're browsing for a file, so navigate to the parent directory
    if (includeFiles && currentPath) {
        // Find the last slash and remove everything after it
        const lastSlashIndex = Math.max(currentPath.lastIndexOf('/'), currentPath.lastIndexOf('\\'));
        if (lastSlashIndex > 0) {
            currentPath = currentPath.substring(0, lastSlashIndex);
        } else {
            currentPath = '';
        }
    }
    // Show the directory browser dialog
    directoryBrowser.show({
        header: title || "Select a Folder",
        path: currentPath,
        includeDirectories: true,
        includeFiles: includeFiles,
        callback: function(path) {
            inputElement.value = path;
            directoryBrowser.close();
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
        DisplayMessage(`❌ ${message}`);
        scrollToElement(fieldId);
        return { isValid: false, error: message };
    }
    
    return { isValid: true, error: null };
}

// Helper function to set input field value and placeholder
function setInputField(page, propertyName, isCheckbox = false) {
    const field = page.querySelector(`#${propertyName}`);
    if (!field) {
        DisplayMessage(`❌ Field with ID "${propertyName}" not found`);
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


