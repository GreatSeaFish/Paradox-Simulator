using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;

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
/// 无状态六边形 A* 寻路主系统 (帧同步适用)
/// </summary>
public static class HexPathfinder
{
    // 六边形的 6 个相邻方向 (Cube 坐标系)
    private static readonly HexCoord[] Directions = new HexCoord[]
    {
        new HexCoord(1, -1, 0), new HexCoord(1, 0, -1), new HexCoord(0, 1, -1),
        new HexCoord(-1, 1, 0), new HexCoord(-1, 0, 1), new HexCoord(0, -1, 1)
    };

    /// <summary>
    /// 无状态寻路：每次计算时实时读取静态地形与动态阵营状态
    /// </summary>
    public static List<HexCoord> FindPath(
        HexCoord start, 
        HexCoord target, 
        MapConfig mapConfig, 
        WorldSimulationState state, 
        int moverPlayerId)
    {
        var path = new List<HexCoord>();
        
        // 起点或终点不在地图内
        if (!mapConfig.Tiles.ContainsKey(start) || !mapConfig.Tiles.ContainsKey(target))
            return path;

        if (start == target)
        {
            path.Add(start);
            return path;
        }

        // 使用自定义的最小堆栈，确保性能和确定性
        var openSet = new DeterministicMinHeap();
        // 记录访问过的最佳 G 消耗，防止重复扩展
        var bestGCosts = new Dictionary<HexCoord, int>();
        
        long sequenceCounter = 0; // 局部变量替代全局状态，保证每次寻路隔离独立

        var startNode = new PathNode
        {
            Coord = start,
            GCost = 0,
            HCost = HexCoord.Distance(start, target),
            Parent = null,
            InsertSequence = ++sequenceCounter
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
                
                // 核心：调用带动态状态的代价计算
                int moveCost = GetDynamicTerrainCost(neighborCoord, mapConfig, state, moverPlayerId);
                
                if (moveCost < 0) 
                    continue; // 不可通行的地形

                // 计算新的 G 消耗
                int tentativeGCost = current.GCost + moveCost;
                
                // 如果发现更优路径，或者从未访问过该邻居
                if (!bestGCosts.TryGetValue(neighborCoord, out int existingGCost) || tentativeGCost < existingGCost)
                {
                    bestGCosts[neighborCoord] = tentativeGCost;
                    var neighborNode = new PathNode
                    {
                        Coord = neighborCoord,
                        GCost = tentativeGCost,
                        HCost = HexCoord.Distance(neighborCoord, target), // 曼哈顿距离作为启发值
                        Parent = current,
                        InsertSequence = ++sequenceCounter // 保证先进队列的优先级明确
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
    /// 动态代价计算中心 (融合地形与政治状态)
    /// 公开此方法，方便在 Processor 推进时做"落脚点合法性校验"
    /// </summary>
    public static int GetDynamicTerrainCost(HexCoord coord, MapConfig mapConfig, WorldSimulationState state, int moverPlayerId)
    {
        // 1. 静态地形校验
        if (!mapConfig.Tiles.TryGetValue(coord, out var tileData)) 
            return -1;
        
        // 基础地形代价
        int cost = tileData.GroundType switch
        {
            "Plain" => 1,
            "Hills" => 2,
            "Mountains" => -1,
            "Sea" => -1, // 假设陆地单位无法下海
            _ => -1
        };
        if (cost < 0) return -1;

        // 2. 动态状态校验：是谁的领地？
        int ownerId = state.GetTileOwner(coord);
        
        // 如果是无主之地，或者自己的领地，按原价走
        if (ownerId == -1 || ownerId == moverPlayerId) 
            return cost;

        // 3. 【外交阻挡判断预留】
        // 如果是别人的领地，且封锁了边界（假设未来有类似 DiplomacyStates 字典）
        // bool isBorderClosed = state.DiplomacyStates.IsClosed(ownerId, moverPlayerId);
        // if (isBorderClosed) return -1; // 封锁状态下视为无法通行

        // 默认行为：如果没有封锁边界，但在敌国领地行军，移动消耗翻倍
        return cost * 1; 
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