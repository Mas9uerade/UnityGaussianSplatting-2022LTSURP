# 性能优化方案与进度

本文档记录本仓库(GaussianExample-URP 适配 Unity 2022 LTS + URP 14)的性能优化项、实现状态与验证方法。

## 背景

- 2022 LTS + URP 14 适配已完成(提交 `9332c65`,URPFeature 重写为经典 `ScriptableRenderPass`)。
- 每帧 GPU 工作分为四段:距离计算(`CSCalcDistances`)、基数排序(`GpuSorting`,13 个 dispatch)、视图数据计算(`CSCalcViewData`)、绘制与合成。
- 优化前的核心问题:无剔除(全量处理)、无时序缓存(相机不动也全算)、中间 RT 全分辨率带宽。

## 已实现

### P0-1/2 相机运动门控 + 自适应排序(提交 `18c6768`)

- 相机 `worldToCameraMatrix` / `projectionMatrix`、屏幕尺寸(含 XR eye 尺寸)、对象 `localToWorldMatrix`、`SplatScale` / `OpacityScale` / `SHOrder` / `SHOnly` / 渲染模式、编辑版本、cutout 状态全部未变化时,跳过排序与视图数据计算,直接复用上帧 GPU 缓冲。
- 编辑操作通过 `editModified` 自动递增版本号触发重算;资产切换/组件重启用 `m_FirstFrame` 强制首帧全量计算。
- `m_SortNthFrame` 语义保留:状态变化时无条件排序;`>1` 时仍按固定帧间隔额外排序。
- 收益:静止场景下排序与视图计算归零;镜头转动时退化为原行为。

### P0-3a Kernel 组大小缓存(提交 `18c6768`)

- `GetKernelThreadGroupSizes` 每帧每对象查询多次 → 只查询一次并缓存。
- 收益:CPU 帧时微降。

### P0-3b Cutout 脏标记上传(提交 `18c6768`)

- cutout 数据仅在内容变化时上传(原来每帧 `Allocator.Temp` + 全量 `SetData`),返回值同时作为门控的失效信号。
- 收益:有 cutout 时减少每帧 CPU 分配与上传;无 cutout 时本就走零开销路径。

### P1-4 逐 splat 视锥剔除 + 可见性压缩(提交 `8d0f678`)

- 新增 `CSInitVisibleList` / `CSCalcVisibility` / `CSWriteVisibleArgs` 三个 kernel:
  - 逐 splat 判断:相机后方(`w <= 0`)、屏幕外(±1.5 × clip 保守边距,避免边缘弹跳)、已删除、被 cutout 裁掉;
  - 可见 splat 通过 `InterlockedAdd` 压入紧凑可见表 `_VisibleSplatIndices`;
  - 可见数写入 `DrawProceduralIndirect` 参数(5 个 uint:`indexCountPerInstance`, `instanceCount`, `startIndexLocation`, `baseVertexLocation`, `startInstanceLocation`)。
- `CSCalcDistances` / `CSCalcViewData` 改为只遍历可见表;排序 payload 从"全量索引"改为"可见索引表",排序后即绘制顺序缓冲。
- 排序内核改为 GPU 动态数量:每个线程块从 `_VisibleSplatCount` 读实际数量并钳制分区边界(`min((gid+1)*PART_SIZE, numKeys)`),Downsweep 按"最后一个有效块"判断全量/部分散列,超出块直接返回。C# 侧仍按全量 N 派发线程块,排序实际工作量与可见数成正比。
- 主 splat 渲染改用 `DrawProceduralIndirect`;顶点 shader 改为 6 顺序顶点生成四边形(与旧索引缓冲路径结果一致)。
- DebugBoxes 仍用全量恒等 `_OrderBuffer` 绘制全部盒子;DebugPoints 不依赖视图数据,保持全量绘制。
- 收益:可见率 40–70% 的典型场景,排序、视图计算、绘制三阶段等比下降。

### P1-5 URP 直接渲染,去掉中间 RT

- 混合数学统一为**预乘线性**(`Blend One OneMinusSrcAlpha`,fragment 输出 `GammaToLinearSpace(color) * alpha`):
  - URP 直绘:splat 直接画进相机目标(带场景深度测试),不再需要全屏 `R16G16B16A16_SFloat` 中间 RT、clear 与全屏 blit;
  - BiRP/HDRP 两段式保持不变(中间 RT + 合成),合成 shader 简化为预乘 over(`float4(col.rgb, col.a)`),数学自洽。
