# Direct Covariance Fisheye VR Validation

This project branch validates the direct covariance fisheye path, not the cubemap path.

## Scene Setup

1. Open `Assets/Scenes/XRSimulatorTemplate.unity`.
2. Run `Tools > VR Preview > Configure Direct Fisheye VR Validation`.
3. Confirm `VrHighQualityFisheye` on the main camera is disabled.
4. Confirm the Gaussian splat renderer uses `Sort Nth Frame = 1`.
5. Enter Play Mode.

## Mock HMD / OpenXR Runtime

Use one of these stereo sources:

- A real OpenXR headset.
- Unity Mock HMD if installed in the project.
- OpenXR Mock Runtime from `Project Settings > XR Plug-in Management > OpenXR > Features` for editor-only validation.

The XR Interaction Simulator only drives simulated HMD/controller poses. It does not prove stereo by itself. The validation overlay must show stereo ON and a non-zero IPD before a no-ghosting result means anything for a headset.

## What Should Pass

The `DirectFisheyeVrDiagnostics` overlay should report:

- `Stereo ON`.
- `IPD` greater than `10 mm`; typical headset values are around `60-70 mm`.
- `Eye matrix delta` greater than zero.
- `Pose static` returning near zero when the simulated HMD is moved or rotated.
- `Direct ON` when fisheye strength is greater than zero.

When these values pass, turn and move the simulated HMD. The scene should change with head rotation and head position, and the left/right images should not show two unrelated camera locations.

## Stereo Render Modes

Test both paths if the active XR provider exposes them:

- Multi Pass: each eye renders as a separate camera pass.
- Single Pass Instanced / texture array: both eyes render into slices of the same XR texture.

This branch forces per-eye splat sorting in both modes. If one mode fails and the other passes, inspect the URP render target slice/composite path first.

## High FOV / Fisheye Stress Matrix

Use the projection panel or controller bindings and test:

- FOV `90`, fisheye `0.0`.
- FOV `120`, fisheye `0.45`.
- FOV `150`, fisheye `0.70`.
- FOV `180+`, fisheye `0.85-1.0`.

Watch the edge of the view for splats that smear into long lines, flip orientation, or cover a large part of the eye. The overlay's `Fisheye stretch probe` is a projection-level warning; it does not replace visual inspection of the actual splat asset.

## Failure Interpretation

- Stereo OFF or IPD near zero: the editor is not producing a headset-like dual-eye render.
- Pose static while using the simulator: the simulated HMD pose is not reaching the camera.
- Left/right ghosting only in Single Pass: inspect texture array depth slices and stereo shader macros.
- Ghosting in both modes: inspect per-eye view/projection matrices and per-eye splat sorting.
- Edge smearing at high FOV/fisheye: inspect the direct covariance Jacobian and consider stricter culling or adaptive subdivision near the fisheye limit.
