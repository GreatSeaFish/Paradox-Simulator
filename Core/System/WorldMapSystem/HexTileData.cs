namespace ParadoxSimulator.Core.WorldMapSystem;

public class HexTileData
{
    public int Id { get; set; } 
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    // 拆分为地面和地表
    public string GroundType { get; set; }
    public string SurfaceType { get; set; }
}
