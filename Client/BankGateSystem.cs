using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class BankGateSystem
    {
        // Pacific Standard Bank door configurations 
        private readonly List<DoorConfig> pacificStandardDoors = new List<DoorConfig>
        {
            // Back area doors
            new DoorConfig { Id = 2, Name = "Back Right Door", Hash = 110411286, Position = new Vector3(260.6432f, 203.2052f, 106.4049f), Category = "back" },
            new DoorConfig { Id = 3, Name = "Back Left Door", Hash = 110411286, Position = new Vector3(258.2022f, 204.1005f, 106.4049f), Category = "back" },
            new DoorConfig { Id = 6, Name = "Back To Hall Right", Hash = 110411286, Position = new Vector3(259.9831f, 215.2468f, 106.4049f), Category = "hall" },
            new DoorConfig { Id = 7, Name = "Back To Hall Left", Hash = 110411286, Position = new Vector3(259.0879f, 212.8062f, 106.4049f), Category = "hall" },
            
            // Interior doors
            new DoorConfig { Id = 4, Name = "Door To Upstair", Hash = 1956494919, Position = new Vector3(237.7704f, 227.87f, 106.426f), Category = "interior" },
            
            // Main entrance doors
            new DoorConfig { Id = 0, Name = "Main Right Door", Hash = 110411286, Position = new Vector3(232.6054f, 214.1584f, 106.4049f), Category = "entrance" },
            new DoorConfig { Id = 1, Name = "Main Left Door", Hash = 110411286, Position = new Vector3(231.5123f, 216.5177f, 106.4049f), Category = "entrance" }
        };

        // Working door configurations for vault access
        private List<WorkingDoor> vaultAccessDoors = new List<WorkingDoor>();

        // Gate states
        private bool hasInitialized = false;

        // Events
        public event Action OnGatesUnlocked;
        public event Action OnGatesLocked;

        private class DoorConfig
        {
            public int Id { get; set; }
            public uint Hash { get; set; }
            public Vector3 Position { get; set; }
            public string Name { get; set; }
            public string Category { get; set; }
        }

        private class WorkingDoor
        {
            public int DoorHash { get; set; }  
            public uint ModelHash { get; set; }
            public Vector3 Position { get; set; }
            public string Name { get; set; }
            public bool IsLocked { get; set; }
            public bool PhysicsLoaded { get; set; }
        }

        public bool AreGatesLocked => vaultAccessDoors.Any() && vaultAccessDoors.All(d => d.IsLocked);
        public bool IsGate1Locked => vaultAccessDoors.FirstOrDefault()?.IsLocked ?? false;
        public bool IsGate2Locked => vaultAccessDoors.Skip(1).FirstOrDefault()?.IsLocked ?? false;

        public async void Initialize()
        {
            if (hasInitialized) return;

            Debug.WriteLine("[BANK_GATES] Initializing with proper DoorSystem functions...");

            // Scan for existing doors using DoorSystemFindExistingDoor
            await ScanForExistingDoors();

            // Wait for physics to load and lock doors
            await WaitForPhysicsAndLockDoors();

            hasInitialized = true;
            Debug.WriteLine($"[BANK_GATES] Initialization complete - Found {vaultAccessDoors.Count} working doors");
        }

        private async Task ScanForExistingDoors()
        {
            Debug.WriteLine("[BANK_GATES] Scanning for existing doors using DoorSystemFindExistingDoor...");
            vaultAccessDoors.Clear();

            // Focus on back/hall doors first for vault access
            var priorityDoors = pacificStandardDoors.Where(d =>
                d.Category == "back" || d.Category == "hall").ToList();

            foreach (var doorConfig in priorityDoors)
            {
                var workingDoor = await FindExistingDoor(doorConfig);
                if (workingDoor != null)
                {
                    vaultAccessDoors.Add(workingDoor);
                    Debug.WriteLine($"[BANK_GATES] Found priority door: {workingDoor.Name} (Hash: {workingDoor.DoorHash})");
                }
            }

            // If no priority doors found, try all doors
            if (vaultAccessDoors.Count == 0)
            {
                Debug.WriteLine("[BANK_GATES] No priority doors found, scanning all doors...");

                foreach (var doorConfig in pacificStandardDoors)
                {
                    var workingDoor = await FindExistingDoor(doorConfig);
                    if (workingDoor != null)
                    {
                        vaultAccessDoors.Add(workingDoor);
                        Debug.WriteLine($"[BANK_GATES] Found fallback door: {workingDoor.Name}");

                        if (vaultAccessDoors.Count >= 2) break; 
                    }
                }
            }

            Debug.WriteLine($"[BANK_GATES] Door scan complete - {vaultAccessDoors.Count} existing doors found");
        }

        private async Task<WorkingDoor> FindExistingDoor(DoorConfig config)
        {
            try
            {
                Debug.WriteLine($"[BANK_GATES] Testing door: {config.Name} at {config.Position}");

                int doorOutPointer = 0; 
                bool doorExists = DoorSystemFindExistingDoor(
                    config.Position.X,
                    config.Position.Y,
                    config.Position.Z,
                    (int)config.Hash,   
                    ref doorOutPointer  
                );

                if (doorExists && doorOutPointer != 0)
                {
                    Debug.WriteLine($"[BANK_GATES] Found existing door: {config.Name} with pointer {doorOutPointer}");

                    return new WorkingDoor
                    {
                        DoorHash = doorOutPointer,  
                        ModelHash = config.Hash,
                        Position = config.Position,
                        Name = config.Name,
                        IsLocked = false,
                        PhysicsLoaded = false
                    };
                }
                else
                {
                    Debug.WriteLine($"[BANK_GATES] No existing door found for {config.Name}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK_GATES] Error finding door {config.Name}: {ex.Message}");
                return null;
            }
        }

        private async Task WaitForPhysicsAndLockDoors()
        {
            Debug.WriteLine("[BANK_GATES] Waiting for physics to load and locking doors...");

            if (vaultAccessDoors.Count == 0)
            {
                Debug.WriteLine("[BANK_GATES] No doors to lock - using fallback state");
                OnGatesLocked?.Invoke();
                //Screen.ShowNotification("~r~Vault access restricted (no doors found)!");
                return;
            }

            int lockedCount = 0;
            foreach (var door in vaultAccessDoors)
            {
                if (await WaitForPhysicsAndLockDoor(door))
                {
                    door.IsLocked = true;
                    lockedCount++;
                    Debug.WriteLine($"[BANK_GATES] Successfully locked {door.Name}");
                }
                else
                {
                    Debug.WriteLine($"[BANK_GATES] Failed to lock {door.Name}");
                }
            }

            if (lockedCount > 0)
            {
                OnGatesLocked?.Invoke();
                //Screen.ShowNotification($"~r~{lockedCount} vault door{(lockedCount > 1 ? "s" : "")} secured!");
                Debug.WriteLine($"[BANK_GATES] Successfully locked {lockedCount}/{vaultAccessDoors.Count} doors");
            }
            else
            {
                // Fallback - mark as locked even if control failed
                foreach (var door in vaultAccessDoors)
                {
                    door.IsLocked = true;
                }
                OnGatesLocked?.Invoke();
                //Screen.ShowNotification("~r~Vault access restricted (fallback mode)!");
                Debug.WriteLine("[BANK_GATES] Used fallback locking - doors marked as locked");
            }
        }

        private async Task<bool> WaitForPhysicsAndLockDoor(WorkingDoor door)
        {
            try
            {
                Debug.WriteLine($"[BANK_GATES] Checking physics for door {door.Name} (Hash: {door.DoorHash})");

                // Wait for physics to load 
                int maxAttempts = 50; // 5 seconds
                int attempts = 0;

                while (attempts < maxAttempts)
                {
                    bool physicsLoaded = DoorSystemGetIsPhysicsLoaded(door.DoorHash);

                    if (physicsLoaded)
                    {
                        door.PhysicsLoaded = true;
                        Debug.WriteLine($"[BANK_GATES] Physics loaded for {door.Name} after {attempts * 100}ms");
                        break;
                    }

                    attempts++;
                    await BaseScript.Delay(100);
                }

                if (!door.PhysicsLoaded)
                {
                    Debug.WriteLine($"[BANK_GATES] Physics loading timeout for {door.Name}");
                    return false;
                }

                //
                DoorSystemSetDoorState((uint)door.DoorHash, 1, true, true); // 1 = LOCKED,
                await BaseScript.Delay(200);

                // Verify the lock state
                int currentState = DoorSystemGetDoorState((uint)door.DoorHash);
                bool isLocked = (currentState == 1);

                Debug.WriteLine($"[BANK_GATES] Lock attempt for {door.Name}: State = {currentState}, Locked = {isLocked}");

                return isLocked;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK_GATES] Error locking door {door.Name}: {ex.Message}");
                return false;
            }
        }

        public async void UnlockGate1()
        {
            Debug.WriteLine("[BANK_GATES] Unlocking Gate 1...");

            if (vaultAccessDoors.Count > 0)
            {
                var door = vaultAccessDoors[0];
                if (await UnlockDoor(door))
                {
                    door.IsLocked = false;
                    //Screen.ShowNotification($"~g~{door.Name} unlocked!");
                    CheckAllDoorsUnlocked();
                }
                else
                {
                    // Fallback
                    door.IsLocked = false;
                    //Screen.ShowNotification($"~g~{door.Name} unlocked! (fallback)");
                    CheckAllDoorsUnlocked();
                }
            }
            else
            {
                Debug.WriteLine("[BANK_GATES] No vault doors available for Gate 1");
                //Screen.ShowNotification("~g~First vault access unlocked! (no doors)");
                CheckAllDoorsUnlocked();
            }
        }

        public async void UnlockGate2()
        {
            Debug.WriteLine("[BANK_GATES] Unlocking Gate 2...");

            if (vaultAccessDoors.Count > 1)
            {
                var door = vaultAccessDoors[1];
                if (await UnlockDoor(door))
                {
                    door.IsLocked = false;
                    //Screen.ShowNotification($"~g~{door.Name} unlocked!");
                    CheckAllDoorsUnlocked();
                }
                else
                {
                    // Fallback
                    door.IsLocked = false;
                    //Screen.ShowNotification($"~g~{door.Name} unlocked! (fallback)");
                    CheckAllDoorsUnlocked();
                }
            }
            else if (vaultAccessDoors.Count == 1)
            {
                Debug.WriteLine("[BANK_GATES] Only one door available - marking Gate 2 as unlocked");
                //Screen.ShowNotification("~g~Second vault access unlocked! (single door)");
                CheckAllDoorsUnlocked();
            }
            else
            {
                Debug.WriteLine("[BANK_GATES] No vault doors available for Gate 2");
                //Screen.ShowNotification("~g~Second vault access unlocked! (no doors)");
                CheckAllDoorsUnlocked();
            }
        }

        private async Task<bool> UnlockDoor(WorkingDoor door)
        {
            try
            {
                Debug.WriteLine($"[BANK_GATES] Unlocking door {door.Name} (Hash: {door.DoorHash})");

                if (!door.PhysicsLoaded)
                {
                    Debug.WriteLine($"[BANK_GATES] Physics not loaded for {door.Name}, cannot unlock");
                    return false;
                }

                // Use force unlock for immediate effect
                DoorSystemSetDoorState((uint)door.DoorHash, 3, true, true); // 3 = DOORSTATE_FORCE_UNLOCKED_THIS_FRAME
                await BaseScript.Delay(100);

                // Then set to normal unlocked
                DoorSystemSetDoorState((uint)door.DoorHash, 0, true, true); // 0 = UNLOCKED
                await BaseScript.Delay(100);

                // Verify the unlock state
                int currentState = DoorSystemGetDoorState((uint)door.DoorHash);
                bool isUnlocked = (currentState == 0);

                Debug.WriteLine($"[BANK_GATES] Unlock attempt for {door.Name}: State = {currentState}, Unlocked = {isUnlocked}");

                return isUnlocked;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BANK_GATES] Error unlocking door {door.Name}: {ex.Message}");
                return false;
            }
        }

        private void CheckAllDoorsUnlocked()
        {
            if (!AreGatesLocked)
            {
                OnGatesUnlocked?.Invoke();
                //Screen.ShowNotification("~g~All vault access doors unlocked! Access granted!");
                Debug.WriteLine("[BANK_GATES] All vault access doors unlocked - access granted");
            }
        }

        public async void UnlockGates()
        {
            Debug.WriteLine("[BANK_GATES] Unlocking all vault access doors...");

            foreach (var door in vaultAccessDoors)
            {
                if (await UnlockDoor(door))
                {
                    door.IsLocked = false;
                }
                else
                {
                    // Fallback
                    door.IsLocked = false;
                }
                await BaseScript.Delay(100);
            }

            CheckAllDoorsUnlocked();
        }

        public void ForceLockGates()
        {
            Debug.WriteLine("[BANK_GATES] Force locking all vault access doors...");

            foreach (var door in vaultAccessDoors)
            {
                door.IsLocked = true;

                // Try to force lock if physics are loaded
                if (door.PhysicsLoaded)
                {
                    try
                    {
                        DoorSystemSetDoorState((uint)door.DoorHash, 4, true, true); // 4 = DOORSTATE_FORCE_LOCKED_THIS_FRAME
                        Debug.WriteLine($"[BANK_GATES] Force locked {door.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BANK_GATES] Error force locking {door.Name}: {ex.Message}");
                    }
                }
            }

            OnGatesLocked?.Invoke();
            Debug.WriteLine("[BANK_GATES] Force locked all vault access doors");
        }

        public void Update()
        {
            if (!hasInitialized) return;

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            foreach (var door in vaultAccessDoors)
            {
                float distance = Vector3.Distance(playerPos, door.Position);

                if (distance < 6f)
                {
                    var color = door.IsLocked ? new[] { 255, 0, 0 } : new[] { 0, 255, 0 };
                    var status = door.IsLocked ? "LOCKED" : "UNLOCKED";
                    var physicsStatus = door.PhysicsLoaded ? "READY" : "LOADING";

                    DrawMarker(0, door.Position.X, door.Position.Y, door.Position.Z + 2f, 0, 0, 0, 0, 0, 0,
                              0.8f, 0.8f, 0.8f, color[0], color[1], color[2], 150, false, true, 2, false, null, null, false);

                    Screen.DisplayHelpTextThisFrame($"~{(door.IsLocked ? "r" : "g")}~{door.Name} {status} - Physics: {physicsStatus}");
                }
            }
        }

        public void Cleanup()
        {
            UnlockGates();
            hasInitialized = false;
            vaultAccessDoors.Clear();
            Debug.WriteLine("[BANK_GATES] Bank gate system cleaned up");
        }

        // Debug method to list all doors
        public void DebugListAllDoors()
        {
            Debug.WriteLine("=== PACIFIC STANDARD BANK DOORS ===");
            foreach (var door in pacificStandardDoors)
            {
                Debug.WriteLine($"ID: {door.Id}, Name: {door.Name}, Hash: {door.Hash}, Category: {door.Category}");
                Debug.WriteLine($"Position: {door.Position}");
                Debug.WriteLine("---");
            }
            Debug.WriteLine("=== WORKING DOORS ===");
            foreach (var door in vaultAccessDoors)
            {
                Debug.WriteLine($"Working: {door.Name}, DoorHash: {door.DoorHash}, Locked: {door.IsLocked}, Physics: {door.PhysicsLoaded}");
            }
            Debug.WriteLine("================");
        }
    }
}

