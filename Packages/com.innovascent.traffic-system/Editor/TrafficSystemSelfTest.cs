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

            // Stop points that all take the component defaults change on the same frame, which
            // halts every vehicle on a site at once and then releases them all at once.
            TrafficLightController stopA = TrafficLaneDesigner.AddStopPoint(manager, lane.waypoints[1]);
            TrafficLightController stopB = TrafficLaneDesigner.AddStopPoint(manager, lane.waypoints[3]);
            float cycle = manager.config != null
                ? manager.config.tiempoVerdeSemaforo + manager.config.tiempoRojoSemaforo + 2f
                : 20f;
            bool staggered = stopA != null && stopB != null
                             && !Mathf.Approximately(stopA.desfaseInicial, stopB.desfaseInicial)
                             && stopA.desfaseInicial >= 0f && stopA.desfaseInicial < cycle
                             && stopB.desfaseInicial >= 0f && stopB.desfaseInicial < cycle;
            Check("two stop points do not change on the same frame", staggered,
                stopA != null && stopB != null
                    ? "offsets " + stopA.desfaseInicial.ToString("F1") + "s and "
                      + stopB.desfaseInicial.ToString("F1") + "s in a " + cycle.ToString("F0") + "s cycle"
                    : "a stop point was not created");

            // Different offsets are not enough on their own: an offset only spreads the lights out
            // if it resolves to the phase it lands in. Walking the cycle has to visit red, green
            // and amber, and has to come back round to where it started.
            float red = 8f, green = 10f;
            float dummy;
            bool sawRed = false, sawGreen = false, sawAmber = false;

            for (float t = 0f; t < red + green + 2f; t += 0.5f)
            {
                TrafficLightController.Estado phase =
                    TrafficLightController.FaseEnElCiclo(t, red, green, out dummy);

                if (phase == TrafficLightController.Estado.Rojo) sawRed = true;
                if (phase == TrafficLightController.Estado.Verde) sawGreen = true;
                if (phase == TrafficLightController.Estado.Amarillo) sawAmber = true;
            }

            float intoStart, intoWrap;
            TrafficLightController.Estado atStart =
                TrafficLightController.FaseEnElCiclo(0f, red, green, out intoStart);
            TrafficLightController.Estado atWrap =
                TrafficLightController.FaseEnElCiclo(red + green + 2f, red, green, out intoWrap);

            Check("an offset lands in the phase it falls in",
                sawRed && sawGreen && sawAmber && atStart == atWrap
                && Mathf.Approximately(intoStart, intoWrap),
                "cycle covers red=" + sawRed + " green=" + sawGreen + " amber=" + sawAmber
                + ", wraps back to " + atWrap);

            float intoLate;
            TrafficLightController.Estado late =
                TrafficLightController.FaseEnElCiclo(red + 3f, red, green, out intoLate);
            Check("an offset past the red phase does not start the light on red",
                late == TrafficLightController.Estado.Verde && Mathf.Approximately(intoLate, 3f),
                "offset " + (red + 3f).ToString("F0") + "s -> " + late + " at " + intoLate.ToString("F1") + "s in");

            TrafficLaneDesigner.Finish();
            Check("Finish stops placement", !TrafficLaneDesigner.IsPlacing, "placing stopped");

            // A lane drawn as straight runs meeting at a right angle asks for a turn of zero
            // radius. The driver cannot steer that fast, so the body swings wide and clips whatever
            // the lane runs beside. Rounding has to take the sharpest corner well down.
            Transform laneRoot = manager.lanes[0].waypoints[0].parent;
            float radiusBefore = TrafficLaneDesigner.TightestRadius(laneRoot);
            float radiusAfter = TrafficLaneDesigner.OpenOutCorners(laneRoot, 3.5f);
            TrafficLaneDesigner.RebuildLaneConfig(manager, "self_test", laneRoot);

            // Only the result is asserted, not an improvement over the reading before: a lane whose
            // waypoints are 10 m apart reports a flattering radius for a corner it never actually
            // rounds, so the two numbers are not comparable.
            Check("opening out the corners gives a radius a vehicle can follow",
                radiusAfter >= 3f,
                "tightest radius now " + radiusAfter.ToString("F1") + " m, was reported as " +
                radiusBefore.ToString("F1") + " m at the original spacing");

            // Branching: a second lane to aim at, then several exits off one waypoint.
            // Laid across the first lane on purpose: the crossing checks further down need two
            // lanes that actually meet.
            TrafficLaneDesigner.BeginLane(manager, "self_test_side");
            foreach (Vector3 p in new[]
            {
                new Vector3(-20f, 0f, 15f),
                new Vector3(-17f, 0f, 15f),
                new Vector3(-10f, 0f, 15f),
                new Vector3(10f, 0f, 15f),
                new Vector3(17f, 0f, 15f),
                new Vector3(20f, 0f, 15f),
            })
            {
                TrafficLaneDesigner.AddWaypoint(p);
            }
            TrafficLaneDesigner.Finish();

            Transform split = manager.lanes[0].waypoints[1];
            bool branched = TrafficBranchTool.Branch(manager, split, "self_test_side", 2f, out string branchError);
            Check("branching a lane part-way along it", branched, branched ? "ok" : branchError);

            if (branched)
            {
                // The connector has to leave along the lane it comes from and arrive along the one
                // it joins. A curve aligned only at its start swings wide and clips whatever the
                // lanes run alongside, which on a real site is the racking.
                Transform connectorRoot = manager.transform.Find("Branches");
                if (connectorRoot != null && connectorRoot.childCount > 0)
                {
                    Transform connector = connectorRoot.GetChild(0);
                    if (connector.childCount >= 2)
                    {
                        Vector3 leaves = (connector.GetChild(1).position - connector.GetChild(0).position).normalized;
                        float alignment = Vector3.Dot(split.forward, leaves);
                        Check("the turn leaves along the lane it came from", alignment > 0.7f,
                            "dot=" + alignment.ToString("F2"));
                    }
                }

                WaypointDecision.Branch[] exits = TrafficBranchTool.BranchesOn(split);
                Check("the split offers carrying on and the exit", exits.Length == 2,
                    "exits=" + exits.Length);

                var decision = split.GetComponent<WaypointDecision>();
                var taken = new Dictionary<string, int>();
                for (int i = 0; i < 600; i++)
                {
                    Transform[] route = decision.GetRutaAleatoria("self_test", split.position);
                    string id = route != null ? decision.GetNuevoLaneId(route) : "none";
                    taken[id] = taken.TryGetValue(id, out int count) ? count + 1 : 1;
                }

                bool everyExitUsed = taken.Count == 2 && !taken.ContainsKey("none");
                Check("every exit is reachable", everyExitUsed, Describe(taken));

                int side = taken.TryGetValue("self_test_side", out int s) ? s : 0;
                int straight = taken.TryGetValue("self_test", out int t) ? t : 0;
                Check("weight 2 sends about twice as many down the exit", side > straight,
                    "exit=" + side + " carry on=" + straight);
            }

            // Crossing detection and the give-way rule. Vehicle compares priorities with
            // "other.priority <= mine", so the lane with the right of way must carry the LOWER
            // number. Getting that backwards leaves requiresYield set and nobody ever stopping,
            // which is invisible until two vehicles occupy the same metre.
            List<TrafficCrossingTool.Crossing> found = TrafficCrossingTool.Find(manager);
            Check("crossing between the two lanes is found", found.Count > 0, "crossings=" + found.Count);

            if (found.Count > 0)
            {
                TrafficCrossingTool.Crossing crossing = found[0];
                TrafficCrossingTool.MarkGiveWay(manager, crossing, true);

                Check("both sides know they are in an intersection",
                    TrafficCrossingTool.IsMarked(manager, crossing), "marked");

                var mainDir = TrafficCrossingTool.WaypointAt(manager, crossing.laneA, crossing.indexA)
                    .GetComponent<LaneDirection>();
                var sideDir = TrafficCrossingTool.WaypointAt(manager, crossing.laneB, crossing.indexB)
                    .GetComponent<LaneDirection>();

                Check("the side lane is the one that yields",
                    sideDir.requiresYield && !mainDir.requiresYield,
                    "side=" + sideDir.requiresYield + " main=" + mainDir.requiresYield);
                Check("right of way carries the lower priority number",
                    mainDir.priority < sideDir.priority,
                    "main=" + mainDir.priority + " side=" + sideDir.priority);
            }

            // A waypoint can sit on more than one crossing. Marking the scene must not let a later
            // crossing clear a yield an earlier one set, which would leave a junction where nobody
            // stops while every crossing still reports itself marked.
            TrafficLaneDesigner.BeginLane(manager, "self_test_third");
            foreach (Vector3 p in new[] { new Vector3(5f, 0f, -20f), new Vector3(5f, 0f, 40f) })
            {
                TrafficLaneDesigner.AddWaypoint(p);
            }
            TrafficLaneDesigner.Finish();

            TrafficCrossingTool.MarkAll(manager);

            List<TrafficCrossingTool.Crossing> all = TrafficCrossingTool.Find(manager);
            int nobodyYields = 0;
            foreach (TrafficCrossingTool.Crossing c in all)
            {
                var dirA = TrafficCrossingTool.WaypointAt(manager, c.laneA, c.indexA).GetComponent<LaneDirection>();
                var dirB = TrafficCrossingTool.WaypointAt(manager, c.laneB, c.indexB).GetComponent<LaneDirection>();
                if (dirA != null && dirB != null && !dirA.requiresYield && !dirB.requiresYield) nobodyYields++;
            }

            Check("marking the scene leaves somebody yielding at every crossing", nobodyYields == 0,
                "crossings=" + all.Count + " with nobody yielding=" + nobodyYields);

            // Adding turns afterwards must not undo those rules: a turn marks the lane it merges
            // into as having the right of way, which used to clear a yield set for a different
            // crossing on the same waypoint.
            TrafficCrossingTool.AddSensibleTurns(manager, 120f, 1f, out int _);

            int afterTurns = 0;
            foreach (TrafficCrossingTool.Crossing c in TrafficCrossingTool.Find(manager))
            {
                var dirA = TrafficCrossingTool.WaypointAt(manager, c.laneA, c.indexA).GetComponent<LaneDirection>();
                var dirB = TrafficCrossingTool.WaypointAt(manager, c.laneB, c.indexB).GetComponent<LaneDirection>();
                if (dirA != null && dirB != null && !dirA.requiresYield && !dirB.requiresYield) afterTurns++;
            }

            Check("adding turns does not undo the give-way rules", afterTurns == 0,
                "crossings with nobody yielding after turns=" + afterTurns);

            // A no-traffic area has to hold against the generators, not just be drawn.
            var zoneObject = new GameObject("SelfTestNoTrafficArea");
            zoneObject.transform.position = new Vector3(0f, 0f, 15f);
            var zone = zoneObject.AddComponent<TrafficExclusionZone>();
            zone.size = new Vector3(30f, 4f, 10f); // wide enough to swallow part of self_test_side
            TrafficExclusionZone.Invalidate();

            Check("a point inside the area is excluded",
                TrafficExclusionZone.IsExcluded(new Vector3(0f, 0f, 15f)), "centre");
            Check("a point outside it is not",
                !TrafficExclusionZone.IsExcluded(new Vector3(0f, 0f, 40f)), "well clear");
            Check("a run passing straight through is caught",
                TrafficExclusionZone.SegmentEnters(new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, 40f)),
                "both ends outside, middle inside");

            // A turn-only zone has to read as clear for the lanes and as blocked for a connector,
            // otherwise the only way to stop an impossible turn is to trim the lanes that use it.
            var turnOnlyObject = new GameObject("Self test turn-only zone");
            Undo.RegisterCreatedObjectUndo(turnOnlyObject, "Self test");
            turnOnlyObject.transform.position = new Vector3(60f, 0f, 0f);
            var turnOnly = turnOnlyObject.AddComponent<TrafficExclusionZone>();
            turnOnly.size = new Vector3(10f, 4f, 10f);
            turnOnly.soloBloqueaGiros = true;
            TrafficExclusionZone.Invalidate();

            Check("a turn-only area does not exclude the lane running through it",
                !TrafficExclusionZone.IsExcluded(new Vector3(60f, 0f, 0f)), "lane is left alone");
            Check("a turn-only area still stops a turn being built through it",
                TrafficExclusionZone.BlocksTurns(new Vector3(60f, 0f, 0f))
                && TrafficExclusionZone.SegmentEnters(new Vector3(50f, 0f, 0f), new Vector3(70f, 0f, 0f)),
                "connector is refused");

            Undo.DestroyObjectImmediate(turnOnlyObject);
            TrafficExclusionZone.Invalidate();

            int insideBefore = CountWaypointsInsideZones(manager);

            TrafficCrossingTool.TrimLanesOutsideZones(manager, out int removedWaypoints, out int _);

            int insideAfter = CountWaypointsInsideZones(manager);

            Check("trimming clears every waypoint out of the area",
                insideBefore > 0 && insideAfter == 0,
                "inside before=" + insideBefore + " after=" + insideAfter + " removed=" + removedWaypoints);

            // The road either side of an area has to survive. Truncating to the longest surviving
            // piece would quietly delete half a corridor, which is the opposite of keeping
            // everything outside the area and routing around it.
            Check("a lane cut in two keeps both pieces",
                removedWaypoints == insideBefore,
                "removed=" + removedWaypoints + " but only " + insideBefore + " were inside");

            bool splitKept = false;
            foreach (LaneConfig config in manager.lanes)
            {
                if (config.laneId.StartsWith("self_test_side")) splitKept = true;
            }
            Check("the split lane is still in the manager", splitKept, "found a self_test_side piece");

            foreach (TrafficCrossingTool.Crossing c in TrafficCrossingTool.Find(manager))
            {
                if (!TrafficExclusionZone.IsExcluded(c.point)) continue;
                Check("no crossing is reported inside the area", false, "at " + c.point);
                break;
            }

            Object.DestroyImmediate(zoneObject);
            TrafficExclusionZone.Invalidate();

            // Remove whatever is left: splitting at a zone can leave more lanes than were created.
            while (manager.lanes.Length > 0) TrafficLaneDesigner.RemoveLane(manager, 0);
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

        static int CountWaypointsInsideZones(TrafficManager manager)
        {
            int count = 0;

            foreach (LaneConfig config in manager.lanes)
            {
                if (config.waypoints == null) continue;
                foreach (Transform point in config.waypoints)
                {
                    if (point != null && TrafficExclusionZone.IsExcluded(point.position)) count++;
                }
            }

            return count;
        }

        static string Describe(Dictionary<string, int> counts)
        {
            var parts = new List<string>(counts.Count);
            foreach (KeyValuePair<string, int> pair in counts) parts.Add(pair.Key + "=" + pair.Value);
            return string.Join(", ", parts);
        }
    }
}
