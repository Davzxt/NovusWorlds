# Novus Worlds Native

This folder is the new C++ Windows engine path. It replaces the Godot runtime as the primary Client/Studio implementation while keeping the existing Express site and APIs.

## Build

Install the toolchain once:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install-native-toolchain.ps1
```

Then build:

```powershell
npm run native:build
```

The release executables are generated in:

```text
build/native-msvc/Release/NovusWorldsClient.exe
build/native-msvc/Release/NovusWorldsStudio.exe
```

## Package Downloads

```powershell
npm run native:package
```

This writes the site downloads:

```text
public/download/NovusWorldsClient-Windows.zip
public/download/NovusWorldsStudio-Windows.zip
```

## Current Native Slice

- Win32 windowing and Direct3D9 renderer.
- Shared Novus DataModel with `Workspace`, services, `Part`, `SpawnLocation`, `Script`, and JSON serialization.
- Native Client loads join JSON, downloads the place from `/api/legacy/place/:id`, renders the map, displays a 2011-style HUD, and runs a classic R6 block character controller.
- Native Studio opens project JSON, edits the shared DataModel, inserts parts/spawns/scripts, saves/publishes to `/api/legacy/studio-project/save`, and includes a classic Studio chrome.

## Next Engine Work

- Native WebSocket replication.
- OBJ/GLTF asset loader for custom R6 and hats.
- Real Studio picking/gizmos.
- Lua 5.1 script runtime.
- D3D9 material atlas for 2011 surfaces.
