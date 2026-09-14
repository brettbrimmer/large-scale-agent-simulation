using Unity.Mathematics;

// Struct containing the simulation state for one dog
public struct DogState
{
    // Current X/Z position
    public float2 position;

    // Direction the dog is currently moving/facing
    public float2 direction;

    // Direction used while wandering
    public float2 wanderDirection;

    // Wolf index currently being chased
    // -1 means no target
    public int targetWolf;
}