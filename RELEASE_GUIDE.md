# LogoSwap Release Guide

Complete guide for building and shipping the LogoSwap plugin.

---

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (builds both target frameworks)
- Git (for tagging releases)
- GitHub account (for hosting releases)

> **Windows users:** Use [WSL](https://learn.microsoft.com/en-us/windows/wsl/install) to run the build script.

---

## Quick Build

```bash
chmod +x build.sh
./build.sh 1.0.0
```

---

## Release Workflow

### Step 1: Update Version Numbers

Before building, update the version in these files:

**`LogoSwap.csproj`**
```xml
<AssemblyVersion>1.2.0.0</AssemblyVersion>
<FileVersion>1.2.0.0</FileVersion>
```

### Step 2: Build the Plugin

```bash
./build.sh 1.2.0
```

The plugin ships one build per Jellyfin ABI, since 10.11 runs on .NET 9 and
12.0 on .NET 10. Output files will be in `./artifacts/`:

- `LogoSwap_1.2.0_jf10.11.zip` - for Jellyfin 10.11 (`targetAbi` 10.11.0.0)
- `LogoSwap_1.2.0_jf12.0.zip` - for Jellyfin 12.0 (`targetAbi` 12.0.0.0)

### Step 3: Create GitHub Release

Tagging is enough. The release workflow builds every target framework,
attaches both zips and updates the manifest:

```bash
git add .
git commit -m "Release v1.2.0"
git tag -a v1.2.0 -m "Version 1.2.0"
git push origin main --tags
```

A tag with a suffix, such as `v1.2.0-beta.1`, is published as a pre-release and
goes to `manifest-testing.json` instead. Number pre-releases of the same
version in increasing order: the trailing number becomes the fourth part of
the plugin version (`v1.2.0-beta.1` is `1.2.0.1`), and Jellyfin only offers an
update whose version is strictly greater than the installed one.

To publish by hand instead, attach both zips to the release and add the
manifest entries below.

### Step 4: Update manifest.json

The build script outputs the exact JSON to add: one entry per ABI, sharing a
version number. List the **highest `targetAbi` first**. Jellyfin installs the
first entry whose `targetAbi` is not greater than the server version, and
entries with equal version numbers keep manifest order, so the ordering is
what sends each server to its own build.

Update `manifest.json`:

```json
[
  {
    "guid": "19b92b8c-3926-4449-8432-10d30ab75a05",
    "name": "LogoSwap",
    "description": "Swap the default Jellyfin logo with your own custom branding.",
    "overview": "Upload a custom logo to replace Jellyfin branding throughout the interface.",
    "owner": "007hacky007",
    "category": "General",
    "imageUrl": "https://raw.githubusercontent.com/007hacky007/LogoSwap/main/static/icon.png",
    "versions": [
      {
        "version": "1.2.0.0",
        "changelog": "New feature: Added awesome thing",
        "targetAbi": "12.0.0.0",
        "sourceUrl": "https://github.com/007hacky007/LogoSwap/releases/download/v1.2.0/LogoSwap_1.2.0_jf12.0.zip",
        "checksum": "abc123def456...",
        "timestamp": "2025-11-25T12:00:00Z"
      },
      {
        "version": "1.2.0.0",
        "changelog": "New feature: Added awesome thing",
        "targetAbi": "10.11.0.0",
        "sourceUrl": "https://github.com/007hacky007/LogoSwap/releases/download/v1.2.0/LogoSwap_1.2.0_jf10.11.zip",
        "checksum": "def456abc123...",
        "timestamp": "2025-11-25T12:00:00Z"
      },
      {
        "version": "1.0.0.0",
        "changelog": "Initial release.",
        "targetAbi": "10.11.0.0",
        "sourceUrl": "https://github.com/NewsGuyTor/LogoSwap/releases/download/1.0.0/LogoSwap_1.0.0.zip",
        "checksum": "...",
        "timestamp": "2025-11-25T12:00:00Z"
      }
    ]
  }
]
```

> **Important:** Keep older versions in the array so users can install previous releases if needed.

### Step 5: Push Updated Manifest

```bash
git add manifest.json
git commit -m "Update manifest for v1.2.0"
git push origin main
```

---

## Checksum

The build script automatically generates an MD5 checksum. This is used by Jellyfin to verify download integrity.

**Manual checksum (if needed):**

```bash
# macOS
md5 -q artifacts/LogoSwap_1.2.0_jf12.0.zip

# Linux
md5sum artifacts/LogoSwap_1.2.0_jf12.0.zip | awk '{print $1}'
```

---

## Folder Structure After Build

```
LogoSwap/
├── artifacts/
│   ├── LogoSwap_1.2.0_jf10.11.zip   ← Jellyfin 10.11
│   └── LogoSwap_1.2.0_jf12.0.zip    ← Jellyfin 12.0
├── bin/
│   └── Release/
│       ├── net9.0/
│       │   └── LogoSwap.dll
│       └── net10.0/
│           └── LogoSwap.dll
├── build.sh
├── manifest.json              ← Update with new version
└── ...
```

---

## Testing Before Release

### Local Installation Test

1. Build the plugin
2. Copy `bin/Release/net10.0/LogoSwap.dll` to your Jellyfin plugins folder:
   - Linux: `/var/lib/jellyfin/plugins/LogoSwap/`
   - Windows: `C:\ProgramData\Jellyfin\Server\plugins\LogoSwap\`
   - Docker: `/config/plugins/LogoSwap/`
   - macOS: `~/.local/share/jellyfin/plugins/LogoSwap/`
3. Restart Jellyfin
4. Verify the plugin appears in Dashboard → Plugins
5. Test all features

### Repository Installation Test

1. Push your manifest changes
2. In Jellyfin: Dashboard → Plugins → Repositories
3. Add your repository URL
4. Go to Catalog and verify your plugin appears
5. Install and test

---

## Troubleshooting

### Build fails with SDK error
Ensure the .NET 10.0 SDK is installed:
```bash
dotnet --list-sdks
```

### Plugin doesn't load
- Check Jellyfin logs for errors
- Verify `targetAbi` matches your Jellyfin version
- Ensure DLL is in a subfolder (e.g., `plugins/LogoSwap/LogoSwap.dll`)

### Users can't install from repository
- Verify `sourceUrl` is accessible (try downloading it in a browser)
- Check that `checksum` matches the actual file
- Ensure `manifest.json` is valid JSON

---

## Checklist

Before releasing:

- [ ] Version updated in `.csproj`
- [ ] Build completed successfully
- [ ] Tested locally
- [ ] GitHub release created with ZIP attached
- [ ] `manifest.json` updated with new version entry
- [ ] Checksum is correct
- [ ] `sourceUrl` points to the correct release asset
- [ ] Pushed updated manifest to main branch
