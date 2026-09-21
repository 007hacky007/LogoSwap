#!/bin/bash
#
# LogoSwap Build Script
# Builds the plugin and packages it for distribution
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Configuration
PROJECT_NAME="LogoSwap"
VERSION="${1:-1.0.0}"
OUTPUT_DIR="./artifacts"
BUILD_CONFIG="Release"

echo -e "${CYAN}╔════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║       LogoSwap Build Script            ║${NC}"
echo -e "${CYAN}╚════════════════════════════════════════╝${NC}"
echo ""

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --version) VERSION="$2"; shift ;;
        -h|--help)
            echo "Usage: ./build.sh [VERSION]"
            echo ""
            echo "Examples:"
            echo "  ./build.sh 1.0.0"
            echo "  ./build.sh --version 1.2.0"
            exit 0
            ;;
        *) 
            if [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
                VERSION="$1"
            fi
            ;;
    esac
    shift
done

echo -e "${YELLOW}Building version: ${VERSION}${NC}"
echo ""

# Clean previous builds
echo -e "${YELLOW}[1/5] Cleaning previous builds...${NC}"
rm -rf ./bin ./obj "$OUTPUT_DIR"
dotnet clean -c "$BUILD_CONFIG" --nologo -v q 2>/dev/null || true

# Restore packages
echo -e "${YELLOW}[2/5] Restoring NuGet packages...${NC}"
dotnet restore --nologo -v q

# Build the project
echo -e "${YELLOW}[3/5] Building ${PROJECT_NAME}...${NC}"
dotnet build -c "$BUILD_CONFIG" --nologo -v q \
    /p:Version="$VERSION" \
    /p:AssemblyVersion="${VERSION}.0" \
    /p:FileVersion="${VERSION}.0"

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Package the plugin, once per target framework
echo -e "${YELLOW}[4/5] Packaging plugin...${NC}"

# Jellyfin ships one plugin DLL per server ABI: 10.11 runs .NET 9, 12.0 runs
# .NET 10. Read the ABI for each target framework out of the project so this
# does not need editing on the next bump.
TARGET_FRAMEWORKS=$(dotnet msbuild "$PROJECT_NAME.csproj" -getProperty:TargetFrameworks | tr ';' ' ')

md5_of() {
    if command -v md5sum &> /dev/null; then
        md5sum "$1" | awk '{print $1}'
    elif command -v md5 &> /dev/null; then
        md5 -q "$1"
    else
        echo "(md5 command not found)"
    fi
}

ZIP_FILES=()
TARGET_ABIS=()
CHECKSUMS=()

for TFM in $TARGET_FRAMEWORKS; do
    ABI="$(dotnet msbuild "$PROJECT_NAME.csproj" -p:TargetFramework="$TFM" -getItem:PackageReference \
        | jq -r '.Items.PackageReference[] | select(.Identity=="Jellyfin.Controller") | .Version').0"
    # 10.11.0.0 -> jf10.11, 12.0.0.0 -> jf12.0
    LABEL="jf$(echo "$ABI" | cut -d. -f1-2)"

    TEMP_DIR=$(mktemp -d)
    PLUGIN_DIR="$TEMP_DIR/$PROJECT_NAME"
    mkdir -p "$PLUGIN_DIR"

    cp "./bin/$BUILD_CONFIG/$TFM/$PROJECT_NAME.dll" "$PLUGIN_DIR/"
    cp "./bin/$BUILD_CONFIG/$TFM/image.png" "$PLUGIN_DIR/image.png"

    ZIP_FILE="$OUTPUT_DIR/${PROJECT_NAME}_${VERSION}_${LABEL}.zip"
    (cd "$TEMP_DIR" && zip -rq "$PROJECT_NAME.zip" "$PROJECT_NAME")
    mv "$TEMP_DIR/$PROJECT_NAME.zip" "$ZIP_FILE"
    rm -rf "$TEMP_DIR"

    echo "  $TFM -> $ZIP_FILE (targetAbi $ABI)"

    ZIP_FILES+=("$ZIP_FILE")
    TARGET_ABIS+=("$ABI")
    CHECKSUMS+=("$(md5_of "$ZIP_FILE")")
done

echo -e "${YELLOW}[5/5] Generating checksums...${NC}"

# Output results
echo ""
echo -e "${GREEN}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║         Build Successful! ✓            ║${NC}"
echo -e "${GREEN}╚════════════════════════════════════════╝${NC}"
echo ""
echo -e "${CYAN}Output files:${NC}"
for i in "${!ZIP_FILES[@]}"; do
    echo "  → ${ZIP_FILES[$i]}  (Jellyfin ${TARGET_ABIS[$i]}, md5 ${CHECKSUMS[$i]})"
done
echo ""
echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}Add these manifest.json entries, highest targetAbi FIRST.${NC}"
echo -e "${YELLOW}Jellyfin picks the first entry whose targetAbi <= the server${NC}"
echo -e "${YELLOW}version, so ordering is what keeps each server on its build.${NC}"
echo ""
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
for i in $(seq $(( ${#ZIP_FILES[@]} - 1 )) -1 0); do
cat << EOF
{
  "version": "${VERSION}.0",
  "changelog": "Your changelog here",
  "targetAbi": "${TARGET_ABIS[$i]}",
  "sourceUrl": "https://github.com/007hacky007/LogoSwap/releases/download/v$VERSION/$(basename "${ZIP_FILES[$i]}")",
  "checksum": "${CHECKSUMS[$i]}",
  "timestamp": "$TIMESTAMP"
},
EOF
done
echo -e "${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
