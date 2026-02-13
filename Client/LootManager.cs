using System;
using System.Collections.Generic;
using System.Linq;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum LootInteractionState
    {
        Available,
        PlayerLooting,
        CrewLooting,
        Completed
    }

    public class VaultGoldLoot
    {
        public Vector3 Position { get; }
        public int TotalGold { get; private set; }
        public int CollectedGold { get; private set; }
        public LootInteractionState State { get; set; }
        public float LootingProgress { get; set; }
        public List<int> LootingCrew { get; set; }

        private const int GOLD_PER_SECOND = 200;
        private const int TARGET_GOLD_AMOUNT = 2000; 

        // Enhanced timing system
        private float accumulatedGold = 0f;
        private float lastUpdateTime = 0f;
        private bool isLooting = false;

        public VaultGoldLoot(Vector3 position)
        {
            Position = position;
            TotalGold = TARGET_GOLD_AMOUNT;
            CollectedGold = 0;
            State = LootInteractionState.Available;
            LootingProgress = 0f;
            LootingCrew = new List<int>();
            accumulatedGold = 0f;
            lastUpdateTime = GetGameTimer() / 1000f;
        }

        public bool IsCompleted => CollectedGold >= TotalGold;
        public float CompletionPercentage => (float)CollectedGold / TotalGold;
        public int RemainingGold => Math.Max(0, TotalGold - CollectedGold);

        public void UpdateLooting(float deltaTime, int activeLooters)
        {
            if (IsCompleted || activeLooters == 0)
            {
                isLooting = false;
                return;
            }

            if (!isLooting)
            {
                isLooting = true;
                lastUpdateTime = GetGameTimer() / 1000f;
            }

            // Calculate time-based collection
            float currentTime = GetGameTimer() / 1000f;
            float actualDeltaTime = currentTime - lastUpdateTime;
            lastUpdateTime = currentTime;

            // More looters = faster collection (up to 3x speed with 3 crew members)
            float speedMultiplier = Math.Min(activeLooters, 3);

            // Calculate gold to add
            float goldToAddFloat = GOLD_PER_SECOND * actualDeltaTime * speedMultiplier;
            accumulatedGold += goldToAddFloat;
            int goldToAdd = (int)accumulatedGold;
            if (goldToAdd > 0)
            {
                accumulatedGold -= goldToAdd;
                CollectedGold = Math.Min(TotalGold, CollectedGold + goldToAdd);
                LootingProgress = CompletionPercentage;

                //Debug.WriteLine($"[LOOT] Added {goldToAdd} gold (Rate: {goldToAddFloat:F2}/s, Looters: {activeLooters}, Total: {CollectedGold})");
            }
        }

        public void Reset()
        {
            CollectedGold = 0;
            State = LootInteractionState.Available;
            LootingProgress = 0f;
            LootingCrew.Clear();
            accumulatedGold = 0f;
            isLooting = false;
            lastUpdateTime = GetGameTimer() / 1000f;
        }
    }

    public class LootManager
    {
        public List<LootItem> LootItems { get; } = new List<LootItem>();
        public Dictionary<string, int> PlayerLoot { get; } = new Dictionary<string, int>();
        public int CarryLimit { get; } = 10;

        // Vault gold system
        public VaultGoldLoot VaultGold { get; private set; }
        private bool isVaultLootActive = false;

        // Events
        public event Action OnVaultLootingStarted;
        public event Action OnVaultLootingCompleted;
        public event Action<float> OnVaultLootingProgress; // Progress 0.0 to 1.0

        public int CurrentCarried
        {
            get { return PlayerLoot.Values.Sum(); }
        }

        public void DrawVaultLootUI()
        {
            if (!isVaultLootActive || VaultGold == null)
            {
                Debug.WriteLine("[LOOT] Vault not active or null");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distance = Vector3.Distance(playerPos, VaultGold.Position);

            //Debug.WriteLine($"[LOOT] Drawing vault at {VaultGold.Position}, distance: {distance}");

            DrawMarker(1, VaultGold.Position.X, VaultGold.Position.Y, VaultGold.Position.Z - 1.0f,
                      0, 0, 0, 0, 0, 0, 3.0f, 3.0f, 2.0f,
                      255, 215, 0, 200, // More opaque
                      false, true, 2, false, null, null, false);

            // draw a cylinder marker
            DrawMarker(25, VaultGold.Position.X, VaultGold.Position.Y, VaultGold.Position.Z,
                      0, 0, 0, 0, 0, 0, 2.0f, 2.0f, 1.0f,
                      255, 215, 0, 100,
                      false, true, 2, false, null, null, false);

            // testing
            if (distance < 10f)
            {
                //Screen.DisplayHelpTextThisFrame($"VAULT GOLD - Distance: {distance:F1}m");

                if (!VaultGold.IsCompleted)
                {
                    Screen.DisplayHelpTextThisFrame($"Hold ~INPUT_CONTEXT~ to collect gold ({VaultGold.CollectedGold}/{VaultGold.TotalGold})");

                    if (VaultGold.LootingCrew.Count > 0)
                    {
                        Screen.DisplayHelpTextThisFrame($"~g~{VaultGold.LootingCrew.Count} crew member(s) assisting!");
                    }
                }
            }

            // Draw debug text
            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            //AddTextComponentString($"Vault: {VaultGold.Position} | Dist: {distance:F1}");
            //DrawText(0.02f, 0.5f);

            // Draw progress bar if actively looting
            if (VaultGold.State == LootInteractionState.PlayerLooting && VaultGold.LootingProgress > 0)
            {
                DrawProgressBar(VaultGold.CompletionPercentage, $"Collecting Gold: {VaultGold.CollectedGold}/{VaultGold.TotalGold}");
            }
        }

        public void SetupVaultLoot(Vector3 vaultPosition)
        {
            VaultGold = new VaultGoldLoot(vaultPosition);
            isVaultLootActive = true;
            Debug.WriteLine($"[LOOT] Vault gold setup at {vaultPosition}");
        }

        public void UpdateVaultLooting(List<int> aliveCrew)
        {
            if (!isVaultLootActive || VaultGold == null || VaultGold.IsCompleted) return;

            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            float distanceToVault = Vector3.Distance(playerPos, VaultGold.Position);

            bool playerNearVault = distanceToVault < 3f;
            bool playerIsLooting = playerNearVault && IsControlPressed(0, 51); // E key held

            // Count how many crew members are near and could help
            int nearbyCrewCount = 0;
            VaultGold.LootingCrew.Clear();

            foreach (int crewPedId in aliveCrew)
            {
                if (!DoesEntityExist(crewPedId)) continue;

                var crewPos = GetEntityCoords(crewPedId, true);
                float crewDistance = Vector3.Distance(crewPos, VaultGold.Position);

                if (crewDistance < 5f) // Crew within helping range
                {
                    nearbyCrewCount++;
                    VaultGold.LootingCrew.Add(crewPedId);

                    // Make crew face the vault and play animation
                    TaskTurnPedToFaceCoord(crewPedId, VaultGold.Position.X, VaultGold.Position.Y, VaultGold.Position.Z, 2000);
                }
            }

            // Determine total active looters
            int activeLooters = (playerIsLooting ? 1 : 0) + nearbyCrewCount;

            if (activeLooters > 0)
            {
                if (VaultGold.State == LootInteractionState.Available)
                {
                    VaultGold.State = LootInteractionState.PlayerLooting;
                    OnVaultLootingStarted?.Invoke();
                    Debug.WriteLine("[LOOT] Vault looting started");
                }

                // Update looting progress
                VaultGold.UpdateLooting(GetFrameTime(), activeLooters);
                OnVaultLootingProgress?.Invoke(VaultGold.CompletionPercentage);

                // Check if completed
                if (VaultGold.IsCompleted && VaultGold.State != LootInteractionState.Completed)
                {
                    VaultGold.State = LootInteractionState.Completed;
                    PlayerLoot["Gold"] = VaultGold.CollectedGold;
                    OnVaultLootingCompleted?.Invoke();
                    Debug.WriteLine($"[LOOT] Vault looting completed! Collected {VaultGold.CollectedGold} gold");
                }
            }
            else
            {
                // Reset state if no one is looting
                if (VaultGold.State == LootInteractionState.PlayerLooting)
                {
                    VaultGold.State = LootInteractionState.Available;
                }
            }
        }

        private void DrawProgressBar(float progress, string text)
        {
            // Draw background
            float barWidth = 0.3f;
            float barHeight = 0.03f;
            float barX = 0.35f;
            float barY = 0.85f;

            DrawRect(barX, barY, barWidth, barHeight, 0, 0, 0, 150);

            // Draw progress
            float progressWidth = barWidth * progress;
            DrawRect(barX - (barWidth - progressWidth) / 2, barY, progressWidth, barHeight, 255, 215, 0, 200);

            // Draw text
            SetTextFont(1);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.5f);
            SetTextColour(255, 255, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextCentre(true);
            SetTextEntry("STRING");
            AddTextComponentString(text);
            DrawText(barX, barY - 0.05f);
        }

        // Legacy methods for compatibility
        public void AddLootItem(LootItem item)
        {
            LootItems.Add(item);
        }

        public bool CanCarry(int amount)
        {
            return (CurrentCarried + amount) <= CarryLimit;
        }

        public int PickUpLoot(LootItem item, int amount)
        {
            if (item.IsDepleted) return 0;
            int canTake = Math.Min(amount, item.Remaining);
            canTake = Math.Min(canTake, CarryLimit - CurrentCarried);
            if (canTake <= 0) return 0;

            int taken = item.PickUp(canTake);
            if (!PlayerLoot.ContainsKey(item.Type))
                PlayerLoot[item.Type] = 0;
            PlayerLoot[item.Type] += taken;
            return taken;
        }

        public void UnloadLoot()
        {
            PlayerLoot.Clear();
            if (VaultGold != null)
            {
                VaultGold.Reset();
            }
        }

        public bool IsVaultLootCompleted()
        {
            return VaultGold?.IsCompleted ?? false;
        }

        public void Cleanup()
        {
            isVaultLootActive = false;
            VaultGold = null;
            LootItems.Clear();
            PlayerLoot.Clear();
            Debug.WriteLine("[LOOT] Loot manager cleaned up");
        }
    }
}

