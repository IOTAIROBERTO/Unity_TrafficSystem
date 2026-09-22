# Changelog

## [1.6.2]

### Fixed — turns cut the corner and clipped what the lanes run alongside
- The connector between two lanes was a quadratic curve with a single control point, placed along
  the heading it leaves on. That aligns the departure but not the arrival, so the curve swings
  across the corner rather than following the road: on a real warehouse it came within 0.66 m of
  the racking the lanes run beside.
- It is now a cubic curve with a control point on each heading, so a turn leaves along its own lane
  and arrives along the one it joins, staying in the corridor between them.
- The self-test asserts the connector's first segment is aligned with the lane it came from.

## [1.6.1]

### Fixed — trimming to a no-traffic area threw away half the corridor
- Trimming kept only a lane's longest stretch outside the area and deleted the rest. On a real
  layout, clearing 19 waypoints out of two areas destroyed 69: a corridor crossing an area lost
  everything on the far side, which is the opposite of keeping the road and routing around.
- A lane crossing an area is now split into a piece before it and a piece after, each kept as its
  own lane, the later ones taking a numbered id so two lanes never answer to the same name. Only
  a piece left with fewer than two waypoints is dropped.
- The self-test asserts that the number of waypoints removed equals the number that were inside,
  and that a lane cut in two is still in the manager.

## [1.6.0]

### Added — areas no route may enter
- `TrafficExclusionZone`: a box placed over part of a site that traffic must stay out of — a
  training area, a working bay, anywhere people stand. Part of a site is often not traffic at all,
  and a generator that only knows about lanes routes straight through it.
- Crossing detection ignores anything inside a zone, turn generation refuses a turn whose
  connector would cut through one (checked along the run, not just at its ends), and **Trim lanes
  out of them** cuts every lane back to its longest stretch outside, dropping any left with fewer
  than two waypoints.
- **No-traffic areas** in the Design tab adds and lists them; each is drawn in the Scene view as a
  red box, so what is off limits is visible while authoring.
- The rule survives regeneration: pressing *Turn at every crossing* again will not route back
  through an area.

## [1.5.1]

### Fixed — adding turns undid the give-way rules
- Creating a turn marks the lane it merges into as having the right of way, and it cleared
  `requiresYield` while doing so. Run after marking a scene, that silently undid the give-way rule
  for every other crossing sharing the same waypoint: on a real layout, marking 28 crossings and
  then adding turns left **16 of them with nobody yielding**.
- A merge now raises the priority without clearing a yield already set, the same sticky rule the
  crossing sweep uses. The self-test adds turns after marking and asserts no crossing is left
  without someone yielding.

## [1.5.0]

### Added — turns and stops without a traffic light to render
- **Turn at every crossing** adds a turn in both directions at each crossing, skipping any where
  the two lanes head within 120 degrees of opposite: without that filter a crossing between
  lanes running against each other gets a route that doubles traffic back on itself.
- **Stop point on every yielding side**, and `TrafficLaneDesigner.AddStopPoint`, create a
  `TrafficLightController` on a bare GameObject with no pole, housing or bulbs. Every use of the
  bulbs in the controller is null-guarded, so the stop works with nothing to render — for floors
  that already carry their own markings.
- A stop point on its own runs the green/amber/red cycle from `TrafficConfig`, so it is a timed
  pause. Assign it to a `FourWayIntersectionController` to phase it with others. Give way, by
  contrast, only holds a vehicle while another with priority is actually close.

## [1.4.3]

### Fixed — marking a whole scene could leave junctions where nobody gives way
- A waypoint often sits on more than one crossing. `Give way at every crossing` wrote each crossing
  independently, so a later one cleared the `requiresYield` an earlier one had set: on a real
  layout, 13 waypoints served two or three crossings and **3 of the 25 ended with neither side
  yielding**, while the UI still showed all 25 as marked.
- Giving way is now sticky while marking a scene: a waypoint that has to yield anywhere keeps
  yielding, holds the weaker priority, and accumulates the lanes it has to cooperate with. Picking
  the rule for a single crossing by hand still wins outright, since that is a deliberate choice.
- The self-test asserts that after marking a scene no crossing is left with nobody yielding.

## [1.4.2]

### Fixed — marking a crossing did nothing on lanes built outside the lane designer
- `MarkGiveWay` wrote the rule onto the waypoint's `LaneDirection` and returned quietly when there
  was none. Lanes authored any other way carry no `LaneDirection` at all, so on a real layout of
  266 waypoints every one of the 25 crossings reported itself marked in the UI and yielded to
  nobody. The tool now adds the component, seeding its lane id and flow direction from the lane.
- `TrafficBranchTool` skipped the same way when the waypoint a branch merges into had no
  `LaneDirection`, leaving the merge wired but without a right of way. It adds one too.
- **Branch** and **Add traffic light** were greyed out unless the selected object had a
  `LaneDirection`, which on those scenes meant every waypoint. They now test membership of a lane
  on the manager instead.

## [1.4.1]

