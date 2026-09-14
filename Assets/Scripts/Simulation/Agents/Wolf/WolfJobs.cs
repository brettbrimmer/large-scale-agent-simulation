using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

[BurstCompile]
internal struct BuildWolfMatricesJob : IJobFor
{
    [ReadOnly]
    public NativeArray<WolfState> wolfStates;

    public NativeList<Matrix4x4>.ParallelWriter matrices;

    public void Execute(int index)
    {
        // Lost wolves are not submitted for rendering.
        WolfState wolf = wolfStates[index];

        if (wolf.lost != 0)
            return;

        float2 direction = math.normalizesafe(wolf.direction);

        // Position of this wolf in the 3D world
        Vector3 position = new Vector3( wolf.position.x, 0.35f, wolf.position.y );

        // Rotate the wolf so +Z faces its movement direction.
        Quaternion rotation = Quaternion.LookRotation( new Vector3(direction.x, 0f, direction.y), Vector3.up );

        // Wolf model is authored extremely small, so scale it up
        Vector3 scale = Vector3.one * 70f;

        // Build the wolf's position + rotation + scale transform
        Matrix4x4 matrix = Matrix4x4.TRS( position, rotation, scale );

        matrices.AddNoResize(matrix);
    }
}

[BurstCompile]
internal struct MoveWolvesJob : IJobFor
{
    public NativeArray<WolfState> wolfStates;

    [ReadOnly]
    public NativeArray<SheepState> sheepCurrent;

    [ReadOnly]
    public NativeParallelMultiHashMap<int, int> sheepGrid;

    [ReadOnly]
    public NativeArray<DogState> dogStates;

    [ReadOnly]
    public NativeParallelMultiHashMap<int, int> dogGrid;

    [WriteOnly]
    public NativeArray<int> sheepCatchRequests;

    public float deltaTime;
    public float dogFleeRadius;

    public float pastureMin;
    public float pastureMax;

    public float gridCellSize;
    public int gridWidth;

    public float wolfSpeed;
    public float wolfStartSpeed;
    public float wolfAcceleration;
    public float wolfCatchDistance;
    public float wolfWanderSpeed;


    public void Execute(int index)
    {
        // Default: this wolf caught nothing this tick
        sheepCatchRequests[index] = -1;

        WolfState wolf = wolfStates[index];

        // Wolf has already been caught by a Dog
        if (wolf.lost != 0)
            return;

        float2 wolfPosition = wolf.position;

        // RUN FROM NEARBY DOG
        int nearbyDog = FindNearbyDog(wolfPosition);

        if (nearbyDog != -1)
        {
            float2 awayFromDog = math.normalizesafe( wolfPosition - dogStates[nearbyDog].position );

            wolf.direction = awayFromDog;

            wolfPosition += awayFromDog * wolfSpeed * deltaTime;

            wolf.position = math.clamp( wolfPosition, new float2(pastureMin, pastureMin), new float2(pastureMax, pastureMax) );

            wolfStates[index] = wolf;
            return;
        }

        // REST AFTER CATCHING A SHEEP
        if (wolf.pauseTimer > 0f)
        {
            wolf.pauseTimer -= deltaTime;

            wolfStates[index] = wolf;
            return;
        }

        // CHECK CURRENT TARGET
        int targetSheep = wolf.targetSheep;

        // Another wolf may already have caught this Sheep
        if ( targetSheep != -1 && sheepCurrent[targetSheep].lost )
        {
            targetSheep = -1;
            wolf.targetSheep = -1;
        }


        // FIND A SHEEP
        if (targetSheep == -1)
        {
            targetSheep = FindNearbySheep(wolfPosition);

            wolf.targetSheep = targetSheep;

            // New chase starts slowly
            if (targetSheep != -1) wolf.currentSpeed = wolfStartSpeed;
        }


        // WANDER
        if (targetSheep == -1)
        {
            wolf.direction = wolf.wanderDirection;
            wolfPosition += wolf.wanderDirection * wolfWanderSpeed * deltaTime;
            wolf.position = math.clamp( wolfPosition, new float2(pastureMin, pastureMin), new float2(pastureMax, pastureMax) );

            wolfStates[index] = wolf;
            return;
        }


        // CHASE SHEEP
        float2 toTarget = sheepCurrent[targetSheep].position - wolfPosition;

        // Wolf reached the Sheep.
        // Don't modify SheepState from this parallel Job.
        if ( math.lengthsq(toTarget) < wolfCatchDistance * wolfCatchDistance )
        {
            sheepCatchRequests[index] = targetSheep;

            wolfStates[index] = wolf;
            return;
        }

        wolf.currentSpeed = math.min( wolf.currentSpeed + wolfAcceleration * deltaTime, wolfSpeed );
        float2 direction = math.normalizesafe(toTarget);
        wolf.direction = direction;
        wolfPosition += direction * wolf.currentSpeed * deltaTime;
        wolf.position = math.clamp( wolfPosition, new float2(pastureMin, pastureMin), new float2(pastureMax, pastureMax) );

        wolfStates[index] = wolf;
    }


