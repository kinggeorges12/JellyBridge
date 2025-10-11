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
    'MediaType' = 'string'  # Union type: 'tv' | 'movie' | 'person' | 'collection'
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
        'MediaStatus' { return 'int' }
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

# Convert Media entity separately
Convert-MediaEntity $fullOutputDir $SeerrRootDir

Write-Host "`nConversion completed!" -ForegroundColor Green
Write-Host "Generated C# classes in: $fullOutputDir" -ForegroundColor Cyan
