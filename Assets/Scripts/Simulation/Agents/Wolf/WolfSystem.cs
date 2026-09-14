using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

/*
    Wolves wander around the pasture. When they see a Sheep, they chase
    it and capture the Sheep (it's lost) when they get close enough. When
    a dog sees a Wolf, it starts chasing the Wolf, and the Wolf runs away
    from it.

    Wolf actions:
    -Run from Dog (if being chased)
    -Rest after capturing a Sheep
    -Find a nearby sheep
    -Wander around if no sheep
    -Capture a sheep
    -Accelerate towards a sheep
    -RENDER WOLVES with Jobs
*/
public class WolfSystem : MonoBehaviour
{
    // Wolf data
    private int wolfCount;
    private const float WolfSpeed = 6f;
    private NativeArray<WolfState> wolfStates; // WolfState structs
    private NativeParallelMultiHashMap<int, int> wolfGrid; // Spatial grid cells containing Wolves. Key = grid cell index, value = wolf index. Duplicate keys allowed
    private NativeList<Matrix4x4> wolfMatrices; // Transform matrices sent to GPU for rendering (pos, scale, rot)
    private NativeArray<int> sheepCatchRequests; // Prevents concurrent writes for Wolves catching Sheep
    

    // Behavior data
    private const float WolfStartSpeed = 2f; // Wolves start slower and accelerate during a chase
    private const float WolfAcceleration = 4f;
    private const float WolfPauseMin = 0.5f; // Wolves pause briefly after catching a sheep
    private const float WolfPauseMax = 2f;    
    private const float WolfCatchDistance = 2.75f; // Distance wolf needs to be to sheep to catch
    private const float WolfFleeRadius = 10f; // Sheep notice wolves within this distance
    private const float WolfWanderSpeed = 1.5f;
    private const float DogFleeRadius = 10f; // Wolves flee from Dogs within this distance

    // World data
    private float pastureMin;
    private float pastureMax;
    private float gridCellSize;
    private int gridWidth;

    // Public data
    public NativeArray<WolfState> States => wolfStates;
    // Each grid is X by X, so may contain multiple wolves. key = grid's cell number, value = index of zero or more wolves
    public NativeParallelMultiHashMap<int, int> Grid => wolfGrid;
    public float GridCellSize => gridCellSize;
    public int GridWidth => gridWidth; 
    // Sheep need this value for their flee behavior.
    public float FleeRadius => WolfFleeRadius;
    public NativeArray<Matrix4x4> Matrices => wolfMatrices.AsArray();
    public int RenderCount => wolfMatrices.Length;
    public int Count => wolfCount;

    public void Initialize( float pastureMinimum, float pastureMaximum, float sheepGridCellSize, int sheepGridWidth )
    {
        wolfCount = SimulationConfig.WolfCount;

        pastureMin = pastureMinimum;
        pastureMax = pastureMaximum;
        gridCellSize = sheepGridCellSize;
        gridWidth = sheepGridWidth;

        // Create wolf data (NativeArray of size WolfCount that stores float (X,Z) coordinates), Persistent
        wolfStates = new NativeArray<WolfState>( wolfCount, Allocator.Persistent );
        // Which wolves are in which spatial grids. key = grid index, value = wolf index. duplicate keys allowed
        wolfGrid = new NativeParallelMultiHashMap<int, int>( wolfCount, Allocator.Persistent );
        wolfMatrices = new NativeList<Matrix4x4>( wolfCount, Allocator.Persistent );
        sheepCatchRequests = new NativeArray<int>(wolfCount, Allocator.Persistent);

        for (int i = 0; i < wolfCount; i++)
        {
            // Start each wolf somewhere random in the pasture
            float2 startPosition = new float2(
                UnityEngine.Random.Range(pastureMin, pastureMax),
                UnityEngine.Random.Range(pastureMin, pastureMax)
            );

            // Create its starting render transform
            wolfMatrices.Add(
                Matrix4x4.TRS(
                    new Vector3(startPosition.x, 0.35f, startPosition.y),
                    Quaternion.identity,
                    Vector3.one
                )
            );

            Vector2 wanderDirection = UnityEngine.Random.insideUnitCircle.normalized;
            wolfStates[i] = new WolfState
            {
                position = startPosition,
                direction = new float2( wanderDirection.x, wanderDirection.y ),
                wanderDirection = new float2( wanderDirection.x, wanderDirection.y ),
                targetSheep = -1,
                currentSpeed = WolfStartSpeed,
                pauseTimer = 0f,
                lost = 0
            };
        }
    }

