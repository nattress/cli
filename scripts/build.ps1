#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

# Load Branch Info
cat "$PSScriptRoot\..\branchinfo.txt" | ForEach-Object {
    if(!$_.StartsWith("#") -and ![String]::IsNullOrWhiteSpace($_)) {
        $splat = $_.Split([char[]]@("="), 2)
        Set-Content "env:\$($splat[0])" -Value $splat[1]
    }
}

$env:CHANNEL=$env:RELEASE_SUFFIX

# Doesn't work yet because dotnet-cli-build interprets it as a target name :)
#if(($args -contains "-v") -or ($args -contains "--verbose")) {
    #$Verbose = $true
#}

# Use a repo-local install directory (but not the artifacts directory because that gets cleaned a lot
if (!$env:DOTNET_INSTALL_DIR)
{
    $env:DOTNET_INSTALL_DIR="$PSScriptRoot\..\.dotnet_stage0\Windows"
}

if (!(Test-Path $env:DOTNET_INSTALL_DIR))
{
    mkdir $env:DOTNET_INSTALL_DIR | Out-Null
}

# Install a stage 0
Write-Host "Installing .NET Core CLI Stage 0 from $env:CHANNEL channel"
& "$PSScriptRoot\obtain\install.ps1" -Channel $env:CHANNEL

# Put the stage0 on the path
$env:PATH = "$env:DOTNET_INSTALL_DIR\cli\bin;$env:PATH"

# Restore the build scripts
Write-Host "Restoring Build Script projects..."
pushd $PSScriptRoot
if($Verbose) {
    dotnet restore
    if($LASTEXITCODE -ne 0) { throw "Failed to restore" }
} else {
    $result = dotnet restore
    if($LASTEXITCODE -ne 0) { $result | ForEach-Object { Write-Host $_ }; throw "Failed to restore" }
}
popd

# Build the builder
Write-Host "Compiling Build Scripts..."
if($Verbose) {
    dotnet build "$PSScriptRoot\dotnet-cli-build"
    if($LASTEXITCODE -ne 0) { throw "Failed to compile build scripts" }
} else {
    $result = dotnet build "$PSScriptRoot\dotnet-cli-build"
    if($LASTEXITCODE -ne 0) { $result | ForEach-Object { Write-Host $_ }; throw "Failed to compile build scripts" }
}

# Run the builder
Write-Host "Invoking Build Scripts..."
$env:DOTNET_HOME="$env:DOTNET_INSTALL_DIR\cli"
& "$PSScriptRoot\dotnet-cli-build\bin\Debug\dnxcore50\dotnet-cli-build.exe" @args
