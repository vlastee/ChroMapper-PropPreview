# ChroMapper-PropPreview

A ChroMapper plugin that previews Vivify content in the editor: it loads the map's asset bundle
and shows its prefabs, materials and render settings at the current song time, as the game would.

It only previews. Editing Vivify events is done with
[ChroMapper-PropEdit](https://github.com/FallenCharlotte/ChroMapper-PropEdit), which has an
editor for every Vivify event type and reads the prefab and material names from the map's
`bundleinfo.json`. PropEdit's TODO board (github.com/users/FallenCharlotte/projects/1) has no
preview item (checked 2026-10-15), so this is a separate plugin rather than a PropEdit feature.

## Status

Previews every Vivify event in ChroMapper 0.14.990 (dev), natively on Linux and as the Windows
build under Proton. Checked with unattended runs on the Sunrise stage map and on published maps
from other mappers (BeatSaver 4968d, 4c036, 4a2b3): skies, environment prefabs, note prefabs,
post-processing and a 41,709-event map (rebuilt in about 0.2 s).

What it previews, at the editor's current time, rebuilt after every edit:

| Event | Preview |
|---|---|
| `InstantiatePrefab` / `DestroyObject` | each instance exists from its event until the `DestroyObject` for its `id`; spawn transform from `position`/`localPosition`, `rotation`/`localRotation`, `scale`; with a `track`, ChroMapper's own track animation moves it (`AnimateTrack` replaces the spawn pose, as in Vivify; `AssignTrackParent` applies) |
| `SetMaterialProperty` / `SetGlobalProperty` | Float, Color, Vector, Texture, Keyword |
| `SetRenderingSettings` | the `renderSettings` values; `skybox` |
| `SetCameraProperty` | `clearFlags` (the skybox shows only with `Skybox`, as in the game), `backgroundColor`, `depthTextureMode`, `culling`, on the view camera (`_Main`) or a `CreateCamera` camera |
| `AssignObjectPrefab` | note, bomb, chain and chain-link prefabs on ChroMapper's notes, `Single`/`Additive`, `anyDirectionAsset` for dot notes; the prefab gets the note's colour and dissolve |
| `SetAnimatorProperty` | Bool, Float and Trigger on prefab instances; animators run while the song plays |
| `CreateScreenTexture` | render textures sized from the view, as shader globals |
| `CreateCamera` | secondary cameras rendering the view into `texture` / `depthTexture`, with their own properties and culling |
| `Blit` | the full chain: priority order, `_Main` / screen texture / camera sources and destinations, `BeforeMainEffect`/`AfterMainEffect` on the final image, the other orders inside the frame |

It follows Vivify's rules, read from its source: the latest event that has started wins; a
static value applies at once; a point definition plays over the event's `duration` (in beats)
with the event's `easing`; a finished or zero-length event shows its last point; named point
definitions are not resolved (Vivify doesn't resolve them for these events either).

Differences from the game:

- Sabers (`AssignObjectPrefab` `saber`): ChroMapper shows no sabers.
- A note takes the `AssignObjectPrefab` assignment in effect at its own beat (Vivify: at its
  spawn, and live notes change with the event).
- `BeforeMainEffect` and `AfterMainEffect` both run after ChroMapper's bloom, which stands in for
  the game's main effect; a `CreateCamera` camera's `mainEffect` and `bloomPrePass` are ignored.
- Animator states don't rewind when you scrub backwards; triggers fire only while playing.
  `Integer` animator properties are skipped (Vivify sets them with `SetFloat`, which Unity rejects).
- TextMeshPro text inside a bundle can throw in ChroMapper (seen on 4968d); the rest still works.
- `qualitySettings`/`xrSettings` are not applied.

## Published maps (Windows ChroMapper under Proton)

Published maps carry only the game's bundle (`bundleWindows2021.vivify`, Direct3D 11 shaders),
which the native Linux ChroMapper (Vulkan) loads but cannot draw. The Windows build of
ChroMapper under Proton draws it (Direct3D 12 through vkd3d), and the plugin picks the bundle
that fits the graphics API. `tools/chromapper-windows.sh`:

    tools/chromapper-windows.sh install       # Windows ChroMapper, same build as the Linux one, + plugins
    tools/chromapper-windows.sh fetch 4968d   # a BeatSaver map into the WIP levels
    tools/chromapper-windows.sh run           # ChroMapper in a window

Your own maps can carry a `bundleLinux.vivify` (a Linux build of the same bundle) for the native
Linux ChroMapper.

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
  and optionally `PROPPREVIEW_DIFF=Expert`, `PROPPREVIEW_CHAR=Standard`: opens the map through ChroMapper's song menu
  without saving it, seeks to each beat, and saves `<beat>.png`. The PNGs keep the frame's
  alpha, which Beat Saber shaders use for bloom, so flatten them to RGB before viewing. Use a
  copy of the map. While it runs, Discord RPC is off and the last-loaded map is the test map;
  both are restored before quitting.

Results go to ChroMapper's log (`~/.config/unity3d/BinaryElement/ChroMapper/ChroMapper.log`;
under Proton `<prefix>/pfx/drive_c/users/steamuser/AppData/LocalLow/BinaryElement/ChroMapper/`),
in lines that start with `PropPreview`. Under Proton, pass Windows paths (`Z:\...`), and stop a
gamescope left behind after ChroMapper quits by its PID.
