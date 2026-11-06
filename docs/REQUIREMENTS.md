# Deo's Framework 16 LED Matrix Manager: Requirements

| | |
|---|---|
| Status | Document v0.3: animations, text and CRT effects added in app v0.2 (§4.7) |
| Owner | Deovandski |
| Last updated | 2026-11-04 |
| Related | [DEV-SETUP.md](DEV-SETUP.md), [PROTOCOL.md](PROTOCOL.md) |

## 1. Purpose

This is a lightweight Windows app for the Framework Laptop 16 **LED Matrix input modules**. Each module is a 9×34 white LED panel. The app:

- starts with Windows and lives in the system tray,
- finds the LED matrix modules, lets you name them, and keeps them in sync,
- lets you pick built-in and app-driven patterns and draw custom images,
- **turns the LEDs off when the laptop's own screen is off**, for example when docked with the lid closed or set to external displays only. It turns them back on when the laptop screen comes back.

The goal is an app that **feels native to Windows 11** and **uses almost no resources** when idle.

## 2. Scope

**In scope (v1):** Windows 10 22H2 and Windows 11, x64. LED Matrix module only (USB VID `0x32AC`, PID `0x0020`). One or two modules installed.

**Out of scope (v1):** B1 display (PID `0x0021`), C1 minimal module (PID `0x0022`), firmware flashing, Linux and macOS, ARM64 builds, and the built-in firmware games (Snake, Pong, and so on). These may come later.

## 3. Key technical facts that shape the design

These are confirmed from Framework's `inputmodule-rs` firmware source. Details are in [PROTOCOL.md](PROTOCOL.md).

1. **Connection:** USB CDC-ACM serial. Windows shows each module as a COM port with no driver needed. Commands are `0x32 0xAC <cmd> <params…>` at 115200 baud.
2. **Idle timeout:** The firmware **goes to sleep after 60 s without a command**. The app must send a keep-alive to hold the LEDs on.
3. **Any command wakes the module.** While the app wants the LEDs off, it must send **nothing** except the Sleep command.
4. **Firmware-side sleep:** USB suspend and the laptop's `SLEEP#` signal also put the module to sleep. After resume or replug, the module comes back empty, so the app must re-send state.
5. **COM ports are exclusive.** If another tool has the port open (the official `inputmodule-control`, the ledmatrix.frame.work web app, or another manager), our app can't open it. The app must say so clearly.
6. **No built-in left/right identity.** Each module reports a USB serial number. Its physical position has to come from the user, for example through an "Identify" flash.

## 4. Functional requirements

Priority: **M** = Must (v1), **S** = Should (v1 if time allows), **C** = Could (later).

### 4.1 Startup and tray

| ID | Requirement | Pri |
|---|---|---|
| FR-01 | The app can be set to start at sign-in, using a per-user `HKCU\…\Run` entry (no admin). On by default after the first run, with a toggle in Settings. | M |
| FR-02 | Only one copy runs at a time. Launching it again brings up the existing window. | M |
| FR-03 | A tray icon shows status: on, off (auto), paused, no device, port busy. The icon follows the light or dark taskbar theme. | M |
| FR-04 | Tray menu: Open, Turn LEDs on/off (manual override), quick pattern list, Pause auto-control, Settings, Exit. | M |
| FR-05 | Closing the window hides it to the tray. Exit only from the tray menu or File → Exit. | M |
| FR-06 | It starts quietly (tray only) when launched at sign-in, and opens the window when launched by hand. | M |

### 4.2 Device detection, naming and sync

| ID | Requirement | Pri |
|---|---|---|
| FR-10 | Lists LED matrix modules by matching USB VID `0x32AC` and PID `0x0020` to their COM ports, with no manual COM selection. | M |
| FR-11 | Hot-plug: notices modules being added or removed (input modules are hot-swappable) without a restart, through `WM_DEVICECHANGE`. | M |
| FR-12 | Re-scans and re-sends state after the PC resumes from sleep and after USB re-enumerates. | M |
| FR-13 | Reads and shows each module's firmware version (command `0x20`) and USB serial number. | M |
| FR-14 | **Identify** button: flashes the selected module (for example, all-on blinking for 3 s) so the user can tell which one it is. | M |
| FR-15 | The user can give each module a name (default "Left" or "Right" after Identify, or "Matrix 1/2"). Names are remembered by USB serial number. | M |
| FR-16 | **Two modules, Left and Right**, each with its own canvas (the same model as ledmatrix.frame.work). A **Swap** button switches which physical module is Left. Extra layout modes come later: *Mirror* (same content on both) and *Span* (one 18×34 canvas). | M (Mirror/Span: S) |
| FR-17 | **Sync:** In Mirror and Span modes, frames go to every module in the same render tick, and both modules change on/off together. | M |
| FR-18 | If a COM port is busy or fails, the UI shows the problem ("in use by another app"), retries with back-off, and never crashes or blocks the UI. | M |
| FR-19 | The app tries to detect left/right placement from USB location data, falling back to the user's naming. | C |

