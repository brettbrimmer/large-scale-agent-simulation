using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

/*
    Sheep actions:
    Turn towards flock
    Run from wolf
    Wander
    Render
*/
public class SheepSimulationSystem : MonoBehaviour
{
    // Sheep data
    private int sheepCount;
    private const float SheepSpeed = 0.5f;
    private NativeArray<SheepState> sheepCurrent; // current SheepState structs (position, velocity, speedMultiplier, lost)
    private NativeArray<SheepState> sheepNext; // updates while Sheep move concurrently in Jobs
    private NativeParallelMultiHashMap<int, int> sheepGrid; // Spatial grid cells containing sheep. Key = grid cell index, value = sheep index. Duplicate keys allowed
    private NativeList<Matrix4x4> sheepMatrices; // Transform matrices sent to GPU for rendering (pos, scale, rot)
    private NativeArray<int> sheepCellCounts; // Number of sheep inside each grid cell. Used for flocking

    // World Data
    private float pastureMin; // play area coordinate
    private float pastureMax; // play area coordinate
    private float cellSize; // Grid cell size in Unity world units
    private int gridWidth;  // Number of cells across width of grid
    private int gridCellCount; // Total grid cells   

    // Behavior data
    private const int HerdCount = 60; // Number of herds for initial spawning
    private const float HerdRadius = 15f;
    private const float SheepFleeSpeed = 4f;
    private const float HerdAttractionStrength = 0.03f; // How strongly sheep turn towards denser areas
    
    // Public data
    public NativeArray<SheepState> Current => sheepCurrent;
    public NativeArray<SheepState> Next => sheepNext;
    public NativeParallelMultiHashMap<int, int> Grid => sheepGrid;
    public NativeArray<Matrix4x4> Matrices => sheepMatrices.AsArray();
    public int RenderCount => sheepMatrices.Length;
    public int Count => sheepCount;
    public bool FlockingEnabled { get; private set; } = true;

    // Builds sheep data structures, spawns herds of sheep
    public void Initialize( float pastureMinimum, float pastureMaximum, float gridCellSize, int sheepGridWidth )
    {
        sheepCount = SimulationConfig.SheepCount;
        pastureMin = pastureMinimum;
        pastureMax = pastureMaximum;
        cellSize = gridCellSize;
        gridWidth = sheepGridWidth;
        gridCellCount = gridWidth * gridWidth;

        // Create the sheep arrays
        sheepCurrent = new NativeArray<SheepState>( sheepCount, Allocator.Persistent );
        sheepNext = new NativeArray<SheepState>( sheepCount, Allocator.Persistent );
        sheepGrid = new NativeParallelMultiHashMap<int, int>( sheepCount, Allocator.Persistent );
        sheepCellCounts = new NativeArray<int>( gridCellCount, Allocator.Persistent );

        // Create random herd centers around the pasture.
        Vector2[] herdCenters = new Vector2[HerdCount];

        for (int h = 0; h < HerdCount; h++)
        {
            herdCenters[h] = new Vector2( UnityEngine.Random.Range( pastureMin + HerdRadius, pastureMax - HerdRadius ), UnityEngine.Random.Range( pastureMin + HerdRadius, pastureMax - HerdRadius ) );
        }

        // Initialize sheeps' direction/velocity
        for (int i = 0; i < sheepCount; i++)
        {
            Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;

            // Pick a random herd
            Vector2 herdCenter = herdCenters[ UnityEngine.Random.Range( 0, HerdCount ) ];

            // Spawn somewhere irregularly around that herd center
            Vector2 spawnPosition = herdCenter + UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range( 5f, HerdRadius );

            // New sheep
            sheepCurrent[i] = new SheepState // a struct with two attributes: position, velocity
            {
                position = new float2( spawnPosition.x, spawnPosition.y ),
                velocity = new float2( randomDirection.x, randomDirection.y ),
                speedMultiplier = UnityEngine.Random.Range( 0.65f, 1.15f ), // different sheep have different speed

                lost = false // lost when a wolf captures it, then it won't draw or interact with anything
            };
        }

        // Let us know sheep were created
        Debug.Log( "Created " + sheepCurrent.Length + " sheep." );

        // Create the render list with enough capacity for every sheep
        sheepMatrices = new NativeList<Matrix4x4>( sheepCount, Allocator.Persistent );

        // Job: Build render matrices
        BuildSheepMatricesJob matrixJob = new BuildSheepMatricesJob { sheepData = sheepCurrent, matrices = sheepMatrices.AsParallelWriter() };
        JobHandle matrixHandle = matrixJob.ScheduleParallel( sheepCount, 64, default );
        matrixHandle.Complete();
    }


