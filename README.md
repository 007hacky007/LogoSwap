# LogoSwap

**Custom logo replacement plugin for Jellyfin 10.11 and 12.0**
<img src="static/icon.png" alt="LogoSwap" width="140" align="right">

Replace the default Jellyfin branding with your own logo across the entire interface - no manual file editing required.

> **This is a maintained fork.** LogoSwap was created by [NewsGuyTor](https://github.com/NewsGuyTor/LogoSwap). The original repository has had no activity since March 2026, so development continues here, starting with Jellyfin 12.0 support. If you installed LogoSwap from the original repository, replace its plugin repository URL with the one [below](#via-plugin-repository-recommended) to keep receiving updates. Your uploaded logo is kept; after updating, click **Apply Custom Logo to Branding** once more so the new CSS replaces the old.


![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11%20%7C%2012.0-00a4dc?style=flat-square&logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-9.0%20%7C%2010.0-512bd4?style=flat-square&logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)


## Features

- **Simple Upload Interface** - Upload your logo directly from the Jellyfin dashboard
- **One-Click Activation** - Writes the logo CSS into Jellyfin's branding settings
- **Live Preview** - See your current logo before applying changes
- **Easy Removal** - Restore default Jellyfin branding with a single click
- **Custom Favicon** - Replace the browser tab icon with a PNG, SVG or ICO file
- **Base URL Support** - Works when Jellyfin runs under a base URL such as `/jellyfin` or behind a reverse proxy path

---

## Installation

### Via Plugin Repository (Recommended)

1. Open Jellyfin Dashboard → **Plugins** → **Repositories**
2. Click **+** to add a new repository:
   - **Name:** `LogoSwap`
   - **URL:** `https://raw.githubusercontent.com/007hacky007/LogoSwap/main/manifest.json`
3. Go to **Plugins** → **Catalog**
4. Find **LogoSwap** and click **Install**
5. Restart Jellyfin

### Manual Installation

1. Download the zip matching your server from [Releases](https://github.com/007hacky007/LogoSwap/releases): `_jf10.11.zip` for Jellyfin 10.11, `_jf12.0.zip` for Jellyfin 12.0
2. Extract its contents to your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/LogoSwap/`
   - Windows: `C:\ProgramData\Jellyfin\Server\plugins\LogoSwap\`
   - Docker: `/config/plugins/LogoSwap/`
3. Restart Jellyfin

---

## Usage

### 1. Upload Your Logo

Navigate to **Dashboard** → **Plugins** → **LogoSwap**

- Click **Select Logo Image (PNG)** and choose your logo file. It uploads as soon as you pick it.

> **Tip:** For best results, use a PNG with a transparent background. Recommended dimensions: 400×100px or similar wide aspect ratio.

### 2. Apply to Branding

- Click **Apply Custom Logo to Branding**
- Hard refresh your browser (`Ctrl+Shift+R` / `Cmd+Shift+R`)

Your custom logo will now appear across the main Jellyfin interface.

> **Note:** The custom logo will not appear in the Dashboard admin area, as it uses a separate interface where custom branding is not applied.

### 3. Manage Your Logo

- **Preview** - View your current uploaded logo
- **Delete Logo** - Remove your logo and restore default branding
- **Remove from Branding** - Disable the logo without deleting the file

### 4. Custom Favicon (Optional)

- Click **Select Favicon Image (PNG, SVG, ICO)** and choose a file up to 512 KiB
- Reload Jellyfin. The new icon shows in the browser tab, with no Apply step

A PNG also replaces the iOS home screen icon, so a square PNG of at least 180x180 pixels gives the best result. **Delete Favicon** restores the Jellyfin icon.

---

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/logoswap/upload` | POST | Upload a new logo (multipart/form-data) |
| `/logoswap/image` | GET | Retrieve the current logo |
| `/logoswap/delete` | DELETE | Delete the uploaded logo |
| `/logoswap/status` | GET | Check if a logo is configured |
| `/logoswap/favicon/upload` | POST | Upload a favicon: PNG, SVG or ICO (multipart/form-data) |
| `/logoswap/favicon/image` | GET | Retrieve the current favicon |
| `/logoswap/favicon/delete` | DELETE | Delete the uploaded favicon |
| `/logoswap/favicon/status` | GET | Check if a favicon is configured |

The uploads and deletes require an authenticated administrator. The images are
anonymous, because the branding CSS and the browser fetch them without an auth header.

---

## How It Works

LogoSwap uses Jellyfin's built-in branding customization system. When you click "Apply Custom Logo to Branding", the plugin:

1. Generates CSS rules that override the logo, covering both the Modern
   layout (default since Jellyfin 12) and the Legacy layout
2. Writes them into Jellyfin's branding Custom CSS setting, between
   `/* LogoSwap CSS */` markers so they can be removed again cleanly

The favicon cannot be reached by branding CSS, because it is a link in the web
client's `index.html`. While a favicon is uploaded, the plugin rewrites the icon
links in that page as Jellyfin serves it, pointing them at `/logoswap/favicon/image`.
Nothing on disk changes, and deleting the favicon restores the original page.

This approach is non-destructive: your original Jellyfin files are never modified.

---

## Building from Source

```bash
git clone https://github.com/007hacky007/LogoSwap.git
cd LogoSwap
dotnet build
```

Output: `bin/Debug/net9.0/LogoSwap.dll` (Jellyfin 10.11) and `bin/Debug/net10.0/LogoSwap.dll` (Jellyfin 12.0)

---

## Requirements

- Jellyfin Server 10.11.x or 12.0+
- .NET 9.0 Runtime (bundled with Jellyfin 10.11) or .NET 10.0 (bundled with Jellyfin 12.0)

The plugin ships one build per server version. The plugin catalogue picks the right one automatically.

---

## Troubleshooting

**Logo not appearing after applying?**
- Hard refresh your browser (`Ctrl+Shift+R`)
- Clear browser cache
- Check Dashboard → General → Branding to verify injection is present

**Upload fails?**
- Ensure the file is a PNG under 4 MB
- Check Jellyfin has write permissions to its plugin config directory
- Review server logs for detailed error messages

---

## FAQ

**Why doesn't my logo appear in the Dashboard?**

The Jellyfin Dashboard admin area uses a separate interface where custom branding CSS is not applied. This is a limitation of how Jellyfin handles its admin pages. Your custom logo will appear on all regular user-facing pages.

**Can I replace the splash/loading screen logo?**

Not yet. The splash screen that appears when Jellyfin is loading is embedded in the core application and cannot be replaced via branding customization. This may be added in a future version if there's a feasible approach.

**Where is my logo stored?**

Your logo and favicon are stored in the plugin's data directory within Jellyfin's configuration folder, so they survive plugin updates. They're served via the `/logoswap/image` and `/logoswap/favicon/image` endpoints.

**The favicon did not change. Why?**

Reload the Jellyfin page once after uploading. The favicon is only replaced when Jellyfin serves its own web client (the default). If you host jellyfin-web separately, the plugin cannot change its `index.html`. Installed web apps (PWA) take their icon from Jellyfin's web manifest, which the plugin does not change.

**My server runs under a base URL. Do I need to re-apply?**

If you applied the logo with an older version, click **Apply Custom Logo to Branding** once more. Older versions wrote an absolute `/logoswap/image` URL that broke under a base URL.

---

## Contributing

Bug reports, feature requests, and pull requests are welcome!

- **Found a bug or have an idea?** [Open an issue](https://github.com/007hacky007/LogoSwap/issues)
- **Want to contribute?** Fork the repo and submit a PR

---

## License

MIT License - see [LICENSE](LICENSE) for details.

---

**Original author:** [NewsGuyTor](https://github.com/NewsGuyTor). Maintained as a fork since September 2026.
