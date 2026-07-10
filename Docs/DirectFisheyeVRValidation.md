# Direct Covariance Fisheye VR Validation

This project branch validates the shared cyclopean direct covariance fisheye path, not the cubemap path.

## Scene Setup

1. Open `Assets/Scenes/XRSimulatorTemplate.unity`.
2. Run `Tools > VR Preview > Configure Direct Fisheye VR Validation`.
3. Confirm `VrHighQualityFisheye` on the main camera is disabled.
4. Confirm the Gaussian splat renderer uses `Sort Nth Frame = 1`.
5. Enter Play Mode.

## Mock HMD / OpenXR Runtime

Use one of these stereo sources:

- A real OpenXR headset.
- OpenXR Mock Runtime from `Project Settings > XR Plug-in Management > OpenXR > Features` for editor-only validation.
- Unity Mock HMD if installed in the project.

This project enables the OpenXR Mock Runtime for the Standalone editor target so the automated capture can drive a
repeatable HMD-style stereo pose without adding another package.

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

## Soft-Saturation Baseline

Keep the first comparison fixed to one configuration:

- FOV `120`, fisheye `0.20`.
- Stereo Scale `0.25`.
- Max Per-Eye Shift `0.004` NDC.
- Radial Compression `2.0`.
- Convergence `2.0 m`.

The preserved hard-clamp result is named `baseline_hardclamp_scale025`. The current soft-saturation result is named `softclamp_scale025`. Soft saturation uses:

`normalized = stereoScale * weightedRawDisparity / max(maxShift, 1e-5)`

`safeDisparity = maxShift * normalized / (1 + abs(normalized))`

Do not test wider FOV or change another stereo parameter until dense flow confirms that the soft-saturation result does not increase vertical disparity or warp residual.

After that comparison passes, keep every other parameter fixed and increase only Stereo Scale:

- Stereo Scale `0.25 -> 0.35 -> 0.50`.

The current engineering default is Stereo Scale `0.35`. Dynamic validation captures five
fixed poses named `dynamic_scale035_center`, `dynamic_scale035_yaw_left10`,
`dynamic_scale035_yaw_right10`, `dynamic_scale035_translate_left05m`, and
`dynamic_scale035_translate_right05m`. Every pose keeps FOV `120`, fisheye `0.20`,
Radial Compression `2.0`, Max Per-Eye Shift `0.004` NDC, and Convergence `2.0 m`.

## XR Controller Mapping

- Right thumbstick: move forward/backward and strafe relative to the HMD heading.
- Right A/B: move the XR Origin up/down.
- Left thumbstick X: decrease/increase fisheye strength.
- Left thumbstick Y: decrease/increase FOV.
- Left thumbstick click: reset to FOV `120` and fisheye `0.20`.

Trigger and grip remain available for UI interaction. Stereo Scale remains `0.35`.

## Fisheye 0.70 Fusion Check

The fixed center-pose HMD Mock check at FOV `120`, fisheye `0.70`, and Stereo Scale
`0.35` remained predominantly horizontal. Using the same OpenCV DIS flow settings
for both captures, fisheye `0.20 -> 0.70` changed global `|dx|` P95 from
`4.658 -> 4.679 px`, global `|dy|` P95 from `0.400 -> 0.382 px`, and normalized
warp residual from `0.00375 -> 0.00343`. This supports fusion at the tested center
pose, but does not remove the separate high-fisheye covariance and popping risks.

Watch the edge of the view for splats that smear into long lines, flip orientation, or cover a large part of the eye. The overlay's `Fisheye stretch probe` is a projection-level warning; it does not replace visual inspection of the actual splat asset.

## Shared Fisheye Stereo Model

Each Gaussian is projected once from the cyclopean camera for its fisheye center and covariance. The two virtual eyes are only used to estimate a geometry-informed horizontal disparity through the same fisheye function. The final eye positions share `y`, footprint axes, size, orientation, and distortion; only `x` receives the convergence-corrected disparity.

Before clamping, the disparity is attenuated toward the fisheye edge:

`radialWeight = 1 / (1 + radialCompression * dot(centerCyclopean, centerCyclopean))`

This keeps central depth while suppressing the large, spatially varying edge disparity that is difficult to fuse. The first dense-flow validation target is:

- Center horizontal disparity: `0.5-1.5 px`.
- Left/right edge P95: no more than `2-3 px`.
- Near-ground P95: no more than `3-4 px`.
- Global vertical disparity P95: below `0.25 px`.
- Foreground vertical disparity P95: below `0.5 px`.

## Stereo Diagnostic Capture Outputs

Run `Tools > VR Preview > Capture Direct Fisheye VR Validation`.

For each fixed head pose and projection case, the capture writes:

- `*_stereo_pair.png`: left and right eye images side by side.
- `*_stereo_pair_left.png` and `*_stereo_pair_right.png`: separate per-eye images from the same head pose.
- `*_stereo_pair_overlay_50.png`: 50% alpha left/right overlay in the same per-eye coordinate frame.
- `*_stereo_pair_anaglyph_red_cyan.png`: left eye in red, right eye in cyan.
- `*_stereo_pair_feature_disparity.csv` and `.txt`: fixed 3D feature measurements with `delta_x = xL - xR` and `delta_y = yL - yR`.

The measured probes are scene-bounds based: center, left edge, right edge, nearest ground-side corner, and farthest target corner. Pixel coordinates use the top-left of each per-eye image as the origin.

## Failure Interpretation

- Stereo OFF or IPD near zero: the editor is not producing a headset-like dual-eye render.
- Pose static while using the simulator: the simulated HMD pose is not reaching the camera.
- Left/right ghosting only in Single Pass: inspect texture array depth slices and stereo shader macros.
- Ghosting in both modes: inspect per-eye view/projection matrices and per-eye splat sorting.
- Edge smearing at high FOV/fisheye: inspect the direct covariance Jacobian and consider stricter culling or adaptive subdivision near the fisheye limit.
