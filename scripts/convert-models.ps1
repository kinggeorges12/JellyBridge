# TypeScript to C# Model Converter
# Converts Jellyseerr TypeScript model files to C# classes

param(
    [string]$SeerrRootDir = "codebase/seerr-main",
    [string]$InputDir = "server/models",
    [string]$OutputDir = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel"
)

Write-Host "Converting TypeScript models to C# classes..." -ForegroundColor Green
Write-Host "Seerr Root Directory: $SeerrRootDir" -ForegroundColor Cyan
Write-Host "Input Directory: $InputDir" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir" -ForegroundColor Cyan

# Build full paths
$fullInputDir = Join-Path $SeerrRootDir $InputDir
$fullOutputDir = $OutputDir

# Ensure output directory exists
if (!(Test-Path $fullOutputDir)) {
    New-Item -ItemType Directory -Path $fullOutputDir -Force
    Write-Host "Created output directory: $fullOutputDir" -ForegroundColor Yellow
}

# Store all parsed interfaces for dependency resolution
$global:allInterfaces = @{}

# Function to scan all TypeScript files and build interface map
function Build-InterfaceMap {
    param([string]$rootDir)
    
    Write-Host "Scanning TypeScript files for interfaces..." -ForegroundColor Yellow
    
    # Get all TypeScript files in the seerr directory
    $tsFiles = Get-ChildItem -Path $rootDir -Filter "*.ts" -Recurse
    
    foreach ($file in $tsFiles) {
        $content = Get-Content -Path $file.FullName -Raw
        
        # Skip files that are just exports or mappings
        if ($content -match 'export\s+const\s+map' -and $content -notmatch 'interface\s+\w+') {
            continue
        }
        
        $interfaces = Convert-TypeScriptInterface $content
        
        foreach ($interface in $interfaces) {
            $global:allInterfaces[$interface.Name] = $interface
            Write-Host "  Found interface: $($interface.Name) in $($file.Name)" -ForegroundColor Gray
        }
    }
    
    Write-Host "Found $($global:allInterfaces.Count) interfaces total" -ForegroundColor Green
}

# Type mapping dictionary
$typeMappings = @{
    'string' = 'string'
    'boolean' = 'bool'
    'string | null' = 'string?'
    'number | null' = 'int?'
    'boolean | null' = 'bool?'
    'string[]' = 'List<string>'
    'number[]' = 'List<int>'
    'boolean[]' = 'List<bool>'
    'MediaType' = 'MediaType'  # Enum from constants/media.ts
}

