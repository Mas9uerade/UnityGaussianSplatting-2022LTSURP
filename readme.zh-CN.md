# Unity Gaussian Splatting(2022 LTS + URP 分支)

Unity 3D 高斯泼溅(3D Gaussian Splatting)实时渲染实现的一个分支,聚焦 **Unity 2022 LTS + URP 14**。

本仓库是 [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)(基于 SIGGRAPH 2023 论文 [3D Gaussian Splatting](https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/))的 fork。上游的完整说明、使用方法、资产创建(PLY/SPZ)、平台要求、性能数据与许可证,请直接查看[上游 README](https://github.com/aras-p/UnityGaussianSplatting)。

## 本分支的改动

- **2022 LTS + URP 14 适配**:`GaussianSplatURPFeature` 从 Unity 6 的 RenderGraph API 重写回经典 `ScriptableRenderPass`,URP 不再要求 Unity 6。
- **性能优化**(详见[性能优化文档](/docs/performance-optimizations.md),均可在运行时开关做 A/B 对比):
  - 运动门控:相机与泼溅对象状态未变化时,跳过每帧 GPU 排序与视图数据重算;
  - 视锥剔除 + 可见性压缩:只有可见 splat 参与排序、视图计算与绘制;
  - URP 直绘:统一预乘线性混合,去掉全屏中间 RT 与合成 blit;
  - 深度写入开关(`m_EnableDepthWrite`):可选与透明物体建立正确的遮挡关系。

## 快速开始

- URP 工程:`projects/GaussianExample-URP`(Unity 2022 LTS + URP 14),并确保 URP 渲染器资产上添加了 `GaussianSplatURPFeature`;
- BiRP 工程:`projects/GaussianExample`(与上游一致);
- 资产创建、组件配置等使用细节见[上游 README](https://github.com/aras-p/UnityGaussianSplatting)。

## 文档

- [性能优化](/docs/performance-optimizations.md)
- [渲染管线集成](/docs/render-pipeline-integration.md)
- [泼溅编辑](/docs/splat-editing.md)

## 许可证

MIT,与上游一致;第三方代码与训练模型授权说明见上游仓库的 LICENSE 与 README。
