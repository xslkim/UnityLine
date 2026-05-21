# UI 折线绘制功能（UILine）

## 1. 功能概述

在 UGUI 体系下提供一个可在任意 `RectTransform` 范围内绘制**折线（Polyline）** 的 UI 组件。给定一个 ≥ 2 个点的坐标数组，把这些点首尾相连绘制成一条带宽度的线，支持：

- 颜色（含透明度）
- 抗锯齿（None / Geometric / SDF 三档）
- 线宽（像素）
- 接头圆角（默认 Round join，并支持 Miter / Bevel 备选）
- 端点样式 Cap（Butt / Round / Square 三选一，起止可独立配置）
- 虚线（dash + gap 模式，几何裁切实现）
- 闭合折线（首尾相连，无 Cap）

并配套一组「世界/归一化/屏幕 → UI 局部像素坐标」的转换工具方法，方便业务方把检测、轨迹等数据投到 UI 上。

## 2. 设计原则与技术选型

| 项 | 选择 | 理由 |
| --- | --- | --- |
| UI 系统 | **UGUI**（继承 `MaskableGraphic`） | 与项目现有 UI 体系（`UIMat`、`NpcRectPoolController` 等）完全一致，能配合 `Mask` / `RectMask2D` / `CanvasGroup` |
| 渲染入口 | `OnPopulateMesh(VertexHelper)` 自绘 | 顶点级控制，能够同时承载 Cap / Join / AA / 虚线四种几何 |
| 默认材质 | `UI/Default`（与 `UIMat.mat` 同 shader） | 几何 AA 无需 shader 改动，最大化兼容 |
| 高级 AA | **自定义 SDF shader**（基于 `UI/Default.shader` 改） | 提供「等级 3」高质量矢量 AA，可选启用 |
| 依赖 | 纯 UGUI 内建，无第三方包 | 与「依赖最小化」要求一致 |
| 接口 | 组件 + 运行时 API 双形式 | Inspector 可拖配置，业务代码也可全程 API 驱动 |
| 更新模式 | 支持每帧 `SetPoints` + `SetVerticesDirty` | 满足「每帧更新」场景，配合缓冲池避免 GC |

## 3. 坐标系约定

- 输入点 `Vector2 p` 表示 **`RectTransform` 局部像素坐标**，**左下为原点 (0,0)，x 向右、y 向上**（Unity `RectTransform` 默认风格）。
- 推荐把 `UILine` 组件挂在一个 anchor stretch、`pivot = (0,0)` 的 `RectTransform` 上，这样输入点就是相对父容器的"图像化"像素位置。
- 当 `pivot ≠ (0,0)` 时，`UILine.OnPopulateMesh` 会把 `rectTransform.rect.min`（即 `(-pivot.x * w, -pivot.y * h)`）作为统一平移量 `origin` 传给 `UILineBuilder`，Builder 在写顶点时执行 `vertex.position = origin + inputPoint`，从而使得"输入坐标 = 左下原点像素坐标"语义与 pivot 无关。
- 与 `NpcRectPoolController` 使用的「左上原点、y 向下」不同；若业务方原始数据是左上系，**自行在外部翻转一次 y**（或调用第 7 节的辅助方法），保持 `UILine` 的输入语义统一。

## 4. 公共 API 设计

### 4.1 枚举

```csharp
public enum LineCap   { Butt, Round, Square }
public enum LineJoin  { Round, Miter, Bevel }
public enum AntiAliasMode { None, Geometric, Sdf }
public enum DashStyle { Solid, Dashed }
```

