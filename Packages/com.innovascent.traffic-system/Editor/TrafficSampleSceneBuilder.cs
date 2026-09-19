using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Builds a runnable block-city sample scene: ground, a ring road around four city blocks
    /// of building cubes, a signalled crossroads in the middle, and traffic circulating on both.
    ///
    /// Vehicles come from the <see cref="VehicleCharacteristics"/> presets already in the project.
    /// When there are none, any selected model asset is wrapped into a pivot-corrected prefab by
    /// measuring its renderer bounds, and failing that the scene falls back to box cars, so the
    /// generator always produces something that runs.
    /// </summary>
    public static class TrafficSampleSceneBuilder
    {
        const string SceneName = "TrafficSystem-SampleScene";
        const string OutputFolder = "Assets/" + SceneName;
        const string ScenePath = OutputFolder + "/" + SceneName + ".unity";
        const string LayerName = "Vehicles";

        // ---- city geometry, in metres ----------------------------------------------------
        const float RingHalf = 55f;      // ring road centreline distance from the centre
        const float LaneOffset = 3.5f;   // half a carriageway: distance from centreline to a lane
        const float RoadHalfWidth = 7f;
        const float ApproachLength = 22f; // how far the crossroads arms reach past the ring
        const float TargetCarLength = 4.5f;

        static Material roadMaterial;
        static Material groundMaterial;
        static Material poleMaterial;

        [MenuItem("Tools/InnovAscent/Traffic System/Build Sample Scene")]
        static void BuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Traffic System",
                    "Build the block-city sample scene?\n\n" +
                    "Ground, four blocks of buildings, a ring road, a signalled crossroads and " +
                    "circulating traffic.\n\n" +
                    $"The current scene is replaced and the result is saved to {ScenePath}.",
                    "Build", "Cancel")) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Build();
        }

        /// <summary>
        /// Builds and saves the sample scene, replacing whatever is open. Asks nothing, so it can
        /// be driven from a script; the menu entry wraps it with the confirmation prompts.
        /// </summary>
        public static void Build() => Build(null);

        /// <summary>
        /// Builds and saves the sample scene. <paramref name="models"/> wins when supplied;
        /// otherwise existing presets, then the current selection, then box cars.
        /// </summary>
        public static void Build(IEnumerable<GameObject> models)
        {
            if (!TrafficLayerUtility.TryCreateLayer(LayerName, out string layerError))
            {
                EditorUtility.DisplayDialog("Traffic System", layerError, "OK");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateSharedMaterials();
            VehicleCharacteristics[] presets = CollectVehiclePresets(models);

            BuildGround();
            BuildRoads();
            BuildCityBlocks();

            var manager = new GameObject("Traffic System").AddComponent<TrafficManager>();
            TrafficConfig config = manager.gameObject.AddComponent<TrafficConfig>();
            config.vehicleLayerName = LayerName;
            config.vehicleLayer = 0;          // derived from the layer name at startup
            config.useSemaforos = true;
            config.maxTrafficDensity = 12;
            config.tiempoVerdeSemaforo = 10f;
            config.tiempoRojoSemaforo = 8f;
            config.distanciaFrenadoSemaforo = 16f;
            config.enableDebugLogs = false;
            manager.config = config;
            manager.vehicleTypes = presets;

            var lanes = new List<LaneConfig>();
            var lights = new List<TrafficLightController>();
            var lanesRoot = new GameObject("Lanes").transform;

            // Crossroads: four signalled arms meeting at the origin. Right-hand traffic, so each
            // lane sits on Cross(up, direction) — the driver's right — of its avenue centreline.
            var avenues = new List<LaneConfig>();
            var avenueDirections = new List<Vector3> { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
            var avenueIds = new[] { "north_to_south", "south_to_north", "east_to_west", "west_to_east" };

            for (int i = 0; i < avenueIds.Length; i++)
            {
                avenues.Add(BuildStraightLane(avenueIds[i], RingHalf + ApproachLength, avenueDirections[i], lanesRoot, lights));
            }

            WireCrossroadTurns(avenues, avenueDirections);
            lanes.AddRange(avenues);

            // Ring road: traffic that drives all the way around the four blocks.
            lanes.Add(BuildRingLane("ring_outer_cw", RingHalf + LaneOffset, true, lanesRoot));
            lanes.Add(BuildRingLane("ring_inner_ccw", RingHalf - LaneOffset, false, lanesRoot));

            manager.lanes = lanes.ToArray();

            var intersection = new GameObject("Crossroads Controller").AddComponent<FourWayIntersectionController>();
            intersection.transform.position = Vector3.zero;
            intersection.semaforoNorthSouth = lights[0];
            intersection.semaforoSouthNorth = lights[1];
            intersection.semaforoEastWest = lights[2];
            intersection.semaforoWestEast = lights[3];
            // One arm at a time rather than both opposing arms together. It halves throughput, but
            // it means a turning vehicle never has oncoming traffic to cross, which is the only
            // conflict a signalled crossroads cannot separate on its own.
            intersection.agruparSemaforosOpuestos = false;
            intersection.tiempoVerdeSolido = 12f;
            intersection.tiempoParpadeoVerde = 4f;
            intersection.tiempoAmarillo = 3f;
            intersection.tiempoSeguridadRojo = 2f;

            new GameObject("Traffic Debug Helper").AddComponent<TrafficDebugHelper>();

            PlaceCamera();

            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Traffic System] Built {ScenePath}: {lanes.Count} lanes, {lights.Count} traffic lights, {presets.Length} vehicle type(s).");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(ScenePath);
        }

        // ============================== MATERIALS ==============================

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

        static void CreateSharedMaterials()
        {
            groundMaterial = NewMaterial("Ground", new Color(0.38f, 0.40f, 0.36f));
            roadMaterial = NewMaterial("Road", new Color(0.16f, 0.16f, 0.17f));
            poleMaterial = NewMaterial("TrafficLightHousing", new Color(0.12f, 0.12f, 0.13f));
        }

        // ============================== CITY ==============================

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

        static void BuildGround()
        {
            var root = new GameObject("Ground").transform;
            GameObject ground = Block(root, "Ground", new Vector3(0f, -0.25f, 0f), new Vector3(240f, 0.5f, 240f), groundMaterial);
            ground.isStatic = true;
        }

        static void BuildRoads()
        {
            var root = new GameObject("Roads").transform;
            float span = (RingHalf + ApproachLength) * 2f;

            // Ring road: four strips forming a square.
            Block(root, "Ring North", new Vector3(0f, 0.02f, RingHalf), new Vector3(RingHalf * 2f + RoadHalfWidth * 2f, 0.05f, RoadHalfWidth * 2f), roadMaterial);
            Block(root, "Ring South", new Vector3(0f, 0.02f, -RingHalf), new Vector3(RingHalf * 2f + RoadHalfWidth * 2f, 0.05f, RoadHalfWidth * 2f), roadMaterial);
            Block(root, "Ring East", new Vector3(RingHalf, 0.02f, 0f), new Vector3(RoadHalfWidth * 2f, 0.05f, RingHalf * 2f), roadMaterial);
            Block(root, "Ring West", new Vector3(-RingHalf, 0.02f, 0f), new Vector3(RoadHalfWidth * 2f, 0.05f, RingHalf * 2f), roadMaterial);

            // Crossroads through the middle.
            Block(root, "Avenue NS", new Vector3(0f, 0.02f, 0f), new Vector3(RoadHalfWidth * 2f, 0.05f, span), roadMaterial);
            Block(root, "Avenue EW", new Vector3(0f, 0.02f, 0f), new Vector3(span, 0.05f, RoadHalfWidth * 2f), roadMaterial);

            foreach (Transform t in root) t.gameObject.isStatic = true;
        }

        static void BuildCityBlocks()
        {
            var root = new GameObject("City Blocks").transform;

            // One block per quadrant, between the crossroads and the ring road.
            float inner = RoadHalfWidth + 3f;
            float outer = RingHalf - RoadHalfWidth - 3f;
            float centre = (inner + outer) * 0.5f;
            float extent = outer - inner;

            var rng = new System.Random(20260919);   // fixed seed: the sample looks the same every build
            var palette = new[]
            {
                NewMaterial("Building A", new Color(0.62f, 0.60f, 0.56f)),
                NewMaterial("Building B", new Color(0.50f, 0.55f, 0.62f)),
                NewMaterial("Building C", new Color(0.66f, 0.52f, 0.46f)),
                NewMaterial("Building D", new Color(0.45f, 0.58f, 0.52f)),
            };

            int quadrant = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var block = new GameObject($"Block {++quadrant}").transform;
                    block.SetParent(root, false);
                    block.localPosition = new Vector3(sx * centre, 0f, sz * centre);

                    // A 3x3 grid of towers, with the occasional gap so it reads as a city.
                    const int Grid = 3;
                    float cell = extent / Grid;
                    for (int ix = 0; ix < Grid; ix++)
                    {
                        for (int iz = 0; iz < Grid; iz++)
                        {
                            if (rng.Next(0, 10) < 2) continue;   // empty lot

                            float height = 6f + (float)rng.NextDouble() * 22f;
                            float footprint = cell * (0.55f + (float)rng.NextDouble() * 0.25f);
                            var local = new Vector3(
                                (ix - (Grid - 1) * 0.5f) * cell,
                                height * 0.5f,
                                (iz - (Grid - 1) * 0.5f) * cell);

                            GameObject tower = Block(block, $"Building {ix}{iz}", local,
                                new Vector3(footprint, height, footprint),
                                palette[rng.Next(palette.Length)]);
                            tower.isStatic = true;
                        }
                    }
                }
            }
        }

        // ============================== LANES ==============================

        static Transform CreateWaypoint(Transform parent, string name, Vector3 position, Vector3 forward,
                                        string laneId, LaneDirection.ZoneType zone, int priority = 5, bool yield = false)
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

        /// <summary>
        /// One arm of the crossroads: a straight run through the ring road and the signalled centre.
        /// </summary>
        static LaneConfig BuildStraightLane(string laneId, float armLength, Vector3 direction,
                                            Transform parent, List<TrafficLightController> lights)
        {
            var root = new GameObject("Lane_" + laneId).transform;
            root.SetParent(parent, false);

            // Keep right: offset the lane from the avenue centreline towards the driver's right.
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 start = -direction * armLength + right * LaneOffset;

            float total = (RingHalf + ApproachLength) * 2f;
            const int Count = 15;
            float step = total / (Count - 1);

            var waypoints = new Transform[Count];
            int lightWaypoint = -1;

            for (int i = 0; i < Count; i++)
            {
                Vector3 pos = start + direction * (step * i);
                float distanceFromCentre = new Vector2(pos.x, pos.z).magnitude;

                bool atCentre = distanceFromCentre < RoadHalfWidth + 4f;
                bool atRing = Mathf.Abs(distanceFromCentre - RingHalf) < RoadHalfWidth + 4f;

                var zone = atCentre || atRing ? LaneDirection.ZoneType.Intersection : LaneDirection.ZoneType.StraightLane;

                // Ring traffic has priority over the avenues at the outer junctions, and traffic
                // going straight through the crossroads has priority over anything turning across
                // it, so the straight run carries the strongest priority of all.
                bool yield = atRing;
                int priority = atCentre ? 3 : (atRing ? 7 : 5);

                waypoints[i] = CreateWaypoint(root, $"WP_{i:00}", pos, direction, laneId, zone, priority, yield);

                if (lightWaypoint < 0 && atCentre) lightWaypoint = i;
            }

            if (lightWaypoint < 1) lightWaypoint = Count / 2;
            lights.Add(BuildTrafficLight(laneId, waypoints[lightWaypoint - 1], direction, Vector3.zero));

            return new LaneConfig
            {
                laneId = laneId,
                activo = true,
                waypoints = waypoints,
                spawnPoint = waypoints[0],
                destroyPoints = new[] { waypoints[Count - 1] },
                destroyRadius = 6f,
                cadenciaSpawn = 8f,
                variacionCadencia = 3f,
                radioSeguridadSpawn = 14f,
                velocidadMaxima = 24f
            };
        }


        /// <summary>
        /// Lets traffic turn at the crossroads instead of every lane running dead straight, and
        /// commits to the turn on approach rather than under the lights.
        /// </summary>
        static void WireCrossroadTurns(List<LaneConfig> avenues, List<Vector3> directions)
        {
            var turnsRoot = new GameObject("Turns").transform;

            for (int i = 0; i < avenues.Count; i++)
            {
                LaneConfig lane = avenues[i];
                Vector3 forward = directions[i];
                Vector3 rightDir = Vector3.Cross(Vector3.up, forward).normalized;

                int centreIndex = FirstIndexPastCentre(lane.waypoints, forward);
                int decisionIndex = centreIndex - 2;
                if (decisionIndex <= 0 || centreIndex >= lane.waypoints.Length - 1) continue;

                var decision = lane.waypoints[decisionIndex].gameObject.AddComponent<WaypointDecision>();
                decision.laneIdsPermitidos = new[] { lane.laneId };
                decision.distanciaMaximaValidacion = 30f;
                decision.esInterseccionConSemaforo = true;

                decision.rutaRecto = Tail(lane.waypoints, decisionIndex + 1);
                decision.laneIdRecto = lane.laneId;

                LaneConfig toTheRight = LaneHeading(avenues, directions, rightDir);
                if (toTheRight != null)
                {
                    decision.rutaDerecha = BuildTurnRoute(turnsRoot, lane.laneId + "_right", forward, rightDir, toTheRight);
                    decision.laneIdDerecha = toTheRight.laneId;
                }

                LaneConfig toTheLeft = LaneHeading(avenues, directions, -rightDir);
                if (toTheLeft != null)
                {
                    decision.rutaIzquierda = BuildTurnRoute(turnsRoot, lane.laneId + "_left", forward, -rightDir, toTheLeft);
                    decision.laneIdIzquierda = toTheLeft.laneId;
                }

                decision.probabilidadRecto = 0.5f;
                decision.probabilidadDerecha = 0.25f;
                decision.probabilidadIzquierda = 0.25f;
            }
        }

        /// <summary>
        /// A curved path from the approach lane into the lane heading <paramref name="exitDir"/>,
        /// followed by the rest of that lane.
        ///
        /// Without it a turn is a jump between two straight lines: the route swapped to waypoints
        /// sitting across the junction and the vehicle steered towards them, which reads as a
        /// twitch the wrong way just before the turn.
        /// </summary>
        static Transform[] BuildTurnRoute(Transform parent, string name, Vector3 entryDir, Vector3 exitDir, LaneConfig exitLane)
        {
            Vector3 entryRight = Vector3.Cross(Vector3.up, entryDir).normalized;
            Vector3 exitRight = Vector3.Cross(Vector3.up, exitDir).normalized;

            const float EntryDistance = 12f;
            const float ExitDistance = 12f;

            Vector3 start = -entryDir * EntryDistance + entryRight * LaneOffset;
            Vector3 bend = entryRight * LaneOffset + exitRight * LaneOffset;
            Vector3 end = exitDir * ExitDistance + exitRight * LaneOffset;

            var root = new GameObject("Turn_" + name).transform;
            root.SetParent(parent, false);

            const int Samples = 6;
            var arc = new List<Transform>();

            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                Vector3 point = QuadraticBezier(start, bend, end, t);
                Vector3 heading = (QuadraticBezier(start, bend, end, Mathf.Min(1f, t + 0.01f)) - point).normalized;
                if (heading.sqrMagnitude < 0.001f) heading = exitDir;

                arc.Add(CreateWaypoint(root, $"WP_{i:00}", point, heading, exitLane.laneId,
                                       LaneDirection.ZoneType.TurnLane, 5));
            }

            int resumeIndex = 0;
            for (int i = 0; i < exitLane.waypoints.Length; i++)
            {
                if (Vector3.Dot(exitLane.waypoints[i].position, exitDir) > ExitDistance) { resumeIndex = i; break; }
            }

            Transform[] tail = Tail(exitLane.waypoints, resumeIndex);
            if (tail != null) arc.AddRange(tail);

            return arc.ToArray();
        }

        static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }

        /// <summary>First waypoint at or past the middle of the crossroads, along travel.</summary>
        static int FirstIndexPastCentre(Transform[] waypoints, Vector3 forward)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (Vector3.Dot(waypoints[i].position, forward) >= 0f) return i;
            }
            return -1;
        }

        static Transform[] Tail(Transform[] waypoints, int from)
        {
            if (from < 0 || from >= waypoints.Length) return null;

            var tail = new Transform[waypoints.Length - from];
            System.Array.Copy(waypoints, from, tail, 0, tail.Length);
            return tail;
        }

        static LaneConfig LaneHeading(List<LaneConfig> avenues, List<Vector3> directions, Vector3 heading)
        {
            for (int i = 0; i < avenues.Count; i++)
            {
                if (Vector3.Dot(directions[i], heading) > 0.9f) return avenues[i];
            }
            return null;
        }

        /// <summary>
        /// A loop around the four blocks. The path starts and ends on the same side so vehicles
        /// drive the whole perimeter before being returned to the pool.
        /// </summary>
        static LaneConfig BuildRingLane(string laneId, float radius, bool clockwise, Transform parent)
        {
            var root = new GameObject("Lane_" + laneId).transform;
            root.SetParent(parent, false);

            // Corners of the ring, in travel order.
            var corners = clockwise
                ? new[]
                {
                    new Vector3(-radius, 0f, -radius), new Vector3(radius, 0f, -radius),
                    new Vector3(radius, 0f, radius), new Vector3(-radius, 0f, radius),
                    new Vector3(-radius, 0f, -radius)
                }
                : new[]
                {
                    new Vector3(-radius, 0f, radius), new Vector3(radius, 0f, radius),
                    new Vector3(radius, 0f, -radius), new Vector3(-radius, 0f, -radius),
                    new Vector3(-radius, 0f, radius)
                };

            const int PerSide = 6;
            var waypoints = new List<Transform>();
            int index = 0;

            for (int c = 0; c < corners.Length - 1; c++)
            {
                Vector3 from = corners[c];
                Vector3 to = corners[c + 1];
                Vector3 forward = (to - from).normalized;

                for (int s = 0; s < PerSide; s++)
                {
                    Vector3 pos = Vector3.Lerp(from, to, s / (float)PerSide);

                    // The avenues cut the ring at the middle of every side.
                    bool atJunction = Mathf.Abs(pos.x) < RoadHalfWidth + 4f || Mathf.Abs(pos.z) < RoadHalfWidth + 4f;
                    var zone = atJunction ? LaneDirection.ZoneType.Intersection : LaneDirection.ZoneType.StraightLane;
                    waypoints.Add(CreateWaypoint(root, $"WP_{index++:00}", pos, forward, laneId, zone, 3));
                }
            }

            // Close the loop just short of the spawn point so vehicles complete the full circuit.
            Vector3 last = Vector3.Lerp(corners[corners.Length - 2], corners[corners.Length - 1], 0.85f);
            Vector3 lastForward = (corners[corners.Length - 1] - corners[corners.Length - 2]).normalized;
            waypoints.Add(CreateWaypoint(root, $"WP_{index:00}", last, lastForward, laneId, LaneDirection.ZoneType.StraightLane, 3));

            return new LaneConfig
            {
                laneId = laneId,
                activo = true,
                waypoints = waypoints.ToArray(),
                spawnPoint = waypoints[0],
                destroyPoints = new[] { waypoints[waypoints.Count - 1] },
                destroyRadius = 6f,
                cadenciaSpawn = 7f,
                variacionCadencia = 2.5f,
                radioSeguridadSpawn = 14f,
                velocidadMaxima = 20f
            };
        }

        // ============================== TRAFFIC LIGHTS ==============================

        /// <summary>A dark housing cube carrying three small coloured cubes.</summary>
        static TrafficLightController BuildTrafficLight(string laneId, Transform controlledWaypoint, Vector3 direction, Vector3 junctionCentre)
        {
            var root = new GameObject("TrafficLight_" + laneId).transform;

            // Across the junction on the far right corner, the placement used through most of the
            // Americas: the driver reads it straight ahead while approaching, not off to the side.
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            const float CornerSetback = RoadHalfWidth + 2.5f;
            root.position = junctionCentre + direction * CornerSetback + right * CornerSetback;
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
            var material = NewMaterial("Bulb " + name, color, 0.7f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }

            // Sits proud of the housing front face so it reads from the road.
            return Block(parent, name, new Vector3(0f, height, -0.36f), new Vector3(0.62f, 0.62f, 0.2f), material);
        }

        // ============================== VEHICLES ==============================

        static VehicleCharacteristics[] CollectVehiclePresets(IEnumerable<GameObject> models)
        {
            var built = new List<VehicleCharacteristics>();

            // Explicit models win.
            if (models != null)
            {
                foreach (GameObject model in models)
                {
                    GameObject prefab = BuildVehiclePrefab(model);
                    if (prefab != null) built.Add(CreatePreset(prefab, model.name));
                }
                if (built.Count > 0) return built.ToArray();
            }

            // Then presets the project already has, as long as their prefab actually renders
            // something. A prefab whose meshes went missing spawns an invisible car and the
            // simulation looks broken for no visible reason.
            var existing = new List<VehicleCharacteristics>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(VehicleCharacteristics)))
            {
                var preset = AssetDatabase.LoadAssetAtPath<VehicleCharacteristics>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset == null || preset.prefab == null) continue;

                if (HasVisibleGeometry(preset.prefab)) existing.Add(preset);
                else Debug.LogWarning($"[Traffic System] Preset '{preset.name}' skipped: '{preset.prefab.name}' has no renderable mesh.");
            }
            if (existing.Count > 0) return existing.ToArray();

            // Then the current selection, and finally box cars so the scene always runs.
            foreach (GameObject model in SelectedModels())
            {
                GameObject prefab = BuildVehiclePrefab(model);
                if (prefab != null) built.Add(CreatePreset(prefab, model.name));
            }
            if (built.Count == 0) built.Add(CreatePreset(BuildBoxCarPrefab(), "BoxCar"));

            return built.ToArray();
        }

        /// <summary>True when the prefab has at least one mesh that will actually draw.</summary>
        static bool HasVisibleGeometry(GameObject prefab)
        {
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) return true;
            }
            foreach (SkinnedMeshRenderer skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null) return true;
            }
            return false;
        }

        static GameObject[] SelectedModels()
        {
            var models = new List<GameObject>();
            foreach (Object o in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetImporter.GetAtPath(path) is not ModelImporter) continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && !models.Contains(go)) models.Add(go);
            }
            return models.ToArray();
        }

        /// <summary>
        /// Wraps a raw model so its pivot sits on the ground at the centre of its footprint,
        /// its long axis points along +Z and its length is normalised to a traffic-sized car.
        /// </summary>
        static GameObject BuildVehiclePrefab(GameObject model)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null) return null;

            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            // Components cannot be removed from a connected prefab instance, and the model's own
            // cameras have to go, so break the link first. Meshes and materials stay referenced.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            StripAuthoringComponents(instance);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(instance);
                Debug.LogWarning($"[Traffic System] '{model.name}' has no renderers, skipped.");
                return null;
            }

            Bounds bounds = Encapsulate(renderers);

            var root = new GameObject("TrafficCar_" + model.name);
            instance.transform.SetParent(root.transform, true);

            if (bounds.size.x > bounds.size.z)
            {
                instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                bounds = Encapsulate(renderers);
            }

            // A car is roughly twice as long as it is wide. A near-square footprint means the
            // model carries geometry that is not the vehicle (a ground plane, a studio backdrop),
            // and normalising it would produce a car-sized box that blocks the whole carriageway.
            float widthRatio = Mathf.Min(bounds.size.x, bounds.size.z) / Mathf.Max(bounds.size.x, bounds.size.z, 0.001f);
            if (widthRatio > 0.6f)
            {
                Object.DestroyImmediate(root);
                Debug.LogWarning(
                    $"[Traffic System] '{model.name}' skipped: footprint is {bounds.size.x:F1} x {bounds.size.z:F1} m, " +
                    $"too square for a vehicle (width/length {widthRatio:F2}). Trim the model to the car body first.");
                return null;
            }

            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = length > 0.001f ? TargetCarLength / length : 1f;
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition *= scale;

            var box = root.AddComponent<BoxCollider>();
            box.size = bounds.size * scale;
            box.center = new Vector3(0f, box.size.y * 0.5f, 0f);

            SetLayerRecursively(root.transform, LayerMask.NameToLayer(LayerName));

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{root.name}.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>
        /// DCC exports routinely carry the authoring scene's cameras and lights. Left in place they
        /// are instantiated with every pooled vehicle: the cameras out-rank the scene camera and
        /// hijack the view, and the lights wreck performance.
        /// </summary>
        static void StripAuthoringComponents(GameObject instance)
        {
            foreach (AudioListener listener in instance.GetComponentsInChildren<AudioListener>(true)) Remove(listener);
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true)) Remove(camera);
            foreach (Light light in instance.GetComponentsInChildren<Light>(true)) Remove(light);
        }

        /// <summary>
        /// Drops a component brought in from a DCC export. Some of them refuse to be removed on
        /// their own, so when the node carries no geometry the whole GameObject goes instead, and
        /// as a last resort the component is left disabled rather than silently kept alive.
        /// </summary>
        static void Remove(Behaviour component)
        {
            if (component == null) return;

            GameObject owner = component.gameObject;
            if (owner == null) return;

            bool nodeIsOnlyForAuthoring =
                owner.transform.childCount == 0 &&
                owner.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                owner != component.transform.root.gameObject;

            if (nodeIsOnlyForAuthoring)
            {
                Object.DestroyImmediate(owner);
                return;
            }

            Object.DestroyImmediate(component);
            if (component != null) component.enabled = false;
        }

        static Bounds Encapsulate(Renderer[] renderers)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static GameObject BuildBoxCarPrefab()
        {
            var root = new GameObject("TrafficCar_Box");
            GameObject body = Block(root.transform, "Body",
                new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.4f, TargetCarLength),
                NewMaterial("BoxCar", new Color(0.8f, 0.3f, 0.25f)));
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 1.4f, TargetCarLength);
            box.center = new Vector3(0f, 0.7f, 0f);

            SetLayerRecursively(root.transform, LayerMask.NameToLayer(LayerName));

            string path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/TrafficCar_Box.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static VehicleCharacteristics CreatePreset(GameObject prefab, string displayName)
        {
            var preset = ScriptableObject.CreateInstance<VehicleCharacteristics>();
            preset.prefab = prefab;
            preset.nombreVehiculo = displayName;
            preset.pesoSpawn = 1;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/Preset_{displayName}.asset");
            AssetDatabase.CreateAsset(preset, path);
            return preset;
        }

        // ============================== MISC ==============================

        static void PlaceCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            // Frames the whole city while keeping the cars on the near ring road readable.
            camera.transform.SetPositionAndRotation(new Vector3(0f, 46f, -108f), Quaternion.Euler(24f, 0f, 0f));
            camera.farClipPlane = 600f;
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            if (layer < 0) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
