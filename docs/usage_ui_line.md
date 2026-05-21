UILine 使用文档

UILine 是基于 UGUI 的折线绘制组件，支持多点连线、多种接头/端点样式、抗锯齿、虚线、闭合折线，适用于 Inspector 手动配置和运行时 API 动态驱动两种工作模式。

目录

快速上手

坐标系说明

组件字段说明

运行时 API

抗锯齿模式选择

坐标转换工具 UILineUtils

对象池 UILinePool

性能注意事项

常见问题

1 快速上手

Inspector 模式

在 Canvas 下创建一个空 GameObject，添加 UILine 组件（Add Component → UILine）。

在 Inspector 的 点列表 里添加若干 Vector2 坐标（左下原点，像素单位）。

调整 线宽、颜色、接头样式 等参数，画面实时更新。

在 Scene 视图中可以直接拖拽每个点（需选中 UILine 所在 GameObject）。

运行时 API 模式

// 1. 获取或创建组件
UILine line = GetComponent();

// 2. 配置样式（只在值改变时才触发重绘，无多余开销）
line.LineWidth    = 4f;
line.color        = new Color(0.2f, 0.8f, 1f, 1f);
line.Join         = LineJoin.Round;
line.StartCap     = LineCap.Round;
line.EndCap       = LineCap.Round;
line.AAMode       = AntiAliasMode.Geometric;

// 3. 设置点并触发重绘（每帧调用也安全）
var pts = new List
{
    new Vector2(20,  50),
    new Vector2(120, 150),
    new Vector2(250, 80),
    new Vector2(350, 200),
};
line.SetPoints(pts);

2 坐标系说明

UILine 使用 UI 局部像素坐标：

原点在 RectTransform 的左下角

X 轴向右，Y 轴向上

单位为像素（与 RectTransform.rect.width/height 对应）

(0, height) ─────── (width, height)
     │                      │
     │    UILine 绘制区域    │
     │                      │
  (0,0) ────────── (width, 0)

如果你的数据来自其他坐标系（屏幕坐标、世界坐标、图像像素坐标），使用 UILineUtils 进行转换。

3 组件字段说明

基础

字段

类型

默认值

说明

点列表

List

空

折线顶点，坐标为 UI 局部像素（左下原点）

线宽

float

4

单位：像素

颜色

Color

白色

继承自 Graphic.color，支持透明度

接头 / 端点

字段

类型

默认值

说明

StartCap

LineCap

Butt

起点端点样式

EndCap

LineCap

Butt

终点端点样式

Join

LineJoin

Round

折线转折处接头样式

MiterLimit

float

4

仅 Miter 有效；超过此值自动退化为 Bevel

LineCap 取值：

值

效果

Butt

线段端面硬切，不延伸

Round

半圆形端点（有 AA）

Square

端面外延半个线宽的方形

LineJoin 取值：

值

效果

Round

圆弧连接，转角平滑（推荐）

Miter

尖角连接；转角极小时自动退化为 Round/Bevel

Bevel

斜切连接，切去尖角

抗锯齿

字段

类型

默认值

说明

AAMode

AntiAliasMode

Geometric

详见第 5 节

FeatherPixels

float

1

几何/SDF 模式的羽化宽度（像素）

SdfMaterial

Material

null

SDF 模式必填，拖入 UILineSdfMat

虚线

字段

类型

默认值

说明

DashStyle

DashStyle

Solid

Solid = 实线，Dashed = 虚线

DashLength

float

8

虚线段长度（像素）

GapLength

float

4

间隙长度（像素）

DashOffset

float

0

起始偏移，每帧递增可实现"行进蚂蚁"动画

高级

字段

类型

默认值

说明

Closed

bool

false

是否闭合折线（首尾相连，cap 失效）

4 运行时 API

点操作

// 整体替换（推荐，内部复用 List 不产生 GC）
line.SetPoints(IList pts);
line.SetPoints(Vector2[] pts, int count);

