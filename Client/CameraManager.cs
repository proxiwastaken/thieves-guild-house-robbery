using System;
using System.Collections.Generic;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class CameraManager
    {
        public List<Camera> Cameras { get; } = new List<Camera>();
        public event Action OnAlarmTriggered;

        private bool alarmActive = false;
        private float lastFrameTime;

        public bool IsAlarmActive => alarmActive;

        public void AddCamera(Camera camera)
        {
            Cameras.Add(camera);
        }

        public void ClearCameras()
        {
            Cameras.Clear();
        }

        public void Update()
        {
            float currentTime = GetGameTimer() / 1000f;
            float deltaTime = currentTime - lastFrameTime;
            lastFrameTime = currentTime;

            if (deltaTime > 0.1f) deltaTime = 0.1f; // Cap delta time

            var playerPos = GetEntityCoords(PlayerPedId(), true);

            foreach (var camera in Cameras)
            {
                camera.Update(deltaTime);

                // Check for player detection
                if (!alarmActive && camera.IsPlayerDetected(playerPos))
                {
                    TriggerAlarm();
                    break;
                }
            }
        }

        public void DrawCameras()
        {
            foreach (var camera in Cameras)
            {
                DrawCamera(camera);
            }
        }

        private void DrawCamera(Camera camera)
        {
            // Draw camera position marker
            int color = camera.IsDisabled ? 2 : (camera.IsActive ? 1 : 0); // Blue if disabled, Red if active, White if inactive
            int r = camera.IsDisabled ? 0 : (camera.IsActive ? 255 : 255);
            int g = camera.IsDisabled ? 100 : (camera.IsActive ? 0 : 255);
            int b = camera.IsDisabled ? 255 : (camera.IsActive ? 0 : 255);

            DrawMarker(28, camera.Position.X, camera.Position.Y, camera.Position.Z + 0.1f,
                      0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0.5f, r, g, b, 200, false, true, 2, false, null, null, false);

            if (camera.IsDisabled)
            {
                // Show disable timer
                float timeLeft = camera.GetDisableTimeRemaining();
                DrawText3D(camera.Position + new Vector3(0, 0, 1f), $"Disabled: {timeLeft:F1}s", 255, 255, 255, 0.4f);
                return;
            }

            if (!camera.IsActive) return;

            // Draw view cone
            Vector3 viewDir = camera.GetViewDirection();
            float halfAngle = camera.ViewAngle / 2f;

            // Calculate cone edges
            Vector3 leftEdge = RotateVector(viewDir, -halfAngle) * camera.DetectionRange;
            Vector3 rightEdge = RotateVector(viewDir, halfAngle) * camera.DetectionRange;
            Vector3 centerRay = viewDir * camera.DetectionRange;

            Vector3 leftPoint = camera.Position + leftEdge;
            Vector3 rightPoint = camera.Position + rightEdge;
            Vector3 centerPoint = camera.Position + centerRay;

            // Draw view cone lines
            DrawLine(camera.Position.X, camera.Position.Y, camera.Position.Z,
                    leftPoint.X, leftPoint.Y, leftPoint.Z, 255, 0, 0, 150);
            DrawLine(camera.Position.X, camera.Position.Y, camera.Position.Z,
                    rightPoint.X, rightPoint.Y, rightPoint.Z, 255, 0, 0, 150);
            DrawLine(leftPoint.X, leftPoint.Y, leftPoint.Z,
                    rightPoint.X, rightPoint.Y, rightPoint.Z, 255, 0, 0, 100);

            // Draw center line brighter
            DrawLine(camera.Position.X, camera.Position.Y, camera.Position.Z,
                    centerPoint.X, centerPoint.Y, centerPoint.Z, 255, 255, 0, 200);
        }

        private Vector3 RotateVector(Vector3 vector, float angleInDegrees)
        {
            float radians = angleInDegrees * (float)(Math.PI / 180.0);
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);

            return new Vector3(
                vector.X * cos - vector.Y * sin,
                vector.X * sin + vector.Y * cos,
                vector.Z
            );
        }

        private void DrawText3D(Vector3 position, string text, int r, int g, int b, float scale)
        {
            Vector3 camPos = GetGameplayCamCoord();
            float distance = GetDistanceBetweenCoords(camPos.X, camPos.Y, camPos.Z, position.X, position.Y, position.Z, true);

            if (distance > 20f) return;

            SetTextScale(0.0f, scale);
            SetTextFont(0);
            SetTextProportional(true);
            SetTextColour(r, g, b, 255);
            SetTextDropshadow(0, 0, 0, 0, 255);
            SetTextEdge(2, 0, 0, 0, 150);
            SetTextDropShadow();
            SetTextOutline();
            SetTextEntry("STRING");
            AddTextComponentString(text);

            float screenX = 0f;
            float screenY = 0f;
            bool onScreen = GetScreenCoordFromWorldCoord(position.X, position.Y, position.Z, ref screenX, ref screenY);
            if (onScreen)
            {
                DrawText(screenX, screenY);
            }
        }





        private void TriggerAlarm()
        {
            if (alarmActive) return;

            alarmActive = true;
            Screen.ShowNotification("~r~ALARM TRIGGERED! You've been detected!");
            OnAlarmTriggered?.Invoke();
        }

        public void ResetAlarm()
        {
            alarmActive = false;
        }

        public void DisableCamerasInRadius(Vector3 position, float radius, float duration)
        {
            foreach (var camera in Cameras)
            {
                float distance = GetDistanceBetweenCoords(position.X, position.Y, position.Z,
                                                        camera.Position.X, camera.Position.Y, camera.Position.Z, true);
                if (distance <= radius)
                {
                    camera.Disable(duration);
                }
            }
        }
    }
}