    /*
        Simulate is called by HerdSimulationManager at 30Hz
        - sheepCurrent Lets the Wolf read the sheep's position and whether that Sheep is currently "lost"
        - sheepNext Lets the Wolf mark a Sheep it caught as "lost"
        - sheepGrid Lets the Wolf find nearby sheep
        - DogSystem Lets the Wolf see if a Dog is chasing it, and which Dog is closest (so it can run directly away from it)
    
        Wolf Actions:

        -Run from Dog (if being chased)
        -Rest after capturing a Sheep
        -Find a nearby sheep
        -Wander around if no sheep
        -Capture a sheep
        -Accelerate towards a sheep
        -RENDER WOLVES with Jobs
    */
    public void Simulate(
        float deltaTime,
        NativeArray<SheepState> sheepCurrent,
        NativeArray<SheepState> sheepNext,
        NativeParallelMultiHashMap<int, int> sheepGrid,
        DogSystem DogSystem
    )
    {
        // Job: Wolf movement
        MoveWolvesJob moveJob = new MoveWolvesJob
            {
                wolfStates = wolfStates,

                sheepCurrent = sheepCurrent,
                sheepGrid = sheepGrid,

                dogStates = DogSystem.States,
                dogGrid = DogSystem.Grid,

                sheepCatchRequests = sheepCatchRequests,
                deltaTime = deltaTime,

                pastureMin = pastureMin,
                pastureMax = pastureMax,

                gridCellSize = gridCellSize,
                gridWidth = gridWidth,

                wolfSpeed = WolfSpeed,
                wolfStartSpeed = WolfStartSpeed,
                wolfAcceleration = WolfAcceleration,
                wolfCatchDistance = WolfCatchDistance,
                wolfWanderSpeed = WolfWanderSpeed,
                dogFleeRadius = DogFleeRadius
            };
        JobHandle moveHandle = moveJob.ScheduleParallel( wolfCount, 64, default );
        moveHandle.Complete();

        // After wolf movement, resolve sheep captures (don't try to write concurrently during Job)
        for (int i = 0; i < wolfCount; i++)
        {
            WolfState wolf = wolfStates[i];

            if (wolf.lost != 0)
                continue;

            // Wolf reached a Sheep
            int caughtSheepIndex =
                sheepCatchRequests[i];

            if (caughtSheepIndex != -1)
            {
                // Another Wolf may have gotten there first
                if (!sheepCurrent[caughtSheepIndex].lost)
                {
                    SheepState caughtSheep = sheepCurrent[caughtSheepIndex];

                    caughtSheep.lost = true;

                    sheepCurrent[caughtSheepIndex] = caughtSheep;

                    sheepNext[caughtSheepIndex] = caughtSheep;

                    wolf.pauseTimer = UnityEngine.Random.Range( WolfPauseMin, WolfPauseMax );

                    wolf.currentSpeed = WolfStartSpeed;

                    Vector2 newDirection = UnityEngine.Random.insideUnitCircle.normalized;

                    wolf.wanderDirection = new float2( newDirection.x, newDirection.y );
                }

                wolf.targetSheep = -1;

                wolfStates[i] = wolf;

                continue;
            }

            // Wolf hit pasture edge, so redirect
            if ( wolf.targetSheep == -1 && ( wolf.position.x <= pastureMin || wolf.position.x >= pastureMax || wolf.position.y <= pastureMin || wolf.position.y >= pastureMax ) )
            {
                Vector2 newDirection = UnityEngine.Random.insideUnitCircle.normalized;
                wolf.wanderDirection = new float2( newDirection.x, newDirection.y );

                wolfStates[i] = wolf;
            }
        }

        // Job: Build Wolf render matrices
        wolfMatrices.Clear(); // Start a fresh render list for this tick
        BuildWolfMatricesJob matrixJob = new BuildWolfMatricesJob { wolfStates = wolfStates, matrices = wolfMatrices.AsParallelWriter() };
        JobHandle matrixHandle = matrixJob.ScheduleParallel( wolfCount, 64, default );
        matrixHandle.Complete();
    }

    // Store each Wolf in its wolfGrid cell. (Each cell is a 4x4 grid.)
    public void BuildGrid()
    {
        // Remove the previous tick's entries but reuse the allocated memory
        wolfGrid.Clear();

        // Store each wolf in the wolfGrid in its current cell
        for (int i = 0; i < wolfCount; i++)
        {
            WolfState wolf = wolfStates[i];

            // Wolf has already been caught
            if (wolf.lost != 0)
                continue;

            float2 wolfPosition = wolf.position;
            
            // Convert (X,Z) position to grid cell index
            int cellKey = SpatialGrid.GetCellKey( wolfPosition, pastureMin, gridCellSize, gridWidth );

            wolfGrid.Add(cellKey, i);
        }
    }

    /*
        Marks a wolf lost, which means it will not be available
        to be chased by Dogs, and it will not be added to the
        render Job for wolves
    */
    public void MarkWolfLost(int wolfIndex)
    {
        WolfState wolf = wolfStates[wolfIndex];

        wolf.lost = 1;
        wolf.targetSheep = -1;

        wolfStates[wolfIndex] = wolf;
    }

    private void OnDestroy()
    {
        // NativeArrays must be released from memory manually

        if (wolfGrid.IsCreated)
            wolfGrid.Dispose();

        if (wolfMatrices.IsCreated)
            wolfMatrices.Dispose();

        if (wolfStates.IsCreated)
            wolfStates.Dispose();

        if (sheepCatchRequests.IsCreated)
            sheepCatchRequests.Dispose();
    }
}