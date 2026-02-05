using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum RobberyState
    {
        None,
        Planning,
        Active,
        Vault,
        Escape,
        Completed,
        Failed
    }

    public class BankRobberyManager
    {
        private HostageSystem hostageSystem;
        private GuardSystem guardSystem;
        private LootManager lootManager;
        private CameraManager cameraManager;
        private Lockpicking lockpicking;
        private VaultDoorSystem vaultDoorSystem;

        private Vector3 vaultDoorPosition = new Vector3(255.2f, 223.2f, 102.3f);


        public RobberyState CurrentState { get; private set; } = RobberyState.None;
        public bool IsRobberyActive => CurrentState != RobberyState.None && CurrentState != RobberyState.Completed && CurrentState != RobberyState.Failed;

        // Bank locations (Union Depository inspired)
        private Vector3 bankExterior = new Vector3(226.4f, 211.6f, 105.5f);
        private Vector3 bankLobby = new Vector3(252.0f, 217.5f, 106.3f);
        private Vector3 vaultEntrance = new Vector3(263.5f, 214.0f, 101.7f);

        public BankRobberyManager(LootManager lootManager, CameraManager cameraManager, Lockpicking lockpicking)
        {
            this.lootManager = lootManager;
            this.cameraManager = cameraManager;
            this.lockpicking = lockpicking;

            hostageSystem = new HostageSystem();
            guardSystem = new GuardSystem();

            // Subscribe to guard alerts
            guardSystem.OnAllGuardsAlerted += OnGuardsAlerted;

            vaultDoorSystem = new VaultDoorSystem(); // Add this
            vaultDoorSystem.OnDoorOpened += OnVaultDoorOpened;
        }

        public void StartBankRobbery(string approach = "loud")
        {
            if (IsRobberyActive)
            {
                Screen.ShowNotification("~r~Bank robbery already active!");
                return;
            }

            CurrentState = RobberyState.Planning;

            Screen.ShowNotification("~b~Thieves Guild:~w~ Time for the big score. Union Depository is the target.");
            Screen.ShowNotification("~y~Setting up the heist...");

            SetupBankHeist(approach);
        }

        private async void SetupBankHeist(string approach)
        {
            try
            {
                // Initialize systems
                guardSystem.Initialize();

                // Setup guards with patrol paths
                await SetupGuards();

                vaultDoorSystem.Initialize();

                // Setup hostages
                var crewStandByPosition = bankLobby + new Vector3(5f, 0f, 0f);
                hostageSystem.Initialize(crewStandByPosition, approach);

                // Subscribe to hostage events for LOUD missions only
                if (approach.ToLower() == "loud")
                {
                    hostageSystem.OnPoliceBreach += OnPoliceBreach;
                    hostageSystem.OnHostageReleased += OnHostageReleased;
                    Screen.ShowNotification("~r~LOUD ROBBERY: Police will respond! Use hostages strategically!");
                }
                else
                {
                    Screen.ShowNotification("~b~QUIET ROBBERY: Stay undetected!");
                }

                // Setup loot
                SetupBankLoot();

                CurrentState = RobberyState.Active;
            }
            catch (Exception ex)
            {
                Screen.ShowNotification("~r~Failed to setup bank robbery!");
                Debug.WriteLine($"[BANK] Setup error: {ex.Message}");
                CurrentState = RobberyState.Failed;
            }
        }

        private void OnPoliceBreach()
        {
            CurrentState = RobberyState.Failed;
            Screen.ShowNotification("~r~HEIST FAILED: Police breached the building!");
            FailRobbery();
        }

        private void OnHostageReleased(int newPoliceLevel)
        {
            Debug.WriteLine($"[BANK] Hostage released, police pressure now at {newPoliceLevel}%");
            Screen.ShowNotification($"~g~Strategic hostage release! Police pressure: {newPoliceLevel}%");
        }

        private async Task SetupGuards()
        {
            // Create patrol paths for guards
            var lobbyPatrol = new List<PatrolNode>
            {
                new PatrolNode(bankLobby + new Vector3(-3f, 2f, 0f), 90f, 4f, true),
                new PatrolNode(bankLobby + new Vector3(3f, 2f, 0f), 270f, 3f),
                new PatrolNode(bankLobby + new Vector3(3f, -3f, 0f), 180f, 5f, true),
                new PatrolNode(bankLobby + new Vector3(-3f, -3f, 0f), 0f, 3f)
            };

            var vaultPatrol = new List<PatrolNode>
            {
                new PatrolNode(vaultEntrance + new Vector3(0f, 2f, 0f), 180f, 6f, true),
                new PatrolNode(vaultEntrance + new Vector3(-2f, 0f, 0f), 90f, 4f, true)
            };

            // Spawn guards with their patrols
            guardSystem.AddGuard(bankLobby + new Vector3(-2f, 1f, 0f), lobbyPatrol);
            await BaseScript.Delay(500);

            guardSystem.AddGuard(vaultEntrance + new Vector3(1f, 1f, 0f), vaultPatrol);
            await BaseScript.Delay(500);

            // Static guard at entrance
            guardSystem.AddGuard(bankExterior + new Vector3(1f, -2f, 0f));
        }

        private void SetupBankLoot()
        {
            // Clear existing loot
            lootManager.LootItems.Clear();

            // Add bank loot
            lootManager.AddLootItem(new LootItem("Cash", bankLobby + new Vector3(2f, -1f, 0f), 5));
            lootManager.AddLootItem(new LootItem("Jewelry", bankLobby + new Vector3(-1f, -2f, 0f), 3));
            lootManager.AddLootItem(new LootItem("Electronics", vaultEntrance + new Vector3(-1f, -1f, 0f), 2));
            lootManager.AddLootItem(new LootItem("Cash", vaultEntrance + new Vector3(2f, 0f, 0f), 10));

            Debug.WriteLine("[BANK] Bank loot setup complete");
        }

        public void Update()
        {
            if (!IsRobberyActive) return;

            guardSystem.Update();
            hostageSystem.Update();
            vaultDoorSystem.Update();

            CheckVaultDoorInteraction();

            // Draw debug info
            guardSystem.DrawDebugInfo();
        }

        private void OnGuardsAlerted()
        {
            Screen.ShowNotification("~r~GUARDS ALERTED! This is now a loud robbery!");
            // Could trigger additional systems here like police response
        }

        public void EndRobbery()
        {
            CurrentState = RobberyState.Completed;
            CleanupRobbery();
            Screen.ShowNotification("~g~Bank robbery completed!");
        }

        public void FailRobbery()
        {
            CurrentState = RobberyState.Failed;
            CleanupRobbery();
            Screen.ShowNotification("~r~Bank robbery failed!");
        }

        private void CheckVaultDoorInteraction()
        {
            if (!IsRobberyActive) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            // Use terminal position instead of door position (from Lua)
            Vector3 terminalPosition = new Vector3(253.3081f, 228.4226f, 101.6833f);
            var distanceToTerminal = Vector3.Distance(playerPos, terminalPosition);

            if (distanceToTerminal < 2f)
            {
                if (vaultDoorSystem.State == VaultDoorState.Closed && !vaultDoorSystem.IsUnlocked)
                {
                    Screen.DisplayHelpTextThisFrame("Press ~INPUT_CONTEXT~ to hack vault terminal");

                    if (IsControlJustPressed(0, 51)) // E key
                    {
                        // Check if all hostages are secured first
                        if (hostageSystem.AllHostagesCrouched)
                        {
                            vaultDoorSystem.StartHacking();
                        }
                        else
                        {
                            Screen.ShowNotification("~r~Secure all hostages first!");
                        }
                    }
                }
                else if (vaultDoorSystem.IsUnlocked)
                {
                    Screen.DisplayHelpTextThisFrame("Terminal hacked! Use arrow keys to control vault door.");
                }
            }
        }


        private void OnVaultDoorOpened()
        {
            CurrentState = RobberyState.Vault;
            Screen.ShowNotification("~g~VAULT ACCESS GRANTED!");

            // Add vault loot
            AddVaultLoot();
        }

        private void AddVaultLoot()
        {
            // Add high-value vault loot
            Vector3 vaultInterior = vaultDoorPosition + new Vector3(0f, -5f, 0f);

            lootManager.AddLootItem(new LootItem("Cash", vaultInterior + new Vector3(-1f, 0f, 0f), 20));
            lootManager.AddLootItem(new LootItem("Jewelry", vaultInterior + new Vector3(1f, 0f, 0f), 10));
            lootManager.AddLootItem(new LootItem("Cash", vaultInterior + new Vector3(0f, -1f, 0f), 25));
            lootManager.AddLootItem(new LootItem("Electronics", vaultInterior + new Vector3(-1f, -1f, 0f), 5));

            Debug.WriteLine("[BANK] Vault loot added");
        }

        private void CleanupRobbery()
        {
            guardSystem?.Cleanup();
            hostageSystem?.Cleanup();
            lootManager?.LootItems?.Clear();
            vaultDoorSystem?.Cleanup();
            Debug.WriteLine("[BANK] Bank robbery cleaned up");
        }
    }
}
