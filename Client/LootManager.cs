using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HouseRobbery.Client
{
    public class LootManager
    {
        public List<LootItem> LootItems { get; } = new List<LootItem>();
        public Dictionary<string, int> PlayerLoot { get; } = new Dictionary<string, int>();
        public int CarryLimit { get; } = 10;

        public int CurrentCarried
        {
            get { return PlayerLoot.Values.Sum(); }
        }

        public void AddLootItem(LootItem item)
        {
            LootItems.Add(item);
        }

        public bool CanCarry(int amount)
        {
            return (CurrentCarried + amount) <= CarryLimit;
        }

        public int PickUpLoot(LootItem item, int amount)
        {
            if (item.IsDepleted) return 0;
            int canTake = Math.Min(amount, item.Remaining);
            canTake = Math.Min(canTake, CarryLimit - CurrentCarried);
            if (canTake <= 0) return 0;

            int taken = item.PickUp(canTake);
            if (!PlayerLoot.ContainsKey(item.Type))
                PlayerLoot[item.Type] = 0;
            PlayerLoot[item.Type] += taken;
            return taken;
        }

        public void UnloadLoot()
        {
            PlayerLoot.Clear();
        }
    }

}
