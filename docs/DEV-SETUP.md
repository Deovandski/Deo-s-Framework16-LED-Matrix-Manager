# Developer setup (Windows PC)

This is what to install on the Framework 16 to build, run and debug the app against the real LED matrix modules.

## 1. Required

| Tool | Why | Install (PowerShell or Terminal) |
|---|---|---|
| **Visual Studio 2026 Community** with the **.NET desktop development** workload | IDE, WPF designer, debugger, includes the .NET 10 SDK | `winget install --id Microsoft.VisualStudio.Community -e --override "--add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended --passive"` |
| **.NET 10 SDK** (LTS) | Command-line builds and tests. Visual Studio already installs it, so this is only needed without VS. | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| **Git for Windows** | Command-line git. Skip it if GitHub Desktop is enough for you. | `winget install --id Git.Git -e` |

Check it worked (open a new terminal):

```powershell
dotnet --list-sdks     # should list a 10.0.x SDK
git --version
```

> If you already have Visual Studio, open **Visual Studio Installer → Modify** and tick **.NET desktop development** instead.

## 2. Recommended for hardware debugging

| Tool | Why | Install |
|---|---|---|
| **Framework `inputmodule-control.exe`** | The official CLI. Use it to check the modules work and to compare behavior with our app. | Download from the [inputmodule-rs releases](https://github.com/FrameworkComputer/inputmodule-rs/releases) |
| **PowerToys** (optional) | Its color picker and screen ruler help with UI polish. | `winget install --id Microsoft.PowerToys -e` |
| **VS Code + C# Dev Kit** (optional) | A lighter editor than Visual Studio. | `winget install --id Microsoft.VisualStudioCode -e` |

Quick hardware check with the official CLI:

```powershell
.\inputmodule-control.exe --list                 # shows each module's COM port, VID/PID and serial number
.\inputmodule-control.exe led-matrix --pattern zigzag
```

## 3. Windows settings worth knowing

1. **Device Manager → Ports (COM & LPT):** each LED matrix shows up as a *USB Serial Device (COMx)*. No driver is needed.
2. **Only one app can open a COM port at a time.** Close the official CLI, the web app (ledmatrix.frame.work) and any other LED tool before debugging ours, or the port will be "in use".
3. **USB selective suspend:** if the modules turn off on their own while our app is running, check *Control Panel → Power Options → Change plan settings → Advanced → USB settings → USB selective suspend*. The firmware sleeps whenever Windows suspends the USB device. The app will handle this, but it's useful to know while debugging.
4. **Module DIP switch 1 = firmware debug mode.** When it's on, the module never sleeps from the idle timeout, and when it does sleep it **shows the reason** (`SLP#`, `USB`, `CMD`) instead of going dark. This is very handy for testing the auto on/off feature. (Remove the module to reach the switch. DIP2 is the bootloader switch, so leave it off.)
5. **Developer Mode** (*Settings → System → For developers*) is **not** needed for the portable build. It only matters later if we sideload an MSIX package.

## 4. Build, run and debug

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\GitHub\Deo-s-Framework16-LED-Matrix-Manager"
dotnet build LedMatrixManager.slnx
dotnet test tests\LedMatrix.Core.Tests
dotnet run --project src\LedMatrix.App
```

Or open **`LedMatrixManager.slnx`** in Visual Studio, right-click **LedMatrix.App**, choose **Set as Startup Project**, and press **F5**.

What's where:

| Project | What it holds |
|---|---|
| `src/LedMatrix.Core` | Protocol encoding, frames, patterns, the per-module worker (`MatrixDevice`), the on/off rules (`ScreenPolicy`) and settings. Cross-platform, with no Windows code. |
| `src/LedMatrix.App` | WPF app: tray icon, main window, LED grid editor, module detection, screen and power monitoring, start with Windows. |
| `tests/LedMatrix.Core.Tests` | xUnit tests for Core, using a fake serial port and a fake clock, so no hardware is needed. |

### Debugging the screen auto on/off

- Every event (display change, lid, display power, lock, resume) and every on/off decision is logged to `%LocalAppData%\DeoLedMatrixManager\logs\app.log`. The **Logs** link at the bottom of the window opens that folder. Debug builds log extra detail.
- Useful breakpoints: `ScreenStateMonitor.Publish` (what Windows reported) and `AppController.ApplyPower` (the decision).
- Easy triggers: **Win+P** (PC screen only, Duplicate, Extend, Second screen only), closing the lid while docked, and `powercfg /change monitor-timeout-ac 1` to test screen timeout quickly. Put your normal timeout back afterwards.
- Closing the lid puts the PC to sleep unless an external monitor is attached or *"When I close the lid"* is set to **Do nothing** in Power Options.
- Turn on the module's **DIP1** switch to see *why* the firmware went to sleep (`SLP#`, `USB`, `CMD`).

### Settings file

`%AppData%\DeoLedMatrixManager\settings.json`. Delete it to start fresh.

## 5. Installer (optional locally)

CI builds the installer for you (see §6). To build it on your PC:

```powershell
winget install --id JRSoftware.InnoSetup -e
dotnet publish src\LedMatrix.App -c Release -o publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.1.0 installer\LedMatrixManager.iss
# Output: installer\Output\DeoLedMatrixManager-Setup-0.1.0.exe
```

The installer is per-user (no admin) and self-contained (no separate .NET install needed). It adds a Start menu shortcut and an optional "start with Windows" entry, and its uninstaller cleans up.

## 6. GitHub: CI, releases and website

| Workflow | Runs when | Does |
|---|---|---|
| `ci.yml` | Every push or PR | Builds the whole solution on Windows and runs the tests |
| `release.yml` | You push a tag like `v0.1.0` | Tests, publishes, builds the installer, creates a GitHub Release with the `.exe` attached |
| `pages.yml` | `site/` changes on `main` | Deploys the website to GitHub Pages |

**One-time setup for the website:** on GitHub, open the repo's **Settings → Pages**, and under *Build and deployment* set **Source** to **GitHub Actions**. The site will be at <https://deovandski.github.io/Deo-s-Framework16-LED-Matrix-Manager/>.

**To release a version:**

```powershell
git tag v0.1.0
git push origin v0.1.0
```
