# 3DGS Fisheye Unity Roadmap

## Current Setup

- Unity project version: 6000.0.58f1.
- Render pipeline: URP 17.0.4.
- UnityGaussianSplatting source is cloned into `UnityGaussianSplatting/`.
- The project references the plugin as a local Unity package:
  - `org.nesnausk.gaussian-splatting`
  - `file:../UnityGaussianSplatting/package`
- `Assets/Settings/PC_Renderer.asset` has `GaussianSplatURPFeature` added.

## Immediate Unity Editor Checks

1. Open the project in Unity.
2. Let Package Manager resolve the local package.
3. Confirm there are no compile errors.
4. In `Assets/Settings/PC_Renderer.asset`, confirm `GaussianSplatURPFeature` appears in Renderer Features.
5. In Player Settings for Windows Standalone, set Graphics API to D3D12 or Vulkan. Do not use DX11.
6. URP requires Render Graph compatibility mode to be off.
7. Use `Tools -> Gaussian Splats -> Create GaussianSplatAsset` to convert a `.ply` or `.spz` file.
8. Add a GameObject with `GaussianSplatRenderer` and assign the generated asset.

## Important Code Locations

- Main render orchestration:
  - `UnityGaussianSplatting/package/Runtime/GaussianSplatRenderer.cs`
- URP render feature:
  - `UnityGaussianSplatting/package/Runtime/GaussianSplatURPFeature.cs`
- Per-splat view cache and distance sort compute kernels:
  - `UnityGaussianSplatting/package/Shaders/SplatUtilities.compute`
- Perspective covariance projection:
  - `UnityGaussianSplatting/package/Shaders/GaussianSplatting.hlsl`
- Final quad expansion shader:
  - `UnityGaussianSplatting/package/Shaders/RenderGaussianSplats.shader`

## Fisheye Implementation Plan

1. Add a `m_FisheyeStrength` property to `GaussianSplatRenderer`.
2. Pass `_FisheyeStrength` into `SplatUtilities.compute`.
3. Replace or blend the center projection in `CSCalcViewData`.
4. Add a fisheye-aware covariance Jacobian beside `CalcCovariance2D`.
5. Add radial sorting mode for fisheye:
   - normal mode sorts by view depth;
   - fisheye mode sorts by camera-to-splat distance.
6. Validate non-VR first.
7. Validate VR in multi-pass first.
8. Only then optimize/adjust for single-pass instanced VR.

## Notes

PlayCanvas' fisheye feature applies to Gaussian splats and infinite sky, not ordinary meshes or UI. For Unity, this means the first correct target is a splat-only VR scene. Regular Unity meshes will not geometrically match the fisheye splats unless they are rendered through a matching projection path too.
