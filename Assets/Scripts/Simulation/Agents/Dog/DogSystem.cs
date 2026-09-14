using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

/*
    Dog behavior:
    -Wander
    -Chase Wolves
    -Capture Wolves
*/
public class DogSystem : MonoBehaviour
{
    // Dog data
    private int DogCount;
    public int Count => DogCount;
    private NativeArray<DogState> dogStates; // DogState structs
    private NativeParallelMultiHashMap<int, int> dogGrid; // Spatial grid cells containing Dogs. Key = grid cell index, value = dog index. Duplicate keys allowed
    private NativeArray<Matrix4x4> dogMatrices; // Transform matrices sent to GPU for rendering (pos, scale, rot)
    private NativeArray<int> wolfCatchRequests; // Prevents concurrent writes for Dogs catching Wolves

    // Behavior data
    private const float DogWanderSpeed = 2.5f; // no chasing
    private const float DogChaseSpeed = 8f; // is chasing
    private const float DogSightRadius = 30f; // distance to see wolves
    private const float DogCatchDistance = 3.65f; // distance to catch wolves

    // World data
    private float pastureMin;
    private float pastureMax;

    // Public data
    public NativeArray<DogState> States => dogStates;
    public NativeParallelMultiHashMap<int, int> Grid => dogGrid;
    public NativeArray<Matrix4x4> Matrices => dogMatrices;

    // Initialize Dogs and spawn them in random positions in the pasture
    public void Initialize( float pastureMinimum, float pastureMaximum )
    {
        DogCount = SimulationConfig.DogCount;

        pastureMin = pastureMinimum;
        pastureMax = pastureMaximum;

        dogStates = new NativeArray<DogState>( DogCount, Allocator.Persistent );
        wolfCatchRequests = new NativeArray<int>( DogCount, Allocator.Persistent );
        dogMatrices = new NativeArray<Matrix4x4>( DogCount, Allocator.Persistent );

        // Create Dogs at random positions in the pasture
        for (int i = 0; i < DogCount; i++)
        {
            float2 startPosition = new float2( UnityEngine.Random.Range(pastureMin, pastureMax), UnityEngine.Random.Range(pastureMin, pastureMax) );
            Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
            float2 wanderDirection = new float2( randomDirection.x, randomDirection.y );
            dogStates[i] = new DogState { position = startPosition, direction = wanderDirection, wanderDirection = wanderDirection, targetWolf = -1 };
            dogMatrices[i] = Matrix4x4.TRS( new Vector3(startPosition.x, 0.35f, startPosition.y), Quaternion.identity, Vector3.one );
        }

        dogGrid = new NativeParallelMultiHashMap<int, int>( DogCount, Allocator.Persistent );
    }

    // Moves Dogs, catches Wolves, update Dog grid, update Dog render matrices
    public void Simulate(float deltaTime, WolfSystem wolfSystem)
    {
        NativeArray<WolfState> wolfStates = wolfSystem.States;

        // Before moving: Stop targeting Wolves that were already caught,
        // and give wandering Dogs a new direction when they reach a pasture edge
        for (int i = 0; i < DogCount; i++)
        {
            DogState dog = dogStates[i];

            int targetWolf = dog.targetWolf;

            if (targetWolf != -1 && wolfStates[targetWolf].lost != 0)
                dog.targetWolf = -1;

            // Pick a new wandering direction after hitting an edge
            if ( dog.targetWolf == -1 && ( dog.position.x <= pastureMin || dog.position.x >= pastureMax || dog.position.y <= pastureMin || dog.position.y >= pastureMax ) )
                dog.wanderDirection = RandomDirection();

            dogStates[i] = dog;
        }

        // Job: Move all Dogs
        MoveDogsJob moveJob = new MoveDogsJob
        {
            dogStates = dogStates,
            wolfStates = wolfStates,
            wolfGrid = wolfSystem.Grid,
            wolfCatchRequests = wolfCatchRequests,
            catchDistance = DogCatchDistance,
            deltaTime = deltaTime,
            pastureMin = pastureMin,
            pastureMax = pastureMax,
            gridCellSize = wolfSystem.GridCellSize,
            gridWidth = wolfSystem.GridWidth,
            wanderSpeed = DogWanderSpeed,
            chaseSpeed = DogChaseSpeed,
            sightRadius = DogSightRadius
        };
        JobHandle dogHandle = moveJob.ScheduleParallel( DogCount, 64, default );
        dogHandle.Complete();

        // Resolve Wolf catches after Dog movement job has finished
        for (int i = 0; i < DogCount; i++)
        {
            int caughtWolf = wolfCatchRequests[i];

            if (caughtWolf == -1)
                continue;

            DogState dog = dogStates[i];

            // Another Dog may have already caught this Wolf
            if (wolfStates[caughtWolf].lost == 0)
                wolfSystem.MarkWolfLost(caughtWolf);

            dog.targetWolf = -1;
            dog.wanderDirection = RandomDirection();

            dogStates[i] = dog;
        }

        // Job: Rebuild the Dog spatial grid using updated positions
        dogGrid.Clear();
        BuildDogGridJob gridJob = new BuildDogGridJob { dogStates = dogStates, gridWriter = dogGrid.AsParallelWriter(), pastureMin = pastureMin, cellSize = wolfSystem.GridCellSize, gridWidth = wolfSystem.GridWidth };
        JobHandle gridHandle = gridJob.ScheduleParallel( DogCount, 64, default );
        gridHandle.Complete();

        // Job: Build GPU transform matrices for rendering each Dog
        BuildDogMatricesJob matrixJob = new BuildDogMatricesJob { dogStates = dogStates, matrices = dogMatrices };
        JobHandle matrixHandle = matrixJob.ScheduleParallel( DogCount, 64, default );
        matrixHandle.Complete();
    }

    // Returns a random direction as a float2
    private float2 RandomDirection()
    {
        Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;

        return new float2( direction.x, direction.y );
    }

    private void OnDestroy()
    {
        if (dogStates.IsCreated)
            dogStates.Dispose();

        if (dogGrid.IsCreated)
            dogGrid.Dispose();

        if (dogMatrices.IsCreated)
            dogMatrices.Dispose();

        if (wolfCatchRequests.IsCreated)
            wolfCatchRequests.Dispose();
    }
}