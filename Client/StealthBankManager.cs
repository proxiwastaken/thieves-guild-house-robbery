using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class StealthBankManager
    {
        private LoadoutSystem loadoutSystem;
        private BankTruckSystem bankTruckSystem;
        private CameraManager stealthCameraManager;
        private GuardSystem stealthGuardSystem;
        private WaypointSystem waypointSystem;
        private BankManagerSystem bankManagerSystem;

        private bool isStealthMode = false;
        private bool hasEnteredBank = false;
        private bool stealthCompromised = false;

        private Vector3[] cameraPositions = {
            new Vector3(0f, 0f, 0f), // UPDATE
            new Vector3(0f, 0f, 0f), // UPDATE
            new Vector3(0f, 0f, 0f), // UPDATE
            new Vector3(0f, 0f, 0f), // UPDATE
        };

        private Vector3[] stealthGuardPositions = {
            new Vector3(0f, 0f, 0f), // UPDATE
            new Vector3(0f, 0f, 0f), // UPDATE
            new Vector3(0f, 0f, 0f), // UPDATE
        };

        // Events
        public event Action OnStealthCompromised;
        public event Action OnStealthBankEntryGranted;

        public bool IsStealthMode => isStealthMode;
        public bool IsStealthCompromised => stealthCompromised;

        public StealthBankManager(CameraManager cameraManager, BankGateSystem gateSystem)
        {
            stealthCameraManager = cameraManager;
            loadoutSystem = new LoadoutSystem();
            waypointSystem = new WaypointSystem();
            bankTruckSystem = new BankTruckSystem(waypointSystem, gateSystem);
            stealthGuardSystem = new GuardSystem();
            bankManagerSystem = new BankManagerSystem();

            // Subscribe to events
            bankTruckSystem.OnTruckStolen += OnTruckStolen;
            bankTruckSystem.OnTruckArrivedAtBank += OnTruckArrivedAtBank;
            bankTruckSystem.OnBankEntryReady += OnBankEntryReady;
            bankManagerSystem.OnBankManagerBetrayal += OnManagerBetrayal;
        }

        public async void Initialize()
        {
            isStealthMode = true;
            stealthCompromised = false;
            hasEnteredBank = false;

            // Initialize systems
            loadoutSystem.Initialize();
            bankTruckSystem.Initialize();
            stealthGuardSystem.Initialize();
            bankManagerSystem.Initialize();

            // Give player silenced weapon
            loadoutSystem.ApplyMissionLoadout(MissionType.Quiet);

            // Setup stealth-specific cameras
            await SetupStealthCameras();

            // Setup stealth-specific guards
            await SetupStealthGuards();

            Screen.ShowNotification("~b~STEALTH MODE: Steal the bank truck for entry!");
            Screen.ShowNotification("~y~Stay undetected and eliminate threats silently!");

            Debug.WriteLine("[STEALTH_BANK] Stealth bank manager initialized");
        }
        private void OnBankEntryReady()
        {
            Screen.ShowNotification("~g~Stealth entry successful! The manager will escort you to the vault.");
            Debug.WriteLine("[STEALTH_BANK] Player ready for bank entry");
        }

        private void OnManagerBetrayal()
        {
            CompromiseStealth();
            Screen.ShowNotification("~r~STEALTH BLOWN! The manager spotted you as fake!");
            Screen.ShowNotification("~r~Fight your way out or complete the heist!");
            Debug.WriteLine("[STEALTH_BANK] Manager betrayal - stealth compromised");
        }

        private async Task SetupStealthCameras()
        {
            // Clear existing cameras
            stealthCameraManager.ClearCameras();

            float[] cameraRotations = { 0f, 90f, 180f, 270f }; // UPDATE

            for (int i = 0; i < cameraPositions.Length; i++)
            {
                if (cameraPositions[i] != Vector3.Zero) // Skip if position not set
                {
                    var camera = new Camera(
                        cameraPositions[i],
                        cameraRotations[i % cameraRotations.Length],
                        detectionRange: 12f,  // Detection range
                        viewAngle: 45f,       // View angle
                        scanAngle: 60f,       // Scan angle
                        scanSpeed: 1.5f,      // Scan speed
                        waitTime: 2f          // Wait time
                    );

                    stealthCameraManager.AddCamera(camera);
                    Debug.WriteLine($"[STEALTH_BANK] Added camera {i + 1} at {cameraPositions[i]}");
                }

                await BaseScript.Delay(200);
            }
        }

        private async Task SetupStealthGuards()
        {
            // TODO PATROL PATHS
            for (int i = 0; i < stealthGuardPositions.Length; i++)
            {
                if (stealthGuardPositions[i] != Vector3.Zero) // Skip if position not set
                {
                    // Simple patrol
                    var guardPatrol = new List<PatrolNode>
                    {
                        new PatrolNode(stealthGuardPositions[i] + new Vector3(2f, 0f, 0f), 90f, 3f, true),
                        new PatrolNode(stealthGuardPositions[i] + new Vector3(0f, 2f, 0f), 0f, 3f, true),
                        new PatrolNode(stealthGuardPositions[i] + new Vector3(-2f, 0f, 0f), 270f, 3f, true),
                        new PatrolNode(stealthGuardPositions[i] + new Vector3(0f, -2f, 0f), 180f, 3f, true),
                    };

                    stealthGuardSystem.AddGuard(stealthGuardPositions[i], guardPatrol);
                    Debug.WriteLine($"[STEALTH_BANK] Added stealth guard {i + 1} at {stealthGuardPositions[i]}");
                }

                await BaseScript.Delay(500);
            }

            // Subscribe to guard alerts
            stealthGuardSystem.OnAllGuardsAlerted += OnStealthCompromisedByGuards;
        }

        private void OnTruckStolen()
        {
            Screen.ShowNotification("~g~Bank truck stolen! Head to the delivery entrance.");
            Debug.WriteLine("[STEALTH_BANK] Player successfully stole bank truck");
        }

        private void OnTruckArrivedAtBank()
        {
            hasEnteredBank = true;
            OnStealthBankEntryGranted?.Invoke();
            Screen.ShowNotification("~g~Stealth entry granted! Avoid cameras and guards.");
            Debug.WriteLine("[STEALTH_BANK] Player gained stealth entry to bank");
        }

        private void OnStealthCompromisedByGuards()
        {
            CompromiseStealth();
        }

        private void CompromiseStealth()
        {
            if (stealthCompromised) return;

            stealthCompromised = true;
            OnStealthCompromised?.Invoke();

            Screen.ShowNotification("~r~STEALTH COMPROMISED!");
            Screen.ShowNotification("~r~Guards are alerted! Situation turned loud!");

            Debug.WriteLine("[STEALTH_BANK] Stealth compromised - switching to loud mode");
        }

        public void Update()
        {
            if (!isStealthMode) return;

            bankTruckSystem.Update();
            bankManagerSystem.Update();
            stealthGuardSystem.Update();
            stealthCameraManager.Update();

            // Check camera detection
            CheckCameraDetection();
        }

        private void CheckCameraDetection()
        {
            if (stealthCompromised) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            foreach (var camera in stealthCameraManager.Cameras)
            {
                if (camera.IsPlayerDetected(playerPos))
                {
                    CompromiseStealth();
                    break;
                }
            }
        }

        public void DrawDebugInfo()
        {
            if (!isStealthMode) return;

            //stealthGuardSystem.DrawDebugInfo();
            stealthCameraManager.DrawCameras();

            // Draw stealth status
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);

            if (stealthCompromised)
            {
                SetTextColour(255, 0, 0, 255); // Red
            }
            else
            {
                SetTextColour(0, 255, 0, 255); // Green
            }

            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString($"STEALTH STATUS: {(stealthCompromised ? "COMPROMISED" : "ACTIVE")}");
            DrawText(0.02f, 0.18f);

            // Draw truck status
            SetTextColour(255, 255, 255, 255);
            SetTextScale(0.0f, 0.3f);
            SetTextEntry("STRING");
            AddTextComponentString($"Truck Status: {bankTruckSystem.State}");
            DrawText(0.02f, 0.22f);
        }

        public void Cleanup()
        {
            loadoutSystem?.Cleanup();
            bankTruckSystem?.Cleanup();
            stealthGuardSystem?.Cleanup();
            stealthCameraManager?.ClearCameras();
            waypointSystem?.Cleanup();
            bankManagerSystem?.Cleanup();

            isStealthMode = false;
            stealthCompromised = false;
            hasEnteredBank = false;

            Debug.WriteLine("[STEALTH_BANK] Stealth bank manager cleaned up");
        }
    }
}
