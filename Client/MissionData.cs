using System.Collections.Generic;
using CitizenFX.Core;

namespace HouseRobbery.Client
{
    public class CameraData
    {
        public Vector3 Position { get; set; }
        public float Rotation { get; set; }
        public float DetectionRange { get; set; } = 15f;
        public float ViewAngle { get; set; } = 60f;
        public float ScanAngle { get; set; } = 45f;

        public CameraData(Vector3 position, float rotation, float detectionRange = 15f, float viewAngle = 60f, float scanAngle = 45f)
        {
            Position = position;
            Rotation = rotation;
            DetectionRange = detectionRange;
            ViewAngle = viewAngle;
            ScanAngle = scanAngle;
        }
    }

    public class LootData
    {
        public Vector3 Position { get; set; }
        public string Type { get; set; }
        public int Amount { get; set; }

        public LootData(Vector3 position, string type, int amount)
        {
            Position = position;
            Type = type;
            Amount = amount;
        }
    }

    public class MissionData
    {
        public string Name { get; set; }
        public Vector3 ExteriorPosition { get; set; }
        public Vector3 InteriorPosition { get; set; }
        public Vector3 EntryPoint { get; set; }
        public Vector3 ExitPoint { get; set; }
        public Vector3 InteriorExitPoint { get; set; }
        public List<CameraData> Cameras { get; set; } = new List<CameraData>();
        public List<LootData> Loot { get; set; } = new List<LootData>();

        public MissionData(string name, Vector3 exterior, Vector3 interior, Vector3 entry, Vector3 exit = new Vector3())
        {
            Name = name;
            ExteriorPosition = exterior;
            InteriorPosition = interior;
            EntryPoint = entry;
            ExitPoint = exit.IsZero ? entry : exit;
            InteriorExitPoint = new Vector3(266.0f, -1007.6f, -101.0f);
        }
    }


}
