# Changelog

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
