using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Generates a whole city from a <see cref="CityLayout"/>: a grid of avenues, a junction
    /// wherever two cross, and the behaviour of each junction taken from the layout.
    ///
    /// The single-crossroads sample stays where it is. This is the version that scales, so the
    /// geometry is derived from the layout rather than written into constants.
    /// </summary>
    public static class TrafficCityBuilder
    {
        const string LayerName = "Vehicles";
        const float WaypointSpacing = 11f;
        const float TargetCarLength = 4.5f;

        static Material roadMaterial;
        static Material groundMaterial;
        static Material poleMaterial;
        static Material[] buildingMaterials;

        /// <summary>One lane of one avenue, kept while the junctions are wired up.</summary>
        class Avenue
        {
            public LaneConfig Lane;
            public Vector3 Direction;
            public bool NorthSouth;
            public int GridIndex;      // which avenue of its axis
        }

        public static string Build(CityLayout layout, IEnumerable<GameObject> models, string outputFolder)
        {
            if (layout == null)
            {
                EditorUtility.DisplayDialog("Traffic System", "No CityLayout assigned.", "OK");
                return null;
            }

            if (!TrafficLayerUtility.TryCreateLayer(LayerName, out string layerError))
            {
                EditorUtility.DisplayDialog("Traffic System", layerError, "OK");
                return null;
            }

            layout.EnsureGridSize();
            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateSharedMaterials();
            VehicleCharacteristics[] presets = TrafficVehiclePrefabs.Collect(models, outputFolder, LayerName, TargetCarLength);

            BuildGround(layout);
            BuildRoads(layout);
            BuildBlocks(layout);

            var manager = new GameObject("Traffic System").AddComponent<TrafficManager>();
            TrafficConfig config = manager.gameObject.AddComponent<TrafficConfig>();
            config.vehicleLayerName = LayerName;
            config.vehicleLayer = 0;
            config.useSemaforos = true;
            config.maxTrafficDensity = layout.maxTrafficDensity;
            config.tiempoVerdeSemaforo = 10f;
            config.tiempoRojoSemaforo = 8f;
            config.distanciaFrenadoSemaforo = 16f;
            config.enableDebugLogs = false;
            manager.config = config;
            manager.vehicleTypes = presets;

            var lanesRoot = new GameObject("Lanes").transform;
            List<Avenue> avenues = BuildAvenues(layout, lanesRoot);

            WireJunctions(layout, avenues);

            var lanes = new List<LaneConfig>();
            foreach (Avenue a in avenues) lanes.Add(a.Lane);
            manager.lanes = lanes.ToArray();

            new GameObject("Traffic Debug Helper").AddComponent<TrafficDebugHelper>();
            PlaceCamera(layout);

            string scenePath = outputFolder + "/TrafficSystem-City.unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Traffic System] Built {scenePath}: {layout.Columns}x{layout.Rows} junctions, " +
                      $"{lanes.Count} lanes, {presets.Length} vehicle type(s).");
            return scenePath;
        }

        // ============================== LANES ==============================

        static List<Avenue> BuildAvenues(CityLayout layout, Transform parent)
        {
            var avenues = new List<Avenue>();

            for (int column = 0; column < layout.Columns; column++)
            {
                float x = layout.JunctionPosition(column, 0).x;
                avenues.Add(BuildAvenue(layout, parent, $"ns{column}_southbound", Vector3.back, new Vector3(x, 0f, 0f), true, column));
                avenues.Add(BuildAvenue(layout, parent, $"ns{column}_northbound", Vector3.forward, new Vector3(x, 0f, 0f), true, column));
            }

            for (int row = 0; row < layout.Rows; row++)
            {
                float z = layout.JunctionPosition(0, row).z;
                avenues.Add(BuildAvenue(layout, parent, $"ew{row}_westbound", Vector3.left, new Vector3(0f, 0f, z), false, row));
                avenues.Add(BuildAvenue(layout, parent, $"ew{row}_eastbound", Vector3.right, new Vector3(0f, 0f, z), false, row));
            }

            return avenues;
        }

        static Avenue BuildAvenue(CityLayout layout, Transform parent, string laneId, Vector3 direction,
                                  Vector3 centreline, bool northSouth, int gridIndex)
        {
            var root = new GameObject("Lane_" + laneId).transform;
            root.SetParent(parent, false);

            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            float half = northSouth ? layout.HalfWidthZ : layout.HalfWidthX;

            int count = Mathf.Max(4, Mathf.RoundToInt(half * 2f / WaypointSpacing) + 1);
            float step = half * 2f / (count - 1);

            var waypoints = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                // Keep right: the lane sits to the driver's right of the avenue centreline.
                Vector3 pos = centreline - direction * half + direction * (step * i) + right * layout.laneOffset;

                bool atJunction = NearAnyJunction(layout, pos, layout.roadHalfWidth + 4f);
                var zone = atJunction ? LaneDirection.ZoneType.Intersection : LaneDirection.ZoneType.StraightLane;

                waypoints[i] = CreateWaypoint(root, $"WP_{i:00}", pos, direction, laneId, zone, 5, false);
            }

            var lane = new LaneConfig
            {
                laneId = laneId,
                activo = true,
                waypoints = waypoints,
                spawnPoint = waypoints[0],
                destroyPoints = new[] { waypoints[count - 1] },
                destroyRadius = 6f,
                cadenciaSpawn = layout.spawnCadence,
                variacionCadencia = layout.spawnCadence * 0.35f,
                radioSeguridadSpawn = 16f,
                velocidadMaxima = layout.laneSpeed
            };

            return new Avenue { Lane = lane, Direction = direction, NorthSouth = northSouth, GridIndex = gridIndex };
        }

        static bool NearAnyJunction(CityLayout layout, Vector3 position, float radius)
        {
            for (int c = 0; c < layout.Columns; c++)
            {
                for (int r = 0; r < layout.Rows; r++)
                {
                    Vector3 j = layout.JunctionPosition(c, r);
                    if (Mathf.Abs(position.x - j.x) < radius && Mathf.Abs(position.z - j.z) < radius) return true;
                }
            }
            return false;
        }

        // ============================== JUNCTIONS ==============================

        static void WireJunctions(CityLayout layout, List<Avenue> avenues)
        {
            var junctionsRoot = new GameObject("Junctions").transform;
            var turnsRoot = new GameObject("Turns").transform;

            for (int column = 0; column < layout.Columns; column++)
            {
                for (int row = 0; row < layout.Rows; row++)
                {
                    Vector3 centre = layout.JunctionPosition(column, row);
                    JunctionMode mode = layout.GetMode(column, row);

                    var approaches = new List<Avenue>();
                    foreach (Avenue a in avenues)
                    {
                        bool serves = a.NorthSouth ? a.GridIndex == column : a.GridIndex == row;
                        if (serves) approaches.Add(a);
                    }

                    var junction = new GameObject($"Junction_{column}_{row}_{mode}").transform;
                    junction.SetParent(junctionsRoot, false);
                    junction.position = centre;

                    var lights = new List<TrafficLightController>();

                    foreach (Avenue a in approaches)
                    {
                        int stopIndex = ApproachIndex(a.Lane.waypoints, centre, a.Direction, layout);
                        if (stopIndex < 1) continue;

                        if (mode == JunctionMode.Unsignalled)
                        {
                            // No lights: north-south keeps right of way and east-west gives way.
                            var dir = a.Lane.waypoints[stopIndex].GetComponent<LaneDirection>();
                            if (dir != null)
                            {
                                dir.priority = a.NorthSouth ? 3 : 7;
                                dir.requiresYield = !a.NorthSouth;
                            }
                        }
                        else
                        {
                            lights.Add(BuildTrafficLight(junction, a.Lane.laneId, a.Lane.waypoints[stopIndex - 1], a.Direction, centre, layout));
                        }

                        WireTurns(layout, turnsRoot, a, approaches, centre, stopIndex);
                    }

                    if (mode != JunctionMode.Unsignalled && lights.Count == 4)
                    {
                        var controller = junction.gameObject.AddComponent<FourWayIntersectionController>();
                        AssignLights(controller, lights);
                        bool grouped = mode == JunctionMode.OpposingPairs;
                        controller.agruparSemaforosOpuestos = grouped;

                        // One arm at a time runs four phases instead of two, so the same phase
                        // lengths would leave every approach red for four fifths of the cycle,
                        // and a vehicle crossing the city queues at one junction after another.
                        controller.tiempoVerdeSolido = grouped ? 12f : 7f;
                        controller.tiempoParpadeoVerde = grouped ? 4f : 2.5f;
                        controller.tiempoAmarillo = grouped ? 3f : 2f;
                        controller.tiempoSeguridadRojo = grouped ? 2f : 1.5f;
                    }
                }
            }
        }

        static void AssignLights(FourWayIntersectionController controller, List<TrafficLightController> lights)
        {
            foreach (TrafficLightController light in lights)
            {
                Vector3 facing = -light.transform.forward;   // the direction traffic approaches from

                if (Vector3.Dot(facing, Vector3.back) > 0.7f) controller.semaforoNorthSouth = light;
                else if (Vector3.Dot(facing, Vector3.forward) > 0.7f) controller.semaforoSouthNorth = light;
                else if (Vector3.Dot(facing, Vector3.left) > 0.7f) controller.semaforoEastWest = light;
                else controller.semaforoWestEast = light;
            }
        }

        /// <summary>Index of the first waypoint inside the junction, along this lane's travel.</summary>
        static int ApproachIndex(Transform[] waypoints, Vector3 centre, Vector3 direction, CityLayout layout)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 toCentre = waypoints[i].position - centre;
                if (Vector3.Dot(toCentre, direction) >= -layout.roadHalfWidth) return i;
            }
            return -1;
        }

        static void WireTurns(CityLayout layout, Transform turnsRoot, Avenue from, List<Avenue> approaches,
                              Vector3 centre, int stopIndex)
        {
            int decisionIndex = stopIndex - 2;
            if (decisionIndex <= 0 || stopIndex >= from.Lane.waypoints.Length - 1) return;

            Transform decisionPoint = from.Lane.waypoints[decisionIndex];
            if (decisionPoint.GetComponent<WaypointDecision>() != null) return;   // already wired

            Vector3 rightDir = Vector3.Cross(Vector3.up, from.Direction).normalized;

            Avenue toRight = Heading(approaches, rightDir);
            Avenue toLeft = Heading(approaches, -rightDir);
            if (toRight == null && toLeft == null) return;

            var decision = decisionPoint.gameObject.AddComponent<WaypointDecision>();
            decision.laneIdsPermitidos = new[] { from.Lane.laneId };
            decision.distanciaMaximaValidacion = 30f;
            decision.esInterseccionConSemaforo = true;

            decision.rutaRecto = Tail(from.Lane.waypoints, decisionIndex + 1);
            decision.laneIdRecto = from.Lane.laneId;

            if (toRight != null)
            {
                decision.rutaDerecha = BuildTurnRoute(layout, turnsRoot, from, toRight, centre);
                decision.laneIdDerecha = toRight.Lane.laneId;
            }
            if (toLeft != null)
            {
                decision.rutaIzquierda = BuildTurnRoute(layout, turnsRoot, from, toLeft, centre);
                decision.laneIdIzquierda = toLeft.Lane.laneId;
            }

            decision.probabilidadRecto = 0.5f;
            decision.probabilidadDerecha = toRight != null ? 0.25f : 0f;
            decision.probabilidadIzquierda = toLeft != null ? 0.25f : 0f;
        }

        static Avenue Heading(List<Avenue> avenues, Vector3 heading)
        {
            foreach (Avenue a in avenues)
            {
                if (Vector3.Dot(a.Direction, heading) > 0.9f) return a;
            }
            return null;
        }

        /// <summary>
        /// A curved path from one approach into the lane leaving the junction the other way,
        /// then the rest of that lane. Without the curve a turn is a jump between two straight
        /// lines and the vehicle visibly steers the wrong way first.
        /// </summary>
        static Transform[] BuildTurnRoute(CityLayout layout, Transform parent, Avenue from, Avenue to, Vector3 centre)
        {
            Vector3 entryRight = Vector3.Cross(Vector3.up, from.Direction).normalized;
            Vector3 exitRight = Vector3.Cross(Vector3.up, to.Direction).normalized;

            float reach = layout.roadHalfWidth + 5f;
            Vector3 start = centre - from.Direction * reach + entryRight * layout.laneOffset;
            Vector3 bend = centre + entryRight * layout.laneOffset + exitRight * layout.laneOffset;
            Vector3 end = centre + to.Direction * reach + exitRight * layout.laneOffset;

            var root = new GameObject($"Turn_{from.Lane.laneId}_to_{to.Lane.laneId}").transform;
            root.SetParent(parent, false);

            const int Samples = 6;
            var route = new List<Transform>();

            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                Vector3 point = Bezier(start, bend, end, t);
                Vector3 heading = (Bezier(start, bend, end, Mathf.Min(1f, t + 0.01f)) - point).normalized;
                if (heading.sqrMagnitude < 0.001f) heading = to.Direction;

                route.Add(CreateWaypoint(root, $"WP_{i:00}", point, heading, to.Lane.laneId,
                                         LaneDirection.ZoneType.TurnLane, 5, false));
            }

            // Rejoin the target lane past the junction.
            int resume = 0;
            for (int i = 0; i < to.Lane.waypoints.Length; i++)
            {
                if (Vector3.Dot(to.Lane.waypoints[i].position - centre, to.Direction) > reach) { resume = i; break; }
            }
            Transform[] tail = Tail(to.Lane.waypoints, resume);
            if (tail != null) route.AddRange(tail);

            return route.ToArray();
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }

        static Transform[] Tail(Transform[] waypoints, int from)
        {
            if (from < 0 || from >= waypoints.Length) return null;

            var tail = new Transform[waypoints.Length - from];
            System.Array.Copy(waypoints, from, tail, 0, tail.Length);
            return tail;
        }

        // ============================== TRAFFIC LIGHTS ==============================

        static TrafficLightController BuildTrafficLight(Transform parent, string laneId, Transform controlledWaypoint,
                                                        Vector3 direction, Vector3 centre, CityLayout layout)
        {
            var root = new GameObject("TrafficLight_" + laneId).transform;
            root.SetParent(parent, false);

            // Across the junction, on the far right corner, so the driver reads it head-on.
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            float setback = layout.roadHalfWidth + 2.5f;
            root.position = centre + direction * setback + right * setback;
            root.rotation = Quaternion.LookRotation(-direction);

            Block(root, "Pole", new Vector3(0f, 1.6f, 0f), new Vector3(0.3f, 3.2f, 0.3f), poleMaterial);
            Block(root, "Housing", new Vector3(0f, 4.1f, 0f), new Vector3(1.1f, 2.9f, 0.6f), poleMaterial);

            var light = root.gameObject.AddComponent<TrafficLightController>();
            light.waypointControlado = controlledWaypoint;
            light.radioDeteccion = 12f;
            light.luzRoja = Bulb(root, "Red", 5.0f, new Color(1f, 0.15f, 0.12f));
            light.luzAmarilla = Bulb(root, "Yellow", 4.1f, new Color(1f, 0.78f, 0.1f));
            light.luzVerde = Bulb(root, "Green", 3.2f, new Color(0.15f, 0.95f, 0.3f));

            return light;
        }

        static GameObject Bulb(Transform parent, string name, float height, Color color)
        {
            Material material = NewMaterial("Bulb " + name, color, 0.7f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }
            return Block(parent, name, new Vector3(0f, height, -0.36f), new Vector3(0.62f, 0.62f, 0.2f), material);
        }

        // ============================== CITY SHELL ==============================

        static void CreateSharedMaterials()
        {
            groundMaterial = NewMaterial("Ground", new Color(0.38f, 0.40f, 0.36f));
            roadMaterial = NewMaterial("Road", new Color(0.16f, 0.16f, 0.17f));
            poleMaterial = NewMaterial("TrafficLightHousing", new Color(0.12f, 0.12f, 0.13f));
            buildingMaterials = new[]
            {
                NewMaterial("Building A", new Color(0.62f, 0.60f, 0.56f)),
                NewMaterial("Building B", new Color(0.50f, 0.55f, 0.62f)),
                NewMaterial("Building C", new Color(0.66f, 0.52f, 0.46f)),
                NewMaterial("Building D", new Color(0.45f, 0.58f, 0.52f)),
            };
        }

        static Material NewMaterial(string name, Color color, float smoothness = 0.2f)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

            return material;
        }

        static GameObject Block(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = centre;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        static void BuildGround(CityLayout layout)
        {
            var root = new GameObject("Ground").transform;
            float sx = (layout.HalfWidthX + layout.blockSize) * 2f;
            float sz = (layout.HalfWidthZ + layout.blockSize) * 2f;
            Block(root, "Ground", new Vector3(0f, -0.25f, 0f), new Vector3(sx, 0.5f, sz), groundMaterial).isStatic = true;
        }

        static void BuildRoads(CityLayout layout)
        {
            var root = new GameObject("Roads").transform;
            float spanX = layout.HalfWidthX * 2f;
            float spanZ = layout.HalfWidthZ * 2f;

            for (int c = 0; c < layout.Columns; c++)
            {
                float x = layout.JunctionPosition(c, 0).x;
                Block(root, $"Avenue NS {c}", new Vector3(x, 0.02f, 0f),
                      new Vector3(layout.roadHalfWidth * 2f, 0.05f, spanZ), roadMaterial).isStatic = true;
            }
            for (int r = 0; r < layout.Rows; r++)
            {
                float z = layout.JunctionPosition(0, r).z;
                Block(root, $"Avenue EW {r}", new Vector3(0f, 0.02f, z),
                      new Vector3(spanX, 0.05f, layout.roadHalfWidth * 2f), roadMaterial).isStatic = true;
            }
        }

        static void BuildBlocks(CityLayout layout)
        {
            var root = new GameObject("City Blocks").transform;
            var rng = new System.Random(20260919);

            // A block sits between each pair of neighbouring avenues, plus a ring of outer blocks.
            for (int c = 0; c <= layout.Columns; c++)
            {
                for (int r = 0; r <= layout.Rows; r++)
                {
                    float xa = c == 0 ? -layout.HalfWidthX : layout.JunctionPosition(c - 1, 0).x;
                    float xb = c == layout.Columns ? layout.HalfWidthX : layout.JunctionPosition(c, 0).x;
                    float za = r == 0 ? -layout.HalfWidthZ : layout.JunctionPosition(0, r - 1).z;
                    float zb = r == layout.Rows ? layout.HalfWidthZ : layout.JunctionPosition(0, r).z;

                    float innerX = xa + layout.roadHalfWidth + 3f;
                    float outerX = xb - layout.roadHalfWidth - 3f;
                    float innerZ = za + layout.roadHalfWidth + 3f;
                    float outerZ = zb - layout.roadHalfWidth - 3f;
                    if (outerX - innerX < 10f || outerZ - innerZ < 10f) continue;

                    var block = new GameObject($"Block {c}_{r}").transform;
                    block.SetParent(root, false);

                    const int Grid = 2;
                    float cellX = (outerX - innerX) / Grid;
                    float cellZ = (outerZ - innerZ) / Grid;

                    for (int ix = 0; ix < Grid; ix++)
                    {
                        for (int iz = 0; iz < Grid; iz++)
                        {
                            if (rng.Next(0, 10) < 2) continue;

                            float height = 8f + (float)rng.NextDouble() * 24f;
                            float fx = cellX * (0.5f + (float)rng.NextDouble() * 0.25f);
                            float fz = cellZ * (0.5f + (float)rng.NextDouble() * 0.25f);
                            var pos = new Vector3(innerX + cellX * (ix + 0.5f), height * 0.5f, innerZ + cellZ * (iz + 0.5f));

                            Block(block, $"Building {ix}{iz}", pos, new Vector3(fx, height, fz),
                                  buildingMaterials[rng.Next(buildingMaterials.Length)]).isStatic = true;
                        }
                    }
                }
            }
        }

        static Transform CreateWaypoint(Transform parent, string name, Vector3 position, Vector3 forward,
                                        string laneId, LaneDirection.ZoneType zone, int priority, bool yield)
        {
            var wp = new GameObject(name);
            wp.transform.SetParent(parent, false);
            wp.transform.position = position;
            wp.transform.rotation = Quaternion.LookRotation(forward);

            var dir = wp.AddComponent<LaneDirection>();
            dir.laneId = laneId;
            dir.flowDirection = forward;
            dir.zoneType = zone;
            dir.priority = priority;
            dir.requiresYield = yield;

            return wp.transform;
        }

        static void PlaceCamera(CityLayout layout)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            float reach = Mathf.Max(layout.HalfWidthX, layout.HalfWidthZ);
            camera.transform.SetPositionAndRotation(
                new Vector3(0f, reach * 0.6f, -reach * 1.25f),
                Quaternion.Euler(24f, 0f, 0f));
            camera.farClipPlane = Mathf.Max(600f, reach * 6f);
        }
    }
}
