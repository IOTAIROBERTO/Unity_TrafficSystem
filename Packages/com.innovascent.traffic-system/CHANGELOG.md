# Changelog

## [Unreleased]

### Added
- `TrafficSystem-SampleScene`: a block city with ground, a ring road, two avenues, four blocks
  of buildings, six lanes and four cube traffic lights, running real car models.
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
