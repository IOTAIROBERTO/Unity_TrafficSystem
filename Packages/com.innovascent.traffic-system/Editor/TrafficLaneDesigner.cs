using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Draws a lane by clicking in the Scene view: every click drops a waypoint on whatever is
    /// under the cursor, and the lane's config is rebuilt from the waypoints as they appear.
    ///
    /// Authoring a lane by hand otherwise means creating empty GameObjects one at a time, naming
    /// them, orienting each one, and wiring the array by hand.
    /// </summary>
    public static class TrafficLaneDesigner
    {
        static TrafficManager manager;
        static Transform laneRoot;
        static string laneId;
        static bool placing;

        public static bool IsPlacing => placing && laneRoot != null;
        public static string ActiveLaneId => laneId;
        public static int PlacedCount => laneRoot != null ? laneRoot.childCount : 0;

        /// <summary>Raised whenever the designer changes something the window shows.</summary>
        public static System.Action Changed;

        // ============================== LANES ==============================

        public static void BeginLane(TrafficManager target, string id)
        {
            if (target == null) return;

            manager = target;
            laneId = string.IsNullOrWhiteSpace(id) ? "lane_" + (target.lanes != null ? target.lanes.Length : 0) : id.Trim();

            Transform lanesParent = FindOrCreate(target.transform, "Lanes");

            var root = new GameObject("Lane_" + laneId);
            Undo.RegisterCreatedObjectUndo(root, "Add traffic lane");
            root.transform.SetParent(lanesParent, false);
            laneRoot = root.transform;

            AppendLaneConfig();
            StartPlacing();
        }

        /// <summary>Continues an existing lane instead of starting a new one.</summary>
        public static void ResumeLane(TrafficManager target, int laneIndex)
        {
            if (target == null || target.lanes == null) return;
            if (laneIndex < 0 || laneIndex >= target.lanes.Length) return;

            LaneConfig lane = target.lanes[laneIndex];
            if (lane.waypoints == null || lane.waypoints.Length == 0 || lane.waypoints[0] == null) return;

            manager = target;
            laneId = lane.laneId;
            laneRoot = lane.waypoints[0].parent;
            StartPlacing();
        }

        public static void Finish()
        {
            placing = false;
            laneRoot = null;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        static void StartPlacing()
        {
            placing = true;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        // ============================== SCENE VIEW ==============================

        static void OnSceneGUI(SceneView view)
        {
            if (!IsPlacing)
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                return;
            }

            Event e = Event.current;

            // Take over plain left clicks so placing a waypoint does not also change the selection.
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            DrawPreview(view);

            bool placeClick = e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control;
            if (placeClick && TryPointUnderCursor(out Vector3 point))
            {
                AddWaypoint(point);
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return))
            {
                Finish();
                e.Use();
            }
        }

        static void DrawPreview(SceneView view)
        {
            if (laneRoot == null) return;

            Handles.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            for (int i = 1; i < laneRoot.childCount; i++)
            {
                Handles.DrawLine(laneRoot.GetChild(i - 1).position, laneRoot.GetChild(i).position, 3f);
            }

            if (laneRoot.childCount > 0 && TryPointUnderCursor(out Vector3 cursor))
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.8f);
                Handles.DrawDottedLine(laneRoot.GetChild(laneRoot.childCount - 1).position, cursor, 4f);
                Handles.SphereHandleCap(0, cursor, Quaternion.identity, 1.2f, EventType.Repaint);
            }

            Handles.BeginGUI();
            var rect = new Rect(12f, 12f, 320f, 54f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 18f),
                      $"Placing '{laneId}' — {PlacedCount} waypoint(s)", EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 22f),
                      "Click to add. Enter or Esc to finish.", EditorStyles.miniLabel);
            Handles.EndGUI();

            view.Repaint();
        }

        /// <summary>Whatever collider is under the cursor, falling back to the ground plane.</summary>
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

        // ============================== WAYPOINTS ==============================

        internal static void AddWaypoint(Vector3 position)
        {
            CreateWaypoint(laneRoot, position, laneId);
            OrientLane();
            RebuildLaneConfig();
            MarkDirty();
        }

        /// <summary>
        /// Creates one waypoint under a lane root. A sibling index of -1 appends it, anything else
        /// inserts it there, which is how the Scene view adds a point in the middle of a lane.
        /// </summary>
        public static Transform CreateWaypoint(Transform root, Vector3 position, string id, int siblingIndex = -1)
        {
            if (root == null) return null;

            var wp = new GameObject("WP");
            Undo.RegisterCreatedObjectUndo(wp, "Add waypoint");
            wp.transform.SetParent(root, true);
            wp.transform.position = position;
            if (siblingIndex >= 0 && siblingIndex <= root.childCount - 1) wp.transform.SetSiblingIndex(siblingIndex);

            var dir = wp.AddComponent<LaneDirection>();
            dir.laneId = id;
            dir.zoneType = LaneDirection.ZoneType.StraightLane;

            RenameWaypoints(root);
            return wp.transform;
        }

        /// <summary>Keeps the hierarchy names matching the running order after an insert or delete.</summary>
        public static void RenameWaypoints(Transform root)
        {
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++) root.GetChild(i).name = $"WP_{i:00}";
        }

        /// <summary>Points every waypoint at the next one, so the flow direction follows the path.</summary>
        public static void OrientLane(Transform root = null)
        {
            Transform target = root != null ? root : laneRoot;
            if (target == null || target.childCount == 0) return;

            for (int i = 0; i < target.childCount; i++)
            {
                Transform current = target.GetChild(i);
                Transform next = i < target.childCount - 1 ? target.GetChild(i + 1) : null;
                Transform previous = i > 0 ? target.GetChild(i - 1) : null;

                Vector3 forward = next != null
                    ? next.position - current.position
                    : (previous != null ? current.position - previous.position : Vector3.forward);

                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

                Undo.RecordObject(current, "Orient waypoints");
                current.rotation = Quaternion.LookRotation(forward.normalized);

                var dir = current.GetComponent<LaneDirection>();
                if (dir != null)
                {
                    Undo.RecordObject(dir, "Orient waypoints");
                    dir.flowDirection = forward.normalized;
                }
            }
        }

        // ============================== LANE CONFIG ==============================

        static void AppendLaneConfig()
        {
            Undo.RecordObject(manager, "Add traffic lane");

            var lanes = new List<LaneConfig>(manager.lanes ?? new LaneConfig[0]);
            lanes.Add(new LaneConfig
            {
                laneId = laneId,
                activo = true,
                waypoints = new Transform[0],
                destroyPoints = new Transform[0],
                destroyRadius = 5f,
                cadenciaSpawn = 5f,
                variacionCadencia = 1.5f,
                radioSeguridadSpawn = 14f,
                velocidadMaxima = 30f
            });
            manager.lanes = lanes.ToArray();
        }

        /// <summary>
        /// Rebuilds the lane's arrays from the waypoints in the hierarchy, so reordering or
        /// deleting children in the Hierarchy window is enough to change the lane.
        /// </summary>
        public static void RebuildLaneConfig(TrafficManager target = null, string id = null, Transform root = null)
        {
            TrafficManager m = target != null ? target : manager;
            string wanted = id ?? laneId;
            Transform source = root != null ? root : laneRoot;

            if (m == null || m.lanes == null || source == null) return;

            var waypoints = new Transform[source.childCount];
            for (int i = 0; i < source.childCount; i++) waypoints[i] = source.GetChild(i);

            for (int i = 0; i < m.lanes.Length; i++)
            {
                if (m.lanes[i].laneId != wanted) continue;

                Undo.RecordObject(m, "Update traffic lane");
                m.lanes[i].waypoints = waypoints;
                m.lanes[i].spawnPoint = waypoints.Length > 0 ? waypoints[0] : null;
                m.lanes[i].destroyPoints = waypoints.Length > 0
                    ? new[] { waypoints[waypoints.Length - 1] }
                    : new Transform[0];
                break;
            }
        }

        public static void RemoveLane(TrafficManager target, int laneIndex)
        {
            if (target == null || target.lanes == null) return;
            if (laneIndex < 0 || laneIndex >= target.lanes.Length) return;

            LaneConfig lane = target.lanes[laneIndex];
            Transform root = lane.waypoints != null && lane.waypoints.Length > 0 && lane.waypoints[0] != null
                ? lane.waypoints[0].parent
                : null;

            var lanes = new List<LaneConfig>(target.lanes);
            lanes.RemoveAt(laneIndex);

            Undo.RecordObject(target, "Remove traffic lane");
            target.lanes = lanes.ToArray();

            if (root != null) Undo.DestroyObjectImmediate(root.gameObject);

            if (IsPlacing && lane.laneId == laneId) Finish();
            MarkDirty();
        }

        // ============================== TRAFFIC LIGHTS ==============================

        /// <summary>
        /// Puts a light on the kerb across the junction to the right of the approach, which is
        /// where a driver reads it, and points it back at the traffic it governs.
        /// </summary>
        public static TrafficLightController AddTrafficLight(TrafficManager target, Transform controlledWaypoint)
        {
            if (target == null || controlledWaypoint == null) return null;

            Vector3 forward = controlledWaypoint.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            var root = new GameObject("TrafficLight_" + controlledWaypoint.name);
            Undo.RegisterCreatedObjectUndo(root, "Add traffic light");
            root.transform.SetParent(FindOrCreate(target.transform, "Traffic Lights"), false);
            root.transform.position = controlledWaypoint.position + right * 5.5f;
            root.transform.rotation = Quaternion.LookRotation(-forward);

            Material housing = SimpleMaterial("TrafficLightHousing", new Color(0.12f, 0.12f, 0.13f));
            Cube(root.transform, "Pole", new Vector3(0f, 1.6f, 0f), new Vector3(0.3f, 3.2f, 0.3f), housing);
            Cube(root.transform, "Housing", new Vector3(0f, 4.1f, 0f), new Vector3(1.1f, 2.9f, 0.6f), housing);

            var light = root.AddComponent<TrafficLightController>();
            light.waypointControlado = controlledWaypoint;
            light.radioDeteccion = 12f;
            light.luzRoja = Bulb(root.transform, "Red", 5.0f, new Color(1f, 0.15f, 0.12f));
            light.luzAmarilla = Bulb(root.transform, "Yellow", 4.1f, new Color(1f, 0.78f, 0.1f));
            light.luzVerde = Bulb(root.transform, "Green", 3.2f, new Color(0.15f, 0.95f, 0.3f));

            MarkDirty();
            return light;
        }

        // ============================== HELPERS ==============================

        static Transform FindOrCreate(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        static GameObject Cube(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            return cube;
        }

        static GameObject Bulb(Transform parent, string name, float height, Color color)
        {
            Material material = SimpleMaterial("Bulb " + name, color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }
            return Cube(parent, name, new Vector3(0f, height, -0.36f), new Vector3(0.62f, 0.62f, 0.2f), material);
        }

        static Material SimpleMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            return material;
        }

        static void MarkDirty()
        {
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            Changed?.Invoke();
        }
    }
}
