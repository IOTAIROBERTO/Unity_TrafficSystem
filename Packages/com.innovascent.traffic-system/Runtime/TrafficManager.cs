using System.Collections.Generic;
using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Traffic Manager optimizado para VR Quest 2/3
    /// Version 2.3 - Removido audio ambiental (manejado externamente)
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public static TrafficManager Instance;

        [Header("Configuración")]
        public TrafficConfig config;
        public LaneConfig[] lanes;
        public VehicleCharacteristics[] vehicleTypes;

        // ============ POOLS Y CONTENEDORES ============
        private Vehicle[] activeVehicles;
        private int activeCount = 0;
        private Dictionary<int, VehiclePool> vehiclePools;
        private Transform vehicleContainer;

        // ============ SPAWN TIMERS ============
        private float[] laneSpawnTimers;
        private float[] nextSpawnTime;

        // ============ BUFFERS COMPARTIDOS (menos GC) ============
        private static readonly RaycastHit[] raycastHits = new RaycastHit[10];
        private static readonly Collider[] colliderBuffer = new Collider[30];

        // ============ WAYPOINT TRACKING ============
        private Dictionary<Transform, Vehicle> waypointOccupancy = new Dictionary<Transform, Vehicle>();
        private Dictionary<Transform, TrafficLightController> waypointTrafficLights = new Dictionary<Transform, TrafficLightController>();

        // ============ CACHE DE PESOS ============
        private int[] vehicleWeights;
        private int totalWeight;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (config == null)
            {
                TrafficLog.Error("TrafficConfig NO ASIGNADO en TrafficManager!");
                return;
            }

            TrafficLog.Enabled = config.enableDebugLogs;
            ResolveVehicleLayer();

            // Inicializar arrays
            activeVehicles = new Vehicle[config.maxTrafficDensity];
            laneSpawnTimers = new float[lanes.Length];
            nextSpawnTime = new float[lanes.Length];

            // Container para vehículos
            vehicleContainer = new GameObject("VehicleContainer").transform;
            vehicleContainer.SetParent(transform);

            // Crear pools
            // A preset with no prefab used to throw here, which aborted Awake: no pool was ever
            // registered, so every later spawn failed on a missing dictionary key instead of
            // reporting the one asset that was actually wrong.
            vehiclePools = new Dictionary<int, VehiclePool>();
            for (int i = 0; i < vehicleTypes.Length; i++)
            {
                if (!IsUsable(vehicleTypes[i], i)) continue;
                vehiclePools[i] = new VehiclePool(vehicleTypes[i].prefab, 3, vehicleContainer);
            }

            if (vehiclePools.Count == 0)
            {
                TrafficLog.Error(
                    "[TrafficManager] No usable vehicle presets, so no traffic will spawn. " +
                    "Assign a prefab to each VehiclePreset listed above, or remove it from the manager.");
            }

            CacheVehicleWeights();

            // Inicializar spawn timers
            for (int i = 0; i < lanes.Length; i++)
            {
                nextSpawnTime[i] = Random.Range(0f, lanes[i].cadenciaSpawn);
            }
        }

        /// <summary>
        /// Physics layer index assigned to spawned vehicles. Resolved once from the config
        /// so the package does not depend on a layer named "Vehicles" existing in the host project.
        /// </summary>
        public int VehicleLayerIndex { get; private set; } = -1;

        void ResolveVehicleLayer()
        {
            string layerName = string.IsNullOrEmpty(config.vehicleLayerName) ? "Vehicles" : config.vehicleLayerName;
            VehicleLayerIndex = LayerMask.NameToLayer(layerName);

            if (VehicleLayerIndex == -1)
            {
                VehicleLayerIndex = gameObject.layer;
                TrafficLog.Error(
                    $"[TrafficManager] Layer '{layerName}' does not exist. Create it in " +
                    $"Edit > Project Settings > Tags and Layers, or set TrafficConfig.vehicleLayerName. " +
                    $"Falling back to layer '{LayerMask.LayerToName(VehicleLayerIndex)}'.");
            }

            // The mask decides what the detection queries can see, and the layer index decides
            // where spawned vehicles are put. If the mask does not cover that layer the two
            // disagree and nothing is ever detected, so keep them consistent.
            int resolvedBit = 1 << VehicleLayerIndex;

            if (config.vehicleLayer.value == 0)
            {
                config.vehicleLayer = resolvedBit;
                TrafficLog.Warn($"[TrafficManager] TrafficConfig.vehicleLayer was empty; derived from layer '{LayerMask.LayerToName(VehicleLayerIndex)}'.");
            }
            else if ((config.vehicleLayer.value & resolvedBit) == 0)
            {
                config.vehicleLayer = config.vehicleLayer.value | resolvedBit;
                TrafficLog.Error(
                    $"[TrafficManager] TrafficConfig.vehicleLayer did not include layer " +
                    $"'{LayerMask.LayerToName(VehicleLayerIndex)}' ({VehicleLayerIndex}), the layer spawned vehicles are put on, " +
                    $"so no vehicle would ever have been detected. The layer has been added to the mask.");
            }
        }

        /// <summary>
        /// A preset can only be spawned from if it exists and points at a prefab. Reports the
        /// offending entry by name once, at startup, rather than failing on every spawn.
        /// </summary>
        bool IsUsable(VehicleCharacteristics preset, int index)
        {
            if (preset == null)
            {
                TrafficLog.Error($"[TrafficManager] vehicleTypes[{index}] is empty. Assign a VehiclePreset or remove the slot.");
                return false;
            }

            if (preset.prefab == null)
            {
                TrafficLog.Error(
                    $"[TrafficManager] VehiclePreset '{preset.name}' has no prefab assigned, so it cannot spawn. " +
                    $"Assign one in the Inspector, or remove it from the TrafficManager.");
                return false;
            }

            return true;
        }

        void CacheVehicleWeights()
        {
            vehicleWeights = new int[vehicleTypes.Length];
            totalWeight = 0;

            for (int i = 0; i < vehicleTypes.Length; i++)
            {
                // Weight 0 keeps an unusable preset out of the draw without shifting the indices
                // the pools are keyed by.
                vehicleWeights[i] = vehiclePools.ContainsKey(i) ? Mathf.Max(0, vehicleTypes[i].pesoSpawn) : 0;
                totalWeight += vehicleWeights[i];
            }
        }

        void Update()
        {
            if (config == null) return;

            float deltaTime = Time.deltaTime;

            // Spawn de vehículos
            for (int i = 0; i < lanes.Length; i++)
            {
                if (!lanes[i].activo) continue;

                laneSpawnTimers[i] += deltaTime;

                if (laneSpawnTimers[i] >= nextSpawnTime[i] && activeCount < config.maxTrafficDensity)
                {
                    if (CanSpawnInLane(ref lanes[i]))
                    {
                        SpawnVehicle(ref lanes[i]);
                        laneSpawnTimers[i] = 0f;

                        float variation = Random.Range(-lanes[i].variacionCadencia, lanes[i].variacionCadencia);
                        nextSpawnTime[i] = lanes[i].cadenciaSpawn + variation;
                    }
                }
            }
        }

        bool CanSpawnInLane(ref LaneConfig lane)
        {
            if (lane.spawnPoint == null) return false;

            int nearbyCount = Physics.OverlapSphereNonAlloc(
                lane.spawnPoint.position,
                lane.radioSeguridadSpawn,
                colliderBuffer,
                config.vehicleLayer
            );

            return nearbyCount == 0;
        }

        /// <summary>Index of a preset that has a pool, or -1 when there is nothing to spawn.</summary>
        int GetRandomVehicleIndexByWeight()
        {
            // Every weight at zero still has to pick something spawnable, so fall back to the
            // first preset that has a pool rather than to index 0, which may be the broken one.
            if (totalWeight == 0) return FirstPooledIndex();

            int random = Random.Range(0, totalWeight);
            int cumulative = 0;

            for (int i = 0; i < vehicleWeights.Length; i++)
            {
                cumulative += vehicleWeights[i];
                if (random < cumulative) return i;
            }

            return FirstPooledIndex();
        }

        int FirstPooledIndex()
        {
            for (int i = 0; i < vehicleTypes.Length; i++)
            {
                if (vehiclePools.ContainsKey(i)) return i;
            }

            return -1;
        }

        void SpawnVehicle(ref LaneConfig lane)
        {
            if (lane.spawnPoint == null || vehicleTypes.Length == 0) return;
            if (activeCount >= activeVehicles.Length) return;

            int vehicleIndex = GetRandomVehicleIndexByWeight();
            if (vehicleIndex < 0) return;
            if (!vehiclePools.TryGetValue(vehicleIndex, out VehiclePool pool)) return;

            VehicleCharacteristics characteristics = vehicleTypes[vehicleIndex];

            GameObject vehicleObj = pool.Get();
            vehicleObj.SetActive(true);

            vehicleObj.transform.position = lane.spawnPoint.position;
            vehicleObj.transform.rotation = lane.spawnPoint.rotation;

            Collider col = vehicleObj.GetComponent<Collider>();
            if (col == null)
            {
                col = vehicleObj.AddComponent<BoxCollider>();
            }
            vehicleObj.layer = VehicleLayerIndex;

            Vehicle vehicle = vehicleObj.GetComponent<Vehicle>();
            if (vehicle == null) vehicle = vehicleObj.AddComponent<Vehicle>();

            vehicle.Initialize(ref lane, vehicleIndex, characteristics);

            activeVehicles[activeCount] = vehicle;
            activeCount++;
        }

        public void ReturnVehicle(Vehicle vehicle, int poolIndex)
        {
            if (vehicle == null || vehicle.gameObject == null) return;
            if (!vehiclePools.ContainsKey(poolIndex)) return;

            for (int i = 0; i < activeCount; i++)
            {
                if (activeVehicles[i] == vehicle)
                {
                    activeVehicles[i] = activeVehicles[activeCount - 1];
                    activeVehicles[activeCount - 1] = null;
                    activeCount--;
                    break;
                }
            }

            vehiclePools[poolIndex].Return(vehicle.gameObject);
        }

        // ============ DETECCIÓN DE VEHÍCULOS ============

        public bool DetectVehicleAhead(
            Vector3 position,
            Vector3 forward,
            float distance,
            out float distToVehicle,
            out GameObject detectedVehicle,
            Collider myCollider = null)
        {
            distToVehicle = distance;
            detectedVehicle = null;
            float closestDist = distance;
            GameObject closestVehicle = null;

            float myLength = myCollider != null ? GetVehicleLength(myCollider) : 2.5f;
            float radius = Mathf.Clamp(myLength * 0.4f, 1f, 3f);
            Vector3 startPos = position + Vector3.up * 0.5f;

            // SphereCast
            int hits = Physics.SphereCastNonAlloc(startPos, radius, forward, raycastHits, distance, config.vehicleLayer);
            for (int i = 0; i < hits; i++)
            {
                ref RaycastHit hit = ref raycastHits[i];
                if (hit.collider != null && 
                    hit.collider.gameObject != null &&
                    hit.collider.gameObject.activeInHierarchy &&
                    hit.collider != myCollider)
                {
                    float otherLength = GetVehicleLength(hit.collider);
                    float realDistance = hit.distance - (myLength * 0.5f) - (otherLength * 0.5f);
                    realDistance = Mathf.Max(0.1f, realDistance);

                    if (realDistance > 0.3f && realDistance < closestDist)
                    {
                        closestDist = realDistance;
                        closestVehicle = hit.collider.gameObject;
                    }
                }
            }

            // OverlapSphere
            int nearbyCount = Physics.OverlapSphereNonAlloc(position, distance, colliderBuffer, config.vehicleLayer);
            for (int i = 0; i < nearbyCount; i++)
            {
                Collider col = colliderBuffer[i];
                if (col != null && 
                    col.gameObject != null &&
                    col.gameObject.activeInHierarchy &&
                    col != myCollider)
                {
                    Vector3 dirToOther = col.transform.position - position;
                    dirToOther.y = 0f;
                    float dist = dirToOther.magnitude;
                    float dotProduct = Vector3.Dot(forward, dirToOther.normalized);

                    if (dist > 0.5f && dist < distance && dotProduct > 0.3f)
                    {
                        float otherLength = GetVehicleLength(col);
                        float realDistance = dist - (myLength * 0.5f) - (otherLength * 0.5f);
                        realDistance = Mathf.Max(0.1f, realDistance);

                        if (realDistance < closestDist)
                        {
                            closestDist = realDistance;
                            closestVehicle = col.gameObject;
                        }
                    }
                }
            }

            if (closestVehicle != null)
            {
                distToVehicle = closestDist;
                detectedVehicle = closestVehicle;
                return true;
            }

            return false;
        }

        float GetVehicleLength(Collider col)
        {
            if (col == null) return 2.5f;

            BoxCollider box = col as BoxCollider;
            if (box != null)
            {
                return box.size.z * col.transform.localScale.z;
            }
            return col.bounds.size.z;
        }

        // ============ WAYPOINT MANAGEMENT ============

        public bool TryOccupyWaypoint(Transform waypoint, Vehicle vehicle)
        {
            if (waypoint == null) return true;
            if (vehicle == null || vehicle.gameObject == null) return false;

            if (!waypointOccupancy.ContainsKey(waypoint) || waypointOccupancy[waypoint] == null)
            {
                waypointOccupancy[waypoint] = vehicle;
                return true;
            }

            return waypointOccupancy[waypoint] == vehicle;
        }

        public void ReleaseWaypoint(Transform waypoint)
        {
            if (waypoint == null) return;

            if (waypointOccupancy.ContainsKey(waypoint))
            {
                waypointOccupancy[waypoint] = null;
            }
        }

        public void RegisterTrafficLight(Transform waypoint, TrafficLightController trafficLight)
        {
            if (waypoint == null || trafficLight == null) return;

            waypointTrafficLights[waypoint] = trafficLight;
        }

        public bool CanPassWaypoint(Transform waypoint)
        {
            if (config == null || !config.useSemaforos) return true;
            if (waypoint == null) return true;

            if (waypointTrafficLights.TryGetValue(waypoint, out var light) && light != null)
            {
                return light.PuedeAvanzar();
            }

            return true;
        }

        public TrafficLightController GetTrafficLightForWaypoint(Transform waypoint)
        {
            if (waypoint == null) return null;

            waypointTrafficLights.TryGetValue(waypoint, out var trafficLight);
            return trafficLight;
        }

        public int GetActiveVehicleCount() => activeCount;
    }
}
