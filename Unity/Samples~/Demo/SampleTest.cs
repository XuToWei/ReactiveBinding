using UnityEngine;

namespace ReactiveBinding.Samples
{
    /// <summary>
    /// Test script for demonstrating all ReactiveBinding features.
    /// Press keys to modify player data and observe reactive callbacks.
    /// </summary>
    public class SampleTest : MonoBehaviour
    {
        [SerializeField] private PlayerStatsUI playerStatsUI;

        private void Update()
        {
            // === Basic Types ===
            if (Input.GetKeyDown(KeyCode.D))
            {
                playerStatsUI.TakeDamage(10);
                Debug.Log("[Test] Took 10 damage");
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                playerStatsUI.Heal(20);
                Debug.Log("[Test] Healed 20 HP");
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                playerStatsUI.UseMana(10f);
                Debug.Log("[Test] Used 10 mana");
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                playerStatsUI.LevelUp();
                Debug.Log("[Test] Leveled up");
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                playerStatsUI.AddBonusDamage(5);
                Debug.Log("[Test] Added 5 bonus damage");
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                playerStatsUI.SetCriticalRate(Random.Range(0f, 1f));
                Debug.Log("[Test] Changed critical rate");
            }

            if (Input.GetKeyDown(KeyCode.N))
            {
                playerStatsUI.SetPlayerName($"Player_{Random.Range(1000, 9999)}");
                Debug.Log("[Test] Changed player name");
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                playerStatsUI.SetAlive(!playerStatsUI.gameObject.activeSelf || Random.value > 0.5f);
                Debug.Log("[Test] Toggled alive state");
            }

            // === Struct Types ===
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                playerStatsUI.ModifyStats(
                    Random.Range(5, 20),
                    Random.Range(5, 20),
                    Random.Range(5, 20)
                );
                Debug.Log("[Test] Modified player stats (struct)");
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                playerStatsUI.MovePosition(new Vector3(
                    Random.Range(-10f, 10f),
                    0,
                    Random.Range(-10f, 10f)
                ));
                Debug.Log("[Test] Moved position (Vector3)");
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                string[] weapons = { "Sword", "Axe", "Staff", "Bow", "Dagger" };
                playerStatsUI.EquipWeapon(
                    weapons[Random.Range(0, weapons.Length)],
                    Random.Range(5, 30),
                    Random.Range(0, 10)
                );
                Debug.Log("[Test] Equipped new weapon (struct)");
            }

