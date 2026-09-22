using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Finds every place two lanes cross and wires what happens there. Lanes drawn by hand meet at
    /// points nobody recorded: the traffic runs straight through them, so two vehicles arriving at
    /// the same metre of floor have nothing telling them who waits.
    ///
    /// Two things can be done at a crossing, and they are different jobs:
    /// give way, which decides who stops, and a turn, which lets traffic change lane there.
    /// </summary>
    public static class TrafficCrossingTool
    {
        // Vehicle compares priorities with "other.priority <= mine", so a LOWER number is the
        // stronger claim, matching the scale the sample builder already uses.
        const int MainPriority = 3;
        const int GiveWayPriority = 7;

        /// <summary>Crossings closer together than this are treated as one.</summary>
        const float ClusterSize = 4f;

        /// <summary>Where two lanes meet, and the waypoint on each that arrives there.</summary>
        public struct Crossing
        {
            public int laneA;
            public int laneB;

            /// <summary>Waypoint on lane A immediately before the crossing.</summary>
            public int indexA;

            /// <summary>Waypoint on lane B immediately before the crossing.</summary>
            public int indexB;

            public Vector3 point;
        }

        // ============================== FINDING ==============================

        /// <summary>
        /// Every crossing between two different lanes, one entry per place rather than one per
        /// pair of segments: lanes that run alongside each other for a while otherwise report the
        /// same corner a dozen times.
        /// </summary>
        public static List<Crossing> Find(TrafficManager manager)
        {
            var found = new List<Crossing>();
            if (manager == null || manager.lanes == null) return found;

            var seen = new HashSet<string>();

            for (int a = 0; a < manager.lanes.Length; a++)
            {
                for (int b = a + 1; b < manager.lanes.Length; b++)
                {
                    Transform[] wa = manager.lanes[a].waypoints;
                    Transform[] wb = manager.lanes[b].waypoints;
                    if (wa == null || wb == null) continue;

                    for (int i = 1; i < wa.Length; i++)
                    {
                        if (wa[i - 1] == null || wa[i] == null) continue;

                        Vector2 a1 = Flat(wa[i - 1].position);
                        Vector2 a2 = Flat(wa[i].position);

                        for (int j = 1; j < wb.Length; j++)
                        {
                            if (wb[j - 1] == null || wb[j] == null) continue;

                            Vector2 b1 = Flat(wb[j - 1].position);
                            Vector2 b2 = Flat(wb[j].position);

                            if (!SegmentsCross(a1, a2, b1, b2, out Vector2 hit)) continue;

                            string key = a + "|" + b + "|" +
                                         Mathf.RoundToInt(hit.x / ClusterSize) + "|" +
                                         Mathf.RoundToInt(hit.y / ClusterSize);
                            if (!seen.Add(key)) continue;

                            // A crossing inside an area that is off limits is not a junction to
                            // manage, it is a place no route should be.
                            if (TrafficExclusionZone.IsExcluded(new Vector3(hit.x, 0f, hit.y))) continue;

                            found.Add(new Crossing
                            {
                                laneA = a,
                                laneB = b,
                                indexA = i - 1,
                                indexB = j - 1,
                                point = new Vector3(hit.x, wa[i - 1].position.y, hit.y),
                            });
                        }
                    }
                }
            }

            return found;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 hit)
        {
            hit = Vector2.zero;

            float denominator = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);
            if (Mathf.Abs(denominator) < 1e-6f) return false; // parallel

            float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / denominator;
            float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / denominator;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;

            hit = p1 + (p2 - p1) * t;
            return true;
        }

        // ============================== STATE ==============================

        /// <summary>True once both sides of the crossing know they are in one.</summary>
        public static bool IsMarked(TrafficManager manager, Crossing crossing)
        {
            LaneDirection a = DirectionAt(manager, crossing.laneA, crossing.indexA);
            LaneDirection b = DirectionAt(manager, crossing.laneB, crossing.indexB);

            return a != null && b != null &&
                   a.zoneType == LaneDirection.ZoneType.Intersection &&
                   b.zoneType == LaneDirection.ZoneType.Intersection;
        }

        /// <summary>True when traffic can already change lane here.</summary>
        public static bool HasTurn(TrafficManager manager, Crossing crossing)
        {
            return TurnExists(manager, crossing.laneA, crossing.indexA, manager.lanes[crossing.laneB].laneId)
                || TurnExists(manager, crossing.laneB, crossing.indexB, manager.lanes[crossing.laneA].laneId);
        }

        static bool TurnExists(TrafficManager manager, int lane, int index, string targetLaneId)
        {
            Transform waypoint = WaypointAt(manager, lane, index);
            if (waypoint == null) return false;

            var decision = waypoint.GetComponent<WaypointDecision>();
            if (decision == null) return false;

            foreach (WaypointDecision.Branch branch in decision.ResolvedBranches)
            {
                if (branch.laneId == targetLaneId) return true;
            }

            return false;
        }

        // ============================== GIVE WAY ==============================

        /// <summary>
        /// Decides who stops. The main lane keeps the right of way and the other one yields, which
        /// is what <see cref="Vehicle"/> reads when it finds itself inside an intersection.
        /// </summary>
        /// <param name="accumulate">
        /// True while marking a whole scene: a waypoint can sit on several crossings, and one that
        /// already has to give way somewhere must keep doing so. Without this the last crossing
        /// written wins and clears an earlier yield, leaving a junction where nobody stops.
        /// False when the user picks the rule for one crossing, where the choice is meant to win.
        /// </param>
        public static void MarkGiveWay(TrafficManager manager, Crossing crossing, bool laneAIsMain,
                                       bool accumulate = false)
        {
            int mainLane = laneAIsMain ? crossing.laneA : crossing.laneB;
            int mainIndex = laneAIsMain ? crossing.indexA : crossing.indexB;
            int sideLane = laneAIsMain ? crossing.laneB : crossing.laneA;
            int sideIndex = laneAIsMain ? crossing.indexB : crossing.indexA;

            Apply(manager, mainLane, mainIndex, false, MainPriority, manager.lanes[sideLane].laneId, accumulate);
            Apply(manager, sideLane, sideIndex, true, GiveWayPriority, manager.lanes[mainLane].laneId, accumulate);
            MarkDirty();
        }

        static void Apply(TrafficManager manager, int lane, int index, bool yields, int priority,
                          string otherLaneId, bool accumulate)
        {
            LaneDirection direction = EnsureDirection(manager, lane, index);
            if (direction == null) return;

            Undo.RecordObject(direction, "Mark crossing");
            direction.zoneType = LaneDirection.ZoneType.Intersection;

            // Giving way is sticky while marking a scene: having the right of way over one lane
            // says nothing about the other crossing this same waypoint sits on.
            bool keepsYield = accumulate && direction.requiresYield;
            direction.requiresYield = yields || keepsYield;
            direction.priority = keepsYield ? Mathf.Max(direction.priority, priority) : priority;

            direction.compatibleLaneIds = MergeLaneIds(accumulate ? direction.compatibleLaneIds : null, otherLaneId);
            EditorUtility.SetDirty(direction);
        }

        /// <summary>Keeps the lanes a waypoint already had to cooperate with, and adds one more.</summary>
        static string[] MergeLaneIds(string[] existing, string laneId)
        {
            var ids = new List<string>();
            if (existing != null) ids.AddRange(existing);
            if (!ids.Contains(laneId)) ids.Add(laneId);
            return ids.ToArray();
        }

        /// <summary>
        /// Marks every crossing, treating the lane with more waypoints as the main one. That is a
        /// guess — a long ring road usually is the through route — so it is a starting point to
        /// correct per crossing, not an answer.
        /// </summary>
        public static int MarkAll(TrafficManager manager)
        {
            List<Crossing> crossings = Find(manager);

            foreach (Crossing crossing in crossings)
            {
                int lengthA = manager.lanes[crossing.laneA].waypoints.Length;
                int lengthB = manager.lanes[crossing.laneB].waypoints.Length;
                MarkGiveWay(manager, crossing, lengthA >= lengthB, accumulate: true);
            }

            return crossings.Count;
        }

        // ============================== TURNS ==============================

        /// <summary>Lets traffic on one lane change onto the other at this crossing.</summary>
        public static bool AddTurn(TrafficManager manager, Crossing crossing, bool fromAtoB, float weight,
                                   out string error)
        {
            int fromLane = fromAtoB ? crossing.laneA : crossing.laneB;
            int fromIndex = fromAtoB ? crossing.indexA : crossing.indexB;
            int toLane = fromAtoB ? crossing.laneB : crossing.laneA;

            Transform source = WaypointAt(manager, fromLane, fromIndex);
            if (source == null) { error = "The waypoint before the crossing is missing."; return false; }

            return TrafficBranchTool.Branch(manager, source, manager.lanes[toLane].laneId, weight, out error);
        }

        /// <summary>
        /// Adds a turn at every crossing, in both directions, wherever the turn is one a vehicle
        /// could actually take. A crossing where the two lanes head in opposite directions would
        /// otherwise get a route that doubles the traffic back on itself.
        /// </summary>
        /// <param name="maxTurnAngle">
        /// Largest heading change allowed, in degrees. 120 keeps ordinary left and right turns and
        /// rejects anything that amounts to a U-turn.
        /// </param>
        /// <returns>How many turns were added.</returns>
        public static int AddSensibleTurns(TrafficManager manager, float maxTurnAngle, float weight,
                                           out int skipped)
        {
            int added = 0;
            skipped = 0;

            foreach (Crossing crossing in Find(manager))
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool fromAtoB = pass == 0;

                    int fromLane = fromAtoB ? crossing.laneA : crossing.laneB;
                    int fromIndex = fromAtoB ? crossing.indexA : crossing.indexB;
                    int toLane = fromAtoB ? crossing.laneB : crossing.laneA;
                    int toIndex = fromAtoB ? crossing.indexB : crossing.indexA;

                    if (TurnExists(manager, fromLane, fromIndex, manager.lanes[toLane].laneId)) continue;

                    float angle = Vector3.Angle(FlowAt(manager, fromLane, fromIndex),
                                                FlowAt(manager, toLane, toIndex));
                    if (angle > maxTurnAngle) { skipped++; continue; }

                    Transform source = WaypointAt(manager, fromLane, fromIndex);
                    Transform entry = WaypointAt(manager, toLane, toIndex);
                    if (source == null || entry == null) { skipped++; continue; }
                    if (TrafficExclusionZone.SegmentEnters(source.position, entry.position)) { skipped++; continue; }

                    if (AddTurn(manager, crossing, fromAtoB, weight, out _)) added++;
                    else skipped++;
                }
            }

            return added;
        }

        /// <summary>
        /// Takes every lane out of the exclusion zones without throwing away the road either side.
        /// A lane that crosses a zone is split into a piece before it and a piece after, each kept
        /// as its own lane, so traffic can still be routed around through the turns at the corners.
        /// Truncating to the longest surviving piece instead would quietly delete half a corridor.
        /// </summary>
        /// <returns>How many lanes were changed.</returns>
        public static int TrimLanesOutsideZones(TrafficManager manager, out int waypointsRemoved, out int lanesDropped)
        {
            waypointsRemoved = 0;
            lanesDropped = 0;
            int changed = 0;

            TrafficExclusionZone.Invalidate();
            var keep = new List<LaneConfig>();

            foreach (LaneConfig lane in manager.lanes)
            {
                if (lane.waypoints == null || lane.waypoints.Length == 0) { keep.Add(lane); continue; }

                // Every stretch that stays outside, in order along the lane.
                var runs = new List<List<Transform>>();
                var current = new List<Transform>();

                foreach (Transform waypoint in lane.waypoints)
                {
                    bool inside = waypoint == null || TrafficExclusionZone.IsExcluded(waypoint.position);

                    if (!inside) { current.Add(waypoint); continue; }

                    if (waypoint != null)
                    {
                        Undo.DestroyObjectImmediate(waypoint.gameObject);
                        waypointsRemoved++;
                    }

                    if (current.Count > 0) { runs.Add(current); current = new List<Transform>(); }
                }

                if (current.Count > 0) runs.Add(current);

                if (runs.Count == 1 && runs[0].Count == lane.waypoints.Length) { keep.Add(lane); continue; }

                changed++;

                for (int i = 0; i < runs.Count; i++)
                {
                    if (runs[i].Count < 2) { lanesDropped++; continue; }

                    LaneConfig piece = i == 0 ? lane : Clone(lane);
                    // A split lane needs its own id, or two lanes answer to the same name and a
                    // decision aiming at one can be sent down the other.
                    if (i > 0) piece.laneId = lane.laneId + "_" + (i + 1);

                    // And its own root: pieces left sharing one parent look independent until the
                    // first is removed, which destroys the other's waypoints with it.
                    if (i > 0)
                    {
                        Transform oldRoot = runs[i][0].parent;
                        var newRoot = new GameObject("Lane_" + piece.laneId);
                        Undo.RegisterCreatedObjectUndo(newRoot, "Split lane");
                        newRoot.transform.SetParent(oldRoot != null ? oldRoot.parent : null, false);

                        foreach (Transform waypoint in runs[i]) Undo.SetTransformParent(waypoint, newRoot.transform, "Split lane");
                        TrafficLaneDesigner.RenameWaypoints(newRoot.transform);
                    }

                    piece.waypoints = runs[i].ToArray();
                    piece.spawnPoint = piece.waypoints[0];
                    piece.destroyPoints = new[] { piece.waypoints[piece.waypoints.Length - 1] };
                    RelabelWaypoints(piece);
                    keep.Add(piece);
                }
            }

            Undo.RecordObject(manager, "Trim lanes to exclusion zones");
            manager.lanes = keep.ToArray();
            MarkDirty();
            return changed;
        }

        static LaneConfig Clone(LaneConfig source)
        {
            return new LaneConfig
            {
                laneId = source.laneId,
                activo = source.activo,
                destroyRadius = source.destroyRadius,
                cadenciaSpawn = source.cadenciaSpawn,
                variacionCadencia = source.variacionCadencia,
                radioSeguridadSpawn = source.radioSeguridadSpawn,
                velocidadMaxima = source.velocidadMaxima,
            };
        }

        /// <summary>Keeps LaneDirection agreeing with the lane it now belongs to after a split.</summary>
        static void RelabelWaypoints(LaneConfig lane)
        {
            foreach (Transform waypoint in lane.waypoints)
            {
                var direction = waypoint.GetComponent<LaneDirection>();
                if (direction == null) continue;

                Undo.RecordObject(direction, "Split lane");
                direction.laneId = lane.laneId;
            }
        }

        // ============================== HELPERS ==============================

        public static Transform WaypointAt(TrafficManager manager, int lane, int index)
        {
            if (manager == null || manager.lanes == null) return null;
            if (lane < 0 || lane >= manager.lanes.Length) return null;

            Transform[] waypoints = manager.lanes[lane].waypoints;
            if (waypoints == null || index < 0 || index >= waypoints.Length) return null;

            return waypoints[index];
        }

        static LaneDirection DirectionAt(TrafficManager manager, int lane, int index)
        {
            Transform waypoint = WaypointAt(manager, lane, index);
            return waypoint != null ? waypoint.GetComponent<LaneDirection>() : null;
        }

        /// <summary>
        /// The waypoint's LaneDirection, adding one if it has none. Lanes built outside the lane
        /// designer often carry no LaneDirection at all, and without it there is nothing to write
        /// the give-way rule onto — the crossing would be marked in the UI and do nothing at all.
        /// </summary>
        static LaneDirection EnsureDirection(TrafficManager manager, int lane, int index)
        {
            Transform waypoint = WaypointAt(manager, lane, index);
            if (waypoint == null) return null;

            var direction = waypoint.GetComponent<LaneDirection>();
            if (direction != null) return direction;

            direction = Undo.AddComponent<LaneDirection>(waypoint.gameObject);
            direction.laneId = manager.lanes[lane].laneId;
            direction.flowDirection = FlowAt(manager, lane, index);
            return direction;
        }

        /// <summary>Heading along the lane at this waypoint, taken from the next one where there is one.</summary>
        static Vector3 FlowAt(TrafficManager manager, int lane, int index)
        {
            Transform current = WaypointAt(manager, lane, index);
            Transform next = WaypointAt(manager, lane, index + 1) ?? WaypointAt(manager, lane, index - 1);
            if (current == null || next == null) return Vector3.forward;

            Vector3 flow = WaypointAt(manager, lane, index + 1) != null
                ? next.position - current.position
                : current.position - next.position;

            flow.y = 0f;
            return flow.sqrMagnitude > 0.001f ? flow.normalized : Vector3.forward;
        }

        static void MarkDirty()
        {
            if (Application.isPlaying) return;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
