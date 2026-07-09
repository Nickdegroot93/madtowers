using System;
using UnityEngine;

/// <summary>
/// Account/economy facade for the menus. COINS ARE REAL (PlayerPrefs-backed, JUICE.md Phase 3:
/// earned in-run by CoinLedger, spent by the future store); the rest is still placeholder and
/// can become real later without touching the menu layout. Local-first: cloud sync arrives
/// with the Supabase phase (BACKEND.md), which will reconcile this same balance.
/// </summary>
public static class PlayerProfileStore
{
    private const string CoinsKey = "profile.coins";

    private static int _coins;
    private static bool _coinsLoaded;

    /// <summary>Raised after the coin balance changes (already persisted).</summary>
    public static event Action CoinsChanged;

    public static int Coins
    {
        get
        {
            if (!_coinsLoaded)
            {
                _coins = Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, 0));
                _coinsLoaded = true;
            }
            return _coins;
        }
    }

    /// <summary>Add (or with a negative amount, spend) coins. Clamped at zero, persisted
    /// immediately - a crash must never eat earned currency.</summary>
    public static void AddCoins(int amount)
    {
        int newBalance = Mathf.Max(0, Coins + amount); // Coins getter ensures the load
        if (newBalance == _coins) return;
        _coins = newBalance;
        PlayerPrefs.SetInt(CoinsKey, _coins);
        PlayerPrefs.Save();
        CoinsChanged?.Invoke();
    }

    public readonly struct Snapshot
    {
        public readonly string PlayerName;
        public readonly int PlayerLevel;
        public readonly float Experience01;
        public readonly int Coins;
        public readonly int Lives;
        public readonly int MaxLives;
        public readonly TimeSpan LifeRefillRemaining;

        public Snapshot(string playerName, int playerLevel, float experience01,
            int coins, int lives, int maxLives, TimeSpan lifeRefillRemaining)
        {
            PlayerName = playerName;
            PlayerLevel = playerLevel;
            Experience01 = experience01;
            Coins = coins;
            Lives = lives;
            MaxLives = maxLives;
            LifeRefillRemaining = lifeRefillRemaining;
        }
    }

    public static Snapshot Current => new Snapshot(
        "PLAYER ONE",
        24,
        0.48f,
        Coins,
        5,
        5,
        new TimeSpan(0, 14, 52));
}