# Function to convert TypeScript type to C# type
function Convert-Type {
    param([string]$tsType, [string]$propertyName = '')
    
    # Handle string literals (e.g., 'person', 'movie')
    if ($tsType -match "^'([^']+)'$") {
        return 'string'
    }
    
    # Handle nullable types
    if ($tsType -match '^(.+)\s*\|\s*null$') {
        $baseType = $Matches[1].Trim()
        $csharpType = Convert-Type $baseType $propertyName
        if ($csharpType -notmatch '\?$') {
            return $csharpType + '?'
        }
        return $csharpType
    }
    
    # Handle union types (convert to string for now)
    if ($tsType -match '\|') {
        return 'string'
    }
    
    # Handle arrays
    if ($tsType -match '^(.+)\[\]$') {
        $elementType = $Matches[1].Trim()
        $csharpElementType = Convert-Type $elementType $propertyName
        return "List<$csharpElementType>"
    }
    
    # Handle optional properties
    if ($tsType -match '^(.+)\?$') {
        $baseType = $Matches[1].Trim()
        $csharpType = Convert-Type $baseType $propertyName
        if ($csharpType -notmatch '\?$') {
            return $csharpType + '?'
        }
        return $csharpType
    }
    
    # Handle complex object types (simplify to string for now)
    if ($tsType -match '^\s*\{.*\}\s*$') {
        return 'string'
    }
    
    # Handle generic types
    if ($tsType -match '^(\w+)<.*>$') {
        $baseType = $Matches[1]
        switch ($baseType) {
            'Promise' { return 'string' }
            'List' { 
                $elementType = $tsType -replace '^List<(.+)>$', '$1'
                $csharpElementType = Convert-Type $elementType $propertyName
                return "List<$csharpElementType>"
            }
            default { return 'string' }
        }
    }
    
    # Handle external type mappings
    switch ($tsType) {
        'MediaStatus' { return 'MediaStatus' }
        'MediaRequestStatus' { return 'MediaRequestStatus' }
        'Promise' { return 'string' }
        'Date' { return 'DateTime' }
        'MediaRequest' { return 'string' }
        'Issue' { return 'string' }
        'TmdbTvRatingResult' { return 'string' }
        'TmdbMovieReleaseResult' { return 'string' }
        default {
            # Check direct mappings
            if ($typeMappings.ContainsKey($tsType)) {
                return $typeMappings[$tsType]
            }
            
            # Special handling for number types based on property name
            if ($tsType -eq 'number') {
                # Properties that should be double (decimal numbers)
                $doubleProperties = @('voteAverage', 'vote_average', 'popularity', 'rating', 'score', 'percentage', 'rate', 'ratio', 'average', 'mean')
                $propertyLower = $propertyName.ToLower()
                
                foreach ($doubleProp in $doubleProperties) {
                    if ($propertyLower -like "*$doubleProp*") {
                        return 'double'
                    }
                }
                
                # Default to int for other number properties
                return 'int'
            }
            
            # Check if it's a known interface from our scan (only add prefix to seerr-defined types)
            if ($global:allInterfaces.ContainsKey($tsType)) {
                return $tsType
            }
            
            # For external types (TMDB, etc.), keep original name
            if ($tsType -match '^Tmdb') {
                return $tsType
            }
            
            return $tsType
        }
    }
}

# Function to convert property name to C# naming convention
function Convert-PropertyName {
    param([string]$tsName)
    
    # Convert camelCase to PascalCase
    $csharpName = $tsName.Substring(0,1).ToUpper() + $tsName.Substring(1)
    
    # Handle special cases that need different naming
    switch ($csharpName) {
        'Iso_639_1' { return 'Iso6391' }
        'Iso_3166_1' { return 'Iso31661' }
        default { return $csharpName }
    }
}

