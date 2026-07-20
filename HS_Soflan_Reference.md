# MajSimaiX HS (Hi-Speed / Soflan / 变速) 完整参考

## 概述

HS（Hi-Speed）是 MajSimaiX 的视觉变速 / Soflan 系统。它只描述谱面时间轴上的视觉速度变化，不应改变音频时间、判定时间或 note 本身的逻辑时间。

当前实现分两层：

- **MajSimaiX 解析层**：解析 `<HS...>`、`<SV*x>`、Soflan group、插值采样，以及按物件声明的 FixedSoflan `@` 修饰符。
- **MajdataView 运行时层**：`SoflanManager` 把解析结果转换为运行时 Soflan 时间轴，具体物件类决定是否使用 Soflan 时间轴显示。

支持的 HS 模式：

1. **全局变速**：默认 group `0`，未进入分组作用域的音符使用全局 HSpeed。
2. **分组变速（Grouped Soflan）**：不同 group 拥有独立 HSpeed 时间轴，音符可进入指定 group。
3. **插值变速与 easing**：按线性或 easings.net 曲线把一段时间内的 HSpeed 展开为采样点。
4. **未定义组号变速组**：用 `<HS?*...>(...)` 创建不会与手动组号冲突的自动 group。
5. **FixedSoflan**：按单个 Tap / Star 系物件声明固定视觉速度，使其 Soflan 显示进度不受玩家 note speed 影响。

`<SV*x>` 是 group `0` 的直接 HSpeed 覆盖，不是独立倍率；它与 `<HS*x>`、`<HS0*x>`
共享同一事件线。

---

## 一、核心数据模型

### 解析输出

`SimaiTimingPoint` 保存 timing 级别的 Soflan 信息：

```csharp
public float HSpeed { get; set; } = 1f;
public int SoflanGroup { get; } = 0;
```

`SimaiNote` 保存 note 级别的 Soflan / FixedSoflan 信息：

```csharp
public int SoflanGroup { get; set; } = 0;
public int SlideSoflanGroup { get; set; } // 未显式设置时继承 SoflanGroup
public bool IsFixedSoflan { get; set; }
public bool HasFixedSoflanSpeed { get; set; }
public float FixedSoflanSpeed { get; set; } = DefaultFixedSoflanSpeed;
public const float DefaultFixedSoflanSpeed = 600f;
```

解析后 `SimaiRawTimingPoint.GetTimingPoint()` 会把当前 timing 的 `SoflanGroup` 传给 `SimaiNoteParser.GetNotes(...)`。当 group 非 `0` 时，该 timing 下解析出的每个 note 都会写入相同的 `SimaiNote.SoflanGroup`。对于 Slide，`SoflanGroup` 表示星头分组，`SlideSoflanGroup` 表示整条轨迹（包括连接段和 same-head 分支）的分组。未显式拆分时，`SlideSoflanGroup` 继承 `SoflanGroup`。

| 写法 | 星头 `SoflanGroup` | 轨迹 `SlideSoflanGroup` |
| --- | ---: | ---: |
| `<HS44>(1-3[4:1]V57[5:1])` | `44` | `44` |
| `<HS44>(1)-3[4:1]V57[5:1]` | `44` | `0` |

### Native AOT ABI

Native AOT 的 `UnmanagedSimaiNote.slideSoflanGroup` 在 x64 布局中复用 `rawContentLen` 与 `rawContent` 指针之间原有的 4 字节对齐 padding。因此 x64 下结构大小仍为 64 字节，`rawContentLen`、`slideSoflanGroup`、`rawContent` 的偏移分别为 48、52、56。

该兼容保证和当前 native 构建示例仅覆盖 x64。32 位布局没有同一段指针对齐 padding，加入 `slideSoflanGroup` 后不保证与旧版 native ABI 二进制兼容；32 位调用方不得照搬 x64 结构布局。

### 运行时转换

`SoflanManager.loadChart(...)` 遍历 `SimaiTimingPoint`：

- BPM 变化进入 `bpmList`。
- `HSpeed` 变化按 `SoflanGroup` 生成 `KeyframeSoflan`。
- 只要出现至少一个 `HSpeed != 上一次同组 HSpeed`，`containsSoflans()` 变为 `true`。
- note 的 `SoflanGroup` 被登记；当前运行时主要使用 timing / component 上的 group 计算显示时间轴。

运行时 Soflan 时间差由 `NoteDrop` 提供：

```csharp
protected float GetSoflanValue(float inputMsec) =>
    SoflanManager.Instance.ConvertAudioTimeToY_PreviewMode(inputMsec, soflanGroup, speed);

protected float GetSoflanTiming() =>
    (GetSoflanValue(timeProvider.AudioTime * 1000.0f) - soflanTime) / 1000.0f;
```

其中 `soflanTime` 是 note 判定时刻在对应 group Soflan 时间轴上的 Y 值。判定仍使用真实 `AudioTime - time`。

---

## 二、HS 语法格式

### 1. 全局瞬时变速 `<HS*x>`

```text
<HS*1.0>
<HS*2.5>
```

