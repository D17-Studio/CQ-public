using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// 将设计分辨率（1600x900，原点在屏幕中心）下的 Vec2 坐标转换为当前窗口的实际坐标
/// </summary>
public class ScreenAdapter
{
    private const float DesignWidth = 1600f;
    private const float DesignHeight = 900f;

    private readonly Camera _mainCamera =  Camera.main;

    /// <summary>设计坐标 → 屏幕像素坐标（左下角为原点）</summary>
    public Vector2 ToScreenPos(Vec2 designPos)
    {
        return new Vector2(designPos.x / DesignWidth * Screen.width, designPos.y / DesignHeight * Screen.height)
               + new Vector2(Screen.width / 2f, Screen.height / 2f);
    }


    public Vector2 ToWorldPos(Vec2 designPos)
    {
        return _mainCamera.ScreenToWorldPoint(ToScreenPos(designPos));
    }
}
