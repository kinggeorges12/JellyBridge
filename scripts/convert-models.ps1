# TypeScript to C# Model Converter with Flattened Structure
# This script uses TypeScript Compiler API for interface-to-class conversions

# Load configuration variables
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $ScriptDir "convert-config.psd1"
if (Test-Path $ConfigPath) {
    $Config = Import-PowerShellDataFile $ConfigPath
    $BridgeBaseDir = $Config.BridgeBaseDir
    $SeerrRootDir = $Config.SeerrRootDir
    $OutputDir = $Config.OutputDir
    $NumberToDoublePattern = $Config.NumberToDoublePattern
    $BlockedClasses = $Config.BlockedClasses
    $DirectoryPairs = $Config.DirectoryPairs
} else {
    Write-Error "Configuration file not found: $ConfigPath"
    exit 1
}

# Change to the base directory to ensure consistent working directory
Push-Location $BridgeBaseDir

Write-Host "=== TypeScript to C# Conversion with Flattened Structure ===" -ForegroundColor Green
Write-Host "Seerr Root Directory: $SeerrRootDir" -ForegroundColor Cyan
Write-Host "Output Directory: $OutputDir" -ForegroundColor Cyan

Write-Host "`nDirectory Pairs:" -ForegroundColor Yellow

Write-Host "`nDirectory Pairs:" -ForegroundColor Yellow
foreach ($pair in $directoryPairs) {
    Write-Host "  $($pair.type): $($pair.input) -> $($pair.output)" -ForegroundColor Cyan
}

