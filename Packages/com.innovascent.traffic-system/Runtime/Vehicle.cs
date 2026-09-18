using System.Collections;
using UnityEngine; 

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Sistema de vehículo con validación de Lane ID
    /// Version 2.2 - Optimizado: 
    /// - Removida variable chassisLean no utilizada
    /// - Reducida frecuencia de CheckDestroyPoints
    /// - Debug.Log envueltos en condicionales
    /// - Cacheada referencia a TrafficManager
    /// </summary>
    public class Vehicle : MonoBehaviour
    {
        // ============ REFERENCIAS CACHEADAS ============
        private Transform cachedTransform;
        private Collider myCollider; 
        private Transform[] waypoints;
        private Transform[] destroyPoints;
        private VehicleCharacteristics characteristics;

        // Cache de TrafficManager para evitar accesos repetidos
        private TrafficManager trafficManager;
        private TrafficConfig trafficConfig;

        // ============ LANE TRACKING ============
        [Header("Identificación de Carril")]
        [Tooltip("ID del carril asignado al spawn - NO MODIFICAR MANUALMENTE")]
        public string assignedLaneId = "";

        private LaneDirection currentLaneDirection;
        private bool isInIntersection;
        private bool shouldYieldInIntersection;

        // ============ ANIMACIÓN DE RUEDAS ============
        [Header("Referencias de Ruedas (Opcional)")]
        public Transform[] frontWheels;
        public Transform[] rearWheels;
        public Transform chassis;

        // ============ WAYPOINT SYSTEM ============
        private int currentWaypointIndex;
        private int lookAheadIndex;
        private const int LOOK_AHEAD_DISTANCE = 2;

        // ============ MOVIMIENTO Y VELOCIDAD ============
        private float currentSpeed;
        private float targetSpeed;
        private float baseTargetSpeed;
        private byte state = 0; // 0=Driving, 1=Braking, 2=Stopped, 3=Accelerating

        // ============ DETECCIÓN ============
        private bool hasVehicleAhead;
        private float distToVehicleAhead;
        private float minSafeDistance;
        private float comfortDistance;
        private float panicDistance;
        private float detectionDistance;

        // ============ STEERING Y ANIMACIÓN ============
        private float currentSteerAngle;
        private float targetSteerAngle;
        private float wheelRotation;
        private float suspensionOffset;
        // REMOVIDO: chassisLean - nunca utilizada

        // ============ OPTIMIZACIÓN ============
        private int poolIndex;
        private float destroyRadius;
        private float updateTimer;
        private float destroyCheckTimer; // Nuevo: para reducir frecuencia de CheckDestroyPoints
        private const float UPDATE_INTERVAL = 0.05f;
        private const float DESTROY_CHECK_INTERVAL = 0.5f; // Verificar destrucción cada 0.5s

        // ============ DECISION SYSTEM ============
        private WaypointDecision currentDecision;
        private bool isProcessingDecision = false;

        // ============ CONFIGURACIÓN INICIAL ============
        private Vector3 initialChassisPosition;
        private Quaternion initialChassisRotation;

        [Header("Animación Procedural")]
        public Transform carBody;
        private Vector3 initialBodyPosition;
        private float motorShakeTime;
        private float suspensionBounceTime;
        private float lastBounceTime;

        // ============ DEBUG ============
        #if UNITY_EDITOR
        private const bool DEBUG_ENABLED = true;
        #else
        private const bool DEBUG_ENABLED = false;
        #endif

        void Awake()
        {
            if (carBody != null)
            {
                initialBodyPosition = carBody.localPosition;
            }

            cachedTransform = transform;
            myCollider = GetComponent<Collider>();

            // Layer is assigned by TrafficManager.SpawnVehicle, which owns the resolved layer index.

            if (chassis != null)
            {
                initialChassisPosition = chassis.localPosition;
                initialChassisRotation = chassis.localRotation;
            }
        } 

        public void Initialize(ref LaneConfig lane, int poolIdx, VehicleCharacteristics chars)
        { 
            // Cachear referencias
            trafficManager = TrafficManager.Instance;
            trafficConfig = trafficManager?.config;

            waypoints = lane.waypoints;
            destroyPoints = lane.destroyPoints;
            destroyRadius = lane.destroyRadius;
            characteristics = chars;
            poolIndex = poolIdx;

            assignedLaneId = lane.laneId;

            #if UNITY_EDITOR
            TrafficLog.Info($"[Vehicle] Spawned con Lane ID: '{assignedLaneId}'");
            #endif

            baseTargetSpeed = lane.velocidadMaxima * chars.multiplicadorVelocidad;
            targetSpeed = baseTargetSpeed;
            currentSpeed = baseTargetSpeed * 0.5f;

            currentWaypointIndex = 0;
            lookAheadIndex = Mathf.Min(LOOK_AHEAD_DISTANCE, waypoints.Length - 1);
            state = 3;

            currentSteerAngle = 0f;
            wheelRotation = 0f;
            suspensionOffset = 0f;

            isProcessingDecision = false;
            currentDecision = null;
            destroyCheckTimer = 0f;

            CalculateDistancesBasedOnSize();
            UpdateCurrentLane();
        }

        void CalculateDistancesBasedOnSize()
        {
            float vehicleLength = 2.5f;

            if (myCollider != null)
            {
                BoxCollider boxCol = myCollider as BoxCollider;
                if (boxCol != null)
                {
                    vehicleLength = boxCol.size.z * cachedTransform.localScale.z;
                }
                else
                {
                    vehicleLength = myCollider.bounds.size.z;
                }
            }

            float sizeMultiplier = characteristics.multiplicadorDistancia;
            panicDistance = Mathf.Max(vehicleLength * 0.5f * sizeMultiplier, 1.0f);
            minSafeDistance = Mathf.Max(vehicleLength * 1.0f * sizeMultiplier, 2.0f);
            comfortDistance = Mathf.Max(vehicleLength * 1.5f * sizeMultiplier, 3.0f);
            detectionDistance = Mathf.Clamp(vehicleLength * 4.0f * sizeMultiplier, 10f, 40f);
        }

        void Update()
        {
            // Validación usando cache
            if (trafficManager == null || trafficConfig == null)
            {
                trafficManager = TrafficManager.Instance;
                trafficConfig = trafficManager?.config;
                if (trafficConfig == null) return;
            }

            float dt = Time.deltaTime * trafficConfig.simulationSpeed;

            // Detección y comportamiento (cada UPDATE_INTERVAL)
            updateTimer += dt;
            if (updateTimer >= UPDATE_INTERVAL)
            {
                DetectVehicleAhead();
                UpdateBehavior(dt);
                updateTimer = 0f;
            }

            // CheckDestroyPoints optimizado (cada DESTROY_CHECK_INTERVAL)
            destroyCheckTimer += dt;
            if (destroyCheckTimer >= DESTROY_CHECK_INTERVAL)
            {
                CheckDestroyPoints();
                destroyCheckTimer = 0f;
            }

            ApplySpeed(dt);
            MoveVehicle(dt);
            UpdateWheelAnimations(dt);
            UpdateMotorShake(dt);          
            UpdateSuspensionBounce(dt); 
        }

        // ============ SISTEMA DE ANIMACIÓN DE RUEDAS ============

        void UpdateWheelAnimations(float dt)
        {
            if (currentSpeed < 0.1f && state == 2) return;
            if (characteristics == null) return;

            float speedInMetersPerSecond = currentSpeed / 3.6f;
            float wheelCircumference = 2f * Mathf.PI * characteristics.WHEEL_RADIUS;
            float rotationPerSecond = (speedInMetersPerSecond / wheelCircumference) * 360f;
            wheelRotation += rotationPerSecond * dt;

            if (frontWheels != null)
            {
                for (int i = 0; i < frontWheels.Length; i++)
                {
                    if (frontWheels[i] != null)
                    {
                        frontWheels[i].localRotation = Quaternion.Euler(wheelRotation, currentSteerAngle, 0f);
                    }
                }
            }

            if (rearWheels != null)
            {
                for (int i = 0; i < rearWheels.Length; i++)
                {
                    if (rearWheels[i] != null)
                    {
                        rearWheels[i].localRotation = Quaternion.Euler(wheelRotation, 0f, 0f);
                    }
                }
            }

            float targetBounce = 0f;
            if (state == 3)
            {
                targetBounce = -characteristics.SUSPENSION_BOUNCE;
            }
            else if (state == 1)
            {
                targetBounce = characteristics.SUSPENSION_BOUNCE * 1.5f;
            }

            suspensionOffset = Mathf.Lerp(suspensionOffset, targetBounce, dt * characteristics.SUSPENSION_SPEED); 
        } 

        void UpdateMotorShake(float dt)
        {
            if (carBody == null) return;

            motorShakeTime += dt * 10f;

            float speedFactor = currentSpeed / baseTargetSpeed;
            float idleShake = state == 2 ? 0.005f : 0f;
            float drivingShake = speedFactor * 0.003f;

            float shakeAmount = idleShake + drivingShake;

            Vector3 shake = new Vector3(
                Mathf.Sin(motorShakeTime * 2.3f) * shakeAmount,
                Mathf.Sin(motorShakeTime * 3.7f) * shakeAmount * 0.5f,
                Mathf.Cos(motorShakeTime * 2.1f) * shakeAmount * 0.3f
            );

            carBody.localPosition = initialBodyPosition + shake;
        }

        void UpdateSuspensionBounce(float dt)
        {
            if (chassis == null || characteristics == null) return;

            suspensionBounceTime += dt;

            if (suspensionBounceTime - lastBounceTime > Random.Range(3f, 8f) && currentSpeed > 10f)
            {
                lastBounceTime = suspensionBounceTime;

                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(BounceEffect());
                }
            }
        }

        IEnumerator BounceEffect()
        { 
            if (chassis == null) yield break;

            float duration = 0.3f;
            float elapsed = 0f;
            float bounceHeight = 0.08f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float bounce = Mathf.Sin(t * Mathf.PI) * bounceHeight;

                chassis.localPosition = initialChassisPosition + Vector3.up * (suspensionOffset + bounce);
                yield return null;
            }
        }

        // ============ SISTEMA DE MOVIMIENTO ============

        void MoveVehicle(float dt)
        {
            if (state == 2 || currentSpeed < 0.1f) return;

            if (waypoints == null || waypoints.Length == 0) return;

            if (currentWaypointIndex >= waypoints.Length)
            {
                cachedTransform.position += cachedTransform.forward * currentSpeed * dt;
                return;
            }

            UpdateCurrentLane();

            Transform currentWp = waypoints[currentWaypointIndex];
            Transform lookAheadWp = waypoints[Mathf.Min(lookAheadIndex, waypoints.Length - 1)];

            if (currentWp == null || lookAheadWp == null) return;

            // Verificar waypoint de decisión
            if (currentDecision == null && !isProcessingDecision)
            {
                currentDecision = currentWp.GetComponent<WaypointDecision>();

                if (currentDecision != null && !currentDecision.PuedeUsarEsteWaypoint(assignedLaneId))
                {
                    currentDecision = null;
                }
            }

            if (characteristics == null) return;

            // Steering suave
            Vector3 currentTarget = currentWp.position;
            Vector3 futureTarget = lookAheadWp.position;

            float distToCurrent = Vector3.Distance(cachedTransform.position, currentTarget);
            float blendFactor = Mathf.Clamp01(1f - (distToCurrent / 5f));

            Vector3 targetPos = Vector3.Lerp(currentTarget, futureTarget, blendFactor);
            Vector3 dirToTarget = targetPos - cachedTransform.position;
            dirToTarget.y = 0f;

            if (dirToTarget.sqrMagnitude > 0.05f)
            {
                Vector3 localDir = cachedTransform.InverseTransformDirection(dirToTarget.normalized);
                targetSteerAngle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                targetSteerAngle = Mathf.Clamp(targetSteerAngle, -characteristics.MAX_STEER_ANGLE, characteristics.MAX_STEER_ANGLE);

                currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, dt * characteristics.STEER_SPEED);

                Quaternion targetRot = Quaternion.LookRotation(dirToTarget);
                float rotationSpeed = 3f + (currentSpeed / baseTargetSpeed) * 2f;
                cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRot, dt * rotationSpeed);

                cachedTransform.position += cachedTransform.forward * currentSpeed * dt;
            }

            // Avance de waypoint
            if (distToCurrent < 3f)
            {
                if (currentDecision != null && !isProcessingDecision)
                {
                    isProcessingDecision = true;
                    ProcesarDecision();
                }
                else if (currentDecision == null)
                {
                    AvanzarAlSiguienteWaypoint();
                }
            }
        }

        void ProcesarDecision()
        {
            if (currentDecision == null) return;

            Transform[] nuevaRuta = currentDecision.GetRutaAleatoria(assignedLaneId, cachedTransform.position);

            if (nuevaRuta != null && nuevaRuta.Length > 0)
            {
                string nuevoLaneId = currentDecision.GetNuevoLaneId(nuevaRuta);
                if (!string.IsNullOrEmpty(nuevoLaneId))
                {
                    #if UNITY_EDITOR
                    TrafficLog.Info($"[Vehicle] Cambio de Lane ID: '{assignedLaneId}' → '{nuevoLaneId}'");
                    #endif
                    assignedLaneId = nuevoLaneId;
                }

                waypoints = nuevaRuta;
                currentWaypointIndex = 0;
                lookAheadIndex = Mathf.Min(LOOK_AHEAD_DISTANCE, waypoints.Length - 1);
            }
            else
            {
                AvanzarAlSiguienteWaypoint();
            }

            currentDecision = null;
            isProcessingDecision = false;
        }

        void AvanzarAlSiguienteWaypoint()
        {
            if (waypoints == null) return;

            currentWaypointIndex++;
            lookAheadIndex = Mathf.Min(currentWaypointIndex + LOOK_AHEAD_DISTANCE, waypoints.Length - 1);
        }

        // ============ DETECCIÓN ============

        void DetectVehicleAhead()
        {
            if (trafficManager == null)
            {
                hasVehicleAhead = false;
                distToVehicleAhead = detectionDistance;
                return;
            }

            bool detectedAny;
            float dist;
            GameObject detectedVehicle;

            detectedAny = trafficManager.DetectVehicleAhead(
                cachedTransform.position,
                cachedTransform.forward,
                detectionDistance,
                out dist,
                out detectedVehicle,
                myCollider
            );

            if (!detectedAny || detectedVehicle == null)
            {
                hasVehicleAhead = false;
                distToVehicleAhead = detectionDistance;
                return;
            }

            Vehicle otherVehicle = detectedVehicle.GetComponent<Vehicle>();
            if (otherVehicle == null)
            {
                hasVehicleAhead = true;
                distToVehicleAhead = dist;
                return;
            }

            // Validación de dirección y lane
            Vector3 otherForward = detectedVehicle.transform.forward;
            float dotProduct = Vector3.Dot(cachedTransform.forward, otherForward);
            bool isOpposing = dotProduct < -0.3f;

            if (isOpposing && !isInIntersection)
            {
                hasVehicleAhead = false;
                distToVehicleAhead = detectionDistance;
                return;
            }

            // Si el otro vehículo está en carril diferente, ignorar en rectas
            if (!isInIntersection && !string.IsNullOrEmpty(assignedLaneId) && !string.IsNullOrEmpty(otherVehicle.assignedLaneId))
            {
                if (assignedLaneId != otherVehicle.assignedLaneId)
                {
                    hasVehicleAhead = false;
                    distToVehicleAhead = detectionDistance;
                    return;
                }
            }

            // Lógica de prioridad en intersecciones
            if (isInIntersection && shouldYieldInIntersection)
            {
                LaneDirection otherLaneDir = otherVehicle.currentLaneDirection;
                if (otherLaneDir != null && currentLaneDirection != null)
                {
                    bool otherHasHigherPriority = otherLaneDir.priority < currentLaneDirection.priority;
                    if (otherHasHigherPriority && dist < comfortDistance * 2f)
                    {
                        hasVehicleAhead = true;
                        distToVehicleAhead = dist;
                        return;
                    }
                }
            }

            hasVehicleAhead = dotProduct > 0.3f || dist < minSafeDistance * 2f;
            if (hasVehicleAhead) distToVehicleAhead = dist;
        }

        // ============ COMPORTAMIENTO Y VELOCIDAD ============

        void UpdateBehavior(float dt)
        {
            if (waypoints == null || waypoints.Length == 0 || currentWaypointIndex >= waypoints.Length)
            {
                state = 0;
                targetSpeed = baseTargetSpeed;
                return;
            }

            targetSpeed = baseTargetSpeed;
            bool debeFrenar = false;
            bool debeDetenerse = false;

            if (hasVehicleAhead)
            {
                if (distToVehicleAhead < panicDistance)
                {
                    debeDetenerse = true;
                    targetSpeed = 0f;
                }
                else if (distToVehicleAhead < minSafeDistance)
                {
                    debeDetenerse = true;
                    targetSpeed = 0f;
                }
                else if (distToVehicleAhead < comfortDistance)
                {
                    debeFrenar = true;
                    float normalizedDist = (distToVehicleAhead - minSafeDistance) / (comfortDistance - minSafeDistance);
                    targetSpeed = baseTargetSpeed * Mathf.Clamp01(normalizedDist) * 0.6f;
                }
            }

            if (trafficManager != null && trafficConfig != null)
            {
                if (trafficConfig.useSemaforos && !debeDetenerse)
                {
                    CheckTrafficLights(ref debeFrenar, ref debeDetenerse);
                }
            }

            if (debeDetenerse)
            {
                state = 1;
            }
            else if (debeFrenar)
            {
                state = 1;
            }
            else if (state == 2)
            {
                if (CanStartMoving()) state = 3;
            }
            else
            {
                state = (currentSpeed < targetSpeed * 0.95f) ? (byte)3 : (byte)0;
            }
        }

        void CheckTrafficLights(ref bool debeFrenar, ref bool debeDetenerse)
        {
            if (trafficManager == null || trafficConfig == null || waypoints == null) return;

            int waypointsToCheck = Mathf.Min(3, waypoints.Length - currentWaypointIndex);

            for (int i = 0; i < waypointsToCheck; i++)
            {
                if (currentWaypointIndex + i >= waypoints.Length) break;

                Transform wpAdelante = waypoints[currentWaypointIndex + i];
                if (wpAdelante == null) continue;

                var semaforo = trafficManager.GetTrafficLightForWaypoint(wpAdelante);

                if (semaforo != null)
                {
                    float distToWaypoint = Vector3.Distance(cachedTransform.position, wpAdelante.position);
                    var estadoSemaforo = semaforo.GetEstado();

                    if (estadoSemaforo == TrafficLightController.Estado.Rojo)
                    {
                        if (distToWaypoint <= trafficConfig.distanciaFrenadoSemaforo)
                        {
                            debeDetenerse = true;
                            targetSpeed = 0f;
                            break;
                        }
                        else if (distToWaypoint <= trafficConfig.distanciaFrenadoSemaforo * 1.5f)
                        {
                            debeFrenar = true;
                            float factor = (distToWaypoint - trafficConfig.distanciaFrenadoSemaforo) / (trafficConfig.distanciaFrenadoSemaforo * 0.5f);
                            targetSpeed = Mathf.Min(targetSpeed, baseTargetSpeed * Mathf.Clamp01(factor));
                        }
                    }
                }
            }
        }

        bool CanStartMoving()
        {
            if (hasVehicleAhead && distToVehicleAhead < minSafeDistance * 2f) return false;

            if (waypoints == null || trafficManager == null) return true;

            if (currentWaypointIndex < waypoints.Length)
            {
                Transform currentWp = waypoints[currentWaypointIndex];
                if (currentWp == null) return true;

                var semaforo = trafficManager.GetTrafficLightForWaypoint(currentWp);
                if (semaforo != null && semaforo.GetEstado() != TrafficLightController.Estado.Verde)
                {
                    return false;
                }
            }

            return true;
        }

        void ApplySpeed(float dt)
        {
            if (characteristics == null) return;

            switch (state)
            {
                case 0:
                    currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, dt * characteristics.aceleracion * 0.3f);
                    break;

                case 1:
                    float brakeForce = characteristics.frenado;
                    if (hasVehicleAhead && distToVehicleAhead < panicDistance) brakeForce *= 2.5f;

                    currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, dt * brakeForce);

                    if (currentSpeed < 0.1f && targetSpeed < 0.1f)
                    {
                        currentSpeed = 0f;
                        state = 2;
                    }
                    break;

                case 2:
                    currentSpeed = 0f;
                    break;

                case 3:
                    currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, dt * characteristics.aceleracion * 0.8f);
                    if (currentSpeed >= targetSpeed * 0.95f) state = 0;
                    break;
            }

            currentSpeed = Mathf.Clamp(currentSpeed, 0f, baseTargetSpeed);
        }

        // ============ UTILIDADES ============

        void CheckDestroyPoints()
        {
            if (destroyPoints == null || destroyPoints.Length == 0) return;
            if (trafficManager == null) return;

            Vector3 vehiclePos = cachedTransform.position;
            float radiusSqr = destroyRadius * destroyRadius;

            for (int i = 0; i < destroyPoints.Length; i++)
            {
                if (destroyPoints[i] == null) continue;

                float distSqr = (vehiclePos - destroyPoints[i].position).sqrMagnitude;
                if (distSqr < radiusSqr)
                {
                    bool hasCompletedRoute = waypoints != null && currentWaypointIndex >= waypoints.Length * 0.5f;
                    if (hasCompletedRoute)
                    {
                        trafficManager.ReturnVehicle(this, poolIndex);
                        return;
                    }
                }
            }
        }

        void UpdateCurrentLane()
        {
            if (waypoints == null || waypoints.Length == 0 || currentWaypointIndex >= waypoints.Length)
            {
                currentLaneDirection = null;
                isInIntersection = false;
                return;
            }

            Transform currentWp = waypoints[currentWaypointIndex];
            if (currentWp == null) return;

            LaneDirection laneDir = currentWp.GetComponent<LaneDirection>();

            if (laneDir != null)
            {
                currentLaneDirection = laneDir;
                isInIntersection = (laneDir.zoneType == LaneDirection.ZoneType.Intersection);
                shouldYieldInIntersection = laneDir.requiresYield;
            }
        }

        // ============ DEBUG VISUAL ============
        #if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || cachedTransform == null) return;

            Vector3 pos = cachedTransform.position;
            Vector3 fwd = cachedTransform.forward;

            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawSphere(pos + fwd * panicDistance, 0.5f);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(pos + fwd * minSafeDistance, 0.8f);

            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(pos + fwd * comfortDistance, 1f);

            if (hasVehicleAhead)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(pos + Vector3.up, pos + fwd * distToVehicleAhead + Vector3.up);
            }

            if (waypoints != null && currentWaypointIndex < waypoints.Length && lookAheadIndex < waypoints.Length)
            {
                if (waypoints[currentWaypointIndex] != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, waypoints[currentWaypointIndex].position);
                }

                if (waypoints[lookAheadIndex] != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(pos, waypoints[lookAheadIndex].position);
                    Gizmos.DrawWireSphere(waypoints[lookAheadIndex].position, 1f);
                }
            }

            Vector3 labelPos = pos + Vector3.up * 3f;
            UnityEditor.Handles.Label(labelPos, $"Lane ID: {assignedLaneId}\nSpeed: {currentSpeed:F1} km/h");
        }
        #endif
    }
}
