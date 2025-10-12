# TypeScript to C# Model Converter with Flattened Structure
# This script uses TypeScript Compiler API for interface-to-class conversions

param(
    [string]$SeerrRootDir = "codebase/seerr-main",
    [string]$OutputDir = "src/Jellyfin.Plugin.JellyseerrBridge/JellyseerrModel"
)

Write-Host "=== TypeScript to C# Conversion with Flattened Structure ===" -ForegroundColor Green
Write-Host "Seerr Root Directory: $SeerrRootDir" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir" -ForegroundColor Cyan

# Define input/output directory pairs
$directoryPairs = @(
    @{
        input = "$SeerrRootDir/server/models"
        output = "$OutputDir/Server"
        type = "Server"
    },
    @{
        input = "$SeerrRootDir/server/interfaces/api"
        output = "$OutputDir/Api"
        type = "Api"
    },
    @{
        input = "$SeerrRootDir/server/api/servarr"
        output = "$OutputDir/Common"
        type = "Servarr"
    },
    @{
        input = "$SeerrRootDir/server/constants"
        output = "$OutputDir/Common"
        type = "Enums"
    }
)

Write-Host "`nDirectory Pairs:" -ForegroundColor Yellow
foreach ($pair in $directoryPairs) {
    Write-Host "  $($pair.type): $($pair.input) -> $($pair.output)" -ForegroundColor Cyan
}

# Check if Node.js is available
try {
    $nodeVersion = node --version 2>$null
    Write-Host "Node.js version: $nodeVersion" -ForegroundColor Yellow
} catch {
    Write-Host "Node.js not found. Please install Node.js to use this converter." -ForegroundColor Red
    exit 1
}

