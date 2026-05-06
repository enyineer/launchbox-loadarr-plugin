# Loadarr

A LaunchBox + BigBox plugin that lets you search ROM sources, download ROMs,
and import them into your library — all without leaving LaunchBox.

![Loadarr in LaunchBox](screenshots/launchbox-main.png)

> [!CAUTION]
> Distributing or downloading copyrighted ROMs may be illegal in your
> jurisdiction. Loadarr is a generic search/download tool — what you do with it
> is **your responsibility**. Use it only for ROMs you legally own (homebrew,
> public-domain titles, your own dumps, etc.). The author of this plugin does
> not host, mirror, or endorse infringement of any kind.

## What it does

- **Search** ROM sources by title from inside LaunchBox or BigBox.
- **Download** with a live progress bar; archives are auto-extracted (the
  primary ROM is picked from inside `.zip` / `.7z` / `.rar`, sidecar files
  for disc images come along, arcade `.zip` romsets are kept intact).
- **Auto-import** into LaunchBox via the plugin API — the new game appears
  in your library without a restart.
- **Enrich metadata** from LaunchBox's bundled metadata database: release
  date, developer, publisher, genres, community rating, Wikipedia URL,
  description.
- **Pick game artwork** from the LaunchBox image database with a region-aware
  default selection (a Germany ROM defaults to the Germany boxart, etc.).
- **Background queue** — start a download, queue more, keep using LaunchBox.
- **BigBox controller UI** — a dedicated fullscreen overlay with a virtual
  keyboard and gamepad-driven navigation, no theme editing required.

## Screenshots

|                                                                 |                                                              |
|-----------------------------------------------------------------|--------------------------------------------------------------|
| **LaunchBox** — search, results, status                         | **Image picker** — region-aware defaults, select-all/none    |
| ![](screenshots/launchbox-main.png)                             | ![](screenshots/launchbox-image-selection.png)               |
| **BigBox overlay** — fullscreen, controller-driven, inline queue                                                    |||
| ![](screenshots/bigbox-overlay.png)                                                                                 |||

# For users

## Requirements

- **Windows** (LaunchBox is Windows-only).
- **LaunchBox** with the modern .NET-Core plugin host (any version since the
  Big-Box-app-store transition — basically anything from the last few years).
  No NuGet config or extra runtimes needed.

## Install

