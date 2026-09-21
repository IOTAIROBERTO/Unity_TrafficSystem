using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Turns a Unity spline into a traffic lane. Curves authored as a spline get tangent handles,
    /// which is what makes a smooth road practical to draw; the simulation still runs on plain
    /// waypoints, so the spline is sampled and baked into them.
    ///
    /// This whole assembly is skipped unless `com.unity.splines` is installed — the package has no
    /// hard dependency on it.
    /// </summary>
    public static class TrafficSplineLane
    {
        /// <summary>
        /// Samples <paramref name="container"/> every <paramref name="spacing"/> metres and builds a
        /// lane from the result. An existing lane with the same id is replaced, so re-baking after
        /// moving a tangent keeps one lane rather than piling up copies.
        /// </summary>
        /// <param name="laneOffset">
        /// Metres to the driver's right of the spline. The spline is the road centreline; a lane
        /// sits to its right under right-hand traffic.
        /// </param>
        public static LaneConfig Bake(
            TrafficManager manager,
            SplineContainer container,
            string laneId,
            float spacing = 8f,
            float laneOffset = 0f)
        {
            if (manager == null || container == null || container.Spline == null) return null;

            string id = string.IsNullOrWhiteSpace(laneId) ? container.gameObject.name : laneId.Trim();
            spacing = Mathf.Max(1f, spacing);

            float length = container.CalculateLength();
            if (length < spacing) return null;

            RemoveExisting(manager, id);

            TrafficLaneDesigner.BeginLane(manager, id);

            int steps = Mathf.Max(2, Mathf.RoundToInt(length / spacing));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;

                Vector3 position = container.EvaluatePosition(t);
                if (!Mathf.Approximately(laneOffset, 0f))
                {
                    Vector3 tangent = ((Vector3)container.EvaluateTangent(t)).normalized;
                    if (tangent.sqrMagnitude > 0.001f)
                    {
                        position += Vector3.Cross(Vector3.up, tangent).normalized * laneOffset;
                    }
                }

                TrafficLaneDesigner.AddWaypoint(position);
            }

            TrafficLaneDesigner.Finish();

            foreach (LaneConfig lane in manager.lanes)
            {
                if (lane.laneId == id) return lane;
            }

            return null;
        }

        /// <summary>Drops a lane already baked under this id, along with its waypoints.</summary>
        static void RemoveExisting(TrafficManager manager, string laneId)
        {
            if (manager.lanes == null) return;

            for (int i = 0; i < manager.lanes.Length; i++)
            {
                if (manager.lanes[i].laneId != laneId) continue;
                TrafficLaneDesigner.RemoveLane(manager, i);
                return;
            }
        }

        // ============================== MENU ==============================

        const string MenuPath = "Tools/InnovAscent/Traffic System/Bake selected spline into a lane";

        [MenuItem(MenuPath, true)]
        static bool BakeSelectedValidate() => Selection.activeGameObject != null &&
                                              Selection.activeGameObject.GetComponent<SplineContainer>() != null;

        [MenuItem(MenuPath)]
        static void BakeSelected()
        {
            var container = Selection.activeGameObject.GetComponent<SplineContainer>();
            var manager = Object.FindFirstObjectByType<TrafficManager>();

            if (manager == null)
            {
                TrafficLog.Error("No TrafficManager in the scene. Create one from the Traffic System window first.");
                return;
            }

            LaneConfig lane = Bake(manager, container, container.gameObject.name);
            if (lane == null)
            {
                TrafficLog.Error($"'{container.gameObject.name}' is too short to bake into a lane.");
                return;
            }

            TrafficLog.Info($"Baked '{lane.laneId}' from a spline: {lane.waypoints.Length} waypoints.");
        }
    }
}