    public void SimulateMovement( float deltaTime, NativeArray<WolfState> wolfStates, NativeParallelMultiHashMap<int, int> wolfGrid, float wolfFleeRadius )
    {
        // Job: Build sheep spatial grid
        sheepGrid.Clear();
        BuildSheepGridJob gridJob = new BuildSheepGridJob { sheepData = sheepCurrent, gridWriter = sheepGrid.AsParallelWriter(), pastureMin = pastureMin, cellSize = cellSize, gridWidth = gridWidth };
        JobHandle gridHandle = gridJob.ScheduleParallel( sheepCount, 64, default );
        gridHandle.Complete();

        // Record how many sheep are in each cell.
        // Sheep cell population counts are only needed for flocking
        if (FlockingEnabled)
        {
            for ( int cellKey = 0; cellKey < gridCellCount; cellKey++ )
                sheepCellCounts[cellKey] = sheepGrid.CountValuesForKey( cellKey );
        }

        // Job: Sheep movement
        MoveSheepJob moveJob =
            new MoveSheepJob
            {
                currentSheepData = sheepCurrent,
                nextSheepData = sheepNext,
                sheepCellCounts = sheepCellCounts,
                sheepGrid = this.sheepGrid,
                cellSize = cellSize,
                gridWidth = gridWidth,
                herdAttractionStrength = HerdAttractionStrength,
                flockingEnabled = FlockingEnabled ? (byte)1 : (byte)0,
                wolfStates = wolfStates,
                wolfGrid = wolfGrid,
                wolfFleeRadius = wolfFleeRadius,
                sheepFleeSpeed = SheepFleeSpeed,
                deltaTime = deltaTime,
                sheepSpeed = SheepSpeed,
                pastureMin = pastureMin,
                pastureMax = pastureMax
            };


        // Run the sheep updates across multiple worker threads.
        // Unity recommends 32-128 batch size depending on complexity.
        // Depends on gridHandle, so the Sheep movement job waits until the spatial grid job finishes
        JobHandle moveHandle = moveJob.ScheduleParallel( sheepCount, 64, gridHandle );

        // We need the new positions before preparing the render matrices,
        // so wait here until the movement job has finished
        moveHandle.Complete();
    }

    /*
        Swap sheepCurrent and sheepNext to update sheepCurrent with updated data.
        These are array references, so we can't just swap sheepNext into sheepCurrent
        and be done, because they'd be referencing the same array
    */
    public void FinishSimulationTick()
    {
        // sheepNext now contains the newly calculated simulation state,
        // so it becomes sheepCurrent. Swapping references
        NativeArray<SheepState> temp = sheepCurrent;

        sheepCurrent = sheepNext;
        sheepNext = temp;

        // Start a fresh render list for this tick
        sheepMatrices.Clear();

        // Build rendering matrices for active sheep
        BuildSheepMatricesJob matrixJob = new BuildSheepMatricesJob { sheepData = sheepCurrent, matrices = sheepMatrices.AsParallelWriter() };

        JobHandle matrixHandle = matrixJob.ScheduleParallel( sheepCount, 64, default );

        matrixHandle.Complete();
    }

    public void ToggleFlocking()
    {
        FlockingEnabled = !FlockingEnabled;
    }

    private void OnDestroy()
    {
        // NativeArrays must be released from memory manually
        if (sheepCurrent.IsCreated)
            sheepCurrent.Dispose();

        if (sheepNext.IsCreated)
            sheepNext.Dispose();

        if (sheepGrid.IsCreated)
            sheepGrid.Dispose();

        if (sheepCellCounts.IsCreated)
            sheepCellCounts.Dispose();

        if (sheepMatrices.IsCreated)
            sheepMatrices.Dispose();
    }
}