namespace ParadoxSimulator.Core.WorldMapSystem;

public static class MapRenderBridge
{
    public static string GetTerrainNameBySourceId(int sourceId)
    {
        return sourceId switch
        {
            0 => "Plain",
            1 => "Sea",
            2 => "Hills",
            3 => "Mountains",
            4 => "Desert",
            5 => "Volcanic",
            6 => "Forest",
            _ => "None"
        };
    }
    
    public static int GetSourceIdByTerrainName(string name)
    {
        return name switch
        {
            "Plain" => 0,
            "Sea" => 1,
            "Hills" => 2,
            "Mountains" => 3,
            "Desert" => 4,
            "Volcanic" => 5,
            "Forest" => 6,
            _ => -1
        };
    }
    
    public static (int cubeX, int cubeY, int cubeZ) OffsetToCube(int offsetX, int offsetY)
    {
        int cubeX = offsetX - (offsetY - (offsetY & 1)) / 2;
        int cubeZ = offsetY;
        int cubeY = -cubeX - cubeZ;
        return (cubeX, cubeY, cubeZ);
    }
    
    public static (int offsetX, int offsetY) CubeToOffset(int cubeX, int cubeY, int cubeZ)
    {
        int offsetX = cubeX + (cubeZ - (cubeZ & 1)) / 2;
        int offsetY = cubeZ;
        return (offsetX, offsetY);
    }
    
}