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
            // Clean up camera objects before clearing the list
            foreach (var camera in Cameras)
            {
                camera.Cleanup();
            }

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
            // Don't draw anything if camera doesn't exist
            if (!camera.IsActive && !camera.IsDisabled) return;

            // Draw transparent red frustum instead of lines
            if (!camera.IsDisabled && camera.IsActive)
            {
                DrawCameraFrustum(camera);
            }

            if (camera.IsDisabled)
            {
                // Show disable timer above the camera
                float timeLeft = camera.GetDisableTimeRemaining();
                DrawText3D(camera.Position + new Vector3(0, 0, 2f), $"Disabled: {timeLeft:F1}s", 255, 255, 255, 0.4f);
            }
        }

        private void DrawCameraFrustum(Camera camera)
        {
            float halfAngle = camera.ViewAngle / 2f;
            float range = camera.DetectionRange;

            // Use the camera's current rotation (in degrees)
            float baseRot = camera.GetCurrentRotation(); // Add this getter if needed

            // Calculate left and right edge directions
            float leftRot = baseRot - halfAngle;
            float rightRot = baseRot + halfAngle;

            Vector3 cameraPos = camera.Position + new Vector3(0, 0, 0.5f); // Slightly elevated

            Vector3 leftDir = new Vector3(
                (float)Math.Cos(leftRot * Math.PI / 180f),
                (float)Math.Sin(leftRot * Math.PI / 180f),
                0f
            );
            Vector3 rightDir = new Vector3(
                (float)Math.Cos(rightRot * Math.PI / 180f),
                (float)Math.Sin(rightRot * Math.PI / 180f),
                0f
            );
            Vector3 centerDir = new Vector3(
                (float)Math.Cos(baseRot * Math.PI / 180f),
                (float)Math.Sin(baseRot * Math.PI / 180f),
                0f
            );

            Vector3 leftPoint = cameraPos + leftDir * range;
            Vector3 rightPoint = cameraPos + rightDir * range;
            Vector3 centerPoint = cameraPos + centerDir * range;

            DrawFrustumTriangle(cameraPos, leftPoint, rightPoint, centerPoint, camera.IsDisabled);
        }

        private void DrawThickLine(Vector3 start, Vector3 end, int r, int g, int b, int alpha, float thickness = 0.05f, int steps = 4)
        {
            // Draw the main line
            DrawLine(start.X, start.Y, start.Z, end.X, end.Y, end.Z, r, g, b, alpha);

            // Draw parallel offset lines for thickness
            Vector3 dir = end - start;
            Vector3 up = new Vector3(0, 0, 1f);
            Vector3 side = Vector3.Cross(dir, up);
            if (side.LengthSquared() < 0.001f) // If looking straight up/down, use X axis
                side = new Vector3(1, 0, 0);
            side = Vector3.Normalize(side);

            float half = thickness / 2f;
            for (int i = 1; i <= steps; i++)
            {
                float offset = half * ((float)i / steps);
                // Both sides
                Vector3 offsetVec = side * offset;
                DrawLine((start + offsetVec).X, (start + offsetVec).Y, (start + offsetVec).Z, (end + offsetVec).X, (end + offsetVec).Y, (end + offsetVec).Z, r, g, b, alpha);
                DrawLine((start - offsetVec).X, (start - offsetVec).Y, (start - offsetVec).Z, (end - offsetVec).X, (end - offsetVec).Y, (end - offsetVec).Z, r, g, b, alpha);
            }
        }

        private void DrawFrustumTriangle(Vector3 apex, Vector3 left, Vector3 right, Vector3 center, bool disabled)
        {
            // Color and transparency
            int r = disabled ? 100 : 255;
            int g = disabled ? 100 : 0;
            int b = disabled ? 255 : 0;
            int alpha = disabled ? 40 : 100;

            float thickness = 0.10f;

            // Draw triangle edges
            DrawThickLine(apex, left, r, g, b, alpha, thickness);
            DrawThickLine(apex, right, r, g, b, alpha, thickness);
            DrawThickLine(left, right, r, g, b, alpha, thickness);

            // Fill the triangle with semi-transparent lines for a "frustum" effect
            int fillLines = 12;
            for (int i = 1; i < fillLines; i++)
            {
                float t = (float)i / fillLines;
                Vector3 fillStart = Vector3.Lerp(left, apex, t);
                Vector3 fillEnd = Vector3.Lerp(right, apex, t);
                DrawThickLine(fillStart, fillEnd, r, g, b, alpha / 2, thickness / 2);
            }
        }



        // dont use, kept for reference
        private void DrawTransparentLine(Vector3 start, Vector3 end, int r, int g, int b, int alpha)
        {
            // Create multiple small markers along the line to simulate transparency
            int points = 10;
            for (int i = 0; i <= points; i++)
            {
                float t = (float)i / points;
                Vector3 point = Vector3.Lerp(start, end, t);

                DrawMarker(28, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0,
                          0.3f, 0.3f, 0.2f, r, g, b, alpha, false, false, 2, false, null, null, false);
            }
        }

        // dont use
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