### 4.3 Auto on/off based on the laptop screen (**must-have**)

| ID | Requirement | Pri |
|---|---|---|
| FR-20 | **LEDs turn off when the laptop's internal screen isn't in use** and back on when it is. "In use" means the internal panel is part of the active Windows display layout and powered on. | **M** |
| FR-21 | The internal panel is identified with `QueryDisplayConfig`, looking for an active path whose target `outputTechnology` is `DISPLAYPORT_EMBEDDED` (11) or `UDI_EMBEDDED` (13), with `INTERNAL` (`0x80000000`) as a fallback. | M |
| FR-22 | Triggers, all event-driven with no polling loop: `WM_DISPLAYCHANGE` (layout changes such as Win+P, dock or undock), `GUID_LIDSWITCH_STATE_CHANGE` (lid open/closed), `GUID_SESSION_DISPLAY_STATUS` (screen timeout or monitor power), and resume from sleep. | M |
| FR-23 | Cases that must work: (a) docked with lid closed, using external monitors → **off**; (b) lid open with Win+P set to "Second screen only" → **off**; (c) undock or reopen the lid → **on**; (d) "Duplicate" or "Extend" that includes the laptop panel → **on**; (e) Windows turns the screen off after its timeout → **off**, and back on when it wakes. | M |
| FR-24 | Turning off sends `Sleep(true)` so the module fades out (the firmware does the fade). It also stops the keep-alive and all other traffic. Turning on sends `Sleep(false)`, then re-sends the current frame and brightness. | M |
| FR-25 | Changes are debounced (about 500 ms) so docking or undocking doesn't make the LEDs flicker on and off. | M |
| FR-26 | Each trigger can be switched on or off in Settings: internal screen inactive (on), lid closed (on), screen timeout (on), session locked (off by default). | S |
| FR-27 | A manual override in the tray ("Keep on" or "Keep off") takes priority over auto-control until it's cleared. | M |

### 4.4 Patterns and effects

| ID | Requirement | Pri |
|---|---|---|
| FR-30 | Firmware patterns: Gradient, Double gradient, ZigZag, Full brightness, "LOTUS" (both directions), Percentage (0–100). | M |
| FR-31 | Global brightness slider (0–255, shown as %), saved for each module or for the whole group. **Default 39%** (a Hatsune Miku easter egg). | M |
| FR-32 | Vertical scroll (firmware `Animate`) on and off. | M |
| FR-33 | App-driven animations rendered on the PC: breathing, blink, clock (HH:MM), scrolling text, battery %, CPU and RAM meters, and a random EQ. | S |
| FR-34 | Simple animation player: a list of frames with a delay per frame (default 100 ms) and looping. | S |
| FR-35 | Audio visualizer driven by system output. | C |
| FR-36 | Scheduled profiles (a different pattern by time of day or by foreground app). | C |

### 4.4a Main window UX (based on ledmatrix.frame.work)

The main window follows the official web app's layout, with Fluent styling:

- **Left sidebar**
  - *Devices*: a Left card and a Right card, each showing name, COM port, firmware version and status, with an **Identify** button.
  - Buttons: **Swap**, **Wake**, **Sleep**.
  - **Brightness** slider (shared by both modules).
  - *Drawing*: brush brightness.
  - *Automatic*: "Turn off when the laptop screen is off", "Start with Windows", and the live screen-state readout.
