using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Waypoint con opciones de ruta múltiple - VERSIÓN MEJORADA
    /// Sistema de validación por Lane ID para evitar conflictos en intersecciones
    /// Version 2.1 - Compatible con nuevo sistema de Lane IDs
    /// </summary>
    public class WaypointDecision : MonoBehaviour
    {
        [Header("=== IDENTIFICACIÓN ===")]
        [Tooltip("IDs de carriles que PUEDEN usar este waypoint de decisión")]
        public string[] laneIdsPermitidos;

        [Header("=== CONFIGURACIÓN DE RUTAS ===")]
        [Tooltip("Waypoints para continuar recto")]
        public Transform[] rutaRecto;

        [Tooltip("Lane ID asociado a la ruta recto (debe coincidir con algún carril)")]
        public string laneIdRecto = "";

        [Tooltip("Waypoints para girar a la derecha")]
        public Transform[] rutaDerecha;

        [Tooltip("Lane ID asociado a la ruta derecha")]
        public string laneIdDerecha = "";

        [Tooltip("Waypoints para girar a la izquierda")]
        public Transform[] rutaIzquierda;

        [Tooltip("Lane ID asociado a la ruta izquierda")]
        public string laneIdIzquierda = "";

        [Header("=== PROBABILIDADES DE GIRO ===")]
        [Range(0f, 1f)]
        [Tooltip("Probabilidad de seguir recto (0-1)")]
        public float probabilidadRecto = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("Probabilidad de girar derecha (0-1)")]
        public float probabilidadDerecha = 0.25f;

        [Range(0f, 1f)]
        [Tooltip("Probabilidad de girar izquierda (0-1)")]
        public float probabilidadIzquierda = 0.25f;

        [Header("=== CONFIGURACIÓN ADICIONAL ===")]
        [Tooltip("¿Requiere detenerse antes de decidir?")]
        public bool requiereDetenerse = false;

        [Tooltip("¿Es intersección con semáforo?")]
        public bool esInterseccionConSemaforo = false;

        [Header("=== VALIDACIÓN DE CONTINUIDAD ===")]
        [Tooltip("Distancia máxima permitida al siguiente waypoint (evita saltos incorrectos)")]
        [Range(5f, 50f)]
        public float distanciaMaximaValidacion = 20f;

        // Cache
        private float totalProbabilidad;

        void Start()
        {
            ValidarConfiguracion();
            NormalizarProbabilidades();
        }

        void ValidarConfiguracion()
        {
            // Validar que al menos una ruta existe
            bool tieneRutas = (rutaRecto != null && rutaRecto.Length > 0) || 
                              (rutaDerecha != null && rutaDerecha.Length > 0) || 
                              (rutaIzquierda != null && rutaIzquierda.Length > 0);

            if (!tieneRutas)
            {
                TrafficLog.Error($"[WaypointDecision] ❌ {gameObject.name} - ¡NO HAY RUTAS CONFIGURADAS!");
            }

            // Advertir si no hay lane IDs
            if (laneIdsPermitidos == null || laneIdsPermitidos.Length == 0)
            {
                TrafficLog.Warn($"[WaypointDecision] ⚠️ {gameObject.name} - No tiene laneIdsPermitidos configurados. Permitirá TODOS los vehículos.");
            }
        }

        void NormalizarProbabilidades()
        {
            totalProbabilidad = probabilidadRecto + probabilidadDerecha + probabilidadIzquierda;

            if (Mathf.Abs(totalProbabilidad - 1f) > 0.01f)
            {
                TrafficLog.Warn($"[WaypointDecision] {gameObject.name} - Probabilidades no suman 1.0 ({totalProbabilidad:F2}). Normalizando...");

                if (totalProbabilidad > 0)
                {
                    probabilidadRecto /= totalProbabilidad;
                    probabilidadDerecha /= totalProbabilidad;
                    probabilidadIzquierda /= totalProbabilidad;
                }
            }
        }

        /// <summary>
        /// Verifica si un vehículo con este laneId puede usar este waypoint de decisión
        /// </summary>
        public bool PuedeUsarEsteWaypoint(string vehicleLaneId)
        {
            // Si no hay restricciones, permitir todos
            if (laneIdsPermitidos == null || laneIdsPermitidos.Length == 0)
                return true;

            // Verificar si el lane ID del vehículo está en la lista permitida
            foreach (string laneId in laneIdsPermitidos)
            {
                if (laneId == vehicleLaneId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// SOBRECARGA: Obtiene ruta sin validación de Lane ID (compatibilidad con código antiguo)
        /// </summary>
        public Transform[] GetRutaAleatoria()
        {
            float random = Random.value;
            float acumulado = 0f;

            acumulado += probabilidadRecto;
            if (random <= acumulado && rutaRecto != null && rutaRecto.Length > 0)
            {
                return rutaRecto;
            }

            acumulado += probabilidadDerecha;
            if (random <= acumulado && rutaDerecha != null && rutaDerecha.Length > 0)
            {
                return rutaDerecha;
            }

            if (rutaIzquierda != null && rutaIzquierda.Length > 0)
            {
                return rutaIzquierda;
            }

            // Fallback
            if (rutaRecto != null && rutaRecto.Length > 0) return rutaRecto;
            if (rutaDerecha != null && rutaDerecha.Length > 0) return rutaDerecha;

            TrafficLog.Error($"[WaypointDecision] {gameObject.name} - No se pudo obtener ninguna ruta");
            return null;
        }

        /// <summary>
        /// Obtiene ruta aleatoria VALIDADA con continuidad espacial y Lane ID
        /// </summary>
        public Transform[] GetRutaAleatoria(string vehicleLaneId, Vector3 posicionActual)
        {
            // Verificar permiso
            if (!PuedeUsarEsteWaypoint(vehicleLaneId))
            {
                TrafficLog.Warn($"[WaypointDecision] Vehículo con laneId '{vehicleLaneId}' no puede usar waypoint {gameObject.name}");
                return null;
            }

            // Generar número aleatorio
            float random = Random.value;
            float acumulado = 0f;

            // Intentar recto
            acumulado += probabilidadRecto;
            if (random <= acumulado && rutaRecto != null && rutaRecto.Length > 0)
            {
                if (ValidarContinuidadEspacial(posicionActual, rutaRecto[0]))
                {
                    TrafficLog.Info($"[WaypointDecision] Vehículo {vehicleLaneId} → RECTO (nuevo lane: {laneIdRecto})");
                    return rutaRecto;
                }
            }

            // Intentar derecha
            acumulado += probabilidadDerecha;
            if (random <= acumulado && rutaDerecha != null && rutaDerecha.Length > 0)
            {
                if (ValidarContinuidadEspacial(posicionActual, rutaDerecha[0]))
                {
                    TrafficLog.Info($"[WaypointDecision] Vehículo {vehicleLaneId} → DERECHA (nuevo lane: {laneIdDerecha})");
                    return rutaDerecha;
                }
            }

            // Intentar izquierda
            if (rutaIzquierda != null && rutaIzquierda.Length > 0)
            {
                if (ValidarContinuidadEspacial(posicionActual, rutaIzquierda[0]))
                {
                    TrafficLog.Info($"[WaypointDecision] Vehículo {vehicleLaneId} → IZQUIERDA (nuevo lane: {laneIdIzquierda})");
                    return rutaIzquierda;
                }
            }

            // Fallback: devolver primera ruta válida disponible
            if (rutaRecto != null && rutaRecto.Length > 0 && ValidarContinuidadEspacial(posicionActual, rutaRecto[0]))
                return rutaRecto;
            if (rutaDerecha != null && rutaDerecha.Length > 0 && ValidarContinuidadEspacial(posicionActual, rutaDerecha[0]))
                return rutaDerecha;
            if (rutaIzquierda != null && rutaIzquierda.Length > 0 && ValidarContinuidadEspacial(posicionActual, rutaIzquierda[0]))
                return rutaIzquierda;

            TrafficLog.Error($"[WaypointDecision] ❌ {gameObject.name} - No se pudo obtener ninguna ruta válida para vehículo {vehicleLaneId}");
            return null;
        }

        /// <summary>
        /// Valida que el siguiente waypoint esté dentro de un rango lógico
        /// Evita saltos a waypoints lejanos o en dirección incorrecta
        /// </summary>
        private bool ValidarContinuidadEspacial(Vector3 posicionActual, Transform siguienteWaypoint)
        {
            if (siguienteWaypoint == null) return false;

            float distancia = Vector3.Distance(posicionActual, siguienteWaypoint.position);

            // Si está demasiado lejos, rechazar
            if (distancia > distanciaMaximaValidacion)
            {
                TrafficLog.Warn($"[WaypointDecision] Waypoint {siguienteWaypoint.name} demasiado lejos ({distancia:F1}m > {distanciaMaximaValidacion}m)");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Obtiene el nuevo Lane ID después de tomar una decisión
        /// </summary>
        public string GetNuevoLaneId(Transform[] rutaElegida)
        {
            if (rutaElegida == rutaRecto) return laneIdRecto;
            if (rutaElegida == rutaDerecha) return laneIdDerecha;
            if (rutaElegida == rutaIzquierda) return laneIdIzquierda;

            return ""; // Sin cambio
        }

        /// <summary>
        /// Verifica si tiene múltiples opciones
        /// </summary>
        public bool TieneMultiplesOpciones()
        {
            int opciones = 0;
            if (rutaRecto != null && rutaRecto.Length > 0) opciones++;
            if (rutaDerecha != null && rutaDerecha.Length > 0) opciones++;
            if (rutaIzquierda != null && rutaIzquierda.Length > 0) opciones++;
            return opciones > 1;
        }

        // ============ VISUALIZACIÓN DEBUG ============

        void OnDrawGizmos()
        {
            Vector3 pos = transform.position;

            // Dibujar conexiones con colores distintos
            if (rutaRecto != null && rutaRecto.Length > 0)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(pos, rutaRecto[0].position);
                Gizmos.DrawSphere(pos + (rutaRecto[0].position - pos).normalized * 1f, 0.3f);
            }

            if (rutaDerecha != null && rutaDerecha.Length > 0)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(pos, rutaDerecha[0].position);
                Gizmos.DrawSphere(pos + (rutaDerecha[0].position - pos).normalized * 1f, 0.3f);
            }

            if (rutaIzquierda != null && rutaIzquierda.Length > 0)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(pos, rutaIzquierda[0].position);
                Gizmos.DrawSphere(pos + (rutaIzquierda[0].position - pos).normalized * 1f, 0.3f);
            }

            // Marcador de decisión
            Gizmos.color = new Color(1f, 0f, 1f, 0.7f);
            Gizmos.DrawWireSphere(pos, 2f);

            // Radio de validación
            Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
            Gizmos.DrawWireSphere(pos, distanciaMaximaValidacion);
        }

        void OnDrawGizmosSelected()
        {
    #if UNITY_EDITOR
            Vector3 labelPos = transform.position + Vector3.up * 3f;

            string laneInfo = "SIN RESTRICCIÓN";
            if (laneIdsPermitidos != null && laneIdsPermitidos.Length > 0)
            {
                laneInfo = string.Join(", ", laneIdsPermitidos);
            }

            UnityEditor.Handles.Label(labelPos, 
                $"WAYPOINT DE DECISIÓN\n" +
                $"━━━━━━━━━━━━━━━━━━\n" +
                $"Lanes permitidos: {laneInfo}\n" +
                $"\n" +
                $"Recto: {probabilidadRecto:P0} → Lane '{laneIdRecto}'\n" +
                $"Derecha: {probabilidadDerecha:P0} → Lane '{laneIdDerecha}'\n" +
                $"Izquierda: {probabilidadIzquierda:P0} → Lane '{laneIdIzquierda}'\n" +
                $"\n" +
                $"Validación: {distanciaMaximaValidacion}m");
    #endif
        }
    }
}
