using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// 将设计分辨率（1920x1080）下的 Vec2 坐标转换为当前窗口的实际像素坐标
/// </summary>
public static class ScreenAdapter
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;

    public static Vector2 ToScreenPos(Vec2 designPos)
    {
        return new Vector2(
            designPos.x / DesignWidth * Screen.width,
            designPos.y / DesignHeight * Screen.height
        );
    }
}
