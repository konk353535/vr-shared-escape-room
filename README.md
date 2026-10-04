# VR Shared Escape Room

A colocated escape room for **Meta Quest 3**: up to 4 players in the same physical room (6 m x 5.5 m) see each other and the same puzzle objects in the same places, using Meta's **colocation discovery** and **group-shared spatial anchors**, with **Netcode for GameObjects** over the local Wi-Fi.

The starter scene is a virtual room that matches the physical one, with a three-step puzzle whose objects work together:

1. Three gems (red, green, blue) are scattered around the room.
2. Put each gem on its matching pedestal by the left wall. When all three are in, the chest by the back wall opens and a key appears.
3. Put the key in the lock beside the exit door. The door swings open.

## Versions

| | |
|---|---|
| Unity | **6000.0.84f1** (Unity 6 LTS) with Android Build Support (OpenJDK, Android SDK & NDK) |
| Meta XR SDK | 207.0.0: Core, Interaction, MR Utility Kit, Platform |
| XR | OpenXR 1.16.1 with the Meta XR and Meta Quest features |
| Networking | Netcode for GameObjects 2.13.3 (Unity Transport) |
| Models | glTFast 6.19.0 (imports image-blaster `.glb` files) |
| Render pipeline | Built-in (same as Meta's current Shared Spatial Anchors sample) |

## First-time setup

1. Open the project in Unity Hub with **6000.0.84f1**. The first import downloads the packages and takes a while.
2. **Escape Room > 0. Switch Build Target to Android (Quest)**
3. **Escape Room > 1. Configure Project for Quest 3**: package name, IL2CPP/ARM64, Vulkan, Linear colour, OpenXR loader + Meta features.
4. **Meta > Tools > Project Setup Tool**, then **Fix All** for both Android and Standalone.
5. **Escape Room > 2. Build Starter Room Scene**: generates `Assets/EscapeRoom/Scenes/EscapeRoom.unity`, the prefabs, materials and `Assets/DefaultNetworkPrefabs.asset`.
6. Plug in a Quest 3 (developer mode on) and **File > Build And Run**. Install the same build on every headset.
7. Commit what Unity generated (`.meta` files, `Assets/XR`, `ProjectSettings`, the scene and prefabs) so everyone opens the same project.

The scene and prefabs are generated from `Assets/EscapeRoom/Editor/StarterRoomBuilder.cs` rather than hand-edited, so changing the room is a code change plus re-running step 5.

## Headset setup (every headset)

- **Settings > Privacy & Safety > Device Permissions > Enhanced Spatial Services: on.** Anchor sharing fails without it.
- Same Wi-Fi network, without client isolation (guest networks often block headset-to-headset traffic).
- Set up a boundary that covers the whole room, or use the room's scanned space.
- Allow the spatial data permission when the app asks.

## Playing

| Who | What |
|---|---|
| Host | Stand in the **centre of the room facing the front wall** (the exit door wall) and press **A**. That pose becomes the shared room origin. |
| Everyone else | Just launch the app. It finds the host over colocation discovery, connects, loads the shared anchor and aligns. |
| Grab | Squeeze **grip** near an object (or pinch with hand tracking). Release to drop or throw. |
| Passthrough | **Y** toggles passthrough, so you can check the virtual walls line up with the real ones. |
| Leave | **B** ends or leaves the session and goes back to searching. |
| Status | A small panel in front of you shows what the session is doing. Click a thumbstick to show it again. |

In the editor (Quest Link), **H** hosts and **J** joins `127.0.0.1` without a shared anchor, for quick logic testing.

### Room coordinates

Origin is the centre of the floor. **+Z points at the front wall** (exit door), +X to the right when facing it. The room spans X -3..3 and Z -2.75..2.75, walls are 2.7 m high. Keep everything you add inside that box.

## How the colocation works

`Assets/EscapeRoom/Scripts/Session/SessionBootstrapper.cs` drives it:

1. Every headset starts `OVRColocationSession.StartDiscoveryAsync()`.
2. The host starts Netcode, creates an `OVRSpatialAnchor` at its calibration pose, aligns to it, then calls `OVRColocationSession.StartAdvertisementAsync()` with its LAN address in the metadata and `OVRSpatialAnchor.ShareAsync(anchors, groupUuid)` to share the anchor to that group.
3. Clients receive the advertisement, connect to the host's address, then `OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(groupUuid)`, localize and bind the anchor (retrying until the host has shared it).
4. `RoomAligner` moves each headset's tracking space so the anchor sits at world origin. From then on all headsets share world coordinates, so Netcode positions match the physical room.

Players are `NetworkPlayer` prefabs (head + hands, owner-authority `NetworkTransform`s). Grabbable items are `NetworkGrabbable`: grabbing asks the server for ownership, the owner moves and throws it, and the server locks it into a socket when the puzzle accepts it. `GemVaultPuzzle` holds the puzzle state in `NetworkVariable`s, so late joiners see the current stage.

## Importing an image-blaster world

[image-blaster](https://github.com/neilsonnn/image-blaster) turns a photo into a World Labs environment, Hunyuan 3D object meshes and SFX under `image-blaster/worlds/<slug>/`.

1. Generate a world with image-blaster and arrange the objects in its viewer (that saves `worlds/<slug>/scene.json`).
2. Open the EscapeRoom scene, then **Escape Room > 3. Import image-blaster World...** and pick the `worlds/<slug>` folder.
3. The importer copies the meshes and sounds into `Assets/EscapeRoom/Imported/<slug>/` and lays them out under `[Room]/ImageBlaster/<slug>`:
   - objects with physics `rigidbody` become networked, grabbable prefabs (placed with a `SpawnMarker`; the host spawns the real networked object there),
   - `static` objects get a mesh collider, `ghost` objects get none,
   - per-object SFX play on impact,
   - the World Labs collider mesh is added as an EditorOnly reference for layout.
4. Move and scale the props to fit inside the 6 m x 5.5 m room and save the scene.

Gaussian splats (`.spz`) aren't imported: Unity has no built-in splat renderer, and in a shared physical room real geometry with colliders is more useful. Hunyuan meshes default to about 1 m in size, so expect to rescale them.

## Project layout

```
Assets/EscapeRoom/
  Scripts/
    Session/      SessionBootstrapper (flow), SessionAdvert (discovery metadata, LAN IP)
    Colocation/   SharedAnchorService (create/share/load anchors), RoomAligner
    Players/      NetworkPlayer (head + hands avatar)
    Interaction/  NetworkGrabbable, HandGrabber, LocalRigBinder
    Puzzle/       GemVaultPuzzle, ItemSocket, ServerSpawner, SpawnMarker, ImpactSound
    UI/           StatusHud, PassthroughToggle
  Editor/
    QuestProjectSetup.cs     Escape Room > 0 / 1
    XRSetup/                 OpenXR + Meta feature setup (own assembly)
    StarterRoomBuilder.cs    Escape Room > 2
    ImageBlasterImporter.cs  Escape Room > 3
Assets/Oculus/OculusProjectConfig.asset   Meta features: anchors, shared anchors, colocation, passthrough
```

## Known limits and next steps

- Not yet run on hardware: the first open and build on a Mac will show whether any package or API needs a nudge.
- Calibration is "host stands in the middle facing the front wall". A more precise option is to use MR Utility Kit's room scan (walls and floor are already known) or a two-corner calibration.
- If two people press A at once you get two sessions; have one host and everyone else wait.
- `OculusProjectConfig` has Meta's experimental features flag on, as Meta's colocation sample does. That's fine for sideloading but must be revisited before a Store release.
- Grabbing is a minimal grip/pinch grabber. The Meta Interaction SDK (already installed) is the upgrade path for proper hand grabs.