Write-Host "`nBlocked Classes:" -ForegroundColor Yellow
foreach ($class in $blockedClasses) {
    Write-Host "  $class" -ForegroundColor Red
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
let blockedClasses = [];
let numberToDoublePattern = '';

try {
    const input = fs.readFileSync(0, 'utf8').trim();
    const inputData = JSON.parse(input);
    directoryPairs = inputData.directoryPairs || [];
    blockedClasses = inputData.blockedClasses || [];
    numberToDoublePattern = inputData.numberToDoublePattern || '';
    
    // Set up the double property function using the PowerShell config
    if (numberToDoublePattern) {
        const regex = new RegExp(numberToDoublePattern, 'i');
        global.shouldBeDoubleFunction = function(propertyName) {
            if (!propertyName) return false;
            return regex.test(propertyName);
        };
    }
    console.log('Received directory pairs from PowerShell:');
    directoryPairs.forEach(pair => {
        console.log('  ' + pair.type + ': ' + pair.input + ' -> ' + pair.output);
    });
    console.log('Received blocked classes from PowerShell:');
    blockedClasses.forEach(className => {
        console.log('  ' + className);
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

// Helper function to generate property declarations with proper initialization
function generatePropertyDeclaration(propType, propName, isOptional = '') {
    const propertyDeclaration = '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }';
    
    // Add initialization based on property type
    if (propType.startsWith('List<')) {
        // Collections - initialize with empty list
        return propertyDeclaration + ' = new();\n';
    } else if (propType === 'string') {
        // Strings - initialize with empty string
        return propertyDeclaration + ' = string.Empty;\n';
        } else if (propType === 'object') {
            // Objects - initialize with null!
            return propertyDeclaration + ' = null!;\n';
        } else if (isOptional === '?') {
            // Nullable types - initialize with null!
            return propertyDeclaration + ' = null!;\n';
        } else if (isBasicType(propType)) {
            // Basic value types (int, bool, DateTime, DateTimeOffset, etc.) - no initialization needed
            return propertyDeclaration + '\n';
        } else if (propType === 'T') {
            // Generic type parameter - initialize with default(T)!
            return propertyDeclaration + ' = default(T)!;\n';
        } else {
            // Everything else (custom classes, generics, collections, etc.) - initialize with new()
            return propertyDeclaration + ' = new();\n';
        }
}

// Helper function to check if a type is a basic C# type
function isBasicType(typeName) {
    const basicTypes = [
        'int', 'long', 'short', 'byte', 'uint', 'ulong', 'ushort', 'sbyte',
        'float', 'double', 'decimal', 'bool', 'char', 'Guid', 'TimeSpan',
        'DateTime', 'DateTimeOffset', 'DateOnly', 'TimeOnly', 'object', 'string'
    ];
    return basicTypes.includes(typeName);
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
        function convertTypeORMEntityToCSharp(node) {
            console.log('Converting TypeORM entity:', node.name.text);
            
            let csharpCode = '';
            const className = node.name.text;
            
            // Add class declaration
            csharpCode += 'public class ' + className + '\n{\n';
            
            // Process class members
            if (node.members) {
                for (const member of node.members) {
                    if (member.kind === ts.SyntaxKind.PropertyDeclaration) {
                        const propertyName = member.name.text;
                        const isOptional = member.questionToken !== undefined;
                        
                        // Get TypeORM decorators
                        const decorators = member.decorators || [];
                        let isPrimaryKey = false;
                        let isColumn = false;
                        let columnType = 'string';
                        
                        for (const decorator of decorators) {
                            if (decorator.expression && decorator.expression.expression) {
                                const decoratorName = decorator.expression.expression.text;
                                if (decoratorName === 'PrimaryGeneratedColumn') {
                                    isPrimaryKey = true;
                                    isColumn = true;
                                } else if (decoratorName === 'Column') {
                                    isColumn = true;
                                    // Try to extract column type from decorator arguments
                                    if (decorator.expression.arguments && decorator.expression.arguments.length > 0) {
                                        const arg = decorator.expression.arguments[0];
                                        if (arg.properties) {
                                            for (const prop of arg.properties) {
                                                if (prop.name.text === 'type' && prop.initializer) {
                                                    columnType = prop.initializer.text.replace(/['"]/g, '');
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Convert TypeScript type to C# type
                        let csharpType = 'string'; // default
                        if (member.type) {
                            if (member.type.kind === ts.SyntaxKind.TypeReference) {
                                const typeName = member.type.typeName.text;
                                // Handle Promise<T> types - convert to nullable T
                                if (typeName === 'Promise' && member.type.typeArguments && member.type.typeArguments.length > 0) {
                                    const innerType = member.type.typeArguments[0];
                                    if (innerType.kind === ts.SyntaxKind.TypeReference) {
                                        csharpType = convertTypeScriptTypeToCSharp(innerType, '', missingTypes, propertyName, className) + '?';
                                    } else if (innerType.kind === ts.SyntaxKind.NumberKeyword) {
                                        csharpType = 'int?';
                                    } else if (innerType.kind === ts.SyntaxKind.StringKeyword) {
                                        csharpType = 'string?';
                                    } else if (innerType.kind === ts.SyntaxKind.BooleanKeyword) {
                                        csharpType = 'bool?';
                                    }
                                } else {
                                    csharpType = convertTypeScriptTypeToCSharp(member.type, '', missingTypes, propertyName, className);
                                }
                            } else if (member.type.kind === ts.SyntaxKind.NumberKeyword) {
                                csharpType = 'int';
                            } else if (member.type.kind === ts.SyntaxKind.StringKeyword) {
                                csharpType = 'string';
                            } else if (member.type.kind === ts.SyntaxKind.BooleanKeyword) {
                                csharpType = 'bool';
                            } else if (member.type.kind === ts.SyntaxKind.UnionType) {
                                // Handle union types (usually for optional properties)
                                const unionTypes = member.type.types;
                                if (unionTypes.length === 2 && unionTypes.some(t => t.kind === ts.SyntaxKind.NullKeyword)) {
                                    const nonNullType = unionTypes.find(t => t.kind !== ts.SyntaxKind.NullKeyword);
                                    if (nonNullType.kind === ts.SyntaxKind.TypeReference) {
                                        csharpType = convertTypeScriptTypeToCSharp(nonNullType, '', missingTypes, propertyName, className) + '?';
                                    } else if (nonNullType.kind === ts.SyntaxKind.NumberKeyword) {
                                        csharpType = 'int?';
                                    } else if (nonNullType.kind === ts.SyntaxKind.StringKeyword) {
                                        csharpType = 'string?';
                                    } else if (nonNullType.kind === ts.SyntaxKind.BooleanKeyword) {
                                        csharpType = 'bool?';
                                    }
                                }
                            } else if (member.type.kind === ts.SyntaxKind.ArrayType) {
                                const elementType = member.type.elementType;
                                if (elementType.kind === ts.SyntaxKind.TypeReference) {
                                    csharpType = 'List<' + convertTypeScriptTypeToCSharp(elementType, '', missingTypes, propertyName, className) + '>';
                                } else if (elementType.kind === ts.SyntaxKind.NumberKeyword) {
                                    csharpType = 'List<int>';
                                } else if (elementType.kind === ts.SyntaxKind.StringKeyword) {
                                    csharpType = 'List<string>';
                                } else if (elementType.kind === ts.SyntaxKind.BooleanKeyword) {
                                    csharpType = 'List<bool>';
                                }
                            }
                        }
                        
                        // Add JSON property attribute
                        csharpCode += '    [JsonPropertyName("' + propertyName + '")]\n';
                        
                        // Add property declaration
                        csharpCode += generatePropertyDeclaration(csharpType, propertyName);
                    }
                }
            }
            
            csharpCode += '}\n';
            return csharpCode;
        }

        function convertFile(tsFilePath, outputDir, modelType, missingTypes = new Set(), blockedClasses = []) {
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
                                        
                                        const enumClass = 'public enum ' + enumName + '\n{\n' +
                                            enumValues.map((value, index) => '    ' + toPascalCase(value) + (index === 0 ? ' = 0' : '')).join(',\n') + '\n}';
                                        
                                        csharpCode += enumClass + '\n\n';
                                        transpiledTypes.add(enumName);
                                        console.log('Generated ' + enumName + ' locally');
                                    }
                                }
                            }
                        }
                    });
                }
                
                // First pass: collect all interfaces and their inheritance relationships
                const interfaces = [];
                const inheritanceMap = new Map();
                
                function collectInterfaces(node) {
                    if (node.kind === ts.SyntaxKind.InterfaceDeclaration || 
                        node.kind === ts.SyntaxKind.ClassDeclaration) {
                        
                        // Skip blocked classes
                        if (blockedClasses.includes(node.name.text)) {
                            console.log('Skipping blocked class: ' + node.name.text);
                            return;
                        }
                        
                        interfaces.push(node);
                        
                        // Track inheritance relationships
                        if (node.heritageClauses && node.heritageClauses.length > 0) {
                            const extendsClause = node.heritageClauses.find(clause => clause.token === ts.SyntaxKind.ExtendsKeyword);
                            if (extendsClause && extendsClause.types.length > 0) {
                                const baseType = extendsClause.types[0];
                                let baseName = '';
                                
                                if (baseType.expression.kind === ts.SyntaxKind.Identifier) {
                                    baseName = baseType.expression.text;
                                } else if (baseType.expression.kind === ts.SyntaxKind.CallExpression) {
                                    const typeName = baseType.expression.expression.text;
                                    if (typeName === 'Omit' && baseType.typeArguments && baseType.typeArguments.length >= 1) {
                                        baseName = baseType.typeArguments[0].expression.text;
                                    }
                                }
                                
                                if (baseName) {
                                    if (!inheritanceMap.has(baseName)) {
                                        inheritanceMap.set(baseName, []);
                                    }
                                    inheritanceMap.get(baseName).push(node.name.text);
                                }
                            }
                        }
                    }
                    ts.forEachChild(node, collectInterfaces);
                }
                
                collectInterfaces(sourceFile);
                
                // Second pass: process interfaces in dependency order (base classes first)
                const processedInterfaces = new Set();
                
                function processInterface(interfaceNode) {
                    if (processedInterfaces.has(interfaceNode.name.text)) {
                        return;
                    }
                    
                    // Check if this interface extends another interface in the same file
                    if (interfaceNode.heritageClauses && interfaceNode.heritageClauses.length > 0) {
                        const extendsClause = interfaceNode.heritageClauses.find(clause => clause.token === ts.SyntaxKind.ExtendsKeyword);
                        if (extendsClause && extendsClause.types.length > 0) {
                            const baseType = extendsClause.types[0];
                            let baseName = '';
                            
                            if (baseType.expression.kind === ts.SyntaxKind.Identifier) {
                                baseName = baseType.expression.text;
                            } else if (baseType.expression.kind === ts.SyntaxKind.CallExpression) {
                                const typeName = baseType.expression.expression.text;
                                if (typeName === 'Omit' && baseType.typeArguments && baseType.typeArguments.length >= 1) {
                                    baseName = baseType.typeArguments[0].expression.text;
                                }
                            }
                            
                            // Find and process the base interface first
                            if (baseName) {
                                const baseInterface = interfaces.find(i => i.name.text === baseName);
                                if (baseInterface && !processedInterfaces.has(baseName)) {
                                    processInterface(baseInterface);
                                }
                            }
                        }
                    }
                    
                    // Process this interface
                        hasInterfaces = true;
                    
                    // Check if this is a TypeORM entity class
                    let isTypeORMEntity = false;
                    if (interfaceNode.kind === ts.SyntaxKind.ClassDeclaration) {
                        console.log('DEBUG: Processing class:', interfaceNode.name.text);
                        console.log('DEBUG: Class has decorators:', interfaceNode.decorators ? interfaceNode.decorators.length : 0);
                        
                        // Check for @Entity decorator using regex on source code
                        const className = interfaceNode.name.text;
                        const entityRegex = new RegExp('@Entity\\(\\s*\\)\\s*\\n\\s*@.*\\n\\s*export\\s+class\\s+' + className, 'g');
                        if (entityRegex.test(sourceCode)) {
                            isTypeORMEntity = true;
                            console.log('DEBUG: Found @Entity decorator via regex!');
                        } else {
                            // Try simpler pattern without additional decorators
                            const simpleEntityRegex = new RegExp('@Entity\\(\\s*\\)\\s*\\n\\s*export\\s+class\\s+' + className, 'g');
                            if (simpleEntityRegex.test(sourceCode)) {
                                isTypeORMEntity = true;
                                console.log('DEBUG: Found @Entity decorator via simple regex!');
                            } else {
                                // Try even simpler pattern
                                const basicEntityRegex = new RegExp('@Entity\\(\\s*\\)\\s*.*class\\s+' + className, 'g');
                                if (basicEntityRegex.test(sourceCode)) {
                                    isTypeORMEntity = true;
                                    console.log('DEBUG: Found @Entity decorator via basic regex!');
                                }
                            }
                        }
                        
                        // Also check TypeScript compiler decorators as fallback
                        if (!isTypeORMEntity && interfaceNode.decorators) {
                            for (const decorator of interfaceNode.decorators) {
                                console.log('DEBUG: Decorator:', decorator.expression ? decorator.expression.expression.text : 'unknown');
                                if (decorator.expression && decorator.expression.expression && 
                                    decorator.expression.expression.text === 'Entity') {
                                    isTypeORMEntity = true;
                                    console.log('DEBUG: Found @Entity decorator via TypeScript compiler!');
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (isTypeORMEntity) {
                        console.log('Detected TypeORM entity:', interfaceNode.name.text);
                        csharpCode += convertTypeORMEntityToCSharp(interfaceNode) + '\n\n';
                    } else {
                        csharpCode += convertInterfaceOrClassToClass(interfaceNode, sourceFile, missingTypes) + '\n\n';
                    }
                    
                    generateUnionTypeEnums(interfaceNode, sourceFile);
                    processedInterfaces.add(interfaceNode.name.text);
                }
                
                // Process all interfaces in dependency order
                interfaces.forEach(processInterface);
                
                function visit(node) {
                    if (node.kind === ts.SyntaxKind.EnumDeclaration) {
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
                        const typeName = node.name.text;
                        console.log('DEBUG: Processing type alias:', typeName);
                        const converted = convertTypeAliasToClass(node, sourceFile, missingTypes);
                        console.log('DEBUG: Converted result for', typeName, ':', converted ? 'SUCCESS' : 'EMPTY');
                        if (converted) {
                            csharpCode += converted + '\n\n';
                            transpiledTypes.add(typeName);
                            console.log('DEBUG: Added', typeName, 'to csharpCode and transpiledTypes');
                        }
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
                
                // Add namespace and using statements based on model type
                let namespace = 'Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel';
                let usingStatements = 'using System;\n' +
                                     'using System.Text.Json.Serialization;\n' +
                                     'using System.Collections.Generic;\n';
                
                if (modelType === 'Server') {
                    namespace = 'Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server';
                    usingStatements += 'using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;\n';
                } else if (modelType === 'Api') {
                    namespace = 'Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api';
                    usingStatements += 'using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;\n';
                } else if (modelType === 'Entity') {
                    namespace = 'Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel';
                }
                
                // TODO: Add cross-namespace using statements based on referenced types
                // const crossNamespaceUsingStatements = generateCrossNamespaceUsingStatements(csharpCode, namespace);
                // usingStatements += crossNamespaceUsingStatements;
                
                let fullCsharpCode = usingStatements + '\n' +
                                     'namespace ' + namespace + ';\n\n' +
                                     csharpCode;
                
                // Add anonymous classes if any were generated for this file
                const fileKey = sourceFile.fileName;
                if (global.anonymousClasses && global.anonymousClasses[fileKey] && global.anonymousClasses[fileKey].length > 0) {
                    fullCsharpCode += '\n\n' + global.anonymousClasses[fileKey].join('\n\n');
                    // Clear the file's anonymous classes after using them
                    global.anonymousClasses[fileKey] = [];
                }
                
                console.log('DEBUG: Writing fullCsharpCode to file:', outputPath);
                console.log('DEBUG: fullCsharpCode length:', fullCsharpCode.length);
                console.log('DEBUG: fullCsharpCode preview:', fullCsharpCode.substring(0, 200) + '...');
                fs.writeFileSync(outputPath, fullCsharpCode);
                console.log('Generated: ' + outputPath);
                
            } catch (error) {
                console.error('Error converting ' + tsFilePath + ':', error);
            }
        }

// Function to convert TypeScript interface or class to C# class
function convertInterfaceOrClassToClass(interfaceNode, sourceFile, missingTypes = new Set()) {
    const interfaceName = interfaceNode.name.text;
    const members = interfaceNode.members;
    
    // Check for inheritance
    let inheritance = '';
    if (interfaceNode.heritageClauses && interfaceNode.heritageClauses.length > 0) {
        const extendsClause = interfaceNode.heritageClauses.find(clause => clause.token === ts.SyntaxKind.ExtendsKeyword);
        if (extendsClause && extendsClause.types.length > 0) {
            const extendsType = extendsClause.types[0];
            
            // Handle Omit<T, K> in inheritance
            if (extendsType.expression.kind === ts.SyntaxKind.CallExpression || 
                (extendsType.expression.kind === ts.SyntaxKind.Identifier && extendsType.typeArguments)) {
                
                const typeName = extendsType.expression.text || (extendsType.expression.expression && extendsType.expression.expression.text);
                
                if (typeName === 'Omit' && extendsType.typeArguments && extendsType.typeArguments.length >= 2) {
                    // Extract the base type (first argument) and omitted properties (second argument)
                    const baseType = convertTypeScriptTypeToCSharp(extendsType.typeArguments[0], sourceFile, missingTypes, '', interfaceName);
                    const omittedProperties = extendsType.typeArguments[1];
                    
                    // Store omitted properties info for later use
                    const originalText = extendsType.getText().replace(/\s+/g, ' ').trim();
                    if (!global.omittedProperties) global.omittedProperties = {};
                    global.omittedProperties[interfaceName] = {
                        baseType: baseType,
                        omittedProps: omittedProperties,
                        originalText: originalText
                    };
                    
                    inheritance = ' : ' + baseType;
                } else {
                    // Handle other utility types in inheritance
                    const baseType = convertTypeScriptTypeToCSharp(extendsType, sourceFile, missingTypes, '', interfaceName);
                    inheritance = ' : ' + baseType;
                }
            } else {
                // Simple type name
                const baseType = extendsType.expression.text;
                inheritance = ' : ' + toPascalCase(baseType);
            }
        }
    }
    
    let csharpClass = 'public class ' + interfaceName + inheritance + '\n{\n';
    
    members.forEach(member => {
        if (member.kind === ts.SyntaxKind.PropertySignature) {
            const propName = member.name.text;
            const propType = convertTypeScriptTypeToCSharp(member.type, sourceFile, missingTypes, propName, interfaceName);
            const isOptional = member.questionToken ? '?' : '';
            
            // Debug indexed access types
            if (member.type.kind === ts.SyntaxKind.IndexedAccessType) {
                console.log('DEBUG: Indexed access type detected for', interfaceName + '.' + propName, '- member.type.kind:', ts.SyntaxKind[member.type.kind]);
                console.log('DEBUG: Indexed access type:', member.type);
            }
            
            csharpClass += '    [JsonPropertyName("' + toJsonPropertyName(propName) + '")]\n';
            
            // Add union type comment if available
            const commentKey = interfaceName + '.' + propName;
            if (global.unionTypeComments && global.unionTypeComments[commentKey]) {
                csharpClass += '    ' + global.unionTypeComments[commentKey] + '\n';
            }
            
            csharpClass += generatePropertyDeclaration(propType, propName, isOptional) + '\n';
        }
    });
    
    // Handle omitted properties from Omit<T, K> inheritance
    if (global.omittedProperties && global.omittedProperties[interfaceName]) {
        const omitInfo = global.omittedProperties[interfaceName];
        
        // Add comment explaining the Omit
        csharpClass += '\n    // TypeScript: ' + omitInfo.originalText + '\n';
        
        // Note: Properties are omitted (not added back) to avoid hiding warnings
        // The base class properties are inherited, omitted properties are excluded
    }
    
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
                        
                        // Add union type comment if available
                        const commentKey = className + '.' + propName;
                        if (global.unionTypeComments && global.unionTypeComments[commentKey]) {
                            csharpClass += '    ' + global.unionTypeComments[commentKey] + '\n';
                        }
                        
                        csharpClass += generatePropertyDeclaration(propType, propName, isOptional) + '\n';
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
    
    // Check for naming conflicts with known enums/types - now programmatic
    const conflictingTypes = []; // No hardcoded conflicts, let the script handle them dynamically
    let finalTypeName = typeName;
    
    if (conflictingTypes.includes(typeName)) {
        // Keep original name for conflicting types
        finalTypeName = typeName;
        console.log('Using original name for conflicting type alias: ' + typeName);
    }
    
    // Handle generic utility types - preserve the utility type name with generics
    if (typeAliasNode.typeParameters && typeAliasNode.typeParameters.length > 0) {
        console.log('Preserving generic utility type: ' + typeName);
        const typeParams = typeAliasNode.typeParameters.map(p => p.name.text).join(', ');
        return 'public class ' + finalTypeName + '<' + typeParams + '>\n{\n' +
               generatePropertyDeclaration('T', 'Value', '') +
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
                
                // Add union type comment if available
                const commentKey = finalTypeName + '.' + propName;
                if (global.unionTypeComments && global.unionTypeComments[commentKey]) {
                    csharpClass += '    ' + global.unionTypeComments[commentKey] + '\n';
                }
                
                csharpClass += generatePropertyDeclaration(propType, propName, isOptional) + '\n';
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
            
            // Generate enum and return it (will be added to transpiled types when written to file)
            const enumClass = 'public enum ' + finalTypeName + '\n{\n' +
                enumValues.map(value => '    ' + toPascalCase(value)).join(',\n') + '\n}';
                console.log('Generated ' + finalTypeName + ' locally');
            return enumClass;  // Return the enum directly
        }
        
        // Handle complex union types (like Results = MovieResult | TvResult | PersonResult | CollectionResult)
        if (nonNullTypes.length >= 2) {
            // Add TypeScript definition comment
            const tsDefinition = '// TypeScript: ' + finalTypeName + ' = ' + 
                nonNullTypes.map(t => t.getText()).join(' | ');
            
            // Check for common base classes using TypeScript compiler API
            const baseTypes = new Set();
            for (const typeNode of nonNullTypes) {
                if (typeNode.kind === ts.SyntaxKind.TypeReference) {
                    // Get the type name from the TypeReference
                    const typeName = typeNode.typeName.getText();
                    
                    // Look for interface declarations with this name in the source file
                    const findInterface = (node) => {
                        if (ts.isInterfaceDeclaration(node) && node.name.text === typeName) {
                            if (node.heritageClauses) {
                                for (const heritageClause of node.heritageClauses) {
                                    for (const heritageType of heritageClause.types) {
                                        if (heritageType.kind === ts.SyntaxKind.ExpressionWithTypeArguments) {
                                            const baseTypeName = heritageType.expression.getText();
                                            baseTypes.add(baseTypeName);
                                        }
                                    }
                                }
                            }
                        }
                        ts.forEachChild(node, findInterface);
                    };
                    
                    findInterface(sourceFile);
                }
            }
            
            // If all types share a common base class, use it
            if (baseTypes.size === 1) {
                const commonBase = Array.from(baseTypes)[0];
                return tsDefinition + '\npublic class ' + finalTypeName + '\n{\n' +
                       generatePropertyDeclaration(commonBase, 'Value', '') +
                       '}\n';
            }
            
            // For complex unions with no common base, use object
            return tsDefinition + '\npublic class ' + finalTypeName + '\n{\n' +
                   generatePropertyDeclaration('object', 'Value', '') +
                   '}\n';
        }
    }
    
    
    const convertedType = convertTypeScriptTypeToCSharp(typeNode, sourceFile, missingTypes, '', finalTypeName);
    return 'public class ' + finalTypeName + '\n{\n' + generatePropertyDeclaration(convertedType, 'Value', '') + '}';
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
        // Convert string enum to C# enum with numeric values
        let csharpClass = 'public enum ' + enumName + '\n{\n';
        
        members.forEach((member, index) => {
            const memberName = member.name.text;
            let memberValue = '';
            
            if (member.initializer && member.initializer.kind === ts.SyntaxKind.StringLiteral) {
                memberValue = ' = ' + index; // Use index for enum values
            } else {
                memberValue = index === 0 ? ' = 0' : ''; // First enum value starts at 0
            }
            
            csharpClass += '    ' + memberName + memberValue;
            if (index < members.length - 1) {
                csharpClass += ',';
            }
            csharpClass += '\n';
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
                
                // Add union type comment if available
                const commentKey = className + '.' + propName;
                if (global.unionTypeComments && global.unionTypeComments[commentKey]) {
                    csharpClass += '    ' + global.unionTypeComments[commentKey] + '\n';
                }
                
                csharpClass += generatePropertyDeclaration(propType, propName, isOptional) + '\n';
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
            
            // Check if this is a union type array and add comment
            if (elementType === 'object' && (typeNode.elementType.kind === ts.SyntaxKind.UnionType || typeNode.elementType.kind === ts.SyntaxKind.ParenthesizedType)) {
                
                // Handle ParenthesizedType by getting the inner type
                let actualElementType = typeNode.elementType;
                if (actualElementType.kind === ts.SyntaxKind.ParenthesizedType) {
                    actualElementType = actualElementType.type;
                }
                
                if (actualElementType.kind === ts.SyntaxKind.UnionType) {
                    const unionTypes = actualElementType.types;
                    const nonNullTypes = unionTypes.filter(t => t.kind !== ts.SyntaxKind.NullKeyword && t.kind !== ts.SyntaxKind.UndefinedKeyword);
                    
                    if (nonNullTypes.length >= 2) {
                        const unionTypeNames = nonNullTypes.map(t => {
                            if (t.kind === ts.SyntaxKind.TypeReference) {
                                return t.typeName.text;
                            }
                            return 'unknown';
                        }).filter(name => name !== 'unknown');
                        
                        if (unionTypeNames.length >= 2) {
                            // Get the original TypeScript text for this union type array and clean it up
                            let originalText = typeNode.getText();
                            // Remove extra whitespace and normalize line breaks
                            originalText = originalText.replace(/\s+/g, ' ').trim();
                            const unionComment = '// Union type array: ' + originalText;
                            // Store the comment to be added when generating the property
                            if (!global.unionTypeComments) global.unionTypeComments = {};
                            const commentKey = parentClassName + '.' + propertyName;
                            global.unionTypeComments[commentKey] = unionComment;
                        }
                    }
                }
            }
            
            return 'List<' + elementType + '>';
        case ts.SyntaxKind.IndexedAccessType:
            // Handle indexed access types
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
            
            // Debug indexed access types
            if (typeNode.kind === ts.SyntaxKind.IndexedAccessType) {
                console.log('DEBUG: Indexed access type detected for', parentClassName + '.' + propertyName, 'typeName:', typeName, 'typeNode.kind:', ts.SyntaxKind[typeNode.kind]);
            }
            
            // Handle indexed access types
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
                    
                    // Add explanatory comment for Record<string, unknown> pattern
                    if (keyType === 'string' && valueType === 'object') {
                        const originalText = typeNode.getText().replace(/\s+/g, ' ').trim();
                        const comment = '// TypeScript: ' + originalText;
                        console.log('DEBUG: Adding Record comment for:', parentClassName + '.' + propertyName, '->', comment);
                        // Store the comment to be added when generating the property
                        if (!global.unionTypeComments) global.unionTypeComments = {};
                        const commentKey = parentClassName + '.' + propertyName;
                        global.unionTypeComments[commentKey] = comment;
                    }
                    
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
                    // Convert Partial<T> to T since we're sending complete objects
                    if (typeName === 'Partial') {
                        const originalText = typeNode.getText().replace(/\s+/g, ' ').trim();
                        const comment = '// TypeScript: ' + originalText;
                        // Store the comment to be added when generating the property
                        if (!global.unionTypeComments) global.unionTypeComments = {};
                        const commentKey = parentClassName + '.' + propertyName;
                        global.unionTypeComments[commentKey] = comment;
                        return baseType;
                    }
                    return typeName + '<' + baseType + '>'; // Preserve other utility type names (e.g., Pick<T, K>)
                }
                
                // Check if this type has been renamed due to conflicts - now programmatic
                const conflictingTypes = []; // No hardcoded conflicts, let the script handle them dynamically
                if (conflictingTypes.includes(typeName)) {
                    return toPascalCase(typeName);
                }
                
                return typeName;
            } else if (typeName === 'Error') {
                // Map TypeScript's built-in Error class to C#'s Exception class
                return 'Exception';
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
                
                // Try to find a common base type for union types
                if (nonNullTypes.length >= 2) {
                    // Convert all types to C# types first
                    const convertedTypes = nonNullTypes.map(t => 
                        convertTypeScriptTypeToCSharp(t, sourceFile, missingTypes, propertyName, parentClassName)
                    );
                    
                    // Check if all types are the same - if so, return that type
                    const firstType = convertedTypes[0];
                    if (convertedTypes.every(t => t === firstType)) {
                        return firstType;
                    }
                    
                    // Check for common base classes using TypeScript compiler API
                    const baseTypes = new Set();
                    for (const typeNode of nonNullTypes) {
                        if (typeNode.kind === ts.SyntaxKind.TypeReference) {
                            // Get the type name from the TypeReference
                            const typeName = typeNode.typeName.getText();
                            
                            // Look for interface declarations with this name in the source file
                            const findInterface = (node) => {
                                if (ts.isInterfaceDeclaration(node) && node.name.text === typeName) {
                                    if (node.heritageClauses) {
                                        for (const heritageClause of node.heritageClauses) {
                                            for (const heritageType of heritageClause.types) {
                                                if (heritageType.kind === ts.SyntaxKind.ExpressionWithTypeArguments) {
                                                    const baseTypeName = heritageType.expression.getText();
                                                    baseTypes.add(baseTypeName);
                                                }
                                            }
                                        }
                                    }
                                }
                                ts.forEachChild(node, findInterface);
                            };
                            
                            findInterface(sourceFile);
                        }
                    }
                    
                    // If all types share a common base class, use it
                    if (baseTypes.size === 1) {
                        const commonBase = Array.from(baseTypes)[0];
                        return commonBase;
                    }
                    
                    // For complex unions with no common base, return object
                    return 'object';
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
            const result = nonNullType ? convertTypeScriptTypeToCSharp(nonNullType, sourceFile, missingTypes, propertyName, parentClassName) : 'object';
            
            // If we're defaulting to object for a union type, add a comment
            if (result === 'object' && nonNullTypes.length >= 2) {
                const unionTypeNames = nonNullTypes.map(t => {
                    if (t.kind === ts.SyntaxKind.TypeReference) {
                        return t.typeName.text;
                    }
                    return 'unknown';
                }).filter(name => name !== 'unknown');
                
                if (unionTypeNames.length >= 2) {
                    // Get the original TypeScript text for this union type
                    const originalText = typeNode.getText();
                    const unionComment = '// Union type: ' + originalText;
                    // Store the comment to be added when generating the property
                    if (!global.unionTypeComments) global.unionTypeComments = {};
                    const commentKey = parentClassName + '.' + propertyName;
                    global.unionTypeComments[commentKey] = unionComment;
                }
            }
            
            return result;
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
                
                // Special case: if one of the types matches a common pattern, use it
                if (firstType.endsWith('Item') || secondType.endsWith('Item')) {
                    return firstType.endsWith('Item') ? firstType : secondType;
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
    const builtInTypes = [
        'string', 'number', 'boolean', 'Date', 'Array', 'Object',
        'Partial', 'Record', 'Pick', 'Omit', 'Exclude', 'Extract'
    ];
    return builtInTypes.includes(typeName);
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
        convertFile(file, pair.output, pair.type, missingTypes, blockedClasses);
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
        
        // Skip blocked classes
        if (blockedClasses.includes(missingType)) {
            console.log('Skipping blocked class: ' + missingType);
            continue;
        }
        
        const sourceFile = findSourceFileForType(missingType, allSearchDirs);
        if (sourceFile) {
            // Determine output directory based on source file location
            let outputDir = null;
            let isEntityFile = false;
            
            // First, check if this is a call from PowerShell with tempDirectoryPairs
            // If so, use the output directory from the tempDirectoryPairs
            if (directoryPairs.length === 1 && directoryPairs[0].input && directoryPairs[0].output) {
                // This is likely a call from PowerShell with tempDirectoryPairs
                outputDir = directoryPairs[0].output;
                console.log('Using output directory from tempDirectoryPairs: ' + outputDir);
            } else if (directoryPairs.length === 2 && directoryPairs[0].type === 'Server' && directoryPairs[1].type === 'Api') {
                // This is the main conversion process - check if source file is an entity file
                if (sourceFile.includes('server/entity/') || sourceFile.includes('server\\entity\\')) {
                    // Entity files should go to Server directory
                    outputDir = directoryPairs[0].output; // Server directory
                    isEntityFile = true;
                    console.log('Entity file detected - using Server directory: ' + outputDir);
                } else {
                    // Use original logic for non-entity files
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
                }
            } else {
                // Original logic for main conversion process
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
            }
            
            if (outputDir) {
                console.log('Converting missing type ' + missingType + ' from ' + sourceFile);
                
                // Determine modelType based on output directory using directory pairs
                let modelType = 'Entity'; // Default to Common namespace
                
                // If this is an entity file, use Server type
                if (isEntityFile) {
                    modelType = 'Server';
                } else {
                    // Find matching directory pair to determine the correct type
                    for (const pair of directoryPairs) {
                        if (outputDir === pair.output || outputDir.startsWith(pair.output + '/') || outputDir.startsWith(pair.output + '\\')) {
                            modelType = pair.type;
                            break;
                        }
                    }
                }
                
                convertFile(sourceFile, outputDir, modelType, missingTypes, blockedClasses);
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

# Function to generate consistent enum format
function New-EnumFromConstants {
    param(
        [string]$EnumName,
        [string[]]$Constants
    )
    
    # Generate consistent enum format
    $enumDefinition = "public enum $EnumName`n{`n"
    for ($i = 0; $i -lt $Constants.Count; $i++) {
        $enumDefinition += "    $($Constants[$i])"
        if ($i -lt $Constants.Count - 1) {
            $enumDefinition += ","
        }
        $enumDefinition += "`n"
    }
    $enumDefinition += "}"
    return $enumDefinition
}

# Function to convert static class to enum
function Convert-StaticClassToEnum {
    param(
        [string]$TypeDefinition,
        [string]$TypeName
    )
    
    # Extract constants from static class
    $constants = @()
    $lines = $TypeDefinition -split "`n"
    foreach ($line in $lines) {
        $constantMatch = [regex]::Match($line, 'public const string (\w+) = "([^"]+)";')
        if ($constantMatch.Success) {
            $constants += $constantMatch.Groups[1].Value
        }
    }
    
    if ($constants.Count -gt 0) {
        Write-Host "  Converting static class $TypeName to enum..." -ForegroundColor Cyan
        $enumDefinition = New-EnumFromConstants -EnumName $TypeName -Constants $constants
        Write-Host "  Generated enum with $($constants.Count) values: $($constants -join ', ')" -ForegroundColor Green
        return $enumDefinition
    }
    
    return $TypeDefinition
}

# Function to detect and convert missing entity types
function Convert-MissingEntityTypes {
    Write-Host "Analyzing generated C# files for missing type references..." -ForegroundColor Cyan
    
    # Get all generated C# files
    $csharpFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "*.cs"
    
    # Extract all type references from C# files
    $missingTypes = @()
    $allTypeReferences = @()
    
    foreach ($file in $csharpFiles) {
        $content = Get-Content $file.FullName -Raw
        # Find type references in property declarations - more specific pattern
        $propertyMatches = [regex]::Matches($content, 'public\s+(\w+)\s+\w+\s*\{\s*get;\s*set;\s*\}', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($propertyMatch in $propertyMatches) {
            $typeName = $propertyMatch.Groups[1].Value
            # Filter out built-in types and generic types
            if ($typeName -notin @('int', 'string', 'bool', 'DateTime', 'DateTimeOffset', 'double', 'float', 'decimal', 'object', 'List', 'Dictionary', 'Array', 'IEnumerable', 'Task', 'void', 'T', 'class') -and 
                $typeName -notmatch '^[A-Z][a-zA-Z]*$' -eq $false) {
                $allTypeReferences += $typeName
            }
        }
        
        # Also find type references in method return types
        $methodMatches = [regex]::Matches($content, 'public\s+(\w+)\s+\w+\s*\(', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($methodMatch in $methodMatches) {
            $typeName = $methodMatch.Groups[1].Value
            if ($typeName -notin @('int', 'string', 'bool', 'DateTime', 'DateTimeOffset', 'double', 'float', 'decimal', 'object', 'List', 'Dictionary', 'Array', 'IEnumerable', 'Task', 'void', 'T', 'class') -and 
                $typeName -notmatch '^[A-Z][a-zA-Z]*$' -eq $false) {
                $allTypeReferences += $typeName
            }
        }
    }
    
    # Get all defined types from generated files
    $definedTypes = @()
    foreach ($file in $csharpFiles) {
        $content = Get-Content $file.FullName -Raw
        $classMatches = [regex]::Matches($content, 'public\s+class\s+(\w+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $classMatches) {
            $definedTypes += $match.Groups[1].Value
        }
        $enumMatches = [regex]::Matches($content, 'public\s+enum\s+(\w+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $enumMatches) {
            $definedTypes += $match.Groups[1].Value
        }
    }
    
    # Find missing types and cross-namespace conflicts
    $uniqueReferences = $allTypeReferences | Sort-Object | Get-Unique
    foreach ($typeRef in $uniqueReferences) {
        if ($typeRef -notin $definedTypes) {
            $missingTypes += $typeRef
        } else {
            # Check if this type is defined in a different namespace than where it's referenced
            $typeDefinedIn = @()
            $typeReferencedIn = @()
            
            foreach ($file in $csharpFiles) {
                $content = Get-Content $file.FullName -Raw
                $fileNamespace = ""
                $namespaceMatch = $content | Select-String -Pattern 'namespace\s+([^;]+)'
                if ($namespaceMatch) {
                    $fileNamespace = $namespaceMatch.Matches[0].Groups[1].Value.Trim()
                }
                
                # Check if type is defined in this file
                if ($content -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b") {
                    $typeDefinedIn += $fileNamespace
                }
                
                # Check if type is referenced in this file (property declarations)
                if ($content -match "public\s+$typeRef\s+\w+\s*\{") {
                    $typeReferencedIn += $fileNamespace
                }
                
                # Also check for List<T> references
                if ($content -match "List<$typeRef>") {
                    $typeReferencedIn += $fileNamespace
                }
                
                # Also check for Dictionary<K,V> references
                if ($content -match "Dictionary<[^,]*,\s*$typeRef>") {
                    $typeReferencedIn += $fileNamespace
                }
            }
            
            # If type is defined in Server/Api but referenced in Api/Server, move to Common
            $commonNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel"
            
            # Only move types to Common if they are truly shared (defined in multiple namespaces)
            # Don't move types that are just referenced across namespaces - use using statements instead
            if ($typeDefinedIn.Count -gt 1) {
                Write-Host "Cross-namespace conflict detected for type: $typeRef" -ForegroundColor Yellow
                Write-Host "  Defined in: $($typeDefinedIn -join ', ')" -ForegroundColor Yellow
                Write-Host "  Referenced in: $($typeReferencedIn -join ', ')" -ForegroundColor Yellow
                Write-Host "  Moving to Common namespace..." -ForegroundColor Cyan
                
                # Find the file where this type is defined and move it to Common
                foreach ($file in $csharpFiles) {
                    $content = Get-Content $file.FullName -Raw
                    if ($content -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b") {
                        # Extract the type definition - need to handle multi-line definitions
                        $typeDefinition = ""
                        $lines = $content -split "`n"
                        $inTypeDefinition = $false
                        $braceCount = 0
                        $typeLines = @()
                        
                        foreach ($line in $lines) {
                            if ($line -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b") {
                                $inTypeDefinition = $true
                                $typeLines += $line
                                # Count braces in this line
                                $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                                $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                                continue
                            }
                            
                            if ($inTypeDefinition) {
                                $typeLines += $line
                                $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                                $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                                
                                if ($braceCount -eq 0) {
                                    $inTypeDefinition = $false
                                    break
                                }
                            }
                        }
                        
                        if ($typeLines.Count -gt 0) {
                            $typeDefinition = $typeLines -join "`n"
                            
                            # Check if this is already an enum - if so, keep it as-is
                            if ($typeDefinition -match "public enum $typeRef") {
                                Write-Host "  Type $typeRef is already an enum, keeping as-is" -ForegroundColor Green
                            }
                            # Check if this is a static class that should be converted to an enum
                            elseif ($typeDefinition -match "public static class $typeRef") {
                                $typeDefinition = Convert-StaticClassToEnum -TypeDefinition $typeDefinition -TypeName $typeRef
                            }
                        }
                        
                        if ($typeDefinition) {
                            # Create Common file for this type
                            $commonFile = Join-Path $OutputDir "Common\$typeRef.cs"
                            $commonContent = @"
using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace $commonNamespace;

$typeDefinition
"@
                            Set-Content -Path $commonFile -Value $commonContent
                            Write-Host "  Created Common file: $commonFile" -ForegroundColor Green
                            Write-Host "  DEBUG: typeDefinition = $typeDefinition" -ForegroundColor Magenta
                            
                            # Remove the type from the original file
                            Write-Host "  DEBUG: Searching for type $typeRef in file $($file.Name)" -ForegroundColor Magenta
                            if ($content -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b") {
                                Write-Host "  DEBUG: Found type $typeRef in file, removing..." -ForegroundColor Magenta
                                $newContent = $content -replace "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b[^}]*\}", ""
                                Set-Content -Path $file.FullName -Value $newContent
                                Write-Host "  Removed from original file: $($file.Name)" -ForegroundColor Green
                            } else {
                                Write-Host "  DEBUG: Type $typeRef NOT FOUND in file $($file.Name)" -ForegroundColor Red
                            }
                        }
                        break
                    }
                }
            }
        }
    }
    
    # Handle cross-namespace type conflicts automatically
    Write-Host "Detecting cross-namespace type conflicts..." -ForegroundColor Cyan
    
    # Get all C# files and analyze cross-namespace references
    $allFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "*.cs"
    $crossNamespaceConflicts = @()
    
    foreach ($file in $allFiles) {
        $content = Get-Content $file.FullName -Raw
        $fileNamespace = ""
        $namespaceMatch = $content | Select-String -Pattern 'namespace\s+([^;]+)'
        if ($namespaceMatch) {
            $fileNamespace = $namespaceMatch.Matches[0].Groups[1].Value.Trim()
        }
        
        # Find type references in this file
        $typeReferences = @()
        $propertyMatches = [regex]::Matches($content, 'public\s+(\w+)\s+\w+\s*\{\s*get;\s*set;\s*\}', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($propertyMatch in $propertyMatches) {
            $typeName = $propertyMatch.Groups[1].Value
            if ($typeName -notin @('int', 'string', 'bool', 'DateTime', 'DateTimeOffset', 'double', 'float', 'decimal', 'object', 'List', 'Dictionary', 'Array', 'IEnumerable', 'Task', 'void', 'T', 'class')) {
                $typeReferences += $typeName
            }
        }
        
        # Check if any of these types are defined in a different namespace
        foreach ($typeRef in $typeReferences) {
            $typeDefinedIn = @()
            foreach ($otherFile in $allFiles) {
                if ($otherFile.FullName -eq $file.FullName) { continue }
                
                $otherContent = Get-Content $otherFile.FullName -Raw
                $otherNamespace = ""
                $otherNamespaceMatch = $otherContent | Select-String -Pattern 'namespace\s+([^;]+)'
                if ($otherNamespaceMatch) {
                    $otherNamespace = $otherNamespaceMatch.Matches[0].Groups[1].Value.Trim()
                }
                
                # Check if this type is defined in the other file
                if ($otherContent -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b") {
                    $typeDefinedIn += $otherNamespace
                }
            }
            
            # If type is defined in a different namespace, it's a cross-namespace conflict
            if ($typeDefinedIn.Count -gt 0 -and $fileNamespace -ne $typeDefinedIn[0]) {
                $conflict = @{
                    TypeName = $typeRef
                    ReferencedIn = $fileNamespace
                    DefinedIn = $typeDefinedIn[0]
                    ReferencedFile = $file.FullName
                    DefinedFile = ($allFiles | Where-Object { (Get-Content $_.FullName -Raw) -match "public\s+(?:static\s+)?(?:class|enum)\s+$typeRef\b" })[0].FullName
                }
                $crossNamespaceConflicts += $conflict
            }
        }
    }
    
    # Remove duplicates and process conflicts
    $uniqueConflicts = $crossNamespaceConflicts | Sort-Object TypeName | Group-Object TypeName | ForEach-Object { $_.Group[0] }
    
    foreach ($conflict in $uniqueConflicts) {
        # Check if the Common file already exists and has content
        $commonFile = Join-Path $OutputDir "Common\$($conflict.TypeName).cs"
        if (Test-Path $commonFile) {
            $existingContent = Get-Content $commonFile -Raw
            if ($existingContent.Trim() -ne "") {
                Write-Host "  Type $($conflict.TypeName) already exists in Common directory, skipping..." -ForegroundColor Green
                continue
            } else {
                Write-Host "  Type $($conflict.TypeName) exists but is empty, will recreate..." -ForegroundColor Yellow
            }
        }
        
        # Only move types that are defined in multiple namespaces (truly shared types)
        # Don't move types that are just referenced across namespaces - use using statements instead
        $allConflictsForType = $crossNamespaceConflicts | Where-Object { $_.TypeName -eq $conflict.TypeName }
        $definedNamespaces = $allConflictsForType | ForEach-Object { $_.DefinedIn } | Sort-Object | Get-Unique
        
        
        if ($definedNamespaces.Count -le 1) {
            # Type is defined in single namespace but referenced across namespaces
            # Skip creating Common copy - let using statements handle cross-namespace access
            Write-Host "Skipping Common copy for $($conflict.TypeName) - using statements will handle cross-namespace access" -ForegroundColor Yellow
            continue
        }
        
        Write-Host "Moving cross-namespace type: $($conflict.TypeName) from $($conflict.DefinedIn) to Common" -ForegroundColor Yellow
        
        # Extract the type definition from the defined file
        $definedContent = Get-Content $conflict.DefinedFile -Raw
        $typeDefinition = ""
        $lines = $definedContent -split "`n"
        $inTypeDefinition = $false
        $braceCount = 0
        $typeLines = @()
        
        foreach ($line in $lines) {
            if ($line -match "public\s+(?:static\s+)?(?:class|enum)\s+$($conflict.TypeName)\b") {
                $inTypeDefinition = $true
                $typeLines += $line
                $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                continue
            }
            
            if ($inTypeDefinition) {
                $typeLines += $line
                $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                
                if ($braceCount -eq 0) {
                    $inTypeDefinition = $false
                    break
                }
            }
        }
        
        if ($typeLines.Count -gt 0) {
            $typeDefinition = $typeLines -join "`n"
            
            # Check if this is a static class that should be converted to an enum
            if ($typeDefinition -match "public static class $($conflict.TypeName)") {
                $typeDefinition = Convert-StaticClassToEnum -TypeDefinition $typeDefinition -TypeName $($conflict.TypeName)
            }
            
            # Create Common file for this type
            $commonFile = Join-Path $OutputDir "Common\$($conflict.TypeName).cs"
            $commonContent = @"
using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

$typeDefinition
"@
            Set-Content -Path $commonFile -Value $commonContent
            Write-Host "  Created Common file: $commonFile" -ForegroundColor Green
            
            # Remove the type from the original file
            $newLines = @()
            $inTypeDefinition = $false
            $braceCount = 0
            
            foreach ($line in $lines) {
                if ($line -match "public\s+(?:static\s+)?(?:class|enum)\s+$($conflict.TypeName)\b") {
                    $inTypeDefinition = $true
                    $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                    $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                    continue
                }
                
                if ($inTypeDefinition) {
                    $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                    $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                    
                    if ($braceCount -eq 0) {
                        $inTypeDefinition = $false
                        continue
                    }
                    continue
                }
                
                $newLines += $line
            }
            
            $newContent = $newLines -join "`n"
            Set-Content -Path $conflict.DefinedFile -Value $newContent
            Write-Host "  Removed from $($conflict.DefinedIn) file: $(Split-Path $conflict.DefinedFile -Leaf)" -ForegroundColor Green
        }
    }
    
    if ($missingTypes.Count -gt 0) {
        Write-Host "Found $($missingTypes.Count) missing types: $($missingTypes -join ', ')" -ForegroundColor Yellow
        
        # Convert missing types from entity, api, and constants directories
        $entityDir = Join-Path $SeerrRootDir "server/entity"
        $apiDir = Join-Path $SeerrRootDir "server/api"
        $constantsDir = Join-Path $SeerrRootDir "server/constants"
        
        $allSourceFiles = @()
        if (Test-Path $entityDir) {
            $entityFiles = Get-ChildItem -Path $entityDir -Filter "*.ts"
            $allSourceFiles += $entityFiles
        }
        if (Test-Path $apiDir) {
            $apiFiles = Get-ChildItem -Path $apiDir -Filter "*.ts"
            $allSourceFiles += $apiFiles
        }
        if (Test-Path $constantsDir) {
            $constantsFiles = Get-ChildItem -Path $constantsDir -Filter "*.ts"
            $allSourceFiles += $constantsFiles
        }
        
        if ($allSourceFiles.Count -gt 0) {
            
            foreach ($missingType in $missingTypes) {
                # Skip blocked classes
                if ($blockedClasses -contains $missingType) {
                    Write-Host "Skipping blocked class: $missingType" -ForegroundColor Red
                    continue
                }
                
                # Find corresponding file in entity or api directories
                $sourceFile = $allSourceFiles | Where-Object { $_.BaseName -eq $missingType }
                
                # If not found by exact name, search for files that contain the missing type
                if (-not $sourceFile) {
                    foreach ($file in $allSourceFiles) {
                        $content = Get-Content $file.FullName -Raw
                        if ($content -match "enum\s+$missingType\b" -or $content -match "class\s+$missingType\b" -or $content -match "interface\s+$missingType\b") {
                            # Check if this file contains other types that already exist in Common
                            $skipFile = $false
                            $allTypesInFile = @()
                            
                            # Extract all enum/class/interface names from the file
                            $enumMatches = [regex]::Matches($content, "enum\s+(\w+)\b")
                            $classMatches = [regex]::Matches($content, "class\s+(\w+)\b")
                            $interfaceMatches = [regex]::Matches($content, "interface\s+(\w+)\b")
                            
                            foreach ($match in $enumMatches) { $allTypesInFile += $match.Groups[1].Value }
                            foreach ($match in $classMatches) { $allTypesInFile += $match.Groups[1].Value }
                            foreach ($match in $interfaceMatches) { $allTypesInFile += $match.Groups[1].Value }
                            
                            # Check if any of these types already exist in Common or Server
                            foreach ($typeInFile in $allTypesInFile) {
                                $commonTypeFile = Join-Path $OutputDir "Common\$typeInFile.cs"
                                $serverTypeFile = Join-Path $OutputDir "Server\$typeInFile.cs"
                                if ((Test-Path $commonTypeFile) -or (Test-Path $serverTypeFile)) {
                                    $existingLocation = if (Test-Path $commonTypeFile) { "Common" } else { "Server" }
                                    Write-Host "Skipping file $($file.Name) - contains type $typeInFile that already exists in $existingLocation" -ForegroundColor Yellow
                                    $skipFile = $true
                                    break
                                }
                            }
                            
                            if (-not $skipFile) {
                                $sourceFile = $file
                                Write-Host "Found $missingType in file: $($file.Name)" -ForegroundColor Cyan
                                break
                            }
                        }
                    }
                }
                
                if ($sourceFile) {
                    Write-Host "Converting missing type: $($sourceFile.Name)" -ForegroundColor Cyan
                    
                    # Determine correct output directory based on source file location
                    $targetOutputDir = Join-Path $OutputDir "Common"  # Default to Common
                    if ($sourceFile.FullName -like "*server/entity*" -or $sourceFile.FullName -like "*server\entity*") {
                        $targetOutputDir = Join-Path $OutputDir "Server"
                        Write-Host "  Entity class detected - writing to Server namespace" -ForegroundColor Cyan
                    }
                    
                    # Skip types that already exist in the target directory
                    $targetFile = Join-Path $targetOutputDir "$missingType.cs"
                    if (Test-Path $targetFile) {
                        Write-Host "Skipping $missingType - already exists in $targetOutputDir directory" -ForegroundColor Green
                        continue
                    }
                    
                    # Check if we need to extract only specific types from the file
                    $content = Get-Content $sourceFile.FullName -Raw
                    $allTypesInFile = @()
                    
                    # Extract all enum/class/interface names from the file
                    $enumMatches = [regex]::Matches($content, "enum\s+(\w+)\b")
                    $classMatches = [regex]::Matches($content, "class\s+(\w+)\b")
                    $interfaceMatches = [regex]::Matches($content, "interface\s+(\w+)\b")
                    
                    foreach ($match in $enumMatches) { $allTypesInFile += $match.Groups[1].Value }
                    foreach ($match in $classMatches) { $allTypesInFile += $match.Groups[1].Value }
                    foreach ($match in $interfaceMatches) { $allTypesInFile += $match.Groups[1].Value }
                    
                    # Check if any of these types already exist in Common
                    $typesToSkip = @()
                    foreach ($typeInFile in $allTypesInFile) {
                        $commonTypeFile = Join-Path $OutputDir "Common\$typeInFile.cs"
                        if (Test-Path $commonTypeFile) {
                            $typesToSkip += $typeInFile
                        }
                    }
                    
                    if ($typesToSkip.Count -gt 0) {
                        Write-Host "File contains types that already exist in Common: $($typesToSkip -join ', ')" -ForegroundColor Yellow
                        Write-Host "Will extract all missing types from file" -ForegroundColor Cyan
                        
                        # Create a temporary file with all missing types from the file
                        $tempContent = ""
                        $lines = $content -split "`n"
                        $inTypeDefinition = $false
                        $braceCount = 0
                        
                        foreach ($line in $lines) {
                            # Check if this line starts a new type definition
                            if ($line -match "export\s+(?:enum|class|interface)\s+(\w+)\b") {
                                $typeName = $matches[1]
                                
                                # If we were in a previous type definition, end it
                                if ($inTypeDefinition) {
                                    $tempContent += $line + "`n"
                                    $braceCount = ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count - ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                                    continue
                                }
                                
                                # Check if this type is missing (not in Common)
                                $commonTypeFile = Join-Path $OutputDir "Common\$typeName.cs"
                                if (-not (Test-Path $commonTypeFile)) {
                                    $inTypeDefinition = $true
                                    $tempContent += $line + "`n"
                                    $braceCount = ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count - ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                                    continue
                                }
                            }
                            
                            if ($inTypeDefinition) {
                                $tempContent += $line + "`n"
                                $braceCount += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
                                $braceCount -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
                                
                                if ($braceCount -eq 0) {
                                    $inTypeDefinition = $false
                                }
                            }
                        }
                        
                        # Create temporary file with all missing types
                        $tempFile = Join-Path $env:TEMP "temp_$missingType.ts"
                        $tempContent | Out-File -FilePath $tempFile -Encoding UTF8
                        
                        # Use the temporary file for conversion
                        $sourceFile = Get-Item $tempFile
                    }
                    
                    # Create a temporary directory pair for conversion
                    # Determine the correct type based on target directory
                    $dirType = if ($targetOutputDir -like "*\Server" -or $targetOutputDir -like "*/Server") { "Server" } else { "Entity" }
                    $tempDirectoryPairs = @(
                        @{
                            input = $sourceFile.DirectoryName
                            output = $targetOutputDir
                            type = $dirType
                        }
                    )
                    
                    # Prepare input data for JavaScript converter
                    $tempConverterInput = @{
                        directoryPairs = $tempDirectoryPairs
                        blockedClasses = $blockedClasses
                    }
                    
                    # Convert the specific file
                    $tempJsonInput = $tempConverterInput | ConvertTo-Json -Depth 3
                    $tempResult = $tempJsonInput | node $converterScript 2>&1
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "Successfully converted type: $($sourceFile.BaseName)" -ForegroundColor Green
                    } else {
                        Write-Host "Failed to convert type: $($sourceFile.BaseName)" -ForegroundColor Red
                        Write-Host $tempResult -ForegroundColor Red
                    }
                } else {
                    Write-Host "Source file not found for missing type: $missingType" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "Entity directory not found: $entityDir" -ForegroundColor Yellow
        }
    } else {
        Write-Host "No missing types found!" -ForegroundColor Green
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
    # Prepare input data for JavaScript converter
    $converterInput = @{
        directoryPairs = $directoryPairs
        blockedClasses = $blockedClasses
        numberToDoublePattern = $NumberToDoublePattern
    }
    $jsonInput = $converterInput | ConvertTo-Json -Depth 3
    Write-Host "Passing directory pairs to TypeScript compiler converter..." -ForegroundColor Gray
    
    # Run the TypeScript compiler converter with JSON input
    $result = $jsonInput | node $converterScript 2>&1
    
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nConversion completed successfully!" -ForegroundColor Green
        Write-Host $result -ForegroundColor Cyan
        
        # Detect and convert missing entity types
        Write-Host "`nDetecting missing entity types..." -ForegroundColor Yellow
        Convert-MissingEntityTypes
        
        
    } else {
        Write-Host "`nConversion failed with exit code: $LASTEXITCODE" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
    }
    
} catch {
    Write-Host "`nError running converter: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Processing TypeScript Imports ===" -ForegroundColor Green

# Process TypeScript imports and add corresponding C# using statements
function Invoke-TypeScriptImports {
    Write-Host "Processing TypeScript imports to add C# using statements..." -ForegroundColor Cyan
    
    # Get all TypeScript files that were converted
    $tsFiles = @()
    $tsFiles += Get-ChildItem -Path "$SeerrRootDir/server/interfaces/api" -Filter "*.ts" -Recurse
    $tsFiles += Get-ChildItem -Path "$SeerrRootDir/server/models" -Filter "*.ts" -Recurse
    
    foreach ($tsFile in $tsFiles) {
        $content = Get-Content $tsFile.FullName -Raw
        $importMatches = [regex]::Matches($content, "import\s+(?:type\s+)?\{([^}]+)\}\s+from\s+['""]([^'""]+)['""]")
        
        if ($importMatches.Count -gt 0) {
            Write-Host "Processing imports in: $($tsFile.Name)" -ForegroundColor Yellow
            Write-Host "  Found $($importMatches.Count) import matches" -ForegroundColor Gray
            
            # Determine the corresponding C# file
            $csharpFile = $null
            Write-Host "  OutputDir: $OutputDir" -ForegroundColor Gray
            if ($tsFile.FullName -like "*server\interfaces\api*" -or $tsFile.FullName -like "*server/interfaces/api*") {
                $relativePath = $tsFile.Name -replace '\.ts$', '.cs'
                $csharpFile = Join-Path $OutputDir "Api/$relativePath"
                Write-Host "  Mapped to Api file: $csharpFile" -ForegroundColor Gray
            } elseif ($tsFile.FullName -like "*server\models*" -or $tsFile.FullName -like "*server/models*") {
                $relativePath = $tsFile.Name -replace '\.ts$', '.cs'
                $csharpFile = Join-Path $OutputDir "Server/$relativePath"
                Write-Host "  Mapped to Server file: $csharpFile" -ForegroundColor Gray
            } else {
                Write-Host "  No path match for: $($tsFile.FullName)" -ForegroundColor Gray
            }
            
            if ($csharpFile -and (Test-Path $csharpFile)) {
                Write-Host "  Found C# file: $csharpFile" -ForegroundColor Gray
                $csharpContent = Get-Content $csharpFile -Raw
                $needsUpdate = $false
                
                foreach ($match in $importMatches) {
                    $importPath = $match.Groups[2].Value
                    
                    # Map TypeScript import paths to C# namespaces
                    $csharpNamespace = ""
                    if ($importPath -like "@server/models/*") {
                        $csharpNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server"
                    } elseif ($importPath -like "@server/interfaces/api/*") {
                        $csharpNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api"
                    } elseif ($importPath -like "@server/entity/*") {
                        $csharpNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel"
                    }
                    
                    if ($csharpNamespace -and $csharpNamespace -ne "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel") {
                        # Check if using statement already exists
                        if ($csharpContent -notmatch "using\s+$([regex]::Escape($csharpNamespace));") {
                            # Add using statement after existing using statements
                            $usingStatement = "using $csharpNamespace;"
                            $csharpContent = $csharpContent -replace "(using[^;]+;[\r\n]+)+", "`$&$usingStatement`n"
                            $needsUpdate = $true
                            Write-Host "  Added using: $usingStatement" -ForegroundColor Green
                        }
                    }
                }
                
                if ($needsUpdate) {
                    Set-Content -Path $csharpFile -Value $csharpContent
                    Write-Host "  Updated: $csharpFile" -ForegroundColor Green
                }
            }
        }
    }
}

Invoke-TypeScriptImports

# Add automatic using statements for cross-namespace references
Write-Host "`n=== Adding Cross-Namespace Using Statements ===" -ForegroundColor Green
function Add-CrossNamespaceUsingStatements {
    Write-Host "Adding cross-namespace using statements..." -ForegroundColor Cyan
    
    $allCSharpFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "*.cs"
    
    foreach ($file in $allCSharpFiles) {
        $content = Get-Content $file.FullName -Raw
        $originalContent = $content
        
        # Determine the namespace of this file
        $fileNamespace = ""
        if ($file.FullName -match "\\Server\\") {
            $fileNamespace = "Server"
        } elseif ($file.FullName -match "\\Api\\") {
            $fileNamespace = "Api"
        } else {
            $fileNamespace = "Common"
        }
        
        # Special case: if file is in Server directory but has base namespace, it's actually in Server namespace
        if ($file.FullName -match "\\Server\\" -and $content -match "namespace Jellyfin\.Plugin\.JellyseerrBridge\.JellyseerrModel;") {
            $fileNamespace = "Server"
        }
        
        # Find all class references in the file by looking for type names in property declarations
        # This regex finds property declarations and extracts the type name
        $classReferences = [regex]::Matches($content, 'public\s+([^;]+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        
        $neededUsings = @()
        
        foreach ($match in $classReferences) {
            $propertyType = $match.Groups[1].Value.Trim()
            
            # Extract the main type name from complex types like List<T>, Dictionary<K,V>, etc.
            $className = $propertyType
            
            # Handle generic types like List<SomeType> -> SomeType
            $listMatch = [regex]::Match($className, 'List<([^>]+)>')
            if ($listMatch.Success) {
                $className = $listMatch.Groups[1].Value.Trim()
            } else {
                $dictMatch = [regex]::Match($className, 'Dictionary<[^,]*,\s*([^>]+)>')
                if ($dictMatch.Success) {
                    $className = $dictMatch.Groups[1].Value.Trim()
                } else {
                    $enumMatch = [regex]::Match($className, 'IEnumerable<([^>]+)>')
                    if ($enumMatch.Success) {
                        $className = $enumMatch.Groups[1].Value.Trim()
                    }
                }
            }
            
            # Skip built-in types
            if ($className -in @('int', 'string', 'bool', 'DateTime', 'DateTimeOffset', 'double', 'float', 'decimal', 'object', 'List', 'Dictionary', 'Array', 'IEnumerable', 'Task', 'void', 'T', 'class')) {
                continue
            }
            
            # Determine which namespace this class is defined in by searching all files
            $classNamespace = ""
            $allFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "*.cs"
            $foundNamespaces = @()
            
            foreach ($searchFile in $allFiles) {
                $searchContent = Get-Content $searchFile.FullName -Raw
                if ($searchContent -match "public\s+(?:static\s+)?(?:class|enum)\s+$className\b") {
                    # Determine namespace from file path
                    if ($searchFile.FullName -like "*\Server\*") {
                        $foundNamespaces += "Server"
                    } elseif ($searchFile.FullName -like "*\Api\*") {
                        $foundNamespaces += "Api"
                    } elseif ($searchFile.FullName -like "*\Common\*") {
                        $foundNamespaces += "Common"
                    } else {
                        $foundNamespaces += "Common"  # Default to Common for base namespace
                    }
                }
            }
            
            # If class exists in multiple namespaces, prefer Common for cross-namespace access
            if ($foundNamespaces.Count -gt 1) {
                $classNamespace = "Common"
            } elseif ($foundNamespaces.Count -eq 1) {
                $classNamespace = $foundNamespaces[0]
            }
            
            # If the class is in a different namespace than the current file, add using statement
            if ($classNamespace -ne "" -and $classNamespace -ne $fileNamespace) {
                if ($classNamespace -eq "Common") {
                    # For Common namespace classes, use the base namespace
                    $usingStatement = "using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;"
                } else {
                    $usingStatement = "using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.$classNamespace;"
                }
                if ($content -notmatch [regex]::Escape($usingStatement)) {
                    $neededUsings += $usingStatement
                }
            }
        }
        
        # Add unique using statements
        $neededUsings = $neededUsings | Sort-Object -Unique
        
        if ($neededUsings.Count -gt 0) {
            # Find the last using statement
            $usingMatches = [regex]::Matches($content, 'using\s+[^;]+;')
            if ($usingMatches.Count -gt 0) {
                $lastUsingEnd = $usingMatches[$usingMatches.Count - 1].Index + $usingMatches[$usingMatches.Count - 1].Length
                $beforeUsings = $content.Substring(0, $lastUsingEnd)
                $afterUsings = $content.Substring($lastUsingEnd)
                
                # Add new using statements
                $newUsings = ""
                foreach ($using in $neededUsings) {
                    $newUsings += "`n$using"
                }
                
                $content = $beforeUsings + $newUsings + $afterUsings
                
                if ($content -ne $originalContent) {
                    Set-Content -Path $file.FullName -Value $content -NoNewline
                    Write-Host "Added using statements to: $($file.Name)" -ForegroundColor Green
                    foreach ($using in $neededUsings) {
                        Write-Host "  + $using" -ForegroundColor Yellow
                    }
                }
            }
        }
    }
}

Add-CrossNamespaceUsingStatements

# Note: CS0108 hiding warnings are best handled by the compiler
# The compiler will tell us exactly which properties need the 'new' keyword
# Attempting to programmatically detect inheritance hierarchies is error-prone

# Apply Error to Exception mapping to all generated files
Write-Host "`n=== Applying Error to Exception Mapping ===" -ForegroundColor Green
$allCSharpFiles = Get-ChildItem -Path $OutputDir -Recurse -Filter "*.cs"
foreach ($file in $allCSharpFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Replace Error with Exception in class inheritance
    $content = $content -replace ': Error\b', ': Exception'
    
    Write-Host "DEBUG: File $($file.Name) - Original length: $($originalContent.Length), New length: $($content.Length)" -ForegroundColor Gray
    Write-Host "DEBUG: Content changed: $($content -ne $originalContent)" -ForegroundColor Gray
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content
        Write-Host "Applied Error->Exception mapping to: $($file.Name)" -ForegroundColor Cyan
    }
}

# Analyze inheritance relationships and add 'new' keyword to properties that actually hide inherited members
Write-Host "`n=== Analyzing inheritance relationships and adding 'new' keyword ===" -ForegroundColor Green

# First, remove any existing 'new' keywords to start clean
Write-Host "Removing existing 'new' keywords..." -ForegroundColor Cyan
foreach ($file in $allCSharpFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $originalContent = $content
    
    # Remove existing 'new' keywords from property declarations
    $content = $content -replace '\s+public\s+new\s+', ' public '
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Removed existing 'new' keywords from: $($file.Name)" -ForegroundColor Gray
    }
}

# First pass: Build inheritance map
$inheritanceMap = @{}
$classProperties = @{}

Write-Host "Building inheritance map..." -ForegroundColor Cyan

foreach ($file in $allCSharpFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    
    # Find all class declarations and their inheritance relationships
    $classMatches = [regex]::Matches($content, 'public\s+class\s+(\w+)(?:\s*:\s*(\w+))?')
    
    foreach ($match in $classMatches) {
        $className = $match.Groups[1].Value
        $baseClassName = $match.Groups[2].Value
        
        if ($baseClassName) {
            $inheritanceMap[$className] = $baseClassName
            Write-Host "Found inheritance: $className : $baseClassName" -ForegroundColor Gray
        }
        
        # Find all properties in this class
        # Extract class content by finding the matching closing brace
        $classStart = $match.Index
        $braceCount = 0
        $inClass = $false
        $classEnd = $classStart
        
        for ($i = $classStart; $i -lt $content.Length; $i++) {
            $char = $content[$i]
            if ($char -eq '{') {
                $braceCount++
                $inClass = $true
            }
            elseif ($char -eq '}') {
                $braceCount--
                if ($inClass -and $braceCount -eq 0) {
                    $classEnd = $i + 1
                    break
                }
            }
        }
        
        if ($classEnd -le $classStart) { $classEnd = $content.Length }
        
        $classContent = $content.Substring($classStart, $classEnd - $classStart)
        $propertyMatches = [regex]::Matches($classContent, 'public\s+([A-Za-z0-9_<>,\s?]+)\s+(\w+)\s*\{\s*get;\s*set;')
        
        $properties = @{}
        foreach ($propMatch in $propertyMatches) {
            $propType = $propMatch.Groups[1].Value.Trim()
            $propName = $propMatch.Groups[2].Value
            $properties[$propName] = $propType
        }
        
        $classProperties[$className] = $properties
    }
}

# Second pass: Find properties that actually hide inherited members and add 'new' keyword
foreach ($file in $allCSharpFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $modified = $false
    
    # Find all class declarations in this file
    $classMatches = [regex]::Matches($content, 'public\s+class\s+(\w+)(?:\s*:\s*(\w+))?')
    
    foreach ($match in $classMatches) {
        $className = $match.Groups[1].Value
        $baseClassName = $match.Groups[2].Value
        
        if ($baseClassName -and $classProperties.ContainsKey($className) -and $classProperties.ContainsKey($baseClassName)) {
            # This class inherits from another class
            $currentClassProps = $classProperties[$className]
            $baseClassProps = $classProperties[$baseClassName]
            
            # Check for property hiding
            foreach ($propName in $currentClassProps.Keys) {
                if ($baseClassProps.ContainsKey($propName)) {
                    # Property exists in both classes - this is hiding!
                    $propType = $currentClassProps[$propName]
                    
                    # Create pattern to match this specific property declaration WITHIN the current class only
                    # Extract the current class content first to ensure we only modify the right class
                    $escapedClassName = [regex]::Escape($className)
                    $escapedPropName = [regex]::Escape($propName)
                    $escapedPropType = [regex]::Escape($propType)
                    
                    # Find the class block for the current class
                    $classPattern = "(?s)(public\s+class\s+$escapedClassName\b[^{]*\{)(.*?)(\npublic\s+(?:class|enum)\s|\z)"
                    if ($content -match $classPattern) {
                        $classPrefix = $Matches[1]
                        $classBody = $Matches[2]
                        $classSuffix = $Matches[3]
                        
                        # Now find and modify the property within the class body
                        $propPattern = "(public\s+)($escapedPropType)(\s+$escapedPropName\s*\{[^}]*\}[^;]*;)"
                        if ($classBody -match $propPattern) {
                            $newClassBody = $classBody -replace $propPattern, '$1new $2$3'
                            if ($newClassBody -ne $classBody) {
                                # Replace the entire class block with the modified version
                                $newClassBlock = $classPrefix + $newClassBody + $classSuffix
                                $oldClassBlock = $classPrefix + $classBody + $classSuffix
                                $content = $content.Replace($oldClassBlock, $newClassBlock)
                                $modified = $true
                                Write-Host "Added 'new' keyword to $className.$propName (hides $baseClassName.$propName) in: $($file.Name)" -ForegroundColor Gray
                            }
                        }
                    }
                }
            }
        }
    }
    
    if ($modified) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
    }
}

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

Write-Host "`n=== Conversion Complete ===" -ForegroundColor Green

# Run model validation using check-models script (moved to very end)
Write-Host "`nRunning model validation..." -ForegroundColor Yellow
$checkScript = Join-Path $scriptDir "check-models.ps1"
if (Test-Path $checkScript) {
    & $checkScript -ModelDir $OutputDir
} else {
    Write-Host "check-models.ps1 not found, skipping validation" -ForegroundColor Yellow
}