// 逐点追加（适合初始化阶段）
line.AddPoint(new Vector2(100, 200));

// 清空
line.ClearPoints();

// 只读访问
IReadOnlyList pts = line.Points;
int count = line.PointCount;

样式属性（setter 均有相等检查，值不变时不触发重绘）

line.LineWidth      = 6f;
line.color          = Color.red;
line.Join           = LineJoin.Miter;
line.MiterLimit     = 3f;
line.StartCap       = LineCap.Round;
line.EndCap         = LineCap.Butt;
line.AAMode         = AntiAliasMode.Geometric;
line.FeatherPixels  = 1.5f;
line.Closed         = true;
line.DashStyle      = DashStyle.Dashed;
line.DashLength     = 12f;
line.GapLength      = 6f;
line.DashOffset     = 0f;
line.SdfMaterial    = myMat;   // AAMode = Sdf 时必填

典型场景示例

实时更新折线（如雷达扫描线）：

void Update()
{
    _ptsBuf.Clear();
    for (int i = 0; i < _dataPoints.Count; i++)
        _ptsBuf.Add(DataToUiLocal(_dataPoints[i]));
    _line.SetPoints(_ptsBuf);
}

行进蚂蚁动画（选区边框）：

void Update()
{
    _line.DashOffset += Time.deltaTime * 30f;  // 每秒移动 30px
}

闭合多边形：

line.Closed = true;
line.Join   = LineJoin.Round;
line.SetPoints(new List
{
    new Vector2(50,  50),
    new Vector2(150, 50),
    new Vector2(150, 150),
    new Vector2(50,  150),
});

SDF 高质量半透明描边：

// 先在 Inspector 或代码里赋好 SdfMaterial
line.SdfMaterial = Resources.Load("UILineSdfMat");
line.AAMode       = AntiAliasMode.Sdf;
line.FeatherPixels = 1.5f;
line.color        = new Color(1, 1, 1, 0.4f);
line.Join         = LineJoin.Round;

5 抗锯齿模式选择

模式

原理

性能

适用场景

None

无处理

最高

像素画风、16px 以上粗线、不在意锯齿

Geometric（默认）

在线段边缘添加半透明羽化条带，使用 UI/Default shader

中等，无需额外材质

绝大多数场景；FeatherPixels=1 效果已很好

Sdf

自定义 shader 按距离场计算 alpha，接头处无重叠绘制

略高（shader 计算），但接头质量最佳

高频半透明描边、需要矢量级平滑的场景

推荐组合：

普通线条：Geometric + FeatherPixels=1 + Join=Round

细线（< 3px）：Geometric + FeatherPixels=0.5

半透明轮廓（color.a < 0.8）：Sdf + Join=Round + 拖入 UILineSdfMat

SDF 模式必须将 Assets/Materials/UILineSdfMat.mat（或自定义同 shader 的材质）赋给 SdfMaterial 字段，否则自动回退 Geometric 并在 Console 输出一次 Warning。

6 坐标转换工具 UILineUtils

如果输入点来自其他坐标系，使用 UILineUtils 静态方法转换为 UILine 所需的局部像素坐标：

RectTransform rt = lineObject.GetComponent();

// 屏幕坐标（如鼠标位置）→ UI 局部像素
Vector2 local = UILineUtils.ScreenToUiLocal(rt, Input.mousePosition, uiCamera);

// 世界坐标（如游戏单位）→ UI 局部像素
Vector2 local = UILineUtils.WorldToUiLocal(rt, worldPos, worldCamera, uiCamera);

// 归一化坐标 (0~1, 左下原点) → UI 局部像素
Vector2 local = UILineUtils.NormalizedToUiLocal(rt, new Vector2(0.5f, 0.5f));

// 归一化坐标 (0~1, 左上原点，常见于 UI 设计稿) → UI 局部像素
Vector2 local = UILineUtils.NormalizedTopLeftToUiLocal(rt, new Vector2(0.3f, 0.2f));

