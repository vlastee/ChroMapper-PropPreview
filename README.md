# ChroMapper-PropPreview

A ChroMapper plugin that previews Vivify content in the editor: it loads the map's asset bundle
and shows its prefabs, materials and render settings at the current song time, as the game would.

It only previews. Editing Vivify events is done with
[ChroMapper-PropEdit](https://github.com/FallenCharlotte/ChroMapper-PropEdit), which has an
editor for every Vivify event type and reads the prefab and material names from the map's
`bundleinfo.json`. PropEdit's TODO board (github.com/users/FallenCharlotte/projects/1) has no
preview item (checked 2026-10-15), so this is a separate plugin rather than a PropEdit feature.

## Status

Starting code. It builds against ChroMapper 0.14.990 (dev). On entering the editor it loads
the map's bundle and logs the bundle's asset names. Nothing is drawn yet.

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

Vivify events, in the order they will be done:

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
