# TypeScript Permissions Enum to C# Converter

param(
    [string]$SourcePath = "codebase/seerr-main/server/lib/permissions.ts",
    [string]$OutputPath = "src/Jellyfin.Plugin.JellyBridge/JellyseerrModel/Server/Permissions.cs"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path $ScriptDir -Parent

Push-Location $ProjectRoot

try {
    if (-not (Test-Path $SourcePath)) {
        throw "Source file not found: $SourcePath"
    }

    $source = Get-Content $SourcePath -Raw
    $enumMatch = [regex]::Match($source, 'export\s+enum\s+Permission\s*\{(?<body>[\s\S]*?)\}', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $enumMatch.Success) {
        throw "Could not find exported Permission enum in $SourcePath"
    }

    $hasPermissionFunction = $source -match 'export\s+const\s+hasPermission\s*=\s*\('

    $body = $enumMatch.Groups['body'].Value
    $members = @()

    foreach ($line in $body -split "`r?`n") {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if ($trimmed.StartsWith('//')) { continue }

        $memberMatch = [regex]::Match($trimmed, '^(?<name>[A-Z0-9_]+)\s*(=\s*(?<value>-?\d+))?\s*,?\s*(//.*)?$')
        if (-not $memberMatch.Success) {
            throw "Unrecognized enum member line: $trimmed"
        }

        $name = $memberMatch.Groups['name'].Value
        $value = $memberMatch.Groups['value'].Value
        if ([string]::IsNullOrWhiteSpace($value)) {
            $members += "    $name"
        } else {
            $members += "    $name = $value"
        }
    }

    $helperBlock = @"

public sealed class PermissionCheckOptions
{
    public string Type { get; init; } = "and";
}

public static class PermissionHelper
{
    public static bool HasPermission(Permission permissions, int value)
    {
        if (permissions == Permission.NONE)
        {
            return true;
        }

        return IsAdmin(value) || HasFlag(value, permissions);
    }

    public static bool HasPermission(IReadOnlyCollection<Permission> permissions, int value, PermissionCheckOptions? options = null)
    {
        options ??= new PermissionCheckOptions();

        if (permissions.Count == 0)
        {
            return options.Type.Equals("and", StringComparison.OrdinalIgnoreCase);
        }

        if (IsAdmin(value))
        {
            return true;
        }

        return options.Type.Equals("and", StringComparison.OrdinalIgnoreCase)
            ? permissions.All(permission => HasFlag(value, permission))
            : permissions.Any(permission => HasFlag(value, permission));
    }

    private static bool IsAdmin(int value)
    {
        return HasFlag(value, Permission.ADMIN);
    }

    private static bool HasFlag(int value, Permission permission)
    {
        return (value & (int)permission) != 0;
    }
}
"@

    $content = @"
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;

public enum Permission
{
$($members -join ",`n")
}
$([string]::Join("", ($(if ($hasPermissionFunction) { $helperBlock } else { "" }))))
"@

    $outputDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    Set-Content -Path $OutputPath -Value $content -NoNewline
    Write-Host "Generated $OutputPath from $SourcePath" -ForegroundColor Green
}
finally {
    Pop-Location
}