using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class Lockpicking
    {
        private bool isActive = false;
        private float lockpickAngle = 90f;
        private float lockRotation = 0f;
        private float sweetSpot;
        private float sweetSpotRange = 15f;
        private int lockpicks = 3;
        private bool isApplyingTension = false;
        private float lockpickHealth = 100f;
        private float maxLockRotation = 90f;
        private Random random = new Random();

        public event Action<bool> OnLockpickingComplete;
        public bool IsActive => isActive;

        public void HandleNuiInput(string key)
        {
            if (!isActive) return;

            // Movement
            if (key == "KeyA")
            {
                lockpickAngle = Math.Max(0f, lockpickAngle - 2f);
            }
            if (key == "KeyD")
            {
                lockpickAngle = Math.Min(180f, lockpickAngle + 2f);
            }

            // Tension
            if (key == "KeyW")
            {
                if (!isApplyingTension)
                {
                    isApplyingTension = true;
                }

                float distanceFromSweetSpot = Math.Abs(lockpickAngle - sweetSpot);

                if (distanceFromSweetSpot <= sweetSpotRange)
                {
                    lockRotation += 1.5f; // Successful tension

                    if (lockRotation >= maxLockRotation)
                    {
                        CompleteLockpicking(true);
                        return;
                    }
                }
                else
                {
                    // Failed tension, damage lockpick
                    float damageMultiplier = Math.Min(distanceFromSweetSpot / sweetSpotRange, 3f);
                    lockpickHealth -= damageMultiplier;

                    if (lockpickHealth <= 0f)
                    {
                        BreakLockpick();
                        return;
                    }
                }
            }

            // Exit
            if (key == "KeyS")
            {
                CompleteLockpicking(false);
            }

            // Update NUI after input with lock rotation
            SendNuiMessage($"{{\"action\":\"update\",\"lockpicks\":{lockpicks},\"health\":{(int)lockpickHealth},\"angle\":{(int)lockpickAngle},\"lockRotation\":{lockRotation}}}");
        }



        public void StartLockpicking()
        {
            isActive = true;
            lockpickAngle = 90f;
            lockRotation = 0f;
            sweetSpot = random.Next(30, 150);
            isApplyingTension = false;
            lockpickHealth = 100f;


            SendNuiMessage($"{{\"action\":\"show\",\"lockpicks\":{lockpicks},\"health\":{(int)lockpickHealth},\"angle\":{(int)lockpickAngle}}}");

            SetNuiFocus(true, true);
        }

        private void BreakLockpick()
        {
            lockpicks--;
            Screen.ShowNotification("~r~Lockpick broken!");

            if (lockpicks <= 0)
            {
                Screen.ShowNotification("~r~No more lockpicks! Robbery failed.");
                CompleteLockpicking(false);
            }
            else
            {
                // Reset for new attempt
                lockpickAngle = 90f;
                lockRotation = 0f;
                lockpickHealth = 100f;
                sweetSpot = random.Next(30, 150);
                isApplyingTension = false;

                // Update NUI for new attempt
                SendNuiMessage($"{{\"action\":\"update\",\"lockpicks\":{lockpicks},\"health\":{(int)lockpickHealth},\"angle\":{(int)lockpickAngle}}}");
            }
        }

        private void CompleteLockpicking(bool success)
        {
            isActive = false;
            SetNuiFocus(false, false);
            SendNuiMessage("{\"action\":\"hide\"}");

            if (success)
            {
                Screen.ShowNotification("~g~Lock picked successfully!");
                // Don't show "House unlocked" here since MissionManager will handle it
            }
            else
            {
                Screen.ShowNotification("~r~Lockpicking failed!");
                // MissionManager will handle mission failure
            }

            OnLockpickingComplete?.Invoke(success);
        }

        public void DrawUI()
        {
            if (!isActive) return;

            float centerX = Screen.Width / 2;
            float centerY = Screen.Height / 2;
            float lockRadius = 0.15f;

            DrawSprite("mpinventory", "mp_armed_mask", centerX, centerY, lockRadius * 2, lockRadius * 2, 0f, 80, 80, 80, 255);
        }
    }
}
