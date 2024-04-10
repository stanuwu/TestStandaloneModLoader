/*
using UnityEngine;

namespace DotNetLib
{
    public class CheatManager : MonoBehaviour
    {
        private void Awake()
        {
            Console.WriteLine("CheatManager Activated");
        }

        private void Update()
        {
            foreach (var weaponSounds in FindObjectsOfType<WeaponSounds>())
            {
                if (weaponSounds == null) continue;
                if (weaponSounds.prop_Player_move_c_0 == null) continue;
                if (weaponSounds.prop_Player_move_c_0.nickLabel.text != "1111") continue;
                weaponSounds.range = 99999f;
            }
        }
    }
}
*/