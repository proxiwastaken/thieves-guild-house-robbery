using System.Collections.Generic;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public class MissionBuilder
    {
        private Dictionary<string, MissionData> missions = new Dictionary<string, MissionData>();
        private string currentEditingMission = null;

        public MissionBuilder()
        {
            // Create the first test mission
            CreateTestMission();
        }

        private void CreateTestMission()
        {
            var mission = new MissionData(
                "house1",
                new Vector3(910.8f, -502.9f, 58.4f),    // Exterior
                new Vector3(266.3f, -1006.6f, -100.6f), // Interior
                new Vector3(899.6f, -474.0f, 59.4f),     // Entry point 
                new Vector3(899.6f, -474.0f, 59.4f)      // Exit point
            );

            // Add default camera
            mission.Cameras.Add(new CameraData(
                new Vector3(911.4f, -488.2f, 62.1f), // Position
                250f, // Rotation
                12f   // DetectionRange
            ));

            mission.Cameras.Add(new CameraData(
                new Vector3(261.9f, -995.4f, -99.0f),
                210f,
                6f
            ));

            // Add default loot - exterior
            mission.Loot.Add(new LootData(
                new Vector3(902.4f, -475.0f, 59.0f), // Position
                "Electronics", // Type
                1       // Amount
            ));

            // Add default loot - interior
            mission.Loot.Add(new LootData(
                new Vector3(263.0f, -1003.0f, -99.0f), // Position
                "Electronics", // Type
                1        // Amount
            ));

            mission.Loot.Add(new LootData(
                new Vector3(265.0f, -997.4f, -99.0f), // Position
                "Jewelry", // Type
                2           // Amount
            ));

            mission.Loot.Add(new LootData(
                new Vector3(256.1f, -1000.7f, -99.0f), // Position
                "Cash", // Type
                3           // Amount
            ));

            missions["house1"] = mission;
            currentEditingMission = "house1";

            Debug.WriteLine("[MISSION BUILDER] Default house1 mission created with:");
            Debug.WriteLine($"- 1 camera at rotation 270°");
            Debug.WriteLine($"- 3 loot items (1 exterior, 2 interior)");
            Debug.WriteLine($"- Entry/Exit point: 878.4f, -497.9f, 58.1f");
        }

        public void SetInteriorExitPoint()
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected for editing!");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var mission = missions[currentEditingMission];
            mission.InteriorExitPoint = playerPos;

            Screen.ShowNotification($"~g~Interior exit point set for {currentEditingMission}");
            Debug.WriteLine($"[MISSION BUILDER] Interior exit point: new Vector3({playerPos.X:F1}f, {playerPos.Y:F1}f, {playerPos.Z:F1}f)");
        }

        public MissionData GetMission(string missionId)
        {
            return missions.ContainsKey(missionId) ? missions[missionId] : null;
        }

        public void SetCurrentMission(string missionId)
        {
            if (missions.ContainsKey(missionId))
            {
                currentEditingMission = missionId;
                Screen.ShowNotification($"~g~Now editing mission: {missionId}");
            }
            else
            {
                Screen.ShowNotification($"~r~Mission '{missionId}' not found!");
            }
        }

        public void AddCamera(float rotation, float detectionRange = 15f, float viewAngle = 60f, float scanAngle = 45f)
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected for editing!");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var mission = missions[currentEditingMission];

            var cameraData = new CameraData(playerPos, rotation, detectionRange, viewAngle, scanAngle);
            mission.Cameras.Add(cameraData);

            Screen.ShowNotification($"~g~Camera added to {currentEditingMission} at rotation {rotation}°");

            // Print for copying
            Debug.WriteLine($"[MISSION BUILDER] Camera added:");
            Debug.WriteLine($"Position: new Vector3({playerPos.X:F1}f, {playerPos.Y:F1}f, {playerPos.Z:F1}f)");
            Debug.WriteLine($"Rotation: {rotation}f");
            Debug.WriteLine($"DetectionRange: {detectionRange}f");
        }

        public void AddLoot(string type, int amount)
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected for editing!");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var mission = missions[currentEditingMission];

            var lootData = new LootData(playerPos, type, amount);
            mission.Loot.Add(lootData);

            Screen.ShowNotification($"~g~{type} x{amount} added to {currentEditingMission}");

            // Print for copying
            Debug.WriteLine($"[MISSION BUILDER] Loot added:");
            Debug.WriteLine($"Position: new Vector3({playerPos.X:F1}f, {playerPos.Y:F1}f, {playerPos.Z:F1}f)");
            Debug.WriteLine($"Type: \"{type}\", Amount: {amount}");
        }

        public void SetEntryPoint()
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected for editing!");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var mission = missions[currentEditingMission];
            mission.EntryPoint = playerPos;

            Screen.ShowNotification($"~g~Entry point set for {currentEditingMission}");
            Debug.WriteLine($"[MISSION BUILDER] Entry point: new Vector3({playerPos.X:F1}f, {playerPos.Y:F1}f, {playerPos.Z:F1}f)");
        }

        public void SetExitPoint()
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected for editing!");
                return;
            }

            var playerPos = GetEntityCoords(PlayerPedId(), true);
            var mission = missions[currentEditingMission];
            mission.ExitPoint = playerPos;

            Screen.ShowNotification($"~g~Exit point set for {currentEditingMission}");
            Debug.WriteLine($"[MISSION BUILDER] Exit point: new Vector3({playerPos.X:F1}f, {playerPos.Y:F1}f, {playerPos.Z:F1}f)");
        }


        public void PrintMissionData()
        {
            if (currentEditingMission == null)
            {
                Screen.ShowNotification("~r~No mission selected!");
                return;
            }

            var mission = missions[currentEditingMission];
            Debug.WriteLine($"=== MISSION DATA: {mission.Name} ===");
            Debug.WriteLine($"Exterior: new Vector3({mission.ExteriorPosition.X:F1}f, {mission.ExteriorPosition.Y:F1}f, {mission.ExteriorPosition.Z:F1}f)");
            Debug.WriteLine($"Interior: new Vector3({mission.InteriorPosition.X:F1}f, {mission.InteriorPosition.Y:F1}f, {mission.InteriorPosition.Z:F1}f)");
            Debug.WriteLine($"Entry: new Vector3({mission.EntryPoint.X:F1}f, {mission.EntryPoint.Y:F1}f, {mission.EntryPoint.Z:F1}f)");
            Debug.WriteLine($"Exit: new Vector3({mission.ExitPoint.X:F1}f, {mission.ExitPoint.Y:F1}f, {mission.ExitPoint.Z:F1}f)");
            Debug.WriteLine($"InteriorExit: new Vector3({mission.InteriorExitPoint.X:F1}f, {mission.InteriorExitPoint.Y:F1}f, {mission.InteriorExitPoint.Z:F1}f)");
            Debug.WriteLine($"Cameras: {mission.Cameras.Count}");
            Debug.WriteLine($"Loot: {mission.Loot.Count}");
            Debug.WriteLine("=================");

            Screen.ShowNotification("Mission data printed to F8 console");
        }



        public List<string> GetMissionIds()
        {
            return new List<string>(missions.Keys);
        }
    }
}
