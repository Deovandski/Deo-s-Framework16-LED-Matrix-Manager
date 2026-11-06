# Microsoft Store

The Store copy is an MSIX package of the same app. The Store signs it, installs it and updates it,
so it works with Smart App Control on and without SmartScreen warnings.

## How the Store copy differs

| | GitHub installer | Store (MSIX) |
|---|---|---|
| Signing | Not signed yet (see RELEASING.md) | Signed by the Store |
| Updates | Download the new installer | Automatic, through the Store |
| Start with Windows | The app's checkbox (registry Run key) | A startup task; switched in **Settings › Apps › Startup** (the app links there) |
| Settings and logs | `%AppData%` / `%LocalAppData%` | The same paths for the app; Windows keeps the files in the package's private folder, and the app's **Logs** link opens the real one |

The app spots the package at runtime (`PackageInfo.IsPackaged`). The package has one entry, the
Start menu tile, with a startup task attached. MSIX startup tasks can't pass arguments, so at start
the app asks Windows how it was launched (`AppInstance.GetActivatedEventArgs`, read by
`PackageInfo.StartedAtSignIn`). Launched by the startup task, it stays in the tray. Launched from
Start, it opens the window.

(A first version used a second, hidden entry that passed `--background`. The Store rejects that:
an entry with `AppListEntry="none"` counts as a "headless app" and needs a waiver from Microsoft.)

If someone installs both copies, the single-instance check lets only one of them run.

## Keeping the Store identity out of git

The identity values come from **Partner Center › your app › Product identity**:
`Package/Identity/Name`, `Package/Identity/Publisher` and `Package/Properties/PublisherDisplayName`.
They aren't secrets. Windows shows them for every installed package, and the package family name
appears in the Store link. Still, they don't need to be in the repository, so:

- `packaging/AppxManifest.template.xml` is committed with `{{placeholders}}`.
- `packaging/store-identity.json` holds the real values. It's in `.gitignore`. Create it from `packaging/store-identity.example.json`.
- Builds write the filled-in manifest only under `artifacts/`, which is also ignored.
- CI (later) reads the values from the repository secrets `STORE_IDENTITY_NAME`, `STORE_PUBLISHER`, `STORE_PUBLISHER_DISPLAY_NAME` (plus `STORE_DISPLAY_NAME` if the reserved name differs).

Before committing, `git status` should never list `packaging/store-identity.json`.

## Building

Requirements: the .NET 10 SDK and a Windows SDK (Visual Studio Installer › Individual components ›
**Windows 11 SDK**). Run in PowerShell from the repo folder:

```powershell
.\tools\Build-StorePackage.ps1              # builds artifacts\store\DeoLedMatrixManager_1.0.0.0_x64.msix
.\tools\Build-StorePackage.ps1 -Register    # also installs it on this PC for testing
```

- **Version:** the Store version is the `<Version>` from `Directory.Build.props` with `.0` added. Every submission needs a higher version than the last one.
- **`-Register`:** installs the unpacked package without signing. It needs **Developer Mode** (Settings › System › For developers). Close the app first. Remove the test copy later in Settings › Apps.
- **Logos:** the images in `packaging/Assets` come from `tools/make_store_assets.py`. Run it again if the icon changes.

**Test checklist for a `-Register` build:**
1. Start it from the Start menu: the window opens and the modules are found.
2. Settings › Apps › Startup lists "Deo's LED Matrix Manager". Sign out and back in: it starts in the tray, with no window.
3. The app's "Start with Windows" area shows the Settings link instead of the checkbox.
4. The Logs link opens a folder that contains `app.log`.
5. Close the lid: the LEDs turn off.

Optional: run the **Windows App Certification Kit** (installed with the Windows SDK) against the
.msix before uploading. It runs the same technical tests as Store certification.

## Submitting (Partner Center)

1. **Product:** open your reserved app, then **Start submission**.
2. **Pricing and availability:** Free, all markets.
3. **Properties:** Category **Utilities & tools**. Answer "No" to "accesses, collects or transmits personal information".
4. **Age ratings:** fill in the questionnaire. There's no user-generated content shared with others and no in-app purchases.
5. **Packages:** upload the `.msix`. It shows a warning that **runFullTrust** needs approval; that is expected. Give the reason under **Submission options › Restricted capabilities**: *"A desktop (WPF) app that drives the Framework Laptop 16 LED Matrix modules over USB serial and listens for display, lid and power events. It needs full trust to open serial ports and use those Win32 notifications."*
6. **Store listing:** use the text below, plus screenshots of the main window.
   - One screenshot showing an animation on both modules.
   - One showing text.
   - One showing the drawing editor.
   - Optional: the tray menu.
   Screenshots must be at least 1366×768.
7. **Submit for certification.** It takes up to 3 business days.

### Listing text (draft)

**Short description:** Patterns, animations and scrolling text for the Framework Laptop 16 LED Matrix modules, with automatic off when the laptop screen is off.

**Description:**

> Deo's LED Matrix Manager drives the LED Matrix input modules of the Framework Laptop 16 from the
> system tray.
>
> • Draw your own pictures, or start from the classic patterns, with greyscale brushes
> • 16 animations that loop seamlessly, from digital rain to a DNA helix, most of them reversible
> • Text that fits itself to the module: stacked, sideways or ticker, still or sliding, with live
>   time, date, battery, CPU and RAM
> • Optional CRT look and old-TV power effects
> • Two modules: independent, mirrored, or one picture spanning both
> • The LEDs switch off when the lid closes or the laptop screen goes dark (great when docked), and
>   come back when it returns
> • Light: it waits quietly in the tray, and simple scrolls run on the module itself
>
> Free and open source (MIT). Not affiliated with or endorsed by Framework Computer Inc.

**Search terms:** LED matrix, Framework Laptop 16, LED, input module, animation, tray.

## Later

- **CI:** have the release workflow also build the `.msix` (from the identity secrets) as a workflow artifact, to upload by hand.
- **Fully automatic:** submitting from CI with the Microsoft Store Developer CLI (`msstore`).
