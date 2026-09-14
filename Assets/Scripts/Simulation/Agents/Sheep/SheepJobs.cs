using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

    [BurstCompile]
    internal struct BuildSheepMatricesJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<SheepState> sheepData; // position, velocity, lost

        // Render list containing only sheep that are still active
        public NativeList<Matrix4x4>.ParallelWriter matrices;

        public void Execute(int index)
        {
            SheepState sheep = sheepData[index];

            // Lost sheep are not submitted for rendering
            if (sheep.lost)
                return;

            float2 direction = math.normalizesafe(sheep.velocity);

            // Position of this sheep in the 3D world
            Vector3 position = new Vector3( sheep.position.x, 0.25f, sheep.position.y );

            // Rotate the sheep so its forward (+Z) direction
            // points in the direction it is moving
            Quaternion rotation = Quaternion.LookRotation( new Vector3(direction.x, 0f, direction.y), Vector3.up );

            // Sheep size
            Vector3 scale = Vector3.one * 0.8f;

            // Build the sheep's position + rotation + scale transform
            Matrix4x4 matrix = Matrix4x4.TRS( position, rotation, scale );

            matrices.AddNoResize(matrix);
        }
    }

    /*
        // Runs the movement calculation for all sheep.
        // The job will run the Execute(index) once for each sheep,
        // so different CPU cores can process different sheep at the same time.
        // Notice this is a struct and the attributes are filled in on construction

        // Sheep movement:
        // The sheep checks nearby spatial-hash cells and is influenced when
        // it sees Wolf or a dense group of Sheep nearby
        // Receive vector influence from nearby wolf (point direction away from wolf)
        // Receive vector influence from nearby flock (turn towards flock)
        // Move in current direction after wolf/flock influence is applied (if any)
    */
    [BurstCompile] // compile this struct to machine code
    internal struct MoveSheepJob : IJobFor
    {
        // Current simulation state that jobs READ from
        [ReadOnly]
        public NativeArray<SheepState> currentSheepData; // stores all sheeps' position, velocity

        // Next simulation state: write into this
        [WriteOnly]
        public NativeArray<SheepState> nextSheepData; // same

        // Current positions of all wolves
        [ReadOnly]
        public NativeArray<WolfState> wolfStates;

        // Spatial grid containing the wolves
        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> wolfGrid;

        // Population of every spatial-grid cell
        [ReadOnly]
        public NativeArray<int> sheepCellCounts;

        // Spatial grid containing the sheep
        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> sheepGrid;

        private const float SeparationRadius = 1.25f;
        private const float SeparationStrength = 0.15f; // 0.5 for strong separation

        public float cellSize;
        public int gridWidth;
        public float herdAttractionStrength;
        public byte flockingEnabled;

        // Neighboring cell must have at least 10% more sheep
        // for sheep to turn towards it
        private const float HerdAttractionThreshold = 1.10f;
        // Sheep won't want to leave for another Herd if this many sheep nearby
        private const int HerdSatisfiedCount = 300;
        // Distance sheep reacts to wolf, and speed sheep flees
        public float wolfFleeRadius;
        public float sheepFleeSpeed;

        public float deltaTime;
        public float sheepSpeed;
        public float pastureMin;
        public float pastureMax;


        // Unity runs this once for every sheep index,
        // distributing the work across worker threads
        public void Execute(int index)
        {
            // Copy this sheep's state. We will modify it, then write the result into SheepNext
            SheepState thisSheep = currentSheepData[index]; // SheepState is a struct with only state, velocity

            if (thisSheep.lost)
            {
                nextSheepData[index] = thisSheep;

                return;
            }

            // Move the sheep
            float currentSpeed = sheepSpeed;
            // Find this sheep's spatial-grid cell
            int2 sheepCell = SpatialGrid.GetCellCoordinates( thisSheep.position, pastureMin, cellSize, gridWidth );

            int sheepCellX = sheepCell.x;
            int sheepCellY = sheepCell.y;

            // Check whether this sheep is fleeing from a wolf
            bool fleeing = FindClosestWolf( thisSheep.position, sheepCellX, sheepCellY, out float2 closestWolfPosition );

            // Flee from a nearby wolf
            if (fleeing)
            {
                float2 fleeDirection = math.normalizesafe(thisSheep.position - closestWolfPosition);

                // Turn toward the flee direction
                thisSheep.velocity = math.normalizesafe( math.lerp(thisSheep.velocity, fleeDirection, 0.25f) );

                currentSpeed = sheepFleeSpeed * thisSheep.speedMultiplier;
            }
            else
            {
                // Each sheep only checks flocking/separation once every 15 simulation ticks.
                // Different sheep check on different ticks so the work is spread out.
                if ( flockingEnabled != 0 )
                {
                    ApplyHerding(ref thisSheep, sheepCellX, sheepCellY);

                    ApplySeparation(ref thisSheep, index, sheepCellX, sheepCellY);
                }
            }

            // Move the sheep
            thisSheep.position += thisSheep.velocity * currentSpeed * deltaTime;

            // Bounce off left/right pasture boundaries
            if ( thisSheep.position.x < pastureMin )
            {
                thisSheep.position.x = pastureMin;
                thisSheep.velocity.x *= -1f; // flip direction
            }
            else if ( thisSheep.position.x > pastureMax )
            {
                thisSheep.position.x = pastureMax;
                thisSheep.velocity.x *= -1f; // flip direction
            }

            // Bounce off top/bottom pasture boundaries
            if ( thisSheep.position.y < pastureMin )
            {
                thisSheep.position.y = pastureMin;
                thisSheep.velocity.y *= -1f; // flip direction
            }
            else if ( thisSheep.position.y > pastureMax )
            {
                thisSheep.position.y = pastureMax;
                thisSheep.velocity.y *= -1f; // flip direction
            }

            // Write the updated state into the NEXT array
            nextSheepData[index] =
                thisSheep;
        }

        /*
            Uses the spatial hash grid to check nearby cells for wolves,
            then returns the position of the closest wolf within flee range.
        */
        private bool FindClosestWolf( float2 sheepPosition, int sheepCellX, int sheepCellY, out float2 closestWolfPosition )
        {
            float closestWolfDistanceSquared = float.MaxValue; closestWolfPosition = float2.zero;

            // Flee radius is larger than one grid cell, so search enough
            // neighboring cells to fully cover the flee radius
            int wolfSearchRadius = (int)math.ceil(wolfFleeRadius / cellSize);

            for (int offsetY = -wolfSearchRadius; offsetY <= wolfSearchRadius; offsetY++)
            {
                for (int offsetX = -wolfSearchRadius; offsetX <= wolfSearchRadius; offsetX++)
                {
                    int checkX = sheepCellX + offsetX;
                    int checkY = sheepCellY + offsetY;

                    if ( checkX < 0 || checkX >= gridWidth || checkY < 0 || checkY >= gridWidth )
                        continue;

                   int cellKey = SpatialGrid.GetCellKey( new int2(checkX, checkY), gridWidth );

                    // Check every wolf in this grid cell
                    // (key = grid index, value = wolf index, duplicate keys allowed)
                    foreach (int wolfIndex in wolfGrid.GetValuesForKey(cellKey))
                    {
                        float distanceSquared = math.lengthsq( sheepPosition - wolfStates[wolfIndex].position );

                        // Remember this wolf if it's the closest one found so far
                        if (distanceSquared < closestWolfDistanceSquared)
                        {
                            closestWolfDistanceSquared = distanceSquared;
                            closestWolfPosition = wolfStates[wolfIndex].position;
                        }
                    }
                }
            }

            return closestWolfDistanceSquared < wolfFleeRadius * wolfFleeRadius;
        }

        private void ApplySeparation( ref SheepState sheep, int sheepIndex, int sheepCellX, int sheepCellY )
        {
            float2 separation = float2.zero;

            float separationRadiusSquared =
                SeparationRadius * SeparationRadius;

            // Check this cell and the 8 surrounding cells
            for (int yOffset = -1; yOffset <= 1; yOffset++)
            {
                for (int xOffset = -1; xOffset <= 1; xOffset++)
                {
                    int cellX = sheepCellX + xOffset;
                    int cellY = sheepCellY + yOffset;

                    if ( cellX < 0 || cellX >= gridWidth || cellY < 0 || cellY >= gridWidth )
                    {
                        continue;
                    }

                    int cellKey = SpatialGrid.GetCellKey( new int2(cellX, cellY), gridWidth );

                    NativeParallelMultiHashMapIterator<int> iterator;
                    int otherSheepIndex;

                    if ( sheepGrid.TryGetFirstValue( cellKey, out otherSheepIndex, out iterator ) )
                    {
                        while (true)
                        {
                            if (otherSheepIndex != sheepIndex)
                            {
                                SheepState otherSheep = currentSheepData[otherSheepIndex];

                                if (!otherSheep.lost)
                                {
                                    float2 away = sheep.position - otherSheep.position;

                                    float distanceSquared = math.lengthsq(away);

                                    if ( distanceSquared > 0.0001f && distanceSquared < separationRadiusSquared )
                                    {
                                        float distance = math.sqrt(distanceSquared);

                                        // Closer sheep push harder
                                        float strength = 1f - distance / SeparationRadius;

                                        separation += (away / distance) * strength;
                                    }
                                }
                            }

                            if ( !sheepGrid.TryGetNextValue( out otherSheepIndex, ref iterator ) )
                                break;
                        }
                    }
                }
            }

            float separationAmount = math.length(separation);

            // If surrounding sheep mostly cancel each other out,
            // don't let a tiny imbalance move this sheep around
            if (separationAmount > 0.5f)
            {
                float2 separationDirection = separation / separationAmount;

                sheep.velocity = math.normalizesafe( sheep.velocity + separationDirection * SeparationStrength );
            }
        }

        private void ApplyHerding( ref SheepState sheep, int sheepCellX, int sheepCellY )
        {
            int currentKey = SpatialGrid.GetCellKey( new int2(sheepCellX, sheepCellY), gridWidth );

            int bestX = sheepCellX;
            int bestY = sheepCellY;

            int bestCount = sheepCellCounts[currentKey];

            // Count all sheep in the nearby cells
            int nearbySheepCount = 0;

            // Check this cell and its 8 neighbors.
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int checkX = sheepCellX + offsetX;
                    int checkY = sheepCellY + offsetY;

                    if ( checkX < 0 || checkX >= gridWidth || checkY < 0 || checkY >= gridWidth )
                        continue;

                    int cellKey = SpatialGrid.GetCellKey( new int2(checkX, checkY), gridWidth );
                    int sheepCount = sheepCellCounts[cellKey];

                    nearbySheepCount += sheepCount;

                    if (sheepCount > bestCount)
                    {
                        bestCount = sheepCount;
                        bestX = checkX;
                        bestY = checkY;
                    }
                }
            }

            // If a neighboring cell has enough more sheep,
            // gently steer toward its center
            if ( nearbySheepCount < HerdSatisfiedCount && (bestX != sheepCellX || bestY != sheepCellY) && bestCount > sheepCellCounts[currentKey] * HerdAttractionThreshold )
            {
                float2 herdTarget = new float2( pastureMin + (bestX + 0.5f) * cellSize, pastureMin + (bestY + 0.5f) * cellSize );

                float2 herdDirection = math.normalizesafe(herdTarget - sheep.position);

                sheep.velocity = math.normalizesafe( sheep.velocity + herdDirection * herdAttractionStrength );
            }
        }
    }

    [BurstCompile]
    internal struct BuildSheepGridJob : IJobFor
    {
        // Current sheep positions.
        [ReadOnly]
        public NativeArray<SheepState> sheepData;

        // Multiple worker threads can safely add sheep to the grid
        public NativeParallelMultiHashMap< int, int >.ParallelWriter gridWriter;

        public float pastureMin;
        public float cellSize;
        public int gridWidth;

        public void Execute(int index)
        {
            if (sheepData[index].lost) { return; }

            float2 position = sheepData[index].position;

            int cellKey = SpatialGrid.GetCellKey( position, pastureMin, cellSize, gridWidth );

            // Record that this sheep is inside this cell
            gridWriter.Add(cellKey, index );
        }
}
