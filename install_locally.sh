#!/usr/bin/env bash
#------------------------------------------------------------------------------
# Pack a .NET solution (or current folder), generate a LocalOnly.NuGet.Config
# inside the feed directory (so it won't affect the solution), and install
# (or reinstall) the Uno.Check global tool from that feed.
#
# Usage:
#   ./pack-and-install.sh [Solution] [LocalFeed]
#
#   Solution:  Optional path to a .sln file or project folder.
#              If omitted, dotnet pack runs on the current directory.
#   LocalFeed: Optional directory to emit nupkg files into.
#              If omitted, defaults to 'LocalFeed' in the script folder.
#------------------------------------------------------------------------------

set -euo pipefail

PackageId="Uno.Check"

# Determine script directory
scriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$scriptDir"

Solution="${1:-}"
LocalFeed="${2:-}"

if [[ -z "$Solution" ]]; then
    echo "No solution specified → packing current directory"
    packTarget="."
else
    packTarget="$Solution"
fi

if [[ -z "$LocalFeed" ]]; then
    LocalFeed="LocalFeed"
    echo "No feed directory specified → using default '$LocalFeed'"
fi

# Ensure feed directory exists
if [[ ! -d "$LocalFeed" ]]; then
    echo "Creating feed directory: $LocalFeed"
    mkdir -p "$LocalFeed"
fi

echo "Packing '$packTarget' to '$LocalFeed'..."
dotnet pack "$packTarget" -c Release -o "$LocalFeed"

# Place NuGet.Config inside the feed directory
configFile="$LocalFeed/LocalOnly.NuGet.Config"
feedUri="$(cd "$LocalFeed" && pwd)"

echo "Generating NuGet.Config at '$configFile'..."
cat > "$configFile" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <!-- only your local folder -->
    <add key="LocalFeed" value="$feedUri" />
  </packageSources>
</configuration>
EOF

# If Uno.Check is already installed, uninstall it first
if dotnet tool list --global | grep -q "^\s*$PackageId\s"; then
    echo "Detected existing installation of '$PackageId'. Uninstalling..."
    dotnet tool uninstall --global "$PackageId"
fi

echo "Installing tool '$PackageId' using only '$configFile'..."
dotnet tool install --global "$PackageId" \
    --configfile "$configFile" \
    --ignore-failed-sources

echo
echo "Done. '$PackageId' is installed from your local feed."

# Prevent the script window from closing immediately
echo
read -p "Press Enter to exit..."