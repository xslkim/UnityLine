using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(UILine))]
public class UILineEditor : Editor
{
    // ── SerializedProperty ──────────────────
    private SerializedProperty _points;
    private SerializedProperty _lineWidth;
    private SerializedProperty _mColor;
    private SerializedProperty _startCap;
    private SerializedProperty _endCap;
    private SerializedProperty _join;
    private SerializedProperty _miterLimit;
    private SerializedProperty _aaMode;
    private SerializedProperty _featherPixels;
    private SerializedProperty _sdfMaterial;
    private SerializedProperty _dashStyle;
    private SerializedProperty _dashLength;
    private SerializedProperty _gapLength;
    private SerializedProperty _dashOffset;
    private SerializedProperty _closed;

    // ── Foldout states ──────────────────────
    private bool _foldBase = true;
    private bool _foldJoin = true;
    private bool _foldAA = true;
    private bool _foldDash = true;
    private bool _foldAdv;

    private void OnEnable()
    {
        _points       = serializedObject.FindProperty("_points");
        _lineWidth    = serializedObject.FindProperty("_lineWidth");
        _mColor       = serializedObject.FindProperty("m_Color");
        _startCap     = serializedObject.FindProperty("_startCap");
        _endCap       = serializedObject.FindProperty("_endCap");
        _join         = serializedObject.FindProperty("_join");
        _miterLimit   = serializedObject.FindProperty("_miterLimit");
        _aaMode       = serializedObject.FindProperty("_aaMode");
        _featherPixels = serializedObject.FindProperty("_featherPixels");
        _sdfMaterial  = serializedObject.FindProperty("_sdfMaterial");
        _dashStyle    = serializedObject.FindProperty("_dashStyle");
        _dashLength   = serializedObject.FindProperty("_dashLength");
        _gapLength    = serializedObject.FindProperty("_gapLength");
        _dashOffset   = serializedObject.FindProperty("_dashOffset");
        _closed       = serializedObject.FindProperty("_closed");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── 基础 ────────────────────────────
        _foldBase = EditorGUILayout.BeginFoldoutHeaderGroup(_foldBase, "基础");
        if (_foldBase)
        {
            EditorGUILayout.PropertyField(_points, new GUIContent("点序列"), true);
            EditorGUILayout.PropertyField(_lineWidth, new GUIContent("线宽"));
            EditorGUILayout.PropertyField(_mColor, new GUIContent("颜色"));
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 接头 / 端点 ─────────────────────
        _foldJoin = EditorGUILayout.BeginFoldoutHeaderGroup(_foldJoin, "接头 / 端点");
        if (_foldJoin)
        {
            EditorGUILayout.PropertyField(_startCap, new GUIContent("起点端"));
            EditorGUILayout.PropertyField(_endCap,   new GUIContent("终点端"));
            EditorGUILayout.PropertyField(_join,      new GUIContent("接头"));
            if ((LineJoin)_join.enumValueIndex == LineJoin.Miter)
                EditorGUILayout.PropertyField(_miterLimit, new GUIContent("Miter极限"));
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 抗锯齿 ──────────────────────────
        _foldAA = EditorGUILayout.BeginFoldoutHeaderGroup(_foldAA, "抗锯齿");
        if (_foldAA)
        {
            EditorGUILayout.PropertyField(_aaMode, new GUIContent("AA模式"));
            var mode = (AntiAliasMode)_aaMode.enumValueIndex;
            switch (mode)
            {
                case AntiAliasMode.None:
                    EditorGUILayout.HelpBox("无抗锯齿，性能最高，肉眼有锯齿", MessageType.Info);
                    break;
                case AntiAliasMode.Geometric:
                    EditorGUILayout.HelpBox("几何 AA（默认），featherPixels=1 满足绝大多数场景", MessageType.Info);
                    EditorGUILayout.PropertyField(_featherPixels, new GUIContent("羽化像素"));
                    break;
                case AntiAliasMode.Sdf:
                    EditorGUILayout.HelpBox("矢量级 AA，需配 UILineSdfMat 材质；高频半透明描边推荐此模式", MessageType.Info);
                    EditorGUILayout.PropertyField(_featherPixels, new GUIContent("羽化像素"));
                    EditorGUILayout.PropertyField(_sdfMaterial, new GUIContent("SDF材质"));
                    break;
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 虚线 ────────────────────────────
        _foldDash = EditorGUILayout.BeginFoldoutHeaderGroup(_foldDash, "虚线");
        if (_foldDash)
        {
            EditorGUILayout.PropertyField(_dashStyle, new GUIContent("虚线样式"));
            if ((DashStyle)_dashStyle.enumValueIndex == DashStyle.Dashed)
            {
                EditorGUILayout.PropertyField(_dashLength, new GUIContent("实线长度"));
                EditorGUILayout.PropertyField(_gapLength,  new GUIContent("间隙长度"));
                EditorGUILayout.PropertyField(_dashOffset, new GUIContent("偏移"));
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 高级 ────────────────────────────
        _foldAdv = EditorGUILayout.BeginFoldoutHeaderGroup(_foldAdv, "高级");
        if (_foldAdv)
        {
            EditorGUILayout.PropertyField(_closed, new GUIContent("闭合"));

            var line = (UILine)target;
            int vEst = EstimateVertices(line);
            EditorGUILayout.HelpBox($"预估顶点数上限 ≈ {vEst}", MessageType.None);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        var line = (UILine)target;
        if (line.PointCount == 0) return;

        var rt = line.rectTransform;
        serializedObject.Update();

        bool changed = false;
        for (int i = 0; i < line.PointCount; i++)
        {
            Vector2 local = line.Points[i];
            Vector2 rtLocal = local + rt.rect.min;
            Vector3 worldPos = rt.TransformPoint(new Vector3(rtLocal.x, rtLocal.y, 0));

            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.PositionHandle(worldPos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newLocal3 = rt.InverseTransformPoint(newWorld);
                Vector2 newLocal = new Vector2(newLocal3.x, newLocal3.y) - rt.rect.min;
                var elem = _points.GetArrayElementAtIndex(i);
                elem.vector2Value = newLocal;
                changed = true;
            }

            Handles.Label(worldPos + Vector3.up * 10, $"P{i}");
        }

        // 预览连线
        for (int i = 0; i + 1 < line.PointCount; i++)
        {
            Vector2 a = line.Points[i]     + rt.rect.min;
            Vector2 b = line.Points[i + 1] + rt.rect.min;
            Handles.DrawLine(
                rt.TransformPoint(new Vector3(a.x, a.y, 0)),
                rt.TransformPoint(new Vector3(b.x, b.y, 0)));
        }

        if (changed)
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }

    /// <summary>根据设计文档 §8 公式预估顶点数上限。</summary>
    private static int EstimateVertices(UILine line)
    {
        int n = line.PointCount;
        if (n < 2) return 0;
        int segs = n - 1;
        bool hasAA = line.AAMode != AntiAliasMode.None;

        // 每段 8 顶点（4核心+4羽化），AA关闭时 4 顶点
        int segV = segs * (hasAA ? 8 : 4);
        // Round join 每接头 ~2Kj+3 ≈ 27 (Kj≤12)
        int joinV = (n - 2) * 27;
        // Cap 每端 ~2Kc+3 ≈ 27
        int capV = 27 * 2;
        return segV + joinV + capV;
    }
}
