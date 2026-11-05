# LED Matrix serial protocol: reference notes

These notes are taken from Framework's official firmware and CLI source in [`FrameworkComputer/inputmodule-rs`](https://github.com/FrameworkComputer/inputmodule-rs): `commands.md`, `fl16-inputmodules/src/control.rs`, `ledmatrix/src/main.rs` and `inputmodule-control/src/inputmodule.rs`. They're checked against the source, not just the docs, so the details below match firmware 0.2.x behavior.

## Transport

- USB CDC-ACM serial. Windows shows it as `USB Serial Device (COMx)` with no driver needed.
- **USB VID `0x32AC`**, PIDs: LED Matrix `0x0020`, B1 Display `0x0021`, C1 Minimal `0x0022`. Each device also reports a unique USB **serial number**.
- Official tools open the port at **115200 baud** with a **20 ms timeout**. Because it's CDC-ACM, the baud rate doesn't really matter, but use 115200 anyway.
- The firmware reads **up to 64 bytes per USB read and parses one command per read**. So **send each command as its own write and keep it to 64 bytes or less**. Don't batch several commands into one buffer.

## Frame format

```
0x32 0xAC <command id> <parameters...>
```

If a "get" command is sent with **no parameters**, the firmware answers with a **32-byte response** (the value is in byte 0 unless noted). Most "set" commands return nothing.

## Commands used by this app (LED Matrix)

| Cmd | ID | Params | Response | Notes |
|---|---|---|---|---|
| Brightness | `0x00` | `u8` 0–255 | none, or 32 B (byte 0 = brightness) if no params | Global brightness scaling |
| Pattern | `0x01` | `u8` pattern id | none | See the pattern table. Percentage needs a second byte: `01 00 <0-100>` |
| Bootloader | `0x02` | none | none | **Don't use.** It reboots into the UF2 bootloader |
| Sleep | `0x03` | `u8` 1 = sleep, 0 = wake | none, or 32 B (byte 0 = sleeping) if no params | The firmware fades LEDs out and in |
| Animate | `0x04` | `u8` bool | none, or 32 B if no params | Scrolls the current image vertically |
| Panic | `0x05` | none | none | **Don't use** (crashes the firmware) |
| DrawBW | `0x06` | 39 bytes | none | 1 bit per LED, see below |
| StageCol | `0x07` | `u8` column x (0–8), then 34 × `u8` brightness | none | Greyscale. Stages one column |
| FlushCols | `0x08` | none | none | Shows all staged columns at once (no tearing) |
| StartGame | `0x10` | `u8` game id | none | Out of scope for v1 |
| AnimationPeriod | `0x1C` | `u16` **little-endian**, milliseconds | none, or 32 B if no params | Scroll speed when Animate is on |
| PwmFreq | `0x1E` | `u8` enum (29 kHz, 3.6 kHz, 1.8 kHz, 900 Hz) | none, or 32 B | |
| DebugMode | `0x1F` | `u8` bool | none, or 32 B | |

> `0x14`–`0x19`, `0x1A` (Fps) and `0x1B` (PowerMode) are **B1 display only**. The LED Matrix firmware ignores them.
| Version | `0x20` | none | 32 B | `b0` = major, `b1` = minor (high 4 bits) and patch (low 4 bits), `b2` = 1 if pre-release |

### Pattern ids (`0x01`)

`0x00` Percentage (plus a 0–100 byte) · `0x01` Gradient · `0x02` Double gradient · `0x03` "LOTUS" sideways · `0x04` ZigZag · `0x05` Full brightness · `0x06` "PANIC" · `0x07` "LOTUS" top-down

### DrawBW bit packing (`0x06`)

The panel is 9 columns (x) by 34 rows (y), 306 LEDs. The payload is 39 bytes (`ceil(306/8)`):

```
i = x + 9*y          // x: 0..8, y: 0..33
bytes[i / 8] |= 1 << (i % 8)   // set = LED on
```

### Greyscale frame

For x = 0..8: `32 AC 07 <x> <34 brightness bytes, top to bottom>`, then `32 AC 08`. That's 10 writes per frame, about 38 bytes each.

## Sleep behavior (important for auto on/off)

The module sleeps (LEDs and controller off, with a fade) when:

- it receives **Sleep(1)**,
- the **idle timer** runs out: **60 s with no command** (not in debug mode),
- **USB suspends** (Windows selective suspend or system sleep),
- the laptop's **`SLEEP#`** line is asserted (host sleep).

It wakes on **any command** (not just Sleep(0)), or when USB resumes or `SLEEP#` is released. The idle timer resets on every command.

What this means for the app:

1. **Keep-alive:** while on, re-send something harmless (for example Brightness with the current value) at least every 30 s, unless frames are already being sent.
2. **While off, send nothing.** Any command wakes the module.
3. After USB resume or replug, treat the module as blank and **re-send brightness and the current frame**.

## Hardware notes

- RP2040 MCU driving an IS31FL3741A LED controller.
- **DIP1** = debug mode: starts fully lit, never sleeps from the timeout, and when sleeping it shows the reason (`SLP#`, `USB`, `CMD`).
- **DIP2** = bootloader: the module shows up as the `RPI-RP2` drive for UF2 flashing.
- Community reports say out-of-range values can lock up the module until it's reseated, so **always validate column index and lengths**.
