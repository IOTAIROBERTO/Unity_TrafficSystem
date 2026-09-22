using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Splits a lane part-way along it: pick a waypoint, pick where it should also be able to go,
    /// and traffic reaching that waypoint chooses between carrying on and taking the new exit.
    /// Any number of exits can hang off one waypoint.
    ///
    /// A split on its own never causes a collision — the vehicles simply fan out. What does is the
    /// far end, where the new exit rejoins the target lane, so that join is marked as an
    /// intersection with the arriving traffic yielding to the lane it merges into.
    /// </summary>
    public static class TrafficBranchTool
    {
        /// <summary>Priority given to the lane being merged into.</summary>
        const int MainPriority = 8;

        /// <summary>Priority given to traffic arriving from the branch, which has to give way.</summary>
        const int BranchPriority = 2;

        /// <summary>Roughly how far apart the connector's waypoints are, in metres.</summary>
        const float ConnectorSpacing = 6f;

        // ============================== API ==============================

        /// <summary>
        /// Adds an exit from <paramref name="sourceWaypoint"/> onto <paramref name="targetLaneId"/>.
        /// The lane the waypoint belongs to keeps a "carry on" exit, so branching stays a choice.
        /// </summary>
        /// <param name="weight">Relative likelihood against the other exits. 1 is even odds with a single carry-on exit.</param>
        public static bool Branch(TrafficManager manager, Transform sourceWaypoint, string targetLaneId,
                                  float weight, out string error)
        {
            error = null;

            if (manager == null || manager.lanes == null) { error = "No TrafficManager in the scene."; return false; }
            if (sourceWaypoint == null) { error = "Select the waypoint the branch leaves from."; return false; }

            int sourceLane = LaneIndexContaining(manager, sourceWaypoint, out int sourceIndex);
            if (sourceLane < 0) { error = $"'{sourceWaypoint.name}' does not belong to any lane on this manager."; return false; }

            int targetLane = LaneIndexById(manager, targetLaneId);
            if (targetLane < 0) { error = $"No lane with id '{targetLaneId}'."; return false; }
            if (targetLane == sourceLane) { error = "The branch has to lead to a different lane."; return false; }

            LaneConfig source = manager.lanes[sourceLane];
            LaneConfig target = manager.lanes[targetLane];

            if (sourceIndex >= source.waypoints.Length - 1)
            {
                error = "That is the last waypoint of its lane, so there is nothing to branch away from. " +
                        "Pick one further back.";
                return false;
            }

            int entryIndex = NearestWaypointIndex(target, sourceWaypoint.position);
            if (entryIndex < 0) { error = $"Lane '{targetLaneId}' has no usable waypoints."; return false; }

            Transform entry = target.waypoints[entryIndex];

            // The connector is its own little run of waypoints, so the two lanes it joins keep
            // their own geometry and either can still be edited by dragging.
            Transform connectorRoot = BuildConnector(manager, source.laneId, target.laneId, sourceWaypoint, entry,
                                                     out List<Transform> connector);
            if (connectorRoot == null) { error = "Could not build the connector."; return false; }

            MarkMerge(connector, entry, target);

            var route = new List<Transform>(connector);
            for (int i = entryIndex; i < target.waypoints.Length; i++)
            {
                if (target.waypoints[i] != null) route.Add(target.waypoints[i]);
            }

            var carryOn = new List<Transform>();
            for (int i = sourceIndex + 1; i < source.waypoints.Length; i++)
            {
                if (source.waypoints[i] != null) carryOn.Add(source.waypoints[i]);
            }

            ApplyDecision(sourceWaypoint, source.laneId, carryOn, target.laneId, route, weight);
            MarkDirty();
            return true;
        }

        /// <summary>Every branch already hanging off this waypoint, for the UI to list.</summary>
        public static WaypointDecision.Branch[] BranchesOn(Transform waypoint)
        {
            if (waypoint == null) return new WaypointDecision.Branch[0];

            var decision = waypoint.GetComponent<WaypointDecision>();
            return decision != null ? decision.ResolvedBranches : new WaypointDecision.Branch[0];
        }

        /// <summary>Drops every exit from this waypoint and removes the decision component.</summary>
        public static void ClearBranches(Transform waypoint)
        {
            if (waypoint == null) return;

            var decision = waypoint.GetComponent<WaypointDecision>();
            if (decision == null) return;

            Undo.DestroyObjectImmediate(decision);
            MarkDirty();
        }

        // ============================== CONNECTOR ==============================

        /// <summary>
        /// A curved run of waypoints from the split to the point it rejoins. Straight lines between
        /// two lanes read as a sudden jerk, so it follows a quadratic curve out of the source
        /// heading and into the target heading.
        /// </summary>
        static Transform BuildConnector(TrafficManager manager, string sourceLaneId, string targetLaneId,
                                        Transform from, Transform to, out List<Transform> waypoints)
        {
            waypoints = new List<Transform>();

            Vector3 start = from.position;
            Vector3 end = to.position;
            float span = Vector3.Distance(start, end);
            if (span < 0.5f) return null;

            // Meeting point of the two headings, which is what makes the curve leave along the
            // source lane and arrive along the target one.
            Vector3 control = start + from.forward * (span * 0.5f);

            Transform branchesRoot = FindOrCreate(manager.transform, "Branches");
            var root = new GameObject($"Branch_{sourceLaneId}_to_{targetLaneId}");
            Undo.RegisterCreatedObjectUndo(root, "Add branch");
            root.transform.SetParent(branchesRoot, false);

            int steps = Mathf.Max(2, Mathf.RoundToInt(span / ConnectorSpacing));

            // i starts at 1: the split waypoint itself is already on the source lane, and ends
            // before the entry, which already belongs to the target lane.
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                Vector3 point = Bezier(start, control, end, t);
                waypoints.Add(TrafficLaneDesigner.CreateWaypoint(root.transform, point, targetLaneId));
            }

            if (waypoints.Count == 0)
            {
                // Too short for a curve, so one point in the middle still gives the vehicle
                // something to aim at and keeps the hop within the continuity check.
                waypoints.Add(TrafficLaneDesigner.CreateWaypoint(root.transform, (start + end) * 0.5f, targetLaneId));
            }

            TrafficLaneDesigner.OrientLane(root.transform);
            AimLast(waypoints, to);
            return root.transform;
        }

        static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * control + t * t * b;
        }

        /// <summary>The last connector waypoint has no successor of its own, so point it at the join.</summary>
        static void AimLast(List<Transform> waypoints, Transform entry)
        {
            Transform last = waypoints[waypoints.Count - 1];
            Vector3 heading = entry.position - last.position;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.001f) return;

            Undo.RecordObject(last, "Aim branch");
            last.rotation = Quaternion.LookRotation(heading.normalized);

            var dir = last.GetComponent<LaneDirection>();
            if (dir != null)
            {
                Undo.RecordObject(dir, "Aim branch");
                dir.flowDirection = heading.normalized;
            }
        }

        // ============================== MERGE ==============================

        /// <summary>
        /// Marks where the branch rejoins. The arriving traffic yields and the lane it merges into
        /// keeps the right of way, which is the difference between a working merge and two vehicles
        /// arriving at the same metre of road.
        /// </summary>
        static void MarkMerge(List<Transform> connector, Transform entry, LaneConfig target)
        {
            Transform last = connector[connector.Count - 1];

            var arriving = last.GetComponent<LaneDirection>();
            if (arriving != null)
            {
                Undo.RecordObject(arriving, "Mark merge");
                arriving.zoneType = LaneDirection.ZoneType.Intersection;
                arriving.requiresYield = true;
                arriving.priority = BranchPriority;
                arriving.compatibleLaneIds = new[] { target.laneId };
            }

            var main = entry.GetComponent<LaneDirection>();
            if (main == null) return;

            Undo.RecordObject(main, "Mark merge");
            main.zoneType = LaneDirection.ZoneType.Intersection;
            main.requiresYield = false;
            main.priority = MainPriority;
        }

        // ============================== DECISION ==============================

        /// <summary>
        /// Puts the exits on the split waypoint: the new branch plus a carry-on along the original
        /// lane, so a vehicle chooses instead of always leaving. Repeated calls add exits rather
        /// than replacing them, which is what makes three, four or more exits possible.
        /// </summary>
        static void ApplyDecision(Transform waypoint, string sourceLaneId, List<Transform> carryOn,
                                  string targetLaneId, List<Transform> route, float weight)
        {
            var decision = waypoint.GetComponent<WaypointDecision>();
            if (decision == null) decision = Undo.AddComponent<WaypointDecision>(waypoint.gameObject);

            Undo.RecordObject(decision, "Add branch");

            var branches = new List<WaypointDecision.Branch>();
            if (decision.branches != null)
            {
                foreach (WaypointDecision.Branch existing in decision.branches)
                {
                    if (existing != null && existing.waypoints != null && existing.waypoints.Length > 0)
                    {
                        branches.Add(existing);
                    }
                }
            }

            // The carry-on is rebuilt every time, since inserting or deleting waypoints on the
            // source lane changes what "carry on" means.
            branches.RemoveAll(b => b.laneId == sourceLaneId);
            if (carryOn.Count > 0)
            {
                branches.Insert(0, new WaypointDecision.Branch
                {
                    laneId = sourceLaneId,
                    waypoints = carryOn.ToArray(),
                    weight = 1f,
                });
            }

            branches.Add(new WaypointDecision.Branch
            {
                laneId = targetLaneId,
                waypoints = route.ToArray(),
                weight = Mathf.Max(0f, weight),
            });

            decision.branches = branches.ToArray();
            decision.laneIdsPermitidos = new[] { sourceLaneId };

            // The first waypoint of an exit can sit further away than the default allows, and the
            // continuity check would then reject a perfectly good route.
            float longest = 0f;
            foreach (WaypointDecision.Branch branch in branches)
            {
                if (branch.waypoints.Length == 0 || branch.waypoints[0] == null) continue;
                longest = Mathf.Max(longest, Vector3.Distance(waypoint.position, branch.waypoints[0].position));
            }
            decision.distanciaMaximaValidacion = Mathf.Clamp(longest + 5f, 5f, 50f);

            EditorUtility.SetDirty(decision);
        }

        // ============================== LOOKUPS ==============================

        static int LaneIndexContaining(TrafficManager manager, Transform waypoint, out int waypointIndex)
        {
            waypointIndex = -1;

            for (int lane = 0; lane < manager.lanes.Length; lane++)
            {
                Transform[] waypoints = manager.lanes[lane].waypoints;
                if (waypoints == null) continue;

                for (int i = 0; i < waypoints.Length; i++)
                {
                    if (waypoints[i] != waypoint) continue;
                    waypointIndex = i;
                    return lane;
                }
            }

            return -1;
        }

        static int LaneIndexById(TrafficManager manager, string laneId)
        {
            for (int i = 0; i < manager.lanes.Length; i++)
            {
                if (manager.lanes[i].laneId == laneId) return i;
            }

            return -1;
        }

        static int NearestWaypointIndex(LaneConfig lane, Vector3 position)
        {
            if (lane.waypoints == null) return -1;

            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < lane.waypoints.Length; i++)
            {
                if (lane.waypoints[i] == null) continue;

                float distance = Vector3.Distance(position, lane.waypoints[i].position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        static Transform FindOrCreate(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        static void MarkDirty()
        {
            if (Application.isPlaying) return;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