- `x` 为浮点数，表示速度倍率。
- 作用于声明之后未进入分组作用域的音符。
- 默认值为 `1.0`。

示例：

```text
(120)
1,2,3,4,          // HSpeed = 1.0
<HS*2.0>
1,2,3,4,          // HSpeed = 2.0
<HS*0.5>
1,2,3,4,          // HSpeed = 0.5
```

### 1.1 SV 兼容标签 `<SV*x>`

```text
<SV*2>       group 0 HSpeed = 2
<SV*0>       停止视觉时间轴
<SV*-1>      反向视觉时间轴
<SV*1>       显式恢复默认速度
```

- 只接受大写 `SV` 和瞬时 `<SV*number>` 形式；数值解析与瞬时 `<HS*x>` 相同，使用
  invariant culture，并允许 `0`、负数、正号、指数及 HS 接受的特殊值。
- SV 直接写入 group `0` 的 `HSpeedEvent` 和一个空 `SimaiTimingPoint`，不维护 `SVeloc`
  或乘法状态；`CommaTimings.HSpeed` 仍为 `1`。
- `<SV*x>`、`<HS*x>`、`<HS0*x>` 在同一实际 timing 按文本顺序执行，最后一个值覆盖前面的
  值；速度持续到下一次 group `0` HS/SV 声明。
- 标签可位于 note 前后；`/` 多押共享最终值。fake-each 的每个展开子项按实际 timing
  查询速度，因此跨槽子项可被下一槽命令覆盖。
- SV 不支持 group 前缀、`?` group、note group scope、duration、`~` 插值链或 easing；
  紧随其后的 `(120)` 仍按普通 BPM 声明解析；
  这些形式抛出 `InvalidSimaiSyntaxException`（非法数值仍沿用 HS 的
  `InvalidSimaiMarkupException`）。标签必须闭合。
- 现有 HS 插值在其覆盖区间内删除/覆盖较早的 group 0 SV 事件，规则不变。

### 2. 分组瞬时变速 `<HSg*x>`

```text
<HS2*1.25>
<HS0*3.0>
```

- `g` 为非负整数，表示 Soflan group。
- `x` 为浮点数，表示该 group 的速度倍率。
- 声明后，该 group 的当前速度会被记录，后续 `<HSg>(...)` 或 `<HSg>` 可复用。
- `<HS0*x>` 合法，表示显式修改默认 group `0` 的速度。

### 3. 插值变速与 easing `<HS*x[duration]easing>` / `<HSg*x[duration]easing>`

```text
<HS*2.0[8:1]>
<HS1*0.5[#1.25]>
<HS2*3.0[150#4:1]>
<HS*4[4:1]easeInOutCirc>
<HS*4[4:1]ioCubic>
<HS*4[#0]>
```

- `duration` 复用 Hold 持续时间语法：
  - `[8:1]`：按当前 BPM 计算比例时值。
  - `[150#8:1]`：按指定 BPM 计算比例时值。
  - `[#1.25]`：直接指定秒数。
- HS 的速度和 duration 数字统一按 invariant culture 解析，使用 `.` 作为小数点。
- 只有绝对秒数形式允许真正的零时长：`[#0]`、`[#0.0]`、`[#+0]` 和 `[#0e0]` 都表示在该段边界瞬间切换到目标速度。边界时刻本身使用切换后的速度。
- `[8:0]`、`[150#8:0]`、负零、负数、非有限值，以及正数下溢成 `0` 的 duration 仍然非法。
- 使用 duration 的 HS 命令必须出现在有效 BPM 声明之后，即使所有段都是 `[#0]`；无 duration 的 `<HS*x>` 不受此限制。
- 语义为：从 `nowTime - duration` 开始，将该 group 在起点时的有效 HSpeed 按指定 easing 插值到 `nowTime` 的目标速度 `x`。
- `[duration]` 后可选写 easing。省略或显式写 `linear` 时使用线性插值；零时长段允许保留任意合法 easing，但 easing 不产生效果。无 duration 的瞬时变速仍不能附加 easing。
- easing 名称大小写不敏感，支持 easings.net 的完整名称：`easeInX`、`easeOutX`、`easeInOutX`；也支持对应缩写 `iX`、`oX`、`ioX`。
- `X` 支持 `Quad`、`Cubic`、`Quart`、`Quint`、`Sine`、`Expo`、`Circ`、`Back`、`Elastic`、`Bounce`。例如 `easeInOutCirc` 等价于 `ioCirc`，`easeInOutCubic` 等价于 `ioCubic`。
- easing 名称前后允许空白或换行，名称内部不允许空白或换行；未知名称会抛出包含原名称的 `InvalidSimaiSyntaxException`。
- 解析阶段会展开为空 `noteContent` 的 `SimaiRawTimingPoint`，运行时把这些空点视为 HSpeed 变化点。
- 插值采样按命令处当前 BPM 的 384-grid 对齐，间隔由 `HSpeedInterpolationGrid` 控制，默认每 32 grid 采样一次。
- 零时长段不参与 interpolation grid 采样，只在自身边界生成一个空 HSpeed timing point，因此没有音符的边界也能被频谱显示和 MA2 导出观察到。
- easing 只映射每个采样点的 HSpeed 进度：`speed = start + (target - start) * easing(progress)`；不改变采样时间、音频时间、判定时间或物件逻辑时间。
- `Back`、`Elastic`、`Bounce` 保留 easings.net 的原始曲线特性；其中 `Back`、`Elastic` 允许中间采样值超出起点到目标值的范围，`Bounce` 保留回弹。起点和终点仍强制精确等于相应速度。
- `startTime` 和 `nowTime` 保留真实时间；即使不落在 32-grid 上也会生成端点。
- 如果理论 `startTime < 0`，小于 `0` 的采样点会被忽略，但会额外在 `t = 0` 生成一次插值点。
- 终点 `nowTime` 必定生成，速度精确等于 `x`。
- 同 group `(startTime, nowTime]` 内已有的 HS 空点会被后解析的插值命令覆盖。
- 插值完成后，该 group 当前速度更新为 `x`。
- easing 只在解析期用于生成采样点，不保存在公开 `SimaiChart` 数据模型中。导出 MA2 时只会得到烘焙后的 `SFL` 速度点，无法还原原始 easing 名称。

