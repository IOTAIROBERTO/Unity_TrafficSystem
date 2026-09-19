using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Creates the physics layer the traffic system needs, so a host project does not have to
    /// add it by hand before the package works.
    /// </summary>
    public static class TrafficLayerUtility
    {
        const string TagManagerPath = "ProjectSettings/TagManager.asset";

        public static bool LayerExists(string layerName)
        {
            return !string.IsNullOrWhiteSpace(layerName) && LayerMask.NameToLayer(layerName) != -1;
        }

        /// <summary>
        /// Writes <paramref name="layerName"/> into the first free user layer slot (8-31).
        /// Returns false and fills <paramref name="error"/> when it cannot.
        /// </summary>
        public static bool TryCreateLayer(string layerName, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(layerName))
            {
                error = "Layer name is empty.";
                return false;
            }

            if (LayerExists(layerName)) return true;

            Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            {
                error = $"Could not open {TagManagerPath}.";
                return false;
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                error = "TagManager.asset has no 'layers' array.";
                return false;
            }

            // 0-7 are Unity's built-in layers and cannot be renamed.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return true;
            }

            error = "No free user layer slot left (8-31).";
            return false;
        }
    }
}