### 4.2 组件字段（Inspector 可调）

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `points` | `List<Vector2>` | 空 | 折线顶点序列（左下原点像素坐标） |
| `lineWidth` | `float` | `4` | 线宽，单位像素 |
| `color` | `Color` | 白 | 沿用 `Graphic.color`，含透明度 |
| `startCap` / `endCap` | `LineCap` | `Butt` | 起止端点样式，独立配置 |
| `join` | `LineJoin` | `Round` | 接头样式 |
| `miterLimit` | `float` | `4` | Miter 模式下夹角过小时自动退化为 Bevel 的阈值（单位：line-width 的倍数） |
| `aaMode` | `AntiAliasMode` | `Geometric` | 抗锯齿模式 |
| `featherPixels` | `float` | `1.0` | 羽化宽度像素数，对 Geometric / SDF 都生效 |
| `closed` | `bool` | `false` | 是否首尾闭合（闭合时忽略 startCap / endCap） |
| `dashStyle` | `DashStyle` | `Solid` | 实线 / 虚线 |
| `dashLength` | `float` | `8` | 单段实线长度（像素，沿弧长） |
| `gapLength` | `float` | `4` | 单段空白长度（像素，沿弧长） |
| `dashOffset` | `float` | `0` | dash 起始相位偏移（像素，沿弧长） |
| `sdfMaterial` | `Material` | 空 | `aaMode = Sdf` 时使用的自定义材质（自动回退） |

### 4.3 运行时方法

```csharp
public sealed class UILine : MaskableGraphic
{
    // 点序列：拷贝写入；调用后会标 Dirty，下一帧重建 Mesh
    public void SetPoints(IList<Vector2> pts);
    public void SetPoints(Vector2[] pts, int count);
    public void AddPoint(Vector2 p);
    public void ClearPoints();

    // 只读访问当前点序列（返回内部 List 的只读视图，不复制）
    public IReadOnlyList<Vector2> Points { get; }
    public int PointCount { get; }

    // 常用属性（修改即触发 SetVerticesDirty / SetMaterialDirty）
    public float       LineWidth     { get; set; }
    public LineJoin    Join          { get; set; }
    public float       MiterLimit    { get; set; }    // Miter 退化阈值，单位：lineWidth 的倍数
    public LineCap     StartCap      { get; set; }
    public LineCap     EndCap        { get; set; }
    public AntiAliasMode AAMode      { get; set; }    // 触发 SetMaterialDirty + SetVerticesDirty
    public float       FeatherPixels { get; set; }
    public bool        Closed        { get; set; }
    public DashStyle   DashStyle     { get; set; }
    public float       DashLength    { get; set; }
    public float       GapLength     { get; set; }
    public float       DashOffset    { get; set; }    // 动画化即可做出"蚂蚁线"
    public Material    SdfMaterial   { get; set; }    // 触发 SetMaterialDirty；null 时自动回退 Geometric

    protected override void OnPopulateMesh(VertexHelper vh);
}
```

### 4.4 抗锯齿模式效果与性能对比

> 顶点开销以 `None` 模式为基准 1×，按"每段线段贡献的核心顶点数"折算。

| 模式 | 实现方式 | 视觉效果 | 顶点开销（每段） | Fragment 开销 | Shader |
| --- | --- | --- | --- | --- | --- |
| `None` | 纯 quad 条带 | 斜线/旋转明显锯齿 | 1×（4 顶点） | 1× | `UI/Default` |
| `Geometric` ⭐默认 | quad 两侧加 1~2px 羽化带，alpha 渐变 | ≥ 2px 线宽下肉眼无锯齿 | ~2×（4 核心 + 4 羽化），Round join/cap 再加扇形顶点 | 1×（仅顶点色插值） | `UI/Default` |
| `Sdf` | 严格不重叠的段 quad + 接头/端点圆盘 quad，shader 内做距离场 + smoothstep | 任意角度极顺滑，矢量级别 | ~1× 段 quad + 每个接头/Round Cap 额外 4 顶点圆盘 | 重一些（距离场 + smoothstep） | `Custom/UILineSDF` |

> SDF 模式下段 quad 顶点数本身比 Geometric 少（没有羽化条带），但每个接头/Round Cap 会额外补一个圆盘 quad，所以实际总顶点数与 Geometric 接近；fragment 端会承担距离场计算。
>
> 实务建议：默认 `Geometric + featherPixels=1`，仅在线宽很细（≤ 1.5px）、高 DPI 下需要高质量时切到 `Sdf`。

## 5. 几何生成算法

为了能同时支持 Cap、Join、AA、Dashed 四个维度，几何生成统一抽象为「子段链 → 三角形带」两步：