### 4. 链式插值 `<HS*x[duration]~y[duration]...>` / `<HSg*x[duration]~y[duration]...>`

```text
<HS*2.0[8:1]~1.0[4:1]~-0.5[#1.25]>
<HS1*0[4:1]~-1[4:1]>
<HS*2.0[8:1]ioCubic~1.0[4:1]oBounce>
<HS*2[#0]~4[#1]>
<HS*2[#1]~4[#0]~8[#1]>
```

- 使用 `~` 串联多个插值段，每一段都必须写成 `targetHSpeed[duration]easing`，其中 easing 后缀可省略。
- 每段 easing 独立生效；某段省略 easing 时该段使用线性，不继承前一段。
- 总时长为所有 `duration` 之和，整条曲线从 `nowTime - totalDuration` 开始，在 `nowTime` 到达最后一个目标速度。
- 第一段从起点时的有效 HSpeed 渐变到第一个目标速度；后续每段从上一段目标速度渐变到下一段目标速度。
- 零时长段按链中从左到右的顺序，在当前段边界立即赋值；它会切断插值连续性，并成为后续正时长段的起始速度。
- 同一边界连续出现多个零时长段时，最后一个目标速度生效；链首、链中、链尾和只有一个 segment 的零时长段都合法。
- 各段边界和 `nowTime` 都会强制生成采样点，即使边界不落在 interpolation grid 上。
- 负数和 `0` HSpeed 允许，例如 `~-1[4:1]`。
- 链中不能省略 duration；`<HS*2~1[4:1]>` 和 `<HS*2[4:1]~1>` 仍是非法语法，瞬时段必须显式写成 `targetHSpeed[#0]`。
- `[#0.00000001]` 等有限正时长仍按真实插值处理，不会自动转换为瞬时段；小于等于内部同刻容差 `1e-9s` 的变化可能不可观察。
- 同 group 同一时刻的 HS 命令按谱面文本顺序执行，后者覆盖前者；该时刻的音符和空 timing point 都使用执行完所有同刻命令后的最终速度。
- 理论起点小于 `t=0` 时，仍先在完整理论时间轴执行零时长段；负时间采样点会被丢弃，但这些瞬时赋值会影响 `t=0` 的速度。
- `<HS*2.0[8:1]>` 等价于只有一个 segment 的链式插值。
- 链式插值允许不同段混用完整名称、缩写和默认线性。

### 5. 分组作用域 `<HSg*x>(...)` / `<HSg>(...)`

```text
<HS2*2.0>(1,2,3,4),
<HS1>(5,6,7,8),
<HS1*1.5[8:1]>(1,2,3,4),
<HS2*2.0>(1)-3[4:1],
<HS2*2.0>(1,2)-4[4:1],
<HS45*2.5>(1-3[4:1]V57[5:1]),
```

