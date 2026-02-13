using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.UI;
using static CitizenFX.Core.Native.API;

namespace HouseRobbery.Client
{
    public enum MissionType
    {
        Loud,
        Quiet
    }

    public class LoadoutSystem
    {
        private bool hasAppliedLoadout = false;
        private uint originalPlayerModel = 0;
        private bool hasChangedModel = false;
        private MissionType currentMissionType;

        // Bank Robber model hash
        private readonly uint bankRobberModel = 3645767658; // A_M_M_BankRobber_01

        // Loud mission weapons
        private readonly uint[] loudWeapons = {
            (uint)GetHashKey("weapon_assaultrifle"),  
            (uint)GetHashKey("weapon_pumpshotgun"),   
            (uint)GetHashKey("weapon_pistol"),        
            (uint)GetHashKey("weapon_grenade"),       
            (uint)GetHashKey("weapon_smokegrenade")   
        };

        // Quiet mission weapons
        private readonly uint[] quietWeapons = {
            (uint)GetHashKey("weapon_pistol"),        
            (uint)GetHashKey("weapon_knife"),         
            (uint)GetHashKey("weapon_stungun")        
        };

        public void Initialize()
        {
            // Store original player model
            originalPlayerModel = (uint)GetEntityModel(PlayerPedId());
            Debug.WriteLine("[LOADOUT] Loadout system initialized");
        }

        public async void ApplyMissionLoadout(MissionType missionType)
        {
            if (hasAppliedLoadout)
            {
                CleanupLoadout(); // Remove previous loadout first
                await BaseScript.Delay(500);
            }

            currentMissionType = missionType;
            var playerPed = PlayerPedId();

            Debug.WriteLine($"[LOADOUT] Applying {missionType} mission loadout");

            // Change player model to bank robber
            await ChangePlayerModel();

            // Give appropriate armor
            ApplyArmor(missionType);

            // Give mission-specific weapons
            ApplyWeapons(missionType);

            hasAppliedLoadout = true;

            // Show notifications based on mission type
            if (missionType == MissionType.Loud)
            {
                Screen.ShowNotification("~r~LOUD LOADOUT EQUIPPED!");
                Screen.ShowNotification("~r~Heavy armor, assault weapons, explosives ready!");
            }
            else
            {
                Screen.ShowNotification("~b~STEALTH LOADOUT EQUIPPED!");
                Screen.ShowNotification("~b~Light armor, silenced weapons, stealth tools ready!");
            }

            Debug.WriteLine($"[LOADOUT] {missionType} loadout applied successfully");
        }

        private async Task ChangePlayerModel()
        {
            try
            {
                // Request the bank robber model
                if (!await LoadModel(bankRobberModel))
                {
                    Debug.WriteLine("[LOADOUT] Failed to load bank robber model");
                    return;
                }

                var playerPed = PlayerPedId();
                var playerPos = GetEntityCoords(playerPed, true);
                var playerHeading = GetEntityHeading(playerPed);

                // Store current vehicle info if player is in one
                int currentVehicle = GetVehiclePedIsIn(playerPed, false);
                int currentSeat = -2;

                if (DoesEntityExist(currentVehicle))
                {
                    // Find which seat the player is in
                    for (int seat = -1; seat < 4; seat++)
                    {
                        if (GetPedInVehicleSeat(currentVehicle, seat) == playerPed)
                        {
                            currentSeat = seat;
                            break;
                        }
                    }
                }

                // Change to bank robber model
                SetPlayerModel(PlayerId(), bankRobberModel);

                // Get new ped reference
                playerPed = PlayerPedId();

                // Restore position and vehicle
                SetEntityCoords(playerPed, playerPos.X, playerPos.Y, playerPos.Z, false, false, false, true);
                SetEntityHeading(playerPed, playerHeading);

                // Put back in vehicle if they were in one
                if (DoesEntityExist(currentVehicle) && currentSeat != -2)
                {
                    SetPedIntoVehicle(playerPed, currentVehicle, currentSeat);
                }

                hasChangedModel = true;
                SetModelAsNoLongerNeeded(bankRobberModel);

                Screen.ShowNotification("~g~Transformed into professional bank robber!");
                Debug.WriteLine("[LOADOUT] Player model changed to bank robber");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOADOUT] Error changing player model: {ex.Message}");
                Screen.ShowNotification("~r~Failed to change appearance!");
            }
        }

