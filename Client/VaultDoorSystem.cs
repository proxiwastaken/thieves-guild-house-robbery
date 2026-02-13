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
        // vault door
        private uint vaultDoorHash = 961976194; // v_ilev_bk_vaultdoor
        private Vector3 terminalPosition = new Vector3(253.3081f, 228.4226f, 101.6833f);

        // Door heading values
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
            isHacking = false;
            hackingProgress = 0f;
            IsUnlocked = false;
            State = VaultDoorState.Closed;
            vaultDoorObject = 0;

            FindVaultDoor();
        }

        private async void FindVaultDoor()
        {
            var playerPos = GetEntityCoords(PlayerPedId(), true);

            // Find the vault door object
            vaultDoorObject = GetClosestObjectOfType(playerPos.X, playerPos.Y, playerPos.Z, 50.0f, vaultDoorHash, false, false, false);

            if (vaultDoorObject != 0 && DoesEntityExist(vaultDoorObject))
            {
                // secure the door
                SetEntityHeading(vaultDoorObject, DOOR_CLOSED_HEADING);
                FreezeEntityPosition(vaultDoorObject, true);

                // Wait a frame then double-check
                await BaseScript.Delay(50);
                SetEntityHeading(vaultDoorObject, DOOR_CLOSED_HEADING);

                // Make sure no other scripts can interfere
                SetEntityCollision(vaultDoorObject, true, true);

                Debug.WriteLine($"[VAULT] Vault door {vaultDoorObject} SECURED at heading {GetEntityHeading(vaultDoorObject):F1}°");
                //Screen.ShowNotification("~g~Vault door located and secured!");
            }
            else
            {
                Debug.WriteLine($"[VAULT] No vault door found near player at {playerPos} (search radius: 50m)");
                // Don't show error notification here - it will try again
            }
        }

        public void StartHacking()
        {
            if (State != VaultDoorState.Closed || isHacking)
            {
                Screen.ShowNotification("~r~Cannot hack door right now!");
                return;
            }

            // Check if player is at terminal
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

            State = VaultDoorState.Closed; // Door is closed but now unlocked
            OnStateChanged?.Invoke(State);

            Screen.ShowNotification("~g~Vault terminal hacked! You can now control the door!");
            Screen.ShowNotification("~y~Use LEFT/RIGHT arrow keys to open/close the vault!");
            Debug.WriteLine("[VAULT] Vault terminal hack completed - door is now controllable");

            OnDoorOpened?.Invoke();
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

            // If we don't have a door object yet, try to find it when player is near the bank
            if (vaultDoorObject == 0 || !DoesEntityExist(vaultDoorObject))
            {
                var playerPos = GetEntityCoords(PlayerPedId(), true);
                var bankPos = new Vector3(255.2f, 223.2f, 102.3f); 
                float distanceToBank = Vector3.Distance(playerPos, bankPos);

                if (distanceToBank < 30f)
                {
                    FindVaultDoor();
                }
                return;
            }

            // Only allow door control if unlocked
            if (!IsUnlocked)
                return;

            // Check if player is near terminal
            var currentPlayerPos = GetEntityCoords(PlayerPedId(), true);
            float distanceToTerminal = GetDistanceBetweenCoords(currentPlayerPos.X, currentPlayerPos.Y, currentPlayerPos.Z,
                terminalPosition.X, terminalPosition.Y, terminalPosition.Z, true);

            if (distanceToTerminal <= 2.0f)
            {
                HandleDoorControls();
            }
        }

        private void HandleDoorControls()
        {
            if (vaultDoorObject == 0 || !DoesEntityExist(vaultDoorObject)) return;

            // Get current door heading 
            float currentHeading = GetEntityHeading(vaultDoorObject);
            float roundedHeading = (float)Math.Round(currentHeading, 1);

            if (roundedHeading == 158.7f)
            {
                currentHeading = currentHeading - 0.1f;
                roundedHeading = (float)Math.Round(currentHeading, 1);
            }

            // Show appropriate help text
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

            // Handle opening (Left arrow key)
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

            // Handle closing (Right arrow key)
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

            float barWidth = 0.25f;      // Use normalized screen coordinates (0.0 to 1.0)
            float barHeight = 0.025f;    // Use normalized coordinates
            float barX = 0.5f;           // Center X (50% of screen width)
            float barY = 0.8f;           // 80% down from top

            // Background
            DrawRect(barX, barY, barWidth, barHeight, 0, 0, 0, 150);

            // Progress
            float progressWidth = barWidth * hackingProgress;
            float progressX = barX - (barWidth - progressWidth) / 2; 
            DrawRect(progressX, barY, progressWidth, barHeight, 0, 255, 0, 200);

            //Text positioning
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.6f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextCentre(true); 
            SetTextEntry("STRING");
            AddTextComponentString($"HACKING TERMINAL... {(int)(hackingProgress * 100)}%");
            DrawText(barX, barY - 0.04f);
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