            // === Enum Types ===
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                PlayerState[] states = { PlayerState.Idle, PlayerState.Walking, PlayerState.Running, PlayerState.Attacking };
                playerStatsUI.SetState(states[Random.Range(0, states.Length)]);
                Debug.Log("[Test] Changed player state (enum)");
            }

            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                ElementType[] elements = { ElementType.None, ElementType.Fire, ElementType.Water, ElementType.Earth, ElementType.Wind };
                playerStatsUI.SetElement(elements[Random.Range(0, elements.Length)]);
                Debug.Log("[Test] Changed element type (enum)");
            }

            // === Nullable Types ===
            if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                if (Random.value > 0.5f)
                    playerStatsUI.SetTarget(Random.Range(1, 100));
                else
                    playerStatsUI.SetTarget(null);
                Debug.Log("[Test] Changed target (int?)");
            }

            if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                if (Random.value > 0.3f)
                    playerStatsUI.SetBuffDuration(Random.Range(1f, 30f));
                else
                    playerStatsUI.SetBuffDuration(null);
                Debug.Log("[Test] Changed buff duration (float?)");
            }

            // === VersionList (Items) ===
            if (Input.GetKeyDown(KeyCode.I))
            {
                string[] items = { "Sword", "Shield", "Potion", "Gold", "Key", "Scroll" };
                playerStatsUI.AddItem(items[Random.Range(0, items.Length)], Random.Range(1, 10));
                Debug.Log("[Test] Added item to inventory");
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                playerStatsUI.RemoveLastItem();
                Debug.Log("[Test] Removed last item from inventory");
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                playerStatsUI.ClearInventory();
                Debug.Log("[Test] Cleared inventory");
            }

            // === VersionDictionary (Skills) ===
            if (Input.GetKeyDown(KeyCode.Q))
            {
                string[] skills = { "Fireball", "Heal", "Shield", "Slash", "Thunder" };
                playerStatsUI.LearnSkill(skills[Random.Range(0, skills.Length)], Random.Range(1, 5));
                Debug.Log("[Test] Learned new skill");
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                string[] skills = { "Fireball", "Heal", "Shield", "Slash", "Thunder" };
                playerStatsUI.UpgradeSkill(skills[Random.Range(0, skills.Length)]);
                Debug.Log("[Test] Upgraded skill");
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                string[] skills = { "Fireball", "Heal", "Shield", "Slash", "Thunder" };
                playerStatsUI.ForgetSkill(skills[Random.Range(0, skills.Length)]);
                Debug.Log("[Test] Forgot skill");
            }

            // === VersionHashSet (Achievements) ===
            if (Input.GetKeyDown(KeyCode.A))
            {
                string[] achievements = { "First Blood", "Level 10", "Boss Slayer", "Explorer", "Collector" };
                playerStatsUI.UnlockAchievement(achievements[Random.Range(0, achievements.Length)]);
                Debug.Log("[Test] Unlocked achievement");
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                string[] achievements = { "First Blood", "Level 10", "Boss Slayer", "Explorer", "Collector" };
                playerStatsUI.RevokeAchievement(achievements[Random.Range(0, achievements.Length)]);
                Debug.Log("[Test] Revoked achievement");
            }

            // === VersionList<Struct> (Buffs) ===
            if (Input.GetKeyDown(KeyCode.Z))
            {
                string[] buffs = { "Attack Up", "Defense Up", "Speed Up", "Regen", "Shield" };
                playerStatsUI.AddBuff(
                    buffs[Random.Range(0, buffs.Length)],
                    Random.Range(5f, 30f),
                    Random.Range(5, 50)
                );
                Debug.Log("[Test] Added buff");
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                playerStatsUI.ClearBuffs();
                Debug.Log("[Test] Cleared all buffs");
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 1000));
            GUILayout.Label("=== ReactiveBinding Sample Test ===");
            GUILayout.Space(5);

            GUILayout.Label("--- Basic Types ---");
            GUILayout.Label("D - Take 10 damage (int)");
            GUILayout.Label("H - Heal 20 HP (int)");
            GUILayout.Label("M - Use 10 mana (float)");
            GUILayout.Label("L - Level up (int + method)");
            GUILayout.Label("B - Add 5 bonus damage (method)");
            GUILayout.Label("C - Random critical rate (float)");
            GUILayout.Label("N - Random player name (string)");
            GUILayout.Label("K - Toggle alive state (bool)");
            GUILayout.Space(5);

            GUILayout.Label("--- Struct Types ---");
            GUILayout.Label("1 - Modify player stats (PlayerStats)");
            GUILayout.Label("2 - Move position (Vector3)");
            GUILayout.Label("3 - Equip weapon (EquipmentSlot)");
            GUILayout.Space(5);

            GUILayout.Label("--- Enum Types ---");
            GUILayout.Label("4 - Change player state");
            GUILayout.Label("5 - Change element type");
            GUILayout.Space(5);

            GUILayout.Label("--- Nullable Types ---");
            GUILayout.Label("6 - Set/clear target (int?)");
            GUILayout.Label("7 - Set/clear buff duration (float?)");
            GUILayout.Space(5);

            GUILayout.Label("--- VersionList (Items) ---");
            GUILayout.Label("I - Add random item");
            GUILayout.Label("R - Remove last item");
            GUILayout.Label("X - Clear inventory");
            GUILayout.Space(5);

            GUILayout.Label("--- VersionDictionary (Skills) ---");
            GUILayout.Label("Q - Learn random skill");
            GUILayout.Label("W - Upgrade random skill");
            GUILayout.Label("E - Forget random skill");
            GUILayout.Space(5);

            GUILayout.Label("--- VersionHashSet (Achievements) ---");
            GUILayout.Label("A - Unlock random achievement");
            GUILayout.Label("S - Revoke random achievement");
            GUILayout.Space(5);

            GUILayout.Label("--- VersionList<Struct> (Buffs) ---");
            GUILayout.Label("Z - Add random buff");
            GUILayout.Label("V - Clear all buffs");

            GUILayout.EndArea();
        }
    }
}
