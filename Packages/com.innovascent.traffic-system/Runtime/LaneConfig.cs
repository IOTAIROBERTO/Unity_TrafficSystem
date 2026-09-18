using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Configuración de carril con sistema de IDs para evitar conflictos en intersecciones
    /// Version 2.2 - Optimizado: Removidas variables no utilizadas
    /// </summary>
    [System.Serializable]
    public class LaneConfig
    {
        [Header("Identificación del Carril")]
        [Tooltip("ID único del carril (ej: 'north_to_south', 'east_to_west')")]
        public string laneId = "lane_default";

        [Tooltip("¿Este carril está activo?")]
        public bool activo = true;

        [Header("Waypoints del Carril")]
        [Tooltip("Waypoints que pertenecen EXCLUSIVAMENTE a este carril")]
        public Transform[] waypoints;

        [Tooltip("Punto de spawn de vehículos")]
        public Transform spawnPoint;

        [Header("Destrucción")]
        [Tooltip("Múltiples puntos donde los vehículos pueden ser destruidos")]
        public Transform[] destroyPoints;

        [Tooltip("Radio de detección para destroy points")]
        [Range(2f, 10f)]
        public float destroyRadius = 4f;

        [Header("Spawn Settings")]
        [Range(0f, 10f)]
        public float cadenciaSpawn = 3f;

        [Range(0f, 2f)]
        public float variacionCadencia = 0.5f;

        [Tooltip("Radio de seguridad para evitar spawn si hay vehículo cerca")]
        [Range(5f, 20f)]
        public float radioSeguridadSpawn = 15f;

        [Header("Velocidad")]
        [Range(20f, 80f)]
        public float velocidadMaxima = 40f;

        // REMOVIDO: pasaPorInterseccion - nunca utilizada
        // REMOVIDO: carrilesCompatiblesEnInterseccion - nunca utilizada
    }
}
