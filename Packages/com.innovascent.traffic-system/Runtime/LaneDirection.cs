using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Define la dirección de un carril o zona de tráfico
    /// Coloca este componente en waypoints o zonas para identificar flujo de tráfico
    /// </summary>
    public class LaneDirection : MonoBehaviour
    {
        [Header("Identificación del Carril")]
        [Tooltip("ID único del carril (ej: 'norte_a_sur', 'este_a_oeste')")]
        public string laneId = "lane_1";

        [Tooltip("Dirección principal del flujo de tráfico en este punto")]
        public Vector3 flowDirection = Vector3.forward;

        [Header("Tipo de Zona")]
        [Tooltip("Tipo de zona de tráfico")]
        public ZoneType zoneType = ZoneType.StraightLane;

        [Header("Intersección (si aplica)")]
        [Tooltip("Si es intersección, lista de carriles compatibles que pueden cruzar")]
        public string[] compatibleLaneIds;

        [Tooltip("¿Esta zona requiere ceder el paso a otros carriles?")]
        public bool requiresYield = false;

        [Tooltip("Prioridad en intersección (1=mayor prioridad, 10=menor prioridad)")]
        [Range(1, 10)]
        public int priority = 5;

        public enum ZoneType
        {
            StraightLane,      // Carril recto - solo detectar vehículos en mismo sentido
            OpposingLane,      // Carril contrario - ignorar detección
            Intersection,      // Intersección - detección compleja con prioridades
            TurnLane          // Carril de giro - requiere ceder paso
        }

        // Cache para optimización
        private Vector3 normalizedFlowDirection;

        void Start()
        {
            // Normalizar dirección de flujo
            normalizedFlowDirection = flowDirection.normalized;

            // Auto-calcular dirección desde transform si no está configurado
            if (flowDirection == Vector3.zero || flowDirection == Vector3.forward)
            {
                normalizedFlowDirection = transform.forward;
            }

            TrafficLog.Info($"[LaneDirection] {gameObject.name} - Lane: {laneId}, Type: {zoneType}, Priority: {priority}");
        }

        /// <summary>
        /// Verifica si un vehículo viene en sentido contrario
        /// </summary>
        public bool IsOpposingDirection(Vector3 vehicleForward)
        {
            // Producto punto negativo = sentido contrario
            float dot = Vector3.Dot(normalizedFlowDirection, vehicleForward.normalized);
            return dot < -0.3f; // Umbral para considerar sentido contrario
        }

        /// <summary>
        /// Verifica si un vehículo viene en el mismo sentido
        /// </summary>
        public bool IsSameDirection(Vector3 vehicleForward)
        {
            float dot = Vector3.Dot(normalizedFlowDirection, vehicleForward.normalized);
            return dot > 0.3f; // Mismo sentido
        }

        /// <summary>
        /// Verifica si dos carriles son compatibles (pueden estar cerca sin chocar)
        /// </summary>
        public bool IsCompatibleWith(string otherLaneId)
        {
            if (compatibleLaneIds == null || compatibleLaneIds.Length == 0)
                return false;

            foreach (string compatibleId in compatibleLaneIds)
            {
                if (compatibleId == otherLaneId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Obtiene la dirección de flujo normalizada
        /// </summary>
        public Vector3 GetFlowDirection()
        {
            return normalizedFlowDirection;
        }

        // Visualización en el editor
        void OnDrawGizmos()
        {
            Vector3 pos = transform.position + Vector3.up * 0.5f;
            Vector3 dir = (Application.isPlaying ? normalizedFlowDirection : flowDirection.normalized);

            // Color según tipo de zona
            Color zoneColor = zoneType switch
            {
                ZoneType.StraightLane => Color.green,
                ZoneType.OpposingLane => Color.red,
                ZoneType.Intersection => Color.yellow,
                ZoneType.TurnLane => Color.cyan,
                _ => Color.white
            };

            Gizmos.color = zoneColor;

            // Flecha de dirección
            Gizmos.DrawRay(pos, dir * 3f);

            // Punta de flecha
            Vector3 arrowTip = pos + dir * 3f;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * 0.5f;
            Gizmos.DrawLine(arrowTip, arrowTip - dir * 0.5f + right);
            Gizmos.DrawLine(arrowTip, arrowTip - dir * 0.5f - right);

            // Esfera en la base
            Gizmos.DrawWireSphere(pos, 0.5f);
        }

        void OnDrawGizmosSelected()
        {
    #if UNITY_EDITOR
            Vector3 labelPos = transform.position + Vector3.up * 3f;

            string compatibleInfo = "";
            if (compatibleLaneIds != null && compatibleLaneIds.Length > 0)
            {
                compatibleInfo = $"\nCompatible: {string.Join(", ", compatibleLaneIds)}";
            }

            UnityEditor.Handles.Label(labelPos, 
                $"LANE: {laneId}\n" +
                $"Type: {zoneType}\n" +
                $"Priority: {priority}\n" +
                $"Yield: {requiresYield}" +
                compatibleInfo);
    #endif
        }
    }
}
