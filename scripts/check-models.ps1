# Comprehensive Model Validation Script
# Validates all generated C# files for missing classes, enums, and conversion issues

param(
    [string]$ModelDir = "src\Jellyfin.Plugin.JellyseerrBridge\JellyseerrModel"
)

Write-Host "=== COMPREHENSIVE VALIDATION OF GENERATED C# FILES ===" -ForegroundColor Green
Write-Host ""

# Check if model directory exists
if (-not (Test-Path $ModelDir)) {
    Write-Host "Model directory not found: $ModelDir" -ForegroundColor Red
    Write-Host "Please run convert-models.ps1 first to generate the models." -ForegroundColor Yellow
    exit 1
}

# Get all generated files
$files = Get-ChildItem -Path $ModelDir -Recurse -Filter "*.cs"
Write-Host "Total files generated: $($files.Count)" -ForegroundColor Cyan

# Check for empty files
Write-Host "`n=== CHECKING FOR EMPTY FILES ===" -ForegroundColor Yellow
$emptyFiles = @()
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match '^\s*$' -or $content.Length -lt 50) {
        $emptyFiles += $file.Name
    }
}
if ($emptyFiles.Count -gt 0) {
    Write-Host "Empty or very small files:" -ForegroundColor Red
    $emptyFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "No empty files found" -ForegroundColor Green
}

# Check for missing using statements
Write-Host "`n=== CHECKING FOR MISSING USING STATEMENTS ===" -ForegroundColor Yellow
$missingUsing = @()

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # Check for List<> usage without System.Collections.Generic
    if ($content -match 'List<' -and $content -notmatch 'using System\.Collections\.Generic') {
        $missingUsing += $file.Name
    }
    
    # Check for DateTime usage without System
    if ($content -match 'DateTime' -and $content -notmatch 'using System') {
        $missingUsing += $file.Name
    }
}

if ($missingUsing.Count -gt 0) {
    Write-Host "Files missing using statements:" -ForegroundColor Red
    $missingUsing | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "All files have proper using statements" -ForegroundColor Green
}

# Check for missing class references
Write-Host "`n=== CHECKING FOR MISSING CLASS REFERENCES ===" -ForegroundColor Yellow

# Load blocked classes from config
$blockedClasses = @()
$configFile = "scripts\convert-config.psd1"
if (Test-Path $configFile) {
    $config = Import-PowerShellDataFile $configFile
    $blockedClasses = $config.BlockedClasses
    Write-Host "Loaded $($blockedClasses.Count) blocked classes from config" -ForegroundColor Cyan
}

# Build map of all classes with their namespaces
$allClasses = @{}
$fileNamespaces = @{}

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # Extract namespace from file
    $namespaceMatch = [regex]::Match($content, 'namespace\s+([^;{]+)')
    $namespace = if ($namespaceMatch.Success) { $namespaceMatch.Groups[1].Value.Trim() } else { "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel" }
    $fileNamespaces[$file.FullName] = $namespace
    
    # Extract all class/enum definitions in this file
    $classDefs = [regex]::Matches($content, '(?:public|internal)\s+(?:static\s+)?(?:class|enum)\s+(\w+)', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($match in $classDefs) {
        $className = $match.Groups[1].Value
        if (-not $allClasses.ContainsKey($className)) {
            $allClasses[$className] = @()
        }
        $allClasses[$className] += $namespace
    }
}

