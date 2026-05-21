using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UILine 顶点生成核心（静态、无状态）。
/// M7 实现 SDF AA 路线（段 quad + 圆盘 quad）。
/// M9 静态缓冲池复用，避免每帧 new 数组。
/// </summary>
public static class UILineBuilder
{
    /// <summary>
    /// 根据弧度、半径、羽化像素数计算 Round Join/Cap 的扇形分段数。
    /// 公式 ceil(angle * r / featherPixels)，下限 3。
    /// </summary>
    public static int ComputeRoundSectors(float angleRadians, float r, float featherPixels)
    {
        if (featherPixels <= 0f) featherPixels = 1f;
        return Mathf.Max(3, Mathf.CeilToInt(angleRadians * r / featherPixels));
    }

    /// <summary>估算给定点数和配置下的顶点数上界。</summary>
    public static int EstimateVertices(int pointCount, LineJoin join, LineCap startCap, LineCap endCap,
                                       AntiAliasMode aaMode, float featherPixels)
    {
        if (pointCount < 2) return 0;
        int segs = pointCount - 1;
        bool hasAA = aaMode != AntiAliasMode.None;
        int factor = hasAA ? 2 : 1;
        int segV  = segs * 4 * factor;
        int joinV = (pointCount - 2) * 16;
        int capV  = 16 * 2;
        return segV + joinV + capV;
    }

    // ══════════════════════════════════════════════════════════
    //  AA parameter helpers
    // ══════════════════════════════════════════════════════════