# Function to generate the TypeScript compiler JavaScript file
function New-TypeScriptCompilerScript {
    $scriptPath = Join-Path $scriptDir "convert-with-typescript-compiler.js"
    
    $jsContent = @"
const fs = require('fs');
const path = require('path');
const ts = require('typescript');

// Read directory pairs from stdin (passed from PowerShell)
let directoryPairs = [];

try {
    const input = fs.readFileSync(0, 'utf8').trim();
    directoryPairs = JSON.parse(input);
    console.log('Received directory pairs from PowerShell:');
    directoryPairs.forEach(pair => {
        console.log('  ' + pair.type + ': ' + pair.input + ' -> ' + pair.output);
    });
} catch (error) {
    console.error('Error reading input from PowerShell:', error.message);
    process.exit(1);
}

// Function to find TypeScript files recursively
function findTypeScriptFiles(dir) {
    const files = [];
    
    if (!fs.existsSync(dir)) {
        return files;
    }
    
    const items = fs.readdirSync(dir);
    
    for (const item of items) {
        const fullPath = path.join(dir, item);
        const stat = fs.statSync(fullPath);
        
        if (stat.isDirectory()) {
            files.push(...findTypeScriptFiles(fullPath));
        } else if (item.endsWith('.ts')) {
            files.push(fullPath);
        }
    }
    
    return files;
}

// Function to convert camelCase to PascalCase
function toPascalCase(str) {
    // Handle special characters that are invalid in C# property names
    let cleanStr = str;
    
    // Remove quotes if present
    cleanStr = cleanStr.replace(/^['"]|['"]$/g, '');
    
    // Replace forward slashes with underscores (for camelCase conversion)
    cleanStr = cleanStr.replace(/\//g, '_');
    
    // Replace other invalid characters with underscores
    cleanStr = cleanStr.replace(/[^a-zA-Z0-9_]/g, '_');
    
    // Remove multiple consecutive underscores
    cleanStr = cleanStr.replace(/_+/g, '_');
    
    // Remove leading/trailing underscores
    cleanStr = cleanStr.replace(/^_+|_+$/g, '');
    
    // Convert camelCase to PascalCase
    // First split on underscores, then handle camelCase within each part
    const underscoreParts = cleanStr.split('_');
    const pascalParts = underscoreParts.map(part => {
        if (part.length === 0) return '';
        
        // Handle camelCase within this part
        // Split camelCase by finding uppercase letters
        const camelParts = [];
        let currentPart = '';
        
        for (let i = 0; i < part.length; i++) {
            const char = part[i];
            if (i > 0 && char >= 'A' && char <= 'Z') {
                // Found uppercase letter, start new part
                if (currentPart.length > 0) {
                    camelParts.push(currentPart);
                    currentPart = char;
                } else {
                    currentPart += char;
                }
            } else {
                currentPart += char;
            }
        }
        
        if (currentPart.length > 0) {
            camelParts.push(currentPart);
        }
        
        // Capitalize each camelCase part
        return camelParts.map(camelPart => {
            if (camelPart.length === 0) return '';
            return camelPart.charAt(0).toUpperCase() + camelPart.slice(1).toLowerCase();
        }).join('');
    });
    
    return pascalParts.join('');
}

// Function to convert property names for JSON attributes (replace slashes with underscores)
function toJsonPropertyName(str) {
    let cleanStr = str;
    
    // Remove quotes if present
    cleanStr = cleanStr.replace(/^['"]|['"]$/g, '');
    
    // Replace forward slashes with underscores
    cleanStr = cleanStr.replace(/\//g, '_');
    
    return cleanStr;
}

        // Function to find source file for a missing class/type
        function findSourceFileForType(typeName, searchDirs) {
            for (const searchDir of searchDirs) {
                if (!fs.existsSync(searchDir)) continue;
                
                const files = findTypeScriptFiles(searchDir);
                for (const file of files) {
                    try {
                        const sourceCode = fs.readFileSync(file, 'utf8');
                        const sourceFile = ts.createSourceFile(file, sourceCode, ts.ScriptTarget.Latest, true);
                        
                        // Check if this file contains the type we're looking for
                        let found = false;
                        function visit(node) {
                            if (node.kind === ts.SyntaxKind.InterfaceDeclaration || 
                                node.kind === ts.SyntaxKind.TypeAliasDeclaration ||
                                node.kind === ts.SyntaxKind.ClassDeclaration) {
                                if (node.name && node.name.text === typeName) {
                                    found = true;
                                }
                            }
                            ts.forEachChild(node, visit);
                        }
                        visit(sourceFile);
                        
                        if (found) {
                            return file;
                        }
                    } catch (error) {
                        // Skip files that can't be parsed
                        continue;
                    }
                }
            }
            return null;
        }

        // Global set to track all transpiled types
        const transpiledTypes = new Set();
        
        // Global set to track converted file paths to avoid duplicates
        const convertedFilePaths = new Set();
        
        // Function to check if a class is already generated
        function isClassGenerated(className) {
            return transpiledTypes.has(className);
        }

        // Function to extract all class names from a C# file
        function extractClassNamesFromFile(filePath) {
            try {
                const content = fs.readFileSync(filePath, 'utf8');
                const classPattern = /public\s+class\s+(\w+)/g;
                let match;
                const classNames = [];
                while ((match = classPattern.exec(content)) !== null) {
                    classNames.push(match[1]);
                }
                return classNames;
            } catch (error) {
                return [];
            }
        }

        // Function to build complete list of transpiled types
        function buildTranspiledTypesList(outputDirs) {
            // Don't clear transpiledTypes - it already contains enums generated during file processing
            
            for (const outputDir of outputDirs) {
                if (!fs.existsSync(outputDir)) continue;
                
                const files = fs.readdirSync(outputDir);
                for (const file of files) {
                    if (file.endsWith('.cs')) {
                        const filePath = path.join(outputDir, file);
                        const classNames = extractClassNamesFromFile(filePath);
                        classNames.forEach(className => {
                            transpiledTypes.add(className);
                        });
                    }
                }
            }
            
            console.log('Built transpiled types list: ' + transpiledTypes.size + ' types');
            console.log('Transpiled types: ' + Array.from(transpiledTypes).sort().join(', '));
        }

        // Function to check if a type already exists in transpiled types list
        function typeAlreadyExists(typeName) {
            return transpiledTypes.has(typeName);
        }
        
        // Function to register an enum/static class globally to prevent duplicates
        function registerGlobalEnum(enumName, enumContent) {
            if (!globalEnumRegistry.has(enumName)) {
                globalEnumRegistry.set(enumName, enumContent);
                centralizedEnumsContent += enumContent + '\n\n';
                console.log('Registered global enum: ' + enumName);
            }
        }
        


        // Function to convert TypeScript file to C# using TypeScript compiler API
        function convertFile(tsFilePath, outputDir, modelType, missingTypes = new Set()) {
            try {
                // Check if this file has already been converted (regardless of output directory)
                if (convertedFilePaths.has(tsFilePath)) {
                    console.log('File already converted, skipping: ' + tsFilePath);
                    return;
                }
                
                console.log('Converting ' + modelType + ': ' + tsFilePath);
                
                // Mark this file as converted
                convertedFilePaths.add(tsFilePath);
                
                // Ensure output directory exists
                if (!fs.existsSync(outputDir)) {
                    fs.mkdirSync(outputDir, { recursive: true });
                }
                
                const sourceCode = fs.readFileSync(tsFilePath, 'utf8');
                const sourceFile = ts.createSourceFile(tsFilePath, sourceCode, ts.ScriptTarget.Latest, true);
                
                let hasInterfaces = false;
                let csharpCode = '';
                
                // Function to generate union type enums from interface properties
                function generateUnionTypeEnums(interfaceNode, sourceFile) {
                    const interfaceName = interfaceNode.name.text;
                    
                    interfaceNode.members.forEach(member => {
                        if (member.kind === ts.SyntaxKind.PropertySignature) {
                            const propertyName = member.name.text;
                            const propertyType = member.type;
                            
                            // Check if this is a union type with string literals
                            if (propertyType.kind === ts.SyntaxKind.UnionType) {
                                const unionTypes = propertyType.types;
                                const nonNullTypes = unionTypes.filter(t => t.kind !== ts.SyntaxKind.NullKeyword && t.kind !== ts.SyntaxKind.UndefinedKeyword);
                                
                                if (nonNullTypes.length >= 2 && 
                                    nonNullTypes.every(t => t.kind === ts.SyntaxKind.LiteralType && t.literal.kind === ts.SyntaxKind.StringLiteral)) {
                                    
                                    // Create enum name based on property name only
                                    // Special case: if property name is "Type", use ClassNameType to avoid C# reserved keyword conflict
                                    const enumName = propertyName === 'type' ? toPascalCase(interfaceName) + 'Type' : toPascalCase(propertyName);
                                    
                                    // Generate enum locally if not already generated
                                    if (!isClassGenerated(enumName)) {
                                        // Get all string literal values
                                        const enumValues = [];
                                        for (let i = 0; i < nonNullTypes.length; i++) {
                                            if (nonNullTypes[i].kind === ts.SyntaxKind.LiteralType && 
                                                nonNullTypes[i].literal.kind === ts.SyntaxKind.StringLiteral) {
                                                enumValues.push(nonNullTypes[i].literal.text);
                                            }
                                        }
                                        
                                        const enumClass = 'public static class ' + enumName + '\n{\n' +
                                            enumValues.map(value => '    public const string ' + toPascalCase(value) + ' = "' + value + '";').join('\n') + '\n}';
                                        
                                        csharpCode += enumClass + '\n\n';
                                        transpiledTypes.add(enumName);
                                        console.log('Generated ' + enumName + ' locally');
                                    }
                                }
                            }
                        }
                    });
                }
                
                function visit(node) {
                    if (node.kind === ts.SyntaxKind.InterfaceDeclaration) {
                        hasInterfaces = true;
                        csharpCode += convertInterfaceToClass(node, sourceFile, missingTypes) + '\n\n';
                        
                        // Check for union type enums that need to be generated
                        generateUnionTypeEnums(node, sourceFile);
                    } else if (node.kind === ts.SyntaxKind.EnumDeclaration) {
                        hasInterfaces = true;
                        // Generate enum in this file if not already generated elsewhere
                        const enumName = node.name.text;
                        if (!isClassGenerated(enumName)) {
                            csharpCode += convertEnumToCSharp(node, sourceFile) + '\n\n';
                            transpiledTypes.add(enumName);
                            console.log('Generated enum locally: ' + enumName);
                        } else {
                            console.log('Skipping enum declaration: ' + enumName + ' - already exists in another file');
                        }
                    } else if (node.kind === ts.SyntaxKind.TypeAliasDeclaration) {
                        hasInterfaces = true;
                        csharpCode += convertTypeAliasToClass(node, sourceFile, missingTypes) + '\n\n';
                    }
                    ts.forEachChild(node, visit);
                }
                
                visit(sourceFile);
                
                if (!hasInterfaces) {
                    console.log('No interfaces found in ' + tsFilePath);
                    return;
                }
                
                // Generate output file path - just use the filename without subdirectories
                const fileName = path.basename(tsFilePath).replace('.ts', '.cs');
                const outputPath = path.join(outputDir, fileName);
                
                // Add namespace and using statements
                let fullCsharpCode = 'using System;\n' +
                                     'using System.Text.Json.Serialization;\n' +
                                     'using System.Collections.Generic;\n\n' +
                                     'namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;\n\n' +
                                     csharpCode;
                
                // Add anonymous classes if any were generated for this file
                const fileKey = sourceFile.fileName;
                if (global.anonymousClasses && global.anonymousClasses[fileKey] && global.anonymousClasses[fileKey].length > 0) {
                    fullCsharpCode += '\n\n' + global.anonymousClasses[fileKey].join('\n\n');
                    // Clear the file's anonymous classes after using them
                    global.anonymousClasses[fileKey] = [];
                }
                
                fs.writeFileSync(outputPath, fullCsharpCode);
                console.log('Generated: ' + outputPath);
                
            } catch (error) {
                console.error('Error converting ' + tsFilePath + ':', error);
            }
        }

// Function to convert TypeScript interface to C# class
function convertInterfaceToClass(interfaceNode, sourceFile, missingTypes = new Set()) {
    const interfaceName = interfaceNode.name.text;
    const members = interfaceNode.members;
    
    // Check for inheritance
    let inheritance = '';
    if (interfaceNode.heritageClauses && interfaceNode.heritageClauses.length > 0) {
        const extendsClause = interfaceNode.heritageClauses.find(clause => clause.token === ts.SyntaxKind.ExtendsKeyword);
        if (extendsClause && extendsClause.types.length > 0) {
            const baseType = extendsClause.types[0].expression.text;
            inheritance = ' : ' + toPascalCase(baseType);
        }
    }
    
    let csharpClass = 'public class ' + interfaceName + inheritance + '\n{\n';
    
    members.forEach(member => {
        if (member.kind === ts.SyntaxKind.PropertySignature) {
            const propName = member.name.text;
            const propType = convertTypeScriptTypeToCSharp(member.type, sourceFile, missingTypes, propName, interfaceName);
            const isOptional = member.questionToken ? '?' : '';
            
            // Special debugging for AddSeriesOptions
            if (interfaceName === 'AddSeriesOptions' && propName === 'seriesType') {
                console.log('DEBUG: AddSeriesOptions.seriesType - member.type.kind:', ts.SyntaxKind[member.type.kind]);
                console.log('DEBUG: AddSeriesOptions.seriesType - member.type:', member.type);
            }
            
            csharpClass += '    [JsonPropertyName("' + toJsonPropertyName(propName) + '")]\n';
            csharpClass += '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }\n\n';
        }
    });
    
    csharpClass += '}';
    return csharpClass;
}

// Function to create intersection class
function createIntersectionClass(className, intersectionTypes, sourceFile, missingTypes = new Set()) {
    let csharpClass = 'public class ' + className + '\n{\n';
    
    intersectionTypes.forEach(typeNode => {
        if (typeNode.kind === ts.SyntaxKind.TypeLiteral) {
            // Handle object literal types
            if (typeNode.members) {
                typeNode.members.forEach(member => {
                    if (member.kind === ts.SyntaxKind.PropertySignature) {
                        const propName = member.name.text;
                        const propType = convertTypeScriptTypeToCSharp(member.type, sourceFile, missingTypes, propName, className);
                        const isOptional = member.questionToken ? '?' : '';
                        
                        csharpClass += '    [JsonPropertyName("' + toJsonPropertyName(propName) + '")]\n';
                        csharpClass += '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }\n\n';
                    }
                });
            }
        } else if (typeNode.kind === ts.SyntaxKind.TypeReference) {
            // Handle type references - for now, we'll add a comment indicating the base type
            const typeName = typeNode.typeName.text;
            csharpClass += '    // Base type: ' + typeName + '\n';
        }
    });
    
    csharpClass += '}';
    return csharpClass;
}

// Function to convert TypeScript type alias to C# class
function convertTypeAliasToClass(typeAliasNode, sourceFile, missingTypes = new Set()) {
    const typeName = typeAliasNode.name.text;
    const typeNode = typeAliasNode.type;
    
    // Check for naming conflicts with known enums/types
    const conflictingTypes = ['MediaStatus', 'MediaRequestStatus', 'ServerType', 'MediaServerType', 'ApiErrorCode'];
    let finalTypeName = typeName;
    
    if (conflictingTypes.includes(typeName)) {
        // Keep original name for conflicting types
        finalTypeName = typeName;
        console.log('Using original name for conflicting type alias: ' + typeName);
    }
    
    // Handle generic utility types - convert to object for now
    if (typeAliasNode.typeParameters && typeAliasNode.typeParameters.length > 0) {
        console.log('Converting generic utility type to object: ' + typeName);
        return '// Generic utility type: ' + typeName + '<' + typeAliasNode.typeParameters.map(p => p.name.text).join(', ') + '>\n' +
               'public class ' + finalTypeName + '\n{\n' +
               '    // This is a TypeScript utility type that needs manual conversion\n' +
               '    public object Value { get; set; }\n' +
               '}\n';
    }
    
    // Handle object type literals - create a proper C# class
    if (typeNode.kind === ts.SyntaxKind.TypeLiteral) {
        let csharpClass = 'public class ' + finalTypeName + '\n{\n';
        
        typeNode.members.forEach(member => {
            if (member.kind === ts.SyntaxKind.PropertySignature) {
                const propName = member.name.text;
                const propType = convertTypeScriptTypeToCSharp(member.type, sourceFile, missingTypes, propName, finalTypeName);
                const isOptional = member.questionToken ? '?' : '';
                
                csharpClass += '    [JsonPropertyName("' + toJsonPropertyName(propName) + '")]\n';
                csharpClass += '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }\n\n';
            }
        });
        
        csharpClass += '}';
        return csharpClass;
    }
    
    // Handle other type aliases (like union types, etc.)
    if (typeNode.kind === ts.SyntaxKind.UnionType) {
        // Handle union type aliases - create a static class with constants
        const unionTypes = typeNode.types;
        const nonNullTypes = unionTypes.filter(t => t.kind !== ts.SyntaxKind.NullKeyword && t.kind !== ts.SyntaxKind.UndefinedKeyword);
        
        if (nonNullTypes.length >= 2 && 
            nonNullTypes.every(t => t.kind === ts.SyntaxKind.LiteralType && t.literal.kind === ts.SyntaxKind.StringLiteral)) {
            
            // Get all string literal values
            const enumValues = [];
            for (let i = 0; i < nonNullTypes.length; i++) {
                if (nonNullTypes[i].kind === ts.SyntaxKind.LiteralType && 
                    nonNullTypes[i].literal.kind === ts.SyntaxKind.StringLiteral) {
                    enumValues.push(nonNullTypes[i].literal.text);
                }
            }
            
            // Generate enum locally if not already generated
            if (!isClassGenerated(finalTypeName)) {
                const enumClass = 'public static class ' + finalTypeName + '\n{\n' +
                    enumValues.map(value => '    public const string ' + toPascalCase(value) + ' = "' + value + '";').join('\n') + '\n}';
                transpiledTypes.add(finalTypeName);
                // Also register the original name so TypeReference lookups work
                if (finalTypeName !== typeName) {
                    transpiledTypes.add(typeName);
                }
                console.log('Generated ' + finalTypeName + ' locally');
                return enumClass;  // Return the static class directly
            }
            
            // If already generated, return empty string (class already exists)
            return '';
        }
    }
    
    
    const convertedType = convertTypeScriptTypeToCSharp(typeNode, sourceFile, missingTypes, '', finalTypeName);
    return 'public class ' + finalTypeName + '\n{\n    public ' + convertedType + ' Value { get; set; }\n}';
}

// Function to convert TypeScript enum to C# enum
function convertEnumToCSharp(enumNode, sourceFile) {
    const enumName = enumNode.name.text;
    const members = enumNode.members;
    
    // Check if this is a string enum (has string literal initializers)
    const hasStringValues = members.some(member => 
        member.initializer && member.initializer.kind === ts.SyntaxKind.StringLiteral
    );
    
    if (hasStringValues) {
        // Convert string enum to C# static class with constants
        let csharpClass = 'public static class ' + enumName + '\n{\n';
        
        members.forEach(member => {
            const memberName = member.name.text;
            let memberValue = '';
            
            if (member.initializer && member.initializer.kind === ts.SyntaxKind.StringLiteral) {
                memberValue = '"' + member.initializer.text + '"';
            } else {
                memberValue = 'null'; // Fallback for missing string values
            }
            
            csharpClass += '    public const string ' + memberName + ' = ' + memberValue + ';\n';
        });
        
        csharpClass += '}';
        return csharpClass;
    } else {
        // Convert numeric enum to C# enum
        let csharpEnum = 'public enum ' + enumName + '\n{\n';
        
        members.forEach((member, index) => {
            const memberName = member.name.text;
            let memberValue = '';
            
            if (member.initializer) {
                if (member.initializer.kind === ts.SyntaxKind.StringLiteral) {
                    memberValue = ' = "' + member.initializer.text + '"';
                } else if (member.initializer.kind === ts.SyntaxKind.NumericLiteral) {
                    memberValue = ' = ' + member.initializer.text;
                }
            } else if (index === 0) {
                memberValue = ' = 1'; // Start C# enums at 1 by default
            }
            
            csharpEnum += '    ' + memberName + memberValue;
            if (index < members.length - 1) {
                csharpEnum += ',';
            }
            csharpEnum += '\n';
        });
        
        csharpEnum += '}';
        return csharpEnum;
    }
}

// Function to create anonymous object class
function createAnonymousObjectClass(className, typeNode, sourceFile, missingTypes) {
    let csharpClass = 'public class ' + className + '\n{\n';
    
    if (typeNode.members) {
        typeNode.members.forEach(member => {
            if (member.kind === ts.SyntaxKind.PropertySignature) {
                const propName = member.name.text;
                const propType = convertTypeScriptTypeToCSharp(member.type, sourceFile, missingTypes, propName, className);
                const isOptional = member.questionToken ? '?' : '';
                
                csharpClass += '    [JsonPropertyName("' + toJsonPropertyName(propName) + '")]\n';
                csharpClass += '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }\n\n';
            }
        });
    }
    
    csharpClass += '}';
    return csharpClass;
}

// Function to convert TypeScript type to C# type
function convertTypeScriptTypeToCSharp(typeNode, sourceFile, missingTypes = new Set(), propertyName = '', parentClassName = '') {
    if (!typeNode) return 'object';
    
    switch (typeNode.kind) {
        case ts.SyntaxKind.StringKeyword:
            return 'string';
        case ts.SyntaxKind.NumberKeyword:
            return 'int';
        case ts.SyntaxKind.BooleanKeyword:
            return 'bool';
        case ts.SyntaxKind.ArrayType:
            const elementType = convertTypeScriptTypeToCSharp(typeNode.elementType, sourceFile, missingTypes, propertyName, parentClassName);
            // Check if this is a specific pattern we want to handle specially
            if (elementType === 'object' && propertyName === 'results' && parentClassName === 'RequestResultsResponse') {
                // Create a specific class for this case
                const specificClassName = 'RequestResult';
                const specificClass = 'public class ' + specificClassName + '\n{\n' +
                    '    [JsonPropertyName("profileName")]\n' +
                    '    public string? ProfileName { get; set; }\n\n' +
                    '    [JsonPropertyName("canRemove")]\n' +
                    '    public bool? CanRemove { get; set; }\n\n' +
                    '    // Additional MediaRequest properties would go here\n' +
                    '}\n';
                
                // Store the class definition to be written later
                const fileKey = sourceFile.fileName;
                if (!global.anonymousClasses) global.anonymousClasses = {};
                if (!global.anonymousClasses[fileKey]) global.anonymousClasses[fileKey] = [];
                global.anonymousClasses[fileKey].push(specificClass);
                
                return 'List<' + specificClassName + '>';
            }
            // Check if this is QueueResponse records property
            if (elementType === 'object' && propertyName === 'records' && parentClassName === 'QueueResponse') {
                return 'List<QueueItem>';
            }
            // Check if this is PersonResult knownFor property
            if (elementType === 'object' && propertyName === 'knownFor' && parentClassName === 'PersonResult') {
                return 'List<SearchResult>';
            }
            return 'List<' + elementType + '>';
        case ts.SyntaxKind.IndexedAccessType:
            // Handle indexed access types like SonarrSeries['seriesType']
            const objectType = typeNode.objectType;
            const indexType = typeNode.indexType;
            
            if (objectType && indexType && 
                objectType.kind === ts.SyntaxKind.TypeReference && 
                indexType.kind === ts.SyntaxKind.LiteralType && 
                indexType.literal.kind === ts.SyntaxKind.StringLiteral) {
                
                const baseTypeName = objectType.typeName.text;
                const propertyName = indexType.literal.text;
                
                console.log('DEBUG: Found indexed access type:', baseTypeName + '[\'' + propertyName + '\']');
                console.log('DEBUG: baseTypeName:', baseTypeName, 'propertyName:', propertyName, 'parentClassName:', parentClassName);
                
                // For indexed access types, return just the property name as a type
                // Special case: if property name is "type", use ClassNameType to avoid C# reserved keyword conflict
                return propertyName === 'type' ? toPascalCase(parentClassName) + 'Type' : toPascalCase(propertyName);
            }
            return 'object';
        case ts.SyntaxKind.TypeReference:
            const typeName = typeNode.typeName.text;
            
            console.log('DEBUG: TypeReference - typeName:', typeName, 'propertyName:', propertyName, 'parentClassName:', parentClassName);
            
            // Special debugging for AddSeriesOptions
            if (parentClassName === 'AddSeriesOptions') {
                console.log('DEBUG: AddSeriesOptions property:', propertyName, 'typeName:', typeName, 'typeNode.kind:', ts.SyntaxKind[typeNode.kind]);
            }
            
            // Handle indexed access types like SonarrSeries['seriesType']
            if (typeNode.typeName.kind === ts.SyntaxKind.IndexedAccessType) {
                const objectType = typeNode.typeName.objectType;
                const indexType = typeNode.typeName.indexType;
                
                if (objectType && indexType && 
                    objectType.kind === ts.SyntaxKind.TypeReference && 
                    indexType.kind === ts.SyntaxKind.LiteralType && 
                    indexType.literal.kind === ts.SyntaxKind.StringLiteral) {
                    
                    const baseTypeName = objectType.typeName.text;
                    const propertyName = indexType.literal.text;
                    
                    console.log('DEBUG: Found indexed access type:', baseTypeName + '[\'' + propertyName + '\']');
                    
                    // For indexed access types, return the parent class + property name as a type
                    // This will be handled by the union type logic if needed
                    return toPascalCase(baseTypeName) + toPascalCase(propertyName);
                }
            }
            
            // Handle special TypeScript types
            if (typeName === 'Date') {
                return 'DateTimeOffset';
            } else if (typeName === 'Record') {
                // Handle Record<K, V> -> Dictionary<K, V>
                if (typeNode.typeArguments && typeNode.typeArguments.length === 2) {
                    const keyType = convertTypeScriptTypeToCSharp(typeNode.typeArguments[0], sourceFile, missingTypes, propertyName, parentClassName);
                    const valueType = convertTypeScriptTypeToCSharp(typeNode.typeArguments[1], sourceFile, missingTypes, propertyName, parentClassName);
                    return 'Dictionary<' + keyType + ', ' + valueType + '>';
                } else {
                    // Fallback for Record without type arguments
                    return 'Dictionary<string, object>';
                }
            } else if (typeName === 'Pick' || typeName === 'Partial' || typeName === 'Omit' || typeName === 'Exclude' || typeName === 'Extract' || typeName === 'NonFunctionProperties' || typeName === 'NonFunctionPropertyNames') {
                // Keep TypeScript utility type names in output for visibility
                // For generic utility types, convert to the base type instead of object
                if (typeNode.typeArguments && typeNode.typeArguments.length > 0) {
                    const baseType = convertTypeScriptTypeToCSharp(typeNode.typeArguments[0], sourceFile, missingTypes, propertyName, parentClassName);
                    // For NonFunctionProperties, create a more specific type
                    if (typeName === 'NonFunctionProperties' && baseType === 'object') {
                        // Create a specific class for NonFunctionProperties<MediaRequest>
                        const specificClassName = parentClassName + toPascalCase(propertyName) + 'Request';
                        const specificClass = 'public class ' + specificClassName + '\n{\n' +
                            '    [JsonPropertyName("profileName")]\n' +
                            '    public string? ProfileName { get; set; }\n\n' +
                            '    [JsonPropertyName("canRemove")]\n' +
                            '    public bool? CanRemove { get; set; }\n\n' +
                            '    // Additional MediaRequest properties would go here\n' +
                            '}\n';
                        
                        // Store the class definition to be written later
                        const fileKey = sourceFile.fileName;
                        if (!global.anonymousClasses) global.anonymousClasses = {};
                        if (!global.anonymousClasses[fileKey]) global.anonymousClasses[fileKey] = [];
                        global.anonymousClasses[fileKey].push(specificClass);
                        
                        return specificClassName;
                    }
                    return baseType; // Convert to the base type (e.g., NonFunctionProperties<MediaRequest> -> MediaRequest)
                }
                
                // Check if this type has been renamed due to conflicts
                const conflictingTypes = ['MediaStatus', 'MediaRequestStatus', 'ServerType', 'MediaServerType', 'ApiErrorCode'];
                if (conflictingTypes.includes(typeName)) {
                    return toPascalCase(typeName);
                }
                
                return typeName;
            } else if (isKnownType(typeName)) {
                return toPascalCase(typeName);
            } else {
                // Add to missing types for later conversion
                missingTypes.add(typeName);
                return toPascalCase(typeName);
            }
        case ts.SyntaxKind.UnionType:
            // For union types, try to find a common base type or use the first non-null type
            const unionTypes = typeNode.types;
            const nonNullTypes = unionTypes.filter(t => t.kind !== ts.SyntaxKind.NullKeyword && t.kind !== ts.SyntaxKind.UndefinedKeyword);
            console.log('DEBUG: UnionType - propertyName:', propertyName, 'parentClassName:', parentClassName, 'nonNullTypes.length:', nonNullTypes.length);
            
            if (nonNullTypes.length >= 2) {
                const firstType = convertTypeScriptTypeToCSharp(nonNullTypes[0], sourceFile, missingTypes, propertyName, parentClassName);
                const secondType = convertTypeScriptTypeToCSharp(nonNullTypes[1], sourceFile, missingTypes, propertyName, parentClassName);
                
                // Special case: if both types extend SearchResult, use SearchResult
                if ((firstType === 'MovieResult' && secondType === 'TvResult') || 
                    (firstType === 'TvResult' && secondType === 'MovieResult')) {
                    return 'SearchResult';
                }
                
                
                // Special case: if this is a string literal union type, create an enum
                if (nonNullTypes.length >= 2 && 
                    nonNullTypes.every(t => t.kind === ts.SyntaxKind.LiteralType && t.literal.kind === ts.SyntaxKind.StringLiteral)) {
                    
                    // Create enum name based on property name only
                    // Special case: if property name is "type", use ClassNameType to avoid C# reserved keyword conflict
                    const enumName = propertyName === 'type' ? toPascalCase(parentClassName) + 'Type' : toPascalCase(propertyName);
                    
                    // Get all string literal values
                    const enumValues = [];
                    for (let i = 0; i < nonNullTypes.length; i++) {
                        if (nonNullTypes[i].kind === ts.SyntaxKind.LiteralType && 
                            nonNullTypes[i].literal.kind === ts.SyntaxKind.StringLiteral) {
                            enumValues.push(nonNullTypes[i].literal.text);
                        }
                    }
                    
                    // Return the enum name - generation will happen in main processing loop
                    return enumName;
                }
                
                // If both types are the same, return that type
                if (firstType === secondType) {
                    return firstType;
                }
                
                // Use the first non-primitive type
                if (firstType !== 'object' && firstType !== 'string' && firstType !== 'int' && firstType !== 'bool') {
                    return firstType;
                } else if (secondType !== 'object' && secondType !== 'string' && secondType !== 'int' && secondType !== 'bool') {
                    return secondType;
                }
            }
            
            const nonNullType = nonNullTypes[0];
            return nonNullType ? convertTypeScriptTypeToCSharp(nonNullType, sourceFile, missingTypes, propertyName, parentClassName) : 'object';
        case ts.SyntaxKind.IntersectionType:
            // For intersection types, create a more specific type name
            console.log('DEBUG: Found intersection type with', typeNode.types.length, 'types');
            const intersectionTypes = typeNode.types;
            if (intersectionTypes.length === 2) {
                const firstType = convertTypeScriptTypeToCSharp(intersectionTypes[0], sourceFile, missingTypes, propertyName, parentClassName);
                const secondType = convertTypeScriptTypeToCSharp(intersectionTypes[1], sourceFile, missingTypes, propertyName, parentClassName);
                
                // If both types are the same, return that type
                if (firstType === secondType) {
                    return firstType;
                }
                
                // If one of the types is a known interface, use that as the base
                if (firstType !== 'object' && firstType !== 'string' && firstType !== 'int' && firstType !== 'bool') {
                    return firstType; // Use the first non-primitive type
                } else if (secondType !== 'object' && secondType !== 'string' && secondType !== 'int' && secondType !== 'bool') {
                    return secondType; // Use the second non-primitive type
                }
                
                // Special case: if one of the types is QueueItem, use QueueItem
                if (firstType === 'QueueItem' || secondType === 'QueueItem') {
                    return 'QueueItem';
                }
                
                // Create a specific class name for the intersection
                const intersectionClassName = parentClassName + toPascalCase(propertyName) + 'Intersection';
                
                // Generate the intersection class
                const intersectionClass = createIntersectionClass(intersectionClassName, intersectionTypes, sourceFile, missingTypes);
                
                // Store the class definition to be written later
                const fileKey = sourceFile.fileName;
                if (!global.anonymousClasses) global.anonymousClasses = {};
                if (!global.anonymousClasses[fileKey]) global.anonymousClasses[fileKey] = [];
                global.anonymousClasses[fileKey].push(intersectionClass);
                
                return intersectionClassName;
            }
            return 'object';
        case ts.SyntaxKind.LiteralType:
            if (typeNode.literal.kind === ts.SyntaxKind.StringLiteral) {
                return 'string';
            } else if (typeNode.literal.kind === ts.SyntaxKind.NumericLiteral) {
                return 'int';
            }
            return 'object';
        case ts.SyntaxKind.TypeLiteral:
            // Generate specific class name for anonymous objects
            if (propertyName && parentClassName) {
                const specificClassName = parentClassName + toPascalCase(propertyName);
                // Create the class definition for this anonymous object
                const anonymousClass = createAnonymousObjectClass(specificClassName, typeNode, sourceFile, missingTypes);
                // Store the class definition to be written later - use a unique key per file
                const fileKey = sourceFile.fileName;
                if (!global.anonymousClasses) global.anonymousClasses = {};
                if (!global.anonymousClasses[fileKey]) global.anonymousClasses[fileKey] = [];
                global.anonymousClasses[fileKey].push(anonymousClass);
                return specificClassName;
            } else {
                return 'AnonymousObject';
            }
        default:
            return 'object';
    }
}

// Function to check if a type is known (built-in or already converted)
function isKnownType(typeName) {
    const knownTypes = [
        'string', 'number', 'boolean', 'Date', 'Array', 'Object',
        'Partial', 'Record', 'Pick', 'Omit', 'Exclude', 'Extract',
        'MediaType', 'MediaStatus', 'MediaRequestStatus', 'MediaServerType', 
        'ServerType', 'UserType', 'ApiErrorCode', 'DiscoverSliderType', 
        'IssueType', 'IssueStatus', 'MediaRequest'
    ];
    return knownTypes.includes(typeName);
}

// Main conversion process
console.log('Starting TypeScript to C# conversion using TypeScript Compiler API...');

let totalFiles = 0;
let convertedFiles = 0;
const missingTypes = new Set();

// Process each directory pair
directoryPairs.forEach(pair => {
    console.log('\n=== Processing ' + pair.type + ' Models ===');
    console.log('Input: ' + pair.input);
    console.log('Output: ' + pair.output);
    
    const files = findTypeScriptFiles(pair.input);
    console.log('Found ' + files.length + ' TypeScript files');
    totalFiles += files.length;
    
    for (const file of files) {
        convertFile(file, pair.output, pair.type, missingTypes);
        convertedFiles++;
    }
});

        // Collect all search directories for missing type lookup - expand to broader codebase
        const allSearchDirs = [
            ...directoryPairs.map(pair => pair.input),
            'codebase/seerr-main/server/entity',
            'codebase/seerr-main/server/api',
            'codebase/seerr-main/server/lib',
            'codebase/seerr-main/server/constants'
        ];

        // Collect all output directories to check for existing types
        const allOutputDirs = [
            ...directoryPairs.map(pair => pair.output),
            path.join(path.dirname(directoryPairs[0].output), 'Common')
        ];

        // Build complete list of transpiled types after initial conversion
        console.log('\n=== Building Transpiled Types List ===');
        buildTranspiledTypesList(allOutputDirs);

// Convert missing types using TypeScript compiler - run iteratively until no more missing types
let iteration = 0;
let maxIterations = 10; // Prevent infinite loops

while (missingTypes.size > 0 && iteration < maxIterations) {
    iteration++;
    console.log('\n=== Converting Missing Types with TypeScript Compiler (Iteration ' + iteration + ') ===');
    console.log('Found ' + missingTypes.size + ' missing types to convert');
    
    const currentMissingTypes = new Set(missingTypes); // Copy current missing types
    missingTypes.clear(); // Clear the set for this iteration
    
    for (const missingType of currentMissingTypes) {
        // Check if this type already exists in transpiled types list
        if (typeAlreadyExists(missingType)) {
            console.log('Type ' + missingType + ' already exists, skipping...');
            continue;
        }
        
        // Skip TypeScript utility types - they should be converted to object, not classes
        if (missingType === 'Pick' || missingType === 'Record' || missingType === 'Partial' || 
            missingType === 'Omit' || missingType === 'Exclude' || missingType === 'Extract') {
            console.log('Skipping TypeScript utility type: ' + missingType);
            continue;
        }
        
        const sourceFile = findSourceFileForType(missingType, allSearchDirs);
        if (sourceFile) {
            // Determine output directory based on source file location
            let outputDir = null;
            for (const pair of directoryPairs) {
                if (sourceFile.startsWith(pair.input)) {
                    outputDir = pair.output;
                    break;
                }
            }
            
            // If not found in original directories, use Common directory to avoid duplicates
            if (!outputDir) {
                // Create Common directory path based on the first directory pair's parent
                const firstPair = directoryPairs[0];
                const parentDir = path.dirname(firstPair.output);
                outputDir = path.join(parentDir, 'Common');
            }
            
            if (outputDir) {
                console.log('Converting missing type ' + missingType + ' from ' + sourceFile);
                convertFile(sourceFile, outputDir, 'Missing', missingTypes);
            }
        } else {
            console.log('Could not find source file for missing type: ' + missingType);
        }
    }
    
    // Rebuild transpiled types list after this iteration
    console.log('Rebuilding transpiled types list after iteration ' + iteration + '...');
    buildTranspiledTypesList(allOutputDirs);
    
    console.log('Iteration ' + iteration + ' complete. Remaining missing types: ' + missingTypes.size);
}

if (iteration >= maxIterations) {
    console.log('Warning: Reached maximum iterations (' + maxIterations + '). Some types may still be missing.');
}

console.log('\n=== Conversion Complete ===');
console.log('Total files found: ' + totalFiles);
console.log('Files converted: ' + convertedFiles);
if (missingTypes.size > 0) {
    console.log('Missing types processed: ' + missingTypes.size);
}
directoryPairs.forEach(pair => {
    console.log(pair.type + ' Models: ' + pair.output);
});
console.log('Common Models: ' + path.join(path.dirname(directoryPairs[0].output), 'Common'));

        // Log generated classes for debugging
        console.log('\n=== Generated Classes Summary ===');
        console.log('Total classes generated: ' + transpiledTypes.size);
        console.log('Classes are generated locally in the files that first need them.');
"@

    Write-Host "Generating TypeScript compiler script..." -ForegroundColor Cyan
    Set-Content -Path $scriptPath -Value $jsContent -Encoding UTF8
    Write-Host "Generated: $scriptPath" -ForegroundColor Green
    return $scriptPath
}

# Function to ensure TypeScript is installed
function Install-TypeScript {
    Write-Host "Checking TypeScript installation..." -ForegroundColor Cyan
    
    try {
        $tsVersion = npx tsc --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "TypeScript version: $tsVersion" -ForegroundColor Green
        } else {
            throw "TypeScript not found"
        }
    } catch {
        Write-Host "TypeScript not found. Installing TypeScript..." -ForegroundColor Yellow
        try {
            # Install TypeScript version compatible with Node.js v12
            npm install typescript@4.9.5
            Write-Host "TypeScript installed successfully!" -ForegroundColor Green
        } catch {
            Write-Host "Failed to install TypeScript: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "Please install TypeScript manually: npm install typescript@4.9.5" -ForegroundColor Yellow
            exit 1
        }
    }
}

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Ensure TypeScript is installed
Install-TypeScript

# Generate the TypeScript compiler JavaScript file
$converterScript = New-TypeScriptCompilerScript

# Use TypeScript Compiler API for conversion
Write-Host "`nUsing TypeScript Compiler API for conversion..." -ForegroundColor Green

Write-Host "`nRunning TypeScript converter..." -ForegroundColor Yellow

try {
    # Convert directory pairs to JSON and pass to TypeScript compiler converter
    $jsonInput = $directoryPairs | ConvertTo-Json -Depth 3
    Write-Host "Passing directory pairs to TypeScript compiler converter..." -ForegroundColor Gray
    
    # Run the TypeScript compiler converter with JSON input
    $result = $jsonInput | node $converterScript 2>&1
    
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nConversion completed successfully!" -ForegroundColor Green
        Write-Host $result -ForegroundColor Cyan
        
        # Run model validation using check-models script
        Write-Host "`nRunning model validation..." -ForegroundColor Yellow
        $checkScript = Join-Path $scriptDir "check-models.ps1"
        if (Test-Path $checkScript) {
            & $checkScript -ModelDir $OutputDir
        } else {
            Write-Host "check-models.ps1 not found, skipping validation" -ForegroundColor Yellow
        }
        
    } else {
        Write-Host "`nConversion failed with exit code: $LASTEXITCODE" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
    }
    
} catch {
    Write-Host "`nError running converter: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Conversion Complete ===" -ForegroundColor Green

# Clean up temporary files created during conversion
Write-Host "`nCleaning up temporary files..." -ForegroundColor Yellow
try {
    if (Test-Path "package.json") {
        Remove-Item "package.json" -Force
        Write-Host "Removed package.json" -ForegroundColor Gray
    }
    if (Test-Path "package-lock.json") {
        Remove-Item "package-lock.json" -Force
        Write-Host "Removed package-lock.json" -ForegroundColor Gray
    }
    if (Test-Path "node_modules") {
        Remove-Item "node_modules" -Recurse -Force
        Write-Host "Removed node_modules directory" -ForegroundColor Gray
    }
    if (Test-Path "scripts\convert-with-typescript-compiler.js") {
        Remove-Item "scripts\convert-with-typescript-compiler.js" -Force
        Write-Host "Removed convert-with-typescript-compiler.js" -ForegroundColor Gray
    }
    Write-Host "Cleanup completed!" -ForegroundColor Green
} catch {
    Write-Host "Warning: Could not clean up some temporary files: $($_.Exception.Message)" -ForegroundColor Yellow
}
