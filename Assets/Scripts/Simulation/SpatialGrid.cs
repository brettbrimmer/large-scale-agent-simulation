using Unity.Mathematics;

/*
    Helper class to convert coordinates:
    
    GetCellKey(): World Position -> Hash-map key
    GetCellCoordinates(): World Position -> grid
    GetCellKey(): Grid -> Hash-map key

    The spatial grids are multi-hash maps <int, int>, where the first int
    is the grid-cell key, and the second int is an entity index.

    For example, key = 23 in the sheep spatial grid returns all values
    stored under key 23. Since duplicate keys are allowed, this may return
    zero or more sheep indices for sheep currently in that grid cell
*/
internal static class SpatialGrid
{
    /*
        Convert world (X,Z) position directly to a hash-map key.
        Uses GetCellCoordinates() as an intermediary conversion from world (X,Z) -> grid (X,Y)
    */
    public static int GetCellKey( float2 position, float pastureMin, float cellSize, int gridWidth )
    {
        int2 cell = GetCellCoordinates( position, pastureMin, cellSize, gridWidth );

        return GetCellKey(cell, gridWidth);
    }

    // Convert world (X,Z) position to grid (X,Y) coordinates
    public static int2 GetCellCoordinates( float2 position, float pastureMin, float cellSize, int gridWidth )
    {
        int cellX = (int)math.floor( (position.x - pastureMin) / cellSize );
        int cellY = (int)math.floor( (position.y - pastureMin) / cellSize );

        cellX = math.clamp(cellX, 0, gridWidth - 1);
        cellY = math.clamp(cellY, 0, gridWidth - 1);

        return new int2(cellX, cellY);
    }

    // Convert grid (X,Y) coordinates to a hash-map key
    public static int GetCellKey( int2 cell, int gridWidth )
    {
        return cell.x + cell.y * gridWidth;
    }
}