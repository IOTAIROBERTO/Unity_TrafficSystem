using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Helper visual para debuggear el sistema de tráfico
    /// Version 2.2 - Optimizado: Removido código comentado y optimizado FindObjectsOfType
    /// </summary>
    public class TrafficDebugHelper : MonoBehaviour
    {
        [Header("Visualización")]
        [Tooltip("Mostrar direcciones de carriles")]
        public bool showLaneDirections = true;

        [Tooltip("Mostrar detecciones de vehículos")]
        public bool showVehicleDetections = true;

        [Tooltip("Mostrar información de vehículos")]
        public bool showVehicleInfo = true;

        // Cache para evitar FindObjectsOfType múltiples veces por frame
        private Vehicle[] cachedVehicles;
        private LaneDirection[] cachedLanes;
        private float cacheTimer;
        private const float CACHE_INTERVAL = 0.5f;

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            // Actualizar cache periódicamente
            cacheTimer += Time.deltaTime;
            if (cacheTimer >= CACHE_INTERVAL || cachedVehicles == null)
            {
                cachedVehicles = FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
                cachedLanes = FindObjectsByType<LaneDirection>(FindObjectsSortMode.None);
                cacheTimer = 0f;
            }

            if (showLaneDirections)
            {
                DrawLaneDirections();
            }

            if (showVehicleInfo)
            {
                DrawVehicleInfo();
            }

            if (showVehicleDetections)
            {
                DrawVehicleDetections();
            }
        }

        void DrawLaneDirections()
        {
            if (cachedLanes == null) return;

            foreach (LaneDirection lane in cachedLanes)
            {
                if (lane == null) continue;

                Vector3 pos = lane.transform.position + Vector3.up;
                Vector3 dir = lane.GetFlowDirection();

                Color color = lane.zoneType switch
                {
                    LaneDirection.ZoneType.StraightLane => new Color(0f, 1f, 0f, 0.5f),
                    LaneDirection.ZoneType.OpposingLane => new Color(1f, 0f, 0f, 0.5f),
                    LaneDirection.ZoneType.Intersection => new Color(1f, 1f, 0f, 0.7f),
                    LaneDirection.ZoneType.TurnLane => new Color(0f, 1f, 1f, 0.6f),
                    _ => Color.white
                };

                Gizmos.color = color;
                Gizmos.DrawRay(pos, dir * 4f);

                // Punta de flecha
                Vector3 arrowTip = pos + dir * 4f;
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                Gizmos.DrawLine(arrowTip, arrowTip - dir * 0.8f + right * 0.5f);
                Gizmos.DrawLine(arrowTip, arrowTip - dir * 0.8f - right * 0.5f);

                if (lane.zoneType == LaneDirection.ZoneType.Intersection)
                {
                    float sphereSize = 1f - (lane.priority / 15f);
                    Gizmos.DrawWireSphere(pos, sphereSize);

                    if (lane.requiresYield)
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireCube(pos, Vector3.one * 0.8f);
                    }
                }
            }
        }

        void DrawVehicleInfo()
        {
            if (cachedVehicles == null) return;

            foreach (Vehicle vehicle in cachedVehicles)
            {
                if (vehicle == null) continue;

                Vector3 pos = vehicle.transform.position;
                Vector3 fwd = vehicle.transform.forward;

                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(pos + Vector3.up * 0.5f, fwd * 3f);

                Gizmos.color = Color.green;
                Gizmos.DrawSphere(pos + Vector3.up * 2f, 0.3f);
            }
        }

        void DrawVehicleDetections()
        {
            if (cachedVehicles == null) return;

            foreach (Vehicle vehicle in cachedVehicles)
            {
                if (vehicle == null) continue;

                Vector3 pos = vehicle.transform.position;
                Vector3 fwd = vehicle.transform.forward;

                Gizmos.color = new Color(1f, 1f, 0f, 0.1f);

                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 left = -right;

                float detectionDist = 15f;

                Gizmos.DrawLine(pos + Vector3.up * 0.5f, pos + Vector3.up * 0.5f + (fwd + right * 0.3f).normalized * detectionDist);
                Gizmos.DrawLine(pos + Vector3.up * 0.5f, pos + Vector3.up * 0.5f + (fwd + left * 0.3f).normalized * detectionDist);
                Gizmos.DrawLine(pos + Vector3.up * 0.5f, pos + Vector3.up * 0.5f + fwd * detectionDist);
            }
        }

        void OnGUI()
        {
            if (!showVehicleInfo) return;

            GUIStyle style = new GUIStyle();
            style.fontSize = 12;
            style.normal.textColor = Color.white;

            GUI.Label(new Rect(10, 10, 300, 20), "=== TRAFFIC DEBUG ===", style);

            int activeCount = cachedVehicles != null ? cachedVehicles.Length : 0;
            GUI.Label(new Rect(10, 30, 300, 20), $"Vehículos Activos: {activeCount}", style);

            // Leyenda de colores
            GUI.Label(new Rect(10, 60, 300, 20), "=== LEYENDA ===", style);

            style.normal.textColor = Color.green;
            GUI.Label(new Rect(10, 80, 300, 20), "Verde: Carril Recto", style);

            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(10, 100, 300, 20), "Amarillo: Intersección", style);

            style.normal.textColor = Color.cyan;
            GUI.Label(new Rect(10, 120, 300, 20), "Cyan: Carril de Giro", style);

            style.normal.textColor = Color.red;
            GUI.Label(new Rect(10, 140, 300, 20), "Rojo: Debe Ceder", style);
        }
    }
}