```
原始 points
   │
   ├─ 闭合处理（closed=true 时在尾部追加 points[0]）
   │
   ├─ 虚线裁切（dashStyle=Dashed 时：沿弧长切成多段子折线，每段独立走下一步；Solid 时直接整段作为唯一子折线）
   │
   ├─ 子折线 → 段元数据（每段算出方向、法向、长度）
   │
   ├─ Join 拼接（在内部顶点处生成 Round/Miter/Bevel 几何）
   │
   ├─ Cap 收尾（每个子折线起止生成 Butt/Round/Square 几何）
   │
   └─ AA 羽化（Geometric 模式下沿外法线方向再加一对羽化顶点条带）
       ↓
   写入 VertexHelper
```

### 5.1 主线段顶点生成（核心条带）

对每段 `Pi → Pi+1`：
- 方向 `d = normalize(Pi+1 - Pi)`
- 法向 `n = (-d.y, d.x)`（左侧为正）
- 半宽 `r = lineWidth / 2`
- 该段贡献 4 个核心顶点：`Pi ± n·r`、`Pi+1 ± n·r`，组成 2 个三角形

### 5.2 内部接头（Join）

对每个内部顶点 `Pi`：
- 上一段法向 `nA`、下一段法向 `nB`
- 角平分线方向 `m = normalize(nA + nB)`
- 转向符号 `s = sign(cross(d_prev, d_next))`：决定内侧/外侧
- **内侧**：两段共享一个偏移点 `Pi + s·m · (r / dot(m, nA))`（miter 公式）
  - **短线段穿透保护**：上述内侧偏移距离 `r / dot(m, nA)` 在夹角很小或邻段长度很短时可能超过段长本身，导致内侧顶点越过 `Pi-1` 或 `Pi+1`，几何会反折。实现时把内侧偏移距离 clamp 到 `min(r/dot(m,nA), prevSegLen·0.5, nextSegLen·0.5)`；超过则两段不共享内侧顶点，各自用本段法向独立的内侧顶点（视觉上接头变粗一像素，但无穿透）
- **外侧**：每段独立保留各自的 `Pi ± n·r` 顶点，二者之间按 join 模式补几何：
  - `Miter`：直接拉到尖角点 `Pi + (-s)·m · (r / dot(m, nA))`；若 `1 / dot(m, nA) > miterLimit` 自动退化为 Bevel
  - `Bevel`：单个三角形连接两个外侧顶点和 `Pi`
  - `Round` ⭐默认：扇形 fan，从一个外侧顶点扫到另一个，三角形数 = `ceil(angle * r / featherPixels)`（半径越大、AA 越精细，分段越多），最少 3 段

### 5.3 端点（Cap）

对开放折线的起点 `P0` 和终点 `Pn`：
- `Butt`：什么也不做。端面是硬切，**与线方向垂直的横切边不做 AA**（Geometric 羽化条带是沿法向加的，覆盖不到端面的横向）
- `Square`：在端点处沿反方向 `-d`（起点） / `+d`（终点）延伸 `r` 距离，新增一对核心顶点。**外侧横切边同样不做 AA**，行为与 Butt 一致，只是端面位置外推了 `r`
- `Round`：以端点为圆心、`r` 为半径，生成半圆扇形 fan（分段数同 Round join），扇形外缘按第 5.4 节再加一圈羽化条带；**端面有完整 AA**

> Geometric / SDF 两种模式下 Butt、Square 的横切边 AA 行为完全一致。若业务需要任意端面方向都 AA，请使用 Round Cap。

### 5.4 几何 AA（`Geometric` 模式）

在第 5.1～5.3 的所有几何外缘上再加一圈羽化条带：
- 每个核心边界顶点 `v_core` 沿法向再加一个羽化顶点 `v_feather = v_core + n · featherPixels`
- 顶点色 alpha：核心 = `1.0`、羽化 = `0.0`
- 三角形：核心边到羽化边形成一条 strip
- **窄线保护**：当 `lineWidth < 2 * featherPixels` 时（羽化带会从中线对侧伸出，覆盖线主体），按下述方式收敛——这是 AGG（Anti-Grain Geometry）的经典处理：
  - 实际羽化宽度 clamp 为 `actualFeather = lineWidth / 2`（确保羽化不穿过中线）
  - 实际线宽 clamp 为 0（即所有"核心"几何都退化到中线，只剩两条对称的羽化条带）
  - 核心顶点 alpha 由 `1.0` 降为 `lineWidth / (2 * featherPixels)`，使得 1 像素粗的线看起来"粗细仍是 1 像素，但整体半透明"，避免因为加了羽化带反而显得"虚胖"
  - 这样 `lineWidth → 0` 时线会平滑淡出而不是突然消失
