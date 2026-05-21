using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UILine 虚线裁切工具，按弧长将折线切成 on/off 区间。
/// 仅输出 on 区间的子折线；off 区间不输出。
/// M9 用静态 List&lt;float&gt; 避免每帧 new float[]。
/// </summary>
public static class UILineDashSlicer
{
    static readonly List<List<Vector2>> s_subPool = new List<List<Vector2>>();
    static readonly List<float> s_sums = new List<float>();

    /// <summary>
    /// 把折线按弧长切成 on/off 段，只把 on 段写到 dst。
    /// dst 调用前会被清空（旧内容归还对象池）；内部 List 池避免每次 alloc 子列表。
    /// </summary>
    public static void Slice(IReadOnlyList<Vector2> points,
                             float dashLength, float gapLength, float dashOffset,
                             List<List<Vector2>> dst)
    {
        // 归还旧内容到对象池
        s_subPool.AddRange(dst);
        dst.Clear();

        int n = points.Count;
        if (n < 2) return;

        // 退化：dashLength 或 gapLength 极小 → 整段等同 Solid
        if (dashLength <= 0.1f || gapLength <= 0.1f)
        {
            List<Vector2> sub = TakeFromPool();
            for (int i = 0; i < n; i++)
                sub.Add(points[i]);
            dst.Add(sub);
            return;
        }

        // 累积弧长 → 复用 s_sums
        s_sums.Clear();
        s_sums.Add(0f);
        for (int i = 0; i < n - 1; i++)
        {
            float segLen = (points[i + 1] - points[i]).magnitude;
            s_sums.Add(s_sums[i] + segLen);
        }
        float L = s_sums[n - 1];
        if (L < 1e-6f) return;

        float T = dashLength + gapLength;

        // k 范围：保证覆盖所有与 [0, L] 有交集的 on 区间
        int kStart = Mathf.FloorToInt((-dashOffset - dashLength) / T) - 1;
        int kEnd   = Mathf.CeilToInt((L - dashOffset) / T) + 1;

        for (int k = kStart; k <= kEnd; k++)
        {
            float a = dashOffset + (float)k * T;
            float b = a + dashLength;

            // clamp 到 [0, L]
            if (b <= 0f || a >= L) continue;
            if (a < 0f) a = 0f;
            if (b > L) b = L;

            // 构造子折线 [a, b]
            List<Vector2> sub = TakeFromPool();
            EmitSubPolyline(points, a, b, sub);
            dst.Add(sub);
        }
    }

    /// <summary>
    /// 闭合折线虚线裁切（环形弧长）。把闭合环按弧长切成 on 区间，
    /// 能正确处理跨 P0 边界的 dash 回绕，每段是独立开放子折线。
    /// </summary>
    public static void SliceClosed(IReadOnlyList<Vector2> points,
                                   float dashLength, float gapLength, float dashOffset,
                                   List<List<Vector2>> dst)
    {
        s_subPool.AddRange(dst);
        dst.Clear();

        int n = points.Count;
        if (n < 3) return;

        if (dashLength <= 0.1f || gapLength <= 0.1f)
        {
            List<Vector2> sub = TakeFromPool();
            for (int i = 0; i < n; i++) sub.Add(points[i]);
            dst.Add(sub);
            return;
        }

        bool alreadyClosed = (points[n - 1] - points[0]).sqrMagnitude < 1e-6f;

        // 构建扩展点列表（环闭合用）
        List<Vector2> extPts = TakeFromPool();
        for (int i = 0; i < n; i++) extPts.Add(points[i]);
        if (!alreadyClosed) extPts.Add(points[0]);

        int nExt = extPts.Count;
        int segCount = nExt - 1;

        // 环形弧长累加 → 复用 s_sums
        s_sums.Clear();
        s_sums.Add(0f);
        for (int i = 0; i < segCount; i++)
        {
            float segLen = (extPts[i + 1] - extPts[i]).magnitude;
            s_sums.Add(s_sums[i] + segLen);
        }

        float P = s_sums[segCount];
        if (P < 1e-6f) { extPts.Clear(); s_subPool.Add(extPts); return; }

        float T = dashLength + gapLength;

        int kStart = Mathf.FloorToInt((-dashOffset - dashLength) / T) - 1;
        int kEnd   = Mathf.CeilToInt((P - dashOffset) / T) + 1;

        for (int k = kStart; k <= kEnd; k++)
        {
            float a = dashOffset + (float)k * T;
            float b = a + dashLength;

            if (b <= 0f || a >= P) continue;

            float aClamped = a < 0f ? 0f : a;
            float bClamped = b > P  ? P  : b;

            if (aClamped >= bClamped) continue;

            List<Vector2> sub = TakeFromPool();

            if (b > P)
            {
                // Dash 跨 P0 边界回绕：先画 [a, P]，再接 [0, b-P]
                EmitSubPolyline(extPts, aClamped, P, sub);
                // 去重：删除末尾 P0 点（下一段开头也是 P0）
                if (sub.Count > 0) sub.RemoveAt(sub.Count - 1);
                EmitSubPolyline(extPts, 0f, b - P, sub);
            }
            else
            {
                EmitSubPolyline(extPts, aClamped, bClamped, sub);
            }

            if (sub.Count >= 2)
                dst.Add(sub);
            else
            {
                sub.Clear();
                s_subPool.Add(sub);
            }
        }

        extPts.Clear();
        s_subPool.Add(extPts);
    }

