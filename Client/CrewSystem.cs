using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class CrewMember
    {
        public int PedId { get; set; }
        public string Name { get; set; }
        public uint Model { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public bool IsFollowing { get; set; }
        public int LastVehicle { get; set; }

        public bool IsAlive => DoesEntityExist(PedId) && !IsPedDeadOrDying(PedId, true);
        public bool IsInVehicle => IsPedInAnyVehicle(PedId, false);

        public CrewMember(int pedId, string name, uint model, Vector3 spawnPos)
        {
            PedId = pedId;
            Name = name;
            Model = model;
            SpawnPosition = spawnPos;
            IsFollowing = false;
            LastVehicle = 0;
        }
    }

    public class CrewSystem
    {
        private List<CrewMember> crew = new List<CrewMember>();
        private bool isActive = false;
        private bool isLoudMission = false;

        // Crew models
        private readonly CrewMemberData[] loudCrewModels = {
            new CrewMemberData("Zoey", "a_f_y_business_02", "Lets make this snappy."),
            new CrewMemberData("Connor", "g_m_y_ballaeast_01", "Let's get this money!"),
            new CrewMemberData("Snake", "g_m_y_mexgoon_02", "Time to get paid!")
        };

        private readonly CrewMemberData[] stealthCrewModels = {
            new CrewMemberData("Snake", "s_m_y_blackops_01", "im solid snake im  here to help")
        };

        // Events
        public event Action<string> OnCrewMemberDown;
        public event Action OnAllCrewDown;

        public IReadOnlyList<CrewMember> Crew => crew.AsReadOnly();
        public bool IsActive => isActive;
        public int AliveCrewCount => crew.Count(c => c.IsAlive);

        private class CrewMemberData
        {
            public string Name { get; }
            public string ModelName { get; }
            public string Catchphrase { get; }

            public CrewMemberData(string name, string modelName, string catchphrase)
            {
                Name = name;
                ModelName = modelName;
                Catchphrase = catchphrase;
            }
        }

        public async Task SpawnCrew(Vector3 basePosition, bool isLoud)
        {
            if (isActive) Cleanup();

            isActive = true;
            isLoudMission = isLoud;

            CrewMemberData[] crewData = isLoud ? loudCrewModels : stealthCrewModels;

            //Screen.ShowNotification($"~b~Spawning {crewData.Length} crew member{(crewData.Length > 1 ? "s" : "")}...");
            Debug.WriteLine($"[CREW] Spawning {crewData.Length} crew members for {(isLoud ? "LOUD" : "STEALTH")} mission");

            for (int i = 0; i < crewData.Length; i++)
            {
                await SpawnCrewMember(crewData[i], basePosition, i);
                await BaseScript.Delay(500); // Stagger spawns
            }

            Screen.ShowNotification($"~g~Crew ready! {AliveCrewCount} member{(AliveCrewCount > 1 ? "s" : "")} standing by.");
            Debug.WriteLine($"[CREW] Successfully spawned {AliveCrewCount} crew members");
        }

        private async Task SpawnCrewMember(CrewMemberData data, Vector3 basePosition, int index)
        {
            uint model = (uint)GetHashKey(data.ModelName);

            if (!await LoadModel(model))
            {
                Debug.WriteLine($"[CREW] Failed to load model {data.ModelName} for {data.Name}");
                return;
            }

            // Position crew membe
            Vector3 offset = GetCrewOffset(index, crew.Count);
            Vector3 spawnPos = basePosition + offset;

            // Get ground Z
            float groundZ = spawnPos.Z;
            GetGroundZFor_3dCoord(spawnPos.X, spawnPos.Y, spawnPos.Z + 10f, ref groundZ, false);
            spawnPos = new Vector3(spawnPos.X, spawnPos.Y, groundZ);

            int ped = CreatePed(4, model, spawnPos.X, spawnPos.Y, spawnPos.Z, 0f, true, true);

            if (DoesEntityExist(ped))
            {
                SetEntityAsMissionEntity(ped, true, true);

                // Setup crew member
                SetupCrewMemberAI(ped, data, isLoudMission);

                var crewMember = new CrewMember(ped, data.Name, model, spawnPos);
                crew.Add(crewMember);

                Debug.WriteLine($"[CREW] Spawned {data.Name} (ID: {ped}) at {spawnPos}");

                // Crew member introduction
                await BaseScript.Delay(1000);
                Screen.ShowNotification($"~b~{data.Name}:~w~ {data.Catchphrase}");
            }
            else
            {
                Debug.WriteLine($"[CREW] Failed to create ped for {data.Name}");
            }

            SetModelAsNoLongerNeeded(model);
        }

        private Vector3 GetCrewOffset(int index, int totalCrew)
        {
            // Arrange crew 
            float angle = (360f / Math.Max(totalCrew, 1)) * index;
            float radians = angle * (float)(Math.PI / 180);
            float distance = 2f;

            return new Vector3(
                (float)(Math.Cos(radians) * distance),
                (float)(Math.Sin(radians) * distance),
                0f
            );
        }

        private void SetupCrewMemberAI(int ped, CrewMemberData data, bool isLoud)
        {
            // Basic setup
            SetPedCanRagdoll(ped, false);
            SetPedFleeAttributes(ped, 0, false);
            SetPedRelationshipGroupHash(ped, (uint)GetHashKey("PLAYER"));
            SetPedAsGroupMember(ped, GetPlayerGroup(PlayerId()));
            SetPedDefaultComponentVariation(ped); // Ensure proper model setup
            SetPedRandomComponentVariation(ped, false);
            SetEntityMaxHealth(ped, 500); // Increase health
            SetEntityHealth(ped, 500);
            SetPedCanBeDraggedOut(ped, false); // Don't let them be dragged from vehicles

            // Combat attributes
            SetPedCombatAttributes(ped, 46, true); 
            SetPedCombatAttributes(ped, 3, true);  
            SetPedCombatAttributes(ped, 1, true);  
            SetPedCombatAttributes(ped, 17, false);
            SetPedCombatMovement(ped, 2);          
            SetPedCombatRange(ped, 2);             

            // vehicle
            SetPedIntoVehicle(ped, 0, -2); // Reset vehicle state
            SetDriverAggressiveness(ped, 0.5f);
            SetDriverAbility(ped, 1.0f);

            // weapons
            if (isLoud)
            {
                switch (data.Name)
                {
                    case "Zoey":
                        GiveWeaponToPed(ped, (uint)GetHashKey("weapon_assaultrifle"), 300, false, true);
                        break;
                    case "Connor":
                        GiveWeaponToPed(ped, (uint)GetHashKey("weapon_carbinerifle"), 250, false, true);
                        break;
                    case "Snake":
                        GiveWeaponToPed(ped, (uint)GetHashKey("weapon_pumpshotgun"), 100, false, true);
                        break;
                }
            }
            else
            {
                GiveWeaponToPed(ped, (uint)GetHashKey("weapon_pistol"), 100, false, true);
                GiveWeaponComponentToPed(ped, (uint)GetHashKey("weapon_pistol"), (uint)GetHashKey("COMPONENT_AT_PI_SUPP_02"));
            }

            // Set relationships
            SetRelationshipBetweenGroups(0, (uint)GetHashKey("PLAYER"), (uint)GetHashKey("PLAYER"));
            SetRelationshipBetweenGroups(5, (uint)GetHashKey("PLAYER"), (uint)GetHashKey("SECURITY_GUARD"));
            SetRelationshipBetweenGroups(5, (uint)GetHashKey("PLAYER"), (uint)GetHashKey("COP"));

            Debug.WriteLine($"[CREW] Enhanced AI setup for {data.Name} - Loud: {isLoud}");
        }

        public void Update()
        {
            if (!isActive) return;

            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            int playerVehicle = GetVehiclePedIsIn(playerPed, false);

            foreach (var crewMember in crew.ToList())
            {
                if (!crewMember.IsAlive)
                {
                    HandleCrewMemberDown(crewMember);
                    continue;
                }

                UpdateCrewMemberBehavior(crewMember, playerPed, playerPos, playerVehicle);
            }

            // Check if all crew is down
            if (crew.Any() && !crew.Any(c => c.IsAlive))
            {
                HandleAllCrewDown();
            }
        }

        private void UpdateCrewMemberBehavior(CrewMember crewMember, int playerPed, Vector3 playerPos, int playerVehicle)
        {
            float distance = Vector3.Distance(GetEntityCoords(crewMember.PedId, true), playerPos);

            // Handle vehicle following
            if (DoesEntityExist(playerVehicle) && playerVehicle != 0)
            {
                HandleVehicleFollowing(crewMember, playerVehicle, distance);
            }
            else
            {
                // On foot following
                HandleFootFollowing(crewMember, playerPed, distance);
            }
        }

        private async void HandleVehicleFollowing(CrewMember crewMember, int playerVehicle, float distance)
        {
            // If crew member is not in any vehicle and player is in a vehicle
            if (!crewMember.IsInVehicle && distance < 25f) 
            {
                // Check if crew member is already busy with a task
                if (IsPedGettingIntoAVehicle(crewMember.PedId) ||
                    GetVehiclePedIsTryingToEnter(crewMember.PedId) != 0)
                {
                    Debug.WriteLine($"[CREW] {crewMember.Name} already trying to enter vehicle");
                    return; // Already trying to enter a vehicle
                }

                // Find empty seat
                int emptySeat = FindEmptySeat(playerVehicle);
                if (emptySeat != -99)
                {
                    Debug.WriteLine($"[CREW] {crewMember.Name} attempting to enter seat {emptySeat}");

                    ClearPedTasks(crewMember.PedId);
                    ClearPedTasksImmediately(crewMember.PedId);

                    // Stop any current movement
                    SetPedMoveRateOverride(crewMember.PedId, 1.0f);

                    //await BaseScript.Delay(200);

                    TaskEnterVehicle(crewMember.PedId, playerVehicle, 10000, emptySeat, 1.0f, 1, 0);
                    crewMember.LastVehicle = playerVehicle;

                    Debug.WriteLine($"[CREW] {crewMember.Name} ordered to enter vehicle seat {emptySeat} (distance: {distance:F1}m)");

                    // FALLBACK: If they don't enter after 5 seconds, warp them in
                    BaseScript.Delay(5000).ContinueWith(_ =>
                    {
                        if (!crewMember.IsInVehicle && DoesEntityExist(playerVehicle) && IsVehicleSeatFree(playerVehicle, emptySeat))
                        {
                            Debug.WriteLine($"[CREW] Force warping {crewMember.Name} into vehicle seat {emptySeat}");
                            SetPedIntoVehicle(crewMember.PedId, playerVehicle, emptySeat);
                        }
                    });
                }
                else
                {
                    Debug.WriteLine($"[CREW] No empty seats in player vehicle for {crewMember.Name}");

                    // If no seats available, make them follow on foot nearby
                    if (distance > 10f)
                    {
                        ClearPedTasks(crewMember.PedId);
                        TaskGoToEntity(crewMember.PedId, PlayerPedId(), -1, 5f, 1.5f, 1073741824, 0);
                    }
                }
            }
            // If crew member is in a different vehicle than player
            else if (crewMember.IsInVehicle && distance < 30f)
            {
                int crewVehicle = GetVehiclePedIsIn(crewMember.PedId, false);
                if (crewVehicle != playerVehicle && DoesEntityExist(crewVehicle))
                {
                    int emptySeat = FindEmptySeat(playerVehicle);
                    if (emptySeat != -99)
                    {
                        Debug.WriteLine($"[CREW] {crewMember.Name} switching from vehicle {crewVehicle} to {playerVehicle}");

                        // Exit current vehicle
                        ClearPedTasks(crewMember.PedId);
                        TaskLeaveVehicle(crewMember.PedId, crewVehicle, 0);

                        await BaseScript.Delay(2500); // Wait for exit

                        // Verify they exited and try to enter player vehicle
                        if (!IsPedInAnyVehicle(crewMember.PedId, false))
                        {
                            TaskEnterVehicle(crewMember.PedId, playerVehicle, 20000, emptySeat, 1.5f, 1, 0);
                            crewMember.LastVehicle = playerVehicle;

                            // Fallback warp after delay
                            BaseScript.Delay(4000).ContinueWith(_ =>
                            {
                                if (!crewMember.IsInVehicle && DoesEntityExist(playerVehicle) && IsVehicleSeatFree(playerVehicle, emptySeat))
                                {
                                    SetPedIntoVehicle(crewMember.PedId, playerVehicle, emptySeat);
                                }
                            });
                        }
                    }
                }
            }
            // If crew member is too far, teleport them closer
            else if (!crewMember.IsInVehicle && distance > 100f)
            {
                var playerPos = GetEntityCoords(PlayerPedId(), true);
                Vector3 teleportPos = playerPos + new Vector3(
                    GetRandomFloatInRange(-10f, 10f),
                    GetRandomFloatInRange(-10f, 10f),
                    0f
                );

                float groundZ = teleportPos.Z;
                GetGroundZFor_3dCoord(teleportPos.X, teleportPos.Y, teleportPos.Z + 10f, ref groundZ, false);
                teleportPos = new Vector3(teleportPos.X, teleportPos.Y, groundZ);

                SetEntityCoords(crewMember.PedId, teleportPos.X, teleportPos.Y, teleportPos.Z, false, false, false, true);
                Debug.WriteLine($"[CREW] Teleported {crewMember.Name} closer to player (was {distance:F1}m away)");
            }
        }


        private void HandleFootFollowing(CrewMember crewMember, int playerPed, float distance)
        {
            // If crew member is in a vehicle but player is on foot, exit vehicle
            if (crewMember.IsInVehicle && distance < 15f)
            {
                int crewVehicle = GetVehiclePedIsIn(crewMember.PedId, false);
                if (crewVehicle != 0)
                {
                    ClearPedTasks(crewMember.PedId);
                    TaskLeaveVehicle(crewMember.PedId, crewVehicle, 0);
                    Debug.WriteLine($"[CREW] {crewMember.Name} exiting vehicle to follow on foot");
                }
                return;
            }

            // Follow on foot if distance is reasonable
            if (distance > 3f && distance < 50f && !crewMember.IsInVehicle)
            {
                // Check if they're not already following or in combat
                if (!IsPedRunning(crewMember.PedId) && !IsPedInCombat(crewMember.PedId, playerPed))
                {
                    ClearPedTasks(crewMember.PedId); // Clear any stuck tasks

                    TaskFollowToOffsetOfEntity(crewMember.PedId, playerPed,
                        GetRandomFloatInRange(-4f, 4f), GetRandomFloatInRange(-6f, -2f), 0f,
                        2.5f, -1, 3f, true);

                    Debug.WriteLine($"[CREW] {crewMember.Name} following on foot (distance: {distance:F1}m)");
                }
            }
            // If too close, make them wait
            else if (distance <= 3f && !crewMember.IsInVehicle)
            {
                if (!IsPedStopped(crewMember.PedId))
                {
                    TaskStandStill(crewMember.PedId, 2000); // Wait for 2 seconds
                }
            }
        }



        private int FindEmptySeat(int vehicle)
        {
            if (!DoesEntityExist(vehicle)) return -99;

            // First check if driver seat is available (in case player is passenger)
            if (IsVehicleSeatFree(vehicle, -1))
            {
                return -1; // Driver seat
            }

            // Then check passenger seats: 0, 1, 2, 3...
            int maxPassengers = GetVehicleMaxNumberOfPassengers(vehicle);
            for (int seat = 0; seat < maxPassengers; seat++)
            {
                if (IsVehicleSeatFree(vehicle, seat))
                {
                    Debug.WriteLine($"[CREW] Found empty seat {seat} in vehicle (max passengers: {maxPassengers})");
                    return seat;
                }
            }

            Debug.WriteLine($"[CREW] No empty seats found in vehicle (checked driver + {maxPassengers} passenger seats)");
            return -99; // No empty seat found
        }

        private void HandleCrewMemberDown(CrewMember crewMember)
        {
            if (crew.Contains(crewMember))
            {
                crew.Remove(crewMember);
                OnCrewMemberDown?.Invoke(crewMember.Name);
                Screen.ShowNotification($"~r~{crewMember.Name} is down!");
                Debug.WriteLine($"[CREW] {crewMember.Name} eliminated from crew");
            }
        }

        private void HandleAllCrewDown()
        {
            OnAllCrewDown?.Invoke();
            Screen.ShowNotification("~r~Your entire crew has been eliminated!");
            Debug.WriteLine("[CREW] All crew members down - mission critical");
        }

        public void SetCrewCombatTarget(int target)
        {
            foreach (var crewMember in crew.Where(c => c.IsAlive))
            {
                if (DoesEntityExist(target))
                {
                    TaskCombatPed(crewMember.PedId, target, 0, 16);
                    Debug.WriteLine($"[CREW] {crewMember.Name} engaging target {target}");
                }
            }
        }

        public void OrderCrewToPosition(Vector3 position)
        {
            foreach (var crewMember in crew.Where(c => c.IsAlive))
            {
                ClearPedTasks(crewMember.PedId);
                TaskGoToCoordAnyMeans(crewMember.PedId, position.X, position.Y, position.Z, 1f, 0, false, 786603, 0f);
            }
            Debug.WriteLine($"[CREW] Ordered crew to position {position}");
        }

        private async Task<bool> LoadModel(uint model)
        {
            RequestModel(model);
            int attempts = 0;
            while (!HasModelLoaded(model) && attempts < 50)
            {
                await BaseScript.Delay(100);
                attempts++;
            }
            return HasModelLoaded(model);
        }

        public void DrawDebugInfo()
        {
            if (!isActive) return;

            // Draw crew status
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString($"CREW: {AliveCrewCount}/{crew.Count + (crew.Count == 0 ? AliveCrewCount : 0)} Active");
            DrawText(0.02f, 0.25f);

            // individual crew member info
            for (int i = 0; i < crew.Count; i++)
            {
                var crewMember = crew[i];
                string status = crewMember.IsAlive ? "ACTIVE" : "DOWN";
                string color = crewMember.IsAlive ? "g" : "r";

                SetTextScale(0.0f, 0.3f);
                SetTextEntry("STRING");
                AddTextComponentString($"~{color}~{crewMember.Name}: {status}");
                DrawText(0.02f, 0.29f + (i * 0.03f));
            }
        }

        public void Cleanup()
        {
            foreach (var crewMember in crew)
            {
                if (DoesEntityExist(crewMember.PedId))
                {
                    int pedId = crewMember.PedId;
                    DeletePed(ref pedId);
                }
            }

            crew.Clear();
            isActive = false;
            Debug.WriteLine("[CREW] Crew system cleaned up");
        }

        private float GetRandomFloatInRange(float min, float max)
        {
            Random rand = new Random();
            return min + (float)rand.NextDouble() * (max - min);
        }
    }
}

