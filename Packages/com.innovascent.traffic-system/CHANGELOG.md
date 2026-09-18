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
