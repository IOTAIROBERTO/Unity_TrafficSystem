using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>How a junction resolves the traffic meeting at it.</summary>
    public enum JunctionMode
    {
        /// <summary>One approach green at a time. Halves throughput, but nothing ever crosses
        /// another vehicle's path, so turns are always safe.</summary>
        OneArmAtATime = 0,

        /// <summary>Opposing approaches green together. Twice the throughput, but a vehicle
        /// turning across the junction has oncoming traffic to deal with.</summary>
        OpposingPairs = 1,

        /// <summary>No lights. One axis has right of way and the other gives way to it.</summary>
        Unsignalled = 2,
    }

    /// <summary>
    /// The shape of a generated city: how many avenues run each way, how far apart they are, and
    /// how each junction behaves. One asset describes a whole layout, so a city can be redesigned
    /// and rebuilt without touching the generator.
    /// </summary>
    [CreateAssetMenu(fileName = "CityLayout", menuName = "Traffic/City Layout", order = 2)]
    public class CityLayout : ScriptableObject
    {
        [Header("Rejilla")]
        [Tooltip("Avenidas que corren norte-sur")]
        [Range(1, 5)]
        public int avenuesNorthSouth = 2;

        [Tooltip("Avenidas que corren este-oeste")]
        [Range(1, 5)]
        public int avenuesEastWest = 2;

        [Tooltip("Distancia entre avenidas paralelas, en metros")]
        [Range(45f, 160f)]
        public float blockSize = 75f;

        [Header("Calzada")]
        [Range(4f, 12f)] public float roadHalfWidth = 7f;
        [Range(2f, 6f)] public float laneOffset = 3.5f;

        [Header("Tráfico")]
        [Range(4, 60)] public int maxTrafficDensity = 16;
        [Range(10f, 60f)] public float laneSpeed = 24f;
        [Range(2f, 15f)] public float spawnCadence = 7f;

        [Header("Cruces")]
        [Tooltip("Modo de cada cruce, en orden fila por fila. Se edita desde la ventana del sistema.")]
        [SerializeField] private JunctionMode[] junctionModes = new JunctionMode[0];

        public int Columns => Mathf.Max(1, avenuesNorthSouth);
        public int Rows => Mathf.Max(1, avenuesEastWest);
        public int JunctionCount => Columns * Rows;

        /// <summary>Resizes the mode grid to match the current dimensions, keeping what fits.</summary>
        public void EnsureGridSize()
        {
            int needed = JunctionCount;
            if (junctionModes != null && junctionModes.Length == needed) return;

            var resized = new JunctionMode[needed];
            if (junctionModes != null)
            {
                int copy = Mathf.Min(junctionModes.Length, needed);
                System.Array.Copy(junctionModes, resized, copy);
            }
            junctionModes = resized;
        }

        public JunctionMode GetMode(int column, int row)
        {
            EnsureGridSize();
            return junctionModes[Index(column, row)];
        }

        public void SetMode(int column, int row, JunctionMode mode)
        {
            EnsureGridSize();
            junctionModes[Index(column, row)] = mode;
        }

        int Index(int column, int row)
        {
            return Mathf.Clamp(row, 0, Rows - 1) * Columns + Mathf.Clamp(column, 0, Columns - 1);
        }

        /// <summary>World position of the junction at this grid slot, centred on the origin.</summary>
        public Vector3 JunctionPosition(int column, int row)
        {
            float x = (column - (Columns - 1) * 0.5f) * blockSize;
            float z = (row - (Rows - 1) * 0.5f) * blockSize;
            return new Vector3(x, 0f, z);
        }

        /// <summary>How far the roads reach past the outermost junctions.</summary>
        public float Margin => blockSize * 0.65f;

        public float HalfWidthX => (Columns - 1) * 0.5f * blockSize + Margin;
        public float HalfWidthZ => (Rows - 1) * 0.5f * blockSize + Margin;
    }
}
