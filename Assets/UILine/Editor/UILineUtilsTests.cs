using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// UILineUtils 坐标转换 EditMode 单测。
/// 在 Unity Test Runner 中运行，不在 dotnet build 中编译。
/// </summary>
public class UILineUtilsTests
{
    private GameObject _go;
    private RectTransform _rt;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestRect");
        _rt = _go.AddComponent<RectTransform>();
        _rt.sizeDelta = new Vector2(800f, 600f);
        _rt.pivot = Vector2.zero;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
    }

    // ── NormalizedToUiLocal（左下原点）────────────────────

    [Test]
    public void NormalizedToUiLocal_Zero_ReturnsBottomLeft()
    {
        var result = UILineUtils.NormalizedToUiLocal(_rt, Vector2.zero);
        Assert.AreEqual(0f, result.x, 0.001f);
        Assert.AreEqual(0f, result.y, 0.001f);
    }

    [Test]
    public void NormalizedToUiLocal_One_ReturnsTopRight()
    {
        var result = UILineUtils.NormalizedToUiLocal(_rt, Vector2.one);
        Assert.AreEqual(800f, result.x, 0.001f);
        Assert.AreEqual(600f, result.y, 0.001f);
    }

    [Test]
    public void NormalizedToUiLocal_Half_ReturnsCenter()
    {
        var result = UILineUtils.NormalizedToUiLocal(_rt, new Vector2(0.5f, 0.5f));
        Assert.AreEqual(400f, result.x, 0.001f);
        Assert.AreEqual(300f, result.y, 0.001f);
    }

    // ── NormalizedTopLeftToUiLocal（左上原点）─────────────

    [Test]
    public void NormalizedTopLeftToUiLocal_Zero_TopLeft_ReturnsTopLeftInBottomLeftSystem()
    {
        var result = UILineUtils.NormalizedTopLeftToUiLocal(_rt, Vector2.zero);
        Assert.AreEqual(0f, result.x, 0.001f);
        Assert.AreEqual(600f, result.y, 0.001f);
    }

    [Test]
    public void NormalizedTopLeftToUiLocal_One_BottomRight_ReturnsBottomRightInBottomLeftSystem()
    {
        var result = UILineUtils.NormalizedTopLeftToUiLocal(_rt, Vector2.one);
        Assert.AreEqual(800f, result.x, 0.001f);
        Assert.AreEqual(0f, result.y, 0.001f);
    }

    [Test]
    public void NormalizedTopLeftToUiLocal_YFlipCorrect()
    {
        var result = UILineUtils.NormalizedTopLeftToUiLocal(_rt, new Vector2(0.25f, 0.25f));
        Assert.AreEqual(200f, result.x, 0.001f);
        Assert.AreEqual(450f, result.y, 0.001f);
    }

    // ── PixelTopLeftToUiLocal ─────────────────────────────

    [Test]
    public void PixelTopLeftToUiLocal_ScalesCorrectly()
    {
        var result = UILineUtils.PixelTopLeftToUiLocal(_rt,
            new Vector2(320f, 180f), new Vector2(640f, 360f));
        Assert.AreEqual(400f, result.x, 0.001f);
        Assert.AreEqual(300f, result.y, 0.001f);
    }

    [Test]
    public void PixelTopLeftToUiLocal_TopLeftPixel_GoesToTopOfRect()
    {
        var result = UILineUtils.PixelTopLeftToUiLocal(_rt,
            new Vector2(0f, 0f), new Vector2(1920f, 1080f));
        Assert.AreEqual(0f, result.x, 0.001f);
        Assert.AreEqual(600f, result.y, 0.001f);
    }

    [Test]
    public void PixelTopLeftToUiLocal_BottomRightPixel_GoesToBottomOfRect()
    {
        var result = UILineUtils.PixelTopLeftToUiLocal(_rt,
            new Vector2(1920f, 1080f), new Vector2(1920f, 1080f));
        Assert.AreEqual(800f, result.x, 0.001f);
        Assert.AreEqual(0f, result.y, 0.001f);
    }

    // ── ScreenToUiLocal ───────────────────────────────────

    [Test]
    public void ScreenToUiLocal_PivotZero_GivesDirectMapping()
    {
        var result = UILineUtils.ScreenToUiLocal(_rt, new Vector2(100f, 200f), null);
        Assert.AreEqual(100f, result.x, 0.001f);
        Assert.AreEqual(200f, result.y, 0.001f);
    }

    [Test]
    public void ScreenToUiLocal_PivotCenter_OffsetsCorrectly()
    {
        _rt.pivot = new Vector2(0.5f, 0.5f);
        var result = UILineUtils.ScreenToUiLocal(_rt, new Vector2(200f, 100f), null);
        Assert.AreEqual(200f + 400f, result.x, 0.001f);
        Assert.AreEqual(100f + 300f, result.y, 0.001f);
    }

    // ── 批量版本 ──────────────────────────────────────────

    [Test]
    public void NormalizedToUiLocal_Batch_ConvertsAll()
    {
        var src = new List<Vector2> { Vector2.zero, Vector2.one };
        var dst = new List<Vector2>();
        UILineUtils.NormalizedToUiLocal(_rt, src, dst);

        Assert.AreEqual(2, dst.Count);
        Assert.AreEqual(0f, dst[0].x, 0.001f);
        Assert.AreEqual(0f, dst[0].y, 0.001f);
        Assert.AreEqual(800f, dst[1].x, 0.001f);
        Assert.AreEqual(600f, dst[1].y, 0.001f);
    }

    [Test]
    public void NormalizedToUiLocal_Batch_ClearsPreviousContent()
    {
        var src = new List<Vector2> { Vector2.one };
        var dst = new List<Vector2> { new Vector2(99f, 99f) };
        UILineUtils.NormalizedToUiLocal(_rt, src, dst);

        Assert.AreEqual(1, dst.Count);
    }

    [Test]
    public void PixelTopLeftToUiLocal_Batch_ConvertsAll()
    {
        var src = new List<Vector2> { new Vector2(0f, 0f), new Vector2(640f, 360f) };
        var dst = new List<Vector2>();
        UILineUtils.PixelTopLeftToUiLocal(_rt, new Vector2(640f, 360f), src, dst);

        Assert.AreEqual(2, dst.Count);
        Assert.AreEqual(0f, dst[0].x, 0.001f);
        Assert.AreEqual(600f, dst[0].y, 0.001f);
        Assert.AreEqual(800f, dst[1].x, 0.001f);
        Assert.AreEqual(0f, dst[1].y, 0.001f);
    }

    [Test]
    public void PixelTopLeftToUiLocal_Batch_ClearsPreviousContent()
    {
        var src = new List<Vector2> { new Vector2(320f, 180f) };
        var dst = new List<Vector2> { new Vector2(99f, 99f) };
        UILineUtils.PixelTopLeftToUiLocal(_rt, new Vector2(640f, 360f), src, dst);

        Assert.AreEqual(1, dst.Count);
    }

    // ── 边界情况 ──────────────────────────────────────────

    [Test]
    public void Batch_EmptySource_ClearsAndReturnsEmpty()
    {
        var src = new List<Vector2>();
        var dst = new List<Vector2> { new Vector2(1f, 1f) };
        UILineUtils.NormalizedToUiLocal(_rt, src, dst);
        Assert.AreEqual(0, dst.Count);
    }

    [Test]
    public void PixelTopLeftToUiLocal_NonSquareImage_ScalesCorrectly()
    {
        var result = UILineUtils.PixelTopLeftToUiLocal(_rt,
            new Vector2(960f, 270f), new Vector2(1920f, 540f));
        Assert.AreEqual(400f, result.x, 0.001f);
        Assert.AreEqual(300f, result.y, 0.001f);
    }
}
