using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UILine 坐标转换辅助工具，纯静态方法。
/// 将屏幕/世界/归一化/像素坐标转换为 RectTransform 局部像素坐标（左下原点，x 右 y 上）。
/// </summary>
public static class UILineUtils
{
    /// <summary>屏幕像素坐标 → UI 局部像素坐标（左下原点）</summary>
    public static Vector2 ScreenToUiLocal(RectTransform ui, Vector2 screen, Camera uiCamera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            ui, screen, uiCamera, out Vector2 localPoint);
        return localPoint + Vector2.Scale(ui.rect.size, ui.pivot);
    }

    /// <summary>世界坐标 → UI 局部像素坐标（左下原点）</summary>
    public static Vector2 WorldToUiLocal(RectTransform ui, Vector3 world,
                                         Camera worldCamera, Camera uiCamera)
    {
        Vector3 screenPoint = worldCamera.WorldToScreenPoint(world);
        return ScreenToUiLocal(ui, screenPoint, uiCamera);
    }

    /// <summary>归一化坐标 (0~1, 左下原点) → UI 局部像素坐标</summary>
    public static Vector2 NormalizedToUiLocal(RectTransform ui, Vector2 normalized01)
    {
        Rect r = ui.rect;
        return new Vector2(normalized01.x * r.width, normalized01.y * r.height);
    }

    /// <summary>归一化坐标 (0~1, 左上原点) → UI 局部像素坐标</summary>
    public static Vector2 NormalizedTopLeftToUiLocal(RectTransform ui, Vector2 normalized01TopLeft)
    {
        Rect r = ui.rect;
        return new Vector2(normalized01TopLeft.x * r.width,
                           (1f - normalized01TopLeft.y) * r.height);
    }

    /// <summary>左上原点像素坐标（如图像像素）→ UI 局部像素坐标</summary>
    public static Vector2 PixelTopLeftToUiLocal(RectTransform ui,
                                                Vector2 pixelTopLeft, Vector2 imageSize)
    {
        Rect r = ui.rect;
        float x = pixelTopLeft.x * r.width / imageSize.x;
        float y = (imageSize.y - pixelTopLeft.y) * r.height / imageSize.y;
        return new Vector2(x, y);
    }

    /// <summary>批量归一化坐标 (0~1, 左下原点) → UI 局部像素坐标</summary>
    public static void NormalizedToUiLocal(RectTransform ui,
                                           IList<Vector2> src, List<Vector2> dst)
    {
        dst.Clear();
        Rect r = ui.rect;
        for (int i = 0; i < src.Count; i++)
        {
            Vector2 n = src[i];
            dst.Add(new Vector2(n.x * r.width, n.y * r.height));
        }
    }

    /// <summary>批量左上原点像素坐标 → UI 局部像素坐标</summary>
    public static void PixelTopLeftToUiLocal(RectTransform ui, Vector2 imageSize,
                                             IList<Vector2> src, List<Vector2> dst)
    {
        dst.Clear();
        Rect r = ui.rect;
        float sx = r.width / imageSize.x;
        float sy = r.height / imageSize.y;
        for (int i = 0; i < src.Count; i++)
        {
            Vector2 p = src[i];
            dst.Add(new Vector2(p.x * sx, (imageSize.y - p.y) * sy));
        }
    }
}
