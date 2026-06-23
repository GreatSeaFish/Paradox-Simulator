
namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;
// 新增：边界/河流数据结构
public class BorderData
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string BorderType { get; set; }
}