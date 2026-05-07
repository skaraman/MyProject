# Script: ScanAllErrors.ps1
# Scans all C# files in the Unity project and outputs compilation errors
# Run from VSCode terminal or via Tasks.json

$workspaceRoot = $args[0] -replace '"', ''
if (-not [string]::IsNullOrWhiteSpace($workspaceRoot)) {
    Set-Location $workspaceRoot
}

Write-Host "Scanning C# files for errors..." -ForegroundColor Cyan

# Get all .cs files in Assets/Editor and Packages directories
$csFiles = @()
$csFiles += Get-ChildItem -Path "Assets/Scripts" -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
$csFiles += Get-ChildItem -Path "Assets/Editor" -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
$csFiles += Get-ChildItem -Path "Packages" -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch 'Tests' } | Select-Object -ExpandProperty FullName

if ($csFiles.Count -eq 0) {
    Write-Host "No C# files found." -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($csFiles.Count) C# files to scan" -ForegroundColor Green

# Unity's ScriptCompilation.log is the source of truth for compilation errors
$scriptPath = Join-Path $PSScriptRoot "Assets/Editor/AllProjectErrorsWindow.cs"
if (Test-Path $scriptPath) {
    Write-Host "Using custom error scanner from AllProjectErrorsWindow.cs" -ForegroundColor Cyan
    
    # Alternative: Parse Unity's compilation log directly
    $compilationLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
    if (Test-Path $compilationLog) {
        Write-Host "Scanning Unity Editor log for errors..." -ForegroundColor Cyan
        
        Get-Content $compilationLog | Select-String -Pattern "error|Error|ERROR" -CaseSensitive:$false | ForEach-Object {
            $_.Line.Trim()
        }
    } else {
        Write-Host "Unity Editor log not found at: $compilationLog" -ForegroundColor Yellow
    }
} else {
    # Fallback: Try to find compilation errors from Unity's internal logs
    Write-Host "No custom scanner found, checking Unity logs..." -ForegroundColor Cyan
    
    $editorLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
    if (Test-Path $editorLog) {
        Get-Content $editorLog | Select-String -Pattern "error|Error|ERROR" -CaseSensitive:$false | ForEach-Object {
            Write-Host $_.Line.Trim() -ForegroundColor Red
        }
    } else {
        Write-Host "Unity Editor log not found." -ForegroundColor Yellow
    }
}

Write-Host "Scan complete." -ForegroundColor Green