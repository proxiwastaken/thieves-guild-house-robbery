using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum GuardState
    {
        Patrolling,
        AtNode,
        Suspicious,
        Alerted,
        Dead
    }

    public class PatrolNode
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }
        public float WaitTime { get; set; } // in seconds
        public bool IsLookoutPoint { get; set; }

        public PatrolNode(Vector3 position, float heading = 0f, float waitTime = 3f, bool isLookoutPoint = false)
        {
            Position = position;
            Heading = heading;
            WaitTime = waitTime;
            IsLookoutPoint = isLookoutPoint;
        }
    }

    public class Guard
    {
        public int PedId { get; set; }
        public GuardState State { get; set; }
        public List<PatrolNode> PatrolPath { get; set; }
        public int CurrentNodeIndex { get; set; }
        public float NodeArrivalTime { get; set; }
        public Vector3 OriginalPosition { get; set; }
        public bool IsInCone { get; set; }
        public float DistanceToReticle { get; set; }

        public Guard(int pedId, Vector3 originalPosition)
        {
            PedId = pedId;
            State = GuardState.Patrolling;
            PatrolPath = new List<PatrolNode>();
            CurrentNodeIndex = 0;
            NodeArrivalTime = 0f;
            OriginalPosition = originalPosition;
            IsInCone = false;
            DistanceToReticle = float.MaxValue;
        }
    }

    public class GuardSystem
    {
        private List<Guard> guards = new List<Guard>();
        private bool allGuardsAlerted = false;
        private float coneDistance = 15f;
        private float coneAngle = 30f;

        public bool IsActive { get; private set; } = false;
        public bool AnyGuardsAlerted => guards.Any(g => g.State == GuardState.Alerted);

        // Events
        public event Action OnAllGuardsAlerted;

        public void Initialize()
        {
            IsActive = true;
            allGuardsAlerted = false;
            Debug.WriteLine("[GUARD] Guard system initialized");
        }

        public void AddGuard(Vector3 position, List<PatrolNode> patrolPath = null)
        {
            AddGuardAtPosition(position, patrolPath);
        }

        public async void AddGuardAtPosition(Vector3 position, List<PatrolNode> patrolPath = null)
        {
            uint guardModel = (uint)GetHashKey("s_m_m_security_01");

            if (await LoadModel(guardModel))
            {
                int ped = CreatePed(4, guardModel, position.X, position.Y, position.Z, 0f, true, true);

                if (DoesEntityExist(ped))
                {
                    SetEntityAsMissionEntity(ped, true, true);
                    SetPedFleeAttributes(ped, 0, false);
                    SetPedCombatAttributes(ped, 46, true); // Can use vehicles
                    SetPedCombatAttributes(ped, 5, true);   // Can fight armed peds
                    SetPedCanRagdoll(ped, false);

                    // Give weapon
                    GiveWeaponToPed(ped, (uint)GetHashKey("weapon_pistol"), 100, false, true);

                    var guard = new Guard(ped, position);

                    if (patrolPath != null && patrolPath.Count > 0)
                    {
                        guard.PatrolPath = patrolPath;
                        StartPatrol(guard);
                    }
                    else
                    {
                        // Default single node patrol (just stand and look around)
                        guard.PatrolPath.Add(new PatrolNode(position, GetEntityHeading(ped), 5f, true));
                        TaskStandStill(ped, -1);
                    }

                    guards.Add(guard);
                    Debug.WriteLine($"[GUARD] Spawned guard {ped} at {position}");
                    Screen.ShowNotification($"~r~Guard spawned at {position.X:F1}, {position.Y:F1}");
                }
                else
                {
                    Debug.WriteLine($"[GUARD] Failed to create guard at {position}");
                }
            }

            SetModelAsNoLongerNeeded(guardModel);
        }

        private void StartPatrol(Guard guard)
        {
            if (guard.PatrolPath.Count == 0) return;

            guard.CurrentNodeIndex = 0;
            guard.State = GuardState.Patrolling;

            var firstNode = guard.PatrolPath[0];
            TaskGoToCoordAnyMeans(guard.PedId, firstNode.Position.X, firstNode.Position.Y, firstNode.Position.Z, 1f, 0, false, 786603, 0f);

            Debug.WriteLine($"[GUARD] Guard {guard.PedId} starting patrol to node 0");
        }

        public void Update()
        {
            if (!IsActive) return;

            try
            {
                UpdateConeDetection();
                UpdateGuardPatrols();
                HandleGuardInput();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GUARD] Update error: {ex.Message}");
            }
        }

        private void UpdateConeDetection()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            var camRot = GetGameplayCamRot(2);
            var forwardVector = RotationToDirection(camRot);

            foreach (var guard in guards)
            {
                if (!DoesEntityExist(guard.PedId) || guard.State == GuardState.Dead) continue;

                var guardPos = GetEntityCoords(guard.PedId, true);
                var distanceToPlayer = Vector3.Distance(playerPos, guardPos);

                if (distanceToPlayer <= coneDistance)
                {
                    var toGuard = (guardPos - playerPos);
                    var toGuardNormalized = Normalize(toGuard);
                    var forwardNormalized = Normalize(forwardVector);

                    var dotProduct = Vector3.Dot(forwardNormalized, toGuardNormalized);
                    var clampedDot = Math.Max(-1f, Math.Min(1f, dotProduct));
                    var angle = Math.Acos(clampedDot) * (180.0 / Math.PI);

                    guard.IsInCone = angle <= coneAngle;
                    guard.DistanceToReticle = guard.IsInCone ? (float)angle : float.MaxValue;
                }
                else
                {
                    guard.IsInCone = false;
                    guard.DistanceToReticle = float.MaxValue;
                }
            }
        }

        private void HandleGuardInput()
        {
            // Only allow stealth takedowns when NOT in combat and guard is unaware
            if (!IsPlayerFreeAiming(PlayerId())) return;

            if (IsControlJustPressed(0, 51)) // E key for takedown
            {
                var guardsInCone = guards.Where(g => g.IsInCone &&
                                                    g.State != GuardState.Dead &&
                                                    g.State != GuardState.Alerted && // Can't takedown alerted guards
                                                    g.State != GuardState.Suspicious) // Can't takedown suspicious guards
                                         .OrderBy(g => g.DistanceToReticle).ToList();

                if (guardsInCone.Any())
                {
                    var targetGuard = guardsInCone.First();

                    // Check if close enough for stealth takedown (within 2 meters)
                    var playerPos = GetEntityCoords(PlayerPedId(), true);
                    var guardPos = GetEntityCoords(targetGuard.PedId, true);
                    var distance = Vector3.Distance(playerPos, guardPos);

                    if (distance <= 2f)
                    {
                        StealthTakedownGuard(targetGuard);
                    }
                    else
                    {
                        Screen.ShowNotification("~r~Get closer for a stealth takedown!");
                    }
                }
            }
        }

        private async void StealthTakedownGuard(Guard guard)
        {
            // Only allow stealth takedown if guard is unaware
            if (guard.State == GuardState.Alerted || guard.State == GuardState.Suspicious)
            {
                Screen.ShowNotification("~r~Guard is too alert for a stealth takedown!");
                return;
            }

            guard.State = GuardState.Dead;

            // Clear tasks and make them fall down
            ClearPedTasks(guard.PedId);
            SetPedToRagdoll(guard.PedId, 3000, 3000, 0, true, true, false);

            // Wait a moment then apply death animation
            await BaseScript.Delay(500);

            SetPedCanRagdoll(guard.PedId, false);
            SetEntityHealth(guard.PedId, 0);

            Screen.ShowNotification("~g~Silent takedown successful!");
            Debug.WriteLine($"[GUARD] Guard {guard.PedId} silently taken down");
        }

        private void UpdateGuardPatrols()
        {
            float currentTime = GetGameTimer() / 1000f;

            foreach (var guard in guards)
            {
                if (!DoesEntityExist(guard.PedId) || guard.State == GuardState.Dead || guard.State == GuardState.Alerted)
                    continue;

                if (guard.PatrolPath.Count == 0) continue;

                var guardPos = GetEntityCoords(guard.PedId, true);
                var currentNode = guard.PatrolPath[guard.CurrentNodeIndex];
                var distanceToNode = Vector3.Distance(guardPos, currentNode.Position);

                switch (guard.State)
                {
                    case GuardState.Patrolling:
                        // Check if guard reached current node
                        if (distanceToNode < 2f)
                        {
                            guard.State = GuardState.AtNode;
                            guard.NodeArrivalTime = currentTime;

                            // Make guard look in the specified direction
                            SetEntityHeading(guard.PedId, currentNode.Heading);
                            TaskStandStill(guard.PedId, (int)(currentNode.WaitTime * 1000));

                            Debug.WriteLine($"[GUARD] Guard {guard.PedId} reached node {guard.CurrentNodeIndex}");
                        }
                        break;

                    case GuardState.AtNode:
                        // Check if wait time is over
                        if (currentTime - guard.NodeArrivalTime >= currentNode.WaitTime)
                        {
                            // Move to next node
                            guard.CurrentNodeIndex = (guard.CurrentNodeIndex + 1) % guard.PatrolPath.Count;
                            var nextNode = guard.PatrolPath[guard.CurrentNodeIndex];

                            guard.State = GuardState.Patrolling;
                            TaskGoToCoordAnyMeans(guard.PedId, nextNode.Position.X, nextNode.Position.Y, nextNode.Position.Z, 1f, 0, false, 786603, 0f);

                            Debug.WriteLine($"[GUARD] Guard {guard.PedId} moving to node {guard.CurrentNodeIndex}");
                        }
                        break;
                }

                // Check for player detection
                CheckPlayerDetection(guard);
            }
        }

        private void CheckPlayerDetection(Guard guard)
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);
            var guardPos = GetEntityCoords(guard.PedId, true);

            float detectionDistance = 15f;
            float distance = Vector3.Distance(playerPos, guardPos);

            if (distance <= detectionDistance)
            {
                // Check if guard can see player (line of sight)
                bool canSeePlayer = HasEntityClearLosToEntity(guard.PedId, playerPed, 17);

                if (canSeePlayer)
                {
                    // Check if player has a weapon drawn
                    uint currentWeapon = 0;
                    GetCurrentPedWeapon(playerPed, ref currentWeapon, true);
                    bool hasWeaponDrawn = currentWeapon != (uint)GetHashKey("weapon_unarmed");

                    // Check for immediate hostile actions
                    bool isAimingWeapon = IsPlayerFreeAiming(PlayerId()) && hasWeaponDrawn;
                    bool recentlyFired = IsPedShooting(playerPed);
                    bool inCombat = IsPedInCombat(playerPed, 0);

                    // Immediate hostility triggers
                    if (isAimingWeapon || recentlyFired || inCombat)
                    {
                        AlertGuard(guard);
                        return;
                    }

                    // Warning conditions (close proximity with weapon or very close without)
                    if ((distance < 5f && hasWeaponDrawn) || distance < 2f)
                    {
                        if (guard.State == GuardState.Patrolling || guard.State == GuardState.AtNode)
                        {
                            // First warning
                            guard.State = GuardState.Suspicious;
                            guard.NodeArrivalTime = GetGameTimer() / 1000f; // Track warning time

                            // Make guard face player and give warning
                            ClearPedTasks(guard.PedId);
                            TaskTurnPedToFaceEntity(guard.PedId, playerPed, 3000);

                            Screen.ShowNotification("~y~Guard: Hey! Keep your distance!");
                            Debug.WriteLine($"[GUARD] Guard {guard.PedId} suspicious of player");
                        }
                        else if (guard.State == GuardState.Suspicious)
                        {
                            // If player ignores warning for 3 seconds, go hostile
                            float currentTime = GetGameTimer() / 1000f;
                            if (currentTime - guard.NodeArrivalTime > 3f)
                            {
                                AlertGuard(guard);
                            }
                        }
                    }
                    else if (distance > 8f && guard.State == GuardState.Suspicious)
                    {
                        // Player backed off, return to patrol
                        guard.State = GuardState.Patrolling;
                        StartPatrol(guard);
                        Screen.ShowNotification("~g~Guard relaxes");
                        Debug.WriteLine($"[GUARD] Guard {guard.PedId} no longer suspicious");
                    }
                }
            }
        }



        private void AlertGuard(Guard guard)
        {
            if (guard.State == GuardState.Alerted) return;

            guard.State = GuardState.Alerted;

            var playerPed = PlayerPedId();

            // Clear current tasks
            ClearPedTasks(guard.PedId);

            // Make guard aggressive
            SetPedCombatAttributes(guard.PedId, 1424, true); // Always fight
            SetPedAsEnemy(guard.PedId, true);
            TaskCombatPed(guard.PedId, playerPed, 0, 16);

            Screen.ShowNotification("~r~GUARD ALERTED! All guards now hostile!");
            Debug.WriteLine($"[GUARD] Guard {guard.PedId} alerted!");

            // Alert all other guards
            AlertAllGuards();
        }

        private void AlertAllGuards()
        {
            if (allGuardsAlerted) return;

            allGuardsAlerted = true;
            var playerPed = PlayerPedId();

            foreach (var guard in guards)
            {
                if (!DoesEntityExist(guard.PedId) || guard.State == GuardState.Dead) continue;

                guard.State = GuardState.Alerted;
                ClearPedTasks(guard.PedId);

                SetPedCombatAttributes(guard.PedId, 1424, true);
                SetPedAsEnemy(guard.PedId, true);
                TaskCombatPed(guard.PedId, playerPed, 0, 16);
            }

            OnAllGuardsAlerted?.Invoke();
            Debug.WriteLine("[GUARD] All guards alerted!");
        }

        public void DrawDebugInfo()
        {
            if (!IsActive) return;

            // Draw guards in cone
            foreach (var guard in guards.Where(g => g.IsInCone && g.State != GuardState.Dead))
            {
                if (!DoesEntityExist(guard.PedId)) continue;

                var guardPos = GetEntityCoords(guard.PedId, true);
                DrawMarker(0, guardPos.X, guardPos.Y, guardPos.Z + 2f, 0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.5f, 255, 0, 0, 100, false, true, 2, false, null, null, false);
            }

            // Draw patrol paths
            foreach (var guard in guards)
            {
                if (guard.PatrolPath.Count <= 1) continue;

                for (int i = 0; i < guard.PatrolPath.Count; i++)
                {
                    var node = guard.PatrolPath[i];
                    var nextNode = guard.PatrolPath[(i + 1) % guard.PatrolPath.Count];

                    // Draw node
                    DrawMarker(1, node.Position.X, node.Position.Y, node.Position.Z - 1f, 0, 0, 0, 0, 0, 0, 1f, 1f, 0.5f, 255, 255, 0, 100, false, true, 2, false, null, null, false);

                    // Draw line to next node
                    DrawLine(node.Position.X, node.Position.Y, node.Position.Z,
                            nextNode.Position.X, nextNode.Position.Y, nextNode.Position.Z,
                            255, 255, 0, 255);
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

        public void Cleanup()
        {
            IsActive = false;

            foreach (var guard in guards)
            {
                if (DoesEntityExist(guard.PedId))
                {
                    int pedId = guard.PedId;
                    DeletePed(ref pedId);
                }
            }

            guards.Clear();
            allGuardsAlerted = false;
            Debug.WriteLine("[GUARD] Guard system cleaned up");
        }
    }
}
