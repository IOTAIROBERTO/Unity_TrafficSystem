using UnityEditor;
using UnityEngine;

namespace InnovAscent.TrafficSystem.EditorTools
{
    /// <summary>
    /// Stamps a whole signalled crossroads in one click: four approach lanes meeting at a point,
    /// a traffic light on each of them, and a <see cref="FourWayIntersectionController"/> wired to
    /// all four and timed for the phasing that was chosen.
    ///
    /// Right-hand traffic throughout: each lane sits to the driver's right of its road
    /// centreline, and each light stands across the junction on the kerb to that driver's right.
    /// </summary>
    public static class TrafficJunctionStamp
    {
        /// <summary>How a stamped junction lets traffic through.</summary>
        public enum Phasing
        {
            /// <summary>One approach at a time. Halves throughput; no oncoming traffic, so turning is safe.</summary>
            OneArmAtATime,

            /// <summary>Opposing approaches together. Higher throughput, but turns cross oncoming traffic.</summary>
            OpposingPairs,
        }

        static readonly Vector3[] Approaches =
        {
            Vector3.right,    // west  -> east
            Vector3.left,     // east  -> west
            Vector3.forward,  // south -> north
            Vector3.back,     // north -> south
        };

        static readonly string[] ApproachNames = { "west_east", "east_west", "south_north", "north_south" };

        /// <summary>
        /// Builds the junction centred on <paramref name="centre"/>.
        /// </summary>
        /// <param name="armLength">How far each approach reaches out from the centre, in metres.</param>
        /// <param name="laneOffset">Distance from the road centreline to the lane, in metres.</param>
        /// <param name="spacing">Distance between waypoints along an arm, in metres.</param>
        public static FourWayIntersectionController Create(
            TrafficManager manager,
            Vector3 centre,
            float armLength = 45f,
            float laneOffset = 3.5f,
            float spacing = 9f,
            Phasing phasing = Phasing.OneArmAtATime,
            string idPrefix = null)
        {
            if (manager == null) return null;

            armLength = Mathf.Max(spacing * 2f, armLength);
            string prefix = string.IsNullOrWhiteSpace(idPrefix)
                ? "junction_" + CountJunctions()
                : idPrefix.Trim();

            var lights = new TrafficLightController[Approaches.Length];

            for (int i = 0; i < Approaches.Length; i++)
            {
                Vector3 direction = Approaches[i];
                string laneId = prefix + "_" + ApproachNames[i];

                Transform stopWaypoint = BuildArm(manager, centre, direction, laneId, armLength, laneOffset, spacing);
                if (stopWaypoint != null) lights[i] = TrafficLaneDesigner.AddTrafficLight(manager, stopWaypoint);
            }

            return BuildController(manager, centre, prefix, lights, phasing);
        }

        // ============================== ARMS ==============================

        /// <summary>
        /// One approach lane, running from <paramref name="armLength"/> before the centre to the
        /// same distance past it. Returns the waypoint traffic stops at, which is the last one
        /// before the junction box.
        /// </summary>
        static Transform BuildArm(
            TrafficManager manager,
            Vector3 centre,
            Vector3 direction,
            string laneId,
            float armLength,
            float laneOffset,
            float spacing)
        {
            // Right-hand traffic: the lane sits to the driver's right of the centreline.
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 lateral = right * laneOffset;

            TrafficLaneDesigner.BeginLane(manager, laneId);

            Transform stopWaypoint = null;
            for (float distance = -armLength; distance <= armLength + 0.01f; distance += spacing)
            {
                TrafficLaneDesigner.AddWaypoint(centre + direction * distance + lateral);

                // The last waypoint short of the junction box is where the light holds traffic.
                if (distance <= -spacing * 0.5f) stopWaypoint = LastPlaced();
            }

            TrafficLaneDesigner.Finish();
            return stopWaypoint;
        }

        static Transform LastPlaced()
        {
            Transform root = TrafficLaneDesigner.ActiveLaneRoot;
            return root != null && root.childCount > 0 ? root.GetChild(root.childCount - 1) : null;
        }

        // ============================== CONTROLLER ==============================

        static FourWayIntersectionController BuildController(
            TrafficManager manager,
            Vector3 centre,
            string prefix,
            TrafficLightController[] lights,
            Phasing phasing)
        {
            var go = new GameObject("Intersection_" + prefix);
            Undo.RegisterCreatedObjectUndo(go, "Add junction");
            go.transform.SetParent(manager.transform, true);
            go.transform.position = centre;

            var controller = go.AddComponent<FourWayIntersectionController>();
            controller.semaforoWestEast = lights[0];
            controller.semaforoEastWest = lights[1];
            controller.semaforoSouthNorth = lights[2];
            controller.semaforoNorthSouth = lights[3];

            // One arm at a time runs four phases instead of two, so each phase has to be shorter
            // or the cycle grows long enough to look broken.
            bool grouped = phasing == Phasing.OpposingPairs;
            controller.agruparSemaforosOpuestos = grouped;
            controller.tiempoVerdeSolido = grouped ? 12f : 7f;
            controller.tiempoParpadeoVerde = grouped ? 4f : 2.5f;
            controller.tiempoAmarillo = grouped ? 3f : 2f;
            controller.tiempoSeguridadRojo = grouped ? 2f : 1.5f;

            return controller;
        }

        static int CountJunctions()
        {
            return Object.FindObjectsByType<FourWayIntersectionController>(FindObjectsSortMode.None).Length;
        }
    }
}
