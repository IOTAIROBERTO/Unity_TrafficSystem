# Changelog

## [1.1.0]

### Fixed — a preset with no prefab took the whole manager down
- A `VehiclePreset` with no prefab assigned made `new VehiclePool(null, ...)` throw inside
  `TrafficManager.Awake`. Awake aborted there, so no pool was ever registered and
  `CacheVehicleWeights` never ran; `totalWeight` stayed 0, every spawn drew index 0, and the
  dictionary lookup threw `KeyNotFoundException` once per spawn attempt for the rest of the
  session. One unassigned field produced thousands of errors pointing at the wrong line.
- Unusable presets are now skipped at startup with one error naming the asset, the remaining
  presets still spawn, and `SpawnVehicle` looks the pool up with `TryGetValue` instead of
  indexing blind. With nothing usable left it says so once rather than failing every frame.
- The Setup tab flags a preset with no prefab before play, with buttons to select it or drop it
  from the manager. The checklist creates empty presets on purpose, so it should be the thing
  that catches one left unfilled.

### Added — junctions, fleet palette and splines
- **Junction stamp**: pick an arm length and a phasing, click in the Scene view, and get four
  approach lanes, a traffic light on each and a `FourWayIntersectionController` wired to all
  four. One-arm-at-a-time and opposing-pairs phasings carry their own timings, since four
  phases at two-phase durations produce a cycle long enough to look broken.
- **Fleet palette**: every vehicle preset in the project as a thumbnail in the Design tab, with
  the ones that spawn boxed. Click to add or remove. Dragging a car prefab or a raw model from
  the Project window onto the Scene view wraps it and adds it to the fleet.
- Dropping a model that cannot be wrapped now adds nothing and says so. `TrafficVehiclePrefabs.Collect`
  falls back to every preset in the project when wrapping fails, which is right for the sample
  builder and wrong here — dropping one car was adding fifteen.
- **Spline lanes**, in their own assembly gated on `com.unity.splines`: draw a road as a spline
  with tangent handles and bake it into waypoints, optionally offset to the driver's right.
  Re-baking replaces the lane instead of duplicating it. The package still has no dependency on
  splines — without the package the assembly is skipped and everything else compiles unchanged.

### Added — graphical editing in the Scene view
- The whole setup is now drawn in the Scene view and edited by dragging, without a window:
  lanes as coloured paths with flow arrows, a green ring on the spawn point, a red ring on the
  end of the route, and each traffic light joined to the waypoint it governs by a dotted line
  with its detection radius drawn.
- On the selected lane: drag a dot to move a waypoint (neighbours re-orient as you drag), click
  the yellow dot on a segment to insert one there, the red dot beside a waypoint to delete it,
  and the blue arrow past the last one to keep extending the lane.
- Only the selected lane carries handles. Other lanes show small dots that select them, so a
  city's worth of lanes stays readable instead of disappearing under a wall of gizmos.
- **Snap** rounds dragged waypoints onto a grid, for roads that need to stay square.
- A **Traffic System** overlay in the Scene view carries the enable toggle, the snap size, the
  new-lane field and the traffic-light button, so authoring never leaves the viewport.
- `TrafficLaneDesigner.CreateWaypoint` and `RenameWaypoints` are public, so waypoints can be
  inserted at an index rather than only appended.

### Added — step-by-step authoring
- **Design tab** in `Tools > InnovAscent > Traffic System`: build a traffic setup by clicking in
  the Scene view instead of wiring arrays by hand. Type a lane id, press **Draw**, and every
  click drops a waypoint where the cursor points. The lane is drawn as a green outline with a
  dotted line to the cursor and an overlay showing the lane id and the waypoint count. Enter or
  Escape finishes.
- Each waypoint is oriented towards the next one as it is placed, and the `LaneConfig` is
  rebuilt on every click: the first waypoint becomes the spawn point, the last the destroy
  point, so a lane is runnable the moment it is drawn.
