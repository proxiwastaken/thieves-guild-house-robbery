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

    public class PoliceUnit
    {
        public int VehicleId { get; set; }
        public int DriverId { get; set; }
        public int PassengerId { get; set; }
        public Vector3 Position { get; set; }
        public PoliceUnitType Type { get; set; }
        public int SpawnWave { get; set; }

        public PoliceUnit(int vehicleId, int driverId, Vector3 position, PoliceUnitType type, int wave)
        {
            VehicleId = vehicleId;
            DriverId = driverId;
            PassengerId = 0;
            Position = position;
            Type = type;
            SpawnWave = wave;
        }
    }

    public enum PoliceUnitType
    {
        PoliceCar,
        RiotVan,
        SWAT,
        Helicopter,
        BreachTeam
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
        private float policeEscalationInterval = 15f;
        private string currentMissionType = ""; // Track if this is LOUD or QUIET
        private List<PoliceUnit> spawnedPoliceUnits = new List<PoliceUnit>();
        private List<int> spawnedHelicopters = new List<int>();
        private Vector3 bankExterior = new Vector3(216.0f, 199.9f, 105.5f);
        private bool hasSpawnedInitialResponse = false;
        private float lastPoliceSpawnTime = 0f;
        private int currentPoliceWave = 0;
        private Vector3 playerLastKnownPosition = Vector3.Zero;
        private float playerDistanceFromBank = 0f;
        private bool hasLeftBankArea = false;
        private const float BANK_EXIT_DISTANCE = 1000f; // Distance considered "leaving the bank"

        private const int MINIMUM_HOSTAGES_REQUIRED = 2;
        private const int MAXIMUM_HOSTAGES_ESCAPED = 6;
        private bool isSpawningHostages = false;
        private bool hasCompletedInitialSpawn = false;


        public int HostagesSecured => hostages.Count(h => h.Type == HostageType.Civilian && h.State == HostageState.Crouched);
        public int HostagesEscaped => hostages.Count(h => h.Type == HostageType.Civilian && h.State == HostageState.Released);
        public bool HasMinimumHostages => HostagesSecured >= MINIMUM_HOSTAGES_REQUIRED;
        public event Action OnMissionFailedInsufficientHostages;


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
        public bool HasSufficientHostages => HostagesSecured >= MINIMUM_HOSTAGES_REQUIRED;


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
                isSpawningHostages = true;
                hasCompletedInitialSpawn = false;

                await SpawnHostages();
                await SpawnBankManager();
                await SpawnCrewMember(crewStandByPosition);

                isSpawningHostages = false;
                hasCompletedInitialSpawn = true;

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
                isSpawningHostages = false;
                IsActive = false;
            }
        }

        private void StartPoliceResponse()
        {
            policeResponseActive = true;
            lastPoliceEscalationTime = GetGameTimer() / 1000f;
            lastPoliceSpawnTime = GetGameTimer() / 1000f;
            hasSpawnedInitialResponse = false;
            currentPoliceWave = 0;

            // let guards handle detection
            // SetPlayerWantedLevel(PlayerId(), 2, false);
            // SetPlayerWantedLevelNow(PlayerId(), false);

            Screen.ShowNotification("~r~LOUD APPROACH: Secure hostages to control police response!");
            Screen.ShowNotification("~y~Use R to release hostages and reduce pressure!");

            Debug.WriteLine("[HOSTAGE] Police response initiated for LOUD mission - waiting for guard detection");
        }

        private async Task SpawnHostages()
        {
            // Clear any existing hostages first
            hostages.Clear();

            var hostageGroups = new[]
            {
        // Group 1 - Teller Area
        new {
            Position = new Vector3(244.7f, 221.8f, 106.3f),
            Heading = 70.1f,
            Count = 3,
            Description = "Teller Area Group",
            SpreadRadius = 4f 
        },
        // Group 2 - Waiting Area
        new {
            Position = new Vector3(254.7f, 212.3f, 106.3f),
            Heading = 320.8f,
            Count = 4,
            Description = "Waiting Area Group",
            SpreadRadius = 5f
        },
        // Group 3 - Office Area
        new {
            Position = new Vector3(258.4f, 218.2f, 106.3f),
            Heading = 287.5f,
            Count = 3,
            Description = "Office Area Group",
            SpreadRadius = 3.5f
        }
    };

            uint[] civilianModels = {
        (uint)GetHashKey("a_m_m_business_01"), 
        (uint)GetHashKey("a_f_y_business_01"), 
        (uint)GetHashKey("a_m_y_business_01"), 
        (uint)GetHashKey("a_f_m_business_02"), 
        (uint)GetHashKey("a_f_y_business_02"), 
        (uint)GetHashKey("a_f_y_hipster_01"),  
        (uint)GetHashKey("a_m_y_hipster_01"),  
        (uint)GetHashKey("a_f_m_fatcult_01"),  
        (uint)GetHashKey("a_m_o_genstreet_01"),
        (uint)GetHashKey("a_f_o_indian_01"),   
    };

            Debug.WriteLine("[HOSTAGE] Spawning hostages with improved spacing");

            int totalHostages = hostageGroups.Sum(g => g.Count);
            int hostageIndex = 0;
            int successfulSpawns = 0;

            //Screen.ShowNotification($"~g~Spawning {totalHostages} hostages in {hostageGroups.Length} groups...");

            foreach (var group in hostageGroups)
            {
                Debug.WriteLine($"[HOSTAGE] Spawning group: {group.Description} at {group.Position}");

                for (int i = 0; i < group.Count; i++)
                {
                    uint model = civilianModels[hostageIndex % civilianModels.Length];

                    float angle = (360f / group.Count) * i * (float)(Math.PI / 180);
                    float distance = (float)(new Random().NextDouble() * group.SpreadRadius);

                    Vector3 spawnPos = group.Position + new Vector3(
                        (float)(Math.Cos(angle) * distance),
                        (float)(Math.Sin(angle) * distance),
                        0f
                    );

                    // Get ground Z coordinate for safety
                    float groundZ = spawnPos.Z;
                    GetGroundZFor_3dCoord(spawnPos.X, spawnPos.Y, spawnPos.Z + 10f, ref groundZ, false);
                    spawnPos = new Vector3(spawnPos.X, spawnPos.Y, groundZ);

                    Debug.WriteLine($"[HOSTAGE] Spawning hostage {hostageIndex + 1} in {group.Description} at {spawnPos}");

                    // RETRY LOGIC 
                    int retryAttempts = 3;
                    bool spawnSuccessful = false;

                    for (int retry = 0; retry < retryAttempts && !spawnSuccessful; retry++)
                    {
                        if (await LoadModel(model))
                        {
                            int ped = CreatePed(4, model, spawnPos.X, spawnPos.Y, spawnPos.Z, group.Heading, true, true);

                            if (DoesEntityExist(ped))
                            {
                                SetEntityAsMissionEntity(ped, true, true);
                                SetPedFleeAttributes(ped, 0, false); // Don't flee initially
                                SetPedCombatAttributes(ped, 17, true); // Passive
                                SetPedCanRagdoll(ped, false); // Prevent ragdoll

                                SetupNaturalHostageBehavior(ped, spawnPos, group.SpreadRadius);

                                hostages.Add(new Hostage(ped, HostageType.Civilian, spawnPos));
                                Debug.WriteLine($"[HOSTAGE] Successfully spawned civilian hostage {ped} in {group.Description}");
                                spawnSuccessful = true;
                                successfulSpawns++;
                            }
                            else
                            {
                                Debug.WriteLine($"[HOSTAGE] Failed to create ped with model {model} at {spawnPos}, retry {retry + 1}");
                                if (retry < retryAttempts - 1) await BaseScript.Delay(500);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[HOSTAGE] Failed to load model {model}, retry {retry + 1}");
                            if (retry < retryAttempts - 1) await BaseScript.Delay(500);
                        }
                    }

                    if (!spawnSuccessful)
                    {
                        Debug.WriteLine($"[HOSTAGE] FAILED to spawn hostage {hostageIndex + 1} after {retryAttempts} attempts");
                    }

                    SetModelAsNoLongerNeeded(model);
                    hostageIndex++;

                    // Small delay between spawns
                    await BaseScript.Delay(150);
                }

                // Slightly longer delay between groups
                await BaseScript.Delay(300);
            }

            Debug.WriteLine($"[HOSTAGE] Finished spawning {successfulSpawns}/{totalHostages} hostages successfully");
            //Screen.ShowNotification($"~g~{successfulSpawns} hostages spawned with natural behavior");

            // SAFETY CHECK: If not enough hostages spawned, retry
            if (successfulSpawns < MINIMUM_HOSTAGES_REQUIRED)
            {
                Debug.WriteLine($"[HOSTAGE] WARNING: Only {successfulSpawns} hostages spawned, need {MINIMUM_HOSTAGES_REQUIRED}");
                //Screen.ShowNotification($"~r~Warning: Only {successfulSpawns} hostages spawned! Retrying...");

                // Retry spawning missing hostages
                await BaseScript.Delay(1000);
            }
        }

        private void SetupNaturalHostageBehavior(int ped, Vector3 basePosition, float moveRadius)
        {
            // Give them a small wander area around their spawn point
            TaskWanderInArea(ped, basePosition.X, basePosition.Y, basePosition.Z, moveRadius, 1f, 1f);
            SetPedSeeingRange(ped, 40.0f);
            SetPedHearingRange(ped, 20.0f);

            // Set them to be alert but not flee immediately
            SetPedAlertness(ped, 1);
        }


        private async Task SpawnBankManager()
        {
            Vector3 managerPosition = new Vector3(253.6f, 223.3f, 106.3f);
            float managerHeading = 163.2f;

            // Get ground Z coordinate for safety
            float groundZ = managerPosition.Z;
            GetGroundZFor_3dCoord(managerPosition.X, managerPosition.Y, managerPosition.Z + 10f, ref groundZ, false);
            managerPosition = new Vector3(managerPosition.X, managerPosition.Y, groundZ);

            uint managerModel = (uint)GetHashKey("cs_bankman");

            Debug.WriteLine($"[HOSTAGE] Spawning bank manager behind desk at {managerPosition}");

            if (await LoadModel(managerModel))
            {
                int ped = CreatePed(4, managerModel, managerPosition.X, managerPosition.Y, managerPosition.Z, managerHeading, true, true);

                if (DoesEntityExist(ped))
                {
                    SetEntityAsMissionEntity(ped, true, true);
                    SetPedFleeAttributes(ped, 0, false);
                    SetPedCombatAttributes(ped, 17, true);
                    SetPedCanRagdoll(ped, false);

                    TaskStandStill(ped, -1);

                    bankManager = new Hostage(ped, HostageType.BankManager, managerPosition);
                    Debug.WriteLine($"[HOSTAGE] Successfully spawned bank manager {ped} behind desk");
                    //Screen.ShowNotification($"~b~Bank manager positioned behind desk");
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

            // Spawn crew member
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
                        // Use player position + offset for standby position
                        StandByPosition = standByPosition != Vector3.Zero ? standByPosition : playerPos + new Vector3(10f, 0f, 0f)
                    };

                    Debug.WriteLine($"[HOSTAGE] Successfully spawned crew member {ped} at {startPosition}");
                    //Screen.ShowNotification($"~o~Spawned crew member at {startPosition.X:F1}, {startPosition.Y:F1}");
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

                UpdateHostageEscapeLogic();

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

            // Track player position and distance from bank
            UpdatePlayerPosition();

            // Check if player has left the bank area
            if (!hasLeftBankArea && playerDistanceFromBank > BANK_EXIT_DISTANCE)
            {
                hasLeftBankArea = true;
                TriggerEscapeSequence();
                return;
            }

            // Escalate police presence over time (only if inside bank)
            if (currentTime - lastPoliceEscalationTime >= policeEscalationInterval)
            {
                EscalatePolicePresence(5); // +5 every 30 seconds
                lastPoliceEscalationTime = currentTime;
            }

            // Spawn police units based on pressure level
            SpawnPoliceUnitsBasedOnPressure(currentTime);

            // Check for overwhelming force breach (not mission fail)
            if (policePresenceLevel >= maxPolicePresence)
            {
                TriggerOverwhelmingForce();
            }
        }

        private void UpdatePlayerPosition()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            playerLastKnownPosition = playerPos;
            playerDistanceFromBank = Vector3.Distance(playerPos, bankExterior);
        }

        private void TriggerEscapeSequence()
        {
            Debug.WriteLine("[HOSTAGE] Player left bank area - triggering escape sequence");
            policeResponseActive = false; // Stop spawning more units

            Screen.ShowNotification("~y~You've left the bank! Police are in pursuit!");

            // Make existing police units pursue player
            foreach (var unit in spawnedPoliceUnits)
            {
                if (DoesEntityExist(unit.DriverId))
                {
                    SetPedCombatAttributes(unit.DriverId, 1424, true); // Make aggressive
                    SetPedCombatMovement(unit.DriverId, 2); // Aggressive movement
                    TaskCombatPed(unit.DriverId, PlayerPedId(), 0, 16); // Start pursuit
                }
            }

            // Trigger mission success/failure based on loot (handled by BankRobberyManager)
            OnPoliceBreach?.Invoke();
        }

        private void TriggerOverwhelmingForce()
        {
            if (!policeResponseActive) return;

            policeResponseActive = false;

            Screen.ShowNotification("~r~OVERWHELMING POLICE FORCE DEPLOYED!");
            Screen.ShowNotification("~r~Heavy tactical teams are breaching the building!");

            SpawnOverwhelmingResponse();

            Debug.WriteLine("[HOSTAGE] Overwhelming force triggered");
        }

        private async void SpawnOverwhelmingResponse()
        {
            Screen.ShowNotification("~r~BREACH TEAMS INBOUND!");

            Vector3[] breachPositions = {
        bankExterior + new Vector3(15f, 15f, 0f),
        bankExterior + new Vector3(-15f, -15f, 0f),
        bankExterior + new Vector3(15f, -15f, 0f),
        bankExterior + new Vector3(-15f, 15f, 0f),
    };

            for (int i = 0; i < breachPositions.Length; i++)
            {
                await SpawnPoliceUnit(breachPositions[i],
                                     (float)(i * 90),
                                     PoliceUnitType.BreachTeam,
                                     99);
                await BaseScript.Delay(500);
            }

            // Increase wanted level to maximum
            SetPlayerWantedLevel(PlayerId(), 5, false);
            SetPlayerWantedLevelNow(PlayerId(), false);
        }

        private void SpawnPoliceUnitsBasedOnPressure(float currentTime)
        {
            // Don't spawn if player has left bank area
            if (hasLeftBankArea) return;

            // Initial response (first wave)
            if (!hasSpawnedInitialResponse && policePresenceLevel >= 10)
            {
                SpawnInitialPoliceResponse();
                hasSpawnedInitialResponse = true;
            }

            // Escalation waves based on pressure level and time
            if (currentTime - lastPoliceSpawnTime >= 45f)
            {
                if (policePresenceLevel >= 25 && currentPoliceWave < 2)
                {
                    SpawnPoliceWave(2);
                }
                else if (policePresenceLevel >= 50 && currentPoliceWave < 3)
                {
                    SpawnPoliceWave(3);
                }
                else if (policePresenceLevel >= 75 && currentPoliceWave < 4)
                {
                    SpawnPoliceWave(4);
                }
            }
        }

        private async void SpawnInitialPoliceResponse()
        {
            Screen.ShowNotification("~r~Police units arriving on scene!");
            Debug.WriteLine("[HOSTAGE] Spawning initial police response");

            currentPoliceWave = 1;
            lastPoliceSpawnTime = GetGameTimer() / 1000f;

            // Spawn 2 police cars around the bank perimeter
            Vector3[] policePositions = {
        bankExterior + new Vector3(5f, 5f, 0f),   
        bankExterior + new Vector3(-5f, -5f, 0f), 
    };

            for (int i = 0; i < policePositions.Length; i++)
            {
                await SpawnPoliceUnit(policePositions[i],
                                     i == 0 ? 45f : 225f,
                                     PoliceUnitType.PoliceCar,
                                     currentPoliceWave);
                await BaseScript.Delay(1000);
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

        //private void TriggerPoliceBreach()
        //{
        //    if (!policeResponseActive) return;
        //
        //    policeResponseActive = false;
        //    OnPoliceBreach?.Invoke();
        //
        //    Screen.ShowNotification("~r~POLICE BREACH! MISSION FAILED!");
        //    Debug.WriteLine("[HOSTAGE] Police breached - mission failed");
        //}

        private void UpdateConeDetection()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Get camera direction
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
                        // Calculate distance to reticle center 
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

            // E key - Primary command
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

            // R key - Release hostage
            if (IsControlJustPressed(0, 45) && currentMissionType == "loud")
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

            // Reduce police presence
            int presenceReduction = 15 + (hostagesReleased * 5);
            policePresenceLevel = Math.Max(0, policePresenceLevel - presenceReduction);

            OnHostageReleased?.Invoke(policePresenceLevel);
            OnPolicePresenceChanged?.Invoke(policePresenceLevel);

            Screen.ShowNotification($"~g~Hostage released! Police presence reduced by {presenceReduction}%");
            Screen.ShowNotification($"~y~Hostages remaining: {HostagesRemaining}");

            Debug.WriteLine($"[HOSTAGE] Released hostage {hostage.PedId}. Police presence now {policePresenceLevel}%");

            // Remove after delay
            await BaseScript.Delay(5000);
            if (DoesEntityExist(hostage.PedId))
            {
                int pedId = hostage.PedId;
                DeletePed(ref pedId);
            }
        }

        private void UpdateHostageEscapeLogic()
        {
            // DON'T CHECK ESCAPE LOGIC WHILE SPAWNING OR BEFORE INITIAL SPAWN COMPLETES
            if (isSpawningHostages || !hasCompletedInitialSpawn)
            {
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            foreach (var hostage in hostages.Where(h => h.Type == HostageType.Civilian && h.State == HostageState.Normal))
            {
                if (!DoesEntityExist(hostage.PedId)) continue;

                var hostagePos = GetEntityCoords(hostage.PedId, true);
                var distanceToPlayer = Vector3.Distance(playerPos, hostagePos);

                if (distanceToPlayer > 50f)
                {
                    if (new Random().Next(0, 1000) < 5) 
                    {
                        TriggerHostageEscape(hostage);
                    }
                }
            }

            // Check if too many hostages have escaped - ONLY AFTER INITIAL SPAWN IS COMPLETE
            int totalHostages = hostages.Count(h => h.Type == HostageType.Civilian);

            if (totalHostages == 0)
            {
                return;
            }

            int hostagesTooFew = totalHostages - HostagesSecured - HostagesEscaped;

            if (hostagesTooFew > 0 && (totalHostages - HostagesEscaped) < MINIMUM_HOSTAGES_REQUIRED)
            {
                Debug.WriteLine($"[HOSTAGE] Checking failure condition: Total={totalHostages}, Secured={HostagesSecured}, Escaped={HostagesEscaped}, Required={MINIMUM_HOSTAGES_REQUIRED}");
                TriggerMissionFailureInsufficientHostages();
            }
        }

        private async void TriggerHostageEscape(Hostage hostage)
        {
            hostage.State = HostageState.Released; // Mark as escaped

            Screen.ShowNotification("~r~A hostage is trying to escape!");
            Debug.WriteLine($"[HOSTAGE] Hostage {hostage.PedId} attempting to escape");

            // Make them run to the nearest exit
            SetPedFleeAttributes(hostage.PedId, 0, true);
            TaskSmartFleePed(hostage.PedId, PlayerPedId(), 50f, -1, false, false);

            // Remove them after a delay
            await BaseScript.Delay(8000);
            if (DoesEntityExist(hostage.PedId))
            {
                int pedId = hostage.PedId;
                DeletePed(ref pedId);
                Debug.WriteLine($"[HOSTAGE] Escaped hostage {pedId} removed from scene");
            }
        }

        private void TriggerMissionFailureInsufficientHostages()
        {
            if (isSpawningHostages || !hasCompletedInitialSpawn)
            {
                Debug.WriteLine("[HOSTAGE] Skipping mission failure - still spawning hostages");
                return;
            }

            int totalHostages = hostages.Count(h => h.Type == HostageType.Civilian);
            if (totalHostages == 0)
            {
                Debug.WriteLine("[HOSTAGE] Skipping mission failure - no hostages spawned yet");
                return;
            }

            if (!hasCompletedInitialSpawn)
            {
                Debug.WriteLine("[HOSTAGE] Skipping mission failure - initial spawn not completed");
                return;
            }

            Screen.ShowNotification("~r~MISSION FAILED!");
            Screen.ShowNotification($"~r~Too many hostages escaped! Need at least {MINIMUM_HOSTAGES_REQUIRED}.");

            Debug.WriteLine($"[HOSTAGE] Mission failed - insufficient hostages. Total: {totalHostages}, Secured: {HostagesSecured}, Escaped: {HostagesEscaped}");
            OnMissionFailedInsufficientHostages?.Invoke();
        }


        private void CommandHostage(Hostage hostage)
        {
            switch (hostage.Type)
            {
                case HostageType.Civilian:
                    if (hostage.State == HostageState.Normal)
                    {
                        MakeHostageCrouch(hostage);

                        // First hostage command
                        if (!hasFirstHostageCommand)
                        {
                            hasFirstHostageCommand = true;
                            //MoveCrewToStandBy();
                        }

                        // Check if we've reached the minimum requirement
                        if (HasSufficientHostages && !AllHostagesCrouched)
                        {
                            //Screen.ShowNotification($"~g~{HostagesSecured}/{hostages.Count(h => h.Type == HostageType.Civilian)} hostages secured!");
                            Screen.ShowNotification("~g~Minimum requirement met! You can proceed with bank manager.");
                        }
                    }
                    break;

                case HostageType.BankManager:
                    if (HasSufficientHostages && hostage.State != HostageState.Following)
                    {
                        MakeBankManagerFollow(hostage);
                    }
                    else if (!HasSufficientHostages)
                    {
                        Screen.ShowNotification($"~r~Secure at least {MINIMUM_HOSTAGES_REQUIRED} hostages first! ({HostagesSecured}/{MINIMUM_HOSTAGES_REQUIRED})");
                    }
                    break;
            }
        }

        private async void MakeHostageCrouch(Hostage hostage)
        {
            hostage.State = HostageState.Crouched;

            // Clear existing tasks
            ClearPedTasks(hostage.PedId);
            ClearPedTasksImmediately(hostage.PedId);

            // Set attributes
            SetPedFleeAttributes(hostage.PedId, 0, false);
            SetPedCombatAttributes(hostage.PedId, 17, true);
            SetPedCanRagdoll(hostage.PedId, false);

            var hostagePos = GetEntityCoords(hostage.PedId, true);

            // Stop current movement
            SetEntityMaxSpeed(hostage.PedId, 0.5f);

            // Make them stay in small area around current position
            TaskStayInCover(hostage.PedId);

            await BaseScript.Delay(500);

            // Apply surrender animation
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
                Debug.WriteLine($"[HOSTAGE] Applied hands up animation to hostage {hostage.PedId}");
                await BaseScript.Delay(2000);
                TaskWanderInArea(hostage.PedId, hostagePos.X, hostagePos.Y, hostagePos.Z, 2f, 0.5f, 0.5f);
            }
            else
            {
                // Fallback
                SetPedCowerHash(hostage.PedId, "CODE_HUMAN_STAND_COWER");
                Debug.WriteLine($"[HOSTAGE] Using cower fallback for hostage {hostage.PedId}");
            }

            Screen.ShowNotification("~g~Hostage secured - they'll stay in the area.");
            Debug.WriteLine($"[HOSTAGE] Hostage {hostage.PedId} secured with limited movement");
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
                    doorOpeningProgress += 0.02f;

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
            if (currentTime - lastCrewMessageTime > 15f) 
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
            // Draw hostages in cone
            var allTargets = new List<Hostage>(hostages);
            if (bankManager != null)
            {
                allTargets.Add(bankManager);
            }

            foreach (var hostage in allTargets.Where(h => h.IsInCone))
            {
                if (!DoesEntityExist(hostage.PedId)) continue;

                var hostagePos = GetEntityCoords(hostage.PedId, true);
                DrawMarker(0, hostagePos.X, hostagePos.Y, hostagePos.Z + 2f, 0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.5f, 255, 255, 0, 100, false, true, 2, false, null, null, false);
            }

            // Police response UI for LOUD missions
            if (currentMissionType == "loud" && policeResponseActive)
            {
                DrawPoliceResponseUI();
            }


            int totalCivilians = hostages.Count(h => h.Type == HostageType.Civilian);
            var statusText = $"Hostages: {HostagesSecured}/{totalCivilians} secured (need {MINIMUM_HOSTAGES_REQUIRED})";

            if (HostagesEscaped > 0)
            {
                statusText += $" | {HostagesEscaped} escaped";
            }

            if (HasSufficientHostages)
            {
                statusText += " - Requirement met! Command bank manager.";
            }

            // Draw controls help
            var controlsText = "E: Command Hostage";
            if (currentMissionType == "loud")
            {
                controlsText += " | R: Release Hostage (reduces police pressure)";
            }

            // Draw status with color coding
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);

            // Color code based on hostage status
            if (HasSufficientHostages)
            {
                SetTextColour(0, 255, 0, 255); // Green - good
            }
            else if (HostagesSecured >= MINIMUM_HOSTAGES_REQUIRED / 2)
            {
                SetTextColour(255, 255, 0, 255); // Yellow 
            }
            else
            {
                SetTextColour(255, 0, 0, 255); // Red
            }

            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString(statusText);
            DrawText(0.02f, 0.1f);

            // Draw controls
            SetTextColour(255, 255, 255, 255);
            SetTextScale(0.0f, 0.3f);
            SetTextEntry("STRING");
            AddTextComponentString(controlsText);
            DrawText(0.02f, 0.15f);
        }

        private void DrawPoliceResponseUI()
        {
            // police pressure bar
            float barWidth = 0.25f;      // Use normalized coordinates
            float barHeight = 0.025f;    // Use normalized coordinates  
            float barX = 0.5f;           // Center X (50% of screen width)
            float barY = 0.05f;          // 5% down from top

            // Background
            DrawRect(barX, barY, barWidth, barHeight, 0, 0, 0, 150);

            // Progress bar
            float progress = (float)policePresenceLevel / maxPolicePresence;
            float progressWidth = barWidth * progress;
            float progressX = barX - (barWidth - progressWidth) / 2; 

            // Color based on danger level
            int r = 255, g = 255, b = 0;
            if (progress >= 0.8f)
            {
                r = 255; g = 0; b = 0; 
            }
            else if (progress >= 0.5f)
            {
                r = 255; g = 128; b = 0; 
            }
            else if (progress >= 0.25f)
            {
                r = 255; g = 255; b = 0; 
            }
            else
            {
                r = 0; g = 255; b = 0; // Green - safe
            }

            DrawRect(progressX, barY, progressWidth, barHeight, r, g, b, 200);

            SetTextFont(1);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.45f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextCentre(true); // CENTER THE TEXT
            SetTextEntry("STRING");
            AddTextComponentString($"POLICE PRESSURE: {policePresenceLevel}%");
            DrawText(barX, barY - 0.035f);
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

        private async void SpawnPoliceWave(int wave)
        {
            if (hasLeftBankArea) return; // Don't spawn if player left

            currentPoliceWave = wave;
            lastPoliceSpawnTime = GetGameTimer() / 1000f;

            switch (wave)
            {
                case 2:
                    Screen.ShowNotification("~r~Police reinforcements have arrived!");
                    await SpawnRiotVans();
                    break;

                case 3:
                    Screen.ShowNotification("~r~SWAT teams deployed!");
                    await SpawnSWATResponse();
                    await SpawnPoliceHelicopter();
                    break;

                case 4:
                    Screen.ShowNotification("~r~Heavy tactical units in position!");
                    await SpawnHeavyTactical();
                    break;
            }

            Debug.WriteLine($"[HOSTAGE] Police wave {wave} spawned");
        }

        private async Task SpawnRiotVans()
        {
            Vector3[] riotPositions = {
        bankExterior + new Vector3(5f, 0f, 0f),  
        bankExterior + new Vector3(-5f, 0f, 0f), 
    };

            for (int i = 0; i < riotPositions.Length; i++)
            {
                await SpawnPoliceUnit(riotPositions[i],
                                     i == 0 ? 270f : 90f,
                                     PoliceUnitType.RiotVan,
                                     currentPoliceWave);
                await BaseScript.Delay(800);
            }
        }

        private async Task SpawnSWATResponse()
        {
            Vector3[] swatPositions = {
        bankExterior + new Vector3(10f, -5f, 0f),
        bankExterior + new Vector3(-10f, 5f, 0f),
    };

            for (int i = 0; i < swatPositions.Length; i++)
            {
                await SpawnPoliceUnit(swatPositions[i],
                                     i == 0 ? 45f : 315f,
                                     PoliceUnitType.SWAT,
                                     currentPoliceWave);
                await BaseScript.Delay(1000);
            }
        }

        private async Task SpawnPoliceHelicopter()
        {
            try
            {
                uint heliModel = (uint)GetHashKey("polmav"); // Police helicopter

                if (!await LoadModel(heliModel))
                {
                    Debug.WriteLine("[HOSTAGE] Failed to load helicopter model");
                    return;
                }

                Vector3 heliSpawn = bankExterior + new Vector3(0f, 0f, 50f);

                int helicopter = CreateVehicle(heliModel, heliSpawn.X, heliSpawn.Y, heliSpawn.Z, 0f, true, false);

                if (DoesEntityExist(helicopter))
                {
                    SetEntityAsMissionEntity(helicopter, true, true);
                    SetVehicleEngineOn(helicopter, true, true, false);

                    // Create helicopter pilot
                    uint pilotModel = (uint)GetHashKey("s_m_m_pilot_02");
                    if (await LoadModel(pilotModel))
                    {
                        int pilot = CreatePed(4, pilotModel, heliSpawn.X, heliSpawn.Y, heliSpawn.Z, 0f, true, true);
                        if (DoesEntityExist(pilot))
                        {
                            SetPedIntoVehicle(pilot, helicopter, -1); // Driver seat
                            SetEntityAsMissionEntity(pilot, true, true);

                            // Make helicopter circle the bank
                            TaskHeliChase(pilot, PlayerPedId(), 0.0f, 0.0f, 80.0f);

                        }
                        SetModelAsNoLongerNeeded(pilotModel);
                    }

                    spawnedHelicopters.Add(helicopter);
                    Screen.ShowNotification("~r~Police helicopter overhead!");
                    Debug.WriteLine("[HOSTAGE] Police helicopter spawned successfully");
                }

                SetModelAsNoLongerNeeded(heliModel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HOSTAGE] Helicopter spawn error: {ex.Message}");
            }
        }

        private async Task SpawnHeavyTactical()
        {
            Vector3[] tacticalPositions = {
        bankExterior + new Vector3(40f, 20f, 0f),  
        bankExterior + new Vector3(-40f, -20f, 0f),
        bankExterior + new Vector3(0f, -45f, 0f),  
    };

            for (int i = 0; i < tacticalPositions.Length; i++)
            {
                await SpawnPoliceUnit(tacticalPositions[i],
                                     (float)(i * 120),
                                     PoliceUnitType.BreachTeam,
                                     currentPoliceWave);
                await BaseScript.Delay(1200);
            }
        }

        private async Task<bool> SpawnPoliceUnit(Vector3 position, float heading, PoliceUnitType unitType, int wave)
        {
            try
            {
                uint vehicleModel = GetPoliceVehicleModel(unitType);
                uint officerModel = GetPoliceOfficerModel(unitType);

                if (!await LoadModel(vehicleModel) || !await LoadModel(officerModel))
                {
                    Debug.WriteLine($"[HOSTAGE] Failed to load models for {unitType}");
                    return false;
                }

                // Get ground Z coordinate
                float groundZ = position.Z;
                GetGroundZFor_3dCoord(position.X, position.Y, position.Z + 10f, ref groundZ, false);
                Vector3 spawnPos = new Vector3(position.X, position.Y, groundZ);

                // Spawn vehicle
                int vehicle = CreateVehicle(vehicleModel, spawnPos.X, spawnPos.Y, spawnPos.Z, heading, true, false);

                if (DoesEntityExist(vehicle))
                {
                    SetEntityAsMissionEntity(vehicle, true, true);
                    SetVehicleOnGroundProperly(vehicle);

                    // Spawn driver
                    int driver = CreatePed(4, officerModel, spawnPos.X, spawnPos.Y, spawnPos.Z, heading, true, true);
                    if (DoesEntityExist(driver))
                    {
                        SetPedIntoVehicle(driver, vehicle, -1); // Driver seat
                        SetEntityAsMissionEntity(driver, true, true);
                        SetupStaticPoliceOfficer(driver, unitType);

                        var policeUnit = new PoliceUnit(vehicle, driver, spawnPos, unitType, wave);
                        spawnedPoliceUnits.Add(policeUnit);

                        Debug.WriteLine($"[HOSTAGE] Spawned {unitType} at {spawnPos}");
                    }
                }

                SetModelAsNoLongerNeeded(vehicleModel);
                SetModelAsNoLongerNeeded(officerModel);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HOSTAGE] Police unit spawn error: {ex.Message}");
                return false;
            }
        }

        private uint GetPoliceVehicleModel(PoliceUnitType unitType)
        {
            switch (unitType)
            {
                case PoliceUnitType.PoliceCar: return (uint)GetHashKey("police");
                case PoliceUnitType.RiotVan: return (uint)GetHashKey("riot");
                case PoliceUnitType.SWAT: return (uint)GetHashKey("fbi2");
                case PoliceUnitType.BreachTeam: return (uint)GetHashKey("insurgent3");
                default: return (uint)GetHashKey("police");
            }
        }

        private uint GetPoliceOfficerModel(PoliceUnitType unitType)
        {
            switch (unitType)
            {
                case PoliceUnitType.PoliceCar: return (uint)GetHashKey("s_m_y_cop_01");
                case PoliceUnitType.RiotVan: return (uint)GetHashKey("s_m_m_armoured_01");
                case PoliceUnitType.SWAT: return (uint)GetHashKey("s_m_y_swat_01");
                case PoliceUnitType.BreachTeam: return (uint)GetHashKey("s_m_y_blackops_01");
                default: return (uint)GetHashKey("s_m_y_cop_01");
            }
        }

        private void SetupStaticPoliceOfficer(int officer, PoliceUnitType unitType)
        {
            // Make them static and defensive, not aggressive
            SetPedCombatAttributes(officer, 46, false);  // Don't use vehicles for pursuit
            SetPedCombatAttributes(officer, 1424, false); // Don't always fight
            SetPedCombatAttributes(officer, 3, true);     // Use cover
            SetPedCombatRange(officer, 2); // Long range but defensive

            // Give appropriate weapons
            uint weaponHash = GetPoliceWeapon(unitType);
            GiveWeaponToPed(officer, weaponHash, 500, false, true);

            // Set them to guard position, not pursue
            SetPedFleeAttributes(officer, 0, false);
            SetPedCombatMovement(officer, 0); // Defensive movement only

            // Make them aim at player but not pursue into bank
            SetPedAsEnemy(officer, false); // Don't make them actively hunt player
            SetPedRelationshipGroupHash(officer, (uint)GetHashKey("COP"));

            Debug.WriteLine($"[HOSTAGE] Static police officer setup complete: {unitType}");
        }

        private uint GetPoliceWeapon(PoliceUnitType unitType)
        {
            switch (unitType)
            {
                case PoliceUnitType.PoliceCar: return (uint)GetHashKey("weapon_pistol");
                case PoliceUnitType.RiotVan: return (uint)GetHashKey("weapon_pumpshotgun");
                case PoliceUnitType.SWAT: return (uint)GetHashKey("weapon_carbinerifle");
                case PoliceUnitType.BreachTeam: return (uint)GetHashKey("weapon_specialcarbine");
                default: return (uint)GetHashKey("weapon_pistol");
            }
        }

        public void Cleanup()
        {
            IsActive = false;
            policeResponseActive = false;

            try
            {
                // Clean up police units
                foreach (var unit in spawnedPoliceUnits)
                {
                    if (DoesEntityExist(unit.VehicleId))
                    {
                        int vehicleId = unit.VehicleId;
                        DeleteVehicle(ref vehicleId);
                    }
                    if (DoesEntityExist(unit.DriverId))
                    {
                        int driverId = unit.DriverId;
                        DeletePed(ref driverId);
                    }
                    if (unit.PassengerId != 0 && DoesEntityExist(unit.PassengerId))
                    {
                        int passengerId = unit.PassengerId;
                        DeletePed(ref passengerId);
                    }
                }
                spawnedPoliceUnits.Clear();

                // Clean up helicopters
                foreach (var heli in spawnedHelicopters)
                {
                    if (DoesEntityExist(heli))
                    {
                        int heliId = heli;
                        DeleteVehicle(ref heliId);
                    }
                }
                spawnedHelicopters.Clear();

                // Reset police tracking
                currentPoliceWave = 0;
                hasSpawnedInitialResponse = false;
                hasLeftBankArea = false;

                // Clear wanted level
                SetPlayerWantedLevel(PlayerId(), 0, false);
                SetPlayerWantedLevelNow(PlayerId(), false);

                // Clean up hostages (existing code)
                foreach (var hostage in hostages)
                {
                    if (DoesEntityExist(hostage.PedId))
                    {
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

                Debug.WriteLine("[HOSTAGE] Hostage system and police response cleaned up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HOSTAGE] Cleanup error: {ex.Message}");
            }
        }


    }
}