- URP 侧开关:`GaussianSplatURPFeature.m_EnableDirectRendering`(默认开;该开关在 URP 渲染器资产的 Feature 面板上,是相机级/全局的,无法像前几项一样做逐对象开关)。关闭时回退到两段式 RT + blit,便于 A/B。
- 收益:4K 下每帧省全屏 clear + RT 写入 + 读取 + 混合带宽(约 100–150 MB/帧)。
- 注意:叠加空间从"gamma 累加后统一线性化"改为"逐 splat 线性化后混合"(更符合物理),与天空盒/外部物体的融合表现可能有细微差异,需重点回归。

### 运行时开关(提交 `b2c5eb8`)

每个 `GaussianSplatRenderer` 组件提供 4 个运行时开关,默认全开;关闭即回到对应修改前的行为。

| 开关 | 对应优化项 | 关闭时的行为 |
|---|---|---|
| `m_EnableMotionGating` | P0-1/2 | 每帧都排序(按 `m_SortNthFrame` 节奏)并计算视图数据 |
| `m_EnableFrustumCulling` | P1-4 | 全量排序、全量视图计算、`DrawProcedural` 全量绘制 |
| `m_EnableKernelSizeCache` | P0-3a | 每帧重新查询 kernel 组大小 |
| `m_EnableCutoutCaching` | P0-3b | 每帧全量上传 cutout 数据 |

开关用 uniform 分支(`_CullingEnabled`)实现:同一 dispatch 内所有线程路径一致,无发散,无实质性能开销。开关切换(尤其剔除开→关/关→开)会触发一帧重算,之后回到正常状态。

### 与透明物体的深度交互(可选,`m_EnableDepthWrite`)

- 默认 splat `ZWrite Off` 且先于所有透明物体渲染,因此与透明物体没有深度联系(透明物体永远盖在 splat 上,即使它实际在 splat 后面)。
- 开启 `GaussianSplatRenderer.m_EnableDepthWrite` 后,主 splat shader 通过材质属性 `ZWrite [_ZWrite]` 写入深度。由于 splat 按 back-to-front 排序,最终深度缓冲等于 splat 云每个像素的"最近 splat 前表面",之后渲染的透明物体会被正确遮挡/正确遮挡 splat。
- 注意:共面/等深 splat 可能出现 z-fighting 或近 splat 四边形边缘的深度伪影;`m_SortNthFrame > 1` 时排序滞后也可能造成少量缺失;建议仅在需要与透明物体交互的场景开启。

## 未实现

| 项 | 内容 | 预期收益 | 备注 |
|---|---|---|---|
| P1-6 | 半分辨率 splat RT 质量档 | RT 带宽/填充 4 倍下降 | 画面略糊;几分钟工作量,低风险 |
| P1-7 | SH 阶数成本控制(按距离自适应) | 视图计算进一步减负 | 需视觉验证,低风险 |
| P2-8 | 多 splat 对象合并排序/绘制 | 多对象场景免去重复排序 | 需统一 key(renderOrder 高位 + 距离)与单次 sort/draw |
| P2-9 | ViewData 压缩(40 B → 32 B) | 视图计算写带宽与 vertex 读取降约 20% | half 精度需验证 |
| P2-10 | 距离 LOD(利用已有 chunk 结构) | 远距离低密度/低阶表示 | 工作量较大 |
| P2-11 | Async compute 重叠排序 | 排序与不透明渲染并行 | 2022 LTS + URP 14 经典 pass 内无法干净切异步队列,建议放弃 |

注:方案中曾提过的 chunk 级粗剔除未单独实现——它已被更细的逐 splat 剔除覆盖。

## 验证方法

- 现有 Profiler GPU marker:`GaussianSplat.Sort`、`GaussianSplat.CalcView`、`GaussianSplat.Draw`、`GaussianSplat.Compose`。
- 静止场景:Sort/CalcView 应为空;相机旋转时随可见比例下降;Frame Debugger 中主 splat draw 的 instance 数应明显小于总 splat 数。
- 回归项:相机旋转时屏幕边缘无 splat 弹出/消失跳变(边距 1.5 可调);编辑(移动/旋转/缩放/删除)、cutout 移动、SH/透明度档位切换后画面立即刷新;DebugBoxes / DebugPoints / DebugChunkBounds 三种调试模式正常。
- 已知取舍:等距(共面)的少量 splat 排序顺序可能因压缩顺序不定而在帧间微变(几乎不可见);"过小 splat"剔除未做(会与现有 1px 低通滤波冲突、产生弹跳)。

## 建议实施顺序

1. 提交运行时开关(当前在工作区未提交);
2. 按实际瓶颈选择:填充率/带宽受限 → P1-5 或 P1-6;多对象场景 → P2-8;大点云内存带宽 → P2-9;
3. P2-11 在 2022 LTS + URP 14 上不建议投入。
