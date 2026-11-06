# Proposal: animations, text and CRT effects (v0.2)

| | |
|---|---|
| Status | **Accepted and implemented** (2026-11-05). See §12 for the decisions and what changed while building. |
| Builds on | [REQUIREMENTS.md](../REQUIREMENTS.md) FR-30–FR-36 (patterns and effects), FR-16 (layout modes) |
| Preview | [LED Motion Lab](https://claude.ai/artifact/Y5ZDb6uHSo9V7Rj5ttzJf8): a live preview built with the same algorithms proposed here (private to the repo owner) |

## 1. Summary

The website shows LEDs moving, but the app only shows still images. This proposal adds movement in three forms:

1. **Animations.** Scrolling patterns that loop without a visible jump, plus a set of ready-made animations.
2. **Text.** Type something and it's fitted to the module, then either stays put or slides vertically or horizontally.
3. **CRT effects.** An old-TV power on/off, a CRT "look" filter that can go over any content, and a "no signal" static screen.

All three run on one small animation engine. The engine follows the app's rules: nothing runs while the LEDs are off, and nothing runs at all when the content is still.

## 2. What the hardware can do

These are checked against the firmware source (`inputmodule-rs`). Frame rates are estimates until we measure them on your modules.

| Capability | How it works | Cost |
|---|---|---|
| **Firmware scroll** (`Animate`, `0x04`) | The module rotates its own image **down one row per tick**. The bottom row wraps to the top. The default is 32 ticks a second, adjustable through `AnimationPeriod` (`0x1C`, in ms). | **Zero.** No USB traffic and no PC CPU. The module does it alone. |
| Black-and-white frame (`DrawBW`) | One 42-byte command per frame. | Cheap. 30–60 fps is realistic. |
| Greyscale frame (`StageCol` ×9 + `FlushCols`) | 10 commands per frame. The firmware refreshes the LEDs after **every** command (about 3.5 ms each over its 1 MHz I²C bus). | About 35–40 ms per frame on the module, so **about 20 fps** is the practical target. |
| Brightness only (`0x00`) | One tiny command. | Cheap. Good for breathing and pulsing. |
| Built-in Game of Life (`StartGame`, `0x10`) | Runs entirely on the module. | Zero. |

The two modules have separate USB ports, so they update in parallel. "20 fps" means 20 fps on both at once.

## 3. Seamless loops ("congruent" patterns)

### The problem

Scrolling a fixed picture means rotating it: the row that falls off the bottom comes back at the top. That's exactly what the firmware's scroll and the website demo do. A rotated picture only loops cleanly if the pattern repeats an exact number of times in 34 rows.

34 = 2 × 17, so only patterns that repeat every **1, 2, 17 or 34 rows** loop cleanly. Everything else shows a jump where the bottom meets the top. For example, diagonal lines that repeat every 4 rows:

```
row 32  ##..   <- pattern continues...
row 33  .##.
-------- wrap (34 is not a multiple of 4)
row 0   #..#   <- ...but restarts here: the stripe jumps sideways
row 1   ##..
```

### The fix: two engines

| | **Procedural engine (in the app)** | **Firmware scroll (on the module)** |
|---|---|---|
| How | Each frame is computed from a formula over an endless surface, `pixel(x, y, t)`, so there's no edge and no seam. | The module rotates a picture we upload once. |
| Seamless | **Always**, by construction. Each animation declares its loop length. For example, stripes repeating every 4 rows loop every 4 frames at 1 px per frame. | Only for content whose vertical repeat is 1, 2, 17 or 34 rows, or content drawn to wrap. |
| Directions | Up, down, left, right, diagonal. | **Down only.** |
| Smooth sub-pixel motion | Yes, in greyscale (anti-aliased). | No (whole rows). |
| Cost | Small, about 20 fps while visible. | Zero. |

**How the app chooses:** a **planner** offloads to the firmware automatically whenever it can: downward motion, seam-free content, no filter on top. Otherwise it uses the procedural engine. You never pick an engine. You just see "Runs on the module (0% CPU)" when the offload applies.

**For your own drawings**, the editor gets a **Wrap preview**. It shows the bottom of the drawing joined to the top, with a seam marker, so you can draw across the join and make any drawing scroll seamlessly.

**A test enforces it:** for every built-in animation, a unit test checks that frame number *LoopLength* is identical to frame 0. That makes "congruent" a rule the code can't break.

## 4. Ready-made animations (first set)

| Name | What you see | Engine | Loops after | Tones |
|---|---|---|---|---|
| Marching stripes | Diagonal stripes sliding down | App (4-row period doesn't divide 34) | 4 frames | B/W |
| Checker march | Checkerboard stepping one cell | Firmware | 2 frames | B/W |
| Chevrons | V-shapes flowing upward | App | 8 frames | B/W |
| Diamonds | The diamond pattern scrolling down (added after v0.2) | App | 10 frames | B/W |
| Sine wave | A smooth wave travelling down | App | 34 frames | Greyscale |
| Helix | Two strands crossing like DNA | App | 34 frames | Greyscale |
| Digital rain | Falling drops with fading tails | App | Continuous (random) | Greyscale |
| Snowfall | Slow flakes drifting down and piling briefly | App | Continuous | Greyscale |
| Plasma | Soft shifting light | App | 64 frames | Greyscale |
| Fire | Flickering flames rising from the bottom | App | Continuous | Greyscale |
| Equalizer | Bouncing bars | App | Continuous | B/W |
| Heartbeat | An ECG trace scrolling, with a pulse | App | 34 frames | Greyscale |
| Bouncing ball | A ball bouncing, crossing between modules in Span mode | App | Continuous | B/W |
| Breathing | A diamond pattern slowly pulsing | App | 60 frames | Greyscale |
| Game of Life | The classic cellular automaton | App (firmware version possible later) | Endless | B/W |
| No signal | CRT static with a rolling bar | App | Continuous | Greyscale |

Every animation has **speed** and **direction** where they make sense, works on each module independently, and can **Mirror** or **Span** both (Span = one 18×34 canvas). In Span mode you can optionally add a **keyboard gap**: a few hidden virtual columns between the modules, so a ball or ticker that crosses over "passes behind" the keyboard instead of jumping.

## 5. Text

Type text and choose a **layout**, a **fit** and a **motion**.

### Layouts

The module is 9 pixels wide and 34 tall, so text can sit three ways:

| Layout | Looks like | Fits without scrolling |
|---|---|---|
| **Stacked** | Upright letters one above another, like a hotel sign | 4 characters (Regular 5×7) / 5 (Tiny 3×5) |
| **Sideways** | Letters rotated 90°, read with your head tilted (like the firmware's "LOTUS") | 5 characters (Regular) / 8 (Tiny) |
| **Ticker** | Upright letters moving sideways through the 9-px window | Always scrolls. Best spread over both modules |

### Fit

- **Auto-fit** picks the largest font where the text fits in the chosen layout and centers it.
- If it doesn't fit, the editor says so and suggests a scrolling motion or a smaller font. Nothing gets cut off silently.
- Fonts: **Tiny 3×5**, **Regular 5×7** and **Bold 5×7**. These are pixel fonts drawn for this project, covering ASCII plus ° % : arrows, with accented letters later.

### Motion

| Motion | Behavior |
|---|---|
| **Static** | Fitted and centered, and stays put. |
| **Slide up / down** | Loops seamlessly with a configurable gap between repeats. Sliding down is offloaded to the firmware when the text block is exactly one loop tall. |
| **Slide left / right** | A ticker. Across both modules in Span mode, with the keyboard gap. |
| **Dwell** (option) | Pauses for a moment each loop, so short words are easy to read. |

### Live tokens (small but powerful)

Text can include `{time}`, `{date}`, `{battery}`, `{cpu}` and `{ram}`. That gives a clock, battery meter and CPU meter for free, covering FR-33 without separate widgets. A token only re-renders when its value changes, about once a second.

## 6. CRT effects

| Effect | Description | When it's used |
|---|---|---|
| **CRT power off / on** | Off: the picture squashes into a bright horizontal line across the middle, collapses to a dot, and the dot fades (about 0.4 s). On: the same in reverse. | Optional **transition** for automatic on/off (instead of the firmware's fade) and for switching content. On sleep there's no time for it, so the app uses the plain fade. |
| **CRT look** (filter) | Alternate rows dimmed like **scanlines**, a faint **rolling bar** drifting down every few seconds, **phosphor afterglow** (moving things leave short fading trails), and a slight **flicker**. Intensity from 0 to 100%. | Over any content: drawings, patterns, animations, text. |
| **No signal** | Static snow, the odd horizontal tear and a rolling bar. | As an animation in its own right, or as an idle screen. |

The CRT effects need greyscale, so they use the procedural engine at about 20 fps.

## 7. App changes (UI)

Each module panel gets a **content type** above its grid:

```
 Left                                  Right
[Drawing | Pattern | Animation | Text]   [Drawing | Pattern | Animation | Text]
 Animation: [Digital rain      v]          Text: [ HELLO {time}       ]
 Speed  ----o------  Direction [Down v]    Layout [Sideways v]  Fit [Auto v]
 [x] CRT look  ---o--- 60%                 Motion [Slide up v]  Speed ---o---
 ( Runs on the module: 0% CPU )            ( Needs scrolling: 11 chars, 5 fit )
 [ live 9x34 preview ]                     [ live 9x34 preview ]

 Both modules: (o) Independent  ( ) Mirror  ( ) Span   [x] Keyboard gap
 Transition when the LEDs turn off/on: (o) Fade  ( ) CRT
```

The on-screen grids animate exactly what's sent, because the same renderer feeds both.

## 8. Architecture

| Piece | Where | What it does |
|---|---|---|
| `IAnimation` | Core | `Size` (9×34, or 18×34+gap for Span), `FrameInterval`, `LoopFrames?`, `Render(frame, target)`. Pure and deterministic, so it's easy to test. |
| `AnimationClock` | Core | **One** background timer drives both modules from the same frame number, so they stay in sync. It stops completely when the LEDs are off or nothing moves. |
| `MotionPlanner` | Core | Decides between firmware offload (`Animate` + `AnimationPeriod`) and the app engine. |
| `TextLayout` + `PixelFont` | Core | Layouts, auto-fit and glyph tables. |
| `Compositor` | Core | Content, then effects (CRT), then transition, then frame. Picks `DrawBW` when a frame is pure black and white (1 command instead of 10). |
| `MatrixDevice` | Core (exists) | Already sends only the latest frame. If a module falls behind, frames are dropped rather than queued, so there's never lag. |
| Content settings | Core | Saves *what* to show (for example "Digital rain, speed 3, CRT 60%"), not raw frames, so animations resume after restart and resume from sleep. |

**Performance targets:** 0% CPU when still or firmware-offloaded. Under 1% CPU for an app-driven animation on both modules at 20 fps. Under 5 MB extra memory.

**Tests:** a loop test for every built-in (frame[N] == frame[0]), text fit and layout tests, planner decision tests, and a test that no frames are sent while the LEDs are off.

## 9. Proposed new requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-60 | Animation engine with a single shared clock, in-sync modules, latest-frame-wins and no output while off | M |
| FR-61 | Built-in animations loop seamlessly (enforced by tests) | M |
| FR-62 | Automatic firmware offload for downward scrolling of seam-free content | M |
| FR-63 | Ready-made animations (§4), with speed and direction | M |
| FR-64 | Text with Stacked / Sideways / Ticker layouts, auto-fit and Static / Slide motions | M |
| FR-65 | Pixel fonts: Tiny 3×5, Regular 5×7, Bold 5×7 | M |
| FR-66 | Live text tokens `{time}`, `{date}`, `{battery}`, `{cpu}`, `{ram}` | S |
| FR-67 | CRT power off/on as a transition option | S |
| FR-68 | CRT look filter (scanlines, rolling bar, afterglow, flicker) with intensity | S |
| FR-69 | "No signal" animation | S |
| FR-70 | Wrap preview in the drawing editor | S |
| FR-71 | Mirror and Span modes (completes FR-16), with an optional keyboard gap | S |
| FR-72 | Frame-rate self-test that measures real fps per module and caps the engine to it | S |

## 10. Phasing

| Phase | Contents |
|---|---|
| **v0.2** | Engine and planner, seamless-loop tests, 8 ready-made animations, text (Stacked / Sideways, Static / Slide up / Slide down), Tiny and Regular fonts, fps self-test |
| **v0.3** | CRT power off/on, CRT look, No signal, Ticker, Span with keyboard gap, live tokens, Bold font, Wrap preview |
| Later | Frame-by-frame animation editor (FR-44), audio visualizer (FR-35), schedules (FR-36) |

## 11. Questions for you

1. **Text layouts:** which matters most to you: Stacked, Sideways or Ticker?
2. **CRT power-off:** should it *replace* the fade when the lid closes, or stay optional?
3. **Span mode:** model the keyboard gap by default (things disappear briefly between the modules), or join the modules edge to edge?
4. **Default speed:** scrolling at about 8 rows a second (calm) or 32 (the firmware default, brisk)?

## 12. Decisions and implementation notes (2026-11-05)

| Question | Decision |
|---|---|
| Text layout that matters most | **Stacked** is the default. Sideways and Ticker are also built. |
| CRT power-off on lid close | **No.** The LEDs keep the firmware fade when the lid closes or the screen turns off, whatever is playing. The CRT power off/on is an optional effect when the *content* changes (Settings → Effects, off by default). |
| Span mode keyboard gap | **User's choice, edge to edge by default.** A "Keyboard gap" checkbox adds 6 hidden columns between the modules. |
| Default speed | **Calm: 8 steps per second.** |
| Default brightness | **39%** (99/255): a Hatsune Miku easter egg. |

**Changes made while building:**

- *Breathing*, *Game of Life* and *Marching stripes* run on the app engine (the table above was updated). Marching stripes repeat every 4 rows, which doesn't divide 34, so the firmware scroll would show a seam. Checker march and slide-down text with a 34-row loop are offloaded to the module.
- The helix's rungs were spaced every 4 rows, which broke the loop. They're now spaced 4-4-4-5, so the loop is exact.
- The loop test also caught floating-point drift in the sine, plasma, heartbeat and breathing formulas. They now wrap the step into the loop before any trigonometry, so the last frame matches the first bit for bit.
- Bold 5×7, the Wrap preview and the fps self-test are deferred to a later release.
