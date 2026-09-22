using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Single entry point for setting up and tuning the traffic system in a host project.
    /// Setup tab: checklist with one-click fixes. Config tab: live traffic settings.
    /// </summary>
    public class TrafficSystemWindow : EditorWindow
    {
        enum Tab { Setup, Design, Config, City }

        static readonly string[] TabLabels = { "Setup", "Design", "Config", "City" };

        Tab tab = Tab.Setup;
        Vector2 scroll;
        TrafficManager manager;
        UnityEditor.Editor configEditor;
        TrafficConfig editedConfig;
        Material modelMaterial;
        CityLayout cityLayout;
        string newLaneId = "";

        [MenuItem("Tools/InnovAscent/Traffic System")]
        public static void Open()
        {
            var window = GetWindow<TrafficSystemWindow>("Traffic System");
            window.minSize = new Vector2(380f, 460f);
            window.Show();
        }

        void OnEnable()
        {
            RefreshManager();
            TrafficLaneDesigner.Changed += Repaint;
        }

        void OnDisable()
        {
            DestroyConfigEditor();
            TrafficLaneDesigner.Changed -= Repaint;
        }

        void OnHierarchyChange()
        {
            RefreshManager();
            Repaint();
        }

        void RefreshManager()
        {
            manager = FindFirstObjectByType<TrafficManager>(FindObjectsInactive.Include);
        }

        void OnGUI()
        {
            if (manager == null) RefreshManager();

            tab = (Tab)GUILayout.Toolbar((int)tab, TabLabels);
            EditorGUILayout.Space(6f);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == Tab.Setup) DrawSetupTab();
            else if (tab == Tab.Design) DrawDesignTab();
            else if (tab == Tab.Config) DrawConfigTab();
            else DrawCityTab();
            EditorGUILayout.EndScrollView();
        }

        // ============================== DESIGN TAB ==============================

        void DrawDesignTab()
        {
            if (manager == null)
            {
                EditorGUILayout.HelpBox("No TrafficManager in the scene. Create one from the Setup tab first.", MessageType.Info);
                if (GUILayout.Button("Go to Setup")) tab = Tab.Setup;
                return;
            }

            if (TrafficLaneDesigner.IsPlacing)
            {
                EditorGUILayout.HelpBox(
                    $"Drawing '{TrafficLaneDesigner.ActiveLaneId}'. Click in the Scene view to drop a waypoint; " +
                    $"they are placed on whatever is under the cursor, or on the ground plane.\n\n" +
                    $"{TrafficLaneDesigner.PlacedCount} waypoint(s) so far. Enter or Esc finishes.",
                    MessageType.Info);

                if (GUILayout.Button("Finish lane", GUILayout.Height(28f))) TrafficLaneDesigner.Finish();
                EditorGUILayout.Space(8f);
            }
            else
            {
                EditorGUILayout.LabelField("New lane", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    newLaneId = EditorGUILayout.TextField("Lane id", newLaneId);
                    if (GUILayout.Button("Draw", GUILayout.Width(70f), GUILayout.Height(20f)))
                    {
                        TrafficLaneDesigner.BeginLane(manager, newLaneId);
                        newLaneId = "";
                    }
                }
                EditorGUILayout.LabelField(
                    "The first waypoint becomes the spawn point and the last the destroy point.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(8f);
            }

            DrawLaneList();
            EditorGUILayout.Space(10f);
            DrawBranchTool();
            EditorGUILayout.Space(10f);
            DrawJunctionTool();
            EditorGUILayout.Space(10f);
            DrawTrafficLightTool();
            EditorGUILayout.Space(10f);
            DrawFleetPalette();
        }

        /// <summary>
        /// A preset with no prefab cannot spawn anything, and used to surface only at runtime as a
        /// pool failure. Name the asset here, with a way to fix or drop it.
        /// </summary>
        void DrawBrokenPresets()
        {
            if (manager.vehicleTypes == null) return;

            for (int i = 0; i < manager.vehicleTypes.Length; i++)
            {
                VehicleCharacteristics preset = manager.vehicleTypes[i];
                bool empty = preset == null;
                if (!empty && preset.prefab != null) continue;

                string message = empty
                    ? $"Vehicle preset slot {i} is empty. It cannot spawn anything."
                    : $"Vehicle preset '{preset.name}' has no prefab assigned. It cannot spawn anything.";
                EditorGUILayout.HelpBox(message, MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(empty))
                    {
                        if (GUILayout.Button("Select the preset", GUILayout.Width(130f)))
                        {
                            Selection.activeObject = preset;
                            EditorGUIUtility.PingObject(preset);
                        }
                    }

                    if (GUILayout.Button("Remove from the manager", GUILayout.Width(180f)))
                    {
                        var types = new List<VehicleCharacteristics>(manager.vehicleTypes);
                        types.RemoveAt(i);
                        Undo.RecordObject(manager, "Remove vehicle preset");
                        manager.vehicleTypes = types.ToArray();
                        EditorUtility.SetDirty(manager);
                        return;
                    }
                }
            }
        }

        string branchTargetLaneId = "";
        float branchWeight = 1f;

        /// <summary>
        /// Splitting a lane part-way along it. Repeating this on one waypoint stacks exits, which
        /// is how a lane fans out into three, four or more.
        /// </summary>
        void DrawBranchTool()
        {
            EditorGUILayout.LabelField("Branch", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Select the waypoint the branch leaves from, pick where it goes, and traffic reaching it " +
                "chooses between carrying on and taking the exit. Repeat on the same waypoint for more exits.",
                EditorStyles.wordWrappedMiniLabel);

            Transform selected = Selection.activeTransform;
            bool isWaypoint = selected != null && selected.GetComponent<LaneDirection>() != null;

            if (!isWaypoint)
            {
                EditorGUILayout.HelpBox("Select a waypoint in the scene.", MessageType.None);
                return;
            }

            string[] laneIds = LaneIds();
            if (laneIds.Length < 2)
            {
                EditorGUILayout.HelpBox("Branching needs at least two lanes.", MessageType.None);
                return;
            }

            int current = Mathf.Max(0, System.Array.IndexOf(laneIds, branchTargetLaneId));
            current = EditorGUILayout.Popup("Goes to", current, laneIds);
            branchTargetLaneId = laneIds[current];

            branchWeight = EditorGUILayout.Slider("Take the exit", branchWeight, 0.1f, 5f);
            EditorGUILayout.LabelField(
                $"Weight {branchWeight:0.0} against 1.0 for carrying on — about {branchWeight / (1f + branchWeight):P0} of traffic takes it.",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button($"Branch '{selected.name}' to '{branchTargetLaneId}'", GUILayout.Height(24f)))
            {
                if (TrafficBranchTool.Branch(manager, selected, branchTargetLaneId, branchWeight, out string error))
                {
                    SceneView.RepaintAll();
                }
                else
                {
                    EditorUtility.DisplayDialog("Traffic System", error, "OK");
                }
            }

            WaypointDecision.Branch[] existing = TrafficBranchTool.BranchesOn(selected);
            if (existing.Length == 0) return;

            float total = 0f;
            foreach (WaypointDecision.Branch b in existing) total += Mathf.Max(0f, b.weight);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Exits from '{selected.name}' ({existing.Length})", EditorStyles.miniBoldLabel);
            foreach (WaypointDecision.Branch b in existing)
            {
                float share = total > 0f ? Mathf.Max(0f, b.weight) / total : 0f;
                EditorGUILayout.LabelField(
                    $"    → {b.laneId}   {share:P0}   ({b.waypoints.Length} wp)", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Remove every exit from this waypoint"))
            {
                TrafficBranchTool.ClearBranches(selected);
                SceneView.RepaintAll();
            }
        }

        string[] LaneIds()
        {
            if (manager.lanes == null) return new string[0];

            var ids = new List<string>(manager.lanes.Length);
            foreach (LaneConfig lane in manager.lanes)
            {
                if (!string.IsNullOrEmpty(lane.laneId)) ids.Add(lane.laneId);
            }

            return ids.ToArray();
        }

        void DrawJunctionTool()
        {
            EditorGUILayout.LabelField("Junction", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Stamps four approach lanes, a traffic light on each and a FourWayIntersectionController wired to all four.",
                EditorStyles.wordWrappedMiniLabel);

            TrafficSceneEditor.JunctionArmLength = EditorGUILayout.FloatField(
                "Arm length (m)", TrafficSceneEditor.JunctionArmLength);
            TrafficSceneEditor.JunctionPhasing = (TrafficJunctionStamp.Phasing)EditorGUILayout.EnumPopup(
                "Phasing", TrafficSceneEditor.JunctionPhasing);

            bool placing = TrafficSceneEditor.PlacingJunction;
            if (GUILayout.Button(placing ? "Click in the Scene view — Esc cancels" : "Place a junction",
                    GUILayout.Height(24f)))
            {
                TrafficSceneEditor.PlacingJunction = !placing;
                SceneView.RepaintAll();
            }
        }

        void DrawFleetPalette()
        {
            EditorGUILayout.LabelField("Fleet", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Click a car to add or remove it from the traffic. Boxed cars are the ones that spawn. " +
                "Dragging a prefab from the Project window onto the Scene view builds a preset for it.",
                EditorStyles.wordWrappedMiniLabel);

            TrafficVehiclePalette.DrawGrid(manager);
        }

        void DrawLaneList()
        {
            int laneCount = manager.lanes != null ? manager.lanes.Length : 0;
            EditorGUILayout.LabelField($"Lanes ({laneCount})", EditorStyles.boldLabel);

            if (laneCount == 0)
            {
                EditorGUILayout.HelpBox("No lanes yet. Give one a name and press Draw.", MessageType.None);
                return;
            }

            for (int i = 0; i < laneCount; i++)
            {
                LaneConfig lane = manager.lanes[i];
                int waypoints = lane.waypoints != null ? lane.waypoints.Length : 0;

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{lane.laneId}", GUILayout.MinWidth(80f));
                    EditorGUILayout.LabelField($"{waypoints} wp", GUILayout.Width(50f));

                    using (new EditorGUI.DisabledScope(waypoints == 0))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(55f)) && lane.waypoints[0] != null)
                        {
                            Selection.activeGameObject = lane.waypoints[0].parent.gameObject;
                            SceneView.FrameLastActiveSceneView();
                        }
                        if (GUILayout.Button("Extend", GUILayout.Width(58f))) TrafficLaneDesigner.ResumeLane(manager, i);
                        if (GUILayout.Button("Re-orient", GUILayout.Width(70f)) && lane.waypoints[0] != null)
                        {
                            TrafficLaneDesigner.OrientLane(lane.waypoints[0].parent);
                        }
                    }

                    if (GUILayout.Button("X", GUILayout.Width(22f)) &&
                        EditorUtility.DisplayDialog("Traffic System",
                            $"Delete lane '{lane.laneId}' and its {waypoints} waypoint(s)?", "Delete", "Cancel"))
                    {
                        TrafficLaneDesigner.RemoveLane(manager, i);
                        return;
                    }
                }
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Rebuild every lane from the hierarchy"))
            {
                foreach (LaneConfig lane in manager.lanes)
                {
                    if (lane.waypoints != null && lane.waypoints.Length > 0 && lane.waypoints[0] != null)
                    {
                        TrafficLaneDesigner.RebuildLaneConfig(manager, lane.laneId, lane.waypoints[0].parent);
                    }
                }
            }
            EditorGUILayout.LabelField(
                "Use that after reordering or deleting waypoints in the Hierarchy.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawTrafficLightTool()
        {
            EditorGUILayout.LabelField("Traffic light", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Select the waypoint where traffic should stop. The light goes on the kerb to its right, facing the oncoming lane.",
                EditorStyles.wordWrappedMiniLabel);

            Transform selected = Selection.activeTransform;
            bool isWaypoint = selected != null && selected.GetComponent<LaneDirection>() != null;

            using (new EditorGUI.DisabledScope(!isWaypoint))
            {
                string label = isWaypoint
                    ? $"Add traffic light at '{selected.name}'"
                    : "Select a waypoint in the scene";

                if (GUILayout.Button(label, GUILayout.Height(24f)))
                {
                    TrafficLightController light = TrafficLaneDesigner.AddTrafficLight(manager, selected);
                    if (light != null) Selection.activeGameObject = light.gameObject;
                }
            }

            int lightCount = FindObjectsByType<TrafficLightController>(FindObjectsSortMode.None).Length;
            EditorGUILayout.LabelField($"{lightCount} traffic light(s) in the scene", EditorStyles.miniLabel);

            if (lightCount >= 4)
            {
                EditorGUILayout.HelpBox(
                    "With four lights on one junction, add a FourWayIntersectionController to a " +
                    "GameObject at the crossing and assign them to run a phased cycle.",
                    MessageType.None);
            }
        }

        // ============================== CITY TAB ==============================

        const string CityOutputFolder = "Assets/TrafficSystem-City";

        static readonly string[] JunctionModeLabels = { "1 arm", "Pairs", "No light" };

        void DrawCityTab()
        {
            EditorGUILayout.LabelField("City layout", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A grid of avenues with a junction wherever two cross. Each junction carries its " +
                "own rule, so one city can mix signalled and give-way crossings.",
                MessageType.None);

            cityLayout = (CityLayout)EditorGUILayout.ObjectField("Layout", cityLayout, typeof(CityLayout), false);

            if (cityLayout == null)
            {
                if (GUILayout.Button("Create a layout asset", GUILayout.Height(24f))) CreateCityLayout();
                return;
            }

            EditorGUI.BeginChangeCheck();

            cityLayout.avenuesNorthSouth = EditorGUILayout.IntSlider("Avenues north-south", cityLayout.avenuesNorthSouth, 1, 5);
            cityLayout.avenuesEastWest = EditorGUILayout.IntSlider("Avenues east-west", cityLayout.avenuesEastWest, 1, 5);
            cityLayout.blockSize = EditorGUILayout.Slider("Block size (m)", cityLayout.blockSize, 45f, 160f);
            cityLayout.maxTrafficDensity = EditorGUILayout.IntSlider("Max vehicles", cityLayout.maxTrafficDensity, 4, 60);
            cityLayout.laneSpeed = EditorGUILayout.Slider("Lane speed (km/h)", cityLayout.laneSpeed, 10f, 60f);
            cityLayout.spawnCadence = EditorGUILayout.Slider("Spawn every (s)", cityLayout.spawnCadence, 2f, 15f);

            cityLayout.EnsureGridSize();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Junctions ({cityLayout.Columns} x {cityLayout.Rows})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Click a junction to change how it resolves traffic.", EditorStyles.miniLabel);

            DrawJunctionGrid();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(cityLayout);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                $"1 arm: one approach green at a time, turns always safe.\n" +
                $"Pairs: opposing approaches green together, twice the flow.\n" +
                $"No light: north-south has right of way, east-west gives way.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                $"Select vehicle models in the Project window to use them; otherwise the build " +
                $"falls back to the project's presets, then to box cars.\n\n" +
                $"The scene is saved to {CityOutputFolder}.",
                MessageType.None);

            if (GUILayout.Button("Build city scene", GUILayout.Height(28f))) BuildCity();
        }

        /// <summary>The grid is drawn north at the top, so it matches the scene seen from above.</summary>
        void DrawJunctionGrid()
        {
            for (int row = cityLayout.Rows - 1; row >= 0; row--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < cityLayout.Columns; column++)
                    {
                        JunctionMode mode = cityLayout.GetMode(column, row);
                        var content = new GUIContent(JunctionModeLabels[(int)mode], $"Junction {column},{row}");

                        if (GUILayout.Button(content, GUILayout.Height(30f), GUILayout.MinWidth(62f)))
                        {
                            cityLayout.SetMode(column, row, (JunctionMode)(((int)mode + 1) % 3));
                        }
                    }
                }
            }
        }

        void CreateCityLayout()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "New city layout", "CityLayout", "asset", "Where should the layout be saved?");
            if (string.IsNullOrEmpty(path)) return;

            var layout = CreateInstance<CityLayout>();
            layout.EnsureGridSize();
            AssetDatabase.CreateAsset(layout, path);
            AssetDatabase.SaveAssets();

            cityLayout = layout;
            Selection.activeObject = layout;
        }

        void BuildCity()
        {
            if (!EditorUtility.DisplayDialog(
                    "Traffic System",
                    $"Build a {cityLayout.Columns} x {cityLayout.Rows} junction city?\n\n" +
                    $"The current scene is replaced and the result is saved to {CityOutputFolder}.",
                    "Build", "Cancel")) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string scenePath = TrafficCityBuilder.Build(cityLayout, TrafficVehiclePrefabs.SelectedModels(), CityOutputFolder);
            if (string.IsNullOrEmpty(scenePath)) return;

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(scenePath);
        }

        // ============================== SETUP TAB ==============================

        void DrawSetupTab()
        {
            EditorGUILayout.LabelField("Scene checklist", EditorStyles.boldLabel);

            if (GUILayout.Button("Fix all", GUILayout.Height(24f))) FixAll();
            EditorGUILayout.LabelField(
                "Creates the manager, the config, the vehicle layer and its mask in one go.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            bool hasManager = manager != null;
            DrawCheck(
                hasManager,
                hasManager ? $"TrafficManager on '{manager.gameObject.name}'" : "No TrafficManager in the scene",
                hasManager ? null : "Create",
                CreateTrafficSystemObject);

            if (!hasManager)
            {
                EditorGUILayout.HelpBox(
                    "Create the traffic system object first. Everything else hangs off it.",
                    MessageType.Info);
                DrawFooter();
                return;
            }

            TrafficConfig config = manager.config;
            DrawCheck(
                config != null,
                config != null ? "TrafficConfig assigned" : "TrafficManager has no TrafficConfig",
                config != null ? null : "Create & assign",
                CreateAndAssignConfig);

            if (config != null)
            {
                string layerName = string.IsNullOrEmpty(config.vehicleLayerName) ? "Vehicles" : config.vehicleLayerName;
                bool layerOk = TrafficLayerUtility.LayerExists(layerName);
                DrawCheck(
                    layerOk,
                    layerOk ? $"Layer '{layerName}' exists" : $"Layer '{layerName}' does not exist",
                    layerOk ? null : "Create layer",
                    () => CreateLayer(layerName));

                bool maskOk = config.vehicleLayer.value != 0;
                DrawCheck(
                    maskOk,
                    maskOk ? "Vehicle layer mask set" : "Vehicle layer mask is empty (detection would match nothing)",
                    maskOk || !layerOk ? null : "Derive from layer",
                    () => DeriveMask(config, layerName));
            }

            int presetCount = manager.vehicleTypes != null ? manager.vehicleTypes.Length : 0;
            DrawCheck(
                presetCount > 0,
                presetCount > 0 ? $"{presetCount} vehicle preset(s)" : "No vehicle presets assigned",
                "New preset asset",
                CreateVehiclePreset);

            EditorGUILayout.LabelField(
                "Select a car prefab in the Project window first and the preset is filled in and added to the " +
                "traffic. Without one it is created empty and left out, since an empty preset cannot spawn.",
                EditorStyles.wordWrappedMiniLabel);

            DrawBrokenPresets();

            int laneCount = manager.lanes != null ? manager.lanes.Length : 0;
            DrawCheck(laneCount > 0, laneCount > 0 ? $"{laneCount} lane(s)" : "No lanes configured", null, null);

            foreach (string issue in CollectLaneIssues(manager))
            {
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }

            EditorGUILayout.Space(10f);
            DrawLaneBuilder();
            EditorGUILayout.Space(10f);
            DrawModelMaterialTool();
            DrawFooter();
        }

        void DrawLaneBuilder()
        {
            EditorGUILayout.LabelField("Add lane from hierarchy", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select the parent GameObject holding this lane's waypoints as children, in order. " +
                "The first child becomes the spawn point and the last one the destroy point.",
                MessageType.None);

            Transform parent = Selection.activeTransform;
            int childCount = parent != null ? parent.childCount : 0;

            using (new EditorGUI.DisabledScope(childCount < 2))
            {
                string label = parent == null
                    ? "Select a parent in the hierarchy"
                    : childCount < 2
                        ? $"'{parent.name}' needs at least 2 children"
                        : $"Add lane from '{parent.name}' ({childCount} waypoints)";

                if (GUILayout.Button(label, GUILayout.Height(24f))) AddLaneFromTransform(parent);
            }
        }

        void DrawModelMaterialTool()
        {
            EditorGUILayout.LabelField("Assign material to models", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select model assets (.fbx, .obj, ...) in the Project window and remap every one of " +
                "their material slots to a single material. Useful right after importing raw models.",
                MessageType.None);

            modelMaterial = (Material)EditorGUILayout.ObjectField("Material", modelMaterial, typeof(Material), false);

            string[] modelPaths = SelectedModelPaths();

            using (new EditorGUI.DisabledScope(modelMaterial == null || modelPaths.Length == 0))
            {
                string label = modelMaterial == null
                    ? "Pick a material first"
                    : modelPaths.Length == 0
                        ? "Select model assets in the Project window"
                        : $"Remap {modelPaths.Length} selected model(s)";

                if (GUILayout.Button(label, GUILayout.Height(24f))) RemapModelMaterials(modelPaths, modelMaterial);
            }
        }

        static string[] SelectedModelPaths()
        {
            return Selection.GetFiltered<Object>(SelectionMode.Assets)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                .Distinct()
                .ToArray();
        }

        static void RemapModelMaterials(string[] modelPaths, Material material)
        {
            int remapped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < modelPaths.Length; i++)
                {
                    string path = modelPaths[i];
                    EditorUtility.DisplayProgressBar("Traffic System", path, (float)i / modelPaths.Length);

                    if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                    foreach (string slotName in MaterialSlotNames(path))
                    {
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slotName), material);
                        remapped++;
                    }

                    importer.SaveAndReimport();
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Traffic System] Remapped {remapped} material slot(s) across {modelPaths.Length} model(s) to '{material.name}'.");
        }

        /// <summary>
        /// Material slot names of an imported model, taken from its renderers and from any
        /// material sub-asset, so it works whether materials are embedded or extracted.
        /// </summary>
        static IEnumerable<string> MaterialSlotNames(string modelPath)
        {
            var names = new HashSet<string>();

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (root != null)
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material slot in renderer.sharedMaterials)
                    {
                        if (slot != null) names.Add(slot.name);
                    }
                }
            }

            foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            {
                if (sub is Material subMaterial) names.Add(subMaterial.name);
            }

            return names;
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(manager == null))
            {
                if (GUILayout.Button("Select TrafficManager")) Selection.activeGameObject = manager.gameObject;
            }
        }

        // ============================== CONFIG TAB ==============================

        void DrawConfigTab()
        {
            if (manager == null || manager.config == null)
            {
                EditorGUILayout.HelpBox("No TrafficConfig in the scene. Use the Setup tab first.", MessageType.Info);
                DestroyConfigEditor();
                return;
            }

            if (configEditor == null || editedConfig != manager.config)
            {
                DestroyConfigEditor();
                editedConfig = manager.config;
                configEditor = UnityEditor.Editor.CreateEditor(editedConfig);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play mode: changes apply live but are discarded on exit.", MessageType.Warning);
            }

            configEditor.OnInspectorGUI();
        }

        void DestroyConfigEditor()
        {
            if (configEditor == null) return;
            DestroyImmediate(configEditor);
            configEditor = null;
            editedConfig = null;
        }

        // ============================== ACTIONS ==============================

        /// <summary>Runs every checklist fix in order, so a fresh scene is ready in one click.</summary>
        void FixAll()
        {
            if (manager == null) CreateTrafficSystemObject();
            if (manager == null) return;

            if (manager.config == null) CreateAndAssignConfig();
            TrafficConfig config = manager.config;
            if (config == null) return;

            string layerName = string.IsNullOrEmpty(config.vehicleLayerName) ? "Vehicles" : config.vehicleLayerName;
            if (!TrafficLayerUtility.LayerExists(layerName)) CreateLayer(layerName);
            if (TrafficLayerUtility.LayerExists(layerName) && config.vehicleLayer.value == 0) DeriveMask(config, layerName);
        }

        void CreateTrafficSystemObject()
        {
            var go = new GameObject("Traffic System");
            Undo.RegisterCreatedObjectUndo(go, "Create Traffic System");

            TrafficConfig config = Undo.AddComponent<TrafficConfig>(go);
            TrafficManager newManager = Undo.AddComponent<TrafficManager>(go);
            newManager.config = config;

            manager = newManager;
            Selection.activeGameObject = go;
            MarkSceneDirty();
        }

        void CreateAndAssignConfig()
        {
            TrafficConfig config = manager.GetComponent<TrafficConfig>();
            if (config == null) config = Undo.AddComponent<TrafficConfig>(manager.gameObject);

            Undo.RecordObject(manager, "Assign TrafficConfig");
            manager.config = config;
            MarkSceneDirty();
        }

        void CreateLayer(string layerName)
        {
            if (TrafficLayerUtility.TryCreateLayer(layerName, out string error)) return;
            EditorUtility.DisplayDialog("Traffic System", error, "OK");
        }

        void DeriveMask(TrafficConfig config, string layerName)
        {
            int index = LayerMask.NameToLayer(layerName);
            if (index == -1) return;

            Undo.RecordObject(config, "Set vehicle layer mask");
            config.vehicleLayer = 1 << index;
            MarkSceneDirty();
        }

        /// <summary>
        /// Creates a vehicle preset. It only joins the fleet once it has a prefab: registering an
        /// empty one used to break the manager at startup, because building its pool throws and
        /// takes the whole of Awake down with it.
        /// </summary>
        void CreateVehiclePreset()
        {
            GameObject model = SelectedPrefabAsset();

            string defaultName = model != null ? "Preset_" + model.name : "VehiclePreset";
            string path = EditorUtility.SaveFilePanelInProject(
                "New vehicle preset", defaultName, "asset",
                "Where should the vehicle preset be saved?");

            if (string.IsNullOrEmpty(path)) return;

            var preset = CreateInstance<VehicleCharacteristics>();
            preset.prefab = model;
            preset.nombreVehiculo = model != null ? model.name : "";
            preset.pesoSpawn = 1;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();

            if (manager != null && preset.prefab != null)
            {
                Undo.RecordObject(manager, "Add vehicle preset");
                var types = new List<VehicleCharacteristics>(manager.vehicleTypes ?? new VehicleCharacteristics[0]) { preset };
                manager.vehicleTypes = types.ToArray();
                MarkSceneDirty();
            }
            else if (manager != null)
            {
                // Not TrafficLog.Warn: that is gated off by default, and this is editor guidance
                // the user has to see.
                Debug.LogWarning(
                    $"[Traffic System] '{preset.name}' was created without a prefab, so it has not been added to " +
                    $"the traffic. Assign its prefab, then add it from the Fleet palette in the Design tab.");
            }

            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);
        }

        /// <summary>The prefab or model selected in the Project window, if that is what is selected.</summary>
        static GameObject SelectedPrefabAsset()
        {
            var selected = Selection.activeObject as GameObject;
            return selected != null && PrefabUtility.IsPartOfPrefabAsset(selected) ? selected : null;
        }

        void AddLaneFromTransform(Transform parent)
        {
            var waypoints = new Transform[parent.childCount];
            for (int i = 0; i < parent.childCount; i++) waypoints[i] = parent.GetChild(i);

            var lane = new LaneConfig
            {
                laneId = parent.name,
                waypoints = waypoints,
                spawnPoint = waypoints[0],
                destroyPoints = new[] { waypoints[waypoints.Length - 1] }
            };

            Undo.RecordObject(manager, "Add traffic lane");
            var lanes = new List<LaneConfig>(manager.lanes ?? new LaneConfig[0]) { lane };
            manager.lanes = lanes.ToArray();
            MarkSceneDirty();

            Selection.activeGameObject = manager.gameObject;
        }

        // ============================== HELPERS ==============================

        static IEnumerable<string> CollectLaneIssues(TrafficManager manager)
        {
            if (manager.lanes == null) yield break;

            var seenIds = new HashSet<string>();

            for (int i = 0; i < manager.lanes.Length; i++)
            {
                LaneConfig lane = manager.lanes[i];
                string label = $"Lane {i} ('{lane.laneId}')";

                if (string.IsNullOrWhiteSpace(lane.laneId))
                    yield return $"Lane {i} has no laneId. Vehicles use it to tell lanes apart.";
                else if (!seenIds.Add(lane.laneId))
                    yield return $"{label}: duplicated laneId. Vehicles in different lanes would ignore each other.";

                if (lane.waypoints == null || lane.waypoints.Length == 0)
                    yield return $"{label}: no waypoints.";

                if (lane.spawnPoint == null)
                    yield return $"{label}: no spawn point, so it will never spawn.";

                if (lane.destroyPoints == null || lane.destroyPoints.Length == 0)
                    yield return $"{label}: no destroy points, so vehicles are never returned to the pool.";
            }
        }

        static void DrawCheck(bool ok, string label, string buttonLabel, System.Action onClick)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ok ? "✓" : "✗", GUILayout.Width(16f));
                GUILayout.Label(label, EditorStyles.wordWrappedLabel);

                if (!string.IsNullOrEmpty(buttonLabel) && onClick != null)
                {
                    if (GUILayout.Button(buttonLabel, GUILayout.Width(120f))) onClick();
                }
            }
        }

        void MarkSceneDirty()
        {
            if (Application.isPlaying) return;
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
