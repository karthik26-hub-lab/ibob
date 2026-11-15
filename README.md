<p align="center">
  <img src="docs/icon.png" width="120" alt="ibob icon: a black pill with two white eyes">
</p>

<h1 align="center">ibob</h1>

<p align="center">
  A tiny pill at the top of your Windows screen that opens into music, meetings, tasks, a file shelf and more.<br>
  Inspired by the iPhone's Dynamic Island. Built for students and office / IT folks.
</p>

<!-- Demo: record a short screen capture (Win + Alt + R with Xbox Game Bar, or ShareX for a GIF) and save it as docs/demo.gif -->
<p align="center">
  <img src="docs/screenshots/pill.png" width="200" alt="The closed pill: two eyes">
  &nbsp;
  <img src="docs/screenshots/pill-alert.png" width="330" alt="The pill showing a meeting alert">
</p>
<p align="center">
  <img src="docs/screenshots/home.png" width="420" alt="Home: now playing">
  <img src="docs/screenshots/plan.png" width="420" alt="Plan: meetings with Join and tasks with countdowns">
</p>
<p align="center">
  <img src="docs/screenshots/disc.png" width="420" alt="Right-click disc on a shelf file">
  <img src="docs/screenshots/clipboard.png" width="420" alt="Searchable clipboard history">
</p>
<p align="center"><sub>Screenshots use sample data (<code>ibob.exe --screenshots &lt;folder&gt;</code> regenerates them).</sub></p>

---

## What it does

At rest ibob is a small black pill with two eyes that blink and look around. It nods along when music plays,
shows a thin line that follows your voice in calls, and a green / orange dot while your camera / mic is on.
Hover it and it springs open, with a row of tabs along the top: click one to jump straight to it
(or scroll the mouse wheel to move through them).

| Tab | What's on it |
|---|---|
| **Home** | What's playing (artwork, seek bar, play / pause / skip), volume, brightness, Wi-Fi, Bluetooth and mic mute |
| **Plan** | Your next meetings and classes from a calendar link, with a **Join** button for Teams / Zoom / Meet, and your tasks: type `DBMS assignment fri 5pm` and it gets a live countdown and reminders |
| **Notes** | A quick notepad that saves as you type |
| **Shelf** | Files, images and screenshots you copy or drop on ibob wait here to be dragged out later (copied text goes to Clipboard) |
| **Clipboard** | Searchable history of copied text, with pins for things you reuse |
| **Focus** | Pomodoro timer with a progress ring in the pill |

The time, battery and a **bell** for your Windows notifications (with a count) sit at the right of the tab bar.
Tabs you don't use can be hidden in Settings (right-click the pill).
### Right-click anything on the shelf

A small disc of actions pops up around the cursor:

- **Preview** images, PDFs, Word documents and text right inside the island
- **Convert:** Word / images → PDF, Word / Excel / PDF → Markdown, image → JPG, shrink images, compress or split PDFs, **extract text from screenshots** (OCR)
- **Merge** PDFs, Word documents and images into one PDF, in the order you pick
- **Dev tools:** format / minify JSON, Base64, URL encode / decode, decode JWTs, SHA-256 hashes (plus a UUID button)

Results land back on the shelf; nothing is written to your folders until you drag it out or choose **Save**.

## Install

1. Download `ibob.exe` from the [latest release](../../releases/latest).
2. Run it and choose **Yes** to install. No admin rights needed; it installs for your account only, starts with Windows
   and appears in the Start menu.
3. Hover the pill at the top of your screen. Right-click the pill for **Settings**.

Uninstall from **Settings → Apps → Installed apps**, or from ibob's own Settings window.

**Needs:** Windows 10 (1809+) or 11, 64-bit. Word → PDF / Markdown uses your installed Microsoft Word.
HEIC photos need Microsoft's free "HEIF Image Extensions".

## Privacy

Everything stays on your computer. Shelf files, notes, tasks, clipboard history and settings live in
`%LOCALAPPDATA%\ibob`. Copies made by password managers are never recorded. The only network requests are the
calendar links you add yourself.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/karthik26-hub-lab/ibob.git
cd ibob
dotnet run --project ibob
```

Single-file release build (what goes on the Releases page):

```bash
dotnet publish ibob -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

### How it's built

C# / .NET 8 / WPF, plus Windows APIs for the system bits:

| Feature | How |
|---|---|
| Media controls | `GlobalSystemMediaTransportControlsSessionManager` (WinRT) |
| Wi-Fi / Bluetooth | `Windows.Devices.Radios` |
| Volume, mic, voice level | Core Audio via [NAudio](https://github.com/naudio/NAudio) |
| Brightness | WMI `WmiMonitorBrightnessMethods` |
| Notifications | `UserNotificationListener` |
| Camera / mic in use | The `CapabilityAccessManager\ConsentStore` registry keys Windows uses for its own privacy indicator |
| Clipboard watching | `AddClipboardFormatListener` |
| OCR | `Windows.Media.Ocr` |
| PDF merge / compress / split | [PDFsharp](https://github.com/empira/PDFsharp) |
| PDF text | [PdfPig](https://github.com/UglyToad/PdfPig) |
| Excel | [ClosedXML](https://github.com/ClosedXML/ClosedXML) |
| Calendars | [Ical.Net](https://github.com/rianjs/ical.net) |
| Word → PDF | Microsoft Word automation (COM) |

Code map: each feature lives in its own file, e.g. `MainWindow.Shelf.cs`, `MainWindow.Today.cs`, `Converters.cs`, `FileTools.cs`, `TextTools.cs`.

## License

[MIT](LICENSE). Libraries keep their own licenses: NAudio, PDFsharp, ClosedXML and Ical.Net (MIT), PdfPig (Apache 2.0).

ibob is an independent project and is not affiliated with Apple.
