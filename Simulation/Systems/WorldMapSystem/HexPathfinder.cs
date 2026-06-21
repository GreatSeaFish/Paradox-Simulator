using System;
using System.Collections.Generic;

namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;

/// <summary>
/// 寻路节点数据
/// </summary>
public class PathNode : IComparable<PathNode>
{
    public HexCoord Coord;
    public int GCost; // 起点到当前点的消耗
    public int HCost; // 启发式消耗（到终点的预估）
    public int FCost => GCost + HCost;
    
    public PathNode Parent;
    
    // 关键：用于确定性 Tie-breaker。记录插入队列的顺序。
    public long InsertSequence;

    public int CompareTo(PathNode other)
    {
        // 1. 比较 F 值（总消耗）
        int cmp = FCost.CompareTo(other.FCost);
        if (cmp != 0) return cmp;

        // 2. F 值相同时，优先选择更接近目标点（H值更小）的节点
        cmp = HCost.CompareTo(other.HCost);
        if (cmp != 0) return cmp;

        // 3. F 和 H 都相同时（帧同步防异位），根据插入序号打破平局，保证绝对确定性
        return InsertSequence.CompareTo(other.InsertSequence);
    }
}

/// <summary>
/// 六边形 A* 寻路主系统 (帧同步适用)
/// </summary>
public class HexPathfinder
{
    // 六边形的 6 个相邻方向 (Cube 坐标系)
    private static readonly HexCoord[] Directions = new HexCoord[]
    {
        new HexCoord(1, -1, 0), new HexCoord(1, 0, -1), new HexCoord(0, 1, -1),
        new HexCoord(-1, 1, 0), new HexCoord(-1, 0, 1), new HexCoord(0, -1, 1)
    };

    // 保存全地图的地形类型
    private Dictionary<HexCoord, string> _mapData = new Dictionary<HexCoord, string>();

    // 插入序号生成器（用于确定性排序）
    private long _sequenceCounter = 0;

    /// <summary>
    /// 初始化/更新地图数据 (可传入从 map_data.json 解析出来的数据)
    /// </summary>
    public void InitMap(IEnumerable<HexTileData> tiles)
    {
        _mapData.Clear();
        foreach (var tile in tiles)
        {
            _mapData[new HexCoord(tile.X, tile.Y, tile.Z)] = tile.GroundType;
        }
    }

    /// <summary>
    /// 寻找最短路径
    /// </summary>
    /// <param name="start">起点坐标</param>
    /// <param name="target">终点坐标</param>
    /// <returns>路径列表（包含起点和终点）。如果无路可走则返回空列表</returns>
    public List<HexCoord> FindPath(HexCoord start, HexCoord target)
    {
        var path = new List<HexCoord>();
        
        if (!_mapData.ContainsKey(start) || !_mapData.ContainsKey(target))
            return path; // 起点或终点不在地图内

        if (start == target)
        {
            path.Add(start);
            return path;
        }

        // 使用自定义的最小堆栈，确保性能和确定性
        var openSet = new DeterministicMinHeap();
        // 记录访问过的最佳 G 消耗，防止重复扩展
        var bestGCosts = new Dictionary<HexCoord, int>();

        _sequenceCounter = 0; // 重置序号

        var startNode = new PathNode
        {
            Coord = start,
            GCost = 0,
            HCost = HexCoord.Distance(start, target),
            Parent = null,
            InsertSequence = ++_sequenceCounter
        };

        openSet.Push(startNode);
        bestGCosts[start] = 0;

        PathNode targetNode = null;

        while (openSet.Count > 0)
        {
            PathNode current = openSet.Pop();

            // 如果当前取出的节点消耗已经大于我们记录的该坐标的最佳消耗，则视为废弃节点（Lazy Deletion）
            if (current.GCost > bestGCosts[current.Coord])
                continue;

            // 寻路成功
            if (current.Coord == target)
            {
                targetNode = current;
                break;
            }

            // 遍历 6 个邻居
            for (int i = 0; i < 6; i++)
            {
                HexCoord neighborCoord = current.Coord + Directions[i];

                // 1. 检查是否存在且可通过
                if (!_mapData.TryGetValue(neighborCoord, out string terrainType))
                    continue;

                int moveCost = GetTerrainCost(terrainType);
                if (moveCost < 0) 
                    continue; // 不可通行的地形

                // 2. 计算新的 G 消耗
                int tentativeGCost = current.GCost + moveCost;

                // 3. 如果发现更优路径，或者从未访问过该邻居
                if (!bestGCosts.TryGetValue(neighborCoord, out int existingGCost) || tentativeGCost < existingGCost)
                {
                    bestGCosts[neighborCoord] = tentativeGCost;

                    var neighborNode = new PathNode
                    {
                        Coord = neighborCoord,
                        GCost = tentativeGCost,
                        HCost = HexCoord.Distance(neighborCoord, target), // 曼哈顿距离作为启发值
                        Parent = current,
                        InsertSequence = ++_sequenceCounter // 保证先进队列的优先级明确
                    };

                    openSet.Push(neighborNode);
                }
            }
        }

        // 回溯生成路径
        if (targetNode != null)
        {
            PathNode curr = targetNode;
            while (curr != null)
            {
                path.Add(curr.Coord);
                curr = curr.Parent;
            }
            path.Reverse(); // 将顺序从 终点->起点 翻转为 起点->终点
        }

        return path;
    }

    /// <summary>
    /// 获取地形移动消耗（在此配置你的游戏逻辑）
    /// </summary>
    private int GetTerrainCost(string terrainType)
    {
        switch (terrainType)
        {
            case "Plain": return 1;
            case "Hills": return 2;
            // -1 表示不可通行
            case "Mountains": return -1; 
            case "Sea": return -1; // 假设陆地单位无法下海，如果造船系统可改为正整数
            default: return -1;
        }
    }

    /// <summary>
    /// 最小堆优先队列实现（完全确定性，脱离对系统版本的依赖）
    /// </summary>
    private class DeterministicMinHeap
    {
        private List<PathNode> _elements = new List<PathNode>();

        public int Count => _elements.Count;

        public void Push(PathNode item)
        {
            _elements.Add(item);
            SiftUp(_elements.Count - 1);
        }

        public PathNode Pop()
        {
            if (_elements.Count == 0) throw new InvalidOperationException("Queue is empty");

            PathNode result = _elements[0];
            PathNode last = _elements[_elements.Count - 1];
            _elements.RemoveAt(_elements.Count - 1);

            if (_elements.Count > 0)
            {
                _elements[0] = last;
                SiftDown(0);
            }
            return result;
        }

        private void SiftUp(int index)
        {
            PathNode item = _elements[index];
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                PathNode parent = _elements[parentIndex];

                if (item.CompareTo(parent) >= 0)
                    break;

                _elements[index] = parent;
                index = parentIndex;
            }
            _elements[index] = item;
        }

        private void SiftDown(int index)
        {
            PathNode item = _elements[index];
            int count = _elements.Count;

            while (true)
            {
                int leftChildIndex = index * 2 + 1;
                if (leftChildIndex >= count)
                    break;

                int minChildIndex = leftChildIndex;
                int rightChildIndex = leftChildIndex + 1;

                if (rightChildIndex < count && _elements[rightChildIndex].CompareTo(_elements[leftChildIndex]) < 0)
                {
                    minChildIndex = rightChildIndex;
                }

                if (item.CompareTo(_elements[minChildIndex]) <= 0)
                    break;

                _elements[index] = _elements[minChildIndex];
                index = minChildIndex;
            }
            _elements[index] = item;
        }
    }
}