- 括号 `()` 内的所有音符属于指定 Soflan group。
- 括号结束后自动退出分组模式，回到默认 group `0`。
- 括号内可包含逗号分隔的多个拍位。
- 把完整 Slide 放在作用域内时，例如 `<HS45*2.5>(1-3[4:1]V57[5:1])`，星头和所有轨迹段都属于 group `45`。
- 当作用域只包住一个 Slide 星头且 `)` 后紧接 slide mark 时，例如 `<HS2>(1)-3[4:1]`，只有星头属于 group `2`，轨迹仍属于作用域外的默认 group `0`。`)` 与 slide mark 之间允许空白。
- head-only 特例要求闭括号前的最后一个音符是单个可构成 Slide 的星头；括号内更早的逗号分隔音符仍可正常保留在该 HS group 中。例如 `<HS2>(1,2)-4[4:1]` 合法，`1` 和星头 `2` 属于 group `2`，但轨迹属于 group `0`。最后一个音符不能与 `/` each、反引号 fake-each 混用，也不能只包住连接 Slide 的前半段，例如 `<HS2>(1/2)-4[4:1]`、``<HS2>(1`2)-4[4:1]``、`<HS2>(1-4[4:1])-6[4:1]` 均非法。
- Slide path 内不能嵌入 HS 声明；例如 `1-<HS44>(2[4:1])V<HS30>(35[5:1])` 不受支持。需要变速时，应选择“完整 Slide 同组”或“仅星头分组”两种作用域写法。
- `@` FixedSoflan 即使与 head-only 作用域组合，也仍只修饰星头；它不会把 FixedSoflan 或星头 group 传播到 Slide body。
- 括号作用域内不支持再写任何 HS 声明，遇到会抛出 `InvalidSimaiSyntaxException`。
- 括号作用域内也不支持 SV 声明。

### 6. 未定义组号变速组 `<HS?*x>(...)`

```text
<HS?*2.0>(1,2,3,4),
<HS?*1.5[8:1]>(5,6,7,8),
```

- `?` 表示该变速组的最终组号由导出器自动分配，避免与手动组号或默认组 `0` 冲突。
- 每次出现 `<HS?*...>(...)` 都会创建一个新的独立组，不能回引复用；如需复用同一组，请使用显式组号 `<HSg*...>` / `<HSg>(...)`。
- 解析阶段使用内部负数组号 `-1, -2, -3, ...` 标记这些自动组。
- `?` 组必须同时写出速度值和括号作用域；`<HS?>`、`<HS?*2.0>` 都是非法语法。
- 导出 MA2 时，自动组会映射为未使用的非 0 正数组号：
  - 设谱面中手动显式组号最大值为 `M`。
  - 自动组起始编号为 `10^(M 的十进制位数)`；如果没有显式非 0 组号，则从 `10` 开始。
  - 自动组按首次出现顺序依次取起始编号、起始编号 + 1、起始编号 + 2 ...
  - 例如显式最大组号为 `122` 时，自动组从 `1000` 开始；显式最大组号为 `16548` 时，自动组从 `100000` 开始。

---

## 三、FixedSoflan 语法

MajSimaiX 使用 `@` 作为 FixedSoflan note 修饰符。它是**单个 note 的修饰符**，不是 HS 命令，也不是 group 命令。

### 1. Tap / Star 头语法

```text
1@
1@600
1@750.5
1@-3[8:1]
1@600-3[8:1]
1@w5[8:1]
```

含义：

- `@`：启用 FixedSoflan，固定速度使用默认值 `600`。
- `@600` / `@750.5`：启用 FixedSoflan，并显式指定固定视觉速度。
- 对 Slide / Wifi 的星星头，`@` 必须写在第一个 slide mark 之前，例如 `1@-3[8:1]`。
- `@` 只修饰当前 token；`1@/2` 中只有 `1` 是 FixedSoflan。
- `@` 不自动创建 Soflan group，也不自动创建 HSpeed 变化。它只改变该 note 在 Soflan 显示分支中使用的 Tap / Star 视觉速度。

### 2. 与 group 的关系

FixedSoflan 和 Soflan group 是两件事：

```text
<HS1*2.0>(1@,2,3@750,)
```

- `1@`、`3@750` 属于 group `1`，并启用 FixedSoflan。
- `2` 属于 group `1`，但不启用 FixedSoflan。
- 如果没有任何 HSpeed 变化导致 `SoflanManager.containsSoflans()` 为 `true`，当前 MajdataView 运行时不会进入 Tap / Star 的 Soflan 分支；此时 `@` 虽然被解析，但显示逻辑仍走普通路径。

### 3. 速度解析规则

- 速度使用 invariant culture 浮点解析。
- 速度必须是正数。
- `NaN`、`Infinity`、`0`、负数非法。
- `@` 后速度为空表示默认 `600`。
- 速度文本内部不允许空白。
- 同一个 note token 内只能出现一个 `@`。

### 4. 位置限制

合法：

```text
1@
1@600
1@600-3[8:1]
```

非法：

```text
@1
1@@
1@ 600
1-3@600[8:1]
```

说明：

- 对非 slide token，`@` 必须位于 note token 末尾或末尾速度之前。
- 对 slide / wifi token，`@` 必须位于星星头之后、第一个 slide mark 之前。
- Slide no-head / delayed no-head 不能携带 `@`；解析器会报 `FixedSoflan modifier is only supported on slide star heads`。
- 解析器会先检查 `@` 附近空白，再去除 raw content 中其它空白。

---

## 四、解析流程与约束

### HS/SV 标签解析

```text
遇到 '<'
  |
  +-- 读取到 '>' 之间的内容
  |
  +-- 验证内容以 "HS" 或 "SV" 开头
  |
  +-- 查找 '*'
      |
      +-- 有 '*'
      |   |
      |   +-- '*' 前有 group: <HSg*x> / <HS?*x>
      |   |   +-- <HS?*x> 分配内部负数组号 -1, -2, ...
      |   |   +-- <HSg*x> 解析 g，要求 g >= 0
      |   |   +-- 解析 x 或 x[duration]easing / 链式 segment
      |   |   +-- 瞬时命令写入 group 当前速度
      |   |   +-- 插值命令展开为 HSpeed 空点
      |   |
      |   +-- '*' 前无 group: <HS*x>
      |       +-- 解析 x 或 x[duration]easing / 链式 segment
      |       +-- 瞬时命令写入全局当前速度
      |       +-- 插值命令展开为默认 group 0 的 HSpeed 空点
      |
      +-- 无 '*': <HSg>（SV 无此形式）
          |
          +-- 解析 g，要求 g > 0
          +-- 如果后面是 (...)，进入分组作用域
          +-- 否则生成空 TimingPoint，记录该 group 当前速度

如果是 <HS?*x>:
  |
  +-- x 必须存在并合法
  +-- 标签后必须跟 (...)
  +-- 没有作用域时抛出 InvalidSimaiSyntaxException
```

提交 note 时，当前 timing 的 HSpeed 由当前 group 决定：

```csharp
var noteHSpeed = currentSoflanGroup != 0
    ? (hsGroupSpeeds.TryGetValue(currentSoflanGroup, out var nghs) ? nghs : 1f)
    : curHSpeed;
```

最终输出前会调用 `BuildFinalHSpeedEvents(...)` 和 `GetEffectiveHSpeed(...)`，按同 group、同时间、解析顺序合并 HSpeed 事件，并把每个 raw timing 的最终有效 HSpeed 补齐。

对于 SV，解析器只走瞬时分支并固定使用 group `0`；`SV` 事件与 HS0 事件共享上述合并表。
同一实际 timing 的空点只保留最后一个声明。fake-each 不在生成 raw point 时固化速度，
最终阶段会按展开后的 timing 再查询事件线。

### FixedSoflan 解析

`SimaiRawTimingPoint` 先移除 lowercase `c`，再检查 raw content 中 `@` 附近空白是否合法，
最后移除一般空白。`SimaiNoteParser.TryGetSingleNote(...)` 再执行：

```text
TryParseFixedSoflanModifier(noteText)
  |
  +-- 没有 '@': 不启用 FixedSoflan
  |
  +-- 多个 '@': 非法
  |
  +-- 有 slide mark
  |   |
  |   +-- '@' 在第一个 slide mark 后: 非法
  |   +-- 解析 '@' 到 slide mark 之间的速度文本
  |   +-- 移除该修饰符后继续按普通 slide 解析
  |
  +-- 没有 slide mark
      |
      +-- 解析 '@' 后的速度文本
      +-- '@' 前内容作为普通 note 解析
```

解析成功后写入：

```csharp
simaiNote.IsFixedSoflan = isFixedSoflan;
simaiNote.HasFixedSoflanSpeed = hasFixedSoflanSpeed;
simaiNote.FixedSoflanSpeed = fixedSoflanSpeed;
```

---

## 五、MajdataView 物件 Soflan 支持矩阵

下表描述的是当前主工程 `Assets/Scripts` 的运行时行为。解析器能把 `SoflanGroup`、`SlideSoflanGroup` 和 `FixedSoflan` 字段写入 note，MajdataEdit 也能按这两个 group 分别导出 Slide 星头与 body 的 MA2 标记，但这不代表对应 Unity 组件一定会使用 Soflan 时间轴显示。当前捆绑的 MajdataView `SlideDrop` / `WifiDrop` 尚不读取 `SlideSoflanGroup`，因此上述 body 分组是解析与导出契约，不是对预览运行时 body 动画变速支持的声明。

| 物件 / 组件 | Soflan 时间轴显示 | FixedSoflan | 运行时依据 |
| --- | --- | --- | --- |
| Tap / Break / EX Tap (`TapBase`, `TapDrop`) | 支持 | 支持 | `TapBase.Update_soflan()` 使用 `GetSoflanTiming()`；`GetSoflanTapDistance/Scale()` 在 `isFixedSoflan` 时使用 `fixedSoflanSpeed`。 |
| Force Star / 普通 Star (`StarDrop`) | 支持 | 支持 | `StarDrop.Update_soflan()` 镜像 Tap 的 Soflan 位移和缩放，FixedSoflan 复用 `TapBase` 算法。 |
| Slide 星星头 (`StarDrop`) | 支持 | 支持，仅星星头 | `JsonDataLoader.InstantiateStar(...)` 对星星头调用 `ApplyFixedSoflan(...)` 并写入 `soflanGroup/soflanTime`。 |
| Wifi 星星头 (`StarDrop`) | 支持 | 支持，仅星星头 | `InstantiateWifi(...)` 中星星头同样应用 FixedSoflan 和 Soflan timing。 |
| Hold / BreakHold / EX Hold (`HoldDrop`) | 支持 | 不支持 | `HoldDrop.Update_soflan()` 使用 `GetSoflanTiming()` 和 `GetSoflanEndTiming()`，但没有 FixedSoflan 字段和固定速度算法。 |
| Touch (`TouchDrop`) | 支持 | 不支持 | `TouchDrop.Update_soflan()` 使用 `GetSoflanTiming()` 计算风扇位移和显示；判定仍使用真实 `AudioTime - time`。 |
| TouchHold (`TouchHoldDrop`) | 支持 | 不支持 | `TouchHoldDrop.Update_soflan()` 使用 `GetSoflanTiming()` 和 `GetSoflanEndTiming()`；mask 填充进度按 Soflan 时间轴换算；判定仍使用真实 timing。 |
| Slide body (`SlideDrop`) | 不支持 | 不支持 | `SlideDrop` 不读取 `SoflanManager`，也不调用 `GetSoflanTiming()`；移动、星星 guide、销毁均基于真实 `AudioTime`。 |
| Wifi body (`WifiDrop`) | 不支持 | 不支持 | `WifiDrop` 不读取 Soflan 时间轴；只使用真实 slide start / duration。 |
| EachLine (`EachLineDrop`) | 不支持 | 不支持 | 该组件不继承 `NoteDrop`，只用 `AudioTime - time` 和 `speed` 计算 scale / visible。 |

关键边界：

- 当前 SlideDrop / WifiDrop 不受 SoflanTiming 影响。只有它们的星星头可以受 Soflan / FixedSoflan 影响。
- `StarDrop.Update_soflan()` 激活 slide body 时使用 `GetTapScale(GetJudgeTiming()) >= 1f`，即真实音频 timing 的普通 Tap scale，而不是 Soflan timing。这用于保证 slide body 出现时机不被 Soflan 时间轴改变。
- Touch / TouchHold 的 `Update_soflan()` 使用 Soflan timing 计算风扇位移和显示；判定仍使用真实 `AudioTime - time`，不受 HS 影响。
- 判定统一不受 Soflan 影响；各物件判定仍使用真实音频时间。

---

## 六、FixedSoflan 运行时细节

### 接入点

`JsonDataLoader.ApplyFixedSoflan(...)` 把解析字段写入 `TapBase`：

```csharp
private void ApplyFixedSoflan(TapBase noteDrop, SimaiNote note)
{
    noteDrop.isFixedSoflan = note.IsFixedSoflan;
    noteDrop.fixedSoflanSpeed = note.FixedSoflanSpeed;
}
```

它只会被调用在 `TapBase` 派生物件上，包括 Tap、Force Star、Slide 星星头、Wifi 星星头。

### 固定速度算法

`TapBase` 在 Soflan 分支中使用固定视觉速度：

```csharp
protected bool IsFixedSoflanEnabled()
{
    return isFixedSoflan && fixedSoflanSpeed > 0f;
}

protected float GetSoflanNoteSpeedValue()
{
    return IsFixedSoflanEnabled() ? fixedSoflanSpeed : noteSpeedValue;
}
```

基础时间窗：

```csharp
DefaultMsec = 240000 / speedValue
```

Mai bug 修正：

```csharp
speedRatio = speedValue / 150
MaiBugAdjustMSec =
    (speedRatio - 1) * (-0.5 / speedRatio) * 1.6 * 1000 / 60
```

移动和缩放时间点：

```csharp
MoveStartTime = DefaultMsec - MaiBugAdjustMSec
ScaleStartTime = 2 * DefaultMsec - MaiBugAdjustMSec
```

Soflan Tap 位移和缩放：

```csharp
progress = (MoveStartTime + timing * 1000) / (2 * MoveStartTime)
outsideDistance = 4.8 + (4.8 - 1.225)
distance = Lerp(1.225, outsideDistance, progress)

scale = (ScaleStartTime - Abs(timing * 1000)) / DefaultMsec
```

其中 `timing` 在 Soflan 谱面中来自 `GetSoflanTiming()`，不是 `AudioTime - time`。FixedSoflan 只替换 `speedValue`，不替换 timing 来源。

### 默认速度 600 的含义

`@` 等价于 `@600`。固定速度 `600` 时：

```text
DefaultMsec = 400ms
MaiBugAdjustMSec = -10ms
MoveStartTime = 410ms
ScaleStartTime = 810ms
```

因此 FixedSoflan Tap / Star 会用固定 `600` 的显示窗口计算移动和缩放进度。玩家 note speed 改变时，该物件在 Soflan 分支里的移动 / 缩放进度保持一致。

### 生效边界

- FixedSoflan 只在 `SoflanManager.containsSoflans()` 为 `true` 时进入当前 Tap / Star Soflan 分支。
- 无 HS 变化的谱面中，`@` 会被解析，但 `TapBase.Update()` 走普通路径，当前普通路径不会调用 `GetSoflanTapDistance/Scale()`。
- FixedSoflan 不改变可判定时间、miss 时间、autoplay 时间。
- FixedSoflan 不扩散到同 timing 的其它 each note，也不扩散到 slide body / wifi body。

---

## 七、完整示例

### 示例 1：传统全局变速

```text
(120)
{4}
<HS*1.0>1,2,3,4,
<HS*2.0>5,6,7,8,
<HS*1.0>1,2,3,4,
```

效果：第二行开始默认 group `0` 的物件以 2 倍 HS 时间轴显示。

### 示例 2：分组变速

```text
(150)
{4}
<HS1*2.0>(1,2,3,4),
<HS2*0.5>(5,6,7,8),
1,2,3,4,
```

效果：

- 组 `1` 的物件使用 2.0 倍 HSpeed。
- 组 `2` 的物件使用 0.5 倍 HSpeed。
- 最后一行回到默认 group `0`，使用全局速度。

### 示例 3：先声明速度，后引用 group

```text
(120)
{4}
<HS1*1.5>
<HS2*3.0>
<HS1>(1,2,3,4),
<HS2>(5,6,7,8),
```

效果：

- 先声明 group `1` 速度 1.5、group `2` 速度 3.0，并生成空 TimingPoint。
- 后续 `<HS1>(...)` 和 `<HS2>(...)` 引用已声明速度。

### 示例 4：FixedSoflan Tap

```text
(130)
{4}
<HS1*2.0>(1@,2,3@750,4,),
```

效果：

- `1@` 属于 group `1`，使用默认固定速度 `600` 计算 Soflan Tap 视觉进度。
- `3@750` 属于 group `1`，使用固定速度 `750`。
- `2`、`4` 属于 group `1`，但按玩家 note speed 计算 Soflan Tap 视觉进度。

### 示例 5：FixedSoflan Slide 星星头

```text
(130)
{4}
<HS1*2.0>(1@-3[8:1],2@750w5[8:1],),
```

效果：

- `1@-3[8:1]` 和 `2@750w5[8:1]` 的星星头受 Soflan / FixedSoflan 影响。
- SlideDrop / WifiDrop 本体不受 SoflanTiming 影响，仍按真实 slide start / duration 播放。

### 示例 6：未定义组号自动分配

```text
(120)
{4}
<HS1*1.0>(1,),
<HS?*1.5>(2@600,),
<HS?*0.5>(3,),
```

解析阶段：

- `<HS1*1.0>(1,)` 使用显式组 `1`
- 第一个 `<HS?*1.5>(...)` 使用内部组 `-1`
- 第二个 `<HS?*0.5>(...)` 使用内部组 `-2`

导出 MA2 时：

- 显式最大组号 `M = 1`，自动组从 `10` 开始
- 内部组 `-1` 映射到 MA2 group `10`
- 内部组 `-2` 映射到 MA2 group `11`
- `2@600` 会随所在自动组导出为 `#10F600`

### 示例 7：Mine 与 MA2 Soflan 尾块共存

MajdataEdit 使用私有 `!m` 修饰符在 MA2 物件记录中保留 Mine 语义。`!m` 与
`#groupFspeed` 位于同一个制表符分隔的尾字段中，二者相对顺序不构成语义：

```text
!m
!m#12
!m#F600
!m#12F600
#12F600!m
```

MajdataEdit 规范输出把 `!m` 放在 `#` 前；读取方必须同时接受 `!m#12F600` 和
`#12F600!m`。实现读取器时，应先识别并移除一次精确的小写 `!m`，再按原有规则解析
`#groupFspeed`，不能依赖修饰符顺序。

- Tap、Hold、Touch、TouchHold 和 Slide 星头按 `IsMine` 标记。
- Slide/Wifi 轨迹按 `IsMineSlide` 标记；星头与轨迹不会互相传播。
- 无头 Slide 只生成轨迹标记。
- 连接 Slide 沿用当前 `SimaiNote` 粒度，拆出的轨迹记录共享 `IsMineSlide`。
- `!m` 不创建新 MA2 物件 ID，也不改变 MA2 汇总统计。

---

## 八、错误处理

| 错误类型 | 示例 | 异常类型 / 信息 |
| --- | --- | --- |
| HS/SV 内容不以合法前缀开头 | `<XX*1.0>`、`<sv*1.0>` | `InvalidSimaiSyntaxException` |
| 多个 `*` | `<HS*1*2>` | `InvalidSimaiSyntaxException` |
| group 非整数或负数 | `<HS-1*1.0>`, `<HSabc*1.0>` | `InvalidSimaiSyntaxException` |
| 仅 group 格式中 group <= 0 | `<HS0>`, `<HS-1>` | `InvalidSimaiSyntaxException` |
| 未定义 group 缺少速度值 | `<HS?>` | `InvalidSimaiSyntaxException` |
| 未定义 group 缺少括号作用域 | `<HS?*1.5>` | `InvalidSimaiSyntaxException` |
| HSpeed 非数字 | `<HS*abc>` | `InvalidSimaiMarkupException` |
| SV 非数字 | `<SV*abc>` | `InvalidSimaiMarkupException` |
| SV group / `?` group | `<SV1*2>`, `<SV?*2>`, `<SV?>` | `InvalidSimaiSyntaxException` |
| SV duration / 插值 / easing | `<SV*2[4:1]>`, `<SV*2~1>`, `<SV*2[4:1]ioCubic>` | `InvalidSimaiSyntaxException` |
| SV group scope | `<SV*2>(1,)` | `InvalidSimaiSyntaxException` |
| 插值时长非法 | `<HS*2.0[8:0]>`, `<HS*2.0[150#8:0]>`, `<HS*2.0[#-0]>`, `<HS*2.0[#NaN]>` | `InvalidSimaiSyntaxException` |
| duration 位于有效 BPM 之前 | `<HS*2.0[#0]>(120)` | `InvalidSimaiSyntaxException` |
| 只有 group 却带时长 | `<HS1[8:1]>` | `InvalidSimaiSyntaxException` |
| 未知 easing | `<HS1*2.0[8:1]ioCubci>` | `InvalidSimaiSyntaxException`: `Unknown HSpeed easing "ioCubci"` |
| easing 用于瞬时变速 | `<HS1*2.0ioCubic>` | `InvalidSimaiMarkupException` |
| easing 名称内部含空白或换行 | `<HS1*2.0[8:1]io Cubic>` | `InvalidSimaiSyntaxException` |
| 分组作用域内声明 HS/SV | `<HS1>(<HS1*2.0>1,)`, `<HS1>(<SV*2>1,)` | `InvalidSimaiSyntaxException` |
| head-only Slide 与 each / fake-each 混用 | `<HS1>(1/2)-3[4:1]`, ``<HS1>(1`2)-3[4:1]`` | `InvalidSimaiSyntaxException` |
| 只包裹部分 Slide path | `<HS1>(1-3[4:1])-5[4:1]` | `InvalidSimaiSyntaxException` |
| Slide path 内嵌 HS | `1-<HS44>(2[4:1])V<HS30>(35[5:1])` | `InvalidSimaiSyntaxException` |
| 变速标签未闭合 | `<SV*2`, `<HS*2` | `InvalidSimaiSyntaxException` |
| lowercase `c` | `1c`, `1-3[8:1]c` | 在 raw timing/note RawContent 中移除，等价于去掉 `c` |
| `<` 后内容不足 | `<H>` | `InvalidSimaiMarkupException` |
| `@` 不在 note token 末尾或 slide mark 前 | `@1`, `1-3@600[8:1]` | `Invalid FixedSoflan modifier` |
| 同一 token 多个 `@` | `1@@`, `1@600@700` | `Invalid FixedSoflan modifier` |
| `@` 速度含空白 | `1@ 600`, `1@6 00` | `Invalid FixedSoflan modifier` |
| `@` 速度非法 | `1@0`, `1@-600`, `1@NaN`, `1@Infinity` | `Invalid FixedSoflan modifier` |
| Slide no-head 携带 `@` | no-head / delayed no-head slide token 中写 `@` | `FixedSoflan modifier is only supported on slide star heads` |

---

## 九、实现文件索引

MajSimaiX 解析层：

- `Runtime/SimaiParser.cs`：`<HS...>`/`<SV*x>` 标签、group 作用域、插值采样、最终 HSpeed 事件合并。
- `Runtime/SimaiRawTimingPoint.cs`：lowercase `c` 规范化、raw content 空白处理、FixedSoflan `@` 空白规则、`SoflanGroup` / `SlideSoflanGroup` 传递。
- `Runtime/SimaiNoteParser.cs`：note 解析、星头/body group 写入、`@` 修饰符、FixedSoflan 字段写入、slide star head 限制。
- `Runtime/SimaiNote.cs`：`SoflanGroup`、`SlideSoflanGroup`、`IsFixedSoflan`、`HasFixedSoflanSpeed`、`FixedSoflanSpeed` 数据字段。
- `Runtime/Unmanaged/UnmanagedSimaiNote.cs`：Native AOT note 布局；x64 下用既有 padding 保存 `slideSoflanGroup`。

MajdataView 运行时：

- `Assets/Scripts/Misc/SoflanManager.cs`：HSpeed 到 Soflan 时间轴的转换、group 缓存、`containsSoflans()`。
- `Assets/Scripts/JsonDataLoader.cs`：把解析结果写入各 Unity 组件；只对 `TapBase` 派生物件调用 `ApplyFixedSoflan(...)`。
- `Assets/Scripts/Notes/NoteDrop.cs`：通用 Soflan timing 计算。
- `Assets/Scripts/Notes/TapBase.cs`：Tap / Star 系 Soflan 与 FixedSoflan 核心移动、缩放算法。
- `Assets/Scripts/Notes/StarDrop.cs`：Star Soflan 分支，以及不受 SoflanTiming 影响的 slide body 激活条件。
- `Assets/Scripts/Notes/HoldDrop.cs`：Hold 的 Soflan head / tail 显示。
- `Assets/Scripts/Notes/TouchDrop.cs`：Touch 的 Soflan 风扇位移分支。
- `Assets/Scripts/Notes/TouchHoldDrop.cs`：TouchHold 的 Soflan 风扇位移和 mask 填充分支。
- `Assets/Scripts/Notes/SlideDrop.cs`、`WifiDrop.cs`、`EachLineDrop.cs`：当前不使用 Soflan timing 的物件实现。
