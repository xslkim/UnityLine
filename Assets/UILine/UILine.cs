using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UGUI 折线绘制组件。在 RectTransform 范围内给定点序列，
/// 以指定线宽和颜色绘制折线。
/// M7 实现 SDF AA 路线。M9 脏标节流、顶点上限截断。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UILine : MaskableGraphic
{
    // ── 基础 ──────────────────────────────
    [SerializeField] private List<Vector2> _points = new List<Vector2>();
    [SerializeField] private float _lineWidth = 4f;

    // ── 接头 / 端点 ───────────────────────
    [SerializeField] private LineCap _startCap = LineCap.Butt;
    [SerializeField] private LineCap _endCap = LineCap.Butt;
    [SerializeField] private LineJoin _join = LineJoin.Round;
    [SerializeField] private float _miterLimit = 4f;

    // ── 抗锯齿 ────────────────────────────
    [SerializeField] private AntiAliasMode _aaMode = AntiAliasMode.Geometric;
    [SerializeField] private float _featherPixels = 1f;

    // ── 虚线 ──────────────────────────────
    [SerializeField] private DashStyle _dashStyle = DashStyle.Solid;
    [SerializeField] private float _dashLength = 8f;
    [SerializeField] private float _gapLength = 4f;
    [SerializeField] private float _dashOffset = 0f;

    // ── 其他 ──────────────────────────────
    [SerializeField] private bool _closed = false;
    [SerializeField] private Material _sdfMaterial = null;

    private bool _sdfWarnLogged;
    private bool _truncateWarnLogged;

    /// <summary>虚线裁切结果缓冲，避免每帧 alloc。</summary>
    static readonly List<List<Vector2>> s_dashBuffer = new List<List<Vector2>>();

    // ══════════════════════════════════════════
    //  公开属性
    // ══════════════════════════════════════════

    public IReadOnlyList<Vector2> Points => _points;
    public int PointCount => _points.Count;

    public float LineWidth
    {
        get => _lineWidth;
        set
        {
            if (Mathf.Approximately(_lineWidth, value)) return;
            _lineWidth = value;
            SetVerticesDirty();
        }
    }

    public LineJoin Join
    {
        get => _join;
        set
        {
            if (_join == value) return;
            _join = value;
            SetVerticesDirty();
        }
    }

    public float MiterLimit
    {
        get => _miterLimit;
        set
        {
            if (Mathf.Approximately(_miterLimit, value)) return;
            _miterLimit = value;
            SetVerticesDirty();
        }
    }

    public LineCap StartCap
    {
        get => _startCap;
        set
        {
            if (_startCap == value) return;
            _startCap = value;
            SetVerticesDirty();
        }
    }

    public LineCap EndCap
    {
        get => _endCap;
        set
        {
            if (_endCap == value) return;
            _endCap = value;
            SetVerticesDirty();
        }
    }

    public AntiAliasMode AAMode
    {
        get => _aaMode;
        set
        {
            if (_aaMode == value) return;
            _aaMode = value;
            _sdfWarnLogged = false;
            SetVerticesDirty();
            SetMaterialDirty();
        }
    }

    public float FeatherPixels
    {
        get => _featherPixels;
        set
        {
            if (Mathf.Approximately(_featherPixels, value)) return;
            _featherPixels = value;
            SetVerticesDirty();
        }
    }

    public bool Closed
    {
        get => _closed;
        set
        {
            if (_closed == value) return;
            _closed = value;
            SetVerticesDirty();
        }
    }

    public DashStyle DashStyle
    {
        get => _dashStyle;
        set
        {
            if (_dashStyle == value) return;
            _dashStyle = value;
            SetVerticesDirty();
        }
    }

    public float DashLength
    {
        get => _dashLength;
        set
        {
            if (Mathf.Approximately(_dashLength, value)) return;
            _dashLength = value;
            SetVerticesDirty();
        }
    }

    public float GapLength
    {
        get => _gapLength;
        set
        {
            if (Mathf.Approximately(_gapLength, value)) return;
            _gapLength = value;
            SetVerticesDirty();
        }
    }

    public float DashOffset
    {
        get => _dashOffset;
        set
        {
            if (Mathf.Approximately(_dashOffset, value)) return;
            _dashOffset = value;
            SetVerticesDirty();
        }
    }

    public Material SdfMaterial
    {
        get => _sdfMaterial;
        set
        {
            if (_sdfMaterial == value) return;
            _sdfMaterial = value;
            _sdfWarnLogged = false;
            SetVerticesDirty();
            SetMaterialDirty();
        }
    }

    /// <summary>
    /// SDF 模式下返回 SdfMaterial；若未赋值则回退 Geometric 并报一次 warning。
    /// </summary>
    public override Material materialForRendering
    {
        get
        {
            if (_aaMode == AntiAliasMode.Sdf)
            {
                if (_sdfMaterial != null) return _sdfMaterial;
                if (!_sdfWarnLogged)
                {
                    Debug.LogWarning("[UILine] AAMode=Sdf 但 SdfMaterial 未赋值，已回退 Geometric");
                    _sdfWarnLogged = true;
                }
            }
            return base.materialForRendering;
        }
    }

    // ══════════════════════════════════════════
    //  运行时方法
    // ══════════════════════════════════════════

    /// <summary>拷贝写入点序列并标脏。</summary>
    public void SetPoints(IList<Vector2> pts)
    {
        _points.Clear();
        if (pts != null)
        {
            for (int i = 0; i < pts.Count; i++)
                _points.Add(pts[i]);
        }
        SetVerticesDirty();
    }

    /// <summary>从数组前 count 个元素拷贝写入。</summary>
    public void SetPoints(Vector2[] pts, int count)
    {
        _points.Clear();
        if (pts != null)
        {
            int n = Mathf.Min(count, pts.Length);
            for (int i = 0; i < n; i++)
                _points.Add(pts[i]);
        }
        SetVerticesDirty();
    }

    /// <summary>追加一个点。</summary>
    public void AddPoint(Vector2 p)
    {
        _points.Add(p);
        SetVerticesDirty();
    }

    /// <summary>清空全部点。</summary>
    public void ClearPoints()
    {
        _points.Clear();
        SetVerticesDirty();
    }

    // ══════════════════════════════════════════
    //  重写
    // ══════════════════════════════════════════

    private const int kVertexLimit = 60000;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_points.Count < 2)
            return;

        int maxPts = _points.Count;
        int vEst = UILineBuilder.EstimateVertices(_points.Count, _join, _startCap, _endCap, _aaMode, _featherPixels);
        if (vEst > kVertexLimit)
        {
            int lo = 2, hi = _points.Count;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (UILineBuilder.EstimateVertices(mid, _join, _startCap, _endCap, _aaMode, _featherPixels) <= kVertexLimit)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            maxPts = lo;
            if (!_truncateWarnLogged)
            {
                Debug.LogWarning($"[UILine] 顶点估算超 65535（估 {vEst}），点数 {_points.Count} 截断为 {maxPts}，已抑制后续警告");
                _truncateWarnLogged = true;
            }
        }

        Vector2 origin = rectTransform.rect.min;
        UILineBuilder.Build(vh, _points, _lineWidth, color, origin,
                            _join, _miterLimit, _startCap, _endCap,
                            _aaMode, _featherPixels,
                            _dashStyle, _dashLength, _gapLength, _dashOffset,
                            _closed, _sdfMaterial, maxPts);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetAllDirty();
    }
#endif
}
