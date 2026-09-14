using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

[BurstCompile]
internal struct BuildDogMatricesJob : IJobFor
{
    [ReadOnly]
    public NativeArray<DogState> dogStates;

    [WriteOnly]
    public NativeArray<Matrix4x4> matrices;

    public void Execute(int index)
    {
        DogState dog = dogStates[index];

        // Get attributes
        float2 direction = math.normalizesafe(dog.direction); // normalize to direction only (not vector w/ length)
        Vector3 position = new Vector3( dog.position.x, 0.35f, dog.position.y );
        Quaternion rotation = Quaternion.LookRotation( new Vector3(direction.x, 0f, direction.y), Vector3.up );

        // Scale dog model up, it's extremely small
        Vector3 scale = Vector3.one * 70f;

        // Build the dog's position + rotation + scale transform for render matrix
        Matrix4x4 matrix = Matrix4x4.TRS( position, rotation, scale );

        matrices[index] = matrix;
    }
}

// Builds the dog spatial grid
[BurstCompile]
internal struct BuildDogGridJob : IJobFor
{
    [ReadOnly]
    public NativeArray<DogState> dogStates;

    public NativeParallelMultiHashMap<int, int>.ParallelWriter gridWriter;

    public float pastureMin;
    public float cellSize;
    public int gridWidth;

    public void Execute(int index)
    {
        int cellKey = SpatialGrid.GetCellKey( dogStates[index].position, pastureMin, cellSize, gridWidth );

        gridWriter.Add(cellKey, index);
    }
}

// Moves Dogs and marks Wolves to be caught after Job ends
[BurstCompile]
internal struct MoveDogsJob : IJobFor
{
    public NativeArray<DogState> dogStates;

    [ReadOnly]
    public NativeArray<WolfState> wolfStates;

    [ReadOnly]
    public NativeParallelMultiHashMap<int, int> wolfGrid;

    [WriteOnly]
    public NativeArray<int> wolfCatchRequests;
    public float catchDistance;
    public float deltaTime;
    public float pastureMin;
    public float pastureMax;
    public float gridCellSize;
    public int gridWidth;
    public float wanderSpeed;
    public float chaseSpeed;
    public float sightRadius;

    // Tells Dogs to hunt Wolves or wander
    public void Execute(int index)
    {
        // Default: this Dog caught nothing this tick
        wolfCatchRequests[index] = -1;

        DogState dog = dogStates[index];
        float2 dogPosition = dog.position;
        int targetWolf = dog.targetWolf;

        // No target, so search nearby wolf-grid cells
        if (targetWolf == -1)
        {
            targetWolf = FindNearbyWolf( dogPosition );
            dog.targetWolf = targetWolf;
        }

        // Chase wolf
        if (targetWolf != -1)
        {
            float2 toWolf = wolfStates[targetWolf].position - dogPosition;
            float2 moveDirection = math.normalizesafe(toWolf);

            dog.direction = moveDirection;
            dogPosition += moveDirection * chaseSpeed * deltaTime;

            float distanceSquared = math.lengthsq(wolfStates[targetWolf].position - dogPosition);

            // Dog caught wolf
            if (distanceSquared < catchDistance * catchDistance)
                wolfCatchRequests[index] = targetWolf;
        }

        // No wolf nearby, so wander
        else
        {
            dog.direction = dog.wanderDirection;
            dogPosition += dog.wanderDirection * wanderSpeed * deltaTime;
            dogPosition = math.clamp( dogPosition, new float2(pastureMin, pastureMin), new float2(pastureMax, pastureMax) );
        }

        dog.position = dogPosition;

        dogStates[index] = dog;
    }

    // Checks nearby spatial grid cells for Wolf, returns index of
    // Wolf if found
    private int FindNearbyWolf( float2 position )
    {
        int2 cell = SpatialGrid.GetCellCoordinates( position, pastureMin, gridCellSize, gridWidth );

        int cellX = cell.x;
        int cellY = cell.y;

        int searchCellRadius = (int)math.ceil( sightRadius / gridCellSize );

        float closestDistanceSquared = sightRadius * sightRadius;

        int closestWolf = -1;

        for ( int offsetY = -searchCellRadius; offsetY <= searchCellRadius; offsetY++ )
        {
            for ( int offsetX = -searchCellRadius; offsetX <= searchCellRadius; offsetX++ )
            {
                int checkX = cellX + offsetX;
                int checkY = cellY + offsetY;

                if ( checkX < 0 || checkX >= gridWidth || checkY < 0 || checkY >= gridWidth )
                    continue;

                int cellKey = SpatialGrid.GetCellKey( new int2(checkX, checkY), gridWidth );

                // Check every wolf in this grid cell
                foreach (int wolfIndex in wolfGrid.GetValuesForKey(cellKey))
                {
                    float distanceSquared = math.lengthsq( wolfStates[wolfIndex].position - position );

                    if (distanceSquared < closestDistanceSquared)
                    {
                        closestDistanceSquared = distanceSquared;
                        closestWolf = wolfIndex;
                    }
                }
            }
        }

        return closestWolf;
    }
}