$missingClasses = @()
$basicTypes = @('string', 'int', 'double', 'bool', 'DateTime', 'DateTimeOffset', 'object', 'List', 'Array', 'Dictionary', 'Task', 'Action', 'Func', 'System', 'Text', 'Json', 'Serialization', 'Collections', 'Generic', 'ComponentModel', 'DataAnnotations', 'Schema', 'MediaRequestStatus', 'MediaType', 'MediaStatus', 'MediaServerType', 'ServerType', 'UserType', 'ApiErrorCode', 'DiscoverSliderType', 'IssueType', 'IssueStatus', 'T', 'K', 'V', 'U', 'R', 'S')

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $currentNamespace = $fileNamespaces[$file.FullName]
    
    # Extract using statements
    $usingStatements = @()
    $usingMatches = [regex]::Matches($content, 'using\s+([^;]+);')
    foreach ($usingMatch in $usingMatches) {
        $usingStatements += $usingMatch.Groups[1].Value.Trim()
    }
    
    # Find class references in property types and method signatures
    $classRefs = [regex]::Matches($content, '(?:public|private|protected|internal)\s+(?:readonly\s+)?([A-Z][a-zA-Z0-9]*)\s+\w+', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $classRefs) {
        $className = $match.Groups[1].Value
        
        # Skip basic types and known types
        if ($basicTypes -contains $className) { continue }
        
        # Skip if it's the class itself
        $currentClassName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        if ($className -eq $currentClassName) { continue }
        
        # Check if the referenced class exists and is accessible
        if ($allClasses.ContainsKey($className)) {
            $classNamespaces = $allClasses[$className]
            
            # Check if class is in current namespace
            $isInCurrentNamespace = $classNamespaces -contains $currentNamespace
            
            # Check if class is accessible via using statements
            $isAccessibleViaUsing = $false
            foreach ($classNamespace in $classNamespaces) {
                if ($usingStatements -contains $classNamespace) {
                    $isAccessibleViaUsing = $true
                    break
                }
            }
            
            # If not accessible, it's missing
            if (-not $isInCurrentNamespace -and -not $isAccessibleViaUsing) {
                $missingClasses += ($file.Name + ': ' + $className + ' (available in: ' + ($classNamespaces -join ', ') + ')')
            }
        } else {
            # Check if this is a blocked class - if so, don't report it as missing
            if ($blockedClasses -contains $className) {
                Write-Host "  Skipping blocked class: $className" -ForegroundColor DarkYellow
                continue
            }
            $missingClasses += ($file.Name + ': ' + $className + ' (not found anywhere)')
        }
    }
}

if ($missingClasses.Count -gt 0) {
    Write-Host "Missing class references:" -ForegroundColor Red
    $missingClasses | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "All class references are resolved" -ForegroundColor Green
}

# Check for type mismatches
Write-Host "`n=== CHECKING FOR TYPE MISMATCHES ===" -ForegroundColor Yellow
$typeMismatches = @()

# Known type mismatches to check for
$knownMismatches = @{
    'SeasonWithEpisodes.cs' = @{
        'Id' = 'int'
        'SeasonNumber' = 'int'
    }
    'PersonResult.cs' = @{
        'MediaType' = 'string?'
    }
}

foreach ($file in $files) {
    $fileName = $file.Name
    if ($knownMismatches.ContainsKey($fileName)) {
        $content = Get-Content $file.FullName -Raw
        foreach ($property in $knownMismatches[$fileName].Keys) {
            $expectedType = $knownMismatches[$fileName][$property]
            $typeMatch = [regex]::Match($content, "public\s+(\w+)\s+$property\s*\{")
            if ($typeMatch.Success) {
                $actualType = $typeMatch.Groups[1].Value
                if ($actualType -ne $expectedType) {
                    $typeMismatches += "${fileName}.${property}: expected ${expectedType}, got ${actualType}"
                }
            }
        }
    }
}

if ($typeMismatches.Count -gt 0) {
    Write-Host "Type mismatches found:" -ForegroundColor Red
    $typeMismatches | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "No type mismatches found" -ForegroundColor Green
}

# Check for missing enums
Write-Host "`n=== CHECKING FOR MISSING ENUMS ===" -ForegroundColor Yellow
$requiredEnums = @('MediaServerType', 'UserType', 'DiscoverSliderType', 'MediaRequestStatus', 'MediaStatus', 'IssueType', 'IssueStatus')
$missingEnums = @()

foreach ($enumName in $requiredEnums) {
    $enumFile = Get-ChildItem -Path $ModelDir -Recurse -Filter "*.cs" | Where-Object { 
        $content = Get-Content $_.FullName -Raw
        $content -match "enum\s+$enumName"
    }
    if (-not $enumFile) {
        $missingEnums += $enumName
    }
}

if ($missingEnums.Count -gt 0) {
    Write-Host "Missing required enums:" -ForegroundColor Red
    $missingEnums | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "All required enums are present" -ForegroundColor Green
}

# Check for duplicate class declarations across files
Write-Host "`n=== CHECKING FOR DUPLICATE CLASS DECLARATIONS ===" -ForegroundColor Yellow
$duplicateClasses = @()
$classDeclarations = @{}

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $currentNamespace = $fileNamespaces[$file.FullName]
    
    # Find all class declarations (including static classes)
    $classDefs = [regex]::Matches($content, '(?:public|internal)\s+(?:static\s+)?(?:class|enum)\s+(\w+)', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $classDefs) {
        $className = $match.Groups[1].Value
        $classKey = "${currentNamespace}.${className}"
        
        if ($classDeclarations.ContainsKey($classKey)) {
            $duplicateClasses += "$className (found in: $($classDeclarations[$classKey]), $($file.Name)) - same namespace: $currentNamespace"
        } else {
            $classDeclarations[$classKey] = $file.Name
        }
    }
}

