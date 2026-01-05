# Justfile for Flappy

# Default target
default: build

# Build the project (Release mode, Native AOT enabled in .fsproj)
build:
    dotnet publish -c Release -o dist

# Install (Windows)
[windows]
install: build
    #!powershell
    $dest = "$env:USERPROFILE\.local\bin"
    if (-not (Test-Path $dest)) { 
        New-Item -ItemType Directory -Path $dest -Force | Out-Null 
    }
    Copy-Item "dist\flappy.exe" -Destination "$dest\flappy.exe" -Force
    Write-Host "Installed flappy to $dest"

# Install (Unix: Linux & macOS)
[unix]
install: build
    mkdir -p ~/.local/bin
    cp dist/flappy ~/.local/bin/
    echo "Installed flappy to ~/.local/bin"

# Clean build artifacts (Windows)
[windows]
clean:
    dotnet clean
    #!powershell
    if (Test-Path dist) { Remove-Item -Recurse -Force dist }
    if (Test-Path dist_aot) { Remove-Item -Recurse -Force dist_aot }

# Clean build artifacts (Unix)
[unix]
clean:
    dotnet clean
    rm -rf dist dist_aot