# Function to generate C# class from TypeScript interface
function Convert-InterfaceToClass {
    param(
        [string]$interfaceName,
        [array]$properties,
        [string]$namespace = "Jellyfin.Plugin.JellyseerrBridge.Models",
        [string]$extendsInterface = ""
    )
    
    # Determine if we need additional using statements
    $needsSystemCollections = $false
    
    foreach ($property in $properties) {
        $csharpType = Convert-Type $property.Type $property.Name
        if ($csharpType -match '^List<') {
            $needsSystemCollections = $true
        }
    }
    
    $csharpClass = "using System.Text.Json.Serialization;`n"
    
    if ($needsSystemCollections) {
        $csharpClass += "using System.Collections.Generic;`n"
    }
    
    $csharpClass += "`nnamespace $namespace;`n`n"
    $csharpClass += "/// <summary>`n"
    $csharpClass += "/// Generated from TypeScript interface: $interfaceName`n"
    $csharpClass += "/// </summary>`n"
    
    # Handle inheritance
    if ($extendsInterface) {
        $csharpClass += "public class $interfaceName : $extendsInterface`n"
    } else {
        $csharpClass += "public class $interfaceName`n"
    }
    
    $csharpClass += "{`n"

    # If this class has a base class, filter out properties that exist in the base class
    $filteredProperties = $properties
    if ($extendsInterface) {
        $baseClassProperties = @()
        switch ($extendsInterface) {
            "SearchResult" {
                $baseClassProperties = @("MediaType", "MediaInfo")
            }
        }
        
        $filteredProperties = $properties | Where-Object { $_.Name -notin $baseClassProperties }
    }

    foreach ($property in $filteredProperties) {
        $csharpType = Convert-Type $property.Type $property.Name
        $csharpName = Convert-PropertyName $property.Name
        $jsonPropertyName = $property.Name
        
        # Handle optional properties and make string properties nullable by default
        if ($property.IsOptional -and $csharpType -notmatch '\?$') {
            $csharpType = $csharpType + '?'
        } elseif ($csharpType -eq 'string' -and -not $property.IsOptional) {
            # Make string properties nullable by default to avoid CS8618 warnings
            $csharpType = 'string?'
        } elseif ($property.Name -eq 'ExternalIds') {
            # Make ExternalIds nullable to avoid CS8618 warnings
            $csharpType = $csharpType + '?'
        }
        
        $csharpClass += "`n    [JsonPropertyName(`"$jsonPropertyName`")]`n"
        $csharpClass += "    public $csharpType $csharpName { get; set; }"
        
        # Add default value for nullable types
        if ($csharpType -match '\?$') {
            $csharpClass += " = null;"
        } elseif ($csharpType -match '^List<') {
            $csharpClass += " = new();"
        } elseif ($csharpType -eq 'bool') {
            $csharpClass += " = false;"
        } elseif ($csharpType -eq 'int') {
            $csharpClass += " = 0;"
        } elseif ($csharpType -eq 'double') {
            $csharpClass += " = 0.0;"
        } elseif ($csharpType -eq 'MediaType') {
            $csharpClass += " = MediaType.MOVIE;"
        } elseif ($csharpType -eq 'MediaStatus') {
            $csharpClass += " = MediaStatus.UNKNOWN;"
        } elseif ($csharpType -eq 'MediaRequestStatus') {
            $csharpClass += " = MediaRequestStatus.PENDING;"
        } elseif ($csharpType -eq 'IssueType') {
            $csharpClass += " = IssueType.OTHER;"
        } elseif ($csharpType -eq 'IssueStatus') {
            $csharpClass += " = IssueStatus.OPEN;"
        } elseif ($csharpType -eq 'MediaServerType') {
            $csharpClass += " = MediaServerType.NOT_CONFIGURED;"
        } elseif ($csharpType -eq 'ServerType') {
            $csharpClass += " = ServerType.JELLYFIN;"
        } elseif ($csharpType -eq 'UserType') {
            $csharpClass += " = UserType.LOCAL;"
        } elseif ($csharpType -eq 'ApiErrorCode') {
            $csharpClass += " = ApiErrorCode.UNKNOWN;"
        } elseif ($csharpType -eq 'DiscoverSliderType') {
            $csharpClass += " = DiscoverSliderType.RECENTLY_ADDED;"
        }
        $csharpClass += "`n"
    }
    
    $csharpClass += "`n}"
    
    return $csharpClass
}

