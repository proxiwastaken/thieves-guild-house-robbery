using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CitizenFX.Core;

namespace HouseRobbery.Client
{
    public class LootItem
    {
        public string Type { get; }
        public Vector3 Position { get; }
        public int MaxAmount { get; }
        public int Remaining { get; private set; }

        public LootItem(string type, Vector3 position, int maxAmount)
        {
            Type = type;
            Position = position;
            MaxAmount = maxAmount;
            Remaining = maxAmount;
        }

        public int PickUp(int amount)
        {
            int taken = Math.Min(amount, Remaining);
            Remaining -= taken;
            return taken;
        }

        public bool IsDepleted => Remaining <= 0;
    }
}