    /// <summary>
    /// 把 dst 中的子列表归还到对象池并清空 dst。
    /// </summary>
    public static void ReturnToPool(List<List<Vector2>> dst)
    {
        s_subPool.AddRange(dst);
        dst.Clear();
    }

    // ── 内部方法 ──────────────────────────────────────────

    static List<Vector2> TakeFromPool()
    {
        if (s_subPool.Count > 0)
        {
            int last = s_subPool.Count - 1;
            List<Vector2> sub = s_subPool[last];
            s_subPool.RemoveAt(last);
            sub.Clear();
            return sub;
        }
        return new List<Vector2>();
    }

    /// <summary>二分查找弧长位置 t 所在的段索引。</summary>
    static int FindSegment(float t, int n)
    {
        if (t <= 0f) return 0;
        float L = s_sums[n - 1];
        if (t >= L) return n - 2;

        int lo = 0;
        int hi = n - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (s_sums[mid] <= t)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    /// <summary>弧长位置 t 在段 segIdx 上的插值坐标。</summary>
    static Vector2 PointAtArc(float t, IReadOnlyList<Vector2> pts, int segIdx)
    {
        float segStart = s_sums[segIdx];
        float segEnd   = s_sums[segIdx + 1];
        float segLen   = segEnd - segStart;
        if (segLen < 1e-6f) return pts[segIdx];
        float frac = (t - segStart) / segLen;
        return Vector2.LerpUnclamped(pts[segIdx], pts[segIdx + 1], frac);
    }

    /// <summary>把弧长区间 [a, b] 对应的子折线写入 dst（保留中间原始顶点）。</summary>
    static void EmitSubPolyline(IReadOnlyList<Vector2> pts,
                                float a, float b, List<Vector2> dst)
    {
        int n = pts.Count;
        int segA = FindSegment(a, n);
        int segB = FindSegment(b, n);

        // a 处插值点
        dst.Add(PointAtArc(a, pts, segA));

        // 中间原始顶点（segA+1 到 segB）
        for (int i = segA + 1; i <= segB; i++)
        {
            Vector2 p = pts[i];
            if (dst.Count > 0 && (p - dst[dst.Count - 1]).sqrMagnitude < 1e-12f)
                continue;
            dst.Add(p);
        }

        // b 处插值点
        Vector2 pb = PointAtArc(b, pts, segB);
        if (dst.Count > 0 && (pb - dst[dst.Count - 1]).sqrMagnitude < 1e-12f)
            return;
        dst.Add(pb);
    }
}
