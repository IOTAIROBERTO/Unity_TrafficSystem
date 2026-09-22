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
        /// <summary>
        /// One way out of this waypoint. Any number of these can hang off a single decision, which
        /// is what lets a lane fan out into three, four or more without chaining decisions.
        /// </summary>
        [System.Serializable]
        public class Branch
        {
            [Tooltip("Lane ID the vehicle adopts after taking this exit")]
            public string laneId = "";

            [Tooltip("Waypoints of this exit, starting with the one nearest this decision")]
            public Transform[] waypoints;

            [Tooltip("Relative likelihood of this exit. Weights are normalised against each other, so 2 is twice as likely as 1")]
            [Min(0f)]
            public float weight = 1f;
        }

        [Header("=== IDENTIFICACIÓN ===")]
        [Tooltip("IDs de carriles que PUEDEN usar este waypoint de decisión")]
        public string[] laneIdsPermitidos;

        [Header("=== SALIDAS ===")]
        [Tooltip("Salidas de este waypoint. Cualquier cantidad: 2, 3, 5...")]
        public Branch[] branches;

        [Header("=== CONFIGURACIÓN DE RUTAS (LEGACY) ===")]
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
        private Branch[] resolvedBranches;
        private float resolvedWeight;

        void Start()
        {
            ValidarConfiguracion();
            NormalizarProbabilidades();
            EnsureBranches();
        }

        /// <summary>
        /// Resolves the exits once. A decision authored before branches existed carries only the
        /// three legacy arrays, so those are folded into branches here rather than migrated on
        /// disk: the scene keeps working untouched and nothing has to be re-saved.
        /// </summary>
        void EnsureBranches()
        {
            if (resolvedBranches != null) return;

            var list = new System.Collections.Generic.List<Branch>();

            if (branches != null)
            {
                foreach (Branch b in branches)
                {
                    if (b != null && b.waypoints != null && b.waypoints.Length > 0) list.Add(b);
                }
            }

            if (list.Count == 0)
            {
                AddLegacy(list, rutaRecto, laneIdRecto, probabilidadRecto);
                AddLegacy(list, rutaDerecha, laneIdDerecha, probabilidadDerecha);
                AddLegacy(list, rutaIzquierda, laneIdIzquierda, probabilidadIzquierda);
            }

            resolvedBranches = list.ToArray();
            resolvedWeight = 0f;
            foreach (Branch b in resolvedBranches) resolvedWeight += Mathf.Max(0f, b.weight);
        }

        static void AddLegacy(System.Collections.Generic.List<Branch> list, Transform[] waypoints, string laneId, float weight)
        {
            if (waypoints == null || waypoints.Length == 0) return;
            list.Add(new Branch { laneId = laneId, waypoints = waypoints, weight = Mathf.Max(0f, weight) });
        }

        /// <summary>Exits actually available, legacy fields included. Empty only when nothing is wired.</summary>
        public Branch[] ResolvedBranches
        {
            get { EnsureBranches(); return resolvedBranches; }
        }

        /// <summary>Picks an exit index by weight, or -1 when there is nothing to pick.</summary>
        int PickWeightedIndex()
        {
            if (resolvedBranches.Length == 0) return -1;

            // Every weight at zero still has to go somewhere, so fall back to a flat draw.
            if (resolvedWeight <= 0f) return Random.Range(0, resolvedBranches.Length);

            float roll = Random.value * resolvedWeight;
            float cumulative = 0f;

            for (int i = 0; i < resolvedBranches.Length; i++)
            {
                cumulative += Mathf.Max(0f, resolvedBranches[i].weight);
                if (roll <= cumulative) return i;
            }

            return resolvedBranches.Length - 1;
        }

        void ValidarConfiguracion()
        {
            // Validar que al menos una ruta existe
            EnsureBranches();
            bool tieneRutas = resolvedBranches.Length > 0;

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
            if (!PuedeUsarEsteWaypoint(vehicleLaneId))
            {
                TrafficLog.Warn($"[WaypointDecision] Vehículo con laneId '{vehicleLaneId}' no puede usar waypoint {gameObject.name}");
                return null;
            }

            EnsureBranches();

            if (resolvedBranches.Length == 0)
            {
                TrafficLog.Error($"[WaypointDecision] ❌ {gameObject.name} - no hay salidas configuradas");
                return null;
            }

            // Draw one exit by weight, then walk the rest in order. Without the walk, a vehicle
            // that draws an exit whose first waypoint is out of range would be left with no route
            // at all, even though another exit was perfectly usable.
            int first = PickWeightedIndex();

            for (int offset = 0; offset < resolvedBranches.Length; offset++)
            {
                Branch branch = resolvedBranches[(first + offset) % resolvedBranches.Length];
                if (branch.waypoints == null || branch.waypoints.Length == 0) continue;
                if (!ValidarContinuidadEspacial(posicionActual, branch.waypoints[0])) continue;

                TrafficLog.Info($"[WaypointDecision] Vehículo {vehicleLaneId} → salida '{branch.laneId}'");
                return branch.waypoints;
            }

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
            EnsureBranches();

            foreach (Branch branch in resolvedBranches)
            {
                if (ReferenceEquals(branch.waypoints, rutaElegida)) return branch.laneId;
            }

            return ""; // Sin cambio
        }

        /// <summary>
        /// Verifica si tiene múltiples opciones
        /// </summary>
        public bool TieneMultiplesOpciones()
        {
            EnsureBranches();
            return resolvedBranches.Length > 1;
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
