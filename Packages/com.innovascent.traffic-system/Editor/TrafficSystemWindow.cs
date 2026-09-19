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
        enum Tab { Setup, Config }

        static readonly string[] TabLabels = { "Setup", "Config" };

        Tab tab = Tab.Setup;
        Vector2 scroll;
        TrafficManager manager;
        UnityEditor.Editor configEditor;
        TrafficConfig editedConfig;
        Material modelMaterial;

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
        }

        void OnDisable()
        {
            DestroyConfigEditor();
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
            else DrawConfigTab();
            EditorGUILayout.EndScrollView();
        }

        // ============================== SETUP TAB ==============================

        void DrawSetupTab()
        {
            EditorGUILayout.LabelField("Scene checklist", EditorStyles.boldLabel);

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

        void CreateVehiclePreset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "New vehicle preset", "VehiclePreset", "asset",
                "Where should the vehicle preset be saved?");

            if (string.IsNullOrEmpty(path)) return;

            var preset = CreateInstance<VehicleCharacteristics>();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();

            if (manager != null)
            {
                Undo.RecordObject(manager, "Add vehicle preset");
                var types = new List<VehicleCharacteristics>(manager.vehicleTypes ?? new VehicleCharacteristics[0]) { preset };
                manager.vehicleTypes = types.ToArray();
                MarkSceneDirty();
            }

            Selection.activeObject = preset;
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
