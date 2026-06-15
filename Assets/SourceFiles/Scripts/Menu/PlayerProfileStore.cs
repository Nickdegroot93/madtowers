using System;

/// <summary>
/// Placeholder account/economy facade for the main menu. Real profile, currency, EXP and
/// life refill systems can replace this narrow API later without touching the menu layout.
/// </summary>
public static class PlayerProfileStore
{
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
        12450,
        5,
        5,
        new TimeSpan(0, 14, 52));
}