    /*
        Checks 3x3 spatial grid area for Sheep, returns index of
        Sheep if found. Skips lost sheep
    */
    private int FindNearbySheep(float2 wolfPosition)
    {
        int2 cell = SpatialGrid.GetCellCoordinates( wolfPosition, pastureMin, gridCellSize, gridWidth );

        int closestSheep = -1;
        float closestDistanceSquared = float.MaxValue;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int cellX = cell.x + offsetX;
                int cellY = cell.y + offsetY;

                if ( cellX < 0 || cellX >= gridWidth || cellY < 0 || cellY >= gridWidth )
                {
                    continue;
                }

                int cellKey = SpatialGrid.GetCellKey( new int2(cellX, cellY), gridWidth );

                foreach ( int sheepIndex in sheepGrid.GetValuesForKey(cellKey) ) {
                    SheepState sheep = sheepCurrent[sheepIndex];

                    if (sheep.lost)
                        continue;

                    float distanceSquared = math.lengthsq( sheep.position - wolfPosition );

                    if ( distanceSquared < closestDistanceSquared )
                    {
                        closestDistanceSquared = distanceSquared;

                        closestSheep = sheepIndex;
                    }
                }
            }
        }

        return closestSheep;
    }


    // Checks within fleeRadius for Dog, returns index of Dog if found
    private int FindNearbyDog(float2 wolfPosition)
    {
        int2 cell = SpatialGrid.GetCellCoordinates( wolfPosition, pastureMin, gridCellSize, gridWidth );

        int searchCellRadius = (int)math.ceil( dogFleeRadius / gridCellSize );

        int closestDog = -1;

        float closestDistanceSquared = dogFleeRadius * dogFleeRadius;

        for ( int offsetY = -searchCellRadius; offsetY <= searchCellRadius; offsetY++ )
        {
            for ( int offsetX = -searchCellRadius; offsetX <= searchCellRadius; offsetX++ )
            {
                int cellX = cell.x + offsetX;
                int cellY = cell.y + offsetY;

                if ( cellX < 0 || cellX >= gridWidth || cellY < 0 || cellY >= gridWidth )
                {
                    continue;
                }

                int cellKey = SpatialGrid.GetCellKey( new int2(cellX, cellY), gridWidth );

                foreach ( int dogIndex in dogGrid.GetValuesForKey(cellKey) )
                {
                    float distanceSquared = math.lengthsq( dogStates[dogIndex].position - wolfPosition );

                    if ( distanceSquared < closestDistanceSquared )
                    {
                        closestDistanceSquared = distanceSquared;

                        closestDog = dogIndex;
                    }
                }
            }
        }

        return closestDog;
    }
}