if ($duplicateClasses.Count -gt 0) {
    Write-Host "Duplicate class declarations found:" -ForegroundColor Red
    $duplicateClasses | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "No duplicate class declarations found" -ForegroundColor Green
}

# Check for duplicate properties within each class
Write-Host "`n=== CHECKING FOR DUPLICATE PROPERTIES ===" -ForegroundColor Yellow
$duplicateProperties = @()

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    $currentClass = $null
    $inClass = $false
    $classProperties = @{}
    $braceCount = 0
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = if ($null -ne $lines[$i]) { $lines[$i].Trim() } else { "" }
        
        # Count braces to properly track class boundaries
        $openBraces = ($line -split '\{').Count - 1
        $closeBraces = ($line -split '\}').Count - 1
        $braceCount += $openBraces - $closeBraces
        
        # Check for class declaration
        $classMatch = [regex]::Match($line, '^public\s+class\s+(\w+)')
        if ($classMatch.Success) {
            $currentClass = $classMatch.Groups[1].Value
            $inClass = $true
            $classProperties = @{}
            continue
        }
        
        # Check for class end (when brace count returns to 0)
        if ($inClass -and $braceCount -eq 0) {
            $inClass = $false
            $currentClass = $null
            $classProperties = @{}
            continue
        }
        
        # Check for property declaration (only if we're inside a class)
        if ($inClass -and $currentClass) {
            $propertyMatch = [regex]::Match($line, 'public\s+[^}]+\s+(\w+)\s*\{')
            if ($propertyMatch.Success) {
                $propName = $propertyMatch.Groups[1].Value
                
                if ($classProperties.ContainsKey($propName)) {
                    $duplicateProperties += "${file.Name}: $currentClass.$propName"
                } else {
                    $classProperties[$propName] = $true
                }
            }
        }
    }
}

if ($duplicateProperties.Count -gt 0) {
    Write-Host "Duplicate properties found:" -ForegroundColor Red
    $duplicateProperties | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "No duplicate properties found" -ForegroundColor Green
}

# Check for invalid property names
Write-Host "`n=== CHECKING FOR INVALID PROPERTY NAMES ===" -ForegroundColor Yellow
$invalidProperties = @()

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # Check for properties with invalid names (like 'string' as property name)
    if ($content -match 'public\s+[^}]+\s+(string|int|bool|double)\s*\{') {
        $invalidProperties += "$($file.Name): invalid property name found"
    }
}

if ($invalidProperties.Count -gt 0) {
    Write-Host "Invalid property names found:" -ForegroundColor Red
    $invalidProperties | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "No invalid property names found" -ForegroundColor Green
}

# Summary
Write-Host "`n=== VALIDATION SUMMARY ===" -ForegroundColor Green
$totalIssues = $emptyFiles.Count + $missingUsing.Count + $missingClasses.Count + $typeMismatches.Count + $missingEnums.Count + $duplicateClasses.Count + $duplicateProperties.Count + $invalidProperties.Count

if ($totalIssues -eq 0) {
    Write-Host "✅ All validations passed! No issues found." -ForegroundColor Green
} else {
    Write-Host "❌ Found $totalIssues issues that need attention:" -ForegroundColor Red
    if ($emptyFiles.Count -gt 0) { Write-Host "  - $($emptyFiles.Count) empty files" -ForegroundColor Red }
    if ($missingUsing.Count -gt 0) { Write-Host "  - $($missingUsing.Count) files missing using statements" -ForegroundColor Red }
    if ($missingClasses.Count -gt 0) { Write-Host "  - $($missingClasses.Count) missing class references" -ForegroundColor Red }
    if ($typeMismatches.Count -gt 0) { Write-Host "  - $($typeMismatches.Count) type mismatches" -ForegroundColor Red }
    if ($missingEnums.Count -gt 0) { Write-Host "  - $($missingEnums.Count) missing enums" -ForegroundColor Red }
    if ($duplicateClasses.Count -gt 0) { Write-Host "  - $($duplicateClasses.Count) duplicate class declarations" -ForegroundColor Red }
    if ($duplicateProperties.Count -gt 0) { Write-Host "  - $($duplicateProperties.Count) duplicate properties" -ForegroundColor Red }
    if ($invalidProperties.Count -gt 0) { Write-Host "  - $($invalidProperties.Count) invalid property names" -ForegroundColor Red }
}

Write-Host "`nValidation completed." -ForegroundColor Cyan
