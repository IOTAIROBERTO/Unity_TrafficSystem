using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// The fleet palette: every <see cref="VehicleCharacteristics"/> in the project as a thumbnail,
    /// with the ones the manager actually spawns marked. Click one to add or remove it from the
    /// fleet, or drag a car straight from the Project window onto the Scene view — a raw model is
    /// wrapped into a vehicle prefab and a preset on the way in.
    /// </summary>
    public static class TrafficVehiclePalette
    {
        /// <summary>Where presets built from dropped models are written.</summary>
        public const string OutputFolder = "Assets/TrafficSystem/Vehicles";

        const float TargetCarLength = 4.5f;

        // ============================== FLEET ==============================

        public static bool IsInFleet(TrafficManager manager, VehicleCharacteristics preset)
        {
            if (manager == null || manager.vehicleTypes == null || preset == null) return false;

            foreach (VehicleCharacteristics type in manager.vehicleTypes)
            {
                if (type == preset) return true;
            }

            return false;
        }

        public static void AddToFleet(TrafficManager manager, VehicleCharacteristics preset)
        {
            if (manager == null || preset == null || IsInFleet(manager, preset)) return;

            var types = new List<VehicleCharacteristics>(manager.vehicleTypes ?? new VehicleCharacteristics[0]) { preset };
            Undo.RecordObject(manager, "Add vehicle to fleet");
            manager.vehicleTypes = types.ToArray();
            MarkDirty();
        }

        public static void RemoveFromFleet(TrafficManager manager, VehicleCharacteristics preset)
        {
            if (manager == null || manager.vehicleTypes == null || preset == null) return;

            var types = new List<VehicleCharacteristics>(manager.vehicleTypes);
            if (!types.Remove(preset)) return;

            Undo.RecordObject(manager, "Remove vehicle from fleet");
            manager.vehicleTypes = types.ToArray();
            MarkDirty();
        }

        // ============================== PROJECT CONTENTS ==============================

        /// <summary>Every vehicle preset asset in the project, in a stable order.</summary>
        public static VehicleCharacteristics[] AllPresets()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(VehicleCharacteristics));
            var presets = new List<VehicleCharacteristics>(guids.Length);

            foreach (string guid in guids)
            {
                var preset = AssetDatabase.LoadAssetAtPath<VehicleCharacteristics>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null) presets.Add(preset);
            }

            presets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return presets.ToArray();
        }

        // ============================== DROPS ==============================

        /// <summary>True when the dragged objects hold something this palette can turn into a vehicle.</summary>
        public static bool CanAccept(Object[] dragged)
        {
            if (dragged == null) return false;

            foreach (Object item in dragged)
            {
                if (item is VehicleCharacteristics) return true;
                if (item is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go)) return true;
            }

            return false;
        }

        /// <summary>
        /// Adds whatever was dropped to the fleet. Presets join directly; a prefab or a raw model
        /// is wrapped first — pivot moved to the centre of the footprint on the ground, long axis
        /// rotated onto +Z, length normalised, and the cameras and lights DCC exports carry
        /// stripped out.
        /// </summary>
        /// <returns>How many vehicles joined the fleet.</returns>
        public static int Accept(TrafficManager manager, Object[] dragged)
        {
            if (manager == null || dragged == null) return 0;

            int added = 0;
            var models = new List<GameObject>();

            foreach (Object item in dragged)
            {
                if (item is VehicleCharacteristics preset)
                {
                    if (!IsInFleet(manager, preset)) { AddToFleet(manager, preset); added++; }
                }
                else if (item is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go))
                {
                    models.Add(go);
                }
            }

            if (models.Count > 0)
            {
                EnsureOutputFolder();
                string layerName = manager.config != null && !string.IsNullOrEmpty(manager.config.vehicleLayerName)
                    ? manager.config.vehicleLayerName
                    : "Vehicles";

                // Deliberately not TrafficVehiclePrefabs.Collect: when wrapping fails it falls back
                // to every preset in the project, which is right for the sample builder and wrong
                // here — dropping one car must add one car or none.
                foreach (GameObject model in models)
                {
                    GameObject prefab = TrafficVehiclePrefabs.Wrap(model, OutputFolder, layerName, TargetCarLength);
                    if (prefab == null)
                    {
                        TrafficLog.Warn($"'{model.name}' could not be wrapped into a vehicle and was not added.");
                        continue;
                    }

                    VehicleCharacteristics preset = TrafficVehiclePrefabs.CreatePreset(prefab, model.name, OutputFolder);
                    if (preset == null || IsInFleet(manager, preset)) continue;

                    AddToFleet(manager, preset);
                    added++;
                }
            }

            return added;
        }

        static void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder)) return;

            if (!AssetDatabase.IsValidFolder("Assets/TrafficSystem")) AssetDatabase.CreateFolder("Assets", "TrafficSystem");
            AssetDatabase.CreateFolder("Assets/TrafficSystem", "Vehicles");
        }

        // ============================== GUI ==============================

        /// <summary>
        /// Draws the palette as a grid of thumbnails. Clicking one toggles it in the fleet; the
        /// ones in the fleet are boxed.
        /// </summary>
        public static void DrawGrid(TrafficManager manager, float cellSize = 64f, int maxColumns = 0)
        {
            VehicleCharacteristics[] presets = AllPresets();

            if (presets.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No vehicle presets in the project. Drag a car prefab from the Project window " +
                    "onto the Scene view and one is built for you.",
                    MessageType.Info);
                return;
            }

            float available = EditorGUIUtility.currentViewWidth - 40f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(available / (cellSize + 6f)));
            if (maxColumns > 0) columns = Mathf.Min(columns, maxColumns);

            int index = 0;
            while (index < presets.Length)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns && index < presets.Length; column++, index++)
                    {
                        DrawCell(manager, presets[index], cellSize);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        static void DrawCell(TrafficManager manager, VehicleCharacteristics preset, float cellSize)
        {
            bool inFleet = IsInFleet(manager, preset);
            GUIStyle style = inFleet ? EditorStyles.helpBox : GUIStyle.none;

            using (new EditorGUILayout.VerticalScope(style, GUILayout.Width(cellSize)))
            {
                Texture preview = preset.prefab != null ? AssetPreview.GetAssetPreview(preset.prefab) : null;
                var content = new GUIContent(preview, preset.nombreVehiculo);

                if (GUILayout.Button(content, GUIStyle.none, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                {
                    if (inFleet) RemoveFromFleet(manager, preset);
                    else AddToFleet(manager, preset);
                }

                var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                label.normal.textColor = inFleet ? new Color(0.45f, 0.95f, 0.5f) : new Color(1f, 1f, 1f, 0.6f);
                GUILayout.Label(preset.name, label, GUILayout.Width(cellSize));
            }
        }

        static void MarkDirty()
        {
            if (Application.isPlaying) return;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