- Round Join / Round Cap 的扇形几何也加羽化：每个扇形顶点沿半径方向再投出一个羽化顶点

### 5.5 虚线（`Dashed` 模式，几何实现）

1. 先把整条折线按弧长参数化：累积每段长度得到 `sums[i]`，总长 `L = sums[n-1]`
2. 沿弧长按 `[dashOffset, dashOffset + dashLength, dashOffset + dashLength + gapLength, …]` 切出 "on/off" 区间
3. 对每个 "on" 区间 `[a, b]`：用二分定位起止段，构造子折线 `S_k`（包含原始内部顶点）
4. 每个 `S_k` 独立按第 5.1～5.4 走一遍，端点用 `startCap` / `endCap` 收尾
5. 与"圆角接头 + AA"完全正交，不需要 shader 侧改动

> 选择几何实现而非 shader 实现，是因为它跟「Round Cap + AA」配合最自然，每个 dash 看起来就是一条完整的小线段。

## 6. 抗锯齿模式 — `Sdf` 路线

当用户切换到 `AAMode = Sdf` 时：

1. 组件把 `material` 替换为 `sdfMaterial`（若为空则回退 `Geometric`，并打一次 warning）
2. 同一个 shader 内通过 UV / 顶点属性区分两种 primitive：**段 quad（Segment）** 和 **圆盘 quad（Disk）**。下游算法保证它们之间**互不重叠**

### 6.1 防接缝：段 quad 与圆盘 quad 严格不重叠

如果按"每段都加宽 `r + feather` 的 quad、shader 里用 `clamp(uv.x, 0, segLen)` 算端点距离"的朴素做法，相邻段的 quad 会在接头处发生**重叠**——UI shader 默认 `Blend SrcAlpha OneMinusSrcAlpha`，重叠区域被绘制两次，AA 边带变深、半透明描边时尤其明显。

为彻底避免，本设计采用 "**段不延展端点 + 接头/Cap 单独用圆盘**" 的拼装：

- **段 quad（Segment）**：尺寸严格为 `segLen × (lineWidth + 2·feather)`。其 UV 设计成 `uv.x ∈ [0, segLen]`、`uv.y ∈ [-(r+σ), r+σ]`，即沿轴向**不向外延伸**。shader 内只算到中心线的距离 `d = abs(uv.y)`，不做端点圆形判断。
- **圆盘 quad（Disk）**：尺寸为 `(lineWidth + 2·feather) × (lineWidth + 2·feather)`。UV 设计成以中心 `(0,0)`、四角 `(±(r+σ), ±(r+σ))`。shader 内算 `d = length(uv.xy)`。

两类 quad 通过顶点 `TEXCOORD1.z`（或在 `UV1` 里塞一个 0/1 flag）告诉 shader 是哪种 primitive。

这样：
- 段 quad 之间在接头处不重叠（每段都终止于该段的物理端点）
- 接头处由一个圆盘 quad 填补圆角空白
- 段 quad 与圆盘 quad 之间仍存在"边界重叠"，但范围被严格收敛到 `feather` 宽度的窄带（典型 1~2 px）。在该窄带内，段 quad 算出的 alpha 为 `1 - smoothstep(r-σ, r+σ, |uv.y|)`，圆盘 quad 算出的 alpha 为 `1 - smoothstep(r-σ, r+σ, sqrt(uv.x²+uv.y²))`，两者距离单调一致但**不严格相等**。

**重叠区域的 alpha 偏差量化**：标准 `SrcAlpha OneMinusSrcAlpha` 混合下，两个 α₁、α₂ 顺次绘制后的合成 α = α₁ + α₂ - α₁·α₂。当 α₁ = α₂ ≈ 0.5 时合成 α ≈ 0.75，比单次绘制偏深约 25%。**该偏差仅出现在 1~2 px 的羽化窄带内**，肉眼几乎不可见；但在半透明描边（如 `color.a ≈ 0.3`）的接头处仍可能看到一道窄而轻微的"加亮线"。

