# Notch

[![Build](https://github.com/Manish-Tamang/golesnotch/actions/workflows/build.yml/badge.svg)](https://github.com/Manish-Tamang/golesnotch/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/Manish-Tamang/golesnotch)](https://github.com/Manish-Tamang/golesnotch/releases/latest)

A small, animated notch for the top of your Windows screen. Closed, it is a pill showing the clock and battery. Hover it and it springs open into music controls with synced lyrics, your notifications and a focus timer.

Written in C# with WPF. It runs natively, uses no web view, and has no dependencies beyond .NET.

## Features

- **Floating or classic style.** Floating sits a few pixels below the top edge as a rounded pill. Classic is joined to the edge, with shoulders that curve out into it.
- **Spring motion.** The notch moves on a physical spring, so opening and closing it in quick succession stays smooth.
- **Now playing.** Cover, title, artist, a progress bar you can click to seek, and play/pause/skip. Works with Spotify, browsers and most other players.
- **Lyrics.** Click the lyrics button and the notch widens to show the lyrics beside the player, scrolling in time with the song. Close the notch and the line being sung stays in the pill.
- **Album colour.** The notch takes on a soft glow in the colour of the album cover.
- **Page icons.** Hover the page dots and they become a row of page icons with a settings button.
- **Notifications.** New Windows notifications pop up in the notch. Click one to enlarge it and read the whole message, then open the app or dismiss it. A page lists the recent ones.
- **Focus timer.** A countdown with 15, 25, 45 and 60 minute presets. The time left shows in the closed notch.
- **Charger and battery popups.** A short popup when the charger is plugged in or removed, and at 20% battery.
- **Volume popup.** Change the volume and the notch shows the level on a bar. The music page has a mute button.
- **Wi-Fi and Bluetooth.** Switch either on or off from the notch, and open Windows' list of networks or Bluetooth devices.
- **Devices.** A popup when a USB drive, phone or Bluetooth device connects or disconnects; click it to open the device. The same page lists what is connected, with Open and Eject. Useful if you run without the taskbar tray.
- **Settings window.** Style, width, gap, accent colour, hover or click to open, animation speed, and a switch for every feature.
- **Stays on top, out of the way.** It keeps itself in front of other windows, never takes keyboard focus, doesn't appear in the taskbar or Alt+Tab, lets clicks through around the pill, and hides while a fullscreen app is in front.

## Download

Get the latest `Notch.exe` from [Releases](https://github.com/Manish-Tamang/golesnotch/releases/latest). The **portable** zip runs on its own; the smaller one needs the .NET 10 Desktop Runtime. Windows SmartScreen may warn you the first time because the exe isn't code-signed: click **More info → Run anyway**.

## Requirements

- Windows 10 (version 2004 or later) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build it

## Run from source

In PowerShell, from the project folder:

```powershell
dotnet run
```

The first run takes a little longer while it builds.

## Build a standalone exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

This creates `publish\Notch.exe`. It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) on the PC that runs it. Use this exe, not `dotnet run`, if you turn on **Start with Windows**: that setting registers whichever exe is running at the time.

## Using it

| Action | What happens |
| --- | --- |
| Hover the notch (or click, if set) | It opens |
| Move the mouse away | It closes |
| Swipe two fingers sideways on the touchpad | Switch between pages |
| Scroll the mouse wheel, or click a dot | Switch between pages |
| Hover the page dots | Show the page icons and the settings button |
| Click the lyrics button on the music page | Turn lyrics on or off |
| Click a notification | Enlarge it; click again to shrink it |
| Click a "device connected" popup | Open the drive or device |
| Click the Wi-Fi or Bluetooth tile | Switch it on or off; the arrow opens Windows' list |
| Right-click the notch | Settings and Quit |

## Releasing

Push a version tag and GitHub Actions builds both zips and publishes the release:

```powershell
git tag v0.3.0
git push origin v0.3.0
```

Every push to `main` is also built, so compile errors show up on the Actions tab.

## Settings

Right-click the notch and choose **Settings**, or hover the page dots and click the gear. Changes apply immediately.

| Setting | Default | Notes |
| --- | --- | --- |
| Style | Floating | Floating or Classic |
| Width | 168 px | Width of the closed notch, 130 to 230 px |
| Gap from the top | 8 px | Floating style only |
| Accent colour | Green | Battery ring, timer ring, active buttons |
| Open the notch | Hover | Hover or Click |
| Animation speed | Normal | Relaxed, Normal or Fast |
| Page icons | On | Hovering the page dots shows page icons |
| Hide in fullscreen | On | Hides while a fullscreen app is focused |
| Start with Windows | Off | Adds the running exe to your sign-in apps |
| Notifications page | On | |
| Focus timer page | On | |
| Devices page | On | Wi-Fi and Bluetooth switches, and connected devices |
| Lyrics | On | See Privacy below |
| Album colour glow | On | Tints the notch with the cover's colour |
| Notification popups | On | |
| Charger and battery popups | On | |
| Device popups | On | |
| Volume popup | On | |

Settings are stored in `%APPDATA%\Notch\settings.json`.

## Privacy

- **Lyrics** come from [LRCLIB](https://lrclib.net), a free and open lyrics database. When you open lyrics, the song's title and artist are sent to it. Nothing else is sent, and nothing is sent if you turn Lyrics off.
- **Notifications** are read from Windows on your PC and shown. They are not stored or sent anywhere. Windows asks for your permission the first time; you can change it later in Windows Settings under Privacy & security, Notifications.
- There is no telemetry and no account.

## Known limits

- The notch shows on the primary monitor only.
- Opening a notification brings up the app that sent it. Windows doesn't let another app trigger the notification's own click action, so it can't jump to the exact message.
- Reading notifications depends on Windows allowing it for apps installed outside the Store. If Windows refuses, the notifications page says so and the rest of the app works as normal.
- Lyrics depend on what LRCLIB has. Browser videos with long titles often have none.
- "Dismiss" and "Clear all" also try to remove the notifications from Windows' own notification centre. Where Windows refuses, they are still hidden in the notch.
- The notch hides for fullscreen apps, not for task switchers or launchers. If it hides for a tool it shouldn't, add that tool's process name to `ShellProcesses` in `Interop/Native.cs`, or turn off **Hide in fullscreen**.
- Windows draws a few of its own screens (Alt+Tab, Start, Task View, the lock screen) in a layer above every app, so the notch can be covered while those are open. It is back as soon as they close.
- The Devices page lists storage and Bluetooth devices. Other USB devices, such as mice and keyboards, aren't listed.

## Project layout

```
App.xaml(.cs)        Start-up: single instance, services, the notch window
Core/                Settings, theme colour, the spring and animation helpers
Services/            Media, lyrics, notifications, battery, volume, radios, devices, focus timer
Controls/            Small custom controls (island shape, rings, bars, dots, page icons, lyrics)
Views/               The notch window, its pages and popups, the settings window
Themes/              Shared XAML styles and icons
Interop/             Win32 and WinRT helpers
docs/                Architecture notes
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together and how to add a page.

## Roadmap

- Frosted glass background
- Tray icon
- Choice of monitor
- To-do list and a shelf for files

## License

[MIT](LICENSE)

## Acknowledgements

Inspired by the notch on MacBooks and by the notch apps made for it and for Windows. This project is written from scratch and shares no code or assets with any of them. Lyrics are provided by [LRCLIB](https://lrclib.net).
