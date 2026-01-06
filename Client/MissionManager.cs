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

        // Mission locations
        private Vector3 houseExterior = new Vector3(885.8f, -515.7f, 57.3f);
        private Vector3 houseInterior = new Vector3(261.4586f, -998.8196f, -99.00863f);
        private Vector3 houseEntryPoint = new Vector3(911.0f, -507.0f, 26.5f);
        private Vector3 houseExitPoint = new Vector3(911.0f, -507.0f, 26.5f); 
        private Vector3 houseInteriorExitPoint = new Vector3(266.0f, -1007.6f, -101.0f);




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
        public event Action<int> OnMissionCompleted; // int = loot value
        public event Action OnMissionFailed;

        public MissionManager(CameraManager cameraManager, LootManager lootManager)
        {
            this.cameraManager = cameraManager;
            this.lootManager = lootManager;
            this.missionBuilder = new MissionBuilder();

            // Subscribe to alarm events
            cameraManager.OnAlarmTriggered += OnAlarmTriggered;
        }

        public void StartMission(string missionId = "house1")
        {
            if (IsMissionActive)
            {
                Screen.ShowNotification("~r~Mission already active!");
                return;
            }

            currentMission = missionBuilder.GetMission(missionId);
            if (currentMission == null)
            {
                Screen.ShowNotification($"~r~Mission '{missionId}' not found!");
                return;
            }

            // Update mission locations
            houseExterior = currentMission.ExteriorPosition;
            houseInterior = currentMission.InteriorPosition;
            houseEntryPoint = currentMission.EntryPoint;
            houseExitPoint = currentMission.ExitPoint;
            houseInteriorExitPoint = currentMission.InteriorExitPoint; // Add this line

            CurrentState = MissionState.Started;
            hasTriggeredHouseText = false;
            hasEnteredHouse = false;
            totalLootValue = 0;

            SetupMissionArea();
            CreateMapBlip();
            ShowMissionStartText();

            OnMissionStateChanged?.Invoke(CurrentState);
            Screen.ShowNotification($"~g~Mission '{missionId}' Started!");
        }



        private void CheckForCarUnloading()
        {
            var playerPed = PlayerPedId();
            int vehicle = GetVehiclePedIsIn(playerPed, false);

            if (DoesEntityExist(vehicle))
            {
                bool allLootCollected = IsAllLootCollected();

                if (allLootCollected)
                {
                    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload all loot and complete mission");
                }
                else
                {
                    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to unload loot (or collect more for bonus)");
                }

                if (IsControlJustPressed(0, 51))
                {
                    // Actually unload the loot here
                    lootManager.UnloadLoot();
                    Screen.ShowNotification("Loot unloaded!");

                    // Then complete the mission
                    CompleteMission();
                }
            }
            else
            {
                bool allLootCollected = IsAllLootCollected();

                if (allLootCollected)
                {
                    Screen.DisplayHelpTextThisFrame("Get in a vehicle to unload all loot and complete the mission");
                }
                else
                {
                    Screen.DisplayHelpTextThisFrame("Get in a vehicle to unload loot (or go back for more)");
                }
            }
        }




        public void Update()
        {
            if (!IsMissionActive) return;

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
                    // Check for entry point interaction
                    entryDistance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                                 houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, true);
                    if (entryDistance < 2.0f)
                    {
                        Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to enter the house");
                        DrawMarker(1, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                                  2.0f, 2.0f, 1.0f, 0, 255, 0, 100, false, true, 2, false, null, null, false);

                        if (IsControlJustPressed(0, 51)) // E key
                        {
                            EnterHouse();
                        }
                    }
                    break;

                case MissionState.InsideHouse:
                    // If player is inside the house (distance > 20f means outside)
                    if (distanceToHouse <= 20f) // Player is still inside
                    {
                        // Check for interior exit point interaction
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
                    }
                    // Check if player is back outside with loot
                    else if (distanceToHouse > 20f && hasEnteredHouse)
                    {
                        if (lootManager.CurrentCarried > 0)
                        {
                            CheckForCarUnloading();
                        }
                        else
                        {
                            // Player exited without loot
                            bool allLootCollected = IsAllLootCollected();
                            if (allLootCollected)
                            {
                                Screen.DisplayHelpTextThisFrame("Mission complete! All loot has been collected.");
                                CompleteMission();
                            }
                            else
                            {
                                Screen.DisplayHelpTextThisFrame("You can re-enter to collect loot, or start your getaway");
                            }
                        }
                    }
                    // Allow re-entry from outside
                    else if (distanceToHouse < 20f && hasEnteredHouse)
                    {
                        entryDistance = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                                                               houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z, true);
                        if (entryDistance < 2.0f)
                        {
                            bool allLootCollected = IsAllLootCollected();
                            if (allLootCollected)
                            {
                                Screen.DisplayHelpTextThisFrame("All loot collected! No need to re-enter.");
                            }
                            else
                            {
                                Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to re-enter for more loot");
                                DrawMarker(1, houseEntryPoint.X, houseEntryPoint.Y, houseEntryPoint.Z - 1.0f, 0, 0, 0, 0, 0, 0,
                                          2.0f, 2.0f, 1.0f, 0, 255, 255, 100, false, true, 2, false, null, null, false);

                                if (IsControlJustPressed(0, 51))
                                {
                                    EnterHouse();
                                }
                            }
                        }
                    }
                    break;





            }
        }



        // Update SetupMissionArea method:
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
            Screen.ShowNotification("~b~Thieves Guild:~w~ Looks like nobody's home, make sure it stays that way.");
        }

        private void ShowHouseApproachText()
        {
            Screen.ShowNotification("~b~Thieves Guild:~w~ Looks like you're about there, make sure you don't trip the alarm.");
        }

        private void EnterHouse()
        {
            var playerPed = PlayerPedId();

            // Teleport player to interior
            SetEntityCoords(playerPed, houseInterior.X, houseInterior.Y, houseInterior.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);

            CurrentState = MissionState.InsideHouse;
            hasEnteredHouse = true;
            OnMissionStateChanged?.Invoke(CurrentState);

            Screen.ShowNotification("~g~Entered the house. Find the loot and get out!");

            // Remove the exterior map blip and add interior guidance
            if (mapBlip != 0)
            {
                RemoveBlip(ref mapBlip);
                mapBlip = 0;
            }
        }

        private void ExitHouse()
        {
            var playerPed = PlayerPedId();

            // Teleport player to exit point
            SetEntityCoords(playerPed, houseExitPoint.X, houseExitPoint.Y, houseExitPoint.Z, false, false, false, true);
            SetEntityHeading(playerPed, 0f);

            Screen.ShowNotification("~g~Exited the house safely!");

            // Check mission completion status
            CheckMissionCompletion();
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
    if (CurrentState == MissionState.Completed) return;
    
    CurrentState = MissionState.Completed;
    OnMissionStateChanged?.Invoke(CurrentState);
    
    // Calculate mission rewards
    totalLootValue = CalculateLootValue();
    bool allLootCollected = IsAllLootCollected();
    
    // Success message with bonus for collecting everything
    if (allLootCollected)
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
    
    // Clean up mission area
    CleanupMission();
    
    OnMissionCompleted?.Invoke(totalLootValue);
}



        private void OnAlarmTriggered()
        {
            if (!IsMissionActive || CurrentState == MissionState.Failed) return;

            FailMission();
        }

        private void FailMission()
        {
            if (CurrentState == MissionState.Failed) return;

            CurrentState = MissionState.Failed;
            OnMissionStateChanged?.Invoke(CurrentState);

            // Fade to black
            DoScreenFadeOut(500);
            Wait(1000);

            // Teleport player outside
            var playerPed = PlayerPedId();
            SetEntityCoords(playerPed, houseExterior.X + 20f, houseExterior.Y, houseExterior.Z, false, false, false, true);

            // Spawn police cars
            SpawnPoliceCars();

            // Set wanted level
            SetPlayerWantedLevel(PlayerId(), 3, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            // Clear player loot
            lootManager.UnloadLoot();

            // Show fail message
            Screen.ShowNotification("~r~Thieves Guild: Hear the pigs have your scent, call this one a fail and lose them");

            DoScreenFadeIn(1000);

            // Clean up mission
            CleanupMission();

            OnMissionFailed?.Invoke();
        }

        private void SpawnPoliceCars()
        {
            // Spawn 2 police cars near the house
            Vector3 cop1Pos = houseExterior + new Vector3(15f, 5f, 0f);
            Vector3 cop2Pos = houseExterior + new Vector3(-15f, -5f, 0f);

            uint policeModel = (uint)GetHashKey("police");
            RequestModel(policeModel);

            // Note: In a real implementation, you'd wait for model to load
            // For now, just attempt to spawn
            CreateVehicle(policeModel, cop1Pos.X, cop1Pos.Y, cop1Pos.Z, 0f, true, true);
            CreateVehicle(policeModel, cop2Pos.X, cop2Pos.Y, cop2Pos.Z, 180f, true, true);

            SetModelAsNoLongerNeeded(policeModel);
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

            // Clear cameras and loot
            cameraManager.ClearCameras();
            cameraManager.ResetAlarm();
            lootManager.LootItems.Clear();

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
