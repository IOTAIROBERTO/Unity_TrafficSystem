using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// Central logging gate for the traffic system. Disabled by default so the package
    /// stays silent when dropped into a host project.
    /// Enabled from <see cref="TrafficConfig.enableDebugLogs"/> by <see cref="TrafficManager"/>.
    /// </summary>
    public static class TrafficLog
    {
        /// <summary>When false, no traffic system log is emitted.</summary>
        public static bool Enabled { get; set; }

        public static void Info(string message)
        {
            if (Enabled) Debug.Log(message);
        }

        public static void Warn(string message)
        {
            if (Enabled) Debug.LogWarning(message);
        }

        /// <summary>Errors are always emitted: they signal a broken setup, not verbosity.</summary>
        public static void Error(string message)
        {
            Debug.LogError(message);
        }
    }
}