### Fixed — the crossings list was unreadable
- Every crossing drew four buttons on its own row, so each entry was three lines tall inside a
  260 px box: past the first few there was nothing to see and nothing to click. The list is now
  one line per crossing — state, the two lanes, the coordinates and a Select — with the buttons in
  a panel underneath for whichever one is selected. The panel's buttons are 26 px tall.
- The list height is adjustable from 120 to 600 px, and the selected row is highlighted.
- Crossing markers in the Scene view were scaled purely by handle size, so they shrank to nothing
  at the zoom where a whole layout fits on screen, which is exactly when they are being looked
  for. They now have a 2.5 m floor, a filled centre and a label naming the two lanes.

## [1.4.0]

### Fixed — the give-way priority was written upside down
- `TrafficBranchTool` marked the lane being merged into with priority 8 and the arriving branch
  with 2. `Vehicle` compares priorities with `other.priority <= mine`, so a **lower** number is the
  stronger claim: the branch outranked the lane it was merging into, `requiresYield` was set but
  never fired, and nothing ever stopped. The tools now write 3 for the right of way and 7 for the
  lane that yields, matching the scale the sample builder already used.
- The self-test asserts the direction of the comparison, so an inverted constant fails a check
  instead of silently producing a merge where nobody gives way.

### Added — crossings between lanes drawn by hand
- **Crossings** in the Design tab: finds every place two lanes meet by intersecting their segments
  on the ground plane, clustering hits within 4 m so lanes running alongside each other report one
  crossing rather than a dozen.
- Per crossing, either lane can be given the right of way, or a turn can be added in either
  direction, built with the same connector as **Branch**.
- **Give way at every crossing** marks the lot, taking the longer lane as the main one.
- The Scene view draws each crossing: red where nobody gives way, green where the rule is set.

## [1.3.0]

### Added — a lane can split into any number of exits
- `WaypointDecision` gained `Branch[]`: one entry per exit, each with its own lane id, waypoints
  and relative weight. A waypoint can now offer three, four or more ways out instead of the fixed
  straight/right/left. Weights are relative, so 2 against 1 is twice as likely.
- **Branch** in the Design tab: select the waypoint the branch leaves from, pick the lane it
  should reach, set the weight. It builds a curved connector under `Branches`, adds the exit, and
  keeps a carry-on exit along the original lane so branching stays a choice. Repeat on the same
  waypoint to stack more exits.
- The far end of a branch is marked automatically: the arriving traffic gets `requiresYield` and a
  low priority, the lane being merged into keeps the right of way. A split cannot collide on its
  own; the merge can, and that is the part that needed the rule.
- The Scene view draws every exit as an arrow labelled with its share of traffic, thicker the more
  it takes, so the route tree is readable without opening an inspector.
- Decisions authored against the old three-route fields keep working: those are folded into exits
  at load, in memory, so no scene has to be re-saved.

## [1.2.0]

### Changed — the demo assets ship outside the package
- `Samples~/Demo` moved to `DemoAssets/` at the root of the same repository, and the `samples`
  entry is gone from `package.json`. The package folder drops from 67 MB to about 450 KB: it is
  code and editor tooling, nothing else. The demo scene, the three car models and the audio bank
  are downloaded from the repository instead, and copied into `Assets/` by hand.
- `Window > Package Manager > Samples` no longer offers the demo, since the Package Manager can
  only import a `Samples~` folder inside the package. The README documents the folder and links
  to it.
- Worth knowing: a UPM git install clones the whole repository, so this does not make installing
  the package a smaller download. It makes the installed package small, and it keeps the demo out
  of every project that only wants the runtime. A smaller clone would need a separate repository
  or a release asset.

## [1.1.1]

### Fixed — the sample shipped a model with no .meta
- `Samples~/Demo/Vehicles/Car/car.fbx` and `Vehicles/Materials/VehicleDefault.mat` had no `.meta`
  file. Unity assigns a fresh GUID to any asset imported without one, so `TrafficCar_car` would
  have resolved to nothing and the sample would have spawned an invisible car in any project that
  imported it. Both `.meta` files are restored with their original GUIDs, and every GUID the
  sample references now resolves to the package, the sample itself, a Unity built-in or URP.

### Changed — the sample scene matches what the tools build
- Everything the traffic system owns now hangs off the manager — `Lanes`, `Traffic Lights`, the
  `Crossroads Controller` and the debug helper — and the scenery is grouped under a `City` root.
  The lanes, the lights and the controller used to sit loose at the scene root, so drawing a lane
  in the sample created a second `Lanes` node under the manager instead of extending the one that
  was already there.
- Waypoints were already named `WP_00`, so the Scene view designer can insert, drag and delete in
  the sample without renumbering anything.

## [1.1.0]

### Changed — the Setup tab no longer creates traffic that cannot run
- **New preset asset** used to create an empty `VehiclePreset` and register it on the manager
  straight away, which is exactly the state that took `TrafficManager.Awake` down. It now fills
  the preset in from the car prefab selected in the Project window and adds that to the traffic;
  with nothing selected the asset is still created but deliberately left out of the manager,
  with a warning saying how to add it once it has a prefab.
- The checklist row says which of the two will happen before the button is pressed.

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
