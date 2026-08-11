# Gaussian Splatting for Unity (2022 LTS + URP fork)

[**中文版 README**](/readme.zh-CN.md) | **English**

This is a fork of [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting), a real-time
[3D Gaussian Splatting](https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/) viewer for Unity, focused on
**Unity 2022 LTS + URP 14**. For the full upstream documentation (usage, asset creation from PLY/SPZ, platform
requirements, performance numbers and licensing), see the [upstream README](https://github.com/aras-p/UnityGaussianSplatting).

## What's different in this fork

- **Unity 2022 LTS + URP 14 support**: `GaussianSplatURPFeature` was rewritten from the Unity 6 RenderGraph API back to
  the classic `ScriptableRenderPass` API, so URP no longer requires Unity 6.
- **Performance optimizations** (see [Performance Optimizations](/docs/performance-optimizations.md); all switchable at
  runtime for A/B testing):
  - Motion gating: skip per-frame GPU sorting and view-data recompute when neither camera nor splat state changed.
  - Per-splat frustum culling with GPU visibility compaction: only visible splats are sorted, view-calculated and drawn.
  - URP direct rendering: unified premultiplied-linear blending without the full-screen intermediate splat RT.
  - Optional splat depth write (`m_EnableDepthWrite`) for correct depth interaction with transparent objects.

## Quick start

- URP sample: `projects/GaussianExample-URP` (Unity 2022 LTS + URP 14); make sure `GaussianSplatURPFeature` is added
  to the URP renderer asset.
- BiRP sample: `projects/GaussianExample` (same as upstream).
- Asset creation and component usage: see the [upstream README](https://github.com/aras-p/UnityGaussianSplatting).

## Docs

- [Performance Optimizations](/docs/performance-optimizations.md)
- [Render Pipeline Integration](/docs/render-pipeline-integration.md)
- [Editing Splats](/docs/splat-editing.md)

## License

MIT, same as upstream. Third-party code and training-model licensing notes are in the upstream repository.