# Function to parse TypeScript interface
function Convert-TypeScriptInterface {
    param([string]$content)
    
    $interfaces = @()
    
    # Find interface definitions - improved to handle nested braces and extends
    $interfacePattern = 'interface\s+(\w+)(?:\s+extends\s+(\w+))?\s*\{'
    $matched = [regex]::Matches($content, $interfacePattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $matched) {
        $interfaceName = $match.Groups[1].Value
        $extendsInterface = $match.Groups[2].Value
        $startPos = $match.Index + $match.Length
        
        # Find the matching closing brace
        $braceCount = 1
        $pos = $startPos
        while ($braceCount -gt 0 -and $pos -lt $content.Length) {
            if ($content[$pos] -eq '{') {
                $braceCount++
            } elseif ($content[$pos] -eq '}') {
                $braceCount--
            }
            $pos++
        }
        
        if ($braceCount -eq 0) {
            $interfaceBody = $content.Substring($startPos, $pos - $startPos - 1)
            # Remove the first line which contains "export interface Name {"
            $lines = $interfaceBody -split "`n"
            if ($lines.Count -gt 1) {
                $interfaceBody = ($lines[1..($lines.Count-1)] -join "`n")
            }
        } else {
            continue
        }
        
        $properties = @()
        
        # Parse properties - handle multi-line object types
        $lines = $interfaceBody -split "`n"
        $i = 0
        while ($i -lt $lines.Count) {
            $line = $lines[$i].Trim()
            if ($line -and $line -match '^(\w+)(\?)?\s*:\s*([^;]+);?$') {
                $propName = $Matches[1]
                $isOptional = $Matches.ContainsKey(2) -and $Matches[2] -eq '?'
                $propType = $Matches[3].Trim()
                
                # Check if this is an inline object type (starts with {)
                if ($propType -match '^\s*\{') {
                    # Skip this property and all lines until we find the closing }
                    $braceCount = 1
                    $j = $i + 1
                    while ($braceCount -gt 0 -and $j -lt $lines.Count) {
                        $nextLine = $lines[$j].Trim()
                        if ($nextLine -match '\{') { $braceCount++ }
                        if ($nextLine -match '\}') { $braceCount-- }
                        $j++
                    }
                    $i = $j - 1
                } else {
                    $properties += @{
                        Name = $propName
                        Type = $propType
                        IsOptional = $isOptional
                    }
                }
            }
            $i++
        }
        
        $interfaces += @{
            Name = $interfaceName
            Properties = $properties
            Extends = $extendsInterface
        }
    }
    
    return $interfaces
}

# Function to parse TypeScript enums from constants files
function Convert-TypeScriptEnums {
    param([string]$content)
    
    $enums = @()
    
    # Find enum definitions
    $enumPattern = 'export\s+enum\s+(\w+)\s*\{([^}]+)\}'
    $enumMatches = [regex]::Matches($content, $enumPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $enumMatches) {
        $enumName = $match.Groups[1].Value
        $enumBody = $match.Groups[2].Value.Trim()
        
        $members = @()
        $lines = $enumBody -split "`n"
        
        foreach ($line in $lines) {
            $line = $line.Trim()
            if ($line -and -not $line.StartsWith('//')) {
                # Handle different enum value formats
                if ($line -match '^(\w+)\s*=\s*([^,]+),?$') {
                    $memberName = $Matches[1]
                    $memberValue = $Matches[2].Trim()
                    
                    # Remove quotes if present
                    if ($memberValue -match "^'([^']+)'$") {
                        $memberValue = $Matches[1]
                    } elseif ($memberValue -match '^"([^"]+)"$') {
                        $memberValue = $Matches[1]
                    }
                    
                    $members += @{
                        Name = $memberName
                        Value = $memberValue
                    }
                } elseif ($line -match '^(\w+),?$') {
                    # Auto-incrementing enum values
                    $memberName = $Matches[1]
                    $members += @{
                        Name = $memberName
                        Value = $null  # Will be auto-incremented
                    }
                }
            }
        }
        
        $enums += @{
            Name = $enumName
            Members = $members
            SourceFile = $file.Name
        }
    }
    
    return $enums
}

