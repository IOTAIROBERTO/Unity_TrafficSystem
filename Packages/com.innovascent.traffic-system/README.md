# InnovAscent Traffic System

Waypoint-based vehicle traffic simulation for Unity. Object pooling, lane IDs, per-lane
spawning, traffic lights and a 4-way intersection controller. Built for standalone VR
(Meta Quest), works on any platform.

## Install

Package Manager → Add package from git URL:

```
https://github.com/IOTAIROBERTO/Unity_TrafficSystem.git?path=/Packages/com.innovascent.traffic-system
```

Or add to `Packages/manifest.json`:

```json
"com.innovascent.traffic-system": "https://github.com/IOTAIROBERTO/Unity_TrafficSystem.git?path=/Packages/com.innovascent.traffic-system"
```

Requires Unity 2022.3 or newer. No package dependencies.

## Setup window

`Tools > InnovAscent > Traffic System` opens the editor window that ships with the package.

**Setup tab** — a scene checklist with one-click fixes:
- creates the `Traffic System` GameObject with `TrafficManager` + `TrafficConfig` wired
- creates the vehicle physics layer in `TagManager.asset` if it does not exist
- derives `vehicleLayer` from that layer when the mask is empty
- creates and registers a new `VehiclePreset` asset
- builds a `LaneConfig` from a selected parent GameObject: its children become the lane
  waypoints in order, first child the spawn point, last child the destroy point
- flags broken lanes (missing laneId, duplicated laneId, no waypoints, no spawn point, no destroy points)

**Config tab** — the full `TrafficConfig` inspector, editable in play mode for live tuning.

## Sample scene generator

`Tools > InnovAscent > Traffic System > Build Sample Scene` generates a runnable block city
into `Assets/TrafficSystem-SampleScene/`:

- ground, a square ring road and two avenues crossing at the centre
- four city blocks of building cubes between them
- six lanes: four signalled arms through the crossroads, plus an inner and an outer ring lane
  so traffic circulates all the way around the blocks
- right-hand traffic: every lane sits to the driver's right of its road centreline, and each
  traffic light stands on the kerb to the right of the approach it governs
- turns at the crossroads: each approach carries a `WaypointDecision` offering straight (50%),
  right (25%) and left (25%) onto the avenue heading that way
- four traffic lights, each a dark housing carrying three coloured cubes, driven by a
  `FourWayIntersectionController`
- `LaneDirection` metadata marking the crossings, with the avenues yielding to ring traffic
- the `TrafficManager` + `TrafficConfig` pair and the vehicle layer

Select model assets in the Project window before running it and each is wrapped into a
usable vehicle prefab: the builder strips the cameras and lights that DCC exports carry,
measures the renderer bounds, moves the origin to the centre of the footprint on the ground,
rotates the long axis onto +Z and normalises the length to 4.5 m. Models whose footprint is
too square to be a car are rejected with a warning, since normalising them would produce a
box that blocks the carriageway.

With nothing selected it uses the project's existing `VehiclePreset` assets, skipping any
whose prefab has no renderable mesh, and finally falls back to box cars — so the scene always
runs and never spawns invisible traffic.

## Manual setup

1. Create a physics layer for vehicles (`Edit > Project Settings > Tags and Layers`).
   Default expected name is `Vehicles`; any name works, set it in `TrafficConfig.vehicleLayerName`.
2. Add a `TrafficConfig` component to a GameObject and tune the global values.
3. Add a `TrafficManager` component and assign:
   - `config` — the `TrafficConfig` above
   - `lanes` — one `LaneConfig` entry per lane (waypoints, spawn point, destroy points, speed, spawn cadence)
   - `vehicleTypes` — `VehiclePreset` assets (`Create > Traffic > Vehicle Preset`), each pointing at a vehicle prefab
4. Optional: put `LaneDirection` on waypoints to declare flow direction, intersections and priorities.
5. Optional: put `WaypointDecision` on a waypoint to branch routes (straight / right / left) with probabilities and lane-ID validation.
6. Optional: `TrafficLightController` per stop waypoint, and `FourWayIntersectionController`
   to drive four of them through a realistic phased cycle.
7. Optional: `TrafficSystem_AudioManager` for ambient city audio.

## Samples

Installs from `Window > Package Manager > InnovAscent Traffic System > Samples`. Nothing is
copied into a project until you import it.

**Demo City Traffic** (~82 MB) contains:

```
Demo/
  SampleScene/           TrafficSystem-SampleScene.unity + its car prefabs and presets
  Vehicles/              three car models, one folder per vehicle
  TrafficCarsAudios/     ambient and engine audio bank
  TrafficCarsPrefabs/    older car prefabs
  TrafficCarsScenes/     the original TrafficScene
  TrafficSystem.prefab   a pre-wired manager + config + lanes
```

`TrafficSystem-SampleScene` is the one to open: a block city with real car models driving the
ring road and the signalled crossroads.

Check `Vehicles/CREDITS.txt` before redistributing: these are third-party models whose author
and licence were never documented, and the vehicles they depict carry manufacturer trademarks.

## Host project integration notes

- Everything lives in the `InnovAscent.TrafficSystem` namespace — add
  `using InnovAscent.TrafficSystem;` in your own scripts.
- Nothing depends on a specific layer name, tag, scene, or singleton outside the package.
  The only global is `TrafficManager.Instance`.
- Logs are off by default. Turn on `TrafficConfig.enableDebugLogs` while wiring a scene,
  turn it off for device builds. Errors (broken setup) are always emitted.
- If `TrafficConfig.vehicleLayer` is left empty it is derived from `vehicleLayerName` at
  startup, so detection queries cannot silently match nothing.

## Components

- `TrafficManager` — spawning, pooling, vehicle detection queries, waypoint and traffic-light registry
- `TrafficConfig` — global simulation values (density, speed, light timings, braking distances)
- `LaneConfig` — serializable per-lane setup, edited inline on `TrafficManager`
- `VehicleCharacteristics` — `ScriptableObject` preset: prefab, size, motor values, audio clips, spawn weight
- `Vehicle` — per-vehicle driving, steering, braking, wheel and suspension animation
- `VehiclePool` — plain stack-based pool, no allocations during gameplay
- `LaneDirection` — waypoint metadata: flow direction, zone type, intersection priority, yield
- `WaypointDecision` — route branching with probabilities and lane-ID permissions
- `TrafficLightController` — single light, supports external control and a blinking-green state
- `FourWayIntersectionController` — phased cycle across four lights (solid green → blinking green → yellow → all-red)
- `TrafficSystem_AudioManager` — ambient loops plus randomized spatial one-shots
- `TrafficDebugHelper` — editor gizmos and on-screen counters
- `TrafficLog` — logging gate

## Known limitations

- Public API and serialized field names are in Spanish (inherited from the original
  implementation). Renaming them requires `[FormerlySerializedAs]` migration on every field.
- `TrafficConfig` is a `MonoBehaviour`, not a `ScriptableObject`, so config lives in the
  scene rather than as a reusable asset.
