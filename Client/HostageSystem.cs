using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum HostageType
    {
        Civilian,
        BankManager,
        CrewMember
    }

    public enum HostageState
    {
        Normal,
        Crouched,
        Following,
        AtDoor,
        Released
    }

    public class Hostage
    {
        public int PedId { get; set; }
        public HostageType Type { get; set; }
        public HostageState State { get; set; }
        public Vector3 OriginalPosition { get; set; }
        public Vector3 StandByPosition { get; set; } // For crew member
        public bool IsInCone { get; set; }
        public float DistanceToReticle { get; set; }

        public Hostage(int pedId, HostageType type, Vector3 position)
        {
            PedId = pedId;
            Type = type;
            State = HostageState.Normal;
            OriginalPosition = position;
            IsInCone = false;
            DistanceToReticle = float.MaxValue;
        }
    }

    public class HostageSystem
    {
        private List<Hostage> hostages = new List<Hostage>();
        private Hostage bankManager = null;
        private Hostage crewMember = null;
        private Vector3? targetDoorPosition = null;
        private float doorOpeningProgress = 0f;
        private bool isDoorOpening = false;
        private bool hasFirstHostageCommand = false;

        // Detection settings
        private float coneDistance = 15f;
        private float coneAngle = 30f; // degrees from center

        // Crew member messages
        private string[] crewMessages = {
            "Everyone stay calm! This is just business.",
            "Keep your heads down and nobody gets hurt!",
            "Don't be a hero - your money is insured.",
            "Stay quiet and this will be over soon.",
            "Nobody move! We're not here to hurt anyone."
        };
        private float lastCrewMessageTime = 0f;

        // police system
        private int policePresenceLevel = 0; // 0-100 scale
        private int maxPolicePresence = 100;
        private int hostagesReleased = 0;
        private bool policeResponseActive = false;
        private float lastPoliceEscalationTime = 0f;
        private float policeEscalationInterval = 30f; // 30 seconds between escalations
        private string currentMissionType = ""; // Track if this is LOUD or QUIET

        // Events for police response
        public event Action<int> OnHostageReleased; // Passes police presence level
        public event Action<int> OnPolicePresenceChanged; // Passes current level
        public event Action OnPoliceBreach; // When police storm the building

        // Public properties
        public int PolicePresenceLevel => policePresenceLevel;
        public int HostagesReleased => hostagesReleased;
        public int HostagesRemaining => hostages.Count(h => h.Type == HostageType.Civilian && h.State != HostageState.Released);
        public bool IsPoliceResponseActive => policeResponseActive;

        public bool IsActive { get; private set; } = false;
        public bool AllHostagesCrouched => hostages.Where(h => h.Type == HostageType.Civilian).All(h => h.State == HostageState.Crouched);

        public async void Initialize(Vector3 crewStandByPosition, string missionType = "quiet")
        {
            IsActive = true;
            currentMissionType = missionType.ToLower();

            // Reset police response
            policePresenceLevel = 0;
            hostagesReleased = 0;
            policeResponseActive = false;

            Screen.ShowNotification("~y~Spawning hostages...");

            try
            {
                await SpawnHostages();
                await SpawnBankManager();
                await SpawnCrewMember(crewStandByPosition);

                // Only start police response for LOUD missions
                if (currentMissionType == "loud")
                {
                    StartPoliceResponse();
                    Debug.WriteLine($"[HOSTAGE] LOUD mission - police response activated");
                }
                else
                {
                    Debug.WriteLine($"[HOSTAGE] QUIET mission - no police response");
                }

                Screen.ShowNotification("~g~Hostage system active. Aim and press E to command hostages.");
                Debug.WriteLine($"[HOSTAGE] Hostage system initialized for {missionType.ToUpper()} mission");
            }
            catch (Exception ex)
            {
                Screen.ShowNotification("~r~Failed to initialize hostage system!");
                Debug.WriteLine($"[HOSTAGE] Error: {ex.Message}");
                IsActive = false;
            }
        }

        private void StartPoliceResponse()
        {
            policeResponseActive = true;
            lastPoliceEscalationTime = GetGameTimer() / 1000f;

            // Give player initial wanted level
            SetPlayerWantedLevel(PlayerId(), 2, false);
            SetPlayerWantedLevelNow(PlayerId(), false);

            Screen.ShowNotification("~r~LOUD APPROACH: Police are responding!");
            Screen.ShowNotification("~y~Use R to release hostages and reduce pressure!");

            Debug.WriteLine("[HOSTAGE] Police response initiated for LOUD mission");
        }

        private async Task SpawnHostages()
        {
            // Get player position as reference point
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Spawn hostages in a circle around the player
            Vector3[] relativePositions = {
        new Vector3(5.0f, 5.0f, 0f),    // Front-right
        new Vector3(-5.0f, 5.0f, 0f),   // Front-left  
        new Vector3(5.0f, -5.0f, 0f),   // Back-right
        new Vector3(-5.0f, -5.0f, 0f)   // Back-left
    };

            uint[] civilianModels = {
        (uint)GetHashKey("a_m_m_business_01"),
        (uint)GetHashKey("a_f_y_business_01"),
        (uint)GetHashKey("a_m_y_business_01"),
        (uint)GetHashKey("a_f_m_business_02")
    };

            Debug.WriteLine($"[HOSTAGE] Player position: {playerPos}");

            for (int i = 0; i < relativePositions.Length; i++)
            {
                uint model = civilianModels[i % civilianModels.Length];

                // Calculate absolute position relative to player
                Vector3 spawnPos = playerPos + relativePositions[i];

                // Get ground Z coordinate for the spawn position
                float groundZ = spawnPos.Z;
                GetGroundZFor_3dCoord(spawnPos.X, spawnPos.Y, spawnPos.Z + 10f, ref groundZ, false);
                spawnPos = new Vector3(spawnPos.X, spawnPos.Y, groundZ);

                Debug.WriteLine($"[HOSTAGE] Attempting to spawn hostage {i} at {spawnPos}");

                if (await LoadModel(model))
                {
                    int ped = CreatePed(4, model, spawnPos.X, spawnPos.Y, spawnPos.Z, 0f, true, true);

                    if (DoesEntityExist(ped))
                    {
                        SetEntityAsMissionEntity(ped, true, true);
                        SetPedFleeAttributes(ped, 0, false); // Don't flee
                        SetPedCombatAttributes(ped, 17, true); // Passive
                        SetPedCanRagdoll(ped, false); // Prevent ragdoll

                        // Use a simpler task that's more reliable
                        TaskStandStill(ped, -1);

                        hostages.Add(new Hostage(ped, HostageType.Civilian, spawnPos));
                        Debug.WriteLine($"[HOSTAGE] Successfully spawned civilian hostage {ped} at {spawnPos}");

                        // Visual confirmation
                        Screen.ShowNotification($"~g~Spawned hostage {i + 1} at {spawnPos.X:F1}, {spawnPos.Y:F1}");
                    }
                    else
                    {
                        Debug.WriteLine($"[HOSTAGE] Failed to create ped with model {model} at {spawnPos}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[HOSTAGE] Failed to load model {model}");
                }

                SetModelAsNoLongerNeeded(model);

                // Small delay between spawns
                await BaseScript.Delay(200);
            }

            Debug.WriteLine($"[HOSTAGE] Finished spawning {hostages.Count} hostages");
        }

        private async Task SpawnBankManager()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Spawn bank manager 8 units in front of player
            Vector3 managerOffset = new Vector3(0f, 8f, 0f);
            Vector3 managerPosition = playerPos + managerOffset;

            // Get ground Z coordinate
            float groundZ = managerPosition.Z;
            GetGroundZFor_3dCoord(managerPosition.X, managerPosition.Y, managerPosition.Z + 10f, ref groundZ, false);
            managerPosition = new Vector3(managerPosition.X, managerPosition.Y, groundZ);

            uint managerModel = (uint)GetHashKey("s_m_m_banker_01");

            Debug.WriteLine($"[HOSTAGE] Attempting to spawn bank manager at {managerPosition}");

            if (await LoadModel(managerModel))
            {
                int ped = CreatePed(4, managerModel, managerPosition.X, managerPosition.Y, managerPosition.Z, 180f, true, true);

                if (DoesEntityExist(ped))
                {
                    SetEntityAsMissionEntity(ped, true, true);
                    SetPedFleeAttributes(ped, 0, false);
                    SetPedCombatAttributes(ped, 17, true);
                    SetPedCanRagdoll(ped, false);

                    TaskStandStill(ped, -1);

                    bankManager = new Hostage(ped, HostageType.BankManager, managerPosition);
                    Debug.WriteLine($"[HOSTAGE] Successfully spawned bank manager {ped} at {managerPosition}");
                    Screen.ShowNotification($"~b~Spawned bank manager at {managerPosition.X:F1}, {managerPosition.Y:F1}");
                }
                else
                {
                    Debug.WriteLine("[HOSTAGE] Failed to create bank manager ped");
                }
            }
            else
            {
                Debug.WriteLine("[HOSTAGE] Failed to load bank manager model");
            }

            SetModelAsNoLongerNeeded(managerModel);
        }

        private async Task SpawnCrewMember(Vector3 standByPosition)
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Spawn crew member 3 units behind player
            Vector3 crewOffset = new Vector3(0f, -3f, 0f);
            Vector3 startPosition = playerPos + crewOffset;

            // Get ground Z coordinate
            float groundZ = startPosition.Z;
            GetGroundZFor_3dCoord(startPosition.X, startPosition.Y, startPosition.Z + 10f, ref groundZ, false);
            startPosition = new Vector3(startPosition.X, startPosition.Y, groundZ);

            uint crewModel = (uint)GetHashKey("s_m_m_security_01");

            Debug.WriteLine($"[HOSTAGE] Attempting to spawn crew member at {startPosition}");

            if (await LoadModel(crewModel))
            {
                int ped = CreatePed(4, crewModel, startPosition.X, startPosition.Y, startPosition.Z, 0f, true, true);

                if (DoesEntityExist(ped))
                {
                    SetEntityAsMissionEntity(ped, true, true);
                    SetPedAsGroupMember(ped, GetPlayerGroup(PlayerId()));
                    SetPedCanRagdoll(ped, false);

                    // Give weapon
                    GiveWeaponToPed(ped, (uint)GetHashKey("weapon_pistol"), 100, false, true);

                    crewMember = new Hostage(ped, HostageType.CrewMember, startPosition)
                    {
                        // Use player position + offset for standby position if not provided
                        StandByPosition = standByPosition != Vector3.Zero ? standByPosition : playerPos + new Vector3(10f, 0f, 0f)
                    };

                    Debug.WriteLine($"[HOSTAGE] Successfully spawned crew member {ped} at {startPosition}");
                    Screen.ShowNotification($"~o~Spawned crew member at {startPosition.X:F1}, {startPosition.Y:F1}");
                }
                else
                {
                    Debug.WriteLine("[HOSTAGE] Failed to create crew member ped");
                }
            }
            else
            {
                Debug.WriteLine("[HOSTAGE] Failed to load crew member model");
            }

            SetModelAsNoLongerNeeded(crewModel);
        }

        // Helper method for async model loading
        private async Task<bool> LoadModel(uint model)
        {
            RequestModel(model);

            int attempts = 0;
            while (!HasModelLoaded(model) && attempts < 50) // Max 5 seconds
            {
                await BaseScript.Delay(100);
                attempts++;
            }

            return HasModelLoaded(model);
        }

        public void Update()
        {
            if (!IsActive) return;

            try
            {
                // Update cone detection
                UpdateConeDetection();

                // Handle crew member behavior
                UpdateCrewMember();

                if (currentMissionType == "loud" && policeResponseActive)
                {
                    UpdatePoliceResponse();
                }

                // Handle bank manager following
                if (bankManager?.State == HostageState.Following)
                {
                    UpdateBankManagerFollowing();
                }

                // Handle door opening
                if (isDoorOpening)
                {
                    UpdateDoorOpening();
                }

                // Handle input
                HandleHostageInput();

                // Draw debug info
                DrawDebugInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HOSTAGE] Update error: {ex.Message}");
            }
        }

        private void UpdatePoliceResponse()
        {
            float currentTime = GetGameTimer() / 1000f;

            // Escalate police presence over time
            if (currentTime - lastPoliceEscalationTime >= policeEscalationInterval)
            {
                EscalatePolicePresence(5); // +5 every 30 seconds
                lastPoliceEscalationTime = currentTime;
            }

            // Check for breach conditions
            if (policePresenceLevel >= maxPolicePresence)
            {
                TriggerPoliceBreach();
            }
        }

        private void EscalatePolicePresence(int amount)
        {
            policePresenceLevel = Math.Min(maxPolicePresence, policePresenceLevel + amount);
            OnPolicePresenceChanged?.Invoke(policePresenceLevel);

            // Increase wanted level based on presence
            if (policePresenceLevel >= 75)
            {
                SetPlayerWantedLevel(PlayerId(), 5, false);
            }
            else if (policePresenceLevel >= 50)
            {
                SetPlayerWantedLevel(PlayerId(), 4, false);
            }
            else if (policePresenceLevel >= 25)
            {
                SetPlayerWantedLevel(PlayerId(), 3, false);
            }

            Screen.ShowNotification($"~r~Police Presence: {policePresenceLevel}%");

            if (policePresenceLevel >= 90)
            {
                Screen.ShowNotification("~r~WARNING: Police about to breach!");
            }
        }

        private void TriggerPoliceBreach()
        {
            if (!policeResponseActive) return;

            policeResponseActive = false;
            OnPoliceBreach?.Invoke();

            Screen.ShowNotification("~r~POLICE BREACH! MISSION FAILED!");
            Debug.WriteLine("[HOSTAGE] Police breached - mission failed");
        }

        private void UpdateConeDetection()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Get camera direction for more accurate aiming
            var camRot = GetGameplayCamRot(2);
            var forwardVector = RotationToDirection(camRot);

            // Include bank manager in detection
            var allTargets = new List<Hostage>(hostages);
            if (bankManager != null)
            {
                allTargets.Add(bankManager);
            }

            foreach (var hostage in allTargets)
            {
                if (!DoesEntityExist(hostage.PedId)) continue;

                var hostagePos = GetEntityCoords(hostage.PedId, true);
                var distanceToPlayer = Vector3.Distance(playerPos, hostagePos);

                if (distanceToPlayer <= coneDistance)
                {
                    // Calculate angle between camera direction and hostage direction
                    var toHostage = (hostagePos - playerPos);
                    var toHostageNormalized = Normalize(toHostage);
                    var forwardNormalized = Normalize(forwardVector);

                    var dotProduct = Vector3.Dot(forwardNormalized, toHostageNormalized);
                    var clampedDot = Math.Max(-1f, Math.Min(1f, dotProduct));
                    var angle = Math.Acos(clampedDot) * (180.0 / Math.PI);

                    hostage.IsInCone = angle <= coneAngle;

                    if (hostage.IsInCone)
                    {
                        // Calculate distance to reticle center for priority
                        hostage.DistanceToReticle = (float)angle;
                    }
                    else
                    {
                        hostage.DistanceToReticle = float.MaxValue;
                    }
                }
                else
                {
                    hostage.IsInCone = false;
                    hostage.DistanceToReticle = float.MaxValue;
                }
            }
        }

        private Vector3 Normalize(Vector3 vector)
        {
            float magnitude = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
            if (magnitude > 0f)
            {
                return new Vector3(vector.X / magnitude, vector.Y / magnitude, vector.Z / magnitude);
            }
            return Vector3.Zero;
        }

        private void HandleHostageInput()
        {
            if (!IsPlayerFreeAiming(PlayerId())) return;

            // E key - Primary command (crouch/follow)
            if (IsControlJustPressed(0, 51))
            {
                var allTargets = new List<Hostage>(hostages);
                if (bankManager != null) allTargets.Add(bankManager);

                var hostagesToCommand = allTargets.Where(h => h.IsInCone).OrderBy(h => h.DistanceToReticle).ToList();

                if (hostagesToCommand.Any())
                {
                    var targetHostage = hostagesToCommand.First();
                    CommandHostage(targetHostage);
                }
                else if (bankManager?.State == HostageState.Following)
                {
                    CheckDoorTargeting();
                }
            }

            // R key - Release hostage (LOUD missions only)
            if (IsControlJustPressed(0, 45) && currentMissionType == "loud") // R key
            {
                var civilianHostages = hostages.Where(h => h.Type == HostageType.Civilian &&
                                                          h.IsInCone &&
                                                          h.State == HostageState.Crouched)
                                             .OrderBy(h => h.DistanceToReticle).ToList();

                if (civilianHostages.Any())
                {
                    var targetHostage = civilianHostages.First();
                    ReleaseHostage(targetHostage);
                }
            }
        }

        private async void ReleaseHostage(Hostage hostage)
        {
            if (hostage.Type != HostageType.Civilian || hostage.State != HostageState.Crouched) return;

            hostage.State = HostageState.Released;
            hostagesReleased++;

            // Unfreeze and clear tasks
            FreezeEntityPosition(hostage.PedId, false);
            ClearPedTasks(hostage.PedId);
            SetEntityInvincible(hostage.PedId, false);

            // Make them run away
            SetPedFleeAttributes(hostage.PedId, 0, true);
            TaskSmartFleePed(hostage.PedId, PlayerPedId(), 100f, -1, false, false);

            // Reduce police presence (strategic benefit)
            int presenceReduction = 15 + (hostagesReleased * 5);
            policePresenceLevel = Math.Max(0, policePresenceLevel - presenceReduction);

            OnHostageReleased?.Invoke(policePresenceLevel);
            OnPolicePresenceChanged?.Invoke(policePresenceLevel);

            Screen.ShowNotification($"~g~Hostage released! Police presence reduced by {presenceReduction}%");
            Screen.ShowNotification($"~y~Hostages remaining: {HostagesRemaining}");

            Debug.WriteLine($"[HOSTAGE] Released hostage {hostage.PedId}. Police presence now {policePresenceLevel}%");

            // Remove from building after delay
            await BaseScript.Delay(5000);
            if (DoesEntityExist(hostage.PedId))
            {
                int pedId = hostage.PedId;
                DeletePed(ref pedId);
            }
        }

        private void CommandHostage(Hostage hostage)
        {
            switch (hostage.Type)
            {
                case HostageType.Civilian:
                    if (hostage.State == HostageState.Normal)
                    {
                        MakeHostageCrouch(hostage);

                        // First hostage command - move crew to standby
                        if (!hasFirstHostageCommand)
                        {
                            hasFirstHostageCommand = true;
                            MoveCrewToStandBy();
                        }
                    }
                    break;

                case HostageType.BankManager:
                    if (AllHostagesCrouched && hostage.State != HostageState.Following)
                    {
                        MakeBankManagerFollow(hostage);
                    }
                    else if (!AllHostagesCrouched)
                    {
                        Screen.ShowNotification("~r~Secure all hostages first!");
                    }
                    break;
            }
        }

        private async void MakeHostageCrouch(Hostage hostage)
        {
            hostage.State = HostageState.Crouched;

            // Nuclear option - completely stop everything
            ClearPedTasks(hostage.PedId);
            ClearPedTasksImmediately(hostage.PedId);

            // Set all the attributes
            SetPedFleeAttributes(hostage.PedId, 0, false);
            SetPedCombatAttributes(hostage.PedId, 17, true);
            SetPedCanRagdoll(hostage.PedId, false);

            // Stop all movement completely
            SetPedMoveRateOverride(hostage.PedId, 0.0f);
            SetEntityMaxSpeed(hostage.PedId, 0.0f);

            // Freeze position immediately
            FreezeEntityPosition(hostage.PedId, true);

            // Stop any velocity
            SetEntityVelocity(hostage.PedId, 0f, 0f, 0f);
            SetEntityAngularVelocity(hostage.PedId, 0f, 0f, 0f);

            // Make them completely passive and unresponsive
            SetEntityInvincible(hostage.PedId, true);
            SetPedConfigFlag(hostage.PedId, 17, true); // PED_CONFIG_FLAG_BlockNonTemporaryEvents
            SetPedConfigFlag(hostage.PedId, 128, true); // PED_CONFIG_FLAG_DisablePanicInVehicle

            await BaseScript.Delay(200); // Let everything settle

            // Now apply animation on the completely stopped ped
            RequestAnimDict("random@mugging3");
            int attempts = 0;
            while (!HasAnimDictLoaded("random@mugging3") && attempts < 20)
            {
                await BaseScript.Delay(100);
                attempts++;
            }

            if (HasAnimDictLoaded("random@mugging3"))
            {
                TaskPlayAnim(hostage.PedId, "random@mugging3", "handsup_standing_base", 8f, -8f, -1, 49, 0f, false, false, false);
                Debug.WriteLine($"[HOSTAGE] Applied hands up animation to completely stopped hostage {hostage.PedId}");
            }
            else
            {
                // Ultimate fallback
                SetPedCowerHash(hostage.PedId, "CODE_HUMAN_STAND_COWER");
                Debug.WriteLine($"[HOSTAGE] Using cower as final fallback for hostage {hostage.PedId}");
            }

            Screen.ShowNotification("~y~Hostage completely secured.");
            Debug.WriteLine($"[HOSTAGE] Hostage {hostage.PedId} should now be completely still");
        }






        private void MakeBankManagerFollow(Hostage hostage)
        {
            hostage.State = HostageState.Following;
            ClearPedTasks(hostage.PedId);

            Screen.ShowNotification("~g~Bank manager will follow you. Aim at doors to command them to open it.");
            Debug.WriteLine($"[HOSTAGE] Bank manager {hostage.PedId} following player");
        }

        private void UpdateBankManagerFollowing()
        {
            if (bankManager == null || !DoesEntityExist(bankManager.PedId)) return;

            var playerPed = PlayerPedId();
            TaskFollowToOffsetOfEntity(bankManager.PedId, playerPed, 0f, -2f, 0f, 5f, -1, 2.5f, true);
        }

        private void CheckDoorTargeting()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            var camRot = GetGameplayCamRot(2);
            var forwardVector = RotationToDirection(camRot);

            // Raycast for doors
            var endPoint = playerPos + forwardVector * 10f;
            int raycast = StartShapeTestRay(playerPos.X, playerPos.Y, playerPos.Z,
                                         endPoint.X, endPoint.Y, endPoint.Z,
                                         -1, playerPed, 0);

            bool hit = false;
            Vector3 hitCoords = Vector3.Zero;
            Vector3 surfaceNormal = Vector3.Zero;
            int entityHit = 0;

            GetShapeTestResult(raycast, ref hit, ref hitCoords, ref surfaceNormal, ref entityHit);

            if (hit)
            {
                // Check if hit entity is a door
                if (IsEntityAnObject(entityHit))
                {
                    targetDoorPosition = hitCoords;
                    CommandBankManagerToDoor();
                }
            }
        }

        private void CommandBankManagerToDoor()
        {
            if (bankManager == null || !targetDoorPosition.HasValue) return;

            bankManager.State = HostageState.AtDoor;
            ClearPedTasks(bankManager.PedId);
            TaskGoToCoordAnyMeans(bankManager.PedId, targetDoorPosition.Value.X, targetDoorPosition.Value.Y, targetDoorPosition.Value.Z, 1f, 0, false, 786603, 0f);

            isDoorOpening = true;
            doorOpeningProgress = 0f;

            Screen.ShowNotification("~y~Bank manager moving to door...");
            Debug.WriteLine($"[HOSTAGE] Bank manager commanded to door at {targetDoorPosition.Value}");
        }

        private void UpdateDoorOpening()
        {
            if (!isDoorOpening) return;

            // Check if bank manager reached the door
            if (bankManager != null && targetDoorPosition.HasValue && DoesEntityExist(bankManager.PedId))
            {
                var managerPos = GetEntityCoords(bankManager.PedId, true);
                var distanceToDoor = Vector3.Distance(managerPos, targetDoorPosition.Value);

                if (distanceToDoor < 2f)
                {
                    // Start opening process
                    doorOpeningProgress += 0.02f; // Adjust speed as needed

                    if (doorOpeningProgress >= 1f)
                    {
                        // Door opened
                        isDoorOpening = false;
                        Screen.ShowNotification("~g~Door opened by bank manager!");
                        Debug.WriteLine("[HOSTAGE] Door opening completed");
                    }
                }
            }
        }

        private void MoveCrewToStandBy()
        {
            if (crewMember == null || !DoesEntityExist(crewMember.PedId)) return;

            ClearPedTasks(crewMember.PedId);
            TaskGoToCoordAnyMeans(crewMember.PedId, crewMember.StandByPosition.X, crewMember.StandByPosition.Y, crewMember.StandByPosition.Z, 1f, 0, false, 786603, 0f);

            Screen.ShowNotification("~b~Your crew member is taking position.");
            Debug.WriteLine("[HOSTAGE] Crew member moving to standby position");
        }

        private void UpdateCrewMember()
        {
            if (!hasFirstHostageCommand) return;

            float currentTime = GetGameTimer() / 1000f;

            // Send periodic messages
            if (currentTime - lastCrewMessageTime > 15f) // Every 15 seconds
            {
                var randomMessage = crewMessages[new Random().Next(crewMessages.Length)];
                BaseScript.TriggerEvent("chat:addMessage", new
                {
                    color = new[] { 100, 150, 255 },
                    multiline = false,
                    args = new[] { "[CREW]", randomMessage }
                });

                lastCrewMessageTime = currentTime;
                Debug.WriteLine($"[HOSTAGE] Crew member says: {randomMessage}");
            }
        }

        private void DrawDebugInfo()
        {
            // ... existing hostage detection drawing ...

            // NEW: Police response UI for LOUD missions
            if (currentMissionType == "loud" && policeResponseActive)
            {
                DrawPoliceResponseUI();
            }

            // Enhanced hostage status
            var statusText = $"Hostages: {HostagesRemaining} remaining | {hostagesReleased} released";
            if (AllHostagesCrouched)
            {
                statusText += " - All secured! Command bank manager.";
            }

            // Draw controls help
            var controlsText = "E: Command Hostage";
            if (currentMissionType == "loud")
            {
                controlsText += " | R: Release Hostage (reduces police pressure)";
            }

            // Draw status
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString(statusText);
            DrawText(0.02f, 0.1f);

            // Draw controls
            SetTextScale(0.0f, 0.3f);
            SetTextEntry("STRING");
            AddTextComponentString(controlsText);
            DrawText(0.02f, 0.15f);
        }

        private void DrawPoliceResponseUI()
        {
            // Police pressure bar
            float barWidth = 0.3f;
            float barHeight = 0.03f;
            float barX = 0.35f;
            float barY = 0.05f;

            // Background
            DrawRect(barX, barY, barWidth, barHeight, 0, 0, 0, 150);

            // Progress bar
            float progress = (float)policePresenceLevel / maxPolicePresence;
            float progressWidth = barWidth * progress;

            // Color based on danger level
            int r = 255, g = 255, b = 0; // Yellow default
            if (progress >= 0.8f)
            {
                r = 255; g = 0; b = 0; // Red - danger
            }
            else if (progress >= 0.5f)
            {
                r = 255; g = 128; b = 0; // Orange - warning
            }
            else if (progress >= 0.25f)
            {
                r = 255; g = 255; b = 0; // Yellow - caution
            }
            else
            {
                r = 0; g = 255; b = 0; // Green - safe
            }

            DrawRect(barX - (barWidth - progressWidth) / 2, barY, progressWidth, barHeight, r, g, b, 200);

            // Text
            SetTextFont(1);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.45f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString($"POLICE PRESSURE: {policePresenceLevel}%");
            DrawText(barX - barWidth / 2, barY - 0.025f);
        }


        private Vector3 RotationToDirection(Vector3 rotation)
        {
            float z = rotation.Z * (float)(Math.PI / 180.0);
            float x = rotation.X * (float)(Math.PI / 180.0);
            float num = Math.Abs((float)Math.Cos(x));

            return new Vector3
            {
                X = (float)(-Math.Sin(z)) * num,
                Y = (float)(Math.Cos(z)) * num,
                Z = (float)Math.Sin(x)
            };
        }

        public void Cleanup()
        {
            IsActive = false;

            try
            {
                // Clean up all spawned peds
                foreach (var hostage in hostages)
                {
                    if (DoesEntityExist(hostage.PedId))
                    {
                        // Unfreeze before deleting
                        FreezeEntityPosition(hostage.PedId, false);
                        int pedId = hostage.PedId;
                        DeletePed(ref pedId);
                    }
                }

                if (bankManager != null && DoesEntityExist(bankManager.PedId))
                {
                    FreezeEntityPosition(bankManager.PedId, false);
                    int managerId = bankManager.PedId;
                    DeletePed(ref managerId);
                }

                if (crewMember != null && DoesEntityExist(crewMember.PedId))
                {
                    FreezeEntityPosition(crewMember.PedId, false);
                    int crewId = crewMember.PedId;
                    DeletePed(ref crewId);
                }

                hostages.Clear();
                bankManager = null;
                crewMember = null;

                Debug.WriteLine("[HOSTAGE] Hostage system cleaned up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HOSTAGE] Cleanup error: {ex.Message}");
            }
        }
    }
}