**消除手段**（按推荐顺序）：
1. **首选**：shader 内对段 quad 的两端 `feather` 区域做 `discard`，由圆盘 quad **完全接管**接头边缘的 AA（不重叠 → 零偏差）
2. **兜底 A**：用 stencil bit 做"先到先得"——圆盘先写 stencil = 1，段 quad 在 stencil = 1 时 discard
3. **兜底 B**：SDF 模式强制 `Blend One OneMinusSrcAlpha` 走预乘 alpha，效果略改善但不消除

首选方案不需要额外 stencil/blend 设置，将作为 M7 阶段的默认实现；兜底方案保留为备选。

### 6.2 Cap 三种样式在 SDF 下的实现

| Cap | 段 quad 行为 | 是否补圆盘 | 备注 |
| --- | --- | --- | --- |
| `Butt` | 段 quad 终点严格收在 `uv.x = segLen`，shader 不算端点距离 | **否** | 端面是硬切，**横切边（与线段方向垂直）不做 AA**；与 Geometric 模式行为一致 |
| `Round` | 同 Butt | **是**（半径 `r+σ` 的圆盘） | 与接头共用同一 Disk 几何，shader 走 `d = length(uv.xy)`；端面有完整 AA |
| `Square` | 沿轴向**延伸 `r` 长度**（顶点几何上把段 quad 端面外推），shader 仍只算 `abs(uv.y)` | **否** | 延伸部分依然按"到中心线距离"做 AA，产生方头；但**外侧横切边不做 AA**（与 Geometric 模式一致） |

> 关于 `Butt` / `Square` 的横切边缘不做 AA：这是工程上的有意取舍——若要让横切边也 AA，需要 shader 内同时计算 `uv.x` 端点距离，回到"段 quad 互相重叠"的设计，与第 6.1 节防接缝策略矛盾。实际上线宽 ≥ 1px 时，轴向端面的锯齿主要由 Canvas 整体 MSAA / 屏幕分辨率消化，肉眼基本不可见；如有严格需求，请改用 `Round` cap。

### 6.3 Join 在 SDF 下的实现

SDF shader 内只识别 §6.1 的两种 primitive（Segment / Disk），所以 Join 的实现策略是把不同 join 形态映射到这两种 primitive 的组合：

| Join | 实现 | AA 行为 |
| --- | --- | --- |
| `Round` ⭐默认 | 每个内部顶点放一个圆盘 quad；段 quad 各自收在端点 | 接头外缘有完整矢量 AA |
| `Miter` | **段 quad 的外侧顶点拉到尖角点**（与 Geometric 走完全相同的几何 miter），不补任何额外 primitive；shader 内仍按 `abs(uv.y)` 对段 quad 做距离场 AA；超 `miterLimit` 自动**退化为 Round**（在接头补圆盘） | 接头两侧的"延伸尖角"边由段 quad 的 `feather` 窄带覆盖，**AA 正常**；尖角顶点处会有一个像素级的硬转角，与矢量绘图惯例一致 |
| `Bevel` | **不放任何额外 primitive**，直接复用相邻两段段 quad 的端面：第 i 段以"内侧到 Pi、外侧延伸到下一段的外侧顶点"的截面收尾，第 i+1 段同理对称——这相当于把"斜切"挤进段 quad 自身的端面 | 斜切边沿段 quad 的 `feather` 自然 AA |

**关于 SDF Bevel 的注意事项**：

为了避免在 SDF 下专门为 Bevel 引入第三类 primitive（带任意三角形 SDF 的 shader 分支），本设计把 Bevel 的几何吸收进了段 quad 自身——这要求 `UILineBuilder` 的 SDF 分支在生成段 quad 时，根据下一段的 join 类型动态调整本段两端的四个顶点位置：

