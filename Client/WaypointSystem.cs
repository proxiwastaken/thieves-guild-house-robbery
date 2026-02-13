using System;
using System.Collections.Generic;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class WaypointSystem
    {
        private List<int> activeWaypoints = new List<int>();
        private int currentGPSWaypoint = 0;

        public void SetWaypoint(Vector3 position, string description = "")
        {
            ClearAllWaypoints();

            int waypointBlip = AddBlipForCoord(position.X, position.Y, position.Z);
            SetBlipSprite(waypointBlip, 1); // Standard waypoint
            SetBlipColour(waypointBlip, 5); // Yellow
            SetBlipRoute(waypointBlip, true);
            SetBlipRouteColour(waypointBlip, 5);

            if (!string.IsNullOrEmpty(description))
            {
                BeginTextCommandSetBlipName("STRING");
                AddTextComponentString(description);
                EndTextCommandSetBlipName(waypointBlip);
            }

            activeWaypoints.Add(waypointBlip);
            Debug.WriteLine($"[WAYPOINT] Set waypoint at {position} - {description}");
        }

        public void SetObjectiveWaypoint(Vector3 position, string description, int color = 1) // Red by default
        {
            int waypointBlip = AddBlipForCoord(position.X, position.Y, position.Z);
            SetBlipSprite(waypointBlip, 162); // Objective marker
            SetBlipColour(waypointBlip, color);
            SetBlipScale(waypointBlip, 1.2f);

            if (!string.IsNullOrEmpty(description))
            {
                BeginTextCommandSetBlipName("STRING");
                AddTextComponentString(description);
                EndTextCommandSetBlipName(waypointBlip);
            }

            activeWaypoints.Add(waypointBlip);
            Debug.WriteLine($"[WAYPOINT] Set objective waypoint at {position} - {description}");
        }

        // NEW METHOD: Set GPS route with purple line
        public void SetGPSRoute(Vector3 position, string description = "", int color = 27) // Purple by default
        {
            ClearGPSRoute();

            // Create waypoint blip
            currentGPSWaypoint = AddBlipForCoord(position.X, position.Y, position.Z);
            SetBlipSprite(currentGPSWaypoint, 162); // Objective marker
            SetBlipColour(currentGPSWaypoint, color);
            SetBlipScale(currentGPSWaypoint, 1.2f);

            // Enable GPS route with purple line
            SetBlipRoute(currentGPSWaypoint, true);
            SetBlipRouteColour(currentGPSWaypoint, color);

            if (!string.IsNullOrEmpty(description))
            {
                BeginTextCommandSetBlipName("STRING");
                AddTextComponentString(description);
                EndTextCommandSetBlipName(currentGPSWaypoint);
            }

            activeWaypoints.Add(currentGPSWaypoint);

            // Set GPS destination for minimap
            SetWaypointOff();
            SetNewWaypoint(position.X, position.Y);

            Debug.WriteLine($"[WAYPOINT] Set GPS route to {position} - {description}");
        }

        public void ClearGPSRoute()
        {
            if (currentGPSWaypoint != 0 && DoesBlipExist(currentGPSWaypoint))
            {
                SetBlipRoute(currentGPSWaypoint, false);
                RemoveBlip(ref currentGPSWaypoint);
                currentGPSWaypoint = 0;
            }
            SetWaypointOff(); // Clear GPS waypoint
        }

        public void ClearAllWaypoints()
        {
            for (int i = 0; i < activeWaypoints.Count; i++)
            {
                int blip = activeWaypoints[i];
                if (DoesBlipExist(blip))
                {
                    SetBlipRoute(blip, false);
                    RemoveBlip(ref blip);
                }
            }
            activeWaypoints.Clear();
            currentGPSWaypoint = 0;
            SetWaypointOff();
        }

        public void Cleanup()
        {
            ClearAllWaypoints();
            Debug.WriteLine("[WAYPOINT] Waypoint system cleaned up");
        }
    }
}

