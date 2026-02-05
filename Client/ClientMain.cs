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
            //new Vector3(-845.4f, 161.0f, 66.6f), // Example house location
            // remove this before monday deprecated
        };

        private bool nearHouse = false;
        private Vector3 currentHousePos;
        private bool showCoords = false;
        Lockpicking lockpickingInstance;

        private LootManager lootManager = new LootManager();
        private CameraManager cameraManager = new CameraManager();
        private MissionManager missionManager;

        private float lastGrenadeThrowTime = 0f;

        private bool hasStartedIntro = false;
        private bool phoneCreated = false;

        // BANK ROBBERY

        private HostageSystem hostageSystem = new HostageSystem();
        private BankRobberyManager bankRobberyManager;
        private GuardSystem guardSystem = new GuardSystem();

        private bool missionMenuEnabled = false;


        private void OnAlarmTriggered()
        {
            // Handle alarm trigger - kick player out, spawn cops, etc.
            //Screen.ShowNotification("~r~SECURITY BREACH! Robbery failed!");
        }

        public ClientMain()
        {
            Debug.WriteLine("House Robbery System Initialized!");

            lockpickingInstance = new Lockpicking();

            cameraManager.OnAlarmTriggered += OnAlarmTriggered;

            missionManager = new MissionManager(cameraManager, lootManager, lockpickingInstance);
            missionManager.OnMissionCompleted += (value) => Debug.WriteLine($"Mission completed with ${value}");
            missionManager.OnMissionFailed += () => Debug.WriteLine("Mission failed!");

            bankRobberyManager = new BankRobberyManager(lootManager, cameraManager, lockpickingInstance);


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

            RegisterNuiCallbackType("MissionSelect");
            EventHandlers["__cfx_nui:MissionSelect"] += new Action<IDictionary<string, object>, CallbackDelegate>((data, cb) =>
            {
                if (data.TryGetValue("action", out var actionObj))
                {
                    var action = actionObj as string;
                    HandleMissionMenuCallback(action);
                }
                cb(new { ok = true });
            });

            EventHandlers["explosionEvent"] += new Action<int, float, float, float, bool, uint, float>(OnExplosion);

            SetNuiFocus(false, false);

            // HOUSE ROBBERY MISSION
            //StartIntroSequence();

            // BANK ROBBERY

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

        [Command("addloot")]
        public void AddLoot(int source, List<object> args, string raw)
        {
            if (args.Count < 2)
            {
                Screen.ShowNotification("Usage: /addloot [type] [amount]");
                Screen.ShowNotification("Types: Cash, Jewelry, Painting, Electronics");
                return;
            }

            string type = args[0].ToString();
            if (!int.TryParse(args[1].ToString(), out int amount) || amount <= 0)
            {
                Screen.ShowNotification("~r~Invalid amount!");
                return;
            }

            missionManager.GetMissionBuilder().AddLoot(type, amount);
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

        [Command("addcamera")]
        public void AddCamera(int source, List<object> args, string raw)
        {
            if (args.Count < 1)
            {
                Screen.ShowNotification("Usage: /addcamera [rotation] [range] [viewAngle] [scanAngle]");
                Screen.ShowNotification("Example: /addcamera 90 15 60 45");
                return;
            }

            if (!float.TryParse(args[0].ToString(), out float rotation))
            {
                Screen.ShowNotification("~r~Invalid rotation value!");
                return;
            }

            float range = args.Count > 1 && float.TryParse(args[1].ToString(), out float r) ? r : 15f;
            float viewAngle = args.Count > 2 && float.TryParse(args[2].ToString(), out float v) ? v : 60f;
            float scanAngle = args.Count > 3 && float.TryParse(args[3].ToString(), out float s) ? s : 45f;

            missionManager.GetMissionBuilder().AddCamera(rotation, range, viewAngle, scanAngle);
        }

        [Command("disablecameras")]
        public void DisableCameras(int source, List<object> args, string raw)
        {
            var playerPed = PlayerPedId();
            var pos = GetEntityCoords(playerPed, true);

            cameraManager.DisableCamerasInRadius(pos, 10f, 10f); // 10 meter radius, 10 second disable
            Screen.ShowNotification("Cameras disabled with EMP!");
        }

        [Command("setentry")]
        public void SetEntryPoint(int source, List<object> args, string raw)
        {
            missionManager.GetMissionBuilder().SetEntryPoint();
        }

        [Command("setexit")]
        public void SetExitPoint(int source, List<object> args, string raw)
        {
            missionManager.GetMissionBuilder().SetExitPoint();
        }

        [Command("setinteriorexit")]
        public void SetInteriorExit(int source, List<object> args, string raw)
        {
            missionManager.GetMissionBuilder().SetInteriorExitPoint();
        }


        [Command("printmission")]
        public void PrintMission(int source, List<object> args, string raw)
        {
            missionManager.GetMissionBuilder().PrintMissionData();
        }

        [Command("selectmission")]
        public void SelectMission(int source, List<object> args, string raw)
        {
            if (args.Count < 1)
            {
                Screen.ShowNotification("Usage: /selectmission [missionId]");
                return;
            }

            string missionId = args[0].ToString();
            missionManager.GetMissionBuilder().SetCurrentMission(missionId);
        }

        [Command("startmission")]
        public void StartMission(int source, List<object> args, string raw)
        {
            string missionId = args.Count > 0 ? args[0].ToString() : "house1";
            missionManager.StartMission(missionId);
        }

        [Command("endmission")]
        public void EndMission(int source, List<object> args, string raw)
        {
            missionManager.ForceEndMission();
        }

        [Command("tpexterior")]
        public void TeleportExterior(int source, List<object> args, string raw)
        {
            missionManager.TeleportToExterior();
        }

        [Command("tpinterior")]
        public void TeleportInterior(int source, List<object> args, string raw)
        {
            missionManager.TeleportToInterior();
        }



        [Tick]
        public async Task OnTick()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // BANK ROBBERY
            hostageSystem.Update();
            bankRobberyManager.Update();
            guardSystem.Update();

            // HOUSE ROBBERY

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

            HandleMissionMenuInput();

            // Mission Menu Input Blocking (like cl_action.lua)
            if (missionMenuEnabled)
            {
                DisableControlAction(0, 1, true);   // LookLeftRight
                DisableControlAction(0, 2, true);   // LookUpDown  
                DisableControlAction(0, 24, true);  // Attack
                DisablePlayerFiring(playerPed, true); // Disable weapon firing
                DisableControlAction(0, 142, true); // MeleeAttackAlternate
                DisableControlAction(0, 106, true); // VehicleMouseControlOverride
            }

            // Check for loot pickup and draw markers
            foreach (var loot in lootManager.LootItems)
            {
                if (loot.IsDepleted) continue;

                // Draw loot marker
                int markerColor = GetLootMarkerColor(loot.Type);
                DrawMarker(1, loot.Position.X, loot.Position.Y, loot.Position.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                          1.0f, 1.0f, 0.5f, markerColor, 255, 0, 150, false, true, 2, false, null, null, false);

                // Draw 3D text above loot
                // Get color for text to match marker
                var textColor = GetLootTextColor(loot.Type);
                DrawLootText(loot.Position + new Vector3(0, 0, 1f), $"~{textColor}~{loot.Type}~w~ x{loot.Remaining}", 255, 255, 255, 0.4f);



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
            //playerPed = PlayerPedId();
            //int vehicle = GetVehiclePedIsIn(playerPed, false);
            //
            //if (DoesEntityExist(vehicle) && lootManager.CurrentCarried > 0)
            //{
            //    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload loot in your vehicle");
            //    if (IsControlJustPressed(0, 51))
            //    {
            //        lootManager.UnloadLoot();
            //        Screen.ShowNotification("Loot unloaded!");
            //    }
            //}

            cameraManager.Update();
            cameraManager.DrawCameras();

            missionManager.Update();

            missionManager.DrawUI();

            await Task.FromResult(0);
        }

        private async Task StartRobberyAttempt()
        {
            //Screen.ShowNotification("Starting house robbery...");
            //lockpickingInstance.StartLockpicking();
        }

        private int GetLootMarkerColor(string lootType)
        {
            switch (lootType.ToLower())
            {
                case "cash": return 2; // Green
                case "jewelry":
                case "jewelery": return 3; // Blue
                case "painting": return 5; // Yellow
                case "electronics": return 6; // Red
                case "drugs": return 8; // Purple
                default: return 0; // White
            }
        }

        private string GetLootTextColor(string lootType)
        {
            switch (lootType.ToLower())
            {
                case "cash": return "g";        // Green
                case "jewelry":
                case "jewelery": return "b";    // Blue
                case "painting": return "y";    // Yellow  
                case "electronics": return "r"; // Red
                case "drugs": return "p";       // Purple
                default: return "w";            // White
            }
        }

        private void DrawLootText(Vector3 position, string text, int r, int g, int b, float scale)
        {
            Vector3 camPos = GetGameplayCamCoord();
            float distance = GetDistanceBetweenCoords(camPos.X, camPos.Y, camPos.Z, position.X, position.Y, position.Z, true);

            if (distance > 10f) return;

            float screenX = 0f;
            float screenY = 0f;
            bool onScreen = GetScreenCoordFromWorldCoord(position.X, position.Y, position.Z, ref screenX, ref screenY);

            if (onScreen)
            {
                // Set up text rendering
                SetTextFont(0);
                SetTextProportional(true);
                SetTextScale(0.0f, scale);
                SetTextColour(r, g, b, 255);
                SetTextDropShadow();
                SetTextOutline();
                SetTextCentre(true); // Center the text
                SetTextEntry("STRING");
                AddTextComponentString(text);

                DrawText(screenX, screenY);
            }
        }


        private void OnExplosion(int source, float x, float y, float z, bool isEntityDamaged, uint damageEntity, float damageScale)
        {
            Vector3 explosionPos = new Vector3(x, y, z);

            Debug.WriteLine($"[EXPLOSION] Detected at {explosionPos}, source: {source}, damage scale: {damageScale}");

            if (missionManager.IsMissionActive)
            {
                // Check if this explosion is near the player
                var playerPos = GetEntityCoords(PlayerPedId(), true);
                float distanceToPlayer = GetDistanceBetweenCoords(explosionPos.X, explosionPos.Y, explosionPos.Z,
                                                                 playerPos.X, playerPos.Y, playerPos.Z, true);

                // If explosion is within reasonable throwing distance and we have EMP grenades
                if (distanceToPlayer < 50f && missionManager.HasEMPGrenades())
                {
                    // Check if player recently threw a grenade
                    if (WasRecentGrenadeThrow())
                    {
                        //Debug.WriteLine("[EMP] Converting grenade explosion to EMP");

                        missionManager.OnGrenadeExplosion(explosionPos);
                    }
                }
            }
        }

        private bool WasRecentGrenadeThrow()
        {
            float currentTime = GetGameTimer() / 1000f;

            // if a grenade was thrown in the last 5 seconds
            if (currentTime - lastGrenadeThrowTime < 5f)
            {
                return true;
            }

            // Also check if player currently has grenade weapon selected
            var playerPed = PlayerPedId();
            uint currentWeapon = 0;
            GetCurrentPedWeapon(playerPed, ref currentWeapon, true);

            uint grenadeWeapon = (uint)GetHashKey("weapon_grenade");
            if (currentWeapon == grenadeWeapon)
            {
                lastGrenadeThrowTime = currentTime;
                return true;
            }

            return false;
        }

        // Intro sequence
        private async void StartIntroSequence()
        {
            if (hasStartedIntro) return;
            hasStartedIntro = true;

            // Wait for everything to initialize
            await BaseScript.Delay(3000);

            // Welcome message
            Screen.ShowNotification("~b~Welcome to Thieves Guild!~w~");
            await BaseScript.Delay(2000);

            // Mission briefing
            SendMissionBriefing();
            await BaseScript.Delay(3000);

            // Auto-start mission
            Screen.ShowNotification("~b~Starting your first mission...~w~");
            await BaseScript.Delay(1500);

            missionManager.StartMission("house1");
        }

        // Mission briefing
        private async void SendMissionBriefing()
        {
            // First notification - Mission basics
            BeginTextCommandThefeedPost("STRING");
            AddTextComponentString("~y~TARGET:~w~ Residential property\n" +
                                  "~y~OBJECTIVE:~w~ Steal valuables without detection");
            EndTextCommandThefeedPostMessagetext("CHAR_LJT", "CHAR_LJT", false, 4, "Thieves Guild", "Mission Briefing");

            await BaseScript.Delay(3000);

            // Second notification - Equipment and warnings
            BeginTextCommandThefeedPost("STRING");
            AddTextComponentString("~g~EQUIPMENT:~w~ Lockpicks & EMP grenades\n" +
                                  "~r~WARNING:~w~ Security cameras active!\n" +
                                  "Good luck.");
            EndTextCommandThefeedPostMessagetext("CHAR_LJT", "CHAR_LJT", false, 4, "Thieves Guild", "Equipment Info");

            Debug.WriteLine("[MISSION] Mission briefing sent (2 parts)");
        }

        [Command("setnight")]
        public void SetNightTime(int source, List<object> args, string raw)
        {
            NetworkOverrideClockTime(23, 0, 0);
            Screen.ShowNotification("~b~Time set to night (23:00).");
        }


        // BANK ROBBERY

        //ActionMenu
        private void ToggleMissionMenu()
        {
            // Toggle menu state (like ToggleActionMenu in Lua)
            missionMenuEnabled = !missionMenuEnabled;

            if (missionMenuEnabled)
            {
                // Focus NUI with mouse cursor enabled
                SetNuiFocus(true, true);

                // Send message to JavaScript to show menu (as JSON string)
                SendNuiMessage(@"{
            ""showmenu"": true,
            ""menuType"": ""mission_selection"",
            ""missions"": [
                {
                    ""id"": ""bank_loud"",
                    ""name"": ""Bank Robbery - Loud"",
                    ""description"": ""High risk, high reward approach""
                },
                {
                    ""id"": ""bank_quiet"",
                    ""name"": ""Bank Robbery - Stealth"",
                    ""description"": ""Silent but deadly approach""
                },
                {
                    ""id"": ""house"",
                    ""name"": ""House Robbery"",
                    ""description"": ""Practice mission for beginners""
                }
            ]
        }");

                Debug.WriteLine("[MISSION_MENU] Mission menu opened");
            }
            else
            {
                // Remove NUI focus
                SetNuiFocus(false, false);

                // Send message to JavaScript to hide menu (as JSON string)
                SendNuiMessage(@"{
            ""hidemenu"": true
        }");

                Debug.WriteLine("[MISSION_MENU] Mission menu closed");
            }
        }

        private void HandleMissionMenuInput()
        {
            // Check Z key pressed (Control 20 like cl_action.lua) to open menu
            if (IsControlJustPressed(1, 20)) // Z key
            {
                ToggleMissionMenu();
            }
        }

        private void HandleMissionMenuCallback(string action)
        {
            Debug.WriteLine($"[MISSION_MENU] Received action: {action}");

            switch (action)
            {
                case "bank_loud":
                    Screen.ShowNotification("~r~Starting LOUD bank robbery approach!");
                    bankRobberyManager.StartBankRobbery("loud");
                    break;

                case "bank_quiet":
                    Screen.ShowNotification("~b~Starting STEALTH bank robbery approach!");
                    bankRobberyManager.StartBankRobbery("quiet");
                    break;

                case "house":
                    Screen.ShowNotification("~y~Starting house robbery!");
                    missionManager.StartMission("house1");
                    break;

                case "exit":
                    // Close menu and return (like cl_action.lua exit handling)
                    ToggleMissionMenu();
                    return;
            }

            // Close menu after selection (unless it was exit)
            ToggleMissionMenu();
        }

        [Command("testhostages")]
        public void TestHostages(int source, List<object> args, string raw)
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Set crew standby position 15 units to the right of player
            Vector3 crewStandByPosition = playerPos + new Vector3(15.0f, 0f, 0f);

            Screen.ShowNotification("~y~Initializing hostage system...");
            Debug.WriteLine($"[TEST] Starting hostage system at player position: {playerPos}");

            hostageSystem.Initialize(crewStandByPosition);
        }

        [Command("cleanhostages")]
        public void CleanHostages(int source, List<object> args, string raw)
        {
            hostageSystem.Cleanup();
            Screen.ShowNotification("~r~Hostage system cleaned up.");
        }

        [Command("givepistol")]
        public void GivePistol(int source, List<object> args, string raw)
        {
            var playerPed = PlayerPedId();

            // Give pistol with ammo
            uint pistolHash = (uint)GetHashKey("weapon_pistol");
            GiveWeaponToPed(playerPed, pistolHash, 250, false, true);

            Screen.ShowNotification("~g~Pistol equipped with 250 rounds!");
            Debug.WriteLine("[WEAPON] Player given pistol with 250 rounds");
        }

        [Command("startbank")]
        public void StartBankRobbery(int source, List<object> args, string raw)
        {
            string approach = args.Count > 0 ? args[0].ToString() : "loud";
            bankRobberyManager.StartBankRobbery(approach);
        }

        [Command("placeguard")]
        public void PlaceGuard(int source, List<object> args, string raw)
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            guardSystem.AddGuard(playerPos);
            Screen.ShowNotification("~y~Guard placed at your location");
        }

        [Command("cleanbank")]
        public void CleanBank(int source, List<object> args, string raw)
        {
            bankRobberyManager.FailRobbery();
            guardSystem.Cleanup();
            hostageSystem.Cleanup();
        }

        [Command("tpbank")]
        public void TeleportToBank(int source, List<object> args, string raw)
        {
            var playerPed = PlayerPedId();

            // Bank exterior coordinates (from BankRobberyManager)
            Vector3 bankExterior = new Vector3(226.4f, 211.6f, 105.5f);

            // Get ground Z coordinate for safety
            float groundZ = bankExterior.Z;
            GetGroundZFor_3dCoord(bankExterior.X, bankExterior.Y, bankExterior.Z + 10f, ref groundZ, false);
            Vector3 safePos = new Vector3(bankExterior.X, bankExterior.Y, groundZ + 1f);

            // Teleport player
            SetEntityCoords(playerPed, safePos.X, safePos.Y, safePos.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);

            Screen.ShowNotification("~g~Teleported to Union Depository exterior");
            Debug.WriteLine($"[TELEPORT] Player teleported to bank exterior: {safePos}");
        }

        [Command("testguards")]
        public void TestGuards(int source, List<object> args, string raw)
        {
            guardSystem.Initialize();

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            // Create a test patrol
            var testPatrol = new List<PatrolNode>
    {
        new PatrolNode(playerPos + new Vector3(5f, 0f, 0f), 0f, 3f),
        new PatrolNode(playerPos + new Vector3(0f, 5f, 0f), 90f, 4f, true),
        new PatrolNode(playerPos + new Vector3(-5f, 0f, 0f), 180f, 3f),
        new PatrolNode(playerPos + new Vector3(0f, -5f, 0f), 270f, 4f)
    };

            guardSystem.AddGuard(playerPos, testPatrol);
            Screen.ShowNotification("~g~Test guard with patrol path spawned!");
        }

        [Command("forceopendoor")]
        public void ForceOpenDoor(int source, List<object> args, string raw)
        {
            uint vaultHash = 961976194;

            try
            {
                // Use state 5 = DOORSTATE_FORCE_OPEN_THIS_FRAME
                DoorSystemSetDoorState(vaultHash, 5, true, true);
                Screen.ShowNotification("~g~Forced door open with state 5!");
                Debug.WriteLine("[VAULT] Forced door open with DOORSTATE_FORCE_OPEN_THIS_FRAME");
            }
            catch (Exception ex)
            {
                Screen.ShowNotification($"~r~Failed: {ex.Message}");
                Debug.WriteLine($"[VAULT] Force open failed: {ex.Message}");
            }
        }

        [Command("unlockdoor")]
        public void UnlockDoor(int source, List<object> args, string raw)
        {
            uint vaultHash = 961976194;

            try
            {
                // Unlock the door (state 0)
                DoorSystemSetDoorState(vaultHash, 0, true, true);
                Screen.ShowNotification("~y~Door unlocked!");

                // Then set open ratio
                BaseScript.Delay(100).ContinueWith(_ => {
                    try
                    {
                        DoorSystemSetOpenRatio(vaultHash, 1.0f, true, true);
                        Screen.ShowNotification("~g~Door opened!");
                        Debug.WriteLine("[VAULT] Door unlocked and opened");
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine($"[VAULT] Open ratio failed: {ex2.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Screen.ShowNotification($"~r~Unlock failed: {ex.Message}");
                Debug.WriteLine($"[VAULT] Unlock failed: {ex.Message}");
            }
        }

        [Command("missionmenu")]
        public void OpenMissionMenu(int source, List<object> args, string raw)
        {
            if (!missionMenuEnabled)
            {
                ToggleMissionMenu();
            }
        }


    }
}
