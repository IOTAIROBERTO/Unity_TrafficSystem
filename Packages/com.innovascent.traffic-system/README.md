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

`Tools > InnovAscent > Traffic System > Build Sample Scene` generates a runnable four-way
intersection into `Assets/TRAFFIC-SAMPLE/TRAFFIC-SAMPLE.unity`: ground and road, four lanes of
seven waypoints each, `LaneDirection` metadata with the crossing marked as an intersection,
four traffic lights driven by a `FourWayIntersectionController`, the `TrafficManager` +
`TrafficConfig` pair, the vehicle layer, and one `VehiclePreset` per vehicle.

Select model assets in the Project window before running it and each one is wrapped into a
pivot-corrected prefab: the builder measures the renderer bounds, moves the origin to the
centre of the footprint on the ground, rotates the long axis onto +Z and normalises the length
to 4.5 m. Raw models therefore work without fixing their pivot in a DCC tool first. With
nothing selected it falls back to placeholder box cars, so the scene always runs.

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

**Demo City Traffic** (17 MB) — wired-up scene, seven vehicle prefabs, six vehicle presets,
ambient audio bank.

### Vehicle models

Car meshes are **not** distributed with this repository. They are large binaries whose author
and licence are not documented, so they are kept out of version control.

The package does not need them: bring your own models and let
`Build Sample Scene` wrap them into pivot-corrected vehicle prefabs, or run it with nothing
selected to get placeholder box cars.

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