        private void ApplyArmor(MissionType missionType)
        {
            var playerPed = PlayerPedId();

            switch (missionType)
            {
                case MissionType.Loud:
                    // Heavy armor for loud approach
                    SetPedArmour(playerPed, 100); // Full armor
                    Debug.WriteLine("[LOADOUT] Applied heavy armor (100%)");
                    break;

                case MissionType.Quiet:
                    // Light armor for stealth approach  
                    SetPedArmour(playerPed, 50); // Moderate armor for mobility
                    Debug.WriteLine("[LOADOUT] Applied light armor (50%)");
                    break;
            }
        }

        private void ApplyWeapons(MissionType missionType)
        {
            var playerPed = PlayerPedId();

            // Clear existing weapons first
            RemoveAllPedWeapons(playerPed, true);

            switch (missionType)
            {
                case MissionType.Loud:
                    ApplyLoudWeapons(playerPed);
                    break;

                case MissionType.Quiet:
                    ApplyQuietWeapons(playerPed);
                    break;
            }
        }

        private void ApplyLoudWeapons(int playerPed)
        {
            // Primary: Assault Rifle with extended magazine
            GiveWeaponToPed(playerPed, loudWeapons[0], 300, false, true);
            GiveWeaponComponentToPed(playerPed, loudWeapons[0], (uint)GetHashKey("COMPONENT_ASSAULTRIFLE_CLIP_02")); // Extended mag
            GiveWeaponComponentToPed(playerPed, loudWeapons[0], (uint)GetHashKey("COMPONENT_AT_AR_FLSH")); // Flashlight

            // Secondary: Pump Shotgun  
            GiveWeaponToPed(playerPed, loudWeapons[1], 100, false, false);
            GiveWeaponComponentToPed(playerPed, loudWeapons[1], (uint)GetHashKey("COMPONENT_AT_AR_FLSH")); // Flashlight

            // Sidearm: Pistol with extended mag
            GiveWeaponToPed(playerPed, loudWeapons[2], 150, false, false);
            GiveWeaponComponentToPed(playerPed, loudWeapons[2], (uint)GetHashKey("COMPONENT_PISTOL_CLIP_02")); // Extended mag

            // Explosives: Grenades
            GiveWeaponToPed(playerPed, loudWeapons[3], 5, false, false);

            // Tactical: Smoke grenades
            GiveWeaponToPed(playerPed, loudWeapons[4], 3, false, false);

            // Set primary weapon
            SetCurrentPedWeapon(playerPed, loudWeapons[0], true);

            Debug.WriteLine("[LOADOUT] Applied loud weapons: Assault rifle, shotgun, pistol, grenades");
        }

        private void ApplyQuietWeapons(int playerPed)
        {
            // Primary: Silenced Pistol
            GiveWeaponToPed(playerPed, quietWeapons[0], 150, false, true);
            GiveWeaponComponentToPed(playerPed, quietWeapons[0], (uint)GetHashKey("COMPONENT_AT_PI_SUPP_02")); // Silencer
            GiveWeaponComponentToPed(playerPed, quietWeapons[0], (uint)GetHashKey("COMPONENT_PISTOL_CLIP_02")); // Extended mag
            GiveWeaponComponentToPed(playerPed, quietWeapons[0], (uint)GetHashKey("COMPONENT_AT_PI_FLSH")); // Flashlight

            // Melee: Knife for silent takedowns
            GiveWeaponToPed(playerPed, quietWeapons[1], 1, false, false);

            // Non-lethal: Stun gun
            GiveWeaponToPed(playerPed, quietWeapons[2], 50, false, false);

            // Set primary weapon
            SetCurrentPedWeapon(playerPed, quietWeapons[0], true);

            Debug.WriteLine("[LOADOUT] Applied stealth weapons: Silenced pistol, knife, stun gun");
        }

