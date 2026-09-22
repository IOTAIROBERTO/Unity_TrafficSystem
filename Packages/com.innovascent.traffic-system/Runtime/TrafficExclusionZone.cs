using System.Collections.Generic;
using UnityEngine;

namespace InnovAscent.TrafficSystem
{
    /// <summary>
    /// A volume no route may enter. Some of a site is not traffic at all — a training area, a
    /// working bay, a place people stand — and a generator that only knows about lanes will happily
    /// route through it.
    ///
    /// Put one of these on an empty GameObject, size it over the area, and the editor tools stop
    /// creating crossings and turns inside it, and trim any lane that runs through it.
    /// </summary>
    [AddComponentMenu("InnovAscent/Traffic Exclusion Zone")]
    public class TrafficExclusionZone : MonoBehaviour
    {
        [Tooltip("Size of the box, in metres, centred on this object and following its rotation")]
        public Vector3 size = new Vector3(10f, 4f, 10f);

        [Tooltip("Why this area is off limits. Shown in the Scene view.")]
        public string reason = "";

        [Tooltip("Only stop turns being built through here, and leave the lanes themselves alone. " +
                 "Use it where two lanes legitimately run past a corner that a vehicle cannot " +
                 "actually swing through: a full zone would trim the lanes as well.")]
        public bool soloBloqueaGiros = false;

        /// <summary>True when the world point falls inside this box.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 half = size * 0.5f;

            // Height is ignored on purpose: lanes sit on the floor and the zones are drawn over a
            // footprint, so a box that is not tall enough should still exclude the route under it.
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
        }

        // ============================== LOOKUP ==============================

        static readonly List<TrafficExclusionZone> cache = new List<TrafficExclusionZone>();
        static int cachedFrame = -1;

        /// <summary>Every zone in the loaded scenes.</summary>
        public static List<TrafficExclusionZone> All()
        {
            // Re-finding them on every query is wasteful when a generator asks thousands of times,
            // and stale by a frame is harmless for an authoring check.
            if (cachedFrame == Time.frameCount && cache.Count > 0) return cache;

            cache.Clear();
            cache.AddRange(FindObjectsByType<TrafficExclusionZone>(FindObjectsSortMode.None));
            cachedFrame = Time.frameCount;
            return cache;
        }

        /// <summary>True when a zone that bans traffic outright covers this point.</summary>
        public static bool IsExcluded(Vector3 worldPoint)
        {
            return CoveredBy(worldPoint, false);
        }

        /// <summary>
        /// True when any zone covers this point, including the ones that only stop turns. This is
        /// the test a turn generator wants: a lane may legitimately run past a corner that a
        /// vehicle still cannot swing through.
        /// </summary>
        public static bool BlocksTurns(Vector3 worldPoint)
        {
            return CoveredBy(worldPoint, true);
        }

        static bool CoveredBy(Vector3 worldPoint, bool includeTurnOnly)
        {
            List<TrafficExclusionZone> zones = All();

            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] == null || !zones[i].isActiveAndEnabled) continue;
                if (!includeTurnOnly && zones[i].soloBloqueaGiros) continue;
                if (zones[i].Contains(worldPoint)) return true;
            }

            return false;
        }

        /// <summary>
        /// True when the straight run between two points enters a zone. A route can clear both ends
        /// and still cut straight through the middle, which is the case that matters for a turn.
        /// </summary>
        public static bool SegmentEnters(Vector3 from, Vector3 to, float step = 1f)
        {
            if (BlocksTurns(from) || BlocksTurns(to)) return true;

            float distance = Vector3.Distance(from, to);
            int samples = Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(0.25f, step)), 1, 200);

            for (int i = 1; i < samples; i++)
            {
                if (BlocksTurns(Vector3.Lerp(from, to, (float)i / samples))) return true;
            }

            return false;
        }

        /// <summary>Forces the next lookup to re-read the scene, after zones are added or moved.</summary>
        public static void Invalidate()
        {
            cache.Clear();
            cachedFrame = -1;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            Color tint = soloBloqueaGiros ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);

            Gizmos.color = new Color(tint.r, tint.g, tint.b, 0.12f);
            Gizmos.DrawCube(Vector3.zero, size);

            Gizmos.color = new Color(tint.r, tint.g, tint.b, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
