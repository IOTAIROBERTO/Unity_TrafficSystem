using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// The palette that floats in the Scene view: draw a lane, drop a traffic light, toggle the
    /// editable overlay and set the grid the waypoints snap to, without leaving the viewport.
    ///
    /// Open it from the Scene view's overlay menu (the ☰ button, top right) if it is hidden.
    /// </summary>
    [Overlay(typeof(SceneView), "Traffic System", true)]
    public class TrafficSceneOverlay : IMGUIOverlay
    {
        string newLaneId = "";

        public override void OnGUI()
        {
            TrafficManager manager = Object.FindFirstObjectByType<TrafficManager>();

            if (manager == null)
            {
                EditorGUILayout.LabelField("No TrafficManager in the scene.", EditorStyles.miniLabel);
                if (GUILayout.Button("Open Traffic System window", GUILayout.Width(210f)))
                {
                    TrafficSystemWindow.Open();
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool enabled = GUILayout.Toggle(TrafficSceneEditor.Enabled, "Edit in scene", EditorStyles.miniButton, GUILayout.Width(95f));
                if (enabled != TrafficSceneEditor.Enabled)
                {
                    TrafficSceneEditor.Enabled = enabled;
                    SceneView.RepaintAll();
                }

                EditorGUILayout.LabelField("Snap", GUILayout.Width(34f));
                float snap = EditorGUILayout.FloatField(TrafficSceneEditor.Snap, GUILayout.Width(44f));
                if (!Mathf.Approximately(snap, TrafficSceneEditor.Snap)) TrafficSceneEditor.Snap = snap;
            }

            if (TrafficLaneDesigner.IsPlacing)
            {
                EditorGUILayout.LabelField(
                    $"Drawing '{TrafficLaneDesigner.ActiveLaneId}' — {TrafficLaneDesigner.PlacedCount} wp",
                    EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Finish lane", GUILayout.Width(210f))) TrafficLaneDesigner.Finish();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                newLaneId = EditorGUILayout.TextField(newLaneId, GUILayout.Width(140f));
                if (GUILayout.Button("New lane", GUILayout.Width(66f)))
                {
                    TrafficLaneDesigner.BeginLane(manager, newLaneId);
                    newLaneId = "";
                }
            }

            Transform selected = Selection.activeTransform;
            bool isWaypoint = selected != null && selected.GetComponent<LaneDirection>() != null;

            using (new EditorGUI.DisabledScope(!isWaypoint))
            {
                string label = isWaypoint ? $"Traffic light at '{selected.name}'" : "Select a waypoint for a light";
                if (GUILayout.Button(label, GUILayout.Width(210f)))
                {
                    TrafficLightController light = TrafficLaneDesigner.AddTrafficLight(manager, selected);
                    if (light != null) Selection.activeGameObject = light.gameObject;
                }
            }

            EditorGUILayout.LabelField("Drag dots to move · yellow + inserts · red − deletes",
                EditorStyles.miniLabel);
        }
    }
}
