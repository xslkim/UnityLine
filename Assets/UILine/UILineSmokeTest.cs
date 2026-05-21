using UnityEngine;

/// <summary>
/// M7 烟测脚本：覆盖 AA 组合 + 虚线 + 闭合 + SDF。
/// 挂在带 UILine 组件的 GameObject 上。
/// </summary>
[RequireComponent(typeof(UILine))]
public class UILineSmokeTest : MonoBehaviour
{
    [Header("切换周期（秒）")]
    [SerializeField] private float _cycleInterval = 3f;

    [Header("蚂蚁线滚动速度（px/秒）")]
    [SerializeField] private float _dashScrollSpeed = 30f;

    [Header("SDF 材质（用于 SDF 测试状态）")]
    [SerializeField] private Material _sdfMat;

    private UILine _line;
    private float _timer;
    private int _state;

    // AA 测试组合 + 虚线 + 闭合 + SDF
    private enum SmokeState { None, Geometric_1, Geometric_2, NarrowLine, Dashed_Round, Dashed_Butt, Closed_Triangle, Closed_RoundJoin, Closed_Dashed, Sdf_Round, Sdf_SemiTransparent, Sdf_MiterDegenerate, Sdf_Bevel }
    private static readonly int StateCount = 13;

    private static readonly Vector2[] DemoPoints =
    {
        new Vector2(50,  50),
        new Vector2(200, 200),  // ~56 deg
        new Vector2(400, 80),   // ~76 deg
        new Vector2(550, 250),  // ~79 deg
        new Vector2(600, 60),   // ~124 deg obtuse
        new Vector2(750, 60),   // ~75 deg
    };

    private static readonly Vector2[] TrianglePoints =
    {
        new Vector2(100, 100),
        new Vector2(300, 100),
        new Vector2(200, 273),
    };

    private static readonly Vector2[] QuadPoints =
    {
        new Vector2(80,  80),
        new Vector2(320, 80),
        new Vector2(320, 320),
        new Vector2(80,  320),
    };

    private static readonly Vector2[] PentagonPoints =
    {
        new Vector2(200,  50),
        new Vector2(343, 154),
        new Vector2(288, 312),
        new Vector2(112, 312),
        new Vector2( 57, 154),
    };

    // Sharp-angle points for SDF miter degenerate test
    private static readonly Vector2[] SharpAnglePoints =
    {
        new Vector2(50,  150),
        new Vector2(375, 150),
        new Vector2(380, 50),   // ~5 deg turn
    };

    private void Start()
    {
        _line = GetComponent<UILine>();
        ApplyState();
    }

    private void Update()
    {
        _timer += Time.deltaTime;

        // 虚线模式持续滚动 dashOffset（蚂蚁线效果）
        if (_line.DashStyle == DashStyle.Dashed)
        {
            _line.DashOffset += Time.deltaTime * _dashScrollSpeed;
        }

        if (_timer >= _cycleInterval)
        {
            _timer = 0f;
            _state = (_state + 1) % StateCount;
            ApplyState();
        }
    }

    private void ApplyState()
    {
        if (_line == null) return;

        _line.SetPoints(DemoPoints);
        _line.Join = LineJoin.Round;
        _line.MiterLimit = 4f;
        _line.Closed = false;
        _line.color = Color.white;
        _line.SdfMaterial = null;

        SmokeState s = (SmokeState)_state;
        switch (s)
        {
            case SmokeState.None:
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.None;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Solid;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                break;

            case SmokeState.Geometric_1:
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Solid;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                break;

            case SmokeState.Geometric_2:
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 2f;
                _line.DashStyle = DashStyle.Solid;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                break;

            case SmokeState.NarrowLine:
                _line.LineWidth = 0.5f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Solid;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                break;

            case SmokeState.Dashed_Round:
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Dashed;
                _line.DashLength = 10f;
                _line.GapLength = 5f;
                _line.DashOffset = 0f;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                break;

            case SmokeState.Dashed_Butt:
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Dashed;
                _line.DashLength = 10f;
                _line.GapLength = 5f;
                _line.DashOffset = 0f;
                _line.StartCap = LineCap.Butt;
                _line.EndCap = LineCap.Butt;
                break;

            case SmokeState.Closed_Triangle:
                _line.SetPoints(TrianglePoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Round;
                _line.Closed = true;
                break;

            case SmokeState.Closed_RoundJoin:
                _line.SetPoints(QuadPoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Round;
                _line.Closed = true;
                break;

            case SmokeState.Closed_Dashed:
                _line.SetPoints(PentagonPoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Geometric;
                _line.FeatherPixels = 1f;
                _line.DashStyle = DashStyle.Dashed;
                _line.DashLength = 15f;
                _line.GapLength = 6f;
                _line.DashOffset = 0f;
                _line.Join = LineJoin.Round;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                _line.Closed = true;
                break;

            // ── SDF 测试 ──────────────────────────────────

            case SmokeState.Sdf_Round:
                _line.SdfMaterial = _sdfMat;
                _line.SetPoints(DemoPoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Sdf;
                _line.FeatherPixels = 1.5f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Round;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                _line.Closed = false;
                break;

            case SmokeState.Sdf_SemiTransparent:
                _line.SdfMaterial = _sdfMat;
                _line.SetPoints(DemoPoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Sdf;
                _line.FeatherPixels = 1.5f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Round;
                _line.StartCap = LineCap.Round;
                _line.EndCap = LineCap.Round;
                _line.Closed = false;
                // color alpha set by Graphic.color; use a semi-transparent color
                _line.color = new Color(1f, 1f, 1f, 0.3f);
                break;

            case SmokeState.Sdf_MiterDegenerate:
                _line.SdfMaterial = _sdfMat;
                _line.SetPoints(SharpAnglePoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Sdf;
                _line.FeatherPixels = 1.5f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Miter;
                _line.MiterLimit = 4f;
                _line.StartCap = LineCap.Butt;
                _line.EndCap = LineCap.Butt;
                _line.Closed = false;
                _line.color = Color.white;
                break;

            case SmokeState.Sdf_Bevel:
                _line.SdfMaterial = _sdfMat;
                _line.SetPoints(DemoPoints);
                _line.LineWidth = 6f;
                _line.AAMode = AntiAliasMode.Sdf;
                _line.FeatherPixels = 1.5f;
                _line.DashStyle = DashStyle.Solid;
                _line.Join = LineJoin.Bevel;
                _line.StartCap = LineCap.Butt;
                _line.EndCap = LineCap.Butt;
                _line.Closed = false;
                _line.color = Color.white;
                break;
        }
    }
}
