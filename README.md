# ChroMapper-PropPreview

A ChroMapper plugin that previews Vivify content in the editor: it loads the map's asset bundle
and shows its prefabs, materials and render settings at the current song time, as the game would.

It only previews. Editing Vivify events is done with
[ChroMapper-PropEdit](https://github.com/FallenCharlotte/ChroMapper-PropEdit), which has an
editor for every Vivify event type and reads the prefab and material names from the map's
`bundleinfo.json`. PropEdit's TODO board (github.com/users/FallenCharlotte/projects/1) has no
preview item (checked 2026-10-15), so this is a separate plugin rather than a PropEdit feature.

## Status

Working in ChroMapper 0.14.990 (dev) on Linux, checked on the Sunrise stage map (127 prefabs, 875
material property events, animated globals, a skybox): the night sky, moon and sea at the start,
dolphins on their tracks, the sunrise later on, with ChroMapper's grid and notes on top.

What it previews, at the editor's current time, rebuilt after every edit:

- `InstantiatePrefab` / `DestroyObject`: each instance exists from its event until the
  `DestroyObject` for its `id`. Its spawn transform comes from `position`/`localPosition`,
  `rotation`/`localRotation` and `scale`. With a `track`, ChroMapper's own track animation
  (the one environment enhancements use) moves it: `AnimateTrack` values replace the spawn pose,
  as in Vivify, and `AssignTrackParent` parents apply.
- `SetMaterialProperty` / `SetGlobalProperty`: Float, Color, Vector, Texture and Keyword.
- `SetRenderingSettings`: the `renderSettings` values, and `skybox`, which is drawn on the
  editor camera in place of ChroMapper's fog backdrop.

It follows Vivify's rules, read from its source. The latest event that has started wins. A
static value applies at once. A point definition plays over the event's `duration` (in beats)
with the event's `easing`. A finished or zero-length event shows its last point. Named point
definitions are not resolved, matching Vivify.

Not done yet: `SetCameraProperty` (a Vivify skybox is shown whenever one is set),
`AssignObjectPrefab` (custom notes and sabers), `SetAnimatorProperty`, `Blit`, `CreateCamera`,
`CreateScreenTexture`, and `qualitySettings`/`xrSettings`.

## Target: ChroMapper 0.14 (dev), not 0.13

Vivify bundles use built-in render pipeline shaders. ChroMapper 0.13 (the current stable
release) renders with URP, which cannot draw them (they come out magenta or invisible).
ChroMapper's dev branch moved to the built-in pipeline and Unity 6000.3 (Caeden117/ChroMapper
#607, merged 2025-10-10, with "Vivify compatibility" among its goals), so this plugin targets
0.14 dev. Bundles exported from Unity 2021.3 (Beat Saber 1.34+) have to load in Unity 6; that
is the first thing to confirm.

## Shaders on Linux

A bundle built for Windows only holds Direct3D 11 shader code. The native Linux ChroMapper 0.14
renders with Vulkan (0.13 used OpenGL), so it needs a second build of the same bundle for
StandaloneLinux64, with Vulkan in its graphics APIs. The plugin loads, in order of preference:

1. `bundleLinux.vivify`: preview-only build for Linux. The game ignores it, and it stays out of
   the BeatSaver zip.
2. `bundleWindows2021.vivify`: the game's bundle. It only draws in ChroMapper on Windows, or
   under Proton.

## Scope

Vivify events and their preview:

| Event | Preview |
|---|---|
| `InstantiatePrefab` / `DestroyObject` | the prefab exists between its two events; `track` parents it to the Heck track (ChroMapper's own TrackAnimator) |
| `SetMaterialProperty` / `SetGlobalProperty` | property values at the current time, from Heck point definitions and easings |
| `SetRenderingSettings` | skybox, fog, ambient light |
| `SetCameraProperty` | clear flags, background colour, depth texture mode |
| `AssignObjectPrefab` | prefabs for notes, walls, sabers |
| `SetAnimatorProperty` | animator parameters |
| `Blit`, `CreateCamera`, `CreateScreenTexture` | later, if at all: post-processing on ChroMapper's own camera |

Everything is computed from the song time, so scrubbing backwards needs no replay.

## Building

Needs the .NET SDK (8 or later) and a ChroMapper install to build against:

    dotnet build -c Release -p:ChroMapperDir=/path/to/chromapper

The DLL goes to `bin/Release/Plugins/`. Copy it into the install's `Plugins/` folder. It also
loads in 0.13 (the bundle loads, but nothing will draw under URP).

## Testing without a display

`SelfTest.cs` and `ScriptedOpen.cs` drive ChroMapper unattended, for example under
`gamescope --backend headless`:

- `PROPPREVIEW_SELFTEST=<bundle>`: loads the bundle at startup, logs each asset and whether its
  shaders run on this graphics API (`Shader.isSupported`), then quits.
- `PROPPREVIEW_OPEN=<map folder>` with `PROPPREVIEW_SHOTS=12,150,300`, `PROPPREVIEW_OUT=<dir>`
  and optionally `PROPPREVIEW_DIFF=Expert`: opens the map through ChroMapper's song menu
  without saving it, seeks to each beat, and saves `<beat>.png`. The PNGs keep the frame's
  alpha, which Beat Saber shaders use for bloom, so flatten them to RGB before viewing. Use a
  copy of the map. While it runs, Discord RPC is off and the last-loaded map is the test map;
  both are restored before quitting.

Results go to ChroMapper's log (`~/.config/unity3d/BinaryElement/ChroMapper/ChroMapper.log`),
in lines that start with `PropPreview`.
