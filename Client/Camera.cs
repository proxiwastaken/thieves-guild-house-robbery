using System;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class Camera
    {
        public Vector3 Position { get; }
        public Vector3 PointA { get; }
        public Vector3 PointB { get; }
        public float ScanSpeed { get; }
        public float WaitTime { get; }
        public float DetectionRange { get; }
        public float ViewAngle { get; }
        public bool IsActive { get; private set; }
        public bool IsDisabled { get; private set; }

        private float currentRotation;
        private float targetRotation;
        private bool scanningToB;
        private float waitTimer;
        private bool isWaiting;
        private float disableTimer;
        private float maxDisableTime;

        private int cameraObject = 0;
        private uint cameraModel = 0;

        public Camera(Vector3 position, float rotation, float detectionRange = 15f, float viewAngle = 60f,
             float scanAngle = 45f, float scanSpeed = 2f, float waitTime = 3f)
        {
            Position = position;
            DetectionRange = detectionRange;
            ViewAngle = viewAngle;
            ScanSpeed = scanSpeed;
            WaitTime = waitTime;
            IsActive = true;
            IsDisabled = false;

            // Calculate Point A and B based on rotation and scan angle
            float leftRotation = rotation - (scanAngle / 2f);
            float rightRotation = rotation + (scanAngle / 2f);

            PointA = position + new Vector3(
                (float)Math.Cos(leftRotation * Math.PI / 180f) * detectionRange * 0.8f,
                (float)Math.Sin(leftRotation * Math.PI / 180f) * detectionRange * 0.8f,
                0f
            );

            PointB = position + new Vector3(
                (float)Math.Cos(rightRotation * Math.PI / 180f) * detectionRange * 0.8f,
                (float)Math.Sin(rightRotation * Math.PI / 180f) * detectionRange * 0.8f,
                0f
            );

            // Start facing left point
            currentRotation = leftRotation;
            targetRotation = leftRotation;
            scanningToB = false;

            InitializeCameraProp();
        }

        private async void InitializeCameraProp()
        {
            cameraModel = (uint)GetHashKey("prop_pap_camera_01"); // Example camera prop model
            RequestModel(cameraModel);

            // Wait for model to load
            int attempts = 0;
            while (!HasModelLoaded(cameraModel) && attempts < 50)
            {
                await BaseScript.Delay(100);
                attempts++;
            }

            if (HasModelLoaded(cameraModel))
            {
                // Create the camera object
                cameraObject = CreateObject((int)cameraModel, Position.X, Position.Y, Position.Z, false, false, false);

                if (DoesEntityExist(cameraObject))
                {
                    // Set initial rotation
                    SetEntityRotation(cameraObject, 0f, 0f, currentRotation, 2, true);

                    // Make it mission entity so it doesn't despawn
                    SetEntityAsMissionEntity(cameraObject, true, true);

                    // Freeze it in place
                    FreezeEntityPosition(cameraObject, true);

                    Debug.WriteLine($"[CAMERA] Spawned camera prop at {Position}");
                }
            }
            else
            {
                Debug.WriteLine("[CAMERA] Failed to load camera model");
            }

            SetModelAsNoLongerNeeded(cameraModel);
        }

        public void Update(float deltaTime)
        {
            if (IsDisabled)
            {
                disableTimer -= deltaTime;
                if (disableTimer <= 0f)
                {
                    IsDisabled = false;
                }
                return;
            }

            if (!IsActive) return;

            if (isWaiting)
            {
                waitTimer -= deltaTime;
                if (waitTimer <= 0f)
                {
                    isWaiting = false;
                    // Switch target
                    Vector3 newTarget = scanningToB ? PointA : PointB;
                    Vector3 direction = newTarget - Position;
                    targetRotation = (float)(Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI);
                    scanningToB = !scanningToB;
                }
                return;
            }

            // Rotate toward target
            float rotationDifference = targetRotation - currentRotation;

            // Handle rotation wrapping
            if (rotationDifference > 180f) rotationDifference -= 360f;
            if (rotationDifference < -180f) rotationDifference += 360f;

            if (Math.Abs(rotationDifference) < 1f)
            {
                currentRotation = targetRotation;
                isWaiting = true;
                waitTimer = WaitTime;
            }
            else
            {
                currentRotation += Math.Sign(rotationDifference) * ScanSpeed * deltaTime * 10f;

                // Normalize rotation
                if (currentRotation >= 360f) currentRotation -= 360f;
                if (currentRotation < 0f) currentRotation += 360f;
            }

            UpdateCameraRotation();
        }

        private void UpdateCameraRotation()
        {
            if (cameraObject != 0 && DoesEntityExist(cameraObject))
            {
                // Only rotate around Z (yaw)
                SetEntityRotation(cameraObject, 0f, 0f, currentRotation, 2, true);
            }
        }

        public float GetCurrentRotation()
        {
            return currentRotation;
        }

        public bool IsPlayerDetected(Vector3 playerPos)
        {
            if (IsDisabled || !IsActive) return false;

            // Check distance
            float distance = GetDistanceBetweenCoords(Position.X, Position.Y, Position.Z,
                                                    playerPos.X, playerPos.Y, playerPos.Z, true);
            if (distance > DetectionRange) return false;

            // Check angle
            Vector3 toPlayer = playerPos - Position;
            float angleToPlayer = (float)(Math.Atan2(toPlayer.Y, toPlayer.X) * 180.0 / Math.PI);

            // Normalize angles
            if (angleToPlayer < 0f) angleToPlayer += 360f;

            float angleDifference = Math.Abs(angleToPlayer - currentRotation);
            if (angleDifference > 180f) angleDifference = 360f - angleDifference;

            return angleDifference <= (ViewAngle / 2f);
        }

        public void Disable(float duration)
        {
            IsDisabled = true;
            disableTimer = duration;
            maxDisableTime = duration;
        }

        public float GetDisableTimeRemaining()
        {
            return IsDisabled ? disableTimer : 0f;
        }

        public Vector3 GetViewDirection()
        {
            float radians = currentRotation * (float)(Math.PI / 180.0);
            return new Vector3((float)Math.Cos(radians), (float)Math.Sin(radians), 0f);
        }

        public void Cleanup()
        {
            if (cameraObject != 0 && DoesEntityExist(cameraObject))
            {
                DeleteEntity(ref cameraObject);
                cameraObject = 0;
            }
        }
    }

}
