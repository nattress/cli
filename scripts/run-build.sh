#!/usr/bin/env bash
#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

set -e

SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do # resolve $SOURCE until the file is no longer a symlink
  DIR="$( cd -P "$( dirname "$SOURCE" )" && pwd )"
  SOURCE="$(readlink "$SOURCE")"
  [[ "$SOURCE" != /* ]] && SOURCE="$DIR/$SOURCE" # if $SOURCE was a relative symlink, we need to resolve it relative to the path where the symlink file was located
done
DIR="$( cd -P "$( dirname "$SOURCE" )" && pwd )"

# Load Branch Info
while read line; do
    if [[ $line != \#* ]]; then
        IFS='=' read -ra splat <<< "$line"
        export ${splat[0]}="${splat[1]}"
    fi
done < "$DIR/../branchinfo.txt"

# Use a repo-local install directory (but not the artifacts directory because that gets cleaned a lot
[ -z "$DOTNET_INSTALL_DIR" ] && export DOTNET_INSTALL_DIR=$DIR/../.dotnet_stage0/$(uname)
[ -d $DOTNET_INSTALL_DIR ] || mkdir -p $DOTNET_INSTALL_DIR

# Ensure the latest stage0 is installed
CHANNEL=$RELEASE_SUFFIX $DIR/obtain/install.sh

# Put it on the PATH (for this shell only)
PATH="$DOTNET_INSTALL_DIR/cli/bin;$PATH"

# Increases the file descriptors limit for this bash. It prevents an issue we were hitting during restore
FILE_DESCRIPTOR_LIMIT=$( ulimit -n )
if [ $FILE_DESCRIPTOR_LIMIT -lt 1024 ]
then
    echo "Increasing file description limit to 1024"
    ulimit -n 1024
fi

# Restore the build scripts
echo "Restoring Build Script projects..."
(
    cd $DIR
    if [[ "$VERBOSE" == "1" ]]; then
        dotnet restore
    else
        dotnet restore > /dev/null
    fi
)

# Build the builder
echo "Compiling Build Scripts..."
if [[ "$VERBOSE" == "1" ]]; then
    dotnet build "$DIR/dotnet-cli-build"
else
    dotnet build "$DIR/dotnet-cli-build" >/dev/null
fi

# Run the builder
echo "Invoking Build Scripts..."
DOTNET_HOME="$DOTNET_INSTALL_DIR/share/dotnet/cli" $DIR/dotnet-cli-build/bin/Debug/dnxcore50/dotnet-cli-build "$@"