# Function to convert TypeScript enums to C# enums
function Convert-EnumToCSharp {
    param(
        [array]$enums,
        [string]$namespace = "Jellyfin.Plugin.JellyseerrBridge.Models"
    )
    
    $csharpEnums = "using System.Text.Json.Serialization;`n`n"
    $csharpEnums += "namespace $namespace;`n`n"
    $csharpEnums += "/// <summary>`n"
    $csharpEnums += "/// Enums imported from TypeScript constants files`n"
    $csharpEnums += "/// </summary>`n`n"
    
    foreach ($enum in $enums) {
        $enumName = $enum.Name
        $members = $enum.Members
        
        # Determine if this enum needs JsonStringEnumConverter
        $hasStringValues = $false
        
        foreach ($member in $members) {
            if ($null -ne $member.Value -and $member.Value -match '^[a-zA-Z]') {
                $hasStringValues = $true
                break
            }
        }
        
        if ($hasStringValues) {
            $csharpEnums += "[JsonConverter(typeof(JsonStringEnumConverter))]`n"
        }
        
        $csharpEnums += "public enum $enumName`n"
        $csharpEnums += "{`n"
        
        $autoIncrementValue = 1
        foreach ($member in $members) {
            $memberName = $member.Name
            $memberValue = $member.Value
            
            # Add JsonPropertyName attribute for string values
            if ($null -ne $memberValue -and $memberValue -match '^[a-zA-Z]') {
                $csharpEnums += "    [JsonPropertyName(`"$memberValue`")]`n"
                $csharpEnums += "    $memberName = $autoIncrementValue,`n"
            } elseif ($null -ne $memberValue) {
                # Numeric value
                $csharpEnums += "    $memberName = $memberValue,`n"
            } else {
                # Auto-increment
                $csharpEnums += "    $memberName = $autoIncrementValue,`n"
            }
            
            $autoIncrementValue++
        }
        
        $csharpEnums += "}`n`n"
        
        # Add extension methods for string conversion
        $csharpEnums += "public static class ${enumName}Extensions`n"
        $csharpEnums += "{`n"
        $csharpEnums += "    public static string ToStringValue(this $enumName value)`n"
        $csharpEnums += "    {`n"
        $csharpEnums += "        return value.ToString().ToLowerInvariant();`n"
        $csharpEnums += "    }`n`n"
        $csharpEnums += "    public static $enumName FromString(string value)`n"
        $csharpEnums += "    {`n"
        $csharpEnums += "        if (Enum.TryParse<$enumName>(value, true, out var result))`n"
        $csharpEnums += "            return result;`n"
        
        # Find the first enum value as default
        $firstMember = $members[0]
        $csharpEnums += "        return $enumName.$($firstMember.Name);`n"
        $csharpEnums += "    }`n"
        $csharpEnums += "}`n`n"
    }
    
    return $csharpEnums
}

