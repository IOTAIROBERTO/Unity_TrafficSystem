using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    // ==================== CONFIGURACIÓN GLOBAL DEL TRÁFICO ====================
    // Version 2.3 - Optimizado: Removido audio ambiental (manejado externamente)
    [System.Serializable]
    public class TrafficConfig : MonoBehaviour
    {
        [Header("Control General")]
        [Range(0f, 2f)]
        public float simulationSpeed = 1f;

        [Range(0, 50)]
        public int maxTrafficDensity = 20;

        [Header("Configuración de Semáforos")]
        public bool useSemaforos = true;

        [Range(5f, 30f)]
        public float tiempoVerdeSemaforo = 10f;

        [Range(2f, 10f)]
        public float tiempoRojoSemaforo = 8f;

        [Range(5f, 30f)]
        public float distanciaFrenadoSemaforo = 15f;

        [Header("Detección de Colisiones")]
        [Range(5f, 20f)]
        public float distanciaDeteccionFrente = 10f;

        [Header("Optimización")]
        [Tooltip("Capa física de los vehículos. Si queda vacía se deriva de 'vehicleLayerName'.")]
        public LayerMask vehicleLayer;

        [Tooltip("Nombre de la capa donde viven los vehículos. Debe existir en Tags and Layers.")]
        public string vehicleLayerName = "Vehicles";

        [Header("Diagnóstico")]
        [Tooltip("Emite los logs del sistema de tráfico. Apagado en producción.")]
        public bool enableDebugLogs = false;

        [Header("Naturalidad del Tráfico")]
        [Range(0f, 1f)]
        public float variacionVelocidad = 0.2f;

        [Range(0f, 0.5f)]
        public float variacionTiempoReaccion = 0.15f;

        // REMOVIDO: Todo el sistema de audio ambiental (manejado por TrafficSystem_AudioManager)
    }
}
