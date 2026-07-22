using System;
using UnityEngine;

/// <summary>
/// Account/economy facade for the menus. Since the SHOP.md v4 fold the balance itself lives in
/// ProgressStore as monotonic earned/spent counters (DATA.md rule 3, cloud-merge ready); this
/// class stays the single call-site gameplay/UI use (CoinLedger banks here, the shop charges
/// here, the top bar listens here) so the fold changed no callers. Name/level/XP remain
/// placeholders until the online identity phase (BACKEND.md).
/// </summary>
public static class PlayerProfileStore
{
    /// <summary>Raised after the coin balance changes (already persisted).</summary>
    public static event Action CoinsChanged;

    public static int Coins => ProgressStore.CoinBalance;

    /// <summary>Add (or with a negative amount, spend) coins. Spending clamps at zero,
    /// persisted immediately - a crash must never eat earned currency.</summary>
    public static void AddCoins(int amount)
    {
        if (amount == 0) return;
        if (amount > 0) ProgressStore.EarnCoins(amount);
        else if (ProgressStore.SpendCoins(-amount) == 0) return;
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
        AttemptsService.Count,
        AttemptsService.MaxAttempts,
        AttemptsService.NextRegenIn);
}
