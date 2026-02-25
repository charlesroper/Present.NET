# Present

A Windows WPF presentation app that displays web content and images as slides. Port of the macOS SwiftUI "Present" app.

## Features

- **Edit Mode**: Left sidebar with slide URL list (numbered, drag-to-reorder); right pane WebView2 preview of the selected slide
- **Play Mode**: Fullscreen presentation window with black background, WebView2 content, slide counter overlay, arrow key navigation (wraps around), Escape to exit
- **Auto-persist**: Slide list is saved automatically and restored on relaunch (`%APPDATA%\Present\slides.txt`)
- **File I/O**: Open/Save slide lists as plain text files (one URL per line)
- **Zoom Controls**: `Ctrl+=` / `Ctrl+-` / `Ctrl+0` to zoom in/out/reset (applies to both preview and fullscreen)
- **Image Slides**: URLs ending in `.png`, `.gif`, `.jpg`, `.jpeg`, `.webp`, `.svg` are rendered as full-window images on a black background
- **Remote Control Server**: Embedded HTTP server on port 9123 with a mobile-friendly HTML control page

## Requirements

- **Windows 10 / 11** (WPF is Windows-only)
- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Microsoft Edge WebView2 Runtime** — Usually pre-installed on Windows 10/11. If not, [download here](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

## Build & Run

```powershell
# Clone and build
git clone https://github.com/charlesroper/present.git
cd present
dotnet restore
dotnet build Present.sln -c Release

# Run
dotnet run --project src/Present/Present.csproj
```

Or open `Present.sln` in Visual Studio 2022 and press F5.

## Usage

### Edit Mode

1. Click **+ Add Slide** to add a new slide URL
2. Type or paste a URL into the text field (e.g. `https://example.com` or `https://example.com/image.png`)
3. Select a slide to preview it in the right pane
4. Drag slides up/down to reorder them
5. Use **↑/↓** toolbar buttons or drag-and-drop to reorder
6. Use **File → Open / Save / Save As** to manage slide list files

### Play Mode

Press **F5** or click **▶ Play** to start the presentation:

| Key | Action |
|-----|--------|
| `→` / `↓` / `Space` / `PgDn` | Next slide |
| `←` / `↑` / `PgUp` | Previous slide |
| `Ctrl+=` | Zoom in |
| `Ctrl+-` | Zoom out |
| `Ctrl+0` | Reset zoom |
| `F` | Toggle slide counter |
| `Esc` | Exit fullscreen |

Navigation wraps around (last slide → first, first slide → last).

### Image Slides

URLs ending in `.png`, `.gif`, `.jpg`, `.jpeg`, `.webp`, or `.svg` are automatically detected and rendered as full-window images on a black background, scaled to fit while preserving aspect ratio.

### Remote Control

An HTTP server runs on port 9123. Open `http://<your-ip>:9123/` on your phone or tablet for a mobile-friendly remote control page. The IP address is shown in the toolbar.

**API Endpoints:**

| Endpoint | Description |
|----------|-------------|
| `GET /` | Mobile HTML remote control page |
| `GET /next` | Go to next slide |
| `GET /prev` | Go to previous slide |
| `GET /play` | Start fullscreen presentation |
| `GET /stop` | Stop presentation (close fullscreen) |
| `GET /zoomin` | Zoom in |
| `GET /zoomout` | Zoom out |
| `GET /scroll?dy=200` | Scroll page by `dy` pixels |
| `GET /status` | JSON status (`currentIndex`, `slideCount`, `isPlaying`, `currentUrl`, `zoomFactor`) |

All endpoints (except `/`) return a JSON status object.

### Keyboard Shortcuts (Edit Mode)

| Shortcut | Action |
|----------|--------|
| `F5` | Play presentation |
| `Ctrl+O` | Open file |
| `Ctrl+S` | Save file |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+=` | Zoom in |
| `Ctrl+-` | Zoom out |
| `Ctrl+0` | Reset zoom |

## Project Structure

```
Present/
├── Present.sln
├── README.md
└── src/
    └── Present/
        ├── Present.csproj
        ├── App.xaml / App.xaml.cs
        ├── MainWindow.xaml / MainWindow.xaml.cs     ← Edit mode UI
        ├── FullscreenWindow.xaml / FullscreenWindow.xaml.cs  ← Play mode
        ├── Models/
        │   ├── SlideItem.cs       ← Data model for a slide
        │   └── SlideHelper.cs     ← URL detection & image HTML
        └── Services/
            ├── PersistenceService.cs    ← Save/load slide lists
            └── RemoteControlServer.cs   ← HTTP remote control server
```

## Slide List File Format

Slide lists are plain text files with one URL per line:

```
https://example.com/slide1
https://example.com/slide2.png
https://mypresentation.com/deck
```

Blank lines are ignored. The auto-save file is located at `%APPDATA%\Present\slides.txt`.