- 段 quad 的内侧端点：拉到 Pi（与下一段共享）
- 段 quad 的外侧端点：根据 join 模式决定
  - `Round` / `Miter超限` → 收在本段法向 `Pi ± n·(r+σ)`（让圆盘 quad 接管外侧）
  - `Miter` 未超限 → 拉到尖角点 `Pi ± m·(r/dot(m,n) + σ/dot(m,n))`
  - `Bevel` → 收在本段法向 `Pi ± n·(r+σ)`，**下一段的同侧外侧顶点也收在 Pi ± nB·(r+σ)**；两段端面在 Pi 处自然形成 V 形斜切，没有重叠也没有缺口

这样 SDF 模式下 shader 内永远只需要识别两种 primitive，逻辑统一。

> 设计取舍：`Sdf + Round` 是默认且视觉最佳的组合；`Sdf + Miter / Bevel` 能正常工作但 SDF 在这两种 join 下相对 Geometric 没有额外视觉收益（接头本身没有曲线）。如果业务以 `Bevel/Miter` 为主，**建议直接用 `Geometric` 模式**以减少 shader/几何复杂度。

### 6.4 Shader 核心片元

```glsl
// 顶点输入 (per-vertex)
//   uv0.xy: 局部坐标
//     - Segment: uv.x ∈ [0, segLen], uv.y ∈ [-r-σ, r+σ]
//     - Disk:    uv.x, uv.y ∈ [-r-σ, r+σ]
//   uv1:    (r, sigma, primKind)   // primKind: 0 = Segment, 1 = Disk
float r     = i.uv1.x;
float sigma = i.uv1.y;
float kind  = i.uv1.z;

float d = (kind < 0.5)
            ? abs(i.uv0.y)                    // Segment
            : length(i.uv0.xy);               // Disk

float a = 1.0 - smoothstep(r - sigma, r + sigma, d);
fixed4 col = i.color;
col.a *= a;
return col;
```

Shader 派生自 `UI/Default.shader`，**完整保留** `Stencil` / `_ClipRect` / `UNITY_UI_CLIP_RECT` / `UNITY_UI_ALPHACLIP` 等 UI 框架宏，确保 `Mask` 与 `RectMask2D` 在 SDF 模式下也能正确裁剪。

## 7. 坐标转换辅助工具

放在 `Assets/Scripts/UI/Line/UILineUtils.cs`，纯静态方法：

```csharp
public static class UILineUtils
{
    // 屏幕像素 → UI 局部（左下原点）
    public static Vector2 ScreenToUiLocal(RectTransform ui, Vector2 screen, Camera uiCamera);

    // 世界坐标 → UI 局部（左下原点）
    public static Vector2 WorldToUiLocal(RectTransform ui, Vector3 world,
                                         Camera worldCamera, Camera uiCamera);

    // 归一化 (0~1, 左下原点) → UI 局部像素
    public static Vector2 NormalizedToUiLocal(RectTransform ui, Vector2 normalized01);

    // 归一化 (0~1, 左上原点) → UI 局部像素（适配业务里左上系数据）
    public static Vector2 NormalizedTopLeftToUiLocal(RectTransform ui, Vector2 normalized01TopLeft);

    // 左上原点像素坐标（例如图像/视频帧像素坐标）→ UI 局部像素
    //   做法：先翻转 y 到左下系 (x, imageSize.y - pixelTopLeft.y)，
    //   再按 (ui.rect.size / imageSize) 等比缩放到 UI 像素空间
    public static Vector2 PixelTopLeftToUiLocal(RectTransform ui,
                                                Vector2 pixelTopLeft, Vector2 imageSize);

    // 批量版本，复用 List 避免 GC
    public static void NormalizedToUiLocal(RectTransform ui,
                                           IList<Vector2> src, List<Vector2> dst);
    public static void PixelTopLeftToUiLocal(RectTransform ui, Vector2 imageSize,
                                             IList<Vector2> src, List<Vector2> dst);
}
```

> 与 `NpcRectPoolController` 里手写的 `nx * kWidth, ny * kHeight` 同一思路，但这里以 `RectTransform.rect.size` 为基准，并显式区分左上/左下系，避免业务方反复踩坑。

## 8. 性能与每帧更新

每帧调用 `SetPoints` 重画的场景下，重点要避免 GC 和重复工作：

