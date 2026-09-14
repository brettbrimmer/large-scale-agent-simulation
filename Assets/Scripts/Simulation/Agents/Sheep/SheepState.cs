using Unity.Mathematics;

// Struct for every sheep (not using GameObject)
public struct SheepState
{
    // Position X/Z
    public float2 position;

    // Velocity vector X/Z
    public float2 velocity;

    // Permanent individual variation in running speed.
    public float speedMultiplier;

    public bool lost;
}