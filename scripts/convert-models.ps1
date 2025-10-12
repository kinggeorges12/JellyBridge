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
                    csharpCode += convertInterfaceOrClassToClass(interfaceNode, sourceFile, missingTypes) + '\n\n';
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
            
            csharpClass += '    public ' + propType + isOptional + ' ' + toPascalCase(propName) + ' { get; set; }\n\n';
        }
    });
    
    // Handle omitted properties from Omit<T, K> inheritance
    if (global.omittedProperties && global.omittedProperties[interfaceName]) {
        const omitInfo = global.omittedProperties[interfaceName];
        
        // Add comment explaining the Omit
        csharpClass += '\n    // TypeScript: ' + omitInfo.originalText + '\n';
        
        // Extract omitted property names and make them nullable
        if (omitInfo.omittedProps.kind === ts.SyntaxKind.LiteralType) {
            const omittedPropName = omitInfo.omittedProps.literal.text;
            csharpClass += '    public object? ' + toPascalCase(omittedPropName) + ' { get; set; } // Omitted from base type\n\n';
        } else if (omitInfo.omittedProps.kind === ts.SyntaxKind.UnionType) {
            // Handle union of omitted properties
            omitInfo.omittedProps.types.forEach(typeNode => {
                if (typeNode.kind === ts.SyntaxKind.LiteralType) {
                    const omittedPropName = typeNode.literal.text;
                    csharpClass += '    public object? ' + toPascalCase(omittedPropName) + ' { get; set; } // Omitted from base type\n\n';
                }
            });
        }
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
               '    public T Value { get; set; }\n' +
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
            
            // Generate enum and return it (will be added to transpiled types when written to file)
            const enumClass = 'public enum ' + finalTypeName + '\n{\n' +
                enumValues.map(value => '    ' + toPascalCase(value)).join(',\n') + '\n}';
            console.log('Generated ' + finalTypeName + ' locally');
            return enumClass;  // Return the enum directly
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

# Function to generate consistent enum format
function Generate-EnumFromConstants {
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
        $enumDefinition = Generate-EnumFromConstants -EnumName $TypeName -Constants $constants
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
            $serverNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server"
            $apiNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api"
            $commonNamespace = "Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel"
            
            if (($typeDefinedIn -contains $serverNamespace -and $typeReferencedIn -contains $apiNamespace) -or
                ($typeDefinedIn -contains $apiNamespace -and $typeReferencedIn -contains $serverNamespace)) {
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
        # Check if the Common file already exists
        $commonFile = Join-Path $OutputDir "Common\$($conflict.TypeName).cs"
        if (Test-Path $commonFile) {
            Write-Host "  Type $($conflict.TypeName) already exists in Common directory, skipping..." -ForegroundColor Green
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
                # Find corresponding file in entity or api directories
                $sourceFile = $allSourceFiles | Where-Object { $_.BaseName -eq $missingType }
                if ($sourceFile) {
                    Write-Host "Converting missing type: $($sourceFile.Name)" -ForegroundColor Cyan
                    
                    # Create a temporary directory pair for conversion
                    $tempDirectoryPairs = @(
                        @{
                            input = $sourceFile.DirectoryName
                            output = "$OutputDir/Common"
                            type = "Entity"
                        }
                    )
                    
                    # Convert the specific file
                    $tempJsonInput = $tempDirectoryPairs | ConvertTo-Json -Depth 3
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
    $jsonInput = $directoryPairs | ConvertTo-Json -Depth 3
    Write-Host "Passing directory pairs to TypeScript compiler converter..." -ForegroundColor Gray
    
    # Run the TypeScript compiler converter with JSON input
    $result = $jsonInput | node $converterScript 2>&1
    
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nConversion completed successfully!" -ForegroundColor Green
        Write-Host $result -ForegroundColor Cyan
        
        # Detect and convert missing entity types
        Write-Host "`nDetecting missing entity types..." -ForegroundColor Yellow
        Convert-MissingEntityTypes
        
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
