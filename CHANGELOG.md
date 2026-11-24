# Changelog

All notable changes to Notch. Versions follow [Semantic Versioning](https://semver.org); downloads are on the [Releases](https://github.com/Manish-Tamang/golesnotch/releases) page.

## 0.4.0 — 2026-11-24

### Added

- **Brightness.** Change the screen brightness and the notch shows the level, the same way it does for the volume. Works with the brightness keys on laptops; external monitors that support DDC/CI can be set from the notch too. Turn the popup off in Settings → Features.
- **Volume and brightness sliders** on the Connections page, under the Wi-Fi and Bluetooth tiles.
- **Drag the volume and brightness popups.** Drag along the bar, or scroll over the popup, to set the level without opening anything.
- **Bluetooth in the notch.** The arrow on the Bluetooth tile opens a list of your paired devices. Connect or disconnect headphones and speakers, and remove devices you no longer use. "Add device" finds nearby devices to pair; when a device shows a code, the notch shows it too so you can confirm. Devices that need a PIN typed on the PC are sent to Windows Settings.

## 0.3.0 — 2026-11-17

### Added

- **File shelf.** Drag files onto the notch to keep them close, then drag them out into a chat, an email or another folder. Works even when the notch is closed or asleep: it opens on the shelf, ready for the drop. Click a file to open it; "Drag all" takes every file at once. Only the paths are kept, never copies, and the shelf is still there after a restart.
- **Sleep.** After a while untouched, the notch tucks itself into a thin strip against the top edge, so it stays out of your way and doesn't cover what's behind it. Point at the strip to open it again. Popups still show, and it goes back to sleep afterwards. Choose Off, 5, 10 or 30 seconds in Settings → Behaviour.
- **Charging at a glance.** The open notch shows a battery with the percentage next to the date and time. It fills in the accent colour with a bolt while the charger is connected, and the idle clock says "Charging" or "Plugged in". The battery ring in the closed notch shows a bolt too.
- **Morphing content.** Opening the notch flies the clock, the album cover and a running timer from the pill into their places on the page, growing as they go, and back again on closing.

### Changed

- Views now blur slightly as they fade out and come into focus as they fade in, so switching between the pill, popups and pages reads as one smooth motion.
- The page icons open only once the pointer rests on the page dots for a moment, so sweeping past the bottom of the notch no longer opens them by accident. Opening them no longer makes the page above blur and refocus.
- The notch's top edge moves on its own spring, and the classic style's shoulders fade in and out instead of popping.
- Opening and closing check where the pointer really is, so the gap above a floating notch counts as part of it and the notch no longer closes when you push the mouse to the very top of the screen.

## 0.2.0 — 2026-11-11

First public release.

- Floating or classic style, moving on a physical spring.
- Now playing with cover, seek bar and controls; synced lyrics from LRCLIB beside the player and in the closed pill.
- Album colour glow.
- Notifications popup and page, with enlarge, open and dismiss.
- Focus timer with presets, shown in the closed pill.
- Charger, low battery, volume and device popups.
- Wi-Fi and Bluetooth switches, and connected drives, phones and Bluetooth devices with Open and Eject.
- Page dots that turn into page icons on hover; two-finger swipe between pages.
- Settings window with live changes.
- Stays on top without taking focus, and hides for fullscreen apps.
