using System.Collections.Generic;
using UnityEngine;

/// <summary>压测：每帧给 50 条 UILine 各传入 64 个噪声点，验证 GC.Alloc 接近 0。</summary>
public class UILineStressTest : MonoBehaviour
{
    [SerializeField] private UILinePool _pool;
    [SerializeField] private int _lineCount = 50;
    [SerializeField] private int _pointsPerLine = 64;
    [SerializeField] private float _spread = 200f;

    private readonly List<UILine> _lines = new List<UILine>();
    private readonly List<Vector2> _ptsBuf = new List<Vector2>();

    private void Start()
    {
        for (int i = 0; i < _lineCount; i++)
            _lines.Add(_pool.Acquire());
    }

    private void Update()
    {
        float t = Time.time;
        for (int li = 0; li < _lines.Count; li++)
        {
            _ptsBuf.Clear();
            for (int pi = 0; pi < _pointsPerLine; pi++)
            {
                float x = pi * (_spread / _pointsPerLine);
                float y = Mathf.PerlinNoise(x * 0.05f + li * 3.7f, t * 0.5f) * _spread * 0.5f;
                _ptsBuf.Add(new Vector2(x, y));
            }
            _lines[li].SetPoints(_ptsBuf);
        }
    }
}
