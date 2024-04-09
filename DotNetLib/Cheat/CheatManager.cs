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
            foreach (var playerMoveC in FindObjectsOfType<Player_move_c>())
            {
                if (playerMoveC == null) continue;
                Console.WriteLine(playerMoveC.nickLabel);
            }
        }
    }
}