// 图像像素坐标（左上原点，如 Texture2D 采样点）→ UI 局部像素
Vector2 imageSize = new Vector2(1024, 512);
Vector2 local = UILineUtils.PixelTopLeftToUiLocal(rt, new Vector2(300, 200), imageSize);

批量转换（零 GC，结果写入 dst 列表）：

var dst = new List();

// 批量归一化 → 局部像素
UILineUtils.NormalizedToUiLocal(rt, srcList, dst);
line.SetPoints(dst);

// 批量图像像素 → 局部像素
UILineUtils.PixelTopLeftToUiLocal(rt, imageSize, srcList, dst);
line.SetPoints(dst);

7 对象池 UILinePool

当场景中需要频繁创建/销毁多条线时，使用 UILinePool 避免 GC。

设置步骤

创建一个 模板 UILine（在 Canvas 下，配好样式，SetActive(false)）。

在池的父 GameObject 上挂 UILinePool 组件。

Inspector 里将模板拖入 _template，设置 _poolSize（建议 = 业务峰值数量 + 20%）。

使用方式

[SerializeField] UILinePool _pool;

void SpawnLines()
{
    _pool.ReleaseAll();  // 归还上一帧所有线

```
for (int i = 0; i < count; i++)
{
    UILine line = _pool.Acquire();  // 取出（已重置为默认状态）
    line.color = myColor;
    line.SetPoints(myPoints[i]);
}
```

}

Acquire() 返回的实例已自动重置为：LineWidth=2，Join=Round，Cap=Round，AAMode=Geometric，Solid，Closed=false。如需其他默认值，可在 Acquire 后覆盖。

8 性能注意事项

场景

建议

每帧更新点

使用 SetPoints(List)，复用同一个 List，不要每帧 new

大量折线实例

使用 UILinePool，避免 Instantiate/Destroy

点数超多（> 500）

考虑在业务侧做抽稀；超 65535 顶点时组件会自动截断并打 Warning

仅修改颜色/偏移

颜色改变会触发 SetVerticesDirty；DashOffset 每帧改动是正常用法，已做相等检查

不需要 AA 的场合

设 AAMode = None 可减少约 40% 顶点数

静态缓冲区：UILineBuilder 内部使用全局静态 List 缓冲区，单线程（主线程）中多条 UILine 在同一帧各自调用 OnPopulateMesh 时顺序执行，不存在竞争问题。

9 常见问题

Q：线条不显示？
A：确认 ① 点数 ≥ 2，② LineWidth > 0，③ color.a > 0，④ UILine 所在 Canvas 的 Render Mode 和相机配置正确。

Q：SDF 模式回退到 Geometric，Console 有 Warning？
A：SdfMaterial 字段未赋值。将 Assets/Materials/UILineSdfMat.mat 拖入 Inspector 的 SdfMaterial 字段，或在代码里 line.SdfMaterial = Resources.Load("UILineSdfMat")。

Q：Mask / RectMask2D 裁剪不生效？
A：Geometric 模式使用默认 UI/Default shader，Mask 完全生效。SDF 模式使用 UILineSDF.shader（内含完整 Stencil / _ClipRect 段），也支持 Mask。如仍异常，检查 Canvas 层级和 Mask 组件配置。

Q：闭合折线 + 虚线时 dash 首尾不对齐？
A：这是正常行为，DashOffset = 0 时 dash 从弧长 0 处开始。调整 DashOffset 可以控制起始相位。

Q：Console 出现"顶点估算超 65535，截断"Warning？
A：点数过多导致顶点数超出 Unity 16 位索引上限。可在业务侧对点列表做抽稀（如 Douglas-Peucker 算法），或降低 Join=Bevel（比 Round 省顶点）。

Q：Scene 视图点编辑不生效？
A：确认选中的是挂有 UILine 的 GameObject，并切换到 Scene 视图。编辑完成后 Hierarchy 里对象名会显示 *（表示有未保存改动），记得 Ctrl+S 保存场景。