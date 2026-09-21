using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// One-click check that the package works in the host project. It drives the lane designer
    /// through its whole API on a throwaway GameObject in the open scene and deletes everything
    /// it made, so it never touches your own scene contents and never opens a dialog.
    /// </summary>
    public static class TrafficSystemSelfTest
    {
        const string MenuPath = "Tools/InnovAscent/Traffic System/Run Self-Test";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var report = new StringBuilder();
            int passed = 0;
            int failed = 0;

            void Check(string what, bool ok, string detail)
            {
                if (ok) passed++; else failed++;
                report.AppendLine((ok ? "PASS  " : "FAIL  ") + what + "  —  " + detail);
            }

            var host = new GameObject("__TrafficSystemSelfTest");
            host.AddComponent<TrafficConfig>();
            var manager = host.AddComponent<TrafficManager>();

            TrafficLaneDesigner.BeginLane(manager, "self_test");
            Check("BeginLane starts placement", TrafficLaneDesigner.IsPlacing,
                "activeLaneId=" + TrafficLaneDesigner.ActiveLaneId);
            Check("BeginLane appends a LaneConfig",
                manager.lanes != null && manager.lanes.Length == 1 && manager.lanes[0].laneId == "self_test",
                "lanes=" + (manager.lanes == null ? 0 : manager.lanes.Length));

            var points = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 10f),
                new Vector3(0f, 0f, 20f),
                new Vector3(10f, 0f, 30f),
            };
            foreach (Vector3 p in points) TrafficLaneDesigner.AddWaypoint(p);
            Check("every click places a waypoint", TrafficLaneDesigner.PlacedCount == points.Count,
                "placed=" + TrafficLaneDesigner.PlacedCount + " expected=" + points.Count);

            LaneConfig lane = manager.lanes[0];
            Check("lane holds the waypoints",
                lane.waypoints != null && lane.waypoints.Length == points.Count,
                "waypoints=" + (lane.waypoints == null ? 0 : lane.waypoints.Length));
            Check("first waypoint is the spawn point", lane.spawnPoint == lane.waypoints[0],
                "spawnPoint=" + (lane.spawnPoint != null ? lane.spawnPoint.name : "null"));
            Check("last waypoint is the destroy point",
                lane.destroyPoints != null && lane.destroyPoints.Length == 1 &&
                lane.destroyPoints[0] == lane.waypoints[lane.waypoints.Length - 1],
                "destroyPoints=" + (lane.destroyPoints == null ? 0 : lane.destroyPoints.Length));

            float dot = Vector3.Dot(lane.waypoints[0].forward,
                (lane.waypoints[1].position - lane.waypoints[0].position).normalized);
            Check("waypoints face the next one", dot > 0.99f, "dot=" + dot.ToString("F3"));

            TrafficLightController light = TrafficLaneDesigner.AddTrafficLight(manager, lane.waypoints[2]);
            int renderers = light != null ? light.GetComponentsInChildren<Renderer>(true).Length : 0;
            Check("traffic light is built with its bulbs", light != null && renderers >= 4,
                "renderers=" + renderers);

            TrafficLaneDesigner.Finish();
            Check("Finish stops placement", !TrafficLaneDesigner.IsPlacing, "placing stopped");

            TrafficLaneDesigner.RemoveLane(manager, 0);
            Check("RemoveLane drops the lane", manager.lanes.Length == 0,
                "lanes=" + manager.lanes.Length);

            if (light != null && light.transform.root != host.transform)
                Object.DestroyImmediate(light.transform.root.gameObject);
            Object.DestroyImmediate(host);

            string summary = "Traffic System self-test: " + passed + " passed, " + failed + " failed.\n" + report;
            LastReport = summary;
            if (failed == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        /// <summary>Report from the last run, for scripted checks.</summary>
        public static string LastReport { get; private set; }
    }
}