1. Download `Loadarr.dll` from the [latest release](../../releases) (or build
   it yourself — see [Building from source](#building-from-source)).
2. Drop it into:

   ```
   <LaunchBox>\Plugins\Loadarr\Loadarr.dll
   ```

   Create the `Loadarr` folder if it doesn't exist.
3. Restart LaunchBox / BigBox.

That's it. There's only one file to ship — third-party dependencies
(HtmlAgilityPack, SharpCompress, Microsoft.Data.Sqlite, etc.) are embedded
into `Loadarr.dll` at build time.

## Using Loadarr — LaunchBox

1. **Tools → Loadarr — Find ROMs…** opens the search window.
   (You can also right-click any game in your library and pick
   *Search Loadarr for this title…* to pre-fill the title and platform.)
2. Type a title and press **Search**. Results from all configured sources
   are returned in parallel and labelled with their source.
3. Pick a result and click **Download & Import**. The image picker opens —
   defaults are pre-checked based on the source's region. Confirm to
   download.
4. The download runs in the background. Click **Queue** to monitor it; you
   can keep searching for more titles in the meantime.
5. When the queue finishes, the new game is already in your library — no
   restart needed.

## Using Loadarr — BigBox (with a controller)

1. Open the **System menu** in BigBox and pick
   **Loadarr — Find ROMs…**. The fullscreen overlay opens with the queue
   visible immediately, so you can keep an eye on in-progress downloads.
2. Press **A** on the query field (or **X** anywhere) to bring up the
   on-screen keyboard.
3. **D-pad** to select the key, **A** confirms each key.
4. Press the **SEARCH** key to run the search.
5. **A** on a result starts the download. The image picker opens — toggle
   selections with **A**, **Y** to select all, **X** to deselect all,
   **Start** to confirm.
6. Press **Start** or **B** to close. BigBox returns to the platform list
   with your new game ready to play as soon as download and extraction are finished.

### Controller cheat sheet

| Button   | What it does                                            |
|----------|---------------------------------------------------------|
| D-pad / left stick | Navigate                                      |
| A        | Confirm (type a key, queue a result, toggle a checkbox) |
| B        | Back / close                                            |
| X        | Show/hide the on-screen keyboard                        |
| Start    | Close the overlay                                       |
| LB / RB  | Tab between focusable groups                            |

## Built-in ROM sources

| Source       | API style    | Notes                                                                    |
|--------------|--------------|--------------------------------------------------------------------------|
| Vimm's Lair  | HTML scrape  | Curated retro ROMs. Fragile by nature — when the site changes its HTML, results may silently disappear until the parser is updated. |

More sources can be added via a small interface — see
[Adding a new ROM source](#adding-a-new-rom-source).

## Configuration

On first launch, defaults are written to:

```
%APPDATA%\Loadarr\settings.json
```

Editable fields:

| Key                           | Default                                       | Purpose                                                                                                                                       |
|-------------------------------|-----------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------|
| `DownloadDirectory`           | `%USERPROFILE%\LaunchBox\Loadarr\Downloads`   | Where downloaded ROMs land before LaunchBox import.                                                                                           |
| `ExtractDownloadedArchives`   | `true`                                        | Extract the primary ROM from `.zip` / `.7z` / `.rar`. Multi-file disc images (`.cue` + `.bin`, `.gdi` + `.raw`) are extracted together. Arcade platforms (MAME, etc.) keep their `.zip` intact — the zip itself is the romset. |
| `SearchTimeoutSeconds`        | `30`                                          | HTTP timeout for searches and downloads.                                                                                                      |
| `EnableDebugLogging`          | `true`                                        | Write per-import diagnostics to `%APPDATA%\Loadarr\loadarr.log`.                                                                              |

If something goes wrong (a ROM doesn't import, an emulator doesn't recognize
the file), the log file is the first place to look — every download,
extraction, metadata lookup, and emulator binding decision is recorded.

## Limitations

- **Vimm's Lair scraping is fragile.** When the site changes its HTML, search
  results from Vimm's may silently disappear. Adding a more stable source
  means implementing one interface — see below.
- **Platform names.** LaunchBox doesn't standardize platform naming;
  `PlatformMapper` covers the common consoles, but exotic platforms may need
  a manual mapping added.
- **No metadata scraping over the internet.** Loadarr enriches imports from
  LaunchBox's *local* metadata database, which is what your installation
  already has. LaunchBox's built-in metadata refresh handles online lookups.
- **BigBox theme integration.** The BigBox UI is a fullscreen overlay
  (one-DLL drop, no setup). It doesn't reuse your active BigBox theme —
  see [Why an overlay, not a theme element?](#why-an-overlay-not-a-theme-element).

# For developers

## Adding a new ROM source

A "ROM source" is anything that exposes a search API and returns downloadable
ROM URLs (an HTML site you scrape, a JSON API, an Internet Archive index,
etc.). Adding one is a small, isolated change.

### 1. Implement [`IRomSource`](src/Loadarr/Sources/IRomSource.cs)

```csharp
internal sealed class MyAwesomeSource : IRomSource
{
    public string Name => "My Awesome Source";

    public Task<IReadOnlyList<RomSearchResult>> SearchAsync(
        string query, string platformHint, CancellationToken ct)
    {
        // Query your backend, map results into RomSearchResult objects.
        // Use platformHint to filter when possible.
    }

    public Task<ResolvedDownload> GetDownloadAsync(
        RomSearchResult result, CancellationToken ct)
    {
        // Resolve the actual download URL + headers + filename.
        // Called when the user picks a result; lets you defer expensive
        // detail-page fetches until they're needed.
    }
}
```

### 2. Register it in [`SourceRegistry`](src/Loadarr/Sources/SourceRegistry.cs)

```csharp
public static IReadOnlyList<IRomSource> Build(HttpClient http) =>
    new IRomSource[]
    {
        new VimmsLairSource(http),
        new MyAwesomeSource(http),
    };
```

That's it — your source is queried in parallel with the others on every
search, and its results show up labelled in the `SOURCE` column.

### Tips

- **Heavy work belongs in a separate library.** Vimm's parsing logic lives
  in `src/Loadarr.Sources.VimmsLair/` (a `netstandard2.0` library), with a
  thin `VimmsLairSource` adapter in the main plugin. This makes it
  unit-testable without booting LaunchBox. Mirror this layout for non-trivial
  sources.
- **Map platform names.** Most ROM sites use their own platform vocabulary
  (e.g. Vimm's says `N64`; LaunchBox says `Nintendo 64`). Extend
  [`PlatformMapper`](src/Loadarr/Services/PlatformMapper.cs) so the imported
  game lands under the canonical LaunchBox platform name.
- **Region awareness pays off.** Populate `RomSearchResult.Region` with
  whatever the source provides ("USA", "Europe", "Germany", …). The image
  picker uses it to pre-select region-appropriate artwork.

## Building from source

The project targets `net48` (LaunchBox's plugin host) and uses
**Costura.Fody** to produce a single `Loadarr.dll`. The build works
cross-platform; only *running* the plugin requires Windows.

### On Windows

```powershell
# Build only
pwsh ./build/build.ps1 -LaunchBoxPath "D:\Games\LaunchBox"

# Build and install into <LaunchBox>\Plugins\Loadarr
pwsh ./build/build.ps1 -LaunchBoxPath "D:\Games\LaunchBox" -Install
```

Or directly:

```powershell
dotnet build src/Loadarr/Loadarr.csproj -c Release /p:LaunchBoxPath="D:\Games\LaunchBox"
```

### On macOS / Linux

```bash
build/build-mac.sh                  # build plugin (auto-builds stub if missing)
build/build-mac.sh --rebuild-stub   # regenerate the LaunchBox API stub
build/build-mac.sh --clean          # wipe bin/obj first
build/build-mac.sh --config Debug   # build Debug instead of Release
```

The output is a single `src/Loadarr/bin/Release/Loadarr.dll`. Copy that one
file to your Windows machine to deploy.

## LaunchBox API stub

`Unbroken.LaunchBox.Plugins.dll` ships only with LaunchBox itself — there's
no NuGet package and no public reference assembly. To unblock cross-platform
builds, this repo includes a tiny **compile-time stub** at
[`build/stubs/LaunchBoxStub/`](build/stubs/LaunchBoxStub/) that:

- Produces an assembly named `Unbroken.LaunchBox.Plugins.dll`.
- Declares only the types and members Loadarr actually uses.
- Has matching namespaces (`Unbroken.LaunchBox.Plugins[.Data]`).

The main project references the stub with `<Private>false</Private>`, so it
is **not** copied into `bin/Release`. At runtime LaunchBox loads its real
DLL from `<LaunchBox>\Core\` and the CLR resolves Loadarr's references
against that — the stub is purely a compile-time substitute.

You only need to touch the stub when Loadarr starts calling a LaunchBox API
it doesn't currently use, or when LaunchBox itself adds members to interfaces
Loadarr implements:

1. Edit [`build/stubs/LaunchBoxStub/Stub.cs`](build/stubs/LaunchBoxStub/Stub.cs).
2. Re-run `build/build-mac.sh --rebuild-stub`.
3. (Recommended) Verify the addition matches the real SDK with one build on
   a Windows machine against the actual LaunchBox install.

The stub project is **not** part of `launchbox-loadarr.sln` and is never
shipped; it's strictly a build-time helper.

## How the LaunchBox plugin pieces fit together

| File                                                                              | Plugin interface                | Purpose                                                                |
|-----------------------------------------------------------------------------------|---------------------------------|------------------------------------------------------------------------|
| [`Plugin/LoadarrToolsMenu.cs`](src/Loadarr/Plugin/LoadarrToolsMenu.cs)             | `ISystemMenuItemPlugin`         | Tools-menu entry (LaunchBox) + System-menu entry (BigBox).             |
| [`Plugin/LoadarrGameMenu.cs`](src/Loadarr/Plugin/LoadarrGameMenu.cs)               | `IGameMenuItemPlugin`           | Right-click on a game → search Loadarr pre-filled.                     |
| [`Services/LaunchBoxImporter.cs`](src/Loadarr/Services/LaunchBoxImporter.cs)       | uses `PluginHelper.DataManager` | Creates the platform if needed, calls `AddNewGame`, then `Save(true)`. |
| [`Services/DownloadQueueService.cs`](src/Loadarr/Services/DownloadQueueService.cs) | (singleton)                     | Single-worker FIFO queue: download → extract → import → fetch images.  |
| [`UI/SearchWindow.xaml`](src/Loadarr/UI/SearchWindow.xaml)                         | WPF window                      | LaunchBox desktop UI.                                                  |
| [`UI/BigBoxSearchWindow.xaml`](src/Loadarr/UI/BigBoxSearchWindow.xaml)             | WPF fullscreen overlay          | BigBox UI with virtual keyboard + inline queue.                        |
| [`Services/XInputController.cs`](src/Loadarr/Services/XInputController.cs)         | gamepad poller                  | P/Invoke into `xinput1_4.dll`; BigBox doesn't forward gamepad input to plugin windows, so we consume it ourselves. |

## Why an overlay, not a theme element?

`IBigBoxThemeElementPlugin` exists, but using it requires the *user* to
hand-edit their active BigBox theme's XAML files (and re-do those edits
every time they change or update the theme). There is no API to navigate
to a theme element from a menu, so even with a theme element you'd still
need an overlay-or-equivalent entry point.

Loadarr's overlay is one DLL, drops into `Plugins/Loadarr/`, works on every
theme without modification, and consumes XInput directly to drive its
controller-friendly UI. The price is that the overlay's visual style is its
own (dark + accent), not your theme's. That tradeoff felt right; if you
disagree, see the conversation in
[the BigBox plugin forum thread](https://forums.launchbox-app.com/topic/82331-dev-using-ibigboxthemeelementplugin-interface/)
for more context on the constraint.

## Help / feedback

- Found a bug? Open an issue.
- Adding a source / fixing a parser? PRs welcome — see
  [Adding a new ROM source](#adding-a-new-rom-source).
- LaunchBox plugin questions in general:
  [LaunchBox plugin API docs](https://pluginapi.launchbox-app.com).
