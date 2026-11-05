# Deo's Framework 16 LED Matrix Manager

A lightweight Windows tray app for the Framework Laptop 16 **LED Matrix** input modules.

- **Two modules, side by side.** Draw on the Left and Right matrices (greyscale), or pick a pattern. The layout follows the official [ledmatrix.frame.work](https://ledmatrix.frame.work) tool.
- **Turns off with the laptop screen.** When you dock with the lid closed, choose "Second screen only", or Windows turns the screen off, the LEDs fade out. They come back when the laptop screen does.
- **Lives in the tray.** Starts with Windows, uses almost nothing while idle, and detects modules automatically, including hot-swaps and resume from sleep.
- **Native and light.** C# / .NET 10 / WPF with the Windows 11 Fluent theme. No admin rights, no drivers, no telemetry.

**Website:** https://deovandski.github.io/Deo-s-Framework16-LED-Matrix-Manager/
**Download:** [latest release](https://github.com/Deovandski/Deo-s-Framework16-LED-Matrix-Manager/releases/latest)

## Repository layout

```
src/LedMatrix.Core          protocol, frames, patterns, device worker, on/off rules, settings
src/LedMatrix.App           WPF tray app (window, LED editor, detection, screen monitoring)
tests/LedMatrix.Core.Tests  xUnit tests (no hardware needed)
installer/                  Inno Setup script
site/                       GitHub Pages website
docs/                       requirements, developer setup, protocol notes
```

## Docs

- [Requirements](docs/REQUIREMENTS.md)
- [Developer setup, building and releasing](docs/DEV-SETUP.md)
- [LED Matrix serial protocol notes](docs/PROTOCOL.md)

## Status

v0.1 is in development. Not affiliated with Framework Computer Inc.
