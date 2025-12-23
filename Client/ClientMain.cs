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
        Lockpicking lockpickingInstance;

        private LootManager lootManager = new LootManager();

        public ClientMain()
        {
            Debug.WriteLine("House Robbery System Initialized!");

            lockpickingInstance = new Lockpicking();

            RegisterNuiCallbackType("lockpickingInput");
            EventHandlers["__cfx_nui:lockpickingInput"] += new Action<IDictionary<string, object>, CallbackDelegate>((data, cb) =>
            {
                if (data.TryGetValue("key", out var keyObj))
                {
                    var key = keyObj as string;
                    lockpickingInstance?.HandleNuiInput(key);
                }
                cb(new { ok = true });
            });
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

        [Command("placeloot")]
        public void PlaceLoot(int source, List<object> args, string raw)
        {
            if (args.Count < 2)
            {
                Screen.ShowNotification("Usage: /placeloot [type] [amount]");
                return;
            }

            string type = args[0].ToString();
            int amount;
            if (!int.TryParse(args[1].ToString(), out amount) || amount <= 0)
            {
                Screen.ShowNotification("Invalid amount.");
                return;
            }

            var playerPed = PlayerPedId();
            var pos = GetEntityCoords(playerPed, true);

            var loot = new LootItem(type, pos, amount);
            lootManager.AddLootItem(loot);

            // Print to F8 console
            string coordString = $"new LootItem(\"{type}\", new Vector3({pos.X:F1}f, {pos.Y:F1}f, {pos.Z:F1}f), {amount})";
            Debug.WriteLine($"[LOOT] {coordString}");
            Screen.ShowNotification($"Placed loot: {type} x{amount} at your feet.");
        }

        [Command("listloot")]
        public void ListLoot(int source, List<object> args, string raw)
        {
            int i = 0;
            foreach (var loot in lootManager.LootItems)
            {
                Debug.WriteLine($"[{i}] {loot.Type} at {loot.Position} (max: {loot.MaxAmount}, left: {loot.Remaining})");
                i++;
            }
            Screen.ShowNotification("Loot list printed to F8 console.");
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

            // Check for loot pickup
            foreach (var loot in lootManager.LootItems)
            {
                if (loot.IsDepleted) continue;
                float dist = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z, loot.Position.X, loot.Position.Y, loot.Position.Z, true);
                if (dist < 1.5f)
                {
                    Screen.DisplayHelpTextThisFrame($"Press ~INPUT_CONTEXT~ to pick up {loot.Type} ({loot.Remaining} left)");
                    if (IsControlJustPressed(0, 51))
                    {
                        int taken = lootManager.PickUpLoot(loot, 1);
                        Screen.ShowNotification($"Picked up {taken} {loot.Type}!");
                    }
                }
            }

            // Check for unloading at car
            playerPed = PlayerPedId();
            int vehicle = GetVehiclePedIsIn(playerPed, false);

            if (DoesEntityExist(vehicle) && lootManager.CurrentCarried > 0)
            {
                Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload loot in your vehicle");
                if (IsControlJustPressed(0, 51))
                {
                    lootManager.UnloadLoot();
                    Screen.ShowNotification("Loot unloaded!");
                }
            }


            await Task.FromResult(0);
        }

        private async Task StartRobberyAttempt()
        {
            Screen.ShowNotification("Starting house robbery...");
            lockpickingInstance.StartLockpicking();
            // TODO: Implement lockpicking minigame
            // TODO: Check if house is already robbed
            // TODO: Start alarm timer
        }
    }
}
