public enum GamePhase
{
    Intro,
    Playing,
    AbilityChoice,
    WinVerifying,
    Paused,
    Completed,
    GameOver,
    // A never-seen brick variant's one-time debut modal is on screen (BlockDiscoveryController).
    // The world stays LIVE (no timeScale pause - the demo and the hovering brick keep animating);
    // the phase alone holds spawning, the timed-goal clock and pending ability offers.
    Discovery
}