- Lane list with **Select**, **Extend** (keep clicking on an existing lane), **Re-orient** and
  delete, plus **Rebuild every lane from the hierarchy** for when waypoints are reordered or
  deleted by hand in the Hierarchy window.
- **Add traffic light**: pick the waypoint the light governs and it is built on the kerb to the
  driver's right, facing the approaching traffic — pole, housing and three coloured bulbs, with
  a `TrafficLightController` registered against that waypoint.
- Everything the designer creates is registered with `Undo`.
- `Run Self-Test` menu entry: drives the whole designer API on a throwaway GameObject in the
  open scene, asserts the result and deletes what it made. Use it to confirm the package works
  in a host project. It opens no dialogs and does not touch the scene contents.

### Fixed — sample scene layout
- Lanes sat on the wrong side of their avenue. The offsets were written out by hand per lane
  and every one of the four had the sign backwards, so traffic drove on the left. They are now
  derived from `Cross(up, direction)`, which cannot be got backwards.
- Traffic lights stood on the near kerb, and for two of the approaches on the far side of the
  centreline, in the opposing lane. Each now stands across the junction on the driver's right,
  the placement used through most of the Americas, where it is read head-on while approaching.
- Turning was a jump between two straight lines: the route swapped to waypoints sitting across
  the junction and the vehicle steered towards them, reading as a twitch the wrong way just
  before the turn. Turns now follow a curved path built from the approach lane into the exit
  lane, and the choice is made two waypoints out rather than under the lights.
- The crossroads now opens one arm at a time instead of both opposing arms together. It halves
  throughput, but it removes oncoming traffic from the junction entirely, which is the only
  conflict a signalled crossroads cannot separate on its own, and makes turning safe.
- Lane speeds lowered to 24 km/h on the avenues and 20 on the ring, and density to 12.

### Fixed — traffic behaviour
- Oncoming traffic braked vehicles inside junctions. `DetectVehicleAhead` only ignored vehicles
  facing the opposite way when *outside* an intersection, and inside one it treated any vehicle
  within twice the safe distance as the one ahead. Two vehicles meeting at a crossing therefore
  stopped each other and neither could leave. Oncoming traffic is on the other carriageway and
  is now ignored everywhere; crossing traffic only stops the side that has to give way.
- A vehicle that turned kept the destroy points of the lane it spawned in, which sit on a road
  it will never reach, so it drove past the end of its route until the overrun timer fired. It
  now retires at the end of the route it is actually driving.

### Added — jam resolution
- `Vehicle` detects being stopped while no red light is holding it. After
  `TrafficConfig.stuckCreepDelay` it stops yielding and crawls, which breaks a mutual block;
  after `stuckDespawnDelay` it returns to the pool. Panic distance still applies, so it creeps
  rather than driving through anything.
- A vehicle that runs out of waypoints without reaching a destroy point is retired after
  `routeOverrunTimeout` instead of driving straight forever holding a slot.
- The stuck timer decays at twice the rate it accumulates, so a vehicle that has started
  creeping keeps creeping instead of flipping between stopped and moving.
- Traffic lights are evaluated on every behaviour tick, not only when the vehicle is otherwise
  free to move, so a queue waiting at a red light is never mistaken for a jam.

### Added
- `TrafficSystem-SampleScene`: a block city with ground, a ring road, two avenues, four blocks
  of buildings, six lanes and four cube traffic lights, running real car models. Traffic can
  turn at the crossroads: each approach carries a `WaypointDecision` offering straight, right
  and left onto the avenue heading that way.
- The six vehicle models are now distributed with the Demo sample instead of being kept out of
  version control.

### Changed
- `Build Sample Scene` now generates the block city, and gained three guards learned from
  running it:
  - it strips the cameras, lights and audio listeners that DCC exports carry, which otherwise
    out-rank the scene camera and hijack the view on every pooled vehicle;
  - it rejects models whose footprint is too square to be a car, because normalising one
    produces a box wide enough to block the carriageway;
  - it skips presets whose prefab has no renderable mesh, so the scene cannot fill up with
    invisible traffic.
