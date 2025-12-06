using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class ClientMain : BaseScript
    {
        private readonly List<Vector3> houseLocations = new List<Vector3>
        {
            new Vector3(-845.4f, 161.0f, 66.6f), // Example house location
            // Add more house coordinates
        };

        private bool nearHouse = false;
        private Vector3 currentHousePos;
        private bool showCoords = false;

        public ClientMain()
        {
            Debug.WriteLine("House Robbery System Initialized!");
        }

        [Command("getcoords")]
        public void GetCurrentCoords()
        {
            var playerPed = PlayerPedId();
            var coords = GetEntityCoords(playerPed, true);
            var heading = GetEntityHeading(playerPed);

            var coordString = $"new Vector3({coords.X:F1}f, {coords.Y:F1}f, {coords.Z:F1}f)";

            // Show on screen for easy copying
            Screen.ShowNotification($"~g~Coordinates copied!~w~\n{coordString}");

            // Print to console (F8) for easy copy-paste
            TriggerEvent("chat:addMessage", new
            {
                color = new[] { 0, 255, 0 },
                multiline = true,
                args = new[] { "[COORDS]", coordString }
            });

            Debug.WriteLine($"=== COPY THIS ===");
            Debug.WriteLine($"{coordString}");
            Debug.WriteLine($"Heading: {heading:F1}");
            Debug.WriteLine($"================");
        }

        [Command("togglecoords")]
        public void ToggleCoords()
        {
            showCoords = !showCoords;
            Screen.ShowNotification($"Coordinate display: {(showCoords ? "~g~ON~w~" : "~r~OFF~w~")}");
        }

        [Tick]
        public async Task OnTick()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Show coordinates on screen if enabled
            if (showCoords)
            {
                var coordText = $"X: {playerPos.X:F1} | Y: {playerPos.Y:F1} | Z: {playerPos.Z:F1}";
                // Draw text on screen
                SetTextFont(0);
                SetTextProportional(true);
                SetTextScale(0.0f, 0.4f);
                SetTextColour(255, 255, 255, 255);
                SetTextDropshadow(0, 0, 0, 0, 255);
                SetTextEdge(2, 0, 0, 0, 150);
                SetTextDropShadow();
                SetTextOutline();
                SetTextEntry("STRING");
                AddTextComponentString(coordText);
                DrawText(0.02f, 0.02f);
            }

            nearHouse = false;

            // Check proximity to houses
            foreach (var house in houseLocations)
            {
                var distance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                    house.X, house.Y, house.Z, true);

                if (distance < 2.0f)
                {
                    nearHouse = true;
                    currentHousePos = house;

                    // Draw marker
                    DrawMarker(1, house.X, house.Y, house.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                        1.0f, 1.0f, 1.0f, 255, 0, 0, 100, false, true, 2, false, null, null, false);

                    // Show interaction prompt
                    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to attempt robbery");

                    // Handle input
                    if (IsControlJustPressed(0, 51)) // E key
                    {
                        await StartRobberyAttempt();
                    }
                    break;
                }
            }

            await Task.FromResult(0);
        }

        private async Task StartRobberyAttempt()
        {
            Screen.ShowNotification("Starting house robbery...");
            // TODO: Implement lockpicking minigame
            // TODO: Check if house is already robbed
            // TODO: Start alarm timer
        }
    }
}
