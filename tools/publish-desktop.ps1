#Requires -Version 7
<#
.SYNOPSIS
    Publishes the Windows companion app as one self-contained executable.

.DESCRIPTION
    Self-contained so the .exe can be copied to a machine with no .NET runtime installed.

    Never add -p:PublishTrimmed=true. The SignalR client serialises hub arguments through
    reflection-based System.Text.Json, which trimming removes with no build-time complaint:
    the app publishes, starts, sits in the tray, and then fails on the first key press.
#>
param([string]$Output = "dist/desktop")

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet publish src/SemperSounds.Desktop/SemperSounds.Desktop.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

Write-Output "Published to $Output/SemperSounds.Desktop.exe"
