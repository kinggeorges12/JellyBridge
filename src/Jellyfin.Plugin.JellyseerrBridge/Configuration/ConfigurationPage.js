const JellyseerrBridgeConfigurationPage = {
    pluginUniqueId: '8ecc808c-d6e9-432f-9219-b638fbfb37e6'
};

// Scroll to a specific element by ID with smooth scrolling
function scrollToElement(elementId, offset = 20) {
    const element = document.getElementById(elementId);
    if (element) {
        const elementPosition = element.getBoundingClientRect().top;
        const offsetPosition = elementPosition + window.pageYOffset - offset;
        
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
    number: (value) => !value || !isNaN(parseFloat(value))
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
        fetch('/JellyseerrBridge/PluginConfiguration')
            .then(response => response.json())
            .then(function (config) {
                // Store configuration globally for other functions to use
                window.configJellyseerrBridge = config;
                
                // Initialize general settings including test connection
                initializeGeneralSettings(page);
                
                // Initialize library settings
                initializeLibrarySettings(page);
                
                // Initialize sync settings including network interface and sync buttons
                initializeSyncSettings(page);
                
                // Initialize advanced settings
                initializeAdvancedSettings(page);
                
                // Scroll to top of page after successful initialization
                scrollToElement('jellyseerrBridgeConfigurationPage');
                
                isInitialized = true;
            })
            .catch(function (error) {
                Dashboard.alert('❌ Failed to load configuration: ' + error.message);
                scrollToElement('jellyseerrBridgeConfigurationPage');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
    });
    
}

// Updates the enabled/disabled state of the Library Prefix field based on the Create Separate Libraries checkbox
function updateLibraryPrefixState() {
    const createSeparateLibrariesCheckbox = document.querySelector('#CreateSeparateLibraries');
    const libraryPrefixInput = document.querySelector('#LibraryPrefix');
    
    const isEnabled = createSeparateLibrariesCheckbox.checked;
    
    // Enable/disable the input
    libraryPrefixInput.disabled = !isEnabled;
    
    // Add/remove disabled styling
    if (isEnabled) {
        libraryPrefixInput.classList.remove('disabled');
    } else {
        libraryPrefixInput.classList.add('disabled');
    }
}

// Helper function to parse network options array and return network objects
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

// Helper function to update available networks list
function updateAvailableNetworks(page, networkMap = []) {
    const config = window.configJellyseerrBridge || {};
    
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

// Helper function to set input field value and placeholder
function setInputField(page, propertyName, isCheckbox = false) {
    const field = page.querySelector(`#${propertyName}`);
    if (!field) {
        Dashboard.alert(`❌ Field with ID "${propertyName}" not found`);
        return;
    }
    
    const config = window.configJellyseerrBridge || {};
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

// Function to initialize general settings including test connection
function initializeGeneralSettings(page) {
    // Set general settings form values with null handling
    setInputField(page, 'IsEnabled', true);
    setInputField(page, 'JellyseerrUrl');
    setInputField(page, 'ApiKey');
    setInputField(page, 'SyncIntervalHours');
    setInputField(page, 'AutoSyncOnStartup', true);
    
    const testButton = page.querySelector('#testConnection');
    if (!testButton) {
        Dashboard.alert('❌ Jellyseerr Bridge: Test connection button not found');
        return;
    }
    
    testButton.addEventListener('click', function () {
        const url = page.querySelector('#JellyseerrUrl').value.trim();
        const apiKey = page.querySelector('#ApiKey').value.trim();
        
        // Validate URL format if provided
        if (url && !validateField('JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
        
        // Validate API Key
        if (!validateField('ApiKey', validators.notNull, 'API Key is required for connection test').isValid) return;
        
        Dashboard.showLoadingMsg();
        
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
                            Dashboard.hideLoadingMsg();
                            Dashboard.processPluginConfigurationUpdateResult(result);
                        }).catch(function (error) {
                            Dashboard.hideLoadingMsg();
                            Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                            scrollToElement('jellyseerrBridgeConfigurationForm');
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
        });
    });
    
    // Add form submit event listener
    const form = page.querySelector('#jellyseerrBridgeConfigurationForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            Dashboard.showLoadingMsg();
            // Use the reusable function to save configuration
            savePluginConfiguration(page).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function (error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyseerrBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
            e.preventDefault();
            return false;
        });
    }
}

// Function to initialize library settings
function initializeLibrarySettings(page) {
    // Set library settings form values with null handling
    setInputField(page, 'LibraryDirectory');
    setInputField(page, 'ExcludeFromMainLibraries', true);
    setInputField(page, 'CreateSeparateLibraries', true);
    setInputField(page, 'LibraryPrefix');
    setInputField(page, 'ManageJellyseerrLibrary', true);
    
    updateLibraryPrefixState();
    
    // Test Favorites Scan button functionality
    const testFavoritesScanButton = page.querySelector('#testFavoritesScan');
    const testFavoritesScanResult = page.querySelector('#testFavoritesScanResult');
    
    if (testFavoritesScanButton) {
        testFavoritesScanButton.addEventListener('click', async function() {
            // Show immediate feedback that button was clicked
            testFavoritesScanResult.textContent = '🔄 Testing favorites scan...';
            testFavoritesScanResult.style.display = 'block';
            
            testFavoritesScanButton.disabled = true;
            testFavoritesScanButton.querySelector('span').textContent = 'Testing...';
            
            try {
                const response = await fetch('/JellyseerrBridge/TestFavoritesScan', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    }
                });
                
                const result = await response.json();
                
                if (response.ok) {
                    let resultText = `Favorites Scan Test Results:\n`;
                    resultText += `Total Users: ${result.totalUsers || 0}\n`;
                    resultText += `Users with Favorites: ${result.usersWithFavorites || 0}\n`;
                    resultText += `Total Favorite Items: ${result.totalFavorites || 0}\n\n`;
                    
                    if (result.userFavorites && result.userFavorites.length > 0) {
                        resultText += `User Favorites:\n`;
                        result.userFavorites.forEach((user, index) => {
                            resultText += `\n${index + 1}. ${user.userName} (${user.favoriteCount} favorites):\n`;
                            if (user.favorites && user.favorites.length > 0) {
                                user.favorites.forEach((favorite, favIndex) => {
                                    resultText += `   ${favIndex + 1}. ${favorite.name} (${favorite.type})\n`;
                                });
                            } else {
                                resultText += `   No favorites found.\n`;
                            }
                        });
                    } else {
                        resultText += `\nNo user favorites found.`;
                    }
                    
                    // Add test request results
                    if (result.testRequests && result.testRequests.length > 0) {
                        resultText += `\n\n=== Test Request Creation Results ===\n`;
                        result.testRequests.forEach((testRequest, index) => {
                            resultText += `\n${index + 1}. ${testRequest.type.toUpperCase()} Request:\n`;
                            resultText += `   Parameters: ${JSON.stringify(testRequest.parameters, null, 2).replace(/\n/g, '\n   ')}\n`;
                            resultText += `   Success: ${testRequest.success ? '✅' : '❌'}\n`;
                            if (testRequest.error) {
                                resultText += `   Error: ${testRequest.error}\n`;
                            }
                            if (testRequest.result) {
                                resultText += `   Response: ${JSON.stringify(testRequest.result, null, 2).replace(/\n/g, '\n   ')}\n`;
                            }
                        });
                    }
                    
                    testFavoritesScanResult.textContent = resultText;
                    testFavoritesScanResult.style.display = 'block';
                } else {
                    testFavoritesScanResult.textContent = `❌ Test failed: ${result.message || result.error || 'Unknown error'}`;
                    testFavoritesScanResult.style.display = 'block';
                }
            } catch (error) {
                testFavoritesScanResult.textContent = `❌ Test failed: ${error.message}`;
                testFavoritesScanResult.style.display = 'block';
            } finally {
                testFavoritesScanButton.disabled = false;
                testFavoritesScanButton.querySelector('span').textContent = 'Test Favorites Scan';
            }
        });
    }
}


// Function to initialize sync settings including network interface and sync buttons
function initializeSyncSettings(page) {
    const config = window.configJellyseerrBridge || {};
    
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
    populateSelectWithNetworks(activeNetworksSelect, config.NetworkMap || []);
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
                Dashboard.alert(`✅ Available networks refreshed successfully! Loaded ${availableNetworks.length} new networks.`);
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to refresh available networks: ' + (error?.message || 'Unknown error'));
                scrollToElement('syncSettings');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
    
    // Add manual sync button functionality
    const syncButton = page.querySelector('#manualSync');
    syncButton.addEventListener('click', function () {
        performManualSyncLibrary(page);
    });

    // Add reset plugin button functionality
    const resetButton = page.querySelector('#resetPlugin');
    resetButton.addEventListener('click', function () {
        performPluginReset(page);
    });

    // Add refresh networks button functionality
    const refreshButton = page.querySelector('#refreshNetworks');
    if (refreshButton) {
        refreshButton.addEventListener('click', function() {
            Dashboard.showLoadingMsg();
            loadRegions(page).then(function() {
                Dashboard.alert('✅ Regions refreshed successfully!');
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to refresh regions: ' + (error?.message || 'Unknown error'));
                scrollToElement('syncSettings');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        });
    }
}


// Function to initialize advanced settings
function initializeAdvancedSettings(page) {
    // Set advanced settings form values with null handling
    setInputField(page, 'RequestTimeout');
    setInputField(page, 'RetryAttempts');
    setInputField(page, 'MaxDiscoverPages');
    setInputField(page, 'MaxCollectionDays');
    setInputField(page, 'PlaceholderDurationSeconds');
    const placeholderDurationInput = page.querySelector('#PlaceholderDurationSeconds');
    if (placeholderDurationInput) {
        placeholderDurationInput.addEventListener('input', function() {
            if (this.value && parseInt(this.value) < 1) {
                this.value = '1';
            }
        });
    }
    setInputField(page, 'EnableDebugLogging', true);
    
    // Add event listener for Create Separate Libraries checkbox
    const createSeparateLibrariesCheckbox = page.querySelector('#CreateSeparateLibraries');
    if (createSeparateLibrariesCheckbox) {
        createSeparateLibrariesCheckbox.addEventListener('change', function() {
            updateLibraryPrefixState();
        });
    }
}

function updateClearButtonVisibility(clearButton, searchValue) {
    if (searchValue && searchValue.trim() !== '') {
        clearButton.style.display = 'flex';
    } else {
        clearButton.style.display = 'none';
    }
}

function loadAvailableNetworks(page) {
    const region = page.querySelector('#selectWatchRegion').value;
    
    Dashboard.alert(`🔍 DEBUG: Starting loadAvailableNetworks with region: "${region}"`);
    
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/Networks', { region: region }),
        type: 'GET',
        dataType: 'json'
    }).then(function(response) {
        Dashboard.alert(`🔍 DEBUG: API Response received for networks. Networks count: ${response?.length || 0}`);
        
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
    
    Dashboard.alert(`🔍 DEBUG: Added ${selectElement.options.length} network options to select element`);
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
    
    // Only sort the destination (available networks), maintain order in active networks
    if (toSelect.id === 'availableNetworks') {
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
    const config = window.configJellyseerrBridge || {};
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

function savePluginConfiguration(view) {
    const form = view.querySelector('#jellyseerrBridgeConfigurationForm');
    
    // Validate URL format if provided
    const url = form.querySelector('#JellyseerrUrl').value.trim();
    if (url && !validateField('JellyseerrUrl', validators.url, 'Jellyseerr URL must start with http:// or https://').isValid) return;
    
    // Validate API Key
    if (!validateField('ApiKey', validators.notNull, 'API Key is required').isValid) return;
    
    // Validate number fields
    if (!validateField('SyncIntervalHours', validators.number, 'Invalid Sync Interval').isValid) return;
    if (!validateField('RequestTimeout', validators.number, 'Invalid Request Timeout').isValid) return;
    if (!validateField('RetryAttempts', validators.number, 'Invalid Retry Attempts').isValid) return;
    if (!validateField('MaxDiscoverPages', validators.number, 'Invalid Max Discover Pages').isValid) return;
    if (!validateField('MaxCollectionDays', validators.number, 'Invalid Max Collection Days').isValid) return;
    
    // Use our custom endpoint to get the current configuration
    return fetch('/JellyseerrBridge/PluginConfiguration')
        .then(response => response.json())
        .then(function (config) {
            // Update config with current form values
            // Only include checkbox values if they differ from defaults
            config.IsEnabled = nullIfDefault(form.querySelector('#IsEnabled').checked, config.DefaultValues.IsEnabled);
            config.JellyseerrUrl = form.querySelector('#JellyseerrUrl').value.trim();
            config.ApiKey = form.querySelector('#ApiKey').value.trim();
            config.LibraryDirectory = form.querySelector('#LibraryDirectory').value.trim();
            config.SyncIntervalHours = safeParseDouble(form.querySelector('#SyncIntervalHours'));
            config.ExcludeFromMainLibraries = nullIfDefault(form.querySelector('#ExcludeFromMainLibraries').checked, config.DefaultValues.ExcludeFromMainLibraries);
            config.CreateSeparateLibraries = nullIfDefault(form.querySelector('#CreateSeparateLibraries').checked, config.DefaultValues.CreateSeparateLibraries);
            config.LibraryPrefix = form.querySelector('#LibraryPrefix').value.trim();
            config.AutoSyncOnStartup = nullIfDefault(form.querySelector('#AutoSyncOnStartup').checked, config.DefaultValues.AutoSyncOnStartup);
            config.Region = nullIfDefault(form.querySelector('#selectWatchRegion').value, config.DefaultValues.Region);
            config.NetworkMap = parseNetworkOptions(form.querySelector('#activeNetworks').options);
            config.RequestTimeout = safeParseInt(form.querySelector('#RequestTimeout'));
            config.RetryAttempts = safeParseInt(form.querySelector('#RetryAttempts'));
            config.MaxDiscoverPages = safeParseInt(form.querySelector('#MaxDiscoverPages'));
            config.MaxCollectionDays = safeParseInt(form.querySelector('#MaxCollectionDays'));
            config.PlaceholderDurationSeconds = safeParseInt(form.querySelector('#PlaceholderDurationSeconds'));
            config.EnableDebugLogging = nullIfDefault(form.querySelector('#EnableDebugLogging').checked, config.DefaultValues.EnableDebugLogging);
            config.ManageJellyseerrLibrary = nullIfDefault(form.querySelector('#ManageJellyseerrLibrary').checked, config.DefaultValues.ManageJellyseerrLibrary);
            
            // Save the configuration using our custom endpoint
            return fetch('/JellyseerrBridge/PluginConfiguration', {
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



function loadRegions(page) {
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/Regions'),
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

// Shared sync function
function performSyncLibrary(page) {
    const manualSyncResult = page.querySelector('#manualSyncResult');
    
    Dashboard.showLoadingMsg();
    
    return ApiClient.ajax({
        url: ApiClient.getUrl('JellyseerrBridge/SyncLibrary'),
        type: 'POST',
        data: '{}',
        contentType: 'application/json',
        dataType: 'json'
    }).then(function(syncData) {
        // Parse the sync results for better user feedback
        const message = syncData.message || 'Folder structure creation completed successfully';
        
        // Build detailed information if available
        let resultText = `Manual Sync Results:\n`;
        resultText += `✅ ${message}\n\n`;
        
        if (syncData.details) {
            resultText += `Details:\n${syncData.details}`;
        }
        
        manualSyncResult.textContent = resultText;
        manualSyncResult.style.display = 'block';
    }).catch(function(error) {
        let resultText = `Manual Sync Results:\n`;
        resultText += `❌ Folder structure creation failed: ${error?.message || 'Unknown error'}\n`;
        
        manualSyncResult.textContent = resultText;
        manualSyncResult.style.display = 'block';
    }).finally(function() {
        Dashboard.hideLoadingMsg();
    });
}

// Complete manual sync workflow
function performManualSyncLibrary(page) {
    // Show confirmation dialog for saving settings before sync
    Dashboard.confirm({
        title: 'Confirm Save',
        text: 'Settings will be saved before starting sync.',
        confirmText: 'Save & Sync',
        cancelText: 'Cancel',
        primary: "confirm"
    }, 'Title', (confirmed) => {
        if (confirmed) {
            // Save settings first, then sync
            Dashboard.showLoadingMsg();
            
            savePluginConfiguration(page).then(function(result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
                // sync if confirmed
                performSyncLibrary(page);
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to save configuration: ' + (error?.message || 'Unknown error'));
                scrollToElement('jellyseerrBridgeConfigurationForm');
            }).finally(function() {
                Dashboard.hideLoadingMsg();
            });
        }
    });
}

// Reset plugin configuration and delete data with double confirmation
function performPluginReset(page) {
    // Get current library directory before resetting configuration, fallback to default if empty
    const displayLibraryDir = getInputValue(page, 'LibraryDirectory') || getDefaultValue(page, 'LibraryDirectory') || '(not configured)';
    
    // First confirmation with information warning emoji
    Dashboard.confirm({
        title: '⚠️ Reset Plugin Configuration',
        text: 'This will reset ALL plugin settings to their default values. Are you sure you want to continue?',
        confirmText: 'Yes, Reset Settings',
        cancelText: 'Cancel',
        primary: "cancel"
    }, 'Title', (confirmed1) => {
        if (confirmed1) {
            // Reset configuration to defaults first
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
                MaxCollectionDays: null,
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
                url: ApiClient.getUrl('JellyseerrBridge/PluginConfiguration'),
                type: 'POST',
                data: JSON.stringify(resetConfig),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function(result) {
                Dashboard.alert('✅ Plugin configuration has been reset to defaults! Please refresh the page to see the changes.');
                
                // Second confirmation with warning emoji for data deletion
                Dashboard.confirm({
                    title: '❗ Delete Library Data',
                    text: `This will permanently delete ALL Jellyseerr library data: Delete ALL library folders and files, Remove ALL generated content, This action CANNOT be undone! Library Directory: "${displayLibraryDir}". Are you sure you want to delete the data?`,
                    confirmText: 'Yes! Proceed to final confirmation...',
                    cancelText: 'Cancel',
                    primary: "cancel"
                }, 'Title', (confirmed2) => {
                    if (confirmed2) {
                        // Third confirmation with emergency emoji for final data deletion
                        Dashboard.confirm({
                            title: '🚨 FINAL CONFIRMATION - DELETE DATA',
                            text: `LAST WARNING: This is your final chance to cancel! This will permanently delete ALL Jellyseerr library data: Delete ALL library folders and files, Remove ALL generated content, This action CANNOT be undone! Library Directory: "${displayLibraryDir}". Are you absolutely certain you want to proceed?`,
                            confirmText: '🚩 YES, DELETE EVERYTHING',
                            cancelText: 'Cancel',
                            primary: "cancel"
                        }, 'Title', (confirmed3) => {
                            if (confirmed3) {
                                // Proceed with data deletion
                                Dashboard.showLoadingMsg();
                                
                                ApiClient.ajax({
                                    url: ApiClient.getUrl('JellyseerrBridge/ResetPlugin'),
                                    type: 'POST',
                                    data: JSON.stringify({
                                        libraryDirectory: currentLibraryDir
                                    }),
                                    contentType: 'application/json',
                                    dataType: 'json'
                                }).then(function(result) {
                                    Dashboard.alert('✅ All Jellyseerr data has been deleted successfully! Please refresh the page to see the changes.');
                                    
                                    // Reload the page to show default values
                                    window.location.reload();
                                }).catch(function(error) {
                                    Dashboard.alert('❌ Failed to delete data: ' + (error?.message || 'Unknown error'));
                                }).finally(function() {
                                    Dashboard.hideLoadingMsg();
                                });
                            }
                        });
                    }
                });
            }).catch(function(error) {
                Dashboard.alert('❌ Failed to reset configuration: ' + (error?.message || 'Unknown error'));
            }).finally(function() {
                Dashboard.hideLoadingMsg();
                
                // Refresh the configuration page to show updated values
                const view = document.querySelector('.configurationPage');
                if (view) {
                    // Call the main configuration function to refresh the UI
                    const configModule = require('./ConfigurationPage.js');
                    configModule.default(view);
                }
            });
        }
    });
}