1. **顶点 buffer 复用**：`UILineBuilder` 内部维护静态 `List<UIVertex>`、`List<int>` 作为复用缓冲，每次 `Build` 前 `Clear()`
2. **VertexHelper 直接灌**：用 `vh.AddVert` / `vh.AddTriangle` 而不是 `vh.AddUIVertexQuad`，避免每个 quad 都过中间数组
3. **脏标位**：只有真正修改了点序列、宽度、颜色、模式时才 `SetVerticesDirty`；颜色变化只需 `SetVerticesDirty`，材质变化才需 `SetMaterialDirty`
4. **对象池**：当业务需要同时存在多条线（比如多目标轨迹）时，复用同一个父节点下的 `UILine` 实例池，参考 `NpcRectPoolController` 的实现风格
5. **顶点上限**：按 `Geometric` 模式估算（最常用），设 N = 输入点数、`Kc` = Round Cap 扇形三角形数、`Kj` = Round Join 扇形三角形数。**每段顶点和接头处相邻段不做共享**，全部按"独立计入"算上界（实际共享会少几个，但作为上界足够）：

   ```
   每段贡献        :  8 顶点（4 核心 + 4 羽化），6 三角形（2 核心 + 4 羽化）
   每个 Round Cap  : 2Kc+3 顶点（Kc+2 核心 + Kc+1 羽化），3Kc 三角形（Kc 核心 fan + 2Kc 羽化 strip）
   每个 Round Join : 2Kj+3 顶点（同 Cap 结构），3Kj 三角形（同上）
   ```

   合计上界（开放折线，含两端 Round Cap、N-2 个 Round Join）：

   - **顶点数** `V ≈ 8·(N-1) + 2·(2Kc+3) + (N-2)·(2Kj+3)`
   - **三角形数** `T ≈ 6·(N-1) + 6·Kc + 3·Kj·(N-2)`

   工程上把 `Kc` / `Kj` 控制在 `≤ 16`，对 N ≤ 1000 点的折线顶点数 `V ≈ 8000 + ... ≈ 数万`，**远小于 UGUI 单 Mesh 的 65535 顶点上限**；当某次构造预估超限时，Builder 主动截断点序列并 `Debug.LogWarning`。SDF 模式顶点更省（无羽化条带），但每个 Round Join / Cap 换成 4 顶点圆盘 quad（2 三角形），量级与 Geometric 接近。

## 9. 文件结构与目录

```
Assets/
├── Scripts/UI/Line/
│   ├── UILine.cs                  // 主组件（MaskableGraphic）
│   ├── UILineBuilder.cs           // 顶点生成核心（静态、无状态、可单测）
│   ├── UILineTypes.cs             // LineCap / LineJoin / AntiAliasMode / DashStyle
│   ├── UILineDashSlicer.cs        // 虚线弧长裁切
│   ├── UILineUtils.cs             // 坐标转换辅助
│   └── Editor/
│       └── UILineEditor.cs        // 自定义 Inspector（可视化点编辑、AA 模式提示）
├── Shaders/UI/
│   └── UILineSDF.shader           // SDF 抗锯齿 shader（派生自 UI/Default.shader）
└── Material/
    └── UILineSdfMat.mat           // 引用 UILineSDF.shader
```

## 10. 使用示例

### 10.1 Inspector 拖拽用法

1. 在 `Canvas` 下任意位置新建一个空 GameObject，挂上 `UILine` 组件
2. 把 `RectTransform` 调成 anchor stretch、pivot `(0, 0)`、`offsetMin/Max = 0`，让它"贴满"父容器
3. 在 Inspector 里编辑 `points` 列表，运行即可看到线
4. 修改 `lineWidth` / `color` / `join` / `aaMode` 实时生效

### 10.2 代码驱动 — 每帧把检测结果画上去

```csharp
public class TrackOverlay : MonoBehaviour
{
    [SerializeField] UILine line;          // 指向场景里的 UILine
    [SerializeField] RectTransform mainImage;
    readonly List<Vector2> buf = new(64);

    void OnTrackUpdated(IReadOnlyList<Vector2> normalizedPoints)
    {
        buf.Clear();
        for (int i = 0; i < normalizedPoints.Count; i++)
            buf.Add(UILineUtils.NormalizedToUiLocal(mainImage, normalizedPoints[i]));

        line.SetPoints(buf);
    }
}
```

