using System;
using System.Collections.Generic;
using System.Linq;
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
        private BankGateSystem bankGateSystem;
        private StealthBankManager stealthBankManager;
        private CrewSystem crewSystem;
        private LoadoutSystem loadoutSystem;
        private BankManagerSystem bankManagerSystem;

        private Vector3 vaultDoorPosition = new Vector3(255.2f, 223.2f, 102.3f);


        public RobberyState CurrentState { get; private set; } = RobberyState.None;
        public bool IsRobberyActive => CurrentState != RobberyState.None && CurrentState != RobberyState.Completed && CurrentState != RobberyState.Failed;

        // Bank locations
        private Vector3 bankExterior = new Vector3(226.4f, 211.6f, 105.5f);
        private Vector3 bankLobby = new Vector3(252.0f, 217.5f, 106.3f);
        private Vector3 vaultEntrance = new Vector3(263.5f, 214.0f, 101.7f);

        private Vector3[] getawayPoints = {
        new Vector3(1024.674f, 490.244f, 62.0f),   // Checkpoint 1 
        new Vector3(1066.904f, 2046.646f, 32.0f),  // Checkpoint 2
    };
        private int currentGetawayPoint = 0;
        private bool isGetawayActive = false;
        private Vector3 dropOffPoint = new Vector3(278.113f, 2571.037f, 43.0f);
        private bool hasLostCops = false;
        private WaypointSystem waypointSystem;

        private bool hasPlayerApproachedBank = false;
        private bool hostagesSpawned = false;
        private const float BANK_APPROACH_DISTANCE = 100f;
        private string missionApproach = "";

        
        public BankGateSystem GetBankGateSystem() => bankGateSystem;
        public BankManagerSystem GetBankManagerSystem() => bankManagerSystem;
        public HostageSystem GetHostageSystem() => hostageSystem;


        public BankRobberyManager(LootManager lootManager, CameraManager cameraManager, Lockpicking lockpicking)
        {
            this.lootManager = lootManager;
            this.cameraManager = cameraManager;
            this.lockpicking = lockpicking;

            hostageSystem = new HostageSystem();
            guardSystem = new GuardSystem();
            bankGateSystem = new BankGateSystem();
            stealthBankManager = new StealthBankManager(cameraManager, bankGateSystem);
            crewSystem = new CrewSystem();
            loadoutSystem = new LoadoutSystem();
            bankManagerSystem = new BankManagerSystem(bankGateSystem);

            waypointSystem = new WaypointSystem();

            guardSystem.OnAllGuardsAlerted += OnGuardsAlerted;

            vaultDoorSystem = new VaultDoorSystem();
            vaultDoorSystem.OnDoorOpened += OnVaultDoorOpened;

            hostageSystem.OnMissionFailedInsufficientHostages += OnInsufficientHostages;

            stealthBankManager.OnStealthCompromised += OnStealthCompromised;
            stealthBankManager.OnStealthBankEntryGranted += OnStealthBankEntryGranted;

            crewSystem.OnCrewMemberDown += OnCrewMemberDown;
            crewSystem.OnAllCrewDown += OnAllCrewDown;

            bankManagerSystem.OnGatesUnlocked += OnManagerGatesUnlocked;

            lootManager.OnVaultLootingStarted += OnVaultLootingStarted;
            lootManager.OnVaultLootingCompleted += OnVaultLootingCompleted;
            lootManager.OnVaultLootingProgress += OnVaultLootingProgress;
        }

        public void StartBankRobbery(string approach = "loud")
        {
            if (IsRobberyActive)
            {
                Screen.ShowNotification("~r~Bank robbery already active!");
                return;
            }

            CurrentState = RobberyState.Planning;

            Screen.ShowNotification("~b~Thieves Guild:~w~ Time for the big score. Pacific Standard is the target.");
            waypointSystem.SetWaypoint(bankExterior, "Pacific Standard");

            SetupBankHeist(approach);
        }

        private async void SetupBankHeist(string approach)
        {
            try
            {
                bool isLoudMission = approach.ToLower() == "loud";
                missionApproach = approach.ToLower();

                loadoutSystem.Initialize();

                var playerPos = GetEntityCoords(PlayerPedId(), true);
                Vector3 crewSpawnPosition = playerPos + new Vector3(-5f, -5f, 0f);
                await crewSystem.SpawnCrew(crewSpawnPosition, isLoudMission);

                if (approach.ToLower() == "quiet")
                {
                    // STEALTH APPROACH
                    loadoutSystem.ApplyMissionLoadout(MissionType.Quiet);
                    stealthBankManager.Initialize();
                    Screen.ShowNotification("~b~STEALTH APPROACH: Use silenced weapons and avoid detection!");
                    Screen.ShowNotification("~y~One crew member will assist with stealth operations!");
                }
                else
                {
                    // LOUD APPROACH
                    loadoutSystem.ApplyMissionLoadout(MissionType.Loud);
                    guardSystem.Initialize();

                    await SetupGuards();
                    vaultDoorSystem.Initialize();

                    // Initialize and lock the vault gates
                    bankGateSystem.Initialize();
                    await BaseScript.Delay(1000); // Give it time to initialize

                    // Initialize the bank manager AFTER gates are set up
                    InitializeBankManager();

                    //Screen.ShowNotification("~r~LOUD ROBBERY: Your crew will help suppress resistance!");
                    //Screen.ShowNotification("~y~Approach the bank to begin the operation!");
                }

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



        private void InitializeBankManager()
        {
            // Initialize for loud mission
            bankManagerSystem.Initialize(true);

            Screen.ShowNotification("~y~The bank manager is present. Secure enough hostages to force cooperation!");
            Debug.WriteLine("[BANK] Bank manager initialized for loud mission");
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
            Screen.ShowNotification($"~g~Hostage released! Police pressure: {newPoliceLevel}%");
        }

        private async Task SetupGuards()
        {
            // patrol paths for guards
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

            // Spawn guards 
            guardSystem.AddGuard(bankLobby + new Vector3(-2f, 1f, 0f), lobbyPatrol);
            await BaseScript.Delay(500);

            guardSystem.AddGuard(vaultEntrance + new Vector3(1f, 1f, 0f), vaultPatrol);
            await BaseScript.Delay(500);

            // Static guard 
            guardSystem.AddGuard(bankExterior + new Vector3(1f, -2f, 0f));
        }

        private void SetupBankLoot()
        {
            // Clear existing loot
            lootManager.LootItems.Clear();

            // DON'T setup vault loot here - wait for vault door to open
            // The vault loot will be setup when OnVaultDoorOpened() is called

            Debug.WriteLine("[BANK] Bank loot setup complete - vault loot will be available after door opens");
        }

        public void Update()
        {
            if (!IsRobberyActive) return;

            // CHECK FOR PLAYER DEATH - Mission fails if player dies
            CheckPlayerDeath();

            crewSystem.Update();
            lootManager.DrawVaultLootUI();

            // Check for player proximity to bank (only for loud missions that haven't spawned hostages yet)
            if (missionApproach == "loud" && !hasPlayerApproachedBank && !hostagesSpawned)
            {
                CheckPlayerProximityToBank();
            }

            // Update getaway sequence if active
            if (isGetawayActive)
            {
                UpdateGetawaySequence();
                return; 
            }

            if (missionApproach == "loud")
            {
                bankManagerSystem.Update();
            }

            // Update vault looting
            if (lootManager.VaultGold != null)
            {
                UpdateVaultLooting();
            }

            if (stealthBankManager.IsStealthMode)
            {
                stealthBankManager.Update();

                if (!stealthBankManager.IsStealthCompromised)
                {
                    vaultDoorSystem.Update();
                    CheckVaultDoorInteraction();
                    stealthBankManager.DrawDebugInfo();
                    crewSystem.DrawDebugInfo();
                    return;
                }
            }

            guardSystem.Update();

            // hostagfe
            if (hostagesSpawned && hostageSystem.IsActive)
            {
                hostageSystem.Update();
            }

            vaultDoorSystem.Update();
            bankGateSystem.Update();

            CheckVaultDoorInteraction();
            CheckHostageRequirement();

            // Draw debug info
            //guardSystem.DrawDebugInfo();
            crewSystem.DrawDebugInfo();
        }

        private void CheckPlayerDeath()
        {
            var playerPed = PlayerPedId();

            // Check if player is dead or dying
            if (IsPedDeadOrDying(playerPed, true))
            {
                Debug.WriteLine("[BANK] Player death detected - failing mission");
                OnPlayerDeath();
            }
        }

        private void OnPlayerDeath()
        {
            if (CurrentState == RobberyState.Failed || CurrentState == RobberyState.Completed)
            {
                return; // Don't fail if already completed/failed
            }

            CurrentState = RobberyState.Failed;
            Screen.ShowNotification("~r~HEIST FAILED: You were eliminated!");
            Screen.ShowNotification("~r~The crew scattered when you went down!");

            Debug.WriteLine("[BANK] Mission failed due to player death");

            // Cleanup the robbery
            FailRobbery();
        }

        private async void CheckPlayerProximityToBank()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToBank = Vector3.Distance(playerPos, bankExterior);

            if (distanceToBank <= BANK_APPROACH_DISTANCE)
            {
                hasPlayerApproachedBank = true;

                //Screen.ShowNotification("~y~Bank area detected! Preparing for operation...");
                Debug.WriteLine($"[BANK] Player approached bank (distance: {distanceToBank:F1}m) - initializing hostage system");

                // NOW initialize the hostage system
                await InitializeHostageSystemOnApproach();
            }
        }

        private async Task InitializeHostageSystemOnApproach()
        {
            if (hostagesSpawned) return; // Prevent double initialization

            try
            {
                hostagesSpawned = true;

                var crewStandByPosition = bankLobby + new Vector3(5f, 0f, 0f);
                hostageSystem.Initialize(crewStandByPosition, missionApproach);

                // Subscribe to hostage events
                hostageSystem.OnMissionFailedInsufficientHostages += OnInsufficientHostages;
                hostageSystem.OnPoliceBreach += OnPoliceBreach;
                hostageSystem.OnHostageReleased += OnHostageReleased;

                //Screen.ShowNotification("~g~Hostage system activated!");

                // ADD VAULT WAYPOINT
                Vector3 vaultTerminalPosition = new Vector3(253.6f, 228.2f, 101.7f);
                waypointSystem.SetWaypoint(vaultTerminalPosition, "Vault Terminal");
                Screen.ShowNotification("~y~Secure hostages with E, then proceed to the vault terminal!");

                Debug.WriteLine("[BANK] Hostage system initialized on player approach");
                Debug.WriteLine($"[BANK] Vault waypoint set at {vaultTerminalPosition}");
            }
            catch (Exception ex)
            {
                Screen.ShowNotification("~r~Failed to initialize hostage system!");
                Debug.WriteLine($"[BANK] Hostage initialization error: {ex.Message}");
                hostagesSpawned = false;
            }
        }
        


        private void OnGuardsAlerted()
        {
            //Screen.ShowNotification("~r~GUARDS ALERTED! This is now a loud robbery!");
            // Could trigger additional systems here like police response

            if (hostageSystem != null && hostageSystem.IsActive)
            {
                // Give initial wanted level when guards spot you
                SetPlayerWantedLevel(PlayerId(), 2, false);
                SetPlayerWantedLevelNow(PlayerId(), false);

                Screen.ShowNotification("~r~Security has called the police!");
                Debug.WriteLine("[BANK] Guards alerted - police response triggered");
            }
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

        private void OnCrewMemberDown(string memberName)
        {
            Debug.WriteLine($"[BANK] Crew member {memberName} eliminated");
            // Could reduce mission score or trigger events
        }

        private void OnAllCrewDown()
        {
            Debug.WriteLine("[BANK] All crew members eliminated - mission compromised");
            Screen.ShowNotification("~r~Mission compromised - continue solo or abort!");
            // Don't automatically fail mission, let player continue
        }

        private void OnStealthCompromised()
        {
            Screen.ShowNotification("~r~STEALTH COMPROMISED! Switching to loud approach!");

            // Convert to loud approach
            var crewStandByPosition = bankLobby + new Vector3(5f, 0f, 0f);
            hostageSystem.Initialize(crewStandByPosition, "loud");

            // Initialize hostage events
            hostageSystem.OnPoliceBreach += OnPoliceBreach;
            hostageSystem.OnHostageReleased += OnHostageReleased;

            Debug.WriteLine("[BANK] Stealth compromised - converted to loud approach");
        }

        private void OnStealthBankEntryGranted()
        {
            Screen.ShowNotification("~g~Stealth entry successful! Proceed to vault quietly.");
            Debug.WriteLine("[BANK] Stealth bank entry granted");
        }

        private void UpdateVaultLooting()
        {
            // Get alive crew member IDs for assistance
            var aliveCrewIds = crewSystem.Crew.Where(c => c.IsAlive).Select(c => c.PedId).ToList();
            lootManager.UpdateVaultLooting(aliveCrewIds);
        }

        private void OnVaultLootingStarted()
        {
            Screen.ShowNotification("~y~Collecting gold from the vault...");

            // Order crew to assist
            var vaultPos = lootManager.VaultGold.Position;
            crewSystem.OrderCrewToPosition(vaultPos + new Vector3(2f, 0f, 0f));

            Debug.WriteLine("[BANK] Vault looting started - crew ordered to assist");
        }

        private void OnVaultLootingProgress(float progress)
        {
            // Optional: Show progress notifications at milestones
            int percentage = (int)(progress * 100);
            if (percentage % 25 == 0 && percentage > 0)
            {
                Screen.ShowNotification($"~y~Vault {percentage}% cleared...");
            }
        }

        private void OnVaultLootingCompleted()
        {
            CurrentState = RobberyState.Escape;
            Screen.ShowNotification("~g~VAULT CLEARED! Time to escape!");
            Screen.ShowNotification("~y~Get to your vehicle and follow the escape route!");

            EscalateWantedLevel();

            // Start getaway sequence
            StartGetawaySequence();

            Debug.WriteLine("[BANK] Vault loot completed - starting getaway sequence");
        }

        private void EscalateWantedLevel()
        {
            // Set maximum wanted level when the big score is complete
            int maxWantedLevel = 6; // Try 6 first, fall back to 5 if not supported

            hostageSystem.Cleanup();

            SetPlayerWantedLevel(PlayerId(), maxWantedLevel, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            // Verify the wanted level was set correctly
            int actualWantedLevel = GetPlayerWantedLevel(PlayerId());

            if (actualWantedLevel < maxWantedLevel)
            {
                // If 6 didn't work, try 5 (standard maximum)
                SetPlayerWantedLevel(PlayerId(), 5, false);
                SetPlayerWantedLevelNow(PlayerId(), false);
                actualWantedLevel = 5;
                Debug.WriteLine("[BANK] Wanted level set to maximum (5 stars)");
            }
            else
            {
                Debug.WriteLine("[BANK] Wanted level set to maximum (6 stars)");
            }

            // Show dramatic notifications
            Screen.ShowNotification("~r~ALARM! VAULT BREACH DETECTED!");
            //Screen.ShowNotification($"~r~MAXIMUM WANTED LEVEL ACTIVATED! ({actualWantedLevel} STARS)");
            Screen.ShowNotification("~r~ALL UNITS CONVERGING ON YOUR LOCATION!");

            // Trigger immediate police escalation
            //TriggerPoliceEscalation();
        }

        //private void DisablePolicePressuure()
        //{
        //    // Disable hostage system police response
        //    if (hostageSystem != null)
        //    {
        //        hostageSystem.Cleanup();
        //    }
        //
        //    // Reduce wanted level
        //    SetPlayerWantedLevel(PlayerId(), 2, false);
        //
        //    //Screen.ShowNotification("~g~Police pressure reduced during escape!");
        //    Debug.WriteLine("[BANK] Police pressure disabled for getaway sequence");
        //}

        private void StartGetawaySequence()
        {
            isGetawayActive = true;
            currentGetawayPoint = 0;
            hasLostCops = false;

            // Set first waypoin
            waypointSystem.SetWaypoint(getawayPoints[0], "Escape Route");

            Screen.ShowNotification("~b~Follow the GPS route to escape!");
            Screen.ShowNotification("~y~Stay with your crew and lose the cops!");

            Debug.WriteLine($"[GETAWAY DEBUG] Started getaway sequence to {getawayPoints[0]}");
        }

        private void UpdateGetawaySequence()
        {
            if (!isGetawayActive) return;

            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Check wanted level to determine if cops are lost
            int wantedLevel = GetPlayerWantedLevel(PlayerId());

            Debug.WriteLine($"[GETAWAY DEBUG] Player Position: {playerPos}");
            Debug.WriteLine($"[GETAWAY DEBUG] Wanted Level: {wantedLevel}");
            Debug.WriteLine($"[GETAWAY DEBUG] Current Checkpoint: {currentGetawayPoint}/{getawayPoints.Length}");
            Debug.WriteLine($"[GETAWAY DEBUG] Has Lost Cops: {hasLostCops}");

            // Handle checkpoint progression
            if (currentGetawayPoint < getawayPoints.Length)
            {
                var targetPoint = getawayPoints[currentGetawayPoint];
                float distance = Vector3.Distance(playerPos, targetPoint);


                Debug.WriteLine($"[GETAWAY DEBUG] Target Point: {targetPoint}");
                Debug.WriteLine($"[GETAWAY DEBUG] Distance: {distance:F1}m");

                // Check if player reached current checkpoint
                if (distance < 50f) //g
                {
                    Debug.WriteLine($"[GETAWAY DEBUG] CHECKPOINT {currentGetawayPoint} REACHED!");
                    HandleCheckpointReached();
                }
            }

            // Check if ready for drop-off (cops lost and at second checkpoint)
            if (hasLostCops && currentGetawayPoint >= getawayPoints.Length)
            {
                Debug.WriteLine("[GETAWAY DEBUG] Ready for drop-off sequence");
                HandleDropOffSequence();
            }

            // Handle cop status
            CheckCopStatus(wantedLevel);

            // Keep crew following during escape
            ManageCrewDuringEscape();
        }

        private void HandleCheckpointReached()
        {
            Debug.WriteLine($"[GETAWAY DEBUG] HandleCheckpointReached called - incrementing from {currentGetawayPoint}");

            currentGetawayPoint++;

            if (currentGetawayPoint < getawayPoints.Length)
            {
                // Move to next checkpoint
                waypointSystem.SetWaypoint(getawayPoints[currentGetawayPoint], $"Escape Checkpoint {currentGetawayPoint + 1}");

                Screen.ShowNotification($"~g~Checkpoint {currentGetawayPoint} reached!");
                Debug.WriteLine($"[GETAWAY DEBUG] Set waypoint to checkpoint {currentGetawayPoint}: {getawayPoints[currentGetawayPoint]}");
            }
            else
            {
                Screen.ShowNotification("~g~Final checkpoint reached! Now lose the cops!");

                // Clear waypoints temporarily
                waypointSystem.ClearAllWaypoints();

                // Show message to lose cops
                Screen.ShowNotification("~y~Lose the police to proceed to drop-off!");
                Debug.WriteLine("[GETAWAY DEBUG] Final checkpoint reached - waiting for cops to be lost");
            }
        }

        private void CheckCopStatus(int wantedLevel)
        {
            Debug.WriteLine($"[COP STATUS DEBUG] Wanted Level: {wantedLevel}, Has Lost Cops: {hasLostCops}, Current Checkpoint: {currentGetawayPoint}");

            // If cops are lost and player is at final checkpoint area
            if (wantedLevel == 0 && !hasLostCops && currentGetawayPoint >= getawayPoints.Length)
            {
                hasLostCops = true;

                Debug.WriteLine("[COP STATUS DEBUG] CONDITIONS MET - Setting drop-off waypoint");

                // Set waypoint to drop-off point using CORRECT method
                waypointSystem.SetWaypoint(dropOffPoint, "Gold Drop-Off");

                Screen.ShowNotification("~g~Cops lost! Head to the drop-off point!");
                Screen.ShowNotification("~y~Deliver the gold to complete the heist!");

                Debug.WriteLine($"[COP STATUS DEBUG] Drop-off waypoint set to {dropOffPoint}");
            }
            else if (wantedLevel > 0 && hasLostCops)
            {
                // Cops found the player again
                hasLostCops = false;
                waypointSystem.ClearAllWaypoints();
                Screen.ShowNotification("~r~Cops are back on your trail! Lose them again!");
                Debug.WriteLine("[COP STATUS DEBUG] Cops found player again - cleared waypoints");
            }
        }

        private void HandleDropOffSequence()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToDropOff = Vector3.Distance(playerPos, dropOffPoint);

            if (distanceToDropOff < 10f) // Close to drop-off point
            {
                CompleteDropOff();
            }
        }

        private async void CompleteDropOff()
        {
            isGetawayActive = false;
            CurrentState = RobberyState.Completed;

            // Clear all waypoints
            waypointSystem.ClearAllWaypoints();

            // Calculate final score
            int goldCollected = lootManager.PlayerLoot.ContainsKey("Gold") ? lootManager.PlayerLoot["Gold"] : 0;
            int aliveCrewBonus = crewSystem.AliveCrewCount * 500;
            int escapeBonus = 1000; // Bonus for successful escape
            int totalScore = goldCollected + aliveCrewBonus + escapeBonus;

            Screen.ShowNotification("~g~HEIST COMPLETED SUCCESSFULLY!");
            await BaseScript.Delay(1500);

            Screen.ShowNotification($"~y~Final Score: ${totalScore:N0}");
            await BaseScript.Delay(1500);

            // Clear wanted level completely
            SetPlayerWantedLevel(PlayerId(), 0, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            Debug.WriteLine($"[BANK] Heist completed successfully! Total score: {totalScore}");

            // Cleanup after delay
            await BaseScript.Delay(2000);
            CleanupRobbery();
        }

        private void CompleteGetawaySequence()
        {
            CompleteDropOff();
        }

        private void ManageCrewDuringEscape()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var aliveCrewIds = crewSystem.Crew.Where(c => c.IsAlive).Select(c => c.PedId).ToList();

            foreach (int crewId in aliveCrewIds)
            {
                if (DoesEntityExist(crewId))
                {
                    var crewPos = GetEntityCoords(crewId, true);
                    float crewDistance = Vector3.Distance(crewPos, playerPos);

                    // If crew is too far behind, teleport them closer
                    if (crewDistance > 150f)
                    {
                        Vector3 catchupPos = playerPos + new Vector3(
                            (float)(new Random().NextDouble() - 0.5) * 15f,
                            (float)(new Random().NextDouble() - 0.5) * 15f,
                            0f
                        );
                        SetEntityCoords(crewId, catchupPos.X, catchupPos.Y, catchupPos.Z, false, false, false, true);
                        Debug.WriteLine($"[BANK] Teleported crew member {crewId} to catch up");
                    }
                }
            }
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
                        if (hostageSystem.HasSufficientHostages)
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

        private void CheckHostageRequirement()
        {
            if (!IsRobberyActive) return;

            // ONLY run this check for LOUD missions that use hostages
            if (stealthBankManager.IsStealthMode && !stealthBankManager.IsStealthCompromised) return;
            if (!hostagesSpawned) return;
            if (bankGateSystem.AreGatesLocked && hostageSystem.HasSufficientHostages)
            {
                // Force manager to unlock gates
                bankManagerSystem.ForceUnlockGates();

                Debug.WriteLine("[BANK] Forcing bank manager to unlock gates - sufficient hostages secured");
            }
        }

        private void OnManagerGatesUnlocked()
        {
            Screen.ShowNotification("~g~VAULT ACCESS UNLOCKED!");
            Screen.ShowNotification("~y~Proceed to the vault terminal to hack the door!");
            Debug.WriteLine("[BANK] Manager has unlocked both vault gates");
        }

        private void OnInsufficientHostages()
        {
            CurrentState = RobberyState.Failed;
            Screen.ShowNotification("~r~HEIST FAILED: Too many hostages escaped!");
            FailRobbery();
        }

        private void OnVaultDoorOpened()
        {
            CurrentState = RobberyState.Vault;
            Screen.ShowNotification("~g~VAULT ACCESS GRANTED!");
            Screen.ShowNotification("~y~Collect the gold to complete the heist!");

            // Clear vault terminal waypoint since player has reached it
            waypointSystem.ClearAllWaypoints();

            // setup the vault gold at the correct position
            Vector3 vaultGoldPosition = new Vector3(265.4f, 214.4f, 101.7f);
            lootManager.SetupVaultLoot(vaultGoldPosition);

            Debug.WriteLine($"[BANK] Vault opened - gold collection setup at {vaultGoldPosition}");
        }


        //private void AddVaultLoot()
        //{
        //    // Add high-value vault loot
        //    Vector3 vaultInterior = vaultDoorPosition + new Vector3(0f, -5f, 0f);
        //
        //    lootManager.AddLootItem(new LootItem("Cash", vaultInterior + new Vector3(-1f, 0f, 0f), 20));
        //    lootManager.AddLootItem(new LootItem("Jewelry", vaultInterior + new Vector3(1f, 0f, 0f), 10));
        //    lootManager.AddLootItem(new LootItem("Cash", vaultInterior + new Vector3(0f, -1f, 0f), 25));
        //    lootManager.AddLootItem(new LootItem("Electronics", vaultInterior + new Vector3(-1f, -1f, 0f), 5));
        //
        //    Debug.WriteLine("[BANK] Vault loot added");
        //}

        private void CleanupRobbery()
        {
            // Unsubscribe from events to prevent memory leaks
            if (hostagesSpawned && hostageSystem != null)
            {
                hostageSystem.OnMissionFailedInsufficientHostages -= OnInsufficientHostages;
                hostageSystem.OnPoliceBreach -= OnPoliceBreach;
                hostageSystem.OnHostageReleased -= OnHostageReleased;
            }

            guardSystem?.Cleanup();
            if (hostagesSpawned) hostageSystem?.Cleanup();
            bankGateSystem?.Cleanup();
            stealthBankManager?.Cleanup();
            crewSystem?.Cleanup();
            lootManager?.LootItems?.Clear();
            loadoutSystem?.Cleanup();
            vaultDoorSystem?.Cleanup();
            bankManagerSystem?.Cleanup();

            // Reset proximity detection flags
            hasPlayerApproachedBank = false;
            hostagesSpawned = false;
            missionApproach = "";

            Debug.WriteLine("[BANK] Bank robbery cleaned up");
        }

    }
}
