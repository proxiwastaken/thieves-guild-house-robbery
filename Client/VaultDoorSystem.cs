using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum VaultDoorState
    {
        Closed,
        Opening,
        Open,
        Failed
    }

    public class VaultDoorSystem
    {
        // Union Depository vault door hash and terminal position (from Lua)
        private uint vaultDoorHash = 961976194; // v_ilev_bk_vaultdoor
        private Vector3 terminalPosition = new Vector3(253.3081f, 228.4226f, 101.6833f); // From Lua

        // Door heading values (from Lua)
        private const float DOOR_CLOSED_HEADING = 160.0f;
        private const float DOOR_OPEN_HEADING = 0.0f;
        private const float DOOR_SPEED = 1.0f; // Rotation speed

        public VaultDoorState State { get; private set; } = VaultDoorState.Closed;
        public bool IsUnlocked { get; private set; } = false;

        private int vaultDoorObject = 0;
        private bool isHacking = false;
        private float hackingProgress = 0f;
        private const float HACKING_TIME = 15f;

        // Events
        public event Action<VaultDoorState> OnStateChanged;
        public event Action OnDoorOpened;

        public void Initialize()
        {
            FindVaultDoor();
            State = VaultDoorState.Closed;
            Debug.WriteLine("[VAULT] Vault door system initialized using Lua method");
        }

        private void FindVaultDoor()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);

            // Find the vault door object (from Lua method)
            vaultDoorObject = GetClosestObjectOfType(playerPos.X, playerPos.Y, playerPos.Z, 100.0f, vaultDoorHash, false, false, false);

            if (vaultDoorObject != 0 && DoesEntityExist(vaultDoorObject))
            {
                // Freeze the door to prevent game interference (from Lua)
                FreezeEntityPosition(vaultDoorObject, true);

                // Set to closed position
                SetEntityHeading(vaultDoorObject, DOOR_CLOSED_HEADING);

                Debug.WriteLine($"[VAULT] Found vault door object: {vaultDoorObject}");
                Screen.ShowNotification("~g~Vault door located and secured!");
            }
            else
            {
                Debug.WriteLine("[VAULT] Failed to find vault door object");
                Screen.ShowNotification("~r~Could not locate vault door!");
            }
        }

        public void StartHacking()
        {
            if (State != VaultDoorState.Closed || isHacking)
            {
                Screen.ShowNotification("~r~Cannot hack door right now!");
                return;
            }

            // Check if player is at terminal (from Lua)
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToTerminal = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                terminalPosition.X, terminalPosition.Y, terminalPosition.Z, true);

            if (distanceToTerminal > 1.5f)
            {
                Screen.ShowNotification("~r~Get closer to the terminal to hack!");
                return;
            }

            isHacking = true;
            hackingProgress = 0f;
            State = VaultDoorState.Opening;
            OnStateChanged?.Invoke(State);

            Screen.ShowNotification("~y~Hacking vault terminal... Stay close!");
            Debug.WriteLine("[VAULT] Started hacking vault terminal");

            StartHackingProcess();
        }

        private async void StartHackingProcess()
        {
            while (isHacking && hackingProgress < 1f)
            {
                await BaseScript.Delay(100);

                // Check if player is still near terminal
                var playerPos = GetEntityCoords(PlayerPedId(), true);
                var distanceToTerminal = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                    terminalPosition.X, terminalPosition.Y, terminalPosition.Z, true);

                if (distanceToTerminal > 2f)
                {
                    Screen.ShowNotification("~r~Too far from terminal! Hack failed.");
                    FailHacking();
                    return;
                }

                // Increment progress
                hackingProgress += 0.1f / HACKING_TIME;

                // Show progress
                int progressPercent = (int)(hackingProgress * 100);
                Screen.DisplayHelpTextThisFrame($"Hacking vault terminal... {progressPercent}%");

                if (hackingProgress >= 1f)
                {
                    CompleteHacking();
                    return;
                }
            }
        }

        private void CompleteHacking()
        {
            isHacking = false;
            hackingProgress = 1f;
            IsUnlocked = true;

            Screen.ShowNotification("~g~Vault terminal hacked! You can now control the door!");
            Debug.WriteLine("[VAULT] Vault terminal hack completed successfully");

            // Don't auto-open, let player control it manually like in Lua
            State = VaultDoorState.Closed; // Still closed but now unlocked
            OnStateChanged?.Invoke(State);
        }

        private void FailHacking()
        {
            isHacking = false;
            hackingProgress = 0f;
            State = VaultDoorState.Failed;
            OnStateChanged?.Invoke(State);

            Screen.ShowNotification("~r~Terminal hack failed!");
            Debug.WriteLine("[VAULT] Terminal hack failed");

            BaseScript.Delay(3000).ContinueWith(_ => {
                State = VaultDoorState.Closed;
                OnStateChanged?.Invoke(State);
            });
        }

        public void Update()
        {
            if (isHacking)
            {
                DrawHackingUI();
                return;
            }

            // Only allow door control if unlocked (like Lua)
            if (!IsUnlocked || vaultDoorObject == 0 || !DoesEntityExist(vaultDoorObject))
                return;

            // Check if player is near terminal (from Lua)
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToTerminal = GetDistanceBetweenCoords(playerPos.X, playerPos.Y, playerPos.Z,
                terminalPosition.X, terminalPosition.Y, terminalPosition.Z, true);

            if (distanceToTerminal <= 1.5f)
            {
                HandleDoorControls();
            }
        }

        private void HandleDoorControls()
        {
            if (vaultDoorObject == 0 || !DoesEntityExist(vaultDoorObject)) return;

            // Get current door heading (from Lua)
            float currentHeading = GetEntityHeading(vaultDoorObject);
            float roundedHeading = (float)Math.Round(currentHeading, 1);

            // Adjust for Lua's specific heading fix
            if (roundedHeading == 158.7f)
            {
                currentHeading = currentHeading - 0.1f;
                roundedHeading = (float)Math.Round(currentHeading, 1);
            }

            // Show appropriate help text (from Lua)
            if (roundedHeading != 0.0f && roundedHeading != 160.0f)
            {
                Screen.DisplayHelpTextThisFrame("Hold ~INPUT_CELLPHONE_LEFT~ to Open Vault~n~Hold ~INPUT_CELLPHONE_RIGHT~ to Close Vault");
            }
            else if (roundedHeading == 0.0f)
            {
                Screen.DisplayHelpTextThisFrame("Hold ~INPUT_CELLPHONE_RIGHT~ to Close Vault");
            }
            else if (roundedHeading == 160.0f)
            {
                Screen.DisplayHelpTextThisFrame("Hold ~INPUT_CELLPHONE_LEFT~ to Open Vault");
            }

            // Handle opening (Left arrow key - Control 174)
            if (IsControlPressed(1, 174) && roundedHeading != 0.0f) // Open
            {
                float newHeading = Math.Max(0.0f, currentHeading - DOOR_SPEED);
                SetEntityHeading(vaultDoorObject, newHeading);

                if (State != VaultDoorState.Opening && State != VaultDoorState.Open)
                {
                    State = VaultDoorState.Opening;
                    OnStateChanged?.Invoke(State);
                }

                if (Math.Round(newHeading, 1) <= 0.0f)
                {
                    State = VaultDoorState.Open;
                    OnStateChanged?.Invoke(State);
                    OnDoorOpened?.Invoke();
                    Screen.ShowNotification("~g~VAULT DOOR FULLY OPENED!");
                }
            }

            // Handle closing (Right arrow key - Control 175)
            if (IsControlPressed(1, 175) && roundedHeading != 160.0f) // Close
            {
                float newHeading = Math.Min(160.0f, currentHeading + DOOR_SPEED);
                SetEntityHeading(vaultDoorObject, newHeading);

                if (State != VaultDoorState.Closed)
                {
                    State = VaultDoorState.Closed;
                    OnStateChanged?.Invoke(State);
                    Screen.ShowNotification("~r~Vault door closing...");
                }
            }
        }

        private void DrawHackingUI()
        {
            if (!isHacking) return;

            // Draw progress bar
            float screenWidth = 1920f;
            float screenHeight = 1080f;
            float barWidth = 400f;
            float barHeight = 30f;
            float barX = (screenWidth - barWidth) / 2f;
            float barY = screenHeight * 0.8f;

            // Background
            DrawRect(barX / screenWidth, barY / screenHeight, barWidth / screenWidth, barHeight / screenHeight, 0, 0, 0, 150);

            // Progress
            float progressWidth = barWidth * hackingProgress;
            DrawRect(barX / screenWidth, barY / screenHeight, progressWidth / screenWidth, barHeight / screenHeight, 0, 255, 0, 200);

            // Text
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.6f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString($"HACKING TERMINAL... {(int)(hackingProgress * 100)}%");
            DrawText((barX + 20f) / screenWidth, (barY - 10f) / screenHeight);
        }

        public void Cleanup()
        {
            isHacking = false;
            State = VaultDoorState.Closed;
            IsUnlocked = false;

            // Reset door to closed position
            if (vaultDoorObject != 0 && DoesEntityExist(vaultDoorObject))
            {
                SetEntityHeading(vaultDoorObject, DOOR_CLOSED_HEADING);
                FreezeEntityPosition(vaultDoorObject, false);
            }

            Debug.WriteLine("[VAULT] Vault door system cleaned up");
        }
    }
}
