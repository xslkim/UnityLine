using System.Collections.Generic;
using UnityEngine;

/// <summary>UILine 对象池，避免运行时 Instantiate/Destroy 产生 GC。</summary>
public class UILinePool : MonoBehaviour
{
    [SerializeField] private UILine _template;
    [SerializeField] private int _poolSize = 20;

    private readonly List<UILine> _pool = new List<UILine>();
    private readonly List<UILine> _active = new List<UILine>();

    private void Awake()
    {
        for (int i = 0; i < _poolSize; i++)
        {
            var line = Instantiate(_template, transform);
            line.gameObject.SetActive(false);
            _pool.Add(line);
        }
    }

    /// <summary>获取一个已复位的 UILine 实例，自动激活。</summary>
    public UILine Acquire()
    {
        UILine line;
        if (_pool.Count > 0)
        {
            line = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            line = Instantiate(_template, transform);
        }
        line.gameObject.SetActive(true);
        line.SetPoints(System.Array.Empty<Vector2>());
        line.LineWidth = 2f;
        line.Join = LineJoin.Round;
        line.StartCap = LineCap.Round;
        line.EndCap = LineCap.Round;
        line.AAMode = AntiAliasMode.Geometric;
        line.DashStyle = DashStyle.Solid;
        line.Closed = false;
        _active.Add(line);
        return line;
    }

    /// <summary>归还所有已借出的 UILine 到池中。</summary>
    public void ReleaseAll()
    {
        foreach (var line in _active)
        {
            line.gameObject.SetActive(false);
            _pool.Add(line);
        }
        _active.Clear();
    }
}
