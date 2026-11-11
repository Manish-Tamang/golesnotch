# Architecture

This explains how Notch is put together, why a few things are done the way they are, and how to add to it.

## The window

`Views/NotchWindow` is one borderless, transparent, always-on-top window, 760 × 400 pixels, centred at the top of the primary monitor. It never moves or resizes.

Windows treats fully transparent pixels of such a window as not there: clicks on them go to whatever is behind. So the only "solid" part is the black island drawn inside it.

`Interop/Native.MakeOverlay` adds two window styles: one that keeps it out of the taskbar and Alt+Tab, and one that stops it taking keyboard focus when clicked. That second style is why the notch has buttons but no text boxes: it can't receive typing.

**Staying on top.** The window is always-on-top, but another always-on-top window used after it would still cover it. `Native.WatchForeground` reports every time a different window comes forward, and the notch then puts itself back in front (`Native.BringToTop`). The same moment is used to check for a fullscreen app. Windows' own overlays (Alt+Tab, Start, Task View) cover the whole screen while open, and so do the task switchers and launchers of replacement desktop shells such as Seelen UI. `Native.IsFullscreenAppOnPrimary` recognises them by window class and process name (`ShellProcesses`) so the notch doesn't hide for them, and a fullscreen window has to stay in front for over a second before the notch hides at all. They are drawn in a system layer no ordinary app can draw above.

**Sideways scrolling.** WPF has no event for horizontal scrolling, so `NotchWindow.OnWindowMessage` listens for the raw `WM_MOUSEHWHEEL` message that a two-finger sideways swipe produces. `OnScroll` turns one swipe into one page change by ignoring the rest of the swipe's momentum.

## The island and why it animates smoothly

The black island is `Controls/IslandShape`, which draws a single rounded rectangle, its `Outline`. Opening, closing and resizing the notch means changing that rectangle's size and corner radius. The same geometry is used as the clip for the content, so content can never spill outside the black.

Two things make it feel smooth.

**Nothing is laid out again while it moves.** The obvious way to resize something in WPF is to animate its `Width` and `Height`. That re-runs layout for everything inside it on every frame, and the animation stutters. Changing a geometry only touches the renderer.

**It moves on a spring, not a timed curve.** The island's width, height and radius are each a `Core/Spring`: a small physics simulation stepped once per frame. A spring has a velocity and keeps it when the target changes. A timed animation that is interrupted has to start again from a standstill, which shows as a hitch when you sweep the mouse in and out. `IslandShape.MoveTo` just gives the springs a new target. Growing uses a slightly bouncy spring; shrinking uses one that settles without overshoot.

The same idea applies to content:

- Every view and page is laid out once, at its own size, and is not resized while the island moves. Where a real layout change is needed (the music player narrows when lyrics open beside it), it happens in one step while that panel is faded out.
- Things that move (pages sliding, the page dots following the bottom edge, lyrics scrolling) use render transforms, not layout.

In classic style the rectangle starts above the top of the screen, so its top corners are cut off, and two small shoulder shapes follow its sides to curve it into the screen edge.

The album glow is a gradient drawn over the same outline, so it follows the island for free.

## Modes

The notch is always in one of three modes (`NotchWindow.Mode`):

| Mode | Shows | Size |
| --- | --- | --- |
| Collapsed | The pill: battery ring or cover, clock, equalizer bars; or the current lyric line | Width from settings (340 with a lyric line) × 32 |
| Hud | A short popup: a status banner, the volume bar or a notification card | 380 × the content's height |
| Expanded | One of the pages, plus the page dots or page icons | The page's width × its height |

`SetMode` does two things: `ApplyIsland` sends the island towards the new mode's size, and `ApplyViews` cross-fades the stacked views so only the right one is visible.

Hovering the page dots sets a flag that adds a little height and swaps the dots for `Controls/PageTabs`, the row of page icons. The row appears where the dots were and the island grows down around it.

## Pages

A page is a `UserControl` that derives from `Views/NotchPage`. It reports how big the notch should be through `PageWidth` and `PageHeight`, and can change them at any time (the music page widens when lyrics open). `IconGlyph` is its icon in the page icon row. `OnShown` and `OnHidden` tell it when to start and stop timers and animations.

### Adding a page

1. Create `Views/MyPage.xaml` with `<v:NotchPage ...>` as its root, and `MyPage.xaml.cs` deriving from `NotchPage`. Take `AppServices` in the constructor.
2. Lay it out at its full size (440 wide unless you set `PageWidth`). Set `PageHeight` and `IconGlyph`.
3. In `NotchWindow`, create it next to the other pages in the constructor and add it to the list in `UpdatePages`.
4. If it should be optional, add a switch to `AppSettings` and the settings window.

## Services

Services live in `Services/` and know nothing about the UI. `AppServices` creates them once and is handed to every view.

| Service | Source | Notes |
| --- | --- | --- |
| `MediaService` | Windows' system media controls | The same data as the volume flyout. Events arrive on a background thread. A missing cover is retried for a few seconds, because browsers supply theirs late. |
| `LyricsService` | lrclib.net | One search per song, cached in memory. Prefers synced lyrics whose length matches the track. |
| `LyricsSession` | `MediaService` + `LyricsService` | Whether lyrics are on, the current song's lyrics and the line being sung. Shared by the music page and the closed pill. |
| `AlbumColor` | The cover image | Groups pixels by hue and averages the most vivid group. |
| `NotificationService` | `UserNotificationListener` | Polls every 1.5 seconds, because the API's change event only works for packaged apps. |
| `PowerService` | `PowerManager` | Battery level, charging and charger state. |
| `VolumeService` | Core Audio (`Interop/CoreAudio.cs`) | Follows the default playback device and is called back when its volume changes. |
| `RadioService` | `Windows.Devices.Radios` | The Wi-Fi and Bluetooth on/off switches. |
| `DeviceService` | `DeviceWatcher` | Storage devices and connected Bluetooth devices. Opens them, and ejects drives through Explorer's own Eject command. |
| `FocusTimer` | A `DispatcherTimer` | Counts down against the clock, so it stays accurate if the UI is busy. |
| `StartupRegistration` | The registry's Run key | Per user; no admin rights. |

Services may raise events on any thread. Views always switch to the UI thread with `Dispatcher.InvokeAsync` before touching controls.

## Settings

`Core/AppSettings` is a plain class saved as JSON by `Core/SettingsStore`. The settings window calls `SettingsStore.Update`, which saves and raises `Changed`. `NotchWindow.ApplySettings` and the pages listen and update live.

The accent colour is a brush resource named `AccentBrush`. `Core/Theme.ApplyAccent` replaces it, and everything that uses it through `DynamicResource` follows.

## Animation

The island moves on springs, as described above. Everything else (fades, page slides, lyric scrolling) is a timed animation started through `Core/Motion`, so they share the same curves. Both obey the animation speed setting.

- `Motion.Spring()`, a soft bounce, is used when something opens or grows.
- `Motion.Settle()`, no bounce, is used when something closes or shrinks.
- `Motion.To` animates from the property's current value, so an animation that interrupts another continues from where it was instead of jumping.

`Core/SpringEase` is the spring-shaped easing curve behind both. It is not the same thing as `Core/Spring`: the easing curve runs for a fixed time, the spring is simulated.

## Errors

A widget failing shouldn't close the notch. Unhandled UI exceptions are written to `%APPDATA%\Notch\crash.log` and the app carries on. Services catch their own failures (no network, no permission, an app closing mid-read) and fall back to an empty state.
