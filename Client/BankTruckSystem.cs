using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum BankTruckState
    {
        NotSpawned,
        Spawned,
        DrivingToHijackLocation,
        AtHijackLocation,
        PlayerNearby,
        Stolen,
        AtBank,
        BankEntryGranted
    }

    public class BankTruckSystem
    {
        private Vector3 truckSpawnLocation = new Vector3(-105.5f, 71.5f, 71.3f);
        private Vector3 hijackLocation = new Vector3(884.411f, 409.451f, 87.0f); // Isolated hijack spot
        private Vector3 bankDeliveryPoint = new Vector3(253.8f, 191.1f, 104.9f);
        private Vector3 bankEntrancePoint = new Vector3(252.0f, 217.5f, 106.3f);

        private int bankTruckVehicle = 0;
        private int truckDriverPed = 0;
        private int truckGuardPed = 0;
        private BankTruckState currentState = BankTruckState.NotSpawned;
        private bool isPlayerInTruck = false;
        private bool hasDisguisedPlayer = false;

        // Store original player appearance for restoration
        private uint originalPlayerModel = 0;
        private bool wasPlayerModelChanged = false;

        private WaypointSystem waypointSystem;
        private BankGateSystem bankGateSystem;

        // Events
        public event Action OnTruckStolen;
        public event Action OnTruckArrivedAtBank;
        public event Action OnBankEntryReady;

        public BankTruckState State => currentState;
        public bool IsPlayerInTruck => isPlayerInTruck;

        public BankTruckSystem(WaypointSystem waypoints, BankGateSystem gateSystem)
        {
            waypointSystem = waypoints;
            bankGateSystem = gateSystem;
        }

        public async void Initialize()
        {
            currentState = BankTruckState.NotSpawned;

            // Store original player model
            originalPlayerModel = (uint)GetEntityModel(PlayerPedId());

            // Lock the bank gates while truck is being stolen
            //bankGateSystem.LockGates();
            Screen.ShowNotification("~r~Bank is locked down during delivery schedule!");

            await SpawnBankTruck();

            Debug.WriteLine("[BANK_TRUCK] Bank truck system initialized");
        }

        private async Task SpawnBankTruck()
        {
            uint truckModel = (uint)GetHashKey("stockade");
            uint driverModel = (uint)GetHashKey("s_m_m_armoured_01");
            uint guardModel = (uint)GetHashKey("s_m_m_armoured_02");

            if (!await LoadModel(truckModel) || !await LoadModel(driverModel) || !await LoadModel(guardModel))
            {
                Debug.WriteLine("[BANK_TRUCK] Failed to load truck models");
                return;
            }

            float groundZ = truckSpawnLocation.Z;
            GetGroundZFor_3dCoord(truckSpawnLocation.X, truckSpawnLocation.Y, truckSpawnLocation.Z + 10f, ref groundZ, false);
            Vector3 spawnPos = new Vector3(truckSpawnLocation.X, truckSpawnLocation.Y, groundZ);

            bankTruckVehicle = CreateVehicle(truckModel, spawnPos.X, spawnPos.Y, spawnPos.Z, 0f, true, false);

            if (DoesEntityExist(bankTruckVehicle))
            {
                SetEntityAsMissionEntity(bankTruckVehicle, true, true);
                SetVehicleOnGroundProperly(bankTruckVehicle);
                SetVehicleEngineOn(bankTruckVehicle, true, true, false);

                // Spawn driver
                truckDriverPed = CreatePed(4, driverModel, spawnPos.X, spawnPos.Y, spawnPos.Z, 0f, true, true);
                if (DoesEntityExist(truckDriverPed))
                {
                    SetEntityAsMissionEntity(truckDriverPed, true, true);
                    SetPedIntoVehicle(truckDriverPed, bankTruckVehicle, -1);
                    GiveWeaponToPed(truckDriverPed, (uint)GetHashKey("weapon_pistol"), 50, false, true);
                    SetPedCombatAttributes(truckDriverPed, 46, true);
                    SetPedFleeAttributes(truckDriverPed, 0, false);
                    SetPedKeepTask(truckDriverPed, true);
                }

                // Spawn guard
                truckGuardPed = CreatePed(4, guardModel, spawnPos.X, spawnPos.Y, spawnPos.Z, 0f, true, true);
                if (DoesEntityExist(truckGuardPed))
                {
                    SetEntityAsMissionEntity(truckGuardPed, true, true);
                    SetPedIntoVehicle(truckGuardPed, bankTruckVehicle, 0);
                    GiveWeaponToPed(truckGuardPed, (uint)GetHashKey("weapon_carbinerifle"), 100, false, true);
                    SetPedCombatAttributes(truckGuardPed, 46, true);
                    SetPedFleeAttributes(truckGuardPed, 0, false);
                }

                // Make truck drive to hijack location
                StartTruckRoute();

                currentState = BankTruckState.DrivingToHijackLocation;
                Screen.ShowNotification("~y~Bank truck is en route to delivery stop...");
                Debug.WriteLine($"[BANK_TRUCK] Spawned bank truck at {spawnPos}, driving to hijack location");
            }

            SetModelAsNoLongerNeeded(truckModel);
            SetModelAsNoLongerNeeded(driverModel);
            SetModelAsNoLongerNeeded(guardModel);
        }

        private void StartTruckRoute()
        {
            if (DoesEntityExist(truckDriverPed) && DoesEntityExist(bankTruckVehicle))
            {
                // Set waypoint to hijack location for player to follow
                waypointSystem.SetObjectiveWaypoint(hijackLocation, "Intercept Bank Truck", 1); // Red

                // Make driver navigate to hijack location
                TaskVehicleDriveToCoord(truckDriverPed, bankTruckVehicle,
                    hijackLocation.X, hijackLocation.Y, hijackLocation.Z,
                    25f, 0, (uint)GetEntityModel(bankTruckVehicle),
                    786603, 2f, 1);

                Screen.ShowNotification("~y~Follow the truck to intercept it!");
                Debug.WriteLine($"[BANK_TRUCK] Truck driving to hijack location: {hijackLocation}");
            }
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

        public void Update()
        {
            if (!DoesEntityExist(bankTruckVehicle)) return;

            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            var truckPos = GetEntityCoords(bankTruckVehicle, true);
            float distanceToTruck = Vector3.Distance(playerPos, truckPos);
            float distanceToHijackLocation = Vector3.Distance(truckPos, hijackLocation);

            switch (currentState)
            {
                case BankTruckState.DrivingToHijackLocation:
                    CheckTruckArrivalAtHijackLocation(distanceToHijackLocation);
                    break;

                case BankTruckState.AtHijackLocation:
                    if (distanceToTruck < 15f)
                    {
                        currentState = BankTruckState.PlayerNearby;
                        Screen.ShowNotification("~y~Eliminate the guards to steal the bank truck!");
                    }
                    break;

                case BankTruckState.PlayerNearby:
                    CheckTruckStealing();
                    break;

                case BankTruckState.Stolen:
                    CheckBankArrival();
                    break;

                case BankTruckState.AtBank:
                    CheckBankEntry();
                    break;
            }

            // Check if player enters truck and apply disguise
            bool playerCurrentlyInTruck = GetVehiclePedIsIn(playerPed, false) == bankTruckVehicle;
            if (playerCurrentlyInTruck != isPlayerInTruck)
            {
                isPlayerInTruck = playerCurrentlyInTruck;

                if (isPlayerInTruck && currentState == BankTruckState.Stolen)
                {
                    ApplyPlayerDisguise();
                    Screen.ShowNotification("~g~Drive to the bank delivery point!");
                }
                else if (!isPlayerInTruck && hasDisguisedPlayer)
                {
                    //  Remove disguise when exiting truck
                    // RemovePlayerDisguise();
                }
            }
        }

        private void CheckTruckArrivalAtHijackLocation(float distanceToHijackLocation)
        {
            if (distanceToHijackLocation < 10f)
            {
                currentState = BankTruckState.AtHijackLocation;

                // Stop the truck
                if (DoesEntityExist(truckDriverPed))
                {
                    ClearPedTasks(truckDriverPed);
                    TaskVehiclePark(truckDriverPed, bankTruckVehicle, hijackLocation.X, hijackLocation.Y, hijackLocation.Z, GetEntityHeading(bankTruckVehicle), 0, 20f, false);
                }

                // Update waypoint to truck location
                waypointSystem.SetObjectiveWaypoint(hijackLocation, "Hijack Bank Truck", 1);

                Screen.ShowNotification("~r~Bank truck stopped for security check! Move in!");
                Debug.WriteLine("[BANK_TRUCK] Truck arrived at hijack location");
            }
        }

        private void CheckTruckStealing()
        {
            bool driverAlive = DoesEntityExist(truckDriverPed) && !IsPedDeadOrDying(truckDriverPed, true);
            bool guardAlive = DoesEntityExist(truckGuardPed) && !IsPedDeadOrDying(truckGuardPed, true);

            if (!driverAlive && !guardAlive)
            {
                currentState = BankTruckState.Stolen;
                OnTruckStolen?.Invoke();

                waypointSystem.SetWaypoint(bankDeliveryPoint, "Bank Delivery Point");
                bankGateSystem.UnlockGates();

                Screen.ShowNotification("~g~Bank truck hijacked! Drive to the bank delivery point.");
                Screen.ShowNotification("~g~Bank security temporarily disabled due to delivery!");
                Debug.WriteLine("[BANK_TRUCK] Truck successfully stolen - gates unlocked");
            }
        }

        private void ApplyPlayerDisguise()
        {
            if (hasDisguisedPlayer) return;

            var playerPed = PlayerPedId();

            try
            {
                // Change player model to match bank driver
                uint driverModel = (uint)GetHashKey("s_m_m_armoured_01");

                if (IsModelValid(driverModel))
                {
                    RequestModel(driverModel);

                    // Wait for model to load
                    int attempts = 0;
                    while (!HasModelLoaded(driverModel) && attempts < 50)
                    {
                        BaseScript.Delay(100).Wait();
                        attempts++;
                    }

                    if (HasModelLoaded(driverModel))
                    {
                        // Store player's current position and vehicle
                        var playerPos = GetEntityCoords(playerPed, true);
                        var playerHeading = GetEntityHeading(playerPed);
                        int currentVehicle = GetVehiclePedIsIn(playerPed, false);

                        // Find which seat the player is in
                        int currentSeat = -2; // Default to no seat found

                        if (DoesEntityExist(currentVehicle) && currentVehicle == bankTruckVehicle)
                        {
                            // Check all possible seats (-1 = driver, 0,1,2,3... = passengers)
                            for (int seat = -1; seat < 4; seat++)
                            {
                                if (GetPedInVehicleSeat(currentVehicle, seat) == playerPed)
                                {
                                    currentSeat = seat;
                                    break;
                                }
                            }
                        }

                        // Change model
                        SetPlayerModel(PlayerId(), driverModel);

                        // Get new ped after model change
                        playerPed = PlayerPedId();

                        // Put back in vehicle if we found the seat
                        if (DoesEntityExist(currentVehicle) && currentVehicle == bankTruckVehicle && currentSeat != -2)
                        {
                            SetPedIntoVehicle(playerPed, currentVehicle, currentSeat);
                        }

                        // Give appropriate weapon
                        GiveWeaponToPed(playerPed, (uint)GetHashKey("weapon_pistol"), 100, false, true);

                        hasDisguisedPlayer = true;
                        wasPlayerModelChanged = true;

                        Screen.ShowNotification("~g~Disguised as bank security driver!");
                        Debug.WriteLine($"[BANK_TRUCK] Player disguised as bank driver in seat {currentSeat}");
                    }

                    SetModelAsNoLongerNeeded(driverModel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK_TRUCK] Error applying disguise: {ex.Message}");
                // fallback
                ApplyDriverOutfit();
            }
        }


        private void ApplyDriverOutfit()
        {
            var playerPed = PlayerPedId();

            // Apply security guard outfit components
            SetPedComponentVariation(playerPed, 11, 55, 0, 2); // Torso
            SetPedComponentVariation(playerPed, 4, 35, 0, 2);  // Legs
            SetPedComponentVariation(playerPed, 6, 25, 0, 2);  // Shoes
            SetPedComponentVariation(playerPed, 8, 58, 0, 2);  // Undershirt
            SetPedComponentVariation(playerPed, 3, 0, 0, 2);   // Arms

            // Add security hat if available
            SetPedPropIndex(playerPed, 0, 46, 0, true);

            hasDisguisedPlayer = true;
            Screen.ShowNotification("~g~Changed into security uniform!");
            Debug.WriteLine("[BANK_TRUCK] Applied driver outfit to player");
        }

        private void RemovePlayerDisguise()
        {
            if (!hasDisguisedPlayer) return;

            try
            {
                if (wasPlayerModelChanged && originalPlayerModel != 0)
                {
                    var playerPos = GetEntityCoords(PlayerPedId(), true);
                    var playerHeading = GetEntityHeading(PlayerPedId());

                    // Restore original model
                    RequestModel(originalPlayerModel);

                    int attempts = 0;
                    while (!HasModelLoaded(originalPlayerModel) && attempts < 50)
                    {
                        BaseScript.Delay(100).Wait();
                        attempts++;
                    }

                    if (HasModelLoaded(originalPlayerModel))
                    {
                        SetPlayerModel(PlayerId(), originalPlayerModel);
                        SetEntityCoords(PlayerPedId(), playerPos.X, playerPos.Y, playerPos.Z, false, false, false, true);
                        SetEntityHeading(PlayerPedId(), playerHeading);
                    }

                    SetModelAsNoLongerNeeded(originalPlayerModel);
                }

                hasDisguisedPlayer = false;
                wasPlayerModelChanged = false;
                Screen.ShowNotification("~y~Disguise removed");
                Debug.WriteLine("[BANK_TRUCK] Player disguise removed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK_TRUCK] Error removing disguise: {ex.Message}");
            }
        }

        private void CheckBankArrival()
        {
            if (!isPlayerInTruck) return;

            var truckPos = GetEntityCoords(bankTruckVehicle, true);
            float distanceToBank = Vector3.Distance(truckPos, bankDeliveryPoint);

            if (distanceToBank < 5f)
            {
                currentState = BankTruckState.AtBank;
                OnTruckArrivedAtBank?.Invoke();

                waypointSystem.SetObjectiveWaypoint(bankEntrancePoint, "Bank Entrance", 2); // Green

                Screen.ShowNotification("~g~Arrived at bank! Exit truck and enter through main entrance.");
                Debug.WriteLine("[BANK_TRUCK] Truck arrived at bank delivery point");
            }
        }

        private void CheckBankEntry()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToEntrance = Vector3.Distance(playerPos, bankEntrancePoint);

            if (distanceToEntrance < 3f)
            {
                currentState = BankTruckState.BankEntryGranted;
                OnBankEntryReady?.Invoke();

                waypointSystem.ClearAllWaypoints();

                Screen.ShowNotification("~g~Entered bank! The manager will assist you to the vault.");
                Debug.WriteLine("[BANK_TRUCK] Player entered bank - stealth entry complete");
            }
        }

        public void Cleanup()
        {
            // Restore player appearance if changed
            if (hasDisguisedPlayer)
            {
                RemovePlayerDisguise();
            }

            if (DoesEntityExist(bankTruckVehicle))
            {
                int vehicleId = bankTruckVehicle;
                DeleteVehicle(ref vehicleId);
            }

            if (DoesEntityExist(truckDriverPed))
            {
                int driverId = truckDriverPed;
                DeletePed(ref driverId);
            }

            if (DoesEntityExist(truckGuardPed))
            {
                int guardId = truckGuardPed;
                DeletePed(ref guardId);
            }

            currentState = BankTruckState.NotSpawned;
            isPlayerInTruck = false;
            hasDisguisedPlayer = false;
            wasPlayerModelChanged = false;

            Debug.WriteLine("[BANK_TRUCK] Bank truck system cleaned up");
        }
    }
}