### 10.3 代码驱动 — 切换抗锯齿等级

```csharp
line.AAMode = AntiAliasMode.Geometric;
line.FeatherPixels = 1.0f;        // 等级 1：默认

line.FeatherPixels = 2.0f;        // 等级 2：更柔和

line.SdfMaterial = sdfMat;        // 必须先赋值，否则 AAMode = Sdf 会回退到 Geometric 并打 warning
line.AAMode = AntiAliasMode.Sdf;  // 等级 3：矢量 AA
```

## 11. 任务拆分与里程碑

| 阶段 | 内容 | 产出 |
| --- | --- | --- |
| M1 骨架 | `UILine` + `UILineTypes` + `UILineBuilder` 框架，最简实现：实线、Butt cap、Bevel join、无 AA | 能在 Inspector 里看到一条折线 |
| M2 Join + Cap | 完整实现 Round / Miter / Bevel 三种 join；Butt / Round / Square 三种 cap；miter limit 退化 | 主功能（不含 AA）可用：颜色、线宽、Round join、Cap 可选；对应 §12 第 1、2、3、5、6 条验收项 |
| M3 几何 AA | Feather 条带 + 窄线保护 + 扇形几何同步加 AA | `AAMode = Geometric` 可用，肉眼无锯齿 |
| M4 虚线 | `UILineDashSlicer` 弧长裁切 + 子段串联 | `dashStyle = Dashed` 可用，与 Round cap 配合自然 |
| M5 闭合 | `closed = true` 路径处理（忽略 Cap、首尾顶点也走 Join） | 闭合多边形描边 |
| M6 坐标工具 | `UILineUtils` 全套静态方法 + 业务侧示例脚本 | 给检测/轨迹类业务提供平滑接入 |
| M7 SDF 路线 | `UILineSDF.shader` + `UILineSdfMat.mat` + Builder 的 SDF 分支 | `AAMode = Sdf` 高质量矢量 AA 可用 |
| M8 自定义 Inspector | 点列表的可视化编辑、AA 等级说明、性能提示 | 易用性提升 |
| M9 性能与压测 | 缓冲池复用、顶点上限保护、对象池示例（参考 `NpcRectPoolController`） | 每帧多条线 ≥ 60FPS |

## 12. 验收标准

- [ ] 给定 ≥ 2 个点的数组能正确画出折线
- [ ] 颜色（含 alpha）实时生效
- [ ] 线宽实时生效，最小 1px、最大 ≥ 64px 仍正常
- [ ] 抗锯齿三种模式（`None` / `Geometric` / `Sdf`）切换正确；其中 `Geometric` 在 `featherPixels = 1px` 与 `featherPixels = 2px` 两组参数下视觉一致性可验证（合计四种典型组合）
- [ ] Round / Miter / Bevel 三种 join 切换无几何破洞；尖锐夹角下 Miter 自动退化
- [ ] Butt / Round / Square 三种 Cap 起止可独立配置
- [ ] 虚线模式下，每个 dash 都带完整 cap 样式、AA 正常
- [ ] 在 `Mask` 与 `RectMask2D` 内被正确裁剪（Geometric 和 Sdf 两种模式都验证）
- [ ] SDF 模式下，半透明颜色（如 `alpha = 0.3`）的折线在接头、Round Cap 处**不出现**因 quad 重叠导致的颜色变深条纹（M7 实现 §6.1 首选 discard 方案后验证）
- [ ] SDF + `Miter` 在尖锐夹角下自动退化为 Round；SDF + `Bevel` 的斜切边由段 quad 端面 AA，无额外几何破洞
- [ ] 每帧 `SetPoints`（点数 ≤ 256）不产生持续 GC（用 Profiler 校验）
- [ ] 支持 Inspector 拖拽用法和纯代码用法，两种方式行为一致
- [ ] 坐标转换工具方法在 `Canvas` 三种 `renderMode`（Overlay / Camera / WorldSpace）下都返回正确结果
