using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Turns raw model assets into vehicle prefabs a generated scene can spawn, and picks which
    /// presets a build should use. Shared by the scene generators so both wrap models the same way.
    /// </summary>
    public static class TrafficVehiclePrefabs
    {
        /// <summary>
        /// Explicit models win; then the project's existing presets, skipping any whose prefab has
        /// no renderable mesh; then the current selection; and finally box cars, so a generated
        /// scene always has something to spawn and never fills up with invisible traffic.
        /// </summary>
        public static VehicleCharacteristics[] Collect(IEnumerable<GameObject> models, string outputFolder,
                                                       string layerName, float targetCarLength)
        {
            var built = new List<VehicleCharacteristics>();

            if (models != null)
            {
                foreach (GameObject model in models)
                {
                    GameObject prefab = Wrap(model, outputFolder, layerName, targetCarLength);
                    if (prefab != null) built.Add(CreatePreset(prefab, model.name, outputFolder));
                }
                if (built.Count > 0) return built.ToArray();
            }

            var existing = new List<VehicleCharacteristics>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(VehicleCharacteristics)))
            {
                var preset = AssetDatabase.LoadAssetAtPath<VehicleCharacteristics>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset == null || preset.prefab == null) continue;

                if (HasVisibleGeometry(preset.prefab)) existing.Add(preset);
                else Debug.LogWarning($"[Traffic System] Preset '{preset.name}' skipped: '{preset.prefab.name}' has no renderable mesh.");
            }
            if (existing.Count > 0) return existing.ToArray();

            foreach (GameObject model in SelectedModels())
            {
                GameObject prefab = Wrap(model, outputFolder, layerName, targetCarLength);
                if (prefab != null) built.Add(CreatePreset(prefab, model.name, outputFolder));
            }
            if (built.Count == 0) built.Add(CreatePreset(BuildBoxCar(outputFolder, layerName, targetCarLength), "BoxCar", outputFolder));

            return built.ToArray();
        }

        public static GameObject[] SelectedModels()
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

        public static bool HasVisibleGeometry(GameObject prefab)
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

        /// <summary>
        /// Wraps a raw model so its pivot sits on the ground at the centre of its footprint, its
        /// long axis points along +Z and its length is normalised, then saves it as a prefab.
        /// </summary>
        public static GameObject Wrap(GameObject model, string outputFolder, string layerName, float targetCarLength)
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

            // A car is roughly twice as long as it is wide. A near-square footprint means the model
            // carries geometry that is not the vehicle, and normalising it would produce a
            // car-sized box wide enough to block the whole carriageway.
            float widthRatio = Mathf.Min(bounds.size.x, bounds.size.z) / Mathf.Max(bounds.size.x, bounds.size.z, 0.001f);
            if (widthRatio > 0.6f)
            {
                Object.DestroyImmediate(root);
                Debug.LogWarning(
                    $"[Traffic System] '{model.name}' skipped: footprint is {bounds.size.x:F1} x {bounds.size.z:F1} m, " +
                    $"too square for a vehicle (width/length {widthRatio:F2}).");
                return null;
            }

            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = length > 0.001f ? targetCarLength / length : 1f;
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition *= scale;

            var box = root.AddComponent<BoxCollider>();
            box.size = bounds.size * scale;
            box.center = new Vector3(0f, box.size.y * 0.5f, 0f);

            SetLayerRecursively(root.transform, LayerMask.NameToLayer(layerName));

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{root.name}.prefab");
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
        /// Some of these refuse to be removed on their own — a URP camera keeps its extra data
        /// component alive — so when the node carries no geometry the whole GameObject goes.
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

        public static GameObject BuildBoxCar(string outputFolder, string layerName, float targetCarLength)
        {
            var root = new GameObject("TrafficCar_Box");

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.8f, 1.4f, targetCarLength);
            body.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 1.4f, targetCarLength);
            box.center = new Vector3(0f, 0.7f, 0f);

            SetLayerRecursively(root.transform, LayerMask.NameToLayer(layerName));

            string path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/TrafficCar_Box.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static VehicleCharacteristics CreatePreset(GameObject prefab, string displayName, string outputFolder)
        {
            var preset = ScriptableObject.CreateInstance<VehicleCharacteristics>();
            preset.prefab = prefab;
            preset.nombreVehiculo = displayName;
            preset.pesoSpawn = 1;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/Preset_{displayName}.asset");
            AssetDatabase.CreateAsset(preset, path);
            return preset;
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            if (layer < 0) return;

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