- **Main area**: two 9×34 LED grids side by side (Left, Right). Each has a **pattern dropdown**, **Clear** and **Invert**.
- **Drawing**: left mouse draws, right mouse (or Ctrl+left) erases, click and drag paints. Changes show on the hardware immediately.
- **Pattern list**: the web app's patterns (Blank, Full, Checkerboard ×4, Diagonal lines ×4, Every 2nd/3rd row/col, Diamond, Scatter 50%), plus greyscale Gradient, Double gradient and ZigZag, and Percentage.
- **Differences from the web app**: modules are detected automatically (no "Connect" dialog), the **Bootloader** button is left out (it's dangerous), and greyscale drawing is supported.

### 4.7 Animations, text and CRT effects (v0.2)

Specified in [proposals/ANIMATION-TEXT-CRT.md](proposals/ANIMATION-TEXT-CRT.md) (accepted). Summary:

| ID | Requirement | Pri | Status |
|---|---|---|---|
| FR-60 | Animation engine: one shared clock, modules in sync, latest frame wins, no output while the LEDs are off, clock stopped when nothing moves | M | Done |
| FR-61 | Built-in animations loop exactly (enforced by a unit test) | M | Done |
| FR-62 | Automatic firmware offload for seam-free downward scrolls | M | Done |
| FR-63 | 16 ready-made animations with speed (default: calm, 8 steps/s) and a "Reverse direction" option | M | Done |
| FR-64 | Text: Stacked (default), Sideways and Ticker layouts; auto-fit; Static and Slide motions; the gap between repeats is automatic (short text wraps around the screen) | M | Done |
| FR-65 | Text size: Auto-fit, Full size (5×7) or ½ size (3×5) (Bold later) | M | Partly |
| FR-66 | Live tokens `{time}`, `{date}`, `{battery}`, `{cpu}`, `{ram}` | S | Done |
| FR-67 | CRT power off/on when the content changes (optional; never replaces the fade on lid close) | S | Done |
| FR-68 | CRT look filter with intensity | S | Done |
| FR-69 | "No signal" animation | S | Done |
| FR-70 | Wrap preview in the drawing editor | S | Later |
| FR-71 | Independent / Mirror / Span, with an optional keyboard gap (default edge to edge) | S | Done |
| FR-72 | Frame-rate self-test | S | Later |

### 4.5 Custom drawing

| ID | Requirement | Pri |
|---|---|---|
| FR-40 | Pixel editor showing the 9×34 grid (or 18×34 in Span mode) with a live preview on the hardware. | M |
| FR-41 | Tools: pencil, eraser, fill, line, rectangle, clear, invert. Brush brightness 0–255 (greyscale). | M |
| FR-42 | Save and load drawings as named presets (JSON). Import a PNG or GIF (converted to 9×34 or 18×34 greyscale) and export to PNG. | M |
| FR-43 | Undo and redo (at least 50 steps). | S |
| FR-44 | Frame-by-frame animation editing (onion skin, duplicate frame). | C |

### 4.6 Settings and persistence

| ID | Requirement | Pri |
|---|---|---|
| FR-50 | Settings and presets are stored as JSON in `%AppData%\DeoLedMatrixManager\`, and logs in `%LocalAppData%\DeoLedMatrixManager\logs\` (rolling, 5 × 1 MB). | M |
| FR-51 | The last shown content is restored on startup and after resume. | M |
| FR-52 | Settings can be exported and imported. | C |

## 5. Non-functional requirements

| ID | Requirement | Pri |
|---|---|---|
| NFR-01 | **Lightweight:** idle CPU about 0% (event-driven, with a keep-alive every 30 s), less than 60 MB working set with the window closed, and the tray icon showing within 1 s of sign-in. | M |
| NFR-02 | **Native to Windows:** WPF Fluent theme that follows the system light or dark mode and accent color, Mica backdrop on Windows 11, Segoe UI Variable and Fluent icons. | M |
| NFR-03 | Per-monitor DPI awareness (v2). Sharp at 100–200% scaling and when moving between docked monitors. | M |
| NFR-04 | Accessibility: full keyboard navigation, UI Automation names on all controls, works in High Contrast themes, and the pixel editor can be used from the keyboard (arrow keys plus Space). | M |
| NFR-05 | No admin rights needed to install or run. No drivers. No background service. | M |
| NFR-06 | Unexpected errors are logged, the serial connection reconnects, and the app keeps running. | M |
| NFR-07 | Serial I/O runs off the UI thread, with a 20 ms write timeout to match the official tools. | M |
| NFR-08 | No telemetry and no network access. | M |
| NFR-09 | **Full installer** (Inno Setup 6): per-user install to `%LocalAppData%\Programs`, no admin. It's self-contained, so no separate .NET install is needed. Includes a Start menu shortcut, a "Start with Windows" option, a "Launch now" option, and a clean uninstall that removes the startup entry and settings on request. Published to GitHub Releases by CI when a `v*` tag is pushed. | M |
| NFR-11 | **Project website** on GitHub Pages (`site/`, deployed by GitHub Actions), with a download button pointing to the latest release, a feature overview and FAQ. | M |
| NFR-10 | Open source under the repo's existing LICENSE. | M |

## 6. Architecture and tech stack (proposed)

**Stack: C# on .NET 10 (LTS, supported until Nov 2028) with WPF and the built-in Fluent theme.**

Why WPF rather than the alternatives:

- **WPF with Fluent (chosen):** It's mature and light, and since .NET 9 it ships a Windows 11 Fluent theme (`ThemeMode="System"`) with accent color support. Tray icons are easy to add, startup is fast, and debugging in Visual Studio is excellent.
- **WinUI 3 / Windows App SDK:** It's the "most Windows 11" option, but it's heavier, has no built-in tray support, and adds packaging and runtime friction. It stays an option if pixel-perfect Windows 11 styling becomes the priority.
- **Rust or C++ with Win32:** Smallest footprint, but much slower to build a pixel editor and settings UI. Not worth it here.

Libraries: deliberately **no third-party runtime dependencies**, to keep the app light and avoid supply-chain risk.

- `System.IO.Ports` (Microsoft's package): serial communication. This is the only NuGet dependency of the app.
- Device discovery reads `HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_32AC&PID_0020\<serial>\Device Parameters\PortName` (read-only, no admin, no WMI), cross-checked against present COM ports and confirmed with a Version query.
- Tray icon: a small `Shell_NotifyIcon` wrapper with a WPF (Fluent) context menu.
- Win32 interop via hand-written P/Invoke: `QueryDisplayConfig`, `RegisterPowerSettingNotification`, `WTSRegisterSessionNotification`, `WM_DEVICECHANGE`.
- `xUnit`: tests only.

**Repo layout:**

```
/src/LedMatrix.Core           net10.0 (cross-platform) library: protocol encoding, frames,
                              patterns, device worker, screen policy, settings model
/src/LedMatrix.App            net10.0-windows WPF app: tray, main window, LED grid editor,
                              device discovery, screen/power monitor, autostart
/tests/LedMatrix.Core.Tests   xUnit tests for Core
/installer                    Inno Setup script
/site                         GitHub Pages website
/.github/workflows            ci.yml (build+test), release.yml (installer), pages.yml (website)
/docs                         REQUIREMENTS, DEV-SETUP, PROTOCOL
LedMatrixManager.slnx, Directory.Build.props, .editorconfig, .gitignore
```

**Main parts:**

- `MatrixDevice`: one per COM port. It runs a dedicated writer queue and handles encoding, keep-alive and reconnects.
- `DeviceManager`: finds modules, watches hot-plug, maps serial numbers to names and layout roles.
- `RenderEngine`: turns the active content (pattern, drawing or animation) into frames for each module, depending on the layout mode.
- `ScreenStateMonitor`: a hidden message window that combines the display layout, lid, display power and resume events into one `ShouldBeOn` signal, with debounce.
- `LedPowerController`: a state machine (On, TurningOff, Off, TurningOn) that applies the manual override and gates all device traffic.

## 7. Acceptance tests (for the must-have auto on/off)

1. Lid open, no external display: LEDs on and stay on longer than 2 minutes (keep-alive works).
2. Plug in a dock with an external monitor and close the lid: LEDs fade off within 2 s and stay off.
3. Open the lid while docked (panel rejoins the layout): LEDs come back on with the previous content.
4. Win+P → "Second screen only" with the lid open: off. Win+P → "Extend": on.
5. Screen timeout fires: off. Move the mouse: on.
6. Sleep and resume the laptop: the previous content comes back within 3 s of the desktop appearing.
7. Pull out and reinsert a module while running: it's detected again, with its name and content restored.
8. The web app has the COM port open: our app shows "port in use" and recovers once it's freed.

## 8. Decisions (2026-11-04)

1. **UI framework:** C# / .NET 10 with WPF and the Fluent theme. ✅
2. **Modules:** two, Left and Right, with UX modeled on ledmatrix.frame.work (§4.4a). ✅
3. **Distribution:** full installer (Inno Setup) on GitHub Releases. ✅
4. **Website:** GitHub Pages site for the project. ✅

Still open:

- **Lock screen:** should the LEDs turn off when Windows is locked, even with the screen on? Current default: no (toggle in Settings).
- **Code signing:** the installer is unsigned for now, so Windows SmartScreen will warn on first run. Signing (for example Azure Trusted Signing or SignPath for OSS) can come later.

## 9. References

- Framework `inputmodule-rs` (firmware, CLI, `commands.md`): https://github.com/FrameworkComputer/inputmodule-rs
- Framework Wiki, LED Matrix Manager page: https://frameworkwiki.pages.dev/docs/community-projects/led-matrix/led-matrix-manager
- Microsoft power setting GUIDs: https://learn.microsoft.com/windows/win32/power/power-setting-guids
- Microsoft `DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY`: https://learn.microsoft.com/windows/win32/api/wingdi/ne-wingdi-displayconfig_video_output_technology
- WPF what's new (Fluent theme): https://learn.microsoft.com/dotnet/desktop/wpf/whats-new
