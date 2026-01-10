using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class EMPGrenade
    {
        private int grenadeCount = 0;
        private CameraManager cameraManager;
        private readonly List<ThrownEMPGrenade> empGrenades = new List<ThrownEMPGrenade>();
        private readonly List<EMPRingEffect> activeRings = new List<EMPRingEffect>();


        // Grenade properties
        private readonly float empRadius = 15f;
        private readonly float empDuration = 10f;

        // Native grenade weapon hash
        private readonly uint grenadeWeapon = (uint)GetHashKey("weapon_grenade");
        private bool isThrowingEMP = false;

        public int GrenadeCount => grenadeCount;
        public bool HasGrenades => grenadeCount > 0;

        public EMPGrenade(CameraManager cameraManager)
        {
            this.cameraManager = cameraManager;
        }

        public void GiveGrenades(int count)
        {
            grenadeCount += count;
            Screen.ShowNotification($"~b~Received {count} EMP Grenade{(count > 1 ? "s" : "")}~w~!");
            Debug.WriteLine($"[EMP] Player given {count} EMP grenades. Total: {grenadeCount}");
        }

        public void Update()
        {
            HandleGrenadeInput();
            UpdateThrownGrenades();
            UpdateEMPRings();
        }

        private void HandleGrenadeInput()
        {
            if (!HasGrenades) return;

            // Check for G key press to throw EMP grenade
            if (IsControlJustPressed(0, 58)) // G key
            {
                ThrowEMPGrenade();
            }
        }

        private void UpdateEMPRings()
        {
            for (int i = activeRings.Count - 1; i >= 0; i--)
            {
                var ring = activeRings[i];
                ring.Update();

                if (ring.IsFinished)
                {
                    activeRings.RemoveAt(i);
                }
                else
                {
                    ring.Draw();
                }
            }
        }

        private void ThrowEMPGrenade()
        {
            var playerPed = PlayerPedId();
            var playerPos = GetEntityCoords(playerPed, true);

            // Get camera direction
            var cameraRot = GetGameplayCamRot(2);
            var cameraCoord = GetGameplayCamCoord();

            // Calculate the forward direction from camera rotation
            float pitch = cameraRot.X * (float)(Math.PI / 180.0); // Convert to radians
            float yaw = cameraRot.Z * (float)(Math.PI / 180.0);

            // Calculate throw direction vector based on camera orientation
            Vector3 throwDirection = new Vector3(
                (float)(-Math.Sin(yaw) * Math.Cos(pitch)),  // X component
                (float)(Math.Cos(yaw) * Math.Cos(pitch)),   // Y component  
                (float)Math.Sin(pitch)                      // Z component (up/down aim)
            );

            // Normalize
            float length = (float)Math.Sqrt(throwDirection.X * throwDirection.X +
                                          throwDirection.Y * throwDirection.Y +
                                          throwDirection.Z * throwDirection.Z);
            throwDirection = throwDirection / length;

            // Add some upward trajectory for arc
            throwDirection.Z += 0.3f;

            // Create EMP grenade
            var empGrenade = new ThrownEMPGrenade(
                playerPos + new Vector3(0, 0, 1f), // Start at player chest height
                throwDirection * 20f,              // Throw in camera direction
                1.0f                               // Fuse time
            );

            empGrenades.Add(empGrenade);
            grenadeCount--;

            // Play throw effects
            PlayGrenadeThrowEffects(playerPed);

            Screen.ShowNotification($"~y~EMP Grenade thrown! ({grenadeCount} remaining)");
            Debug.WriteLine($"[EMP] EMP Grenade thrown toward camera direction. Remaining: {grenadeCount}");
        }

        private async void PlayGrenadeThrowEffects(int playerPed)
        {
            string animDict = "veh@driveby@first_person@passenger_rear_right_handed@throw";
            string animName = "throw_225l"; 

            // animation dictionary
            RequestAnimDict(animDict);

            while (!HasAnimDictLoaded(animDict))
            {
                await BaseScript.Delay(0); 
            }

            TaskPlayAnim(playerPed, animDict, animName, 2.0f, 2.0f, 2000, 18, 1.0f, false, false, false);

            Debug.WriteLine("[EMP] Playing throw animation successfully");

            // Play throw sound
            PlaySoundFrontend(-1, "THROW_GRENADE", "HUD_AWARDS", true);
        }

        private void UpdateThrownGrenades()
        {
            for (int i = empGrenades.Count - 1; i >= 0; i--)
            {
                var grenade = empGrenades[i];
                grenade.Update();

                if (grenade.HasExploded)
                {
                    TriggerEMPEffect(grenade.ExplodePosition);
                    empGrenades.RemoveAt(i);
                }
            }
        }

        public bool IsEMPExplosion(Vector3 explosionPos)
        {
            // Check if any of our EMP grenades are near this explosion
            foreach (var grenade in empGrenades)
            {
                float distance = GetDistanceBetweenCoords(
                    explosionPos.X, explosionPos.Y, explosionPos.Z,
                    grenade.Position.X, grenade.Position.Y, grenade.Position.Z, true);

                if (distance < 5f) // Within 5 meters
                {
                    Debug.WriteLine($"[EMP] Identified EMP grenade explosion at {explosionPos}");
                    return true;
                }
            }
            return false;
        }

        public void OnExplosion(Vector3 position, int explosionType)
        {
            // Check if this explosion is from one of our EMP grenades
            if (IsEMPExplosion(position))
            {
                Debug.WriteLine($"[EMP] Converting explosion at {position} to EMP");
                TriggerEMPEffect(position);
            }
        }

        private void TriggerEMPEffect(Vector3 position)
        {
            // Create EMP explosion effect
            AddExplosion(position.X, position.Y, position.Z, 83, 0.0f, true, false, 1.0f); // EMPLAUNCHER_EMP

            // Disable cameras in radius
            cameraManager.DisableCamerasInRadius(position, empRadius, empDuration);

            // Create visual effects
            CreateEMPVisualEffects(position);

            Screen.ShowNotification("~b~EMP Detonated!~w~ Cameras disabled in area.");

            Debug.WriteLine($"[EMP] EMP explosion at {position}, radius: {empRadius}m, duration: {empDuration}s");
        }

        private async void CreateEMPVisualEffects(Vector3 position)
        {
            // explosion/particles
            AddExplosion(position.X, position.Y, position.Z, 83, 1.0f, true, false, 2.0f);
            await BaseScript.Delay(100);
            AddExplosion(position.X, position.Y, position.Z + 2f, 22, 0.5f, false, true, 1.0f); // FLARE explosion for light

            // 2. Create electric spark ring around the explosion
            for (int ring = 0; ring < 2; ring++)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * 45f; // 8 directions
                    float radians = angle * (float)(Math.PI / 180.0);
                    float ringRadius = 2f + (ring * 2f);

                    Vector3 sparkPos = position + new Vector3(
                        (float)Math.Cos(radians) * ringRadius,
                        (float)Math.Sin(radians) * ringRadius,
                        0.5f + (ring * 0.5f)
                    );

                    // Use EMP sparks
                    AddExplosion(sparkPos.X, sparkPos.Y, sparkPos.Z, 83, 0.2f, false, true, 0.3f);
                    await BaseScript.Delay(25);
                }
            }

            // expanding marker rings
            CreateExpandingEnergyRings(position);

            
            //await BaseScript.Delay(200);
            //for (int i = 0; i < 12; i++)
            //{
            //    float randomAngle = (float)(new Random().NextDouble() * 360);
            //    float randomDistance = (float)(new Random().NextDouble() * empRadius);
            //    float radians = randomAngle * (float)(Math.PI / 180.0);
            //
            //    Vector3 dischargePos = position + new Vector3(
            //        (float)Math.Cos(radians) * randomDistance,
            //        (float)Math.Sin(radians) * randomDistance,
            //        (float)(new Random().NextDouble() * 3f)
            //    );
            //
            //    // Small electrical discharge explosions
            //    AddExplosion(dischargePos.X, dischargePos.Y, dischargePos.Z, 18, 0.1f, false, true, 0.2f);
            //    await BaseScript.Delay(30);
            //}

            // 5. Enhanced screen effects
            var playerPos = GetEntityCoords(PlayerPedId(), true);
            float distance = GetDistanceBetweenCoords(position.X, position.Y, position.Z,
                                                    playerPos.X, playerPos.Y, playerPos.Z, true);

            if (distance < empRadius)
            {
                // i dont think this does anything but i tried to make a flashbang effect
                for (int flash = 0; flash < 3; flash++)
                {
                    SetFlash(0, 0, 100, 300, 100);
                    await BaseScript.Delay(150);
                }

                // screen shake
                ShakeGameplayCam("SMALL_EXPLOSION_SHAKE", 0.5f);
            }

            // sounds
            PlaySoundFromCoord(-1, "Power_Down", position.X, position.Y, position.Z,
                              "DLC_HEIST_HACKING_SNAKE_SOUNDS", false, 50, false);

            await BaseScript.Delay(500);
            PlaySoundFromCoord(-1, "Electricity_Stop", position.X, position.Y, position.Z,
                              "DLC_HEIST_HACKING_SNAKE_SOUNDS", false, 30, false);

            
            //await BaseScript.Delay(1000);
            //for (int i = 0; i < 5; i++)
            //{
            //    float angle = i * 72f;
            //    float radians = angle * (float)(Math.PI / 180.0);
            //
            //    Vector3 sparkPos = position + new Vector3(
            //        (float)Math.Cos(radians) * 1.5f,
            //        (float)Math.Sin(radians) * 1.5f,
            //        0.2f
            //    );
            //
            //    AddExplosion(sparkPos.X, sparkPos.Y, sparkPos.Z, 18, 0.05f, false, true, 0.1f);
            //    await BaseScript.Delay(100);
            //}
        }

        private void CreateExpandingEnergyRings(Vector3 center)
        {
            // Create 3 rings that will expand over time
            for (int i = 0; i < 3; i++)
            {
                var ring = new EMPRingEffect(center, i * 0.5f); // Stagger start times
                activeRings.Add(ring);
            }
        }

        public void DrawUI()
        {
            if (!HasGrenades) return;

            // Draw grenade count on screen
            float x = 0.92f;
            float y = 0.15f;

            SetTextFont(0);
            SetTextProportional(true);
            SetTextScale(0.0f, 0.4f);
            SetTextColour(0, 150, 255, 255);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString($"EMP Grenades: {grenadeCount}\nPress G to throw");
            DrawText(x, y);
        }
    }

    // Custom EMP grenade with physics
    public class ThrownEMPGrenade
    {
        public Vector3 Position { get; private set; }
        private Vector3 velocity;
        private float fuseTime;
        private float gravity = -9.8f;
        private bool hasExploded = false;
        private float bounceReduction = 0.6f;

        public Vector3 ExplodePosition { get; private set; }
        public bool HasExploded => hasExploded;

        public ThrownEMPGrenade(Vector3 startPos, Vector3 initialVelocity, float fuse)
        {
            Position = startPos;
            velocity = initialVelocity;
            fuseTime = fuse;
        }

        public void Update()
        {
            if (hasExploded) return;

            float deltaTime = GetFrameTime();
            fuseTime -= deltaTime;

            // Apply physics
            velocity.Z += gravity * deltaTime;
            Position += velocity * deltaTime;

            // Check for ground collision - EXPLODE IMMEDIATELY
            float groundZ = 0f;
            GetGroundZFor_3dCoord(Position.X, Position.Y, Position.Z + 2f, ref groundZ, false);

            if (Position.Z <= groundZ + 0.3f)
            {
                // IMPACT!
                Position = new Vector3(Position.X, Position.Y, groundZ + 0.1f);
                Explode();
                return;
            }

            //  (backup explosion)
            if (fuseTime <= 0f)
            {
                Explode();
                return;
            }

            // Draw grenade visual
            DrawMarker(28, Position.X, Position.Y, Position.Z, 0, 0, 0, 0, 0, 0,
                      0.3f, 0.3f, 0.3f, 0, 150, 255, 200, false, false, 2, false, null, null, false);
        }

        private void Explode()
        {
            hasExploded = true;
            ExplodePosition = Position;
        }
    }

    public class EMPRingEffect
    {
        private Vector3 center;
        private float currentRadius;
        private float maxRadius;
        private float startDelay;
        private float age;
        private bool isActive;

        public bool IsFinished => age > 5f; // 5 seconds

        public EMPRingEffect(Vector3 centerPos, float delay)
        {
            center = centerPos;
            currentRadius = 0f;
            maxRadius = 15f;
            startDelay = delay;
            age = 0f;
            isActive = false;
        }

        public void Update()
        {
            age += GetFrameTime();

            if (age >= startDelay && !isActive)
            {
                isActive = true;
            }

            if (isActive)
            {
                // Expand the ring
                currentRadius += 8f * GetFrameTime(); // expand at 8 units per second

                if (currentRadius > maxRadius)
                {
                    currentRadius = maxRadius;
                }
            }
        }

        public void Draw()
        {
            if (!isActive || currentRadius <= 0) return;

            // alpha based on age (fades over time)
            float alpha = Math.Max(0f, 1f - ((age - startDelay) / 4f));
            int alphaInt = (int)(alpha * 180);

            if (alphaInt <= 0) return;

            // Draw ring using markers at points around circle
            int points = Math.Min(24, (int)(currentRadius * 2));
            float angleStep = 360f / points;

            for (int i = 0; i < points; i++)
            {
                float angle = i * angleStep;
                float radians = angle * (float)(Math.PI / 180.0);

                Vector3 ringPos = center + new Vector3(
                    (float)Math.Cos(radians) * currentRadius,
                    (float)Math.Sin(radians) * currentRadius,
                    0.3f
                );

                // Draw the marker for this frame
                DrawMarker(28, ringPos.X, ringPos.Y, ringPos.Z, 0, 0, 0, 0, 0, 0,
                          0.8f, 0.8f, 0.5f, 0, 150, 255, alphaInt, false, false, 2, false, null, null, false);
            }
        }
    }
}
