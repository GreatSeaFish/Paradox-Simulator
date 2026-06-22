// res://View/Interaction/TerritoryVisualizer.cs
using Godot;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class TerritoryVisualizer : Node2D
{
    private TileMapLayer[] _territoryLayers = new TileMapLayer[8]; // [cite: 606]
    private readonly Color[] _colorValues = { 
        Colors.Red, Colors.Blue, Colors.Orange, Colors.Green, 
        Colors.Yellow, Colors.Purple, Colors.Pink, Colors.LightBlue 
    }; // [cite: 607]

    public void Init(Node2D markLayersRoot)
    {
        for (int i = 0; i < 8; i++) // [cite: 611]
        {
            _territoryLayers[i] = markLayersRoot.GetNode<TileMapLayer>($"Player{i + 1}"); // [cite: 612]
            _territoryLayers[i].Clear(); // [cite: 613]
        }
    }

    /// <summary>
    /// 当逻辑帧推进、地块归属发生改动时，被顶层调用进行批量重绘
    /// </summary>
    public void UpdateOwnershipVisuals()
    {
        var mapData = CoreHost.WorldSimulationState; // [cite: 650]
        if (mapData == null) return; // [cite: 651]

        var layerCells = new Dictionary<int, Godot.Collections.Array<Vector2I>>(); // [cite: 651]
        for (int i = 0; i < 8; i++) // [cite: 652]
        {
            layerCells[i] = new Godot.Collections.Array<Vector2I>(); // [cite: 652]
        }

        foreach (var kvp in mapData.TileOwners) // [cite: 653]
        {
            int ownerId = kvp.Value; // [cite: 653]
            if (ownerId != -1)  // [cite: 654]
            {
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == ownerId); // [cite: 654]
                if (playerInfo != null) // [cite: 655]
                {
                    var offset = MapRenderBridge.CubeToOffset(kvp.Key.X, kvp.Key.Y, kvp.Key.Z); // [cite: 655]
                    layerCells[playerInfo.SlotId].Add(new Vector2I(offset.offsetX, offset.offsetY)); // [cite: 656]
                }
            }
        }

        for (int i = 0; i < 8; i++) // [cite: 657]
        {
            var targetLayer = _territoryLayers[i]; // [cite: 657]
            if (targetLayer == null) continue; // [cite: 658]

            targetLayer.Clear(); // [cite: 658]
            if (layerCells[i].Count > 0) // [cite: 659]
            {
                targetLayer.SetCellsTerrainConnect(layerCells[i], 0, 0, false); // 
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.SlotId == i); // [cite: 661]
                if (playerInfo != null) // [cite: 662]
                {
                    targetLayer.Modulate = _colorValues[playerInfo.ColorId]; // [cite: 662]
                }
            }
        }
    }
}