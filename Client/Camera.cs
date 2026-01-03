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

        public Camera(Vector3 position, Vector3 pointA, Vector3 pointB, float scanSpeed = 2f,
                     float waitTime = 3f, float detectionRange = 15f, float viewAngle = 60f)
        {
            Position = position;
            PointA = pointA;
            PointB = pointB;
            ScanSpeed = scanSpeed;
            WaitTime = waitTime;
            DetectionRange = detectionRange;
            ViewAngle = viewAngle;
            IsActive = true;
            IsDisabled = false;

            // Calculate initial rotation toward Point A
            Vector3 direction = PointA - Position;
            currentRotation = (float)(Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI);
            targetRotation = currentRotation;
            scanningToB = false;
            waitTimer = 0f;
            isWaiting = false;
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
    }
}
