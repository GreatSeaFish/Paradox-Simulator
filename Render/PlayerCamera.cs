using Godot;
using System;

public partial class PlayerCamera : Camera2D
{
    private bool _isDraggingCamera = false;

    [ExportGroup("Camera Settings")]
    [Export] public float ZoomSpeed = 0.1f;        // 每次滚轮滚动的缩放步长
    [Export] public float MinZoom = 0.3f;         // 最小缩小比例
    [Export] public float MaxZoom = 3.0f;         // 最大放大比例

    public override void _UnhandledInput(InputEvent @event)
    {
        // 1. 鼠标中键 按下/抬起 状态切换
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _isDraggingCamera = mb.Pressed;
                GetViewport().SetInputAsHandled();
            }

            // 2. 滚轮前后滑动 缩放逻辑（锚定鼠标指针）
            if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                // 【核心步骤 1】记录缩放前，鼠标在游戏世界（World）中的全局坐标
                Vector2 mousePosBeforeZoom = GetGlobalMousePosition();

                // 计算新的缩放值
                Vector2 currentZoom = Zoom;
                if (mb.ButtonIndex == MouseButton.WheelUp)
                {
                    currentZoom += new Vector2(ZoomSpeed, ZoomSpeed);
                }
                else if (mb.ButtonIndex == MouseButton.WheelDown)
                {
                    currentZoom -= new Vector2(ZoomSpeed, ZoomSpeed);
                }

                // 限制缩放边界
                currentZoom.X = Mathf.Clamp(currentZoom.X, MinZoom, MaxZoom);
                currentZoom.Y = Mathf.Clamp(currentZoom.Y, MinZoom, MaxZoom);
                
                // 应用新的缩放
                Zoom = currentZoom;

                // 【核心步骤 2】由于 Zoom 改变了，此时鼠标在世界中的全局坐标已经发生了偏移
                // 重新获取缩放后，鼠标在世界中的新全局坐标
                Vector2 mousePosAfterZoom = GetGlobalMousePosition();

                // 【核心步骤 3】补偿相机的坐标，把这个世界坐标的差值加回给相机
                // 这样就能把缩放后的地图“拉回来”，使得鼠标指针下的像素保持不动
                GlobalPosition += (mousePosBeforeZoom - mousePosAfterZoom);

                GetViewport().SetInputAsHandled();
            }
        }

        // 3. 按住中键时 移动鼠标平移相机
        if (_isDraggingCamera && @event is InputEventMouseMotion mm)
        {
            GlobalPosition -= mm.Relative / Zoom;
            GetViewport().SetInputAsHandled();
        }
    }
}