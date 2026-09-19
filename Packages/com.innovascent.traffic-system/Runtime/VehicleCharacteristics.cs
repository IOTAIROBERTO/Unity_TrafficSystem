// ==================== CARACTERÍSTICAS DE VEHÍCULO ====================
// Version 2.2 - Optimizado: Removidas variables no utilizadas
using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    [CreateAssetMenu(
        fileName = "VehiclePreset",
        menuName = "Traffic/Vehicle Preset",
        order = 1
    )]
    public class VehicleCharacteristics : ScriptableObject
    {
        public GameObject prefab;
        public string nombreVehiculo;

        [Header("🚗 Tamaño y Detección")]
        [Tooltip("Radio de detección del vehículo")]
        [Range(1f, 10f)]
        public float radioDeteccion = 2.5f;

        [Tooltip("Multiplicador de distancia de seguridad")]
        [Range(0.5f, 1.5f)]
        public float multiplicadorDistancia = 1f;

        [Header("Características Motrices")]
        [Range(0.1f, 2f)]
        public float multiplicadorVelocidad = 1f;

        [Range(1f, 5f)]
        public float aceleracion = 2f;

        [Range(2f, 30f)]
        public float frenado = 20f;

        // REMOVIDO: agresividad - nunca utilizada
        // REMOVIDO: variacionVelocidad - nunca utilizada

        [Header("Sistema de Audio")]
        [Tooltip("Sonidos de motor en ralentí")]
        public AudioClip[] motorIdleSounds;

        [Tooltip("Sonidos de motor acelerando")]
        public AudioClip[] motorAcceleratingSounds;

        // NOTA: Los siguientes arrays se mantienen por si se usan en código de audio externo
        // Si confirmas que no se usan, pueden eliminarse:
        [Tooltip("Sonidos de motor a alta velocidad")]
        public AudioClip[] motorHighSpeedSounds;

        [Tooltip("Sonidos de frenado")]
        public AudioClip[] brakingSounds;

        [Tooltip("Sonidos de claxon")]
        public AudioClip[] hornSounds;

        [Tooltip("Sonidos de arranque")]
        public AudioClip[] engineStartSounds;

        [Header("Sonidos de Neumáticos")]
        [Tooltip("Sonidos de neumáticos derrapando")]
        public AudioClip[] tireSquealSounds;

        [Tooltip("Sonidos de neumáticos rodando")]
        public AudioClip[] tireRollingSounds;

        [Header("Configuración de Audio")]
        [Range(0f, 1f)]
        public float volumenMotor = 0.5f;

        [Range(0f, 1f)]
        public float volumenNeumaticos = 0.3f;

        [Range(0.5f, 2f)]
        public float pitchMotorBase = 1f;

        [Header("Probabilidades de Sonidos")]
        [Range(0f, 1f)]
        public float probabilidadClaxon = 0.05f;

        [Range(0f, 1f)]
        public float probabilidadDerrapeCurva = 0.3f;

        [Header("Spawn")]
        [Tooltip("Entre más alto, más probable que aparezca")]
        public int pesoSpawn = 1; 

        [Header("Animación")]
        public float MAX_STEER_ANGLE = 35f;
        public float STEER_SPEED = 3f;
        public float SUSPENSION_BOUNCE = 0.03f;
        public float SUSPENSION_SPEED = 2f; 
        public float WHEEL_RADIUS = 0.35f;
    }
}
