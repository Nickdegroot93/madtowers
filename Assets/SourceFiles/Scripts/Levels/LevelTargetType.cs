public enum LevelTargetType
{
    /// <summary>No goal - free play until the tower falls.</summary>
    Endless,
    /// <summary>Win by placing this many blocks.</summary>
    PlaceBlocks,
    /// <summary>Win by reaching this tower height in meters above the floor.</summary>
    ReachHeight,
    /// <summary>Win by placing this many blocks before the authored time limit expires.</summary>
    TimedPlaceBlocks,
    /// <summary>Win by reaching this tower height before the authored time limit expires.</summary>
    TimedReachHeight
}
