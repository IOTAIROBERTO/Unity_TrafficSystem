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

## Quick start on a new project

Everything below happens in `Tools > InnovAscent > Traffic System`. Nothing has to be wired by
hand and no sample has to be imported.

1. **Setup tab → Fix all.** Creates the `Traffic System` GameObject with `TrafficManager` and
   `TrafficConfig` wired together, creates the vehicle physics layer and derives the layer mask.
2. **Setup tab → New preset asset.** Creates a `VehiclePreset` asset and registers it on the
   manager. Drop your car prefab into its `prefab` field. The prefab needs a renderer, a
   collider and a `Vehicle` component, with its pivot at the centre of the footprint on the
   ground and the car pointing down +Z.
3. **Design tab → type a lane id → Draw.** Click along the road in the Scene view; each click
   drops a waypoint. The first is the spawn point, the last the destroy point, and the lane is
   rebuilt on every click. Press Enter or Escape to finish.
4. **Design tab → Add traffic light** (optional). Select the waypoint the light should govern
   and press the button: the light is built on the kerb to the driver's right, facing the
   traffic it stops.
5. Repeat step 3 for every lane. Use **Extend** to keep adding to a lane you already drew, and
   **Re-orient** after moving waypoints by hand.
6. Press Play.

To confirm the package works in your project before building anything, run
`Tools > InnovAscent > Traffic System > Run Self-Test`. It drives the whole designer API on a
throwaway GameObject, asserts the result in the Console and deletes what it made.

If you would rather generate a whole grid city than draw it, use the **City tab**: set the
number of avenues, the block size and the behaviour of each junction, then press Build.

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

**Design tab** — authoring by clicking in the Scene view:
- **Draw** starts a new lane; every click drops a waypoint under the cursor, on whatever
  collider it hits or on the ground plane when it hits nothing
- each waypoint is oriented towards the next one, and the `LaneConfig` is rebuilt on every
  click: first waypoint = spawn point, last = destroy point
- the lane in progress is drawn as a green outline with a dotted line to the cursor and an
  overlay showing the lane id and waypoint count; Enter or Escape finishes
- the lane list offers **Select**, **Extend**, **Re-orient** and delete per lane, plus
  **Rebuild every lane from the hierarchy** after reordering waypoints by hand
- **Add traffic light** builds pole, housing and three coloured bulbs on the kerb to the
  driver's right of the selected waypoint, with a `TrafficLightController` registered on it
- everything it creates is registered with `Undo`

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

## Editing in the Scene view

Once a lane exists it is drawn in the Scene view and edited by dragging, no window needed. The
palette lives in the Scene view's own overlay (**Traffic System**; reopen it from the ☰ button
top right if it is hidden) and carries the **Edit in scene** toggle, a **Snap** grid, **New
lane**, and the traffic-light button.

What is drawn:
- each lane as a coloured path with cones showing which way traffic flows
- a green ring on the spawn point and a red ring on the end of the route
- traffic lights joined to the waypoint they govern by a dotted line, with their detection
  radius as a circle
- the lane id, its waypoint count and its speed, next to the spawn point

What can be dragged, on the lane you have selected:
- **the dots** move waypoints; the neighbours re-orient themselves as you drag, and **Snap**
  rounds the position onto a grid
- **the yellow dot on a segment** inserts a waypoint there, which is how a straight road
  becomes a curve
- **the red dot beside a waypoint** deletes it, down to a minimum of two
- **the blue arrow past the last waypoint** continues the lane with more clicks

Only the selected lane carries handles; every other lane shows small dots you click to switch
to it, so a city's worth of lanes stays readable.

## Junctions, the fleet and splines

**Stamp a junction.** Set an arm length and a phasing in the overlay or the Design tab, press
**Junction**, and click where the roads cross. You get four approach lanes, a traffic light on
each and a `FourWayIntersectionController` wired to all four, timed for the phasing chosen:

- *One arm at a time* — four phases. Halves throughput, but no oncoming traffic is ever in the
  box, which is the one conflict a signalled crossroads cannot separate, so turning is safe.
- *Opposing pairs* — two phases. More throughput; turns cross oncoming traffic.

**The fleet.** The Design tab shows every `VehiclePreset` in the project as a thumbnail. Click
one to add or remove it from the traffic; the ones that spawn are boxed. Dragging a car prefab
or a raw model from the Project window onto the Scene view builds a preset for it and adds it:
the model is wrapped on the way in — pivot moved to the centre of the footprint on the ground,
long axis rotated onto +Z, length normalised to 4.5 m, and the cameras and lights that DCC
exports carry stripped out. A model too square to be a car is refused with a warning rather
than turned into a box that blocks the carriageway.

**Curves from splines.** Install `com.unity.splines` and a menu entry appears:
`Tools > InnovAscent > Traffic System > Bake selected spline into a lane`. Draw the road as a
spline with its tangent handles, then bake it: the spline is sampled at a fixed spacing into
waypoints, optionally offset to the driver's right of the centreline. Re-baking replaces the
lane rather than piling up copies, so a tangent can be nudged and baked again.

The splines support lives in its own assembly, gated on `com.unity.splines` being present. The
package has no dependency on it: without the package that assembly is skipped and everything
else compiles unchanged.

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
