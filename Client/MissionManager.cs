using System;
using System.Collections.Generic;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{


    public enum MissionState
    {
        None,
        Started,
        AtHouse,
        InsideHouse,
        Completed,
        Failed
    }

    public class MissionManager
    {
        public MissionState CurrentState { get; private set; } = MissionState.None;
        public bool IsMissionActive => CurrentState != MissionState.None && CurrentState != MissionState.Completed && CurrentState != MissionState.Failed;

        private MissionBuilder missionBuilder;
        private MissionData currentMission;

        // Auto-retry tracking
        private bool waitingForWantedLevelClear = false;
        private int lastWantedLevel = 0;
        private bool hasCompletedMission = false;

        // Mission locations
        private Vector3 houseExterior = new Vector3(885.8f, -515.7f, 57.3f);
        private Vector3 houseInterior = new Vector3(261.4586f, -998.8196f, -99.00863f);
        private Vector3 houseEntryPoint = new Vector3(911.0f, -507.0f, 26.5f);
        private Vector3 houseExitPoint = new Vector3(911.0f, -507.0f, 26.5f); 
        private Vector3 houseInteriorExitPoint = new Vector3(266.0f, -1007.6f, -101.0f);

        // Vehicle tracking
        private int playerLastVehicle = 0;
        private Vector3 vehicleSpawnPoint = new Vector3(917.3f, -512.2f, 58.6f);
        private uint lastVehicleModel = 0;
        private float lastVehicleHeading = 0f;

        // lockpicking
        private Lockpicking lockpickingInstance;
        private bool isLockpickingActive = false;

        // Grenade
        private EMPGrenade empGrenade;

        // Mission components
        private CameraManager cameraManager;
        private LootManager lootManager;

        // Mission tracking
        private int mapBlip = 0;
        private bool hasTriggeredHouseText = false;
        private bool hasEnteredHouse = false;
        private int totalLootValue = 0;

        // Events
        public event Action<MissionState> OnMissionStateChanged;
        public event Action<int> OnMissionCompleted;
        public event Action OnMissionFailed;

        public MissionManager(CameraManager cameraManager, LootManager lootManager, Lockpicking lockpicking)
        {
            this.cameraManager = cameraManager;
            this.lootManager = lootManager;
            this.lockpickingInstance = lockpicking;
            this.missionBuilder = new MissionBuilder();
            this.empGrenade = new EMPGrenade(cameraManager);

            // Subscribe to alarm events
            cameraManager.OnAlarmTriggered += OnAlarmTriggered;

            lockpickingInstance.OnLockpickingComplete += OnLockpickingComplete;
        }

        public void StartMission(string missionId = "house1")
        {
            if (IsMissionActive)
            {
                Screen.ShowNotification("~r~Mission already active!");
                return;
            }

            if (hasCompletedMission)
            {
                Screen.ShowNotification("~r~You have already completed this mission!");
                return;
            }

            currentMission = missionBuilder.GetMission(missionId);
            if (currentMission == null)
            {
                Screen.ShowNotification($"~r~Mission '{missionId}' not found!");
                return;
            }

            // Store player's vehicle before starting mission
            StorePlayerVehicle();

            // Update mission locations
            houseExterior = currentMission.ExteriorPosition;
            houseInterior = currentMission.InteriorPosition;
            houseEntryPoint = currentMission.EntryPoint;
            houseExitPoint = currentMission.ExitPoint;
            houseInteriorExitPoint = currentMission.InteriorExitPoint;

            CurrentState = MissionState.Started;
            hasTriggeredHouseText = false;
            hasEnteredHouse = false;
            totalLootValue = 0;

            SetupMissionArea();
            CreateMapBlip();
            ShowMissionStartText();

            empGrenade.GiveGrenades(2);

            GivePlayerSilencedPistol();

            OnMissionStateChanged?.Invoke(CurrentState);
            Screen.ShowNotification($"~g~Mission '{missionId}' Started!");
        }

        private void GivePlayerSilencedPistol()
        {
            var playerPed = PlayerPedId();

            // Give silenced pistol with ammo
            uint silencedPistolHash = (uint)GetHashKey("weapon_pistol");
            GiveWeaponToPed(playerPed, silencedPistolHash, 120, false, true);

            // Add suppressor component
            uint suppressorHash = (uint)GetHashKey("COMPONENT_AT_PI_SUPP_02");
            GiveWeaponComponentToPed(playerPed, silencedPistolHash, suppressorHash);

            Debug.WriteLine("[MISSION] Player given silenced pistol with 120 rounds");
        }

        private void CheckForCarUnloading()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            int vehicle = GetVehiclePedIsIn(playerPed, false);

            // Check if player is in a vehicle
            bool inVehicle = DoesEntityExist(vehicle);

            // Check if player is near their spawned vehicle (even if not in it)
            bool nearSpawnedVehicle = false;
            if (!inVehicle && playerLastVehicle != 0 && DoesEntityExist(playerLastVehicle))
            {
                var vehiclePos = GetEntityCoords(playerLastVehicle, true);
                float distanceToVehicle = GetDistanceBetweenCoords(
                    playerPos.X, playerPos.Y, playerPos.Z,
                    vehiclePos.X, vehiclePos.Y, vehiclePos.Z, true);

                if (distanceToVehicle < 5.0f) // Within 5 units of vehicle
                {
                    nearSpawnedVehicle = true;
                }
            }

            // Check if player is near vehicle spawn point (vehicle may still be spawning)
            bool nearVehicleSpawnPoint = false;
            if (!inVehicle && !nearSpawnedVehicle)
            {
                float distanceToSpawnPoint = GetDistanceBetweenCoords(
                    playerPos.X, playerPos.Y, playerPos.Z,
                    vehicleSpawnPoint.X, vehicleSpawnPoint.Y, vehicleSpawnPoint.Z, true);

                if (distanceToSpawnPoint < 15.0f)
                {
                    nearVehicleSpawnPoint = true;
                }
            }

            // Debug logging
            //Debug.WriteLine($"[UNLOAD] InVehicle: {inVehicle}, NearVehicle: {nearSpawnedVehicle}, NearSpawn: {nearVehicleSpawnPoint}, VehicleExists: {(playerLastVehicle != 0 && DoesEntityExist(playerLastVehicle))}, CurrentCarried: {lootManager.CurrentCarried}");

            if (inVehicle || nearSpawnedVehicle)
            {
                bool allLootCollected = IsAllLootCollected();

                if (allLootCollected)
                {
                    if (inVehicle)
                    {
                        Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload all loot and complete mission");
                    }
                    else
                    {
                        Screen.DisplayHelpTextThisFrame("Get in your vehicle and press ~INPUT_CONTEXT~ to unload all loot and complete mission");
                    }
                }
                else
                {
                    if (inVehicle)
                    {
                        Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload loot (or collect more for bonus)");
                    }
                    else
                    {
                        Screen.DisplayHelpTextThisFrame("Get in your vehicle and press ~INPUT_CONTEXT~ to unload loot (or go back for more)");
                    }
                }

                if (IsControlJustPressed(0, 51) && inVehicle)
                {
                    bool hadAllLoot = IsAllLootCollected();
                    int currentLootCount = lootManager.CurrentCarried;

                    // CALCULATE TOTAL VALUE BEFORE UNLOADING
                    totalLootValue = CalculateLootValue();

                    // Show unload notification with loot count
                    Screen.ShowNotification($"~g~Loot unloaded! Collected {currentLootCount} items.");
                    lootManager.UnloadLoot();
                    CompleteMissionWithStatus(hadAllLoot);
                }


            }
            else if (nearVehicleSpawnPoint)
            {
                // Player is near spawn point but vehicle isn't there yet OR vehicle is spawning
                if (playerLastVehicle == 0 || !DoesEntityExist(playerLastVehicle))
                {
                    Screen.DisplayHelpTextThisFrame("Your vehicle is being moved to this location...");
                }
                else
                {
                    // Vehicle exists but player not close enough
                    Screen.DisplayHelpTextThisFrame("Get closer to your vehicle to unload loot");
                }
            }
            else
            {
                // Player is far from spawn point - guide them there
                bool allLootCollected = IsAllLootCollected();

                if (allLootCollected)
                {
                    Screen.DisplayHelpTextThisFrame("Go to your vehicle (marked on GPS) to unload all loot and complete the mission");
                }
                else
                {
                    Screen.DisplayHelpTextThisFrame("Go to your vehicle (marked on GPS) to unload loot (or go back for more)");
                }
            }
        }

        public void DrawUI()
        {
            if (IsMissionActive)
            {
                empGrenade.DrawUI();
            }
        }

        public bool HasEMPGrenades()
        {
            return empGrenade != null && empGrenade.HasGrenades;
        }

        public void OnGrenadeExplosion(Vector3 position)
        {
            if (empGrenade != null)
            {
                // convert
                empGrenade.OnExplosion(position, 0); // 0 = GRENADE explosion type
            }
        }

        private void CheckForAutoRetry()
        {
            // Only check if we're waiting for wanted level to clear
            if (!waitingForWantedLevelClear || hasCompletedMission) return;

            int currentWantedLevel = GetPlayerWantedLevel(PlayerId());

            // If wanted level just cleared (was > 0, now = 0)
            if (lastWantedLevel > 0 && currentWantedLevel == 0)
            {
                waitingForWantedLevelClear = false;

                // Show retry message and auto-restart
                Screen.ShowNotification("~g~Wanted level cleared! Restarting mission...");

                // Small delay then restart
                BaseScript.Delay(2000).ContinueWith(_ => {
                    if (!IsMissionActive && !hasCompletedMission)
                    {
                        StartMission("house1");
                    }
                });
            }

            lastWantedLevel = currentWantedLevel;
        }

        public void Update()
        {
            if (!IsMissionActive)
            {
                CheckForAutoRetry();
                return;
            }

            empGrenade.Update();

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToHouse = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                           houseExterior.X, houseExterior.Y, houseExterior.Z, true);
            float entryDistance;

            switch (CurrentState)
            {
                case MissionState.Started:
                    if (distanceToHouse < 50f && !hasTriggeredHouseText)
                    {
                        hasTriggeredHouseText = true;
                        ShowHouseApproachText();
                        CurrentState = MissionState.AtHouse;
                        OnMissionStateChanged?.Invoke(CurrentState);
                    }
                    break;

                case MissionState.AtHouse:
                    // Don't allow entry if lockpicking is active
                    if (isLockpickingActive)
                    {
                        // Show that lockpicking is in progress
                        Screen.DisplayHelpTextThisFrame("Lockpicking in progress...");
                        return;
                    }

                    // Check for entry point interaction
                    entryDistance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                                houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, true);
                    if (entryDistance < 2.0f)
                    {
                        Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to pick the lock");
                        DrawMarker(1, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                                  2.0f, 2.0f, 1.0f, 0, 255, 0, 100, false, true, 2, false, null, null, false);

                        if (IsControlJustPressed(0, 51)) // E key
                        {
                            EnterHouse(); // This will now start lockpicking
                        }
                    }
                    break;

                case MissionState.InsideHouse:
                    // check for interior exit when in InsideHouse state
                    float interiorExitDistance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                                         houseInteriorExitPoint.X, houseInteriorExitPoint.Y, houseInteriorExitPoint.Z, true);

                    if (interiorExitDistance < 3.0f)
                    {
                        Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to exit the house");
                        DrawMarker(1, houseInteriorExitPoint.X, houseInteriorExitPoint.Y, houseInteriorExitPoint.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                                  2.0f, 2.0f, 1.0f, 255, 255, 0, 100, false, true, 2, false, null, null, false);

                        if (IsControlJustPressed(0, 51)) // E key
                        {
                            ExitHouse();
                        }
                    }

                    // Check if player is actually outside the house
                    float distanceToInterior = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                                       houseInterior.X, houseInterior.Y, houseInterior.Z, true);

                    // If player is far from interior coordinates, they're probably outside
                    if (distanceToInterior > 50f)
                    {

                        // Handle different scenarios based on what player has
                        if (lootManager.CurrentCarried > 0)
                        {
                            // Player has loot - always show unload guidance
                            CheckForCarUnloading();
                        }
                        else
                        {
                            // Player has no loot - check overall mission status
                            bool allLootCollected = IsAllLootCollected();
                            if (allLootCollected)
                            {
                                // All loot collected but player isn't carrying any (shouldn't happen)
                                Screen.DisplayHelpTextThisFrame("Mission complete! All loot has been collected.");
                                CompleteMission();
                            }
                            else
                            {
                                // Player can go back for more loot or complete mission
                                Screen.DisplayHelpTextThisFrame("You can re-enter to collect loot, or start your getaway vehicle to complete mission");

                                // Check for re-entry
                                entryDistance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                                       houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, true);
                                if (entryDistance < 2.0f)
                                {
                                    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to re-enter for more loot");
                                    DrawMarker(1, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                                              2.0f, 2.0f, 1.0f, 0, 255, 255, 100, false, true, 2, false, null, null, false);

                                    if (IsControlJustPressed(0, 51))
                                    {
                                        EnterHouse();
                                    }
                                }
                                // ALSO allow completing mission without all loot
                                else if (lootManager.PlayerLoot.Count > 0) // Player has collected SOME loot previously
                                {
                                    // Show guidance to vehicle even without current loot
                                    Screen.DisplayHelpTextThisFrame("Go to your vehicle to complete the mission with collected loot");
                                }
                            }
                        }
                    }
                    break;
            }
        }

        private void StorePlayerVehicle()
        {
            var playerPed = PlayerPedId();
            int currentVehicle = GetVehiclePedIsIn(playerPed, false);

            if (currentVehicle != 0 && DoesEntityExist(currentVehicle))
            {
                playerLastVehicle = currentVehicle;
                lastVehicleModel = (uint)GetEntityModel(currentVehicle);
                lastVehicleHeading = GetEntityHeading(currentVehicle);
                Debug.WriteLine($"[MISSION] Stored player vehicle: {GetDisplayNameFromVehicleModel(lastVehicleModel)}");
            }
            else
            {
                // Try to get the last vehicle the player was in
                int lastVehicle = GetPlayersLastVehicle();
                if (lastVehicle != 0 && DoesEntityExist(lastVehicle))
                {
                    playerLastVehicle = lastVehicle;
                    lastVehicleModel = (uint)GetEntityModel(lastVehicle);
                    lastVehicleHeading = GetEntityHeading(lastVehicle);
                    Debug.WriteLine($"[MISSION] Stored last vehicle: {GetDisplayNameFromVehicleModel(lastVehicleModel)}");
                }
                else
                {
                    // Default to a common vehicle if no vehicle found
                    lastVehicleModel = (uint)GetHashKey("sultan");
                    lastVehicleHeading = 180f;
                    Debug.WriteLine("[MISSION] No vehicle found, will spawn default Sultan");
                }
            }
        }

        private async void SpawnPlayerVehicle()
        {
            // Delete existing vehicle if it exists
            if (playerLastVehicle != 0 && DoesEntityExist(playerLastVehicle))
            {
                DeleteEntity(ref playerLastVehicle);
            }

            // Request the vehicle model
            RequestModel(lastVehicleModel);

            // Wait for model to load
            int attempts = 0;
            while (!HasModelLoaded(lastVehicleModel) && attempts < 50)
            {
                await BaseScript.Delay(100);
                attempts++;
            }

            if (!HasModelLoaded(lastVehicleModel))
            {
                Debug.WriteLine($"[MISSION] Failed to load vehicle model: {GetDisplayNameFromVehicleModel(lastVehicleModel)}");
                // Fall back to a basic vehicle
                lastVehicleModel = (uint)GetHashKey("sultan");
                RequestModel(lastVehicleModel);

                attempts = 0;
                while (!HasModelLoaded(lastVehicleModel) && attempts < 50)
                {
                    await BaseScript.Delay(100);
                    attempts++;
                }
            }

            // Spawn the vehicle
            int newVehicle = CreateVehicle(lastVehicleModel, vehicleSpawnPoint.X, vehicleSpawnPoint.Y, vehicleSpawnPoint.Z,
                                           lastVehicleHeading, true, false);

            if (DoesEntityExist(newVehicle))
            {
                playerLastVehicle = newVehicle;
                SetVehicleOnGroundProperly(newVehicle);
                SetEntityAsMissionEntity(newVehicle, true, true);

                // Add waypoint to vehicle
                SetNewWaypoint(vehicleSpawnPoint.X, vehicleSpawnPoint.Y);

                // Delay this notification so it doesn't override loot notifications
                await BaseScript.Delay(1000);
                Screen.ShowNotification("~g~Your vehicle has been moved to a safe location.");

                Debug.WriteLine("[MISSION] Player vehicle spawned successfully");
            }
            else
            {
                Debug.WriteLine("[MISSION] Failed to spawn player vehicle");
            }

            SetModelAsNoLongerNeeded(lastVehicleModel);
        }

        private void GivePlayerMoney(int amount)
        {
            uint bankStat = (uint)GetHashKey("BANK_BALANCE");
            int currentBank = 0;
            StatGetInt(bankStat, ref currentBank, -1);
            StatSetInt(bankStat, currentBank + amount, true);

            UseFakeMpCash(true);
            ChangeFakeMpCash(0, amount);

            Screen.ShowNotification($"~g~+${amount}~w~ cash received!");
            Debug.WriteLine($"[MISSION] Gave player ${amount} cash");
        }

        private void SetupMissionArea()
        {
            // Clear existing cameras and loot
            cameraManager.ClearCameras();
            lootManager.LootItems.Clear();

            if (currentMission == null) return;

            // Add cameras from mission data
            foreach (var cameraData in currentMission.Cameras)
            {
                var camera = new Camera(
                    cameraData.Position,
                    cameraData.Rotation,
                    cameraData.DetectionRange,
                    cameraData.ViewAngle,
                    cameraData.ScanAngle
                );
                cameraManager.AddCamera(camera);
            }

            // Add loot from mission data
            foreach (var lootData in currentMission.Loot)
            {
                lootManager.AddLootItem(new LootItem(lootData.Type, lootData.Position, lootData.Amount));
            }

            Debug.WriteLine($"[MISSION] Mission area setup complete: {currentMission.Cameras.Count} cameras, {currentMission.Loot.Count} loot items");
        }

        public MissionBuilder GetMissionBuilder()
        {
            return missionBuilder;
        }

        private void CreateMapBlip()
        {
            mapBlip = AddBlipForCoord(houseExterior.X, houseExterior.Y, houseExterior.Z);
            SetBlipSprite(mapBlip, 374); // House icon
            SetBlipDisplay(mapBlip, 4);
            SetBlipScale(mapBlip, 1.0f);
            SetBlipColour(mapBlip, 5); // Yellow
            SetBlipAsShortRange(mapBlip, false);
            BeginTextCommandSetBlipName("STRING");
            AddTextComponentString("Target House - TG");
            EndTextCommandSetBlipName(mapBlip);
        }

        private void ShowMissionStartText()
        {
            //Screen.ShowNotification("~b~Thieves Guild:~w~ Looks like nobody's home, make sure it stays that way. We've supplied you with a couple EMP nades too.");
        }

        private void ShowHouseApproachText()
        {
            Screen.ShowNotification("~b~Thieves Guild:~w~ Looks like you're about there, make sure you don't trip the alarm. Front door's too risky, head around the back.");
        }

        private void EnterHouse()
        {
            // Don't teleport immediately - start lockpicking first
            Screen.ShowNotification("~y~You need to pick the lock first...");
            StartLockpicking();
        }

        private void StartLockpicking()
        {
            isLockpickingActive = true;
            lockpickingInstance.StartLockpicking();
        }

        private void OnLockpickingComplete(bool success)
        {
            isLockpickingActive = false;

            if (success)
            {
                // SUCCESS: Teleport player inside
                var playerPed = PlayerPedId();
                SetEntityCoords(playerPed, houseInterior.X, houseInterior.Y, houseInterior.Z, false, false, false, true);
                SetEntityHeading(playerPed, 0f);

                CurrentState = MissionState.InsideHouse;
                hasEnteredHouse = true;
                OnMissionStateChanged?.Invoke(CurrentState);

                Screen.ShowNotification("~g~Lock picked successfully! You're inside the house.");
                Screen.ShowNotification("~g~Find the loot and get out before you're caught!");

                // Remove the exterior map blip
                if (mapBlip != 0)
                {
                    RemoveBlip(ref mapBlip);
                    mapBlip = 0;
                }
            }
            else
            {
                // FAILURE: Fail the entire mission
                Screen.ShowNotification("~r~Thieves Guild: You've failed to pick the lock. Mission aborted!");
                Screen.ShowNotification("~r~The noise has attracted unwanted attention...");

                // Fail the mission completely
                FailMissionFromLockpicking();
            }
        }

        private async void FailMissionFromLockpicking()
        {
            if (CurrentState == MissionState.Failed) return;

            CurrentState = MissionState.Failed;
            OnMissionStateChanged?.Invoke(CurrentState);

            // Spawn some police response for failing lockpicking
            Screen.ShowNotification("~r~Police have been alerted to suspicious activity!");

            // Set a small wanted level (not as severe as getting caught red-handed on camera)
            SetPlayerWantedLevel(PlayerId(), 1, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            waitingForWantedLevelClear = true;
            lastWantedLevel = GetPlayerWantedLevel(PlayerId());

            // Wait a moment then cleanup
            await BaseScript.Delay(2000);

            CleanupMission();
            OnMissionFailed?.Invoke();
        }

        private async void ExitHouse()
        {
            var playerPed = PlayerPedId();

            // Teleport player to ENTRY point
            SetEntityCoords(playerPed, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);

            // Set waypoint immediately so player knows where to go
            SetNewWaypoint(vehicleSpawnPoint.X, vehicleSpawnPoint.Y);

            Screen.ShowNotification("~g~Exited the house safely!");

            // Check if player has loot and provide appropriate feedback
            if (lootManager.CurrentCarried > 0)
            {
                bool allLootCollected = IsAllLootCollected();
                if (allLootCollected)
                {
                    Screen.ShowNotification("~g~All loot collected! Return to your vehicle to complete the mission.");
                }
                else
                {
                    Screen.ShowNotification("~y~You have loot! Return to your vehicle to unload, or go back for more.");
                }

                // Wait for loot notification to be visible, THEN spawn vehicle
                await BaseScript.Delay(2000);
            }
            else
            {
                // Check mission completion status only if no loot carried
                CheckMissionCompletion();
                await BaseScript.Delay(1500);
            }

            // NOW spawn the vehicle (after loot notifications have been shown)
            SpawnPlayerVehicle();
        }



        private bool IsAllLootCollected()
        {
            foreach (var lootItem in lootManager.LootItems)
            {
                if (!lootItem.IsDepleted)
                {
                    return false;
                }
            }
            return true;
        }

        private void CheckMissionCompletion()
        {
            // Check if all loot has been collected
            bool allLootCollected = true;
            foreach (var lootItem in lootManager.LootItems)
            {
                if (!lootItem.IsDepleted)
                {
                    allLootCollected = false;
                    break;
                }
            }

            if (allLootCollected)
            {
                Screen.ShowNotification("~g~All loot collected! Return to your vehicle to complete the mission.");
            }
            else
            {
                Screen.ShowNotification("~y~You can collect more loot or return to your vehicle with what you have.");
            }
        }



        private void CompleteMission()
        {
            totalLootValue = CalculateLootValue();
            bool allLootCollected = IsAllLootCollected();
            CompleteMissionWithStatus(allLootCollected);
        }



        private void CompleteMissionWithStatus(bool hadAllLoot)
        {
            if (CurrentState == MissionState.Completed) return;

            CurrentState = MissionState.Completed;
            hasCompletedMission = true;
            waitingForWantedLevelClear = false;
            OnMissionStateChanged?.Invoke(CurrentState);

            // Success message with bonus for collecting everything
            if (hadAllLoot)
            {
                int bonus = (int)(totalLootValue * 0.5f); // 50% bonus for collecting everything
                totalLootValue += bonus;
                Screen.ShowNotification("~b~Thieves Guild:~w~ Perfect! You got everything. Here's a bonus for being thorough.");
                Screen.ShowNotification($"~g~Mission Complete! Total value: ${totalLootValue} (includes ${bonus} bonus)");
            }
            else
            {
                Screen.ShowNotification("~b~Thieves Guild:~w~ Not bad, but you left some behind. Still counts as a success.");
                Screen.ShowNotification($"~g~Mission Complete! Loot value: ${totalLootValue}");
            }

            GivePlayerMoney(totalLootValue);

            // Clean up mission area
            CleanupMission();

            OnMissionCompleted?.Invoke(totalLootValue);
        }



        private void OnAlarmTriggered()
        {
            if (!IsMissionActive || CurrentState == MissionState.Failed) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            bool isPlayerInside = IsPlayerInsideHouse(playerPos);

            if (isPlayerInside)
            {
                // Player caught inside - immediate fail state
                Screen.ShowNotification("~r~ALARM TRIGGERED! Security detected you inside!");
                FailMissionImmediate();
            }
            else
            {
                // Player caught outside - start alarm but give them a chance
                Screen.ShowNotification("~r~ALARM TRIGGERED! Security is on high alert!");
                //StartExteriorAlarm();
                FailMissionFromCamera();
            }
        }

        private async void FailMissionFromCamera()
        {
            if (CurrentState == MissionState.Failed) return;

            CurrentState = MissionState.Failed;
            OnMissionStateChanged?.Invoke(CurrentState);

            // Start exterior alarm
            StartExteriorAlarm();

            // Give player 3-star wanted level like lockpicking failure
            SetPlayerWantedLevel(PlayerId(), 3, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            waitingForWantedLevelClear = true;
            lastWantedLevel = GetPlayerWantedLevel(PlayerId());

            // Show fail messages
            Screen.ShowNotification("~r~Thieves Guild: You've been spotted on camera!");
            Screen.ShowNotification("~r~Police are on their way - mission failed!");

            // Wait a moment then cleanup (no teleportation, no police spawning)
            await BaseScript.Delay(2000);

            CleanupMission();
            OnMissionFailed?.Invoke();
        }

        private bool IsPlayerInsideHouse(Vector3 playerPos)
        {
            // Check if player is close to interior coordinates (within 100 units)
            float distanceToInterior = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                               houseInterior.X, houseInterior.Y, houseInterior.Z, true);
            return distanceToInterior < 100f;
        }

        private async void StartExteriorAlarm()
        {
            // Prepare the alarm first
            if (PrepareAlarm("JEWEL_STORE_HEIST_ALARMS") == true)
            {
                StartAlarm("JEWEL_STORE_HEIST_ALARMS", true);

                PlaySoundFrontend(-1, "Out_Of_Bounds_Timer", "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS", true);

                Debug.WriteLine("[ALARM] Exterior alarm started successfully");
            }
        }

        private void StopAlarms()
        {
            // Stop all alarms when mission ends
            StopAlarm("JEWEL_STORE_HEIST_ALARMS", true);
            StopAllAlarms(true);

            // Also stop any prepared alarms (idk if this necessary)
            if (IsAlarmPlaying("JEWEL_STORE_HEIST_ALARMS"))
            {
                StopAlarm("JEWEL_STORE_HEIST_ALARMS", false);
            }

            Debug.WriteLine("[ALARM] All alarms stopped");
        }

        private async void FailMissionImmediate()
        {
            if (CurrentState == MissionState.Failed) return;

            CurrentState = MissionState.Failed;
            OnMissionStateChanged?.Invoke(CurrentState);

            DoScreenFadeOut(300);

            await BaseScript.Delay(500);

            var playerPed = PlayerPedId();

            // Teleport player outside immediately
            SetEntityCoords(playerPed, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, false, false, false, true);

            // Spawn player's vehicle at the safe location
            SpawnPlayerVehicle();

            // Start exterior alarm now that player is outside
            StartExteriorAlarm();

            // Spawn police and set wanted level
            SpawnPoliceResponse();

            waitingForWantedLevelClear = true;
            lastWantedLevel = GetPlayerWantedLevel(PlayerId());

            // Clear player loot immediately
            lootManager.UnloadLoot();

            // Show fail message
            Screen.ShowNotification("~r~Thieves Guild: Hear the pigs have your scent, call this one a fail and lose them");

            DoScreenFadeIn(1000);

            // Clean up mission
            CleanupMission();

            OnMissionFailed?.Invoke();
        }

        private async void FailMission()
        {
            if (CurrentState == MissionState.Failed) return;

            CurrentState = MissionState.Failed;
            OnMissionStateChanged?.Invoke(CurrentState);

            DoScreenFadeOut(500);

            await BaseScript.Delay(500);

            var playerPed = PlayerPedId();

            // Teleport player outside
            SetEntityCoords(playerPed, houseExterior.X + 20f, houseExterior.Y, houseExterior.Z, false, false, false, true);

            // Spawn player's vehicle at the safe location
            SpawnPlayerVehicle();

            // Spawn police response
            SpawnPoliceResponse();

            // Clear player loot
            lootManager.UnloadLoot();

            // Show fail message
            Screen.ShowNotification("~r~Thieves Guild: Hear the pigs have your scent, call this one a fail and lose them");

            DoScreenFadeIn(1000);

            // Clean up mission
            CleanupMission();

            OnMissionFailed?.Invoke();
        }

        private void SpawnPoliceResponse()
        {
            // Spawn 2 police cars with officers near the house
            Vector3 cop1Pos = houseExterior + new Vector3(15f, 5f, 0f);
            Vector3 cop2Pos = houseExterior + new Vector3(-15f, -5f, 0f);

            uint policeModel = (uint)GetHashKey("police");
            uint copModel = (uint)GetHashKey("s_m_y_cop_01");

            RequestModel(policeModel);
            RequestModel(copModel);

            // Create police vehicles
            int vehicle1 = CreateVehicle(policeModel, cop1Pos.X, cop1Pos.Y, cop1Pos.Z, 0f, true, true);
            int vehicle2 = CreateVehicle(policeModel, cop2Pos.X, cop2Pos.Y, cop2Pos.Z, 180f, true, true);

            // Create police officers
            int cop1 = CreatePed(4, copModel, cop1Pos.X + 2f, cop1Pos.Y, cop1Pos.Z, 0f, true, true);
            int cop2 = CreatePed(4, copModel, cop2Pos.X - 2f, cop2Pos.Y, cop2Pos.Z, 180f, true, true);

            // Give cops weapons
            GiveWeaponToPed(cop1, (uint)GetHashKey("weapon_pistol"), 100, false, true);
            GiveWeaponToPed(cop2, (uint)GetHashKey("weapon_pistol"), 100, false, true);

            // Set cop AI and behavior
            SetupPoliceAI(cop1);
            SetupPoliceAI(cop2);

            // Get player reference
            var playerPed = PlayerPedId();

            // Make cops hostile to player and start combat
            SetPedRelationshipGroupHash(cop1, (uint)GetHashKey("COP"));
            SetPedRelationshipGroupHash(cop2, (uint)GetHashKey("COP"));
            SetRelationshipBetweenGroups(5, (uint)GetHashKey("COP"), (uint)GetHashKey("PLAYER")); // 5 = hate

            // Force cops to target and pursue player
            SetPedAsEnemy(cop1, true);
            SetPedAsEnemy(cop2, true);

            // Make them aggressive and combat ready
            TaskCombatPed(cop1, playerPed, 0, 16);
            TaskCombatPed(cop2, playerPed, 0, 16);

            // Set wanted level to 3 stars
            SetPlayerWantedLevel(PlayerId(), 3, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            SetModelAsNoLongerNeeded(policeModel);
            SetModelAsNoLongerNeeded(copModel);

            Debug.WriteLine("[MISSION] Police response spawned - 2 vehicles, 2 officers, 3 star wanted level");
        }

        // Helper method to set up police AI
        private void SetupPoliceAI(int copPed)
        {
            // Set ped as police type
            SetPedAsCop(copPed, true);

            // Configure AI behavior
            SetPedCombatAttributes(copPed, 46, true);  // Can use vehicles
            SetPedCombatAttributes(copPed, 5, true);   // Can fight armed peds when not armed
            SetPedCombatAttributes(copPed, 1424, true); // Always fight
            SetPedCombatAttributes(copPed, 3, false);  // Can't use cover

            // Set combat movement
            SetPedCombatMovement(copPed, 2); // Aggressive movement
            SetPedCombatRange(copPed, 2);    // Long range combat

            // Set accuracy and other stats
            SetPedAccuracy(copPed, 75);      // Good accuracy
            SetPedMaxHealth(copPed, 200);    // Extra health
            SetPedArmour(copPed, 100);       // Give armor

            // Make them fearless
            SetPedFleeAttributes(copPed, 0, false);
            SetPedCombatAttributes(copPed, 17, false); // Won't flee
        }


        private int CalculateLootValue()
        {
            int value = 0;
            foreach (var kvp in lootManager.PlayerLoot)
            {
                switch (kvp.Key)
                {
                    case "Cash": value += kvp.Value * 100; break;
                    case "Jewelry": value += kvp.Value * 250; break;
                    case "Painting": value += kvp.Value * 500; break;
                    case "Electronics": value += kvp.Value * 150; break;
                    default: value += kvp.Value * 50; break;
                }
            }
            return value;
        }

        private void CleanupMission()
        {
            // Remove map blip
            if (mapBlip != 0)
            {
                RemoveBlip(ref mapBlip);
                mapBlip = 0;
            }

            // Stop all alarms
            StopAlarms();

            // Clear cameras and loot
            cameraManager.ClearCameras();
            cameraManager.ResetAlarm();
            lootManager.LootItems.Clear();

            // Reset vehicle tracking (but don't delete the vehicle - player needs it)
            playerLastVehicle = 0;
            lastVehicleModel = 0;
            lastVehicleHeading = 0f;

            Debug.WriteLine("[MISSION] Mission cleanup complete");
        }




        public void ForceEndMission()
        {
            if (IsMissionActive)
            {
                CurrentState = MissionState.None;
                CleanupMission();
                Screen.ShowNotification("~r~Mission terminated");
            }
        }

        // Testing methods
        public void TeleportToExterior()
        {
            var playerPed = PlayerPedId();

            // Get ground Z coordinate
            float groundZ = 0f;
            GetGroundZFor_3dCoord(houseExterior.X, houseExterior.Y, houseExterior.Z + 10f, ref groundZ, false);

            Vector3 safePos = new Vector3(houseExterior.X, houseExterior.Y, groundZ + 1f); // +1f for safety

            SetEntityCoords(playerPed, safePos.X, safePos.Y, safePos.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);

            Screen.ShowNotification("~g~Teleported to house exterior");
        }

        public void TeleportToInterior()
        {
            var playerPed = PlayerPedId();
            SetEntityCoords(playerPed, houseInterior.X, houseInterior.Y, houseInterior.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);
            Screen.ShowNotification("~g~Teleported to house interior");
        }

    }
}