# Function to convert Media entity to C# class
function Convert-MediaEntity {
    param([string]$outputDir, [string]$seerrRootDir)
    
    Write-Host "Converting Media entity..." -ForegroundColor Cyan
    
    $mediaFile = Join-Path $seerrRootDir "server\entity\Media.ts"
    if (-not (Test-Path $mediaFile)) {
        Write-Host "  Media.ts file not found at: $mediaFile" -ForegroundColor Red
        return
    }
    
    $content = Get-Content -Path $mediaFile -Raw
    
    # Parse Media entity properties
    $properties = @()
    
    # Find all @Column decorated properties
    $columnPattern = '@Column[^}]*?public\s+(\w+)(\?)?\s*:\s*([^;]+);'
    $columnMatches = [regex]::Matches($content, $columnPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $columnMatches) {
        $propName = $match.Groups[1].Value
        $isOptional = $match.Groups[2].Value -eq '?'
        $propType = $match.Groups[3].Value.Trim()
        
        # Convert TypeScript type to C# type
        $csharpType = Convert-Type $propType $propName
        
        # Handle optional properties
        if ($isOptional -and $csharpType -notmatch '\?$') {
            $csharpType = $csharpType + '?'
        }
        
        $properties += @{
            Name = $propName
            Type = $csharpType
            IsOptional = $isOptional
        }
    }
    
    # Also find public properties without @Column decorators (like serviceUrl, mediaUrl, etc.)
    $publicPropPattern = 'public\s+(\w+)(\?)?\s*:\s*([^;=]+);'
    $publicMatches = [regex]::Matches($content, $publicPropPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($match in $publicMatches) {
        $propName = $match.Groups[1].Value
        $isOptional = $match.Groups[2].Value -eq '?'
        $propType = $match.Groups[3].Value.Trim()
        
        # Skip if already processed as @Column
        if ($properties | Where-Object { $_.Name -eq $propName }) {
            continue
        }
        
        # Convert TypeScript type to C# type
        $csharpType = Convert-Type $propType $propName
        
        # Handle optional properties
        if ($isOptional -and $csharpType -notmatch '\?$') {
            $csharpType = $csharpType + '?'
        }
        
        $properties += @{
            Name = $propName
            Type = $csharpType
            IsOptional = $isOptional
        }
    }
    
    # Generate C# class
    $csharpClass = Convert-InterfaceToClass "Media" $properties "Jellyfin.Plugin.JellyseerrBridge.Models" ""
    
    $outputFile = Join-Path $outputDir "Media.cs"
    Set-Content -Path $outputFile -Value $csharpClass -Encoding UTF8
    Write-Host "  Generated: Media.cs with $($properties.Count) properties" -ForegroundColor Green
}

# Build interface map first to resolve dependencies
Build-InterfaceMap $SeerrRootDir

# Process each TypeScript file
$tsFiles = Get-ChildItem -Path $fullInputDir -Filter "*.ts" -Recurse

foreach ($file in $tsFiles) {
    Write-Host "Processing: $($file.Name)" -ForegroundColor Yellow
    
    $content = Get-Content -Path $file.FullName -Raw
    
    # Skip files that are just exports or mappings
    if ($content -match 'export\s+const\s+map' -and $content -notmatch 'interface\s+\w+') {
        Write-Host "  Skipping mapping file: $($file.Name)" -ForegroundColor Gray
        continue
    }
    
    $interfaces = Convert-TypeScriptInterface $content
    
    foreach ($interface in $interfaces) {
        Write-Host "  Converting interface: $($interface.Name)" -ForegroundColor Cyan
        
        $csharpClass = Convert-InterfaceToClass $interface.Name $interface.Properties "Jellyfin.Plugin.JellyseerrBridge.Models" $interface.Extends
        $outputFile = Join-Path $fullOutputDir "$($interface.Name).cs"
        
        Set-Content -Path $outputFile -Value $csharpClass -Encoding UTF8
        Write-Host "    Generated: $($interface.Name).cs" -ForegroundColor Green
    }
}

# Function to extract enum references from model interfaces
function Get-UsedEnums {
    param([string]$inputDir)
    
    $usedEnums = @()
    
    # Get all TypeScript files in the models directory
    $tsFiles = Get-ChildItem -Path $inputDir -Filter "*.ts" -Recurse
    
    foreach ($file in $tsFiles) {
        $content = Get-Content -Path $file.FullName -Raw
        
        # Find all enum references in property types
        # Look for patterns like: propertyName: EnumName
        $enumPattern = ':\s*(\w+)(?:\s*\|\s*\w+)*;?$'
        $enumMatches = [regex]::Matches($content, $enumPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        foreach ($match in $enumMatches) {
            $enumName = $match.Groups[1].Value
            
            # Check if this looks like an enum (starts with capital letter and is not a basic type)
            $basicTypes = @('string', 'number', 'boolean', 'Date', 'Promise', 'List', 'Array')
            if ($enumName -match '^[A-Z]' -and $basicTypes -notcontains $enumName) {
                if ($usedEnums -notcontains $enumName) {
                    $usedEnums += $enumName
                }
            }
        }
    }
    
    # Also check the Media entity file for enum references
    $mediaEntityFile = Join-Path (Split-Path $inputDir -Parent) "entity\Media.ts"
    if (Test-Path $mediaEntityFile) {
        $content = Get-Content -Path $mediaEntityFile -Raw
        
        # Find enum references in Media entity
        $enumPattern = ':\s*(\w+)(?:\s*\|\s*\w+)*;?$'
        $enumMatches = [regex]::Matches($content, $enumPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        foreach ($match in $enumMatches) {
            $enumName = $match.Groups[1].Value
            
            # Check if this looks like an enum
            $basicTypes = @('string', 'number', 'boolean', 'Date', 'Promise', 'List', 'Array')
            if ($enumName -match '^[A-Z]' -and $basicTypes -notcontains $enumName) {
                if ($usedEnums -notcontains $enumName) {
                    $usedEnums += $enumName
                }
            }
        }
    }
    
    return $usedEnums
}

# Function to process constants files and generate enums
function Convert-ConstantsFiles {
    param([string]$outputDir, [string]$seerrRootDir, [string]$inputDir)
    
    Write-Host "Processing constants files for enums..." -ForegroundColor Cyan
    
    $constantsDir = Join-Path $seerrRootDir "server\constants"
    if (-not (Test-Path $constantsDir)) {
        Write-Host "  Constants directory not found at: $constantsDir" -ForegroundColor Red
        return
    }
    
    # Automatically detect which enums are used in the models
    $usedEnums = Get-UsedEnums $inputDir
    Write-Host "  Detected used enums: $($usedEnums -join ', ')" -ForegroundColor Yellow
    
    if ($usedEnums.Count -eq 0) {
        Write-Host "  No enums found in model interfaces" -ForegroundColor Yellow
        return
    }
    
    $allEnums = @()
    
    # Process all TypeScript files in constants directory to find the enums we need
    $constantsFiles = Get-ChildItem -Path $constantsDir -Filter "*.ts" -Recurse
    
    foreach ($file in $constantsFiles) {
        $content = Get-Content -Path $file.FullName -Raw
        $enums = Convert-TypeScriptEnums $content
        
        # Filter to only include enums that are actually used and add source file info
        $filteredEnums = $enums | Where-Object { $usedEnums -contains $_.Name }
        
        if ($filteredEnums.Count -gt 0) {
            # Add source file information to each enum
            foreach ($enum in $filteredEnums) {
                $enum.SourceFile = $file.Name
            }
            $allEnums += $filteredEnums
            Write-Host "    Found $($filteredEnums.Count) used enums in $($file.Name): $($filteredEnums.Name -join ', ')" -ForegroundColor Green
        }
    }
    
    if ($allEnums.Count -gt 0) {
        # Group enums by their source file
        $enumsByFile = @{}
        foreach ($enum in $allEnums) {
            $sourceFile = $enum.SourceFile
            if (-not $enumsByFile.ContainsKey($sourceFile)) {
                $enumsByFile[$sourceFile] = @()
            }
            $enumsByFile[$sourceFile] += $enum
        }
        
        # Generate separate C# enum files for each source file
        foreach ($sourceFile in $enumsByFile.Keys) {
            $fileEnums = $enumsByFile[$sourceFile]
            $csharpEnums = Convert-EnumToCSharp $fileEnums
            
            # Create filename based on source file (e.g., media.ts -> MediaEnums.cs)
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($sourceFile)
            $enumFileName = $baseName.Substring(0,1).ToUpper() + $baseName.Substring(1) + "Enums.cs"
            $outputFile = Join-Path $outputDir $enumFileName
            
            Set-Content -Path $outputFile -Value $csharpEnums -Encoding UTF8
            Write-Host "  Generated: $enumFileName with $($fileEnums.Count) enums from $sourceFile" -ForegroundColor Green
        }
    } else {
        Write-Host "  No used enums found in constants files" -ForegroundColor Yellow
    }
}

# Convert constants files to generate enums
Convert-ConstantsFiles $fullOutputDir $SeerrRootDir $fullInputDir

# Convert Media entity separately
Convert-MediaEntity $fullOutputDir $SeerrRootDir

Write-Host "`nConversion completed!" -ForegroundColor Green
Write-Host "Generated C# classes in: $fullOutputDir" -ForegroundColor Cyan