    private static void ComputeAAParams(float lineWidth, float featherPixels,
        AntiAliasMode aaMode,
        out float coreHalfWidth, out float actualFeather,
        out float coreAlpha, out float featherAlpha)
    {
        if (featherPixels < 0f) featherPixels = 0f;

        if (aaMode == AntiAliasMode.None)
        {
            coreHalfWidth = lineWidth * 0.5f;
            actualFeather = 0f;
            coreAlpha = 1f;
            featherAlpha = 0f;
        }
        else // Geometric
        {
            if (lineWidth < 2f * featherPixels)
            {
                actualFeather = lineWidth * 0.5f;
                coreHalfWidth = 0f;
                coreAlpha = lineWidth / (2f * featherPixels);
                featherAlpha = 0f;
            }
            else
            {
                actualFeather = featherPixels;
                coreHalfWidth = lineWidth * 0.5f;
                coreAlpha = 1f;
                featherAlpha = 0f;
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Static reusable buffers (M9: 避免每帧 new 数组)
    // ══════════════════════════════════════════════════════════

    static readonly List<Vector2> s_segDir      = new List<Vector2>();
    static readonly List<Vector2> s_segNrm      = new List<Vector2>();
    static readonly List<float>   s_segLen      = new List<float>();
    static readonly List<float>   s_totalLen    = new List<float>();
    static readonly List<Vector2> s_plusStart   = new List<Vector2>();
    static readonly List<Vector2> s_minusStart  = new List<Vector2>();
    static readonly List<Vector2> s_plusEnd     = new List<Vector2>();
    static readonly List<Vector2> s_minusEnd    = new List<Vector2>();
    static readonly List<int>     s_segBase     = new List<int>();

    static readonly List<List<Vector2>> s_dashSubs = new List<List<Vector2>>();
    static readonly List<Vector2> s_closedWorkPts = new List<Vector2>();

    /// <summary>预填充各静态 List 至指定长度（追加 default 值）。</summary>
    private static void EnsureCount<T>(List<T> list, int count)
    {
        list.Clear();
        for (int i = 0; i < count; i++)
            list.Add(default);
    }

    // ══════════════════════════════════════════════════════════
    //  Main Build
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 构建折线全部几何并写入 VertexHelper。
    /// Solid 模式直接画整条折线；Dashed 模式先裁切再逐段画。
    /// maxPoints 用于顶点数超限截断（M9）。
    /// </summary>
    public static void Build(VertexHelper vh, IReadOnlyList<Vector2> pts,
                             float lineWidth, Color color, Vector2 origin,
                             LineJoin join, float miterLimit,
                             LineCap startCap, LineCap endCap,
                             AntiAliasMode aaMode, float featherPixels,
                             DashStyle dashStyle, float dashLength,
                             float gapLength, float dashOffset,
                             bool closed,
                             Material sdfMaterial = null,
                             int maxPoints = int.MaxValue)
    {
        if (pts == null || pts.Count < 2) return;

        int n = Mathf.Min(pts.Count, maxPoints);

        bool useSdf = aaMode == AntiAliasMode.Sdf && sdfMaterial != null;

        if (dashStyle == DashStyle.Solid)
        {
            BuildPolylineImpl(vh, pts, n, lineWidth, color, origin,
                              join, miterLimit, startCap, endCap,
                              aaMode, featherPixels, closed, useSdf);
            return;
        }

        // Dashed：裁切 + 逐段画
        s_dashSubs.Clear();
        if (closed && pts.Count >= 3)
        {
            // Build a truncated wrapper for closed
            if (n < pts.Count)
            {
                s_closedWorkPts.Clear();
                for (int i = 0; i < n; i++) s_closedWorkPts.Add(pts[i]);
                UILineDashSlicer.SliceClosed(s_closedWorkPts, dashLength, gapLength, dashOffset, s_dashSubs);
            }
            else
            {
                UILineDashSlicer.SliceClosed(pts, dashLength, gapLength, dashOffset, s_dashSubs);
            }
        }
        else
        {
            if (n < pts.Count)
            {
                s_closedWorkPts.Clear();
                for (int i = 0; i < n; i++) s_closedWorkPts.Add(pts[i]);
                UILineDashSlicer.Slice(s_closedWorkPts, dashLength, gapLength, dashOffset, s_dashSubs);
            }
            else
            {
                UILineDashSlicer.Slice(pts, dashLength, gapLength, dashOffset, s_dashSubs);
            }
        }
        foreach (var sub in s_dashSubs)
        {
            BuildPolylineImpl(vh, sub, sub.Count, lineWidth, color, origin,
                              join, miterLimit, startCap, endCap,
                              aaMode, featherPixels, false, useSdf);
        }
        UILineDashSlicer.ReturnToPool(s_dashSubs);
    }

    /// <summary>
    /// 对折线执行完整阶段流水线（段几何 → Join → Cap → AA），写入 VertexHelper。
    /// </summary>
    private static void BuildPolylineImpl(VertexHelper vh, IReadOnlyList<Vector2> pts,
                                          int maxPts,
                                          float lineWidth, Color color, Vector2 origin,
                                          LineJoin join, float miterLimit,
                                          LineCap startCap, LineCap endCap,
                                          AntiAliasMode aaMode, float featherPixels,
                                          bool closed, bool useSdf)
    {
        if (pts == null || pts.Count < 2) return;
        if (lineWidth <= 0f) return;

        int useCount = Mathf.Min(pts.Count, maxPts);

        if (useSdf)
        {
            BuildSdfImpl(vh, pts, useCount, lineWidth, color, origin,
                         join, miterLimit, startCap, endCap,
                         featherPixels, closed);
            return;
        }

        // ── closed: build working point list into s_closedWorkPts ──
        IReadOnlyList<Vector2> workPts;
        int nDistinct;

        if (closed && useCount >= 3)
        {
            bool alreadyClosed = (pts[useCount - 1] - pts[0]).sqrMagnitude < 1e-6f;
            if (alreadyClosed)
            {
                workPts = pts;
                nDistinct = useCount - 1;
            }
            else
            {
                s_closedWorkPts.Clear();
                for (int i = 0; i < useCount; i++)
                    s_closedWorkPts.Add(pts[i]);
                s_closedWorkPts.Add(pts[0]);
                workPts = s_closedWorkPts;
                nDistinct = useCount;
            }
        }
        else
        {
            workPts = pts;
            nDistinct = 0;
            closed = false;
        }

        int n = workPts.Count;
        int segCount = n - 1;

        // AA parameters
        float coreHalfWidth, actualFeather, coreAlpha, featherAlpha;
        ComputeAAParams(lineWidth, featherPixels, aaMode,
                        out coreHalfWidth, out actualFeather,
                        out coreAlpha, out featherAlpha);
        bool hasAA = aaMode == AntiAliasMode.Geometric && actualFeather > 0f;

        Color coreColor = color;
        coreColor.a *= coreAlpha;
        Color featherColor = color;
        featherColor.a *= featherAlpha;

        // ── Ensure static buffers to segCount ──────────────────
        EnsureCount(s_segDir, segCount);
        EnsureCount(s_segNrm, segCount);
        EnsureCount(s_segLen, segCount);
        EnsureCount(s_plusStart, segCount);
        EnsureCount(s_minusStart, segCount);
        EnsureCount(s_plusEnd, segCount);
        EnsureCount(s_minusEnd, segCount);

        // ── Pass 1: raw per-segment data ─────────────────────
        for (int i = 0; i < segCount; i++)
        {
            Vector2 d = workPts[i + 1] - workPts[i];
            float len = d.magnitude;
            if (len < 1e-6f) { s_segLen[i] = 0f; continue; }
            d /= len;
            Vector2 nrm = new Vector2(-d.y, d.x);

            s_segDir[i] = d;
            s_segNrm[i] = nrm;
            s_segLen[i] = len;

            s_plusStart[i]  = workPts[i]     + nrm * coreHalfWidth;
            s_minusStart[i] = workPts[i]     - nrm * coreHalfWidth;
            s_plusEnd[i]    = workPts[i + 1] + nrm * coreHalfWidth;
            s_minusEnd[i]   = workPts[i + 1] - nrm * coreHalfWidth;
        }

        // ── Pass 2: joins at internal vertices ───────────────
        var joinInfos = new List<JoinInfo>();

        int joinEnd = closed ? nDistinct : (n - 1);

        for (int i = 1; i < joinEnd; i++)
        {
            int prev = i - 1;
            int next = i;

            if (s_segLen[prev] < 1e-6f || s_segLen[next] < 1e-6f) continue;

            ProcessJoin(workPts[i], prev, next,
                        coreHalfWidth, actualFeather, hasAA,
                        coreAlpha, featherAlpha,
                        join, miterLimit, featherPixels, joinInfos);
        }

        // closed: wrap join at vertex 0 between seg[segCount-1] and seg[0]
        if (closed && segCount >= 2 && s_segLen[segCount - 1] >= 1e-6f && s_segLen[0] >= 1e-6f)
        {
            ProcessJoin(workPts[0], segCount - 1, 0,
                        coreHalfWidth, actualFeather, hasAA,
                        coreAlpha, featherAlpha,
                        join, miterLimit, featherPixels, joinInfos);
        }

        // ── Pass 3: square caps (open only) ─────────────────
        if (!closed)
        {
            if (s_segLen[0] >= 1e-6f && startCap == LineCap.Square)
            {
                Vector2 ext = s_segDir[0] * coreHalfWidth;
                s_plusStart[0]  -= ext;
                s_minusStart[0] -= ext;
            }

            if (s_segLen[segCount - 1] >= 1e-6f && endCap == LineCap.Square)
            {
                Vector2 ext = s_segDir[segCount - 1] * coreHalfWidth;
                s_plusEnd[segCount - 1]  += ext;
                s_minusEnd[segCount - 1] += ext;
            }
        }

        // ── Pass 4: write segment body triangles ─────────────
        EnsureCount(s_segBase, segCount);

        for (int i = 0; i < segCount; i++)
        {
            if (s_segLen[i] < 1e-6f) { s_segBase[i] = -1; continue; }

            Vector2 v0 = s_minusStart[i];
            Vector2 v1 = s_plusStart[i];
            Vector2 v2 = s_minusEnd[i];
            Vector2 v3 = s_plusEnd[i];

            if (hasAA)
            {
                Vector2 sn = s_segNrm[i];
                Vector2 f0 = v0 - sn * actualFeather;
                Vector2 f1 = v1 + sn * actualFeather;
                Vector2 f2 = v2 - sn * actualFeather;
                Vector2 f3 = v3 + sn * actualFeather;

                int b = vh.currentVertCount;
                s_segBase[i] = b;

                vh.AddVert(new UIVertex { position = v0 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v1 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v2 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v3 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = f0 + origin, color = featherColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = f1 + origin, color = featherColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = f2 + origin, color = featherColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = f3 + origin, color = featherColor, uv0 = Vector2.zero });

                vh.AddTriangle(b + 0, b + 2, b + 1);
                vh.AddTriangle(b + 1, b + 2, b + 3);

                vh.AddTriangle(b + 0, b + 2, b + 6);
                vh.AddTriangle(b + 0, b + 6, b + 4);

                vh.AddTriangle(b + 1, b + 3, b + 7);
                vh.AddTriangle(b + 1, b + 7, b + 5);
            }
            else
            {
                int b = vh.currentVertCount;
                s_segBase[i] = b;

                vh.AddVert(new UIVertex { position = v0 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v1 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v2 + origin, color = coreColor, uv0 = Vector2.zero });
                vh.AddVert(new UIVertex { position = v3 + origin, color = coreColor, uv0 = Vector2.zero });

                vh.AddTriangle(b + 0, b + 2, b + 1);
                vh.AddTriangle(b + 1, b + 2, b + 3);
            }
        }

        // ── Pass 5: outer join geometry ──────────────────────
        foreach (var ji in joinInfos)
        {
            AddOuterJoin(vh, ji, coreColor, featherColor, origin);
        }

        // ── Pass 6: round caps (open only) ───────────────────
        if (!closed)
        {
            if (startCap == LineCap.Round && s_segLen[0] >= 1e-6f)
            {
                int capSectors = ComputeRoundSectors(Mathf.PI, coreHalfWidth, featherPixels);
                AddHalfCircleFan(vh, pts[0], s_minusStart[0], s_plusStart[0],
                                 -s_segDir[0], coreHalfWidth, capSectors,
                                 coreColor, featherColor, origin,
                                 hasAA, actualFeather, coreAlpha, featherAlpha);
            }

            if (endCap == LineCap.Round && s_segLen[segCount - 1] >= 1e-6f)
            {
                int capSectors = ComputeRoundSectors(Mathf.PI, coreHalfWidth, featherPixels);
                AddHalfCircleFan(vh, pts[Mathf.Min(segCount, pts.Count - 1)],
                                 s_minusEnd[segCount - 1], s_plusEnd[segCount - 1],
                                 s_segDir[segCount - 1], coreHalfWidth, capSectors,
                                 coreColor, featherColor, origin,
                                 hasAA, actualFeather, coreAlpha, featherAlpha);
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Internal helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 处理单个内部顶点处的 join，将 JoinInfo 写入 joinInfos。
    /// 读取并修改 s_segDir, s_segNrm, s_segLen, s_plusEnd, s_minusEnd, s_plusStart, s_minusStart。
    /// </summary>
    private static void ProcessJoin(Vector2 center,
        int prev, int next,
        float coreHalfWidth, float actualFeather, bool hasAA,
        float coreAlpha, float featherAlpha,
        LineJoin join, float miterLimit, float featherPixels,
        List<JoinInfo> joinInfos)
    {
        Vector2 dPrev = s_segDir[prev];
        Vector2 dNext = s_segDir[next];
        Vector2 nA = s_segNrm[prev];
        Vector2 nB = s_segNrm[next];

        float crossVal = dPrev.x * dNext.y - dPrev.y * dNext.x;
        float dotDir = dPrev.x * dNext.x + dPrev.y * dNext.y;

        if (Mathf.Abs(crossVal) < 1e-6f && dotDir > 0.9999f) return;

        bool isUturn = dotDir < -0.9999f;

        if (isUturn)
        {
            float uSweepA = AngleBetween(center, s_minusEnd[prev], s_minusStart[next]);
            float uSweepB = AngleBetween(center, s_plusEnd[prev], s_plusStart[next]);
            int uSecA = ComputeRoundSectors(uSweepA, coreHalfWidth, featherPixels);
            int uSecB = ComputeRoundSectors(uSweepB, coreHalfWidth, featherPixels);

            joinInfos.Add(new JoinInfo
            {
                center = center,
                outerPrev = s_plusEnd[prev],
                outerNext = s_plusStart[next],
                outerMiter = Vector2.zero,
                featherPrev = hasAA ? s_plusEnd[prev] + nA * actualFeather : Vector2.zero,
                featherNext = hasAA ? s_plusStart[next] + nB * actualFeather : Vector2.zero,
                featherMiter = Vector2.zero,
                join = LineJoin.Round,
                miterLimit = 0f,
                mDotNA = 0f,
                coreHalfWidth = coreHalfWidth,
                roundSectors = uSecA,
                hasAA = hasAA,
                actualFeather = actualFeather,
                coreAlpha = coreAlpha,
                featherAlpha = featherAlpha,
            });
            joinInfos.Add(new JoinInfo
            {
                center = center,
                outerPrev = s_minusEnd[prev],
                outerNext = s_minusStart[next],
                outerMiter = Vector2.zero,
                featherPrev = hasAA ? s_minusEnd[prev] - nA * actualFeather : Vector2.zero,
                featherNext = hasAA ? s_minusStart[next] - nB * actualFeather : Vector2.zero,
                featherMiter = Vector2.zero,
                join = LineJoin.Round,
                miterLimit = 0f,
                mDotNA = 0f,
                coreHalfWidth = coreHalfWidth,
                roundSectors = uSecB,
                hasAA = hasAA,
                actualFeather = actualFeather,
                coreAlpha = coreAlpha,
                featherAlpha = featherAlpha,
            });
            return;
        }

        Vector2 m = (nA + nB).normalized;
        float s = crossVal > 0f ? 1f : -1f;
        float mDotNA = Vector2.Dot(m, nA);

        if (Mathf.Abs(mDotNA) < 1e-6f) return;

        float miterDist = coreHalfWidth / mDotNA;
        float clampedMDist = Mathf.Min(miterDist,
            s_segLen[prev] * 0.5f, s_segLen[next] * 0.5f);

        Vector2 innerMiter = center + s * m * clampedMDist;

        bool plusIsInner = Vector2.Dot(s * m, nA) > 0f;

        if (plusIsInner)
        {
            s_plusEnd[prev]   = innerMiter;
            s_plusStart[next] = innerMiter;
        }
        else
        {
            s_minusEnd[prev]   = innerMiter;
            s_minusStart[next] = innerMiter;
        }

        Vector2 outerPrev = plusIsInner ? s_minusEnd[prev] : s_plusEnd[prev];
        Vector2 outerNext = plusIsInner ? s_minusStart[next] : s_plusStart[next];
        Vector2 outerMiter = center + (-s) * m * miterDist;
        Vector2 nO = plusIsInner ? -nA : nA;
        Vector2 nP = plusIsInner ? -nB : nB;

        float sweepA = AngleBetween(center, outerPrev, outerNext);
        int secs = ComputeRoundSectors(sweepA, coreHalfWidth, featherPixels);

        joinInfos.Add(new JoinInfo
        {
            center = center,
            outerPrev = outerPrev,
            outerNext = outerNext,
            outerMiter = outerMiter,
            featherPrev = hasAA ? outerPrev + nO * actualFeather : Vector2.zero,
            featherNext = hasAA ? outerNext + nP * actualFeather : Vector2.zero,
            featherMiter = hasAA ? outerMiter + (-s) * m * actualFeather : Vector2.zero,
            join = join,
            miterLimit = miterLimit,
            mDotNA = mDotNA,
            coreHalfWidth = coreHalfWidth,
            roundSectors = secs,
            hasAA = hasAA,
            actualFeather = actualFeather,
            coreAlpha = coreAlpha,
            featherAlpha = featherAlpha,
        });
    }

    private struct JoinInfo
    {
        public Vector2 center, outerPrev, outerNext, outerMiter;
        public Vector2 featherPrev, featherNext, featherMiter;
        public LineJoin join;
        public float miterLimit, mDotNA, coreHalfWidth;
        public int roundSectors;
        public bool hasAA;
        public float actualFeather, coreAlpha, featherAlpha;
    }

    private static void AddOuterJoin(VertexHelper vh, JoinInfo ji,
        Color coreColor, Color featherColor, Vector2 origin)
    {
        Vector2 op = ji.outerPrev + origin;
        Vector2 on = ji.outerNext + origin;
        Vector2 pc = ji.center   + origin;

        switch (ji.join)
        {
            case LineJoin.Bevel:
            {
                int b = vh.currentVertCount;
                vh.AddVert(MakeVert(pc, coreColor));

                if (ji.hasAA)
                {
                    Vector2 fp = ji.featherPrev + origin;
                    Vector2 fn = ji.featherNext + origin;
                    vh.AddVert(MakeVert(op, coreColor));
                    vh.AddVert(MakeVert(on, coreColor));
                    vh.AddVert(MakeVert(fp, featherColor));
                    vh.AddVert(MakeVert(fn, featherColor));
                    vh.AddTriangle(b + 1, b, b + 2);
                    vh.AddTriangle(b + 1, b + 2, b + 4);
                    vh.AddTriangle(b + 1, b + 4, b + 3);
                }
                else
                {
                    vh.AddVert(MakeVert(op, coreColor));
                    vh.AddVert(MakeVert(on, coreColor));
                    vh.AddTriangle(b, b + 1, b + 2);
                }
                break;
            }

            case LineJoin.Miter:
            {
                if (1f / ji.mDotNA > ji.miterLimit)
                    goto case LineJoin.Bevel;

                Vector2 om = ji.outerMiter + origin;

                if (ji.hasAA)
                {
                    Vector2 fp = ji.featherPrev + origin;
                    Vector2 fm = ji.featherMiter + origin;
                    Vector2 fn = ji.featherNext + origin;

                    int b = vh.currentVertCount;
                    vh.AddVert(MakeVert(pc, coreColor));
                    vh.AddVert(MakeVert(op, coreColor));
                    vh.AddVert(MakeVert(om, coreColor));
                    vh.AddVert(MakeVert(on, coreColor));
                    vh.AddVert(MakeVert(fp, featherColor));
                    vh.AddVert(MakeVert(fm, featherColor));
                    vh.AddVert(MakeVert(fn, featherColor));

                    vh.AddTriangle(b + 1, b, b + 2);
                    vh.AddTriangle(b + 2, b, b + 3);
                    vh.AddTriangle(b + 1, b + 2, b + 5);
                    vh.AddTriangle(b + 1, b + 5, b + 4);
                    vh.AddTriangle(b + 2, b + 3, b + 6);
                    vh.AddTriangle(b + 2, b + 6, b + 5);
                }
                else
                {
                    int b = vh.currentVertCount;
                    vh.AddVert(MakeVert(op, coreColor));
                    vh.AddVert(MakeVert(om, coreColor));
                    vh.AddVert(MakeVert(pc, coreColor));
                    vh.AddVert(MakeVert(on, coreColor));
                    vh.AddTriangle(b,     b + 2, b + 1);
                    vh.AddTriangle(b + 1, b + 2, b + 3);
                }
                break;
            }

            case LineJoin.Round:
            {
                AddRoundFan(vh, pc, op, on, ji.coreHalfWidth, ji.roundSectors,
                            coreColor, featherColor,
                            ji.hasAA, ji.actualFeather, ji.coreAlpha, ji.featherAlpha);
                break;
            }
        }
    }

    private static void AddRoundFan(VertexHelper vh, Vector2 center,
                                     Vector2 from, Vector2 to,
                                     float coreRadius, int sectors,
                                     Color coreColor, Color featherColor,
                                     bool hasAA, float actualFeather,
                                     float coreAlpha, float featherAlpha)
    {
        Vector2 df = (from - center).normalized;
        Vector2 dt = (to   - center).normalized;

        float a0 = Mathf.Atan2(df.y, df.x);
        float a1 = Mathf.Atan2(dt.y, dt.x);

        float sweep = a1 - a0;
        float cross = df.x * dt.y - df.y * dt.x;
        if (cross > 0f && sweep < 0f) sweep += 2f * Mathf.PI;
        if (cross < 0f && sweep > 0f) sweep -= 2f * Mathf.PI;

        int steps = Mathf.Max(2, sectors);
        float step = sweep / steps;

        if (hasAA)
        {
            float featherRadius = coreRadius + actualFeather;
            int baseIdx = vh.currentVertCount;

            vh.AddVert(MakeVert(center, coreColor));

            for (int i = 0; i <= steps; i++)
            {
                float a = a0 + step * i;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * coreRadius;
                vh.AddVert(MakeVert(p, coreColor));
            }

            for (int i = 0; i <= steps; i++)
            {
                float a = a0 + step * i;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * featherRadius;
                vh.AddVert(MakeVert(p, featherColor));
            }

            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(baseIdx, baseIdx + 1 + i, baseIdx + 2 + i);
            }

            int coreRingStart = baseIdx + 1;
            int featherRingStart = baseIdx + 1 + (steps + 1);
            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(coreRingStart + i,
                               coreRingStart + i + 1,
                               featherRingStart + i + 1);
                vh.AddTriangle(coreRingStart + i,
                               featherRingStart + i + 1,
                               featherRingStart + i);
            }
        }
        else
        {
            int baseIdx = vh.currentVertCount;
            vh.AddVert(MakeVert(center, coreColor));

            for (int i = 0; i <= steps; i++)
            {
                float a = a0 + step * i;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * coreRadius;
                vh.AddVert(MakeVert(p, coreColor));
            }

            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(baseIdx, baseIdx + 1 + i, baseIdx + 2 + i);
            }
        }
    }

    /// <summary>
    /// Half-circle fan centered at centerPt, sweeping from sideA to sideB
    /// through outwardDir. Core radius = coreRadius.
    /// </summary>
    private static void AddHalfCircleFan(VertexHelper vh, Vector2 centerPt,
                                          Vector2 sideA, Vector2 sideB,
                                          Vector2 outwardDir,
                                          float coreRadius, int sectors,
                                          Color coreColor, Color featherColor,
                                          Vector2 origin,
                                          bool hasAA, float actualFeather,
                                          float coreAlpha, float featherAlpha)
    {
        Vector2 c = centerPt + origin;
        Vector2 dA = (sideA - centerPt).normalized;
        Vector2 dB = (sideB - centerPt).normalized;

        float aA = Mathf.Atan2(dA.y, dA.x);
        float aB = Mathf.Atan2(dB.y, dB.x);

        float sweep = aA - aB;
        float aOut = Mathf.Atan2(outwardDir.y, outwardDir.x);

        float diff = aOut - aB;
        while (diff >  Mathf.PI) diff -= 2f * Mathf.PI;
        while (diff < -Mathf.PI) diff += 2f * Mathf.PI;

        float rawSweep = aA - aB;
        while (rawSweep >  Mathf.PI) rawSweep -= 2f * Mathf.PI;
        while (rawSweep < -Mathf.PI) rawSweep += 2f * Mathf.PI;

        if (rawSweep > 0f)
        {
            if (diff < 0f || diff > Mathf.PI) sweep = rawSweep - 2f * Mathf.PI;
            else sweep = rawSweep;
        }
        else
        {
            if (diff > 0f || diff < -Mathf.PI) sweep = rawSweep + 2f * Mathf.PI;
            else sweep = rawSweep;
        }

        int steps = Mathf.Max(2, sectors);
        float step = sweep / steps;

        if (hasAA)
        {
            float featherRadius = coreRadius + actualFeather;
            int baseIdx = vh.currentVertCount;

            vh.AddVert(MakeVert(c, coreColor));

            for (int i = 0; i <= steps; i++)
            {
                float a = aB + step * i;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * coreRadius;
                vh.AddVert(MakeVert(p, coreColor));
            }

            for (int i = 0; i <= steps; i++)
            {
                float a = aB + step * i;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * featherRadius;
                vh.AddVert(MakeVert(p, featherColor));
            }

            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(baseIdx, baseIdx + 1 + i, baseIdx + 2 + i);
            }

            int coreRingStart = baseIdx + 1;
            int featherRingStart = baseIdx + 1 + (steps + 1);
            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(coreRingStart + i,
                               coreRingStart + i + 1,
                               featherRingStart + i + 1);
                vh.AddTriangle(coreRingStart + i,
                               featherRingStart + i + 1,
                               featherRingStart + i);
            }
        }
        else
        {
            int baseIdx = vh.currentVertCount;
            vh.AddVert(MakeVert(c, coreColor));

            for (int i = 0; i <= steps; i++)
            {
                float a = aB + step * i;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * coreRadius;
                vh.AddVert(MakeVert(p, coreColor));
            }

            for (int i = 0; i < steps; i++)
            {
                vh.AddTriangle(baseIdx, baseIdx + 1 + i, baseIdx + 2 + i);
            }
        }
    }

    private static float AngleBetween(Vector2 center, Vector2 a, Vector2 b)
    {
        Vector2 da = (a - center).normalized;
        Vector2 db = (b - center).normalized;
        float dot = Mathf.Clamp(Vector2.Dot(da, db), -1f, 1f);
        return Mathf.Acos(dot);
    }

    private static UIVertex MakeVert(Vector2 pos, Color color)
    {
        return new UIVertex { position = pos, color = color, uv0 = Vector2.zero };
    }

    // ══════════════════════════════════════════════════════════
    //  SDF 实现（段 quad + 圆盘 quad，严格不重叠）
    // ══════════════════════════════════════════════════════════

    static readonly List<Vector2> s_sdfWorkPts = new List<Vector2>();
    static readonly List<Vector2> s_sdfDiskCenters = new List<Vector2>();

    private static void BuildSdfImpl(VertexHelper vh, IReadOnlyList<Vector2> pts,
                                      int maxPts,
                                      float lineWidth, Color color, Vector2 origin,
                                      LineJoin join, float miterLimit,
                                      LineCap startCap, LineCap endCap,
                                      float featherPixels, bool closed)
    {
        float r = lineWidth * 0.5f;
        float sigma = Mathf.Max(0f, featherPixels);
        float eta = r + sigma;

        int useCount = Mathf.Min(pts.Count, maxPts);

        // ── closed: build working point list ─────────────────
        IReadOnlyList<Vector2> workPts;
        int nDistinct;

        if (closed && useCount >= 3)
        {
            bool alreadyClosed = (pts[useCount - 1] - pts[0]).sqrMagnitude < 1e-6f;
            if (alreadyClosed)
            {
                workPts = pts;
                nDistinct = useCount - 1;
            }
            else
            {
                s_sdfWorkPts.Clear();
                for (int i = 0; i < useCount; i++)
                    s_sdfWorkPts.Add(pts[i]);
                s_sdfWorkPts.Add(pts[0]);
                workPts = s_sdfWorkPts;
                nDistinct = useCount;
            }
        }
        else
        {
            workPts = pts;
            nDistinct = 0;
            closed = false;
        }

        int n = workPts.Count;
        int segCount = n - 1;
        if (segCount < 1) return;

        // ── Ensure static buffers to segCount ──────────────────
        EnsureCount(s_segDir, segCount);
        EnsureCount(s_segNrm, segCount);
        EnsureCount(s_segLen, segCount);
        EnsureCount(s_totalLen, segCount);
        EnsureCount(s_minusStart, segCount);
        EnsureCount(s_plusStart, segCount);
        EnsureCount(s_minusEnd, segCount);
        EnsureCount(s_plusEnd, segCount);

        // ── Per-segment geometry ──────────────────────────────
        for (int i = 0; i < segCount; i++)
        {
            Vector2 d = workPts[i + 1] - workPts[i];
            float len = d.magnitude;
            if (len < 1e-6f) { s_segLen[i] = 0f; s_totalLen[i] = 0f; continue; }
            d /= len;
            Vector2 nm = new Vector2(-d.y, d.x);

            s_segDir[i] = d;
            s_segNrm[i] = nm;
            s_segLen[i] = len;
            s_totalLen[i] = len;

            s_minusStart[i] = workPts[i]     - nm * eta;
            s_plusStart[i]  = workPts[i]     + nm * eta;
            s_minusEnd[i]   = workPts[i + 1] - nm * eta;
            s_plusEnd[i]    = workPts[i + 1] + nm * eta;
        }

        // ── Square caps extend segment quads (open only) ─────
        if (!closed)
        {
            if (s_segLen[0] >= 1e-6f && startCap == LineCap.Square)
            {
                Vector2 ext = s_segDir[0] * r;
                s_minusStart[0] -= ext;
                s_plusStart[0]  -= ext;
                s_totalLen[0]   += r;
            }
            if (s_segLen[segCount - 1] >= 1e-6f && endCap == LineCap.Square)
            {
                Vector2 ext = s_segDir[segCount - 1] * r;
                s_minusEnd[segCount - 1] += ext;
                s_plusEnd[segCount - 1]  += ext;
                s_totalLen[segCount - 1] += r;
            }
        }

        // ── Disk quad list ────────────────────────────────────
        s_sdfDiskCenters.Clear();

        // ── Process internal joins ────────────────────────────
        int joinEnd = closed ? nDistinct : (n - 1);
        for (int i = 1; i < joinEnd; i++)
        {
            int prev = i - 1;
            int next = i;
            if (s_segLen[prev] < 1e-6f || s_segLen[next] < 1e-6f) continue;

            ProcessSdfJoin(workPts[i], prev, next,
                r, eta, join, miterLimit);
        }

        if (closed && segCount >= 2 && s_segLen[segCount - 1] >= 1e-6f && s_segLen[0] >= 1e-6f)
        {
            ProcessSdfJoin(workPts[0], segCount - 1, 0,
                r, eta, join, miterLimit);
        }

        // ── Round caps (open only) ────────────────────────────
        if (!closed)
        {
            if (startCap == LineCap.Round && s_segLen[0] >= 1e-6f)
                s_sdfDiskCenters.Add(workPts[0]);
            if (endCap == LineCap.Round && s_segLen[segCount - 1] >= 1e-6f)
                s_sdfDiskCenters.Add(workPts[segCount - 1]);
        }

        // ── Write segment quads ───────────────────────────────
        for (int i = 0; i < segCount; i++)
        {
            if (s_segLen[i] < 1e-6f || s_totalLen[i] < 1e-6f) continue;

            float tLen = s_totalLen[i];
            Vector2 segStart = workPts[i];
            Vector2 segEnd   = workPts[i + 1];

            float ums = Vector2.Dot(s_minusStart[i] - segStart, s_segNrm[i]);
            float ups = Vector2.Dot(s_plusStart[i]  - segStart, s_segNrm[i]);
            float ume = Vector2.Dot(s_minusEnd[i]   - segEnd,   s_segNrm[i]);
            float upe = Vector2.Dot(s_plusEnd[i]    - segEnd,   s_segNrm[i]);

            int b = vh.currentVertCount;

            vh.AddVert(new UIVertex
            {
                position = s_minusStart[i] + origin,
                color = color,
                uv0 = new Vector4(0f, ums, tLen, 0f),
                uv1 = new Vector4(r, sigma, 0f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = s_plusStart[i] + origin,
                color = color,
                uv0 = new Vector4(0f, ups, tLen, 0f),
                uv1 = new Vector4(r, sigma, 0f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = s_minusEnd[i] + origin,
                color = color,
                uv0 = new Vector4(tLen, ume, tLen, 0f),
                uv1 = new Vector4(r, sigma, 0f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = s_plusEnd[i] + origin,
                color = color,
                uv0 = new Vector4(tLen, upe, tLen, 0f),
                uv1 = new Vector4(r, sigma, 0f, 0f),
            });

            vh.AddTriangle(b,     b + 2, b + 1);
            vh.AddTriangle(b + 1, b + 2, b + 3);
        }

        // ── Write disk quads ──────────────────────────────────
        float halfSz = eta;
        foreach (var center in s_sdfDiskCenters)
        {
            Vector2 c = center + origin;
            int b = vh.currentVertCount;

            vh.AddVert(new UIVertex
            {
                position = new Vector3(c.x - halfSz, c.y - halfSz, 0f),
                color = color,
                uv0 = new Vector4(-halfSz, -halfSz, 0f, 0f),
                uv1 = new Vector4(r, sigma, 1f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(c.x - halfSz, c.y + halfSz, 0f),
                color = color,
                uv0 = new Vector4(-halfSz, halfSz, 0f, 0f),
                uv1 = new Vector4(r, sigma, 1f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(c.x + halfSz, c.y - halfSz, 0f),
                color = color,
                uv0 = new Vector4(halfSz, -halfSz, 0f, 0f),
                uv1 = new Vector4(r, sigma, 1f, 0f),
            });
            vh.AddVert(new UIVertex
            {
                position = new Vector3(c.x + halfSz, c.y + halfSz, 0f),
                color = color,
                uv0 = new Vector4(halfSz, halfSz, 0f, 0f),
                uv1 = new Vector4(r, sigma, 1f, 0f),
            });

            vh.AddTriangle(b,     b + 2, b + 1);
            vh.AddTriangle(b + 1, b + 2, b + 3);
        }
    }

    /// <summary>
    /// SDF join 处理：调整相邻段 quad 的端面顶点，并在 Round 时记录圆盘到 s_sdfDiskCenters。
    /// </summary>
    private static void ProcessSdfJoin(Vector2 center,
        int prev, int next,
        float r, float eta, LineJoin join, float miterLimit)
    {
        Vector2 dPrev = s_segDir[prev];
        Vector2 dNext = s_segDir[next];
        Vector2 nA = s_segNrm[prev];
        Vector2 nB = s_segNrm[next];

        float crossVal = dPrev.x * dNext.y - dPrev.y * dNext.x;
        float dotDir = dPrev.x * dNext.x + dPrev.y * dNext.y;

        if (Mathf.Abs(crossVal) < 1e-6f && dotDir > 0.9999f) return;

        if (dotDir < -0.9999f)
        {
            s_sdfDiskCenters.Add(center);
            return;
        }

        Vector2 m = (nA + nB).normalized;
        float s = crossVal > 0f ? 1f : -1f;
        float mDotNA = Vector2.Dot(m, nA);
        if (Mathf.Abs(mDotNA) < 1e-6f) return;

        float miterDist = r / mDotNA;
        float clampedMDist = Mathf.Min(miterDist,
            s_segLen[prev] * 0.5f, s_segLen[next] * 0.5f);

        Vector2 innerPt = center + s * m * clampedMDist;
        bool plusIsInner = Vector2.Dot(s * m, nA) > 0f;

        if (plusIsInner)
        {
            s_plusEnd[prev]   = innerPt;
            s_plusStart[next] = innerPt;
        }
        else
        {
            s_minusEnd[prev]   = innerPt;
            s_minusStart[next] = innerPt;
        }

        LineJoin effectiveJoin = join;
        if (join == LineJoin.Miter && 1f / mDotNA > miterLimit)
            effectiveJoin = LineJoin.Round;

        if (effectiveJoin == LineJoin.Round)
        {
            s_sdfDiskCenters.Add(center);
        }
        else if (effectiveJoin == LineJoin.Miter)
        {
            Vector2 outerPt = center + (-s) * m * miterDist;
            if (plusIsInner)
            {
                s_minusEnd[prev]   = outerPt;
                s_minusStart[next] = outerPt;
            }
            else
            {
                s_plusEnd[prev]   = outerPt;
                s_plusStart[next] = outerPt;
            }
        }
    }
}