- `Build` is public and takes an optional model list, so it can be driven from a script. The
  menu entry keeps the confirmation prompts.

### Fixed
- The project had no Render Pipeline Asset assigned: `GraphicsSettings.defaultRenderPipeline`
  was null, so everything rendered through the Built-in pipeline and every URP material came
  out magenta. `PC_RPAsset` is now assigned in GraphicsSettings and in all six quality levels.
- Converted 38 vehicle materials from the Built-in `Standard` shader to `Universal Render
  Pipeline/Lit`, carrying colour, texture, metallic and smoothness across.

### Known issues
- The six car prefabs in `TrafficCarsPrefabs/` have no meshes: every `MeshFilter.sharedMesh`
  is null because the meshes lived in the project this system was extracted from. They spawn
  invisible vehicles and are superseded by `SampleScene/`.
- Three of the six vehicle models (Camaro, Ferrari Testarossa, Ford Ka) are rejected by the
  builder for having a near-square footprint, and are unused.


## [1.0.0]

First release as a UPM package. Extracted from `Assets/TRAFFIC_SYSTEM/`.

### Added
- `package.json`, `InnovAscent.TrafficSystem.asmdef` and a `Demo City Traffic` sample.
- `TrafficLog`: single logging gate, off by default, driven by `TrafficConfig.enableDebugLogs`.
- `TrafficConfig.vehicleLayerName` and `TrafficManager.VehicleLayerIndex`: the vehicle
  physics layer is now configurable instead of hardcoded.
- Editor tooling, shipped in the package under an Editor-only assembly
  (`InnovAscent.TrafficSystem.Editor`, excluded from builds):
  `Tools > InnovAscent > Traffic System` window with a Setup checklist and a live Config panel,
  plus `TrafficLayerUtility` to create the vehicle layer from code.
- `Build Sample Scene`: generates a four-way intersection scene, wrapping any selected raw
  model into a pivot-corrected, orientation-corrected, length-normalised vehicle prefab.
- `Assign material to models`: remaps every material slot of the selected model importers to
  one material.
- `Vehicle Models` sample: 13 vehicle models, one folder per vehicle with its own
  `Materials/` and `Textures/`, plus a shared `VehicleDefault.mat` (URP/Lit).

### Changed
- All types moved into the `InnovAscent.TrafficSystem` namespace.
- `Debug_Helper.cs` renamed to `TrafficDebugHelper.cs` to match its MonoBehaviour class name.
- `FindObjectsOfType` replaced with `FindObjectsByType` (deprecated in Unity 6).
- Vehicle layer is assigned only by `TrafficManager.SpawnVehicle`; `Vehicle.Awake` no longer
  resolves a layer by name.

### Fixed
- An empty `TrafficConfig.vehicleLayer` mask made every detection query match nothing, so
  vehicles never braked. It is now derived from the configured layer at startup.
- `TrafficManager.SpawnVehicle` could write past the end of `activeVehicles` when
  `maxTrafficDensity` changed after `Awake`.
- A non-empty `vehicleLayer` mask that did not cover the layer spawned vehicles are put on
  was left alone, so the two silently disagreed and nothing was ever detected. The resolved
  layer is now added to the mask and the mismatch is reported.

### Demo sample
- Removed `EnvironmentEscenas`, 775 objects of scenery from another project with no surviving
  mesh or material: it rendered nothing and produced 477 broken references on scene open.
- Removed 17 components whose script no longer exists, 2 of them on `TrafficCars_PickUp`,
  which logged a warning for every pooled instance.
- Assigned a material to 891 renderers that had none.
- `TrafficConfig.vehicleLayer` now ships empty so it is derived from `vehicleLayerName` in
  whatever project the sample is imported into, instead of a hardcoded mask pointing at a
  layer index that does not exist.

Verified in Unity 6000.0.68f1: scene opens with 0 broken references, play mode runs with
0 errors and 0 warnings, vehicles spawn on all four lanes, drive, and stop at red lights.
