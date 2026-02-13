using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum BankManagerState
    {
        Normal,
        EscortingPlayer,
        AtVault,
        Hostile
    }

    public class BankManagerSystem
    {
        private int bankManagerPed = 0;
        private BankManagerState currentState = BankManagerState.Normal;
        private Vector3 managerPosition = new Vector3(253.3f, 225.8f, 106.3f);
        private Vector3 vaultHackingPosition = new Vector3(253.7f, 228.1f, 101.7f);

        // Gate positions
        private Vector3 gate1Position = new Vector3(262.1f, 222.5f, 106.3f);
        private Vector3 gate2Position = new Vector3(261.8f, 215.7f, 106.3f);

        private bool hasGreetedPlayer = false;
        private bool isEscorting = false;
        private bool hasBetrayed = false;
        private bool isLoudMission = false;
        private bool hasUnlockedGates = false;

        private BankGateSystem gateSystem;

        // Events
        public event Action OnBankManagerBetrayal;
        public event Action OnGatesUnlocked;

        public BankManagerState State => currentState;
        public bool HasBetrayed => hasBetrayed;

        public BankManagerSystem(BankGateSystem bankGateSystem = null)
        {
            gateSystem = bankGateSystem;
        }

        public async void Initialize(bool loudMission = false)
        {
            isLoudMission = loudMission;
            await SpawnBankManager();
            currentState = BankManagerState.Normal;
            Debug.WriteLine($"[BANK_MANAGER] Bank manager system initialized for {(loudMission ? "LOUD" : "STEALTH")} mission");
        }

        private async Task SpawnBankManager()
        {
            uint managerModel = (uint)GetHashKey("cs_bankman");

            if (!await LoadModel(managerModel))
            {
                Debug.WriteLine("[BANK_MANAGER] Failed to load bank manager model");
                return;
            }

            float groundZ = managerPosition.Z;
            GetGroundZFor_3dCoord(managerPosition.X, managerPosition.Y, managerPosition.Z, ref groundZ, false);
            Vector3 spawnPos = new Vector3(managerPosition.X, managerPosition.Y, groundZ);

            bankManagerPed = CreatePed(4, managerModel, spawnPos.X, spawnPos.Y, spawnPos.Z, 163.2f, true, true);

            if (DoesEntityExist(bankManagerPed))
            {
                SetEntityAsMissionEntity(bankManagerPed, true, true);
                SetPedFleeAttributes(bankManagerPed, 0, false);
                SetPedCombatAttributes(bankManagerPed, 17, true);
                SetPedCanRagdoll(bankManagerPed, false);
                TaskStandStill(bankManagerPed, -1);

                Debug.WriteLine($"[BANK_MANAGER] Spawned bank manager at {spawnPos}");
            }

            SetModelAsNoLongerNeeded(managerModel);
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
            if (!DoesEntityExist(bankManagerPed)) return;

            if (!DoesEntityExist(bankManagerPed)) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var managerPos = GetEntityCoords(bankManagerPed, true);
            float distanceToPlayer = Vector3.Distance(playerPos, managerPos);

            if (isLoudMission)
            {
                UpdateLoudMission(distanceToPlayer);
            }
            else
            {
                UpdateStealthMission(distanceToPlayer);
            }
        }

        private void UpdateLoudMission(float distanceToPlayer)
        {
            switch (currentState)
            {
                case BankManagerState.Normal:
                    // Manager cowers in fear during loud mission
                    if (distanceToPlayer < 8f && !hasGreetedPlayer)
                    {
                        hasGreetedPlayer = true;
                        Screen.ShowNotification("~r~Bank Manager: Please don't hurt anyone! I'll cooperate!");

                        // Make manager cower
                        ClearPedTasks(bankManagerPed);
                        SetPedCowerHash(bankManagerPed, "CODE_HUMAN_STAND_COWER");
                    }
                    break;

                case BankManagerState.EscortingPlayer:
                    //UpdateGateUnlocking();
                    break;
            }
        }

        private void UpdateStealthMission(float distanceToPlayer)
        {
            switch (currentState)
            {
                case BankManagerState.Normal:
                    CheckPlayerApproach(distanceToPlayer);
                    break;

                case BankManagerState.EscortingPlayer:
                    UpdateEscorting();
                    CheckVaultArrival();
                    break;

                case BankManagerState.AtVault:
                    CheckBetrayal();
                    break;
            }
        }

        public void ForceUnlockGates()
        {
            if (hasUnlockedGates || !isLoudMission) return;

            currentState = BankManagerState.EscortingPlayer;
            hasUnlockedGates = true;

            Screen.ShowNotification("~r~Bank Manager: Okay! I'll unlock the gates! Please don't hurt the hostages!");

            // Clear any current tasks
            ClearPedTasks(bankManagerPed);

            // Start moving to first gate
            MoveToGate1();

            Debug.WriteLine("[BANK_MANAGER] Manager forced to unlock gates");
        }

        private async void MoveToGate1()
        {
            Screen.ShowNotification("~y~The bank manager is moving to unlock the first gate...");

            // Move to gate 1
            TaskGoToCoordAnyMeans(bankManagerPed, gate1Position.X, gate1Position.Y, gate1Position.Z, 2.0f, 0, false, 786603, 0f);

            // Wait for arrival at gate 1
            while (Vector3.Distance(GetEntityCoords(bankManagerPed, true), gate1Position) > 2f)
            {
                await BaseScript.Delay(500);
                if (!DoesEntityExist(bankManagerPed)) return;
            }

            await UnlockGate1();
        }

        private async Task UnlockGate1()
        {
            //Screen.ShowNotification("~g~Bank Manager: Unlocking first gate...");

            // Animation for unlocking
            TaskStandStill(bankManagerPed, 3000);
            await BaseScript.Delay(3000);

            // Unlock gate 1 through gate system
            if (gateSystem != null)
            {
                gateSystem.UnlockGate1();
            }

            //Screen.ShowNotification("~g~First vault gate unlocked!");

            // Move to gate 2
            MoveToGate2();
        }

        private async void MoveToGate2()
        {
           // Screen.ShowNotification("~y~The bank manager is moving to unlock the second gate...");

            // Move to gate 2
            TaskGoToCoordAnyMeans(bankManagerPed, gate2Position.X, gate2Position.Y, gate2Position.Z, 2.0f, 0, false, 786603, 0f);

            // Wait for arrival at gate 2
            while (Vector3.Distance(GetEntityCoords(bankManagerPed, true), gate2Position) > 2f)
            {
                await BaseScript.Delay(500);
                if (!DoesEntityExist(bankManagerPed)) return;
            }

            await UnlockGate2();
        }

        private async Task UnlockGate2()
        {
            //Screen.ShowNotification("~g~Bank Manager: Unlocking second gate...");

            // Animation for unlocking
            TaskStandStill(bankManagerPed, 3000);
            await BaseScript.Delay(3000);

            // Unlock gate 2 through gate system
            if (gateSystem != null)
            {
                gateSystem.UnlockGate2();
            }

            Screen.ShowNotification("~g~Second vault gate unlocked! The manager is fleeing!");

            // Trigger escape behavior
            OnGatesUnlocked?.Invoke();
            ManagerEscape();
        }

        private void ManagerEscape()
        {
            currentState = BankManagerState.Normal; // Reset state

            // Make manager run away
            ClearPedTasks(bankManagerPed);
            SetPedFleeAttributes(bankManagerPed, 0, true);
            TaskSmartFleePed(bankManagerPed, PlayerPedId(), 100f, -1, false, false);

            //Screen.ShowNotification("~y~The bank manager has fled the scene!");
            Debug.WriteLine("[BANK_MANAGER] Manager escaped after unlocking gates");
        }

        private void CheckPlayerApproach(float distanceToPlayer)
        {
            if (distanceToPlayer < 5f && !hasGreetedPlayer)
            {
                hasGreetedPlayer = true;

                // Manager greets player
                Screen.ShowNotification("~b~Bank Manager: Welcome! I'll escort you to the vault for the delivery.");

                // Start escorting player
                currentState = BankManagerState.EscortingPlayer;
                isEscorting = true;

                Debug.WriteLine("[BANK_MANAGER] Manager greeting player and starting escort");
            }
        }

        private void UpdateEscorting()
        {
            if (!isEscorting) return;

            var playerPed = PlayerPedId();

            // Make manager follow player towards vault
            TaskFollowToOffsetOfEntity(bankManagerPed, playerPed, 2f, 0f, 0f, 3f, -1, 1.5f, true);

            // give directions
            var currentTime = GetGameTimer() / 1000f;
            if (((int)currentTime % 15) == 0) // Every 15 seconds
            {
                var messages = new[]
                {
                    "This way to the vault, please.",
                    "The secure area is just ahead.",
                    "Please follow me downstairs.",
                    "Almost there, just through here."
                };

                var randomMessage = messages[new Random().Next(messages.Length)];
                Screen.ShowNotification($"~b~Bank Manager: {randomMessage}");
            }
        }

        private void CheckVaultArrival()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToVault = Vector3.Distance(playerPos, vaultHackingPosition);

            if (distanceToVault < 4f)
            {
                currentState = BankManagerState.AtVault;
                isEscorting = false;

                // Manager stops and positions near vault
                ClearPedTasks(bankManagerPed);
                TaskStandStill(bankManagerPed, -1);

                Screen.ShowNotification("~b~Bank Manager: Here's the vault access terminal. Please proceed.");
                Debug.WriteLine("[BANK_MANAGER] Arrived at vault - ready for betrayal");
            }
        }

        private void CheckBetrayal()
        {
            if (hasBetrayed) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToHackingPos = Vector3.Distance(playerPos, vaultHackingPosition);

            // Trigger betrayal when player gets very close to hacking terminal
            if (distanceToHackingPos < 2f)
            {
                TriggerBetrayal();
            }
        }

        private void TriggerBetrayal()
        {
            if (hasBetrayed) return;

            hasBetrayed = true;
            currentState = BankManagerState.Hostile;

            // Manager realizes this isn't a real delivery
            Screen.ShowNotification("~r~Bank Manager: Wait... you're not from the transport company!");
            Screen.ShowNotification("~r~Bank Manager: SECURITY! WE'RE BEING ROBBED!");

            // Make manager hostile and armed
            ClearPedTasks(bankManagerPed);
            SetPedCombatAttributes(bankManagerPed, 17, false); // Remove passive
            SetPedCombatAttributes(bankManagerPed, 46, true);   // Use cover
            GiveWeaponToPed(bankManagerPed, (uint)GetHashKey("weapon_pistol"), 50, false, true);

            // Manager tries to get to cover and call for help
            SetPedRelationshipGroupHash(bankManagerPed, (uint)GetHashKey("SECURITY_GUARD"));
            SetRelationshipBetweenGroups(5, (uint)GetHashKey("SECURITY_GUARD"), (uint)GetHashKey("PLAYER"));

            TaskCombatPed(bankManagerPed, PlayerPedId(), 0, 16);

            OnBankManagerBetrayal?.Invoke();

            Debug.WriteLine("[BANK_MANAGER] Bank manager betrayal triggered - combat initiated");
        }

        public void Cleanup()
        {
            if (DoesEntityExist(bankManagerPed))
            {
                int managerId = bankManagerPed;
                DeletePed(ref managerId);
            }

            currentState = BankManagerState.Normal;
            hasGreetedPlayer = false;
            isEscorting = false;
            hasBetrayed = false;

            Debug.WriteLine("[BANK_MANAGER] Bank manager system cleaned up");
        }
    }
}