        private async Task<bool> LoadModel(uint model)
        {
            if (!IsModelValid(model))
            {
                Debug.WriteLine($"[LOADOUT] Model {model} is not valid");
                return false;
            }

            RequestModel(model);

            int attempts = 0;
            while (!HasModelLoaded(model) && attempts < 50)
            {
                await BaseScript.Delay(100);
                attempts++;
            }

            return HasModelLoaded(model);
        }

        public bool HasAppliedLoadout()
        {
            return hasAppliedLoadout;
        }

        public MissionType GetCurrentMissionType()
        {
            return currentMissionType;
        }

        public void RestorePlayerAppearance()
        {
            if (!hasChangedModel || originalPlayerModel == 0) return;

            try
            {
                var playerPed = PlayerPedId();
                var playerPos = GetEntityCoords(playerPed, true);
                var playerHeading = GetEntityHeading(playerPed);

                // Store current vehicle info
                int currentVehicle = GetVehiclePedIsIn(playerPed, false);
                int currentSeat = -2;

                if (DoesEntityExist(currentVehicle))
                {
                    for (int seat = -1; seat < 4; seat++)
                    {
                        if (GetPedInVehicleSeat(currentVehicle, seat) == playerPed)
                        {
                            currentSeat = seat;
                            break;
                        }
                    }
                }

                // Request original model
                RequestModel(originalPlayerModel);

                int attempts = 0;
                while (!HasModelLoaded(originalPlayerModel) && attempts < 50)
                {
                    BaseScript.Delay(100).Wait();
                    attempts++;
                }

                if (HasModelLoaded(originalPlayerModel))
                {
                    SetPlayerModel(PlayerId(), originalPlayerModel);

                    // Get new ped reference and restore position
                    playerPed = PlayerPedId();
                    SetEntityCoords(playerPed, playerPos.X, playerPos.Y, playerPos.Z, false, false, false, true);
                    SetEntityHeading(playerPed, playerHeading);

                    // Put back in vehicle
                    if (DoesEntityExist(currentVehicle) && currentSeat != -2)
                    {
                        SetPedIntoVehicle(playerPed, currentVehicle, currentSeat);
                    }

                    SetModelAsNoLongerNeeded(originalPlayerModel);
                    hasChangedModel = false;

                    Screen.ShowNotification("~g~Original appearance restored!");
                    Debug.WriteLine("[LOADOUT] Player appearance restored");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOADOUT] Error restoring appearance: {ex.Message}");
            }
        }

        public void CleanupLoadout()
        {
            if (!hasAppliedLoadout) return;

            var playerPed = PlayerPedId();

            // Remove all weapons
            RemoveAllPedWeapons(playerPed, true);

            // Reset armor
            SetPedArmour(playerPed, 0);

            // Restore original appearance
            RestorePlayerAppearance();

            hasAppliedLoadout = false;

            Screen.ShowNotification("~y~Mission loadout removed");
            Debug.WriteLine("[LOADOUT] Loadout cleaned up");
        }

      
        public void GiveSilencedPistol()
        {
            if (currentMissionType == MissionType.Quiet)
            {
                Screen.ShowNotification("~g~Stealth weapons already equipped!");
            }
            else
            {
                var playerPed = PlayerPedId();
                uint pistolHash = (uint)GetHashKey("weapon_pistol");

                GiveWeaponToPed(playerPed, pistolHash, 100, false, true);
                GiveWeaponComponentToPed(playerPed, pistolHash, (uint)GetHashKey("COMPONENT_AT_PI_SUPP_02"));

                Screen.ShowNotification("~g~Received silenced pistol!");
                Debug.WriteLine("[LOADOUT] Gave legacy silenced pistol");
            }
        }

        public bool HasSilencedWeapon()
        {
            return hasAppliedLoadout && HasPedGotWeapon(PlayerPedId(), (uint)GetHashKey("weapon_pistol"), false);
        }

        
        public void Initialize(MissionType missionType)
        {
            Initialize();
            ApplyMissionLoadout(missionType);
        }

        public void Cleanup()
        {
            CleanupLoadout();
            Debug.WriteLine("[LOADOUT] Loadout system cleaned up");
        }
    }
}
