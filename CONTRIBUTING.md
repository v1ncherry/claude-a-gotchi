# Contributing to claude-a-gotchi

Thanks for helping Clawd! There are three easy ways in, and none of them needs programming:

1. **[Make a costume](#costume-files)**: a hat, an outfit, a body color, a blanket cover or a pillow.
2. **[Translate](#translations)** the app into your language.
3. **[Report a bug or wish for something](https://github.com/v1ncherry/claude-a-gotchi/issues/new/choose).**

Code changes are welcome too ([below](#code)). Everyone here follows the [Code of Conduct](CODE_OF_CONDUCT.md).

---

## Costume files

Every costume is one small text file in [`costumes/`](costumes):

| Folder | `type` | What it is |
|---|---|---|
| `costumes/hats/` | `hat` | Something on Clawd's head |
| `costumes/outfits/` | `outfit` | Something he wears: scarf, glasses, cape … |
| `costumes/colors/` | `color` | His body color |
| `costumes/covers/` | `cover` | The pattern of his blanket |
| `costumes/pillows/` | `pillow` | The color of his pillow |

### A complete example

```text
# Lines starting with # are comments.
type: hat
id: party
name: Party hat
name.de: Partyhut
author: your-github-name
at: -5, -9
mirror: none
colors:
  P = #e25c9a
  Y = #ffd84a
  W = #ffffff
pixels:
  ....WW....
  ....WW....
  ....PP....
  ...PPYP...
  ...PYPP...
  ..PPPPYP..
  ..PYPPPP..
  .PPPPYPPP.
  .PPYPPPPP.
  PPPPPPPYPP
```

### The fields

| Field | Needed for | Meaning |
|---|---|---|
| `type` | all | `hat`, `outfit`, `color`, `cover` or `pillow` |
| `id` | all | Short name in lowercase, digits and dashes, e.g. `pirate-hat`. The file must be called `<id>.txt`. |
| `name` | all | English name shown in the app (max. 40 characters) |
| `name.de`, `name.fr`, … | optional | The name in other languages |
| `author` | optional | You! |
| `order` | optional | Sort position in the app (built-in ones use 10, 20, 30 …). Without it, new costumes come last. |
| `colors` | hat, outfit, cover | One line per color: a letter or digit, `=`, a color like `#ff8800` |
| `pixels` | hat, outfit, cover | The picture. One character = one Clawd pixel. `.` is see-through. All rows must be equally long. Max. 40 × 30. |
| `at` | hat, outfit | Where the top-left corner of the picture sits, see [the map](#where-things-go-the-map) |
| `mirror` | hat, outfit | `none` (default), `left` = flip when Clawd looks left, `right` = flip when he looks right. For things with a direction, like a cap's brim. |
| `layer` | outfit | `front` (default) over everything, `face` under the eyes (glasses), `back` behind the body (cape) |
| `in-bed` | outfit | `yes` if he keeps it on while sleeping (default `no`). Hats are always swapped for the night cap in bed. |
| `flutter` | outfit | From this row on, the picture waves while he moves (the cape uses `8`) |
| `body` | color | His body color, e.g. `#da7756` |
| `base`, `band` | cover | Blanket color, and the color of the band below the white fold |
| `main`, `shade` | pillow | Pillow color and the darker edge |

For a **cover**, the picture is a *tile* that repeats across the whole blanket; `.` shows the `base` color. Keep tiles small (4 to 16 pixels wide) and check that the edges fit together.

### Where things go: the map

`at: x, y` is measured in Clawd pixels from the **top center of his body**. `x` goes right, `y` goes down, so negative `y` means above his head. This is Clawd standing and looking right, with the coordinates of every part:

```text
          -12 -8  -4  0   4   8 11   <- x
          |   |   |   |   |   |  |
  y  0    ....BBBBBBBBBBBBBBBB....     B = body: x -8..7, y 0..11
  y  1    ....BBBBBBBBBBBBBBBB....
  y  2    ....BBEEBBBBBBBBEEBB....     E = eyes: x -6..-5 and 4..5
  y  3    ....BBEEBBBBBBBBEEBB....
  y  4    AAAABBBBBBBBBBBBBBBBAAAA     A = arms: x -12..-9 and 8..11
  y  5    AAAABBBBBBBBBBBBBBBBAAAA
  y  6    AAAABBBBBBBBBBBBBBBBAAAA
  y  7    AAAABBBBBBBBBBBBBBBBAAAA
  y  8    ....BBBBBBBBBBBBBBBB....
  y  9    ....BBBBBBBBBBBBBBBB....
  y 10    ....BBBBBBBBBBBBBBBB....
  y 11    ....BBBBBBBBBBBBBBBB....
  y 12    ....LL..LL....LL..LL....     L = legs: y 12..15
  y 13    ....LL..LL....LL..LL....
  y 14    ....LL..LL....LL..LL....
  y 15    ....LL..LL....LL..LL....
```

Rules of thumb:

- **A hat** usually overlaps the head by 1 row, so `at` y = `1 - height`. A hat 16 pixels wide and centered starts at x = `-8`.
- **Glasses** sit on y = 1..4 with `layer: face`, so the eyes stay visible.
- **A scarf** sits around y = 10, just above the legs.

Not sure? Just try it: the app and the pull request preview show you exactly where it lands.

### Try it on your computer

1. In the app, open the settings (click Clawd's bed → *Customize*) and click **Make your own costumes …** at the bottom of the *Look* tab.
2. Save your `.txt` file into the folder that opens (`%LOCALAPPDATA%\claude-a-gotchi\costumes`).
3. Switch back to the settings window. It reloads the costume files every time it becomes active, and your costume appears in the list. A file with the same `type` and `id` as a built-in costume replaces it.

If it does not show up, there is a mistake in the file: `%LOCALAPPDATA%\claude-a-gotchi\log.txt` says which.

You can also render a preview picture yourself:

```powershell
claude-a-gotchi.exe --preview my-hat.txt preview.png
```

### Send it in

1. [Fork](https://github.com/v1ncherry/claude-a-gotchi/fork) the repository.
2. Add your file to the right folder, e.g. `costumes/hats/pirate-hat.txt`. You can do this right on GitHub: *Add file → Create new file*.
3. Open a pull request. A bot builds the app, checks your file and answers with a picture of Clawd wearing your costume, from both sides and in a few poses. If something is wrong, the picture lists the problems.

Please keep costumes friendly, original and small. No logos or characters that belong to someone else.

---

## Translations

All texts live in [`lang/`](lang), one JSON file per language:

1. Copy `lang/en.json` to `lang/<code>.json`, using the two-letter code of your language (`fr`, `es`, `ja`, …).
2. Set `"_code"` to that code and `"_name"` to the language's own name (`"Français"`).
3. Translate the values, not the keys. Keep `{0}` placeholders where they are. The `fmt.*` entries are number and time formats ([.NET format strings](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings)).
4. To test it without building: put the file into `%LOCALAPPDATA%\claude-a-gotchi\lang\` and pick the language in *Settings → Language* (restart Clawd after changes).
5. Open a pull request. Missing texts fall back to English, so a partial translation is fine.

You can also add translated costume names (`name.fr: …`) to the files in `costumes/`.

---

## Code

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/claude-a-gotchi                                 # start the app
dotnet build src/claude-a-gotchi -c Release -o build/dev                 # build only
build\dev\claude-a-gotchi.exe --check .                                  # check all costume and language files
powershell -ExecutionPolicy Bypass -File tools/make-media.ps1            # render all README pictures (needs Python + Pillow)
powershell -ExecutionPolicy Bypass -File tools/build.ps1                 # installer + zip (needs Inno Setup 6)
```

Where things are:

| File | What it does |
|---|---|
| `PetForm.cs` | The desktop window: movement, sleeping, throwing, hooks state, limits, drawing loop |
| `Art.cs`, `Sprites.cs` | Drawing Clawd, his bed, the limit plate and the hourglass |
| `Costumes.cs` | Reading costume files |
| `Loc.cs` | Translations |
| `QuickPanel.cs`, `StudioForm.cs`, `WelcomeForm.cs`, `Ui.cs` | The self-painted windows |
| `HookBridge.cs` | Claude Code hooks: install, remove, handle a hook call |
| `Usage.cs` | Limits from local logs and (optionally) from the usage endpoint |
| `Native.cs` | Windows API: window scanning, the see-through layered window |
| `Tools.cs` | Command line tools: `--preview`, `--check`, `--gallery`, `--frames`, `--ui-test` |

Some guidelines:

- Keep it light: Clawd idles at about 0.01 % CPU. A hook call must stay fast (about 60 ms), because Claude Code runs it on every tool call.
- All visible text goes through `Loc.T("key")`, with the key in `lang/en.json` (and ideally `lang/de.json`).
- Pixel art stays on the grid: one Clawd pixel is always `k × k` screen pixels.
- Write code comments in English.
- Open an issue first for bigger changes, so we can agree on the idea before you spend time on it.

Releases are built by GitHub Actions when a version tag (`v1.2.3`) is pushed.
