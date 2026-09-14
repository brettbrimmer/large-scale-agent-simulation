using Unity.Mathematics;

// Struct containing all simulation state for one wolf
public struct WolfState
{
    // Current X/Z position
    public float2 position;

    // Direction the wolf is currently moving/facing
    public float2 direction;

    // Direction used while wandering
    public float2 wanderDirection;

    // Sheep index currently being chased
    // -1 means no target
    public int targetSheep;

    // Current movement speed
    public float currentSpeed;

    // Time remaining before hunting again
    public float pauseTimer;

    // 1 = caught by a dog, 0 = active
    public byte lost;
}