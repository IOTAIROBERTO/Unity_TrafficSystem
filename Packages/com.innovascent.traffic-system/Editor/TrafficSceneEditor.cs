using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Draws the whole traffic setup in the Scene view and lets it be edited by dragging:
    /// waypoints are handles you can move, every segment carries a + that inserts a point,
    /// every waypoint a − that deletes it, and the last one an arrow that continues the lane.
    ///
    /// It reads the scene directly, so there is no second model to keep in sync: what is drawn
    /// is what the simulation runs.
    /// </summary>
    [InitializeOnLoad]
    public static class TrafficSceneEditor
    {
        const string EnabledKey = "InnovAscent.TrafficSystem.SceneEditor.Enabled";
        const string SnapKey = "InnovAscent.TrafficSystem.SceneEditor.Snap";

        static TrafficSceneEditor()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        /// <summary>Whether the editable overlay is drawn at all.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>Grid size dragged waypoints snap to. 0 disables snapping.</summary>
        public static float Snap
        {
            get => EditorPrefs.GetFloat(SnapKey, 0f);
            set => EditorPrefs.SetFloat(SnapKey, Mathf.Max(0f, value));
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Enabled) return;
            if (TrafficLaneDesigner.IsPlacing) return; // click-to-place owns the view while drawing

            TrafficManager manager = Object.FindFirstObjectByType<TrafficManager>();
            if (manager == null) return;

            HandleVehicleDrop(manager);
            HandleJunctionPlacement(manager);

            if (manager.lanes == null) return;

            int active = ActiveLaneIndex(manager);
            for (int i = 0; i < manager.lanes.Length; i++)
            {
                DrawLane(manager, i, i == active);
            }

            DrawTrafficLights();
            DrawBranches();
            DrawCrossings(manager);
        }

        // ============================== CROSSINGS ==============================

        /// <summary>Set by the window: draws every place two lanes meet, and whether anyone yields.</summary>
        public static bool ShowCrossings { get; set; }

        /// <summary>
        /// Red where two lanes cross with nobody giving way, green where the rule is set. A crossing
        /// nothing has been said about is the one that produces two vehicles on the same metre.
        /// </summary>
        static void DrawCrossings(TrafficManager manager)
        {
            if (!ShowCrossings) return;

            foreach (TrafficCrossingTool.Crossing crossing in TrafficCrossingTool.Find(manager))
            {
                bool marked = TrafficCrossingTool.IsMarked(manager, crossing);
                float size = HandleUtility.GetHandleSize(crossing.point);

                Handles.color = marked
                    ? new Color(0.3f, 0.95f, 0.4f, 0.9f)
                    : new Color(1f, 0.25f, 0.2f, 0.9f);

                Handles.DrawWireDisc(crossing.point, Vector3.up, size * 0.5f, 3f);
                if (!marked) Handles.DrawWireDisc(crossing.point, Vector3.up, size * 0.35f, 2f);
            }
        }

        // ============================== BRANCHES ==============================

        /// <summary>
        /// Every split, drawn as one arrow per exit with the share of traffic that takes it, so the
        /// route tree is readable without opening a single inspector.
        /// </summary>
        static void DrawBranches()
        {
            WaypointDecision[] decisions = Object.FindObjectsByType<WaypointDecision>(FindObjectsSortMode.None);

            var style = new GUIStyle(EditorStyles.miniBoldLabel);
            style.normal.textColor = new Color(1f, 0.85f, 0.3f);

            foreach (WaypointDecision decision in decisions)
            {
                WaypointDecision.Branch[] branches = decision.ResolvedBranches;
                if (branches.Length < 2) continue; // a single exit is just the lane carrying on

                float total = 0f;
                foreach (WaypointDecision.Branch b in branches) total += Mathf.Max(0f, b.weight);

                Vector3 origin = decision.transform.position;
                float size = HandleUtility.GetHandleSize(origin);

                Handles.color = new Color(1f, 0.75f, 0.2f, 0.9f);
                Handles.DrawWireDisc(origin, Vector3.up, size * 0.3f, 3f);

                foreach (WaypointDecision.Branch branch in branches)
                {
                    if (branch.waypoints == null || branch.waypoints.Length == 0 || branch.waypoints[0] == null) continue;

                    Vector3 target = branch.waypoints[0].position;
                    float share = total > 0f ? Mathf.Max(0f, branch.weight) / total : 0f;

                    Handles.color = new Color(1f, 0.75f, 0.2f, 0.55f + share * 0.45f);
                    Handles.DrawLine(origin, target, 2f + share * 4f);

                    Vector3 label = Vector3.Lerp(origin, target, 0.55f) + Vector3.up * size * 0.35f;
                    Handles.Label(label, $"{branch.laneId} {share:P0}", style);
                }
            }
        }

        /// <summary>
        /// The lane the selection is inside, or -1. Only that lane gets editing handles: drawing
        /// them for every lane at once buries the roads under a wall of dots.
        /// </summary>
        static int ActiveLaneIndex(TrafficManager manager)
        {
            Transform selected = Selection.activeTransform;
            if (selected == null) return -1;

            for (int i = 0; i < manager.lanes.Length; i++)
            {
                Transform[] waypoints = manager.lanes[i].waypoints;
                if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null) continue;

                Transform root = waypoints[0].parent;
                if (selected == root || selected.IsChildOf(root)) return i;
            }

            return -1;
        }

        // ============================== DROPPING VEHICLES ==============================

        /// <summary>
        /// Accepts a vehicle preset, a prefab or a raw model dragged from the Project window onto
        /// the Scene view, and adds it to the fleet the manager spawns.
        /// </summary>
        static void HandleVehicleDrop(TrafficManager manager)
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!TrafficVehiclePalette.CanAccept(DragAndDrop.objectReferences)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int added = TrafficVehiclePalette.Accept(manager, DragAndDrop.objectReferences);
                if (added > 0) TrafficLog.Info($"{added} vehicle(s) added to the traffic fleet.");
            }

            e.Use();
        }

        // ============================== STAMPING JUNCTIONS ==============================

        /// <summary>Set by the overlay: the next Scene view click stamps a crossroads.</summary>
        public static bool PlacingJunction { get; set; }

        /// <summary>Phasing the next stamped junction runs.</summary>
        public static TrafficJunctionStamp.Phasing JunctionPhasing { get; set; } =
            TrafficJunctionStamp.Phasing.OneArmAtATime;

        /// <summary>Arm length of the next stamped junction, in metres.</summary>
        public static float JunctionArmLength { get; set; } = 45f;

        static void HandleJunctionPlacement(TrafficManager manager)
        {
            if (!PlacingJunction) return;

            Event e = Event.current;

            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            if (e.type == EventType.Repaint && TryPointUnderCursor(out Vector3 preview))
            {
                Handles.color = new Color(0.3f, 0.8f, 1f, 0.8f);
                float half = JunctionArmLength;
                Handles.DrawLine(preview - Vector3.right * half, preview + Vector3.right * half, 2f);
                Handles.DrawLine(preview - Vector3.forward * half, preview + Vector3.forward * half, 2f);
                Handles.DrawWireDisc(preview, Vector3.up, 6f, 2f);
                SceneView.RepaintAll();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                PlacingJunction = false;
                e.Use();
                return;
            }

            bool click = e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control;
            if (!click || !TryPointUnderCursor(out Vector3 centre)) return;

            TrafficJunctionStamp.Create(manager, centre, JunctionArmLength, phasing: JunctionPhasing);
            PlacingJunction = false;
            MarkDirty();
            e.Use();
        }

        static bool TryPointUnderCursor(out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
            {
                point = hit.point;
                return true;
            }

            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        // ============================== LANES ==============================

        static void DrawLane(TrafficManager manager, int laneIndex, bool editable)
        {
            LaneConfig lane = manager.lanes[laneIndex];
            if (lane.waypoints == null || lane.waypoints.Length == 0) return;

            Transform root = lane.waypoints[0] != null ? lane.waypoints[0].parent : null;
            Color color = LaneColor(lane.laneId);

            // The path and its direction arrows.
            Handles.color = lane.activo ? color : new Color(color.r, color.g, color.b, 0.25f);
            for (int i = 1; i < lane.waypoints.Length; i++)
            {
                Transform a = lane.waypoints[i - 1];
                Transform b = lane.waypoints[i];
                if (a == null || b == null) continue;

                Handles.DrawLine(a.position, b.position, 4f);
                DrawFlowArrow(a.position, b.position);
            }

            // Spawn point, in green, and the end of the route, in red.
            Transform first = lane.waypoints[0];
            Transform last = lane.waypoints[lane.waypoints.Length - 1];
            if (first != null)
            {
                Handles.color = new Color(0.2f, 0.95f, 0.35f, 0.9f);
                Handles.DrawWireDisc(first.position, Vector3.up, HandleUtility.GetHandleSize(first.position) * 0.45f, 3f);
            }
            if (last != null)
            {
                Handles.color = new Color(1f, 0.3f, 0.25f, 0.9f);
                Handles.DrawWireDisc(last.position, Vector3.up, HandleUtility.GetHandleSize(last.position) * 0.45f, 3f);
            }

            DrawLaneLabel(lane, first, editable);

            if (!editable)
            {
                DrawPickHandles(lane, color);
                return;
            }

            DrawWaypointHandles(manager, lane, root, color);
            DrawInsertButtons(manager, lane, root);
            DrawExtendButton(manager, laneIndex, last);
        }

        /// <summary>
        /// Small dots on an inactive lane: clicking one selects that waypoint, which makes its
        /// lane the active one and brings out the editing handles.
        /// </summary>
        static void DrawPickHandles(LaneConfig lane, Color color)
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.55f);

            foreach (Transform wp in lane.waypoints)
            {
                if (wp == null) continue;

                float size = HandleUtility.GetHandleSize(wp.position) * 0.05f;
                if (!Handles.Button(wp.position, Quaternion.identity, size, size * 2f, Handles.DotHandleCap)) continue;

                Selection.activeGameObject = wp.gameObject;
                GUIUtility.ExitGUI();
            }
        }

        static void DrawLaneLabel(LaneConfig lane, Transform first, bool editable)
        {
            if (first == null) return;

            var style = new GUIStyle(editable ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel);
            style.normal.textColor = editable ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            Handles.Label(first.position + Vector3.up * 2.2f,
                $"{lane.laneId}  ({lane.waypoints.Length} wp, {lane.velocidadMaxima:0} km/h)", style);
        }

        static void DrawFlowArrow(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            if (direction.sqrMagnitude < 0.01f) return;

            Vector3 middle = (from + to) * 0.5f;
            float size = HandleUtility.GetHandleSize(middle) * 0.12f;
            Handles.ConeHandleCap(0, middle, Quaternion.LookRotation(direction.normalized), size, EventType.Repaint);
        }

        static void DrawWaypointHandles(TrafficManager manager, LaneConfig lane, Transform root, Color color)
        {
            for (int i = 0; i < lane.waypoints.Length; i++)
            {
                Transform wp = lane.waypoints[i];
                if (wp == null) continue;

                float size = HandleUtility.GetHandleSize(wp.position) * 0.07f;

                Handles.color = color;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(wp.position, size, SnapVector(), Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wp, "Move waypoint");
                    wp.position = ApplySnap(moved);
                    TrafficLaneDesigner.OrientLane(root);
                    MarkDirty();
                }

                DrawDeleteButton(manager, lane, root, wp, i, size);
            }
        }

        static void DrawDeleteButton(TrafficManager manager, LaneConfig lane, Transform root, Transform wp, int index, float size)
        {
            if (lane.waypoints.Length <= 2) return; // a lane needs a start and an end

            // Beside the road rather than above it, so it stays distinct in a top-down view.
            Vector3 side = Vector3.Cross(Vector3.up, wp.forward).normalized;
            Vector3 position = wp.position + side * size * 7f;
            Handles.color = new Color(1f, 0.35f, 0.3f, 0.9f);
            if (!Handles.Button(position, Quaternion.identity, size * 1.6f, size * 2f, Handles.SphereHandleCap)) return;

            Undo.DestroyObjectImmediate(wp.gameObject);
            TrafficLaneDesigner.RenameWaypoints(root);
            TrafficLaneDesigner.OrientLane(root);
            TrafficLaneDesigner.RebuildLaneConfig(manager, lane.laneId, root);
            MarkDirty();
            GUIUtility.ExitGUI();
        }

        static void DrawInsertButtons(TrafficManager manager, LaneConfig lane, Transform root)
        {
            if (root == null) return;

            for (int i = 1; i < lane.waypoints.Length; i++)
            {
                Transform a = lane.waypoints[i - 1];
                Transform b = lane.waypoints[i];
                if (a == null || b == null) continue;

                Vector3 middle = (a.position + b.position) * 0.5f;
                float size = HandleUtility.GetHandleSize(middle) * 0.055f;

                Handles.color = new Color(1f, 0.9f, 0.3f, 0.85f);
                if (!Handles.Button(middle, Quaternion.identity, size, size * 2f, Handles.DotHandleCap)) continue;

                TrafficLaneDesigner.CreateWaypoint(root, middle, lane.laneId, i);
                TrafficLaneDesigner.OrientLane(root);
                TrafficLaneDesigner.RebuildLaneConfig(manager, lane.laneId, root);
                MarkDirty();
                GUIUtility.ExitGUI();
            }
        }

        static void DrawExtendButton(TrafficManager manager, int laneIndex, Transform last)
        {
            if (last == null) return;

            float size = HandleUtility.GetHandleSize(last.position) * 0.6f;
            Vector3 position = last.position + last.forward * size * 0.6f;

            Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            if (!Handles.Button(position, last.rotation, size, size * 0.6f, Handles.ArrowHandleCap)) return;

            TrafficLaneDesigner.ResumeLane(manager, laneIndex);
            GUIUtility.ExitGUI();
        }

        // ============================== TRAFFIC LIGHTS ==============================

        static void DrawTrafficLights()
        {
            TrafficLightController[] lights =
                Object.FindObjectsByType<TrafficLightController>(FindObjectsSortMode.None);

            foreach (TrafficLightController light in lights)
            {
                if (light == null || light.waypointControlado == null) continue;

                Handles.color = new Color(1f, 0.6f, 0.1f, 0.8f);
                Handles.DrawDottedLine(light.transform.position, light.waypointControlado.position, 3f);
                Handles.DrawWireDisc(light.waypointControlado.position, Vector3.up, light.radioDeteccion, 2f);
            }
        }

        // ============================== HELPERS ==============================

        /// <summary>A stable colour per lane id, so lanes stay visually distinct without config.</summary>
        static Color LaneColor(string laneId)
        {
            int hash = string.IsNullOrEmpty(laneId) ? 0 : laneId.GetHashCode();
            float hue = Mathf.Abs(hash % 1000) / 1000f;
            return Color.HSVToRGB(hue, 0.65f, 1f);
        }

        static Vector3 SnapVector()
        {
            float snap = Snap;
            return snap > 0f ? new Vector3(snap, snap, snap) : Vector3.zero;
        }

        static Vector3 ApplySnap(Vector3 position)
        {
            float snap = Snap;
            if (snap <= 0f) return position;

            return new Vector3(
                Mathf.Round(position.x / snap) * snap,
                position.y,
                Mathf.Round(position.z / snap) * snap);
        }

        static void MarkDirty()
        {
            if (Application.isPlaying) return;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
