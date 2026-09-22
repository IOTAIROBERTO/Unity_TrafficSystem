// ==================== TRAFFIC LIGHT CONTROLLER (MODIFICADO CON PARPADEO) ====================
using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Controlador de semáforo que se conecta a un waypoint específico
    /// VERSIÓN MEJORADA: Con soporte para forzar estados específicos + estado APAGADO para parpadeo
    /// </summary>
    public class TrafficLightController : MonoBehaviour
    {
        [Header("Configuración del Semáforo")]
        [Tooltip("El waypoint que este semáforo controla")]
        public Transform waypointControlado;

        [Tooltip("Radio de detección para vehículos cercanos")]
        [Range(5f, 20f)]
        public float radioDeteccion = 10f;

        [Header("Luces Visuales (Opcional)")]
        [Tooltip("Renderer de la luz roja")]
        public GameObject luzRoja;

        [Tooltip("Renderer de la luz amarilla")]
        public GameObject luzAmarilla;

        [Tooltip("Renderer de la luz verde")]
        public GameObject luzVerde;

        [Header("Estado Inicial")]
        public bool empezarEnVerde = false;

        [Tooltip("Seconds to advance this light's cycle at startup. Without it every light on a " +
                 "site changes on the same frame, so all traffic stops at once and then all moves " +
                 "at once. Give neighbouring stops different offsets to spread the waiting out.")]
        public float desfaseInicial = 0f;

        // Estado interno - ✅ AGREGADO ESTADO APAGADO
        private enum EstadoSemaforo { Rojo, Amarillo, Verde, Apagado }
        private EstadoSemaforo estadoActual;
        private float timer = 0f;

        // ✅ NUEVO: Flag para control externo
        private bool controlExterno = false;

        void Start()
        {
            // Estado inicial
            estadoActual = empezarEnVerde ? EstadoSemaforo.Verde : EstadoSemaforo.Rojo;
            timer = 0f;
            if (desfaseInicial > 0f) AplicarDesfase(desfaseInicial);

            // Registrar con el TrafficManager
            if (TrafficManager.Instance != null && waypointControlado != null)
            {
                TrafficManager.Instance.RegisterTrafficLight(waypointControlado, this);
                TrafficLog.Info($"[TrafficLight] Semáforo registrado en waypoint {waypointControlado.name}");
            }
            else
            {
                TrafficLog.Warn($"[TrafficLight] No se pudo registrar semáforo. TrafficManager: {TrafficManager.Instance != null}, Waypoint: {waypointControlado != null}");
            }

            ActualizarVisuales();
        }

        /// <summary>
        /// Places the light this many seconds into the red-green-amber cycle. The offset has to be
        /// resolved into a phase and a time within it: feeding it straight to the timer only works
        /// while it is shorter than the phase the light happens to start in, and any larger value
        /// ends that phase on the first frame, which drops every light back onto the same schedule.
        /// </summary>
        void AplicarDesfase(float segundos)
        {
            float rojo = 8f, verde = 10f;

            if (TrafficManager.Instance != null && TrafficManager.Instance.config != null)
            {
                rojo = TrafficManager.Instance.config.tiempoRojoSemaforo;
                verde = TrafficManager.Instance.config.tiempoVerdeSemaforo;
            }

            float dentroDeLaFase;
            Estado fase = FaseEnElCiclo(segundos, rojo, verde, out dentroDeLaFase);

            estadoActual = fase == Estado.Verde ? EstadoSemaforo.Verde
                         : fase == Estado.Amarillo ? EstadoSemaforo.Amarillo
                         : EstadoSemaforo.Rojo;
            timer = dentroDeLaFase;
        }

        /// <summary>
        /// Which phase a light sits in this many seconds into the cycle, and how far into that
        /// phase it is. Kept separate from the component so the mapping can be checked on its own.
        /// </summary>
        public static Estado FaseEnElCiclo(float segundos, float rojo, float verde, out float dentroDeLaFase)
        {
            const float ambar = 2f;
            float ciclo = rojo + verde + ambar;

            if (ciclo <= 0f)
            {
                dentroDeLaFase = 0f;
                return Estado.Rojo;
            }

            float t = Mathf.Repeat(segundos, ciclo);

            if (t < rojo)
            {
                dentroDeLaFase = t;
                return Estado.Rojo;
            }

            if (t < rojo + verde)
            {
                dentroDeLaFase = t - rojo;
                return Estado.Verde;
            }

            dentroDeLaFase = t - rojo - verde;
            return Estado.Amarillo;
        }

        void Update()
        {
            // Si está bajo control externo, no ejecutar lógica automática
            if (controlExterno) return;

            // Si el sistema de semáforos está desactivado, siempre verde
            if (TrafficManager.Instance == null || !TrafficManager.Instance.config.useSemaforos)
            {
                estadoActual = EstadoSemaforo.Verde;
                ActualizarVisuales();
                return;
            }

            // Avanzar timer
            timer += Time.deltaTime * TrafficManager.Instance.config.simulationSpeed;

            // Máquina de estados del semáforo
            switch (estadoActual)
            {
                case EstadoSemaforo.Verde:
                    if (timer >= TrafficManager.Instance.config.tiempoVerdeSemaforo)
                    {
                        CambiarEstado(EstadoSemaforo.Amarillo);
                    }
                    break;

                case EstadoSemaforo.Amarillo:
                    if (timer >= 2f) // Amarillo siempre dura 2 segundos
                    {
                        CambiarEstado(EstadoSemaforo.Rojo);
                    }
                    break;

                case EstadoSemaforo.Rojo:
                    if (timer >= TrafficManager.Instance.config.tiempoRojoSemaforo)
                    {
                        CambiarEstado(EstadoSemaforo.Verde);
                    }
                    break;
            }

            ActualizarVisuales();
        }

        void CambiarEstado(EstadoSemaforo nuevoEstado)
        {
            estadoActual = nuevoEstado;
            timer = 0f;
            TrafficLog.Info($"[TrafficLight] {WaypointName} cambió a {estadoActual}");
        }

        // ✅ CORREGIDO: Ahora soporta estado APAGADO para parpadeo
        void ActualizarVisuales()
        {
            switch (estadoActual)
            {
                case EstadoSemaforo.Rojo:
                    if (luzRoja != null) luzRoja.SetActive(true);
                    if (luzAmarilla != null) luzAmarilla.SetActive(false);
                    if (luzVerde != null) luzVerde.SetActive(false);
                    break;

                case EstadoSemaforo.Amarillo:
                    if (luzRoja != null) luzRoja.SetActive(false);
                    if (luzAmarilla != null) luzAmarilla.SetActive(true);
                    if (luzVerde != null) luzVerde.SetActive(false);
                    break;

                case EstadoSemaforo.Verde:
                    if (luzRoja != null) luzRoja.SetActive(false);
                    if (luzAmarilla != null) luzAmarilla.SetActive(false);
                    if (luzVerde != null) luzVerde.SetActive(true);
                    break;

                case EstadoSemaforo.Apagado:
                    // ✅ APAGAR TODAS LAS LUCES (para efecto de parpadeo)
                    if (luzRoja != null) luzRoja.SetActive(false);
                    if (luzAmarilla != null) luzAmarilla.SetActive(false);
                    if (luzVerde != null) luzVerde.SetActive(false);
                    break;
            }
        }

        /// <summary>
        /// Devuelve true si los vehículos pueden pasar este semáforo
        /// </summary>
        public bool PuedeAvanzar()
        {
            // Si no hay sistema de semáforos, siempre puede avanzar
            if (TrafficManager.Instance == null || !TrafficManager.Instance.config.useSemaforos)
            {
                return true;
            }

            // Verde = puede avanzar
            // Verde parpadeando (apagado temporal) = puede avanzar (sigue siendo verde técnicamente)
            // Rojo/Amarillo = no puede
            return estadoActual == EstadoSemaforo.Verde || estadoActual == EstadoSemaforo.Apagado;
        }

        /// <summary>
        /// Forzar cambio de estado (útil para sincronizar semáforos)
        /// </summary>
        public void ForzarEstado(bool verde)
        {
            estadoActual = verde ? EstadoSemaforo.Verde : EstadoSemaforo.Rojo;
            timer = 0f;
            ActualizarVisuales();
        }

        // ✅ CORREGIDO: Ahora incluye estado 3 = Apagado
        /// <summary>
        /// Fuerza el semáforo a un estado específico
        /// </summary>
        /// <param name="estado">0=Rojo, 1=Amarillo, 2=Verde, 3=Apagado (para parpadeo)</param>
        public void ForzarEstadoEspecifico(int estado)
        {
            switch (estado)
            {
                case 0:
                    estadoActual = EstadoSemaforo.Rojo;
                    break;
                case 1:
                    estadoActual = EstadoSemaforo.Amarillo;
                    break;
                case 2:
                    estadoActual = EstadoSemaforo.Verde;
                    break;
                case 3:
                    estadoActual = EstadoSemaforo.Apagado; // ✅ AGREGADO
                    break;
                default:
                    TrafficLog.Warn($"[TrafficLight] Estado inválido: {estado}");
                    return;
            }

            timer = 0f;
            ActualizarVisuales();
        }

        // ✅ NUEVO: Activar/desactivar control externo
        /// <summary>
        /// Habilita o deshabilita el control externo del semáforo
        /// Cuando está activo, el semáforo no ejecuta su lógica automática
        /// </summary>
        /// <summary>
        /// Name to use in a log line. Going through waypointControlado?.name is not safe here: the
        /// null-conditional tests for a real null reference, while an unassigned inspector field is
        /// a live C# object that only Unity's own == overload reports as null, so reading .name off
        /// it throws UnassignedReferenceException.
        /// </summary>
        string WaypointName
        {
            get { return waypointControlado != null ? waypointControlado.name : "(sin waypoint)"; }
        }

        public void SetControlExterno(bool activo)
        {
            controlExterno = activo;

            if (activo)
            {
                TrafficLog.Info($"[TrafficLight] {WaypointName} ahora bajo control externo");
            }
        }

        // Gizmos para visualizar en el editor
        void OnDrawGizmosSelected()
        {
            if (waypointControlado != null)
            {
                // Línea hacia el waypoint controlado
                Gizmos.color = PuedeAvanzar() ? Color.green : Color.red;
                Gizmos.DrawLine(transform.position, waypointControlado.position);

                // Esfera de radio de detección
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawWireSphere(waypointControlado.position, radioDeteccion);
            }
        }

        public enum Estado
        {
            Verde,
            Amarillo,
            Rojo
        }

        public Estado GetEstado()
        {
            return estadoActual switch
            {
                EstadoSemaforo.Verde => Estado.Verde,
                EstadoSemaforo.Amarillo => Estado.Amarillo,
                EstadoSemaforo.Apagado => Estado.Verde, // ✅ Apagado = sigue siendo verde lógicamente
                _ => Estado.Rojo
            };
        }
    }
}
