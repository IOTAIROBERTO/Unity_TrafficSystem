// ==================== FOUR WAY INTERSECTION CONTROLLER ====================
using UnityEngine;
using System.Collections;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Controlador de cruce de 4 vías con secuencia realista de semáforos
    /// Maneja parpadeo verde, transiciones seguras y ciclos por turnos
    /// </summary>
    public class FourWayIntersectionController : MonoBehaviour
    {
        [Header("=== SEMÁFOROS DEL CRUCE ===")]
        [Tooltip("Semáforo 1: Oeste a Este")]
        public TrafficLightController semaforoWestEast;

        [Tooltip("Semáforo 2: Este a Oeste")]
        public TrafficLightController semaforoEastWest;

        [Tooltip("Semáforo 3: Sur a Norte")]
        public TrafficLightController semaforoSouthNorth;

        [Tooltip("Semáforo 4: Norte a Sur")]
        public TrafficLightController semaforoNorthSouth;

        [Header("=== TIEMPOS DEL CICLO ===")]
        [Tooltip("Tiempo en verde antes de parpadear (segundos)")]
        [Range(10f, 60f)]
        public float tiempoVerdeSolido = 20f;

        [Tooltip("Duración del parpadeo verde (segundos)")]
        [Range(3f, 10f)]
        public float tiempoParpadeoVerde = 5f;

        [Tooltip("Duración de la luz amarilla (segundos)")]
        [Range(2f, 5f)]
        public float tiempoAmarillo = 3f;

        [Tooltip("Tiempo de seguridad con todo en rojo (segundos)")]
        [Range(0.5f, 3f)]
        public float tiempoSeguridadRojo = 2f;

        [Header("=== CONFIGURACIÓN DE GRUPOS ===")]
        [Tooltip("¿Agrupar semáforos opuestos? (N-S juntos, E-O juntos)")]
        public bool agruparSemaforosOpuestos = true;

        [Tooltip("Orden de activación (0=W-E, 1=E-W, 2=S-N, 3=N-S)")]
        public int[] ordenActivacion = new int[] { 0, 2, 1, 3 }; // W-E, S-N, E-W, N-S

        [Header("=== ESTADO ACTUAL (INFO) ===")]
        [SerializeField] private int faseActual = 0;
        [SerializeField] private string estadoActual = "Inicializando...";
        [SerializeField] private float tiempoEnFase = 0f;

        // Estado interno
        private enum EstadoCiclo
        {
            VerdeSolido,
            VerdeParpadeando,
            Amarillo,
            TodoRojo
        }

        private EstadoCiclo estadoCiclo = EstadoCiclo.VerdeSolido;
        private TrafficLightController[] todosLosSemaforos;
        private bool parpadeoActivo = false;
        private float timerParpadeo = 0f;
        private bool luzParpadeoEncendida = true;

        void Start()
        {
            // Validar configuración
            if (semaforoWestEast == null || semaforoEastWest == null ||
                semaforoSouthNorth == null || semaforoNorthSouth == null)
            {
                TrafficLog.Error("[FourWayController] ⚠️ FALTAN SEMÁFOROS! Asignar todos los semáforos en el Inspector");
                enabled = false;
                return;
            }

            // Crear array de semáforos
            todosLosSemaforos = new TrafficLightController[]
            {
                semaforoWestEast,    // 0
                semaforoEastWest,    // 1
                semaforoSouthNorth,  // 2
                semaforoNorthSouth   // 3
            };

            // Validar orden de activación
            if (ordenActivacion == null || ordenActivacion.Length != 4)
            {
                TrafficLog.Warn("[FourWayController] Orden de activación inválido, usando default");
                ordenActivacion = new int[] { 0, 2, 1, 3 };
            }

            // ✅ ACTIVAR CONTROL EXTERNO EN TODOS LOS SEMÁFOROS
            foreach (var sem in todosLosSemaforos)
            {
                // A light with no waypoint controls nothing, so driving it only produces log noise
                // and leaves a dead entry in the cycle.
                if (sem == null) continue;

                if (sem.waypointControlado == null)
                {
                    TrafficLog.Warn($"[FourWayController] '{sem.name}' no tiene waypoint asignado, se omite del ciclo");
                    continue;
                }

                sem.SetControlExterno(true);
            }

            TrafficLog.Info($"[FourWayController] ✅ Inicializado con {todosLosSemaforos.Length} semáforos");
            TrafficLog.Info($"[FourWayController] Orden: {string.Join(" → ", ordenActivacion)}");
            TrafficLog.Info($"[FourWayController] Agrupación opuestos: {agruparSemaforosOpuestos}");

            // Iniciar el ciclo
            StartCoroutine(CicloSemaforico());
        }

        /// <summary>
        /// Ciclo principal de control de semáforos
        /// </summary>
        private IEnumerator CicloSemaforico()
        {
            // Comenzar con todo en rojo
            EstablecerTodosEnRojo();
            yield return new WaitForSeconds(1f);

            // Ciclo infinito
            while (true)
            {
                // Iterar por cada fase según el orden configurado
                for (int i = 0; i < ordenActivacion.Length; i++)
                {
                    faseActual = i;
                    int indiceSemaforo = ordenActivacion[i];

                    // Determinar qué semáforos activar
                    TrafficLightController[] semaforosActivos = ObtenerSemaforosParaFase(indiceSemaforo);

                    TrafficLog.Info($"[FourWayController] 🔄 FASE {faseActual + 1}/4 - Activando: {string.Join(", ", System.Array.ConvertAll(semaforosActivos, s => s.gameObject.name))}");

                    // ═══════════════════════════════════════════════════════════
                    // FASE 1: VERDE SÓLIDO
                    // - Semáforos activos: VERDE constante
                    // - Semáforos inactivos: ROJO constante
                    // ═══════════════════════════════════════════════════════════
                    estadoCiclo = EstadoCiclo.VerdeSolido;
                    estadoActual = $"Fase {faseActual + 1} - Verde Sólido";
                    tiempoEnFase = 0f;

                    // PRIMERO: Todos en rojo
                    EstablecerTodosEnRojo();
                    // SEGUNDO: Solo los activos en verde
                    foreach (var sem in semaforosActivos)
                    {
                        sem.ForzarEstadoEspecifico(2); // Verde
                    }
                    // RESULTADO: Activos=VERDE, Inactivos=ROJO

                    // Esperar tiempo verde sólido
                    float tiempoEsperado = tiempoVerdeSolido;
                    while (tiempoEnFase < tiempoEsperado)
                    {
                        tiempoEnFase += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;
                        yield return null;
                    }

                    TrafficLog.Info($"[FourWayController] 💚 Verde sólido completado");

                    // ═══════════════════════════════════════════════════════════
                    // FASE 2: VERDE PARPADEANDO
                    // - Semáforos activos: VERDE parpadeando (verde <-> apagado)
                    // - Semáforos inactivos: ROJO constante (sin parpadear)
                    // ═══════════════════════════════════════════════════════════
                    estadoCiclo = EstadoCiclo.VerdeParpadeando;
                    estadoActual = $"Fase {faseActual + 1} - Verde Parpadeando";
                    tiempoEnFase = 0f;
                    parpadeoActivo = true;
                    timerParpadeo = 0f;
                    luzParpadeoEncendida = true;

                    // Parpadeo durante el tiempo configurado
                    while (tiempoEnFase < tiempoParpadeoVerde)
                    {
                        tiempoEnFase += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;
                        timerParpadeo += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;

                        // Parpadear cada 0.5 segundos
                        if (timerParpadeo >= 0.5f)
                        {
                            timerParpadeo = 0f;
                            luzParpadeoEncendida = !luzParpadeoEncendida;

                            // IMPORTANTE: Solo los semáforos activos parpadean
                            // Los demás PERMANECEN en rojo constante
                            foreach (var sem in semaforosActivos)
                            {
                                if (luzParpadeoEncendida)
                                {
                                    sem.ForzarEstadoEspecifico(2); // Verde
                                }
                                else
                                {
                                    sem.ForzarEstadoEspecifico(3); // Apagado (todas las luces off)
                                }
                            }

                            // CRÍTICO: Asegurar que los NO activos permanezcan en rojo
                            foreach (var sem in todosLosSemaforos)
                            {
                                bool esActivo = System.Array.Exists(semaforosActivos, s => s == sem);
                                if (!esActivo)
                                {
                                    sem.ForzarEstadoEspecifico(0); // Rojo constante
                                }
                            }
                        }

                        yield return null;
                    }

                    parpadeoActivo = false;
                    TrafficLog.Info($"[FourWayController] 💚⚡ Parpadeo verde completado");

                    // ═══════════════════════════════════════════════════════════
                    // FASE 3: AMARILLO
                    // - Semáforos activos: AMARILLO constante
                    // - Semáforos inactivos: ROJO constante
                    // ═══════════════════════════════════════════════════════════
                    estadoCiclo = EstadoCiclo.Amarillo;
                    estadoActual = $"Fase {faseActual + 1} - Amarillo";
                    tiempoEnFase = 0f;

                    // PRIMERO: Todos en rojo
                    EstablecerTodosEnRojo();
                    // SEGUNDO: Solo los activos en amarillo
                    foreach (var sem in semaforosActivos)
                    {
                        sem.ForzarEstadoEspecifico(1); // Amarillo
                    }
                    // RESULTADO: Activos=AMARILLO, Inactivos=ROJO

                    // Esperar tiempo amarillo
                    while (tiempoEnFase < tiempoAmarillo)
                    {
                        tiempoEnFase += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;
                        yield return null;
                    }

                    TrafficLog.Info($"[FourWayController] 🟡 Amarillo completado");

                    // ═══════════════════════════════════════════════════════════
                    // FASE 4: TODO EN ROJO (SEGURIDAD)
                    // - TODOS los semáforos: ROJO constante
                    // - Tiempo de despeje antes del siguiente semáforo
                    // ═══════════════════════════════════════════════════════════
                    estadoCiclo = EstadoCiclo.TodoRojo;
                    estadoActual = "Todo en Rojo (Seguridad)";
                    tiempoEnFase = 0f;

                    EstablecerTodosEnRojo(); // TODOS en rojo

                    // Esperar tiempo de seguridad
                    while (tiempoEnFase < tiempoSeguridadRojo)
                    {
                        tiempoEnFase += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;
                        yield return null;
                    }

                    TrafficLog.Info($"[FourWayController] 🔴 Tiempo de seguridad completado");
                }

                // Reiniciar ciclo
                TrafficLog.Info($"[FourWayController] 🔄 Ciclo completo, reiniciando...");
            }
        }

        /// <summary>
        /// Obtiene los semáforos que deben estar activos en una fase
        /// </summary>
        private TrafficLightController[] ObtenerSemaforosParaFase(int indiceSemaforo)
        {
            if (!agruparSemaforosOpuestos)
            {
                // Modo individual: solo un semáforo a la vez
                return new TrafficLightController[] { todosLosSemaforos[indiceSemaforo] };
            }
            else
            {
                // Modo agrupado: semáforos opuestos juntos
                // 0 (W-E) con 1 (E-W), 2 (S-N) con 3 (N-S)
                switch (indiceSemaforo)
                {
                    case 0: // W-E + E-W
                        return new TrafficLightController[] { semaforoWestEast, semaforoEastWest };
                    case 1: // E-W + W-E (igual que 0)
                        return new TrafficLightController[] { semaforoWestEast, semaforoEastWest };
                    case 2: // S-N + N-S
                        return new TrafficLightController[] { semaforoSouthNorth, semaforoNorthSouth };
                    case 3: // N-S + S-N (igual que 2)
                        return new TrafficLightController[] { semaforoSouthNorth, semaforoNorthSouth };
                    default:
                        return new TrafficLightController[] { todosLosSemaforos[indiceSemaforo] };
                }
            }
        }

        /// <summary>
        /// Establece todos los semáforos en rojo
        /// </summary>
        private void EstablecerTodosEnRojo()
        {
            foreach (var sem in todosLosSemaforos)
            {
                if (sem != null)
                {
                    sem.ForzarEstadoEspecifico(0); // Rojo
                }
            }
        }

        /// <summary>
        /// Visualización en el editor
        /// </summary>
        void OnDrawGizmos()
        {
            if (todosLosSemaforos == null || todosLosSemaforos.Length == 0) return;

            Vector3 centerPos = transform.position;

            // Dibujar conexiones a todos los semáforos
            for (int i = 0; i < todosLosSemaforos.Length; i++)
            {
                if (todosLosSemaforos[i] != null)
                {
                    // Color según estado
                    Color lineColor = Color.gray;

                    if (Application.isPlaying)
                    {
                        bool isActive = false;

                        if (agruparSemaforosOpuestos)
                        {
                            int fase = ordenActivacion[faseActual];
                            var activeSems = ObtenerSemaforosParaFase(fase);
                            isActive = System.Array.Exists(activeSems, s => s == todosLosSemaforos[i]);
                        }
                        else
                        {
                            isActive = (ordenActivacion[faseActual] == i);
                        }

                        if (isActive)
                        {
                            lineColor = estadoCiclo switch
                            {
                                EstadoCiclo.VerdeSolido => Color.green,
                                EstadoCiclo.VerdeParpadeando => new Color(0f, 1f, 0f, parpadeoActivo && luzParpadeoEncendida ? 1f : 0.3f),
                                EstadoCiclo.Amarillo => Color.yellow,
                                EstadoCiclo.TodoRojo => Color.red,
                                _ => Color.gray
                            };
                        }
                        else
                        {
                            lineColor = Color.red;
                        }
                    }

                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(centerPos, todosLosSemaforos[i].transform.position);
                }
            }

            // Marcador central
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(centerPos, 2f);
            Gizmos.DrawWireCube(centerPos, Vector3.one * 4f);
        }

        void OnDrawGizmosSelected()
        {
    #if UNITY_EDITOR
            Vector3 labelPos = transform.position + Vector3.up * 5f;

            string info = "CONTROLADOR 4 VÍAS\n";
            info += $"═══════════════════\n";

            if (Application.isPlaying)
            {
                info += $"Fase: {faseActual + 1}/4\n";
                info += $"Estado: {estadoActual}\n";
                info += $"Tiempo: {tiempoEnFase:F1}s\n";
                info += $"Agrupación: {(agruparSemaforosOpuestos ? "OPUESTOS" : "INDIVIDUAL")}\n";
            }
            else
            {
                info += "Tiempos configurados:\n";
                info += $"Verde: {tiempoVerdeSolido}s\n";
                info += $"Parpadeo: {tiempoParpadeoVerde}s\n";
                info += $"Amarillo: {tiempoAmarillo}s\n";
                info += $"Seguridad: {tiempoSeguridadRojo}s\n";
            }

            UnityEditor.Handles.Label(labelPos, info);
    #endif
        }

        /// <summary>
        /// Método público para forzar cambio de fase (útil para debugging)
        /// </summary>
        public void ForzarSiguienteFase()
        {
            if (!Application.isPlaying) return;

            StopAllCoroutines();
            faseActual = (faseActual + 1) % ordenActivacion.Length;
            StartCoroutine(CicloSemaforico());

            TrafficLog.Info($"[FourWayController] 🔧 Fase forzada a: {faseActual + 1}");
        }

        void OnDestroy()
        {
            // Liberar control externo al destruir el controlador
            if (todosLosSemaforos != null)
            {
                foreach (var sem in todosLosSemaforos)
                {
                    if (sem != null)
                    {
                        sem.SetControlExterno(false);
                    }
                }
            }
        }
    }
}
