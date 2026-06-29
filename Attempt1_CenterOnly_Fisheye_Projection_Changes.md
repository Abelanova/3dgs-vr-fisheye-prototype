# Attempt 1: Center-Only Fisheye Projection Changes

Base commit used for this attempt:

`21656055583577d523f984bb54b2dc1a1ba4e803`

`Adjust FOV and fisheye visibility coverage`

## Purpose

This branch keeps the early center-only fisheye prototype usable for PPT/video capture. The rendering method is still the old Attempt 1 path; the changes below are mainly scene, UI, recorder, and interaction support so the prototype can be recorded cleanly and repeated on another historical version.

## Changes Made

### Normal game view instead of square crop

Files:

- `Assets/Scripts/CameraFovController.cs`
- `Assets/Scenes/XRSimulatorTemplate.unity`
- `Assets/Editor/XrSimulatorPreviewSceneSetup.cs`

Changes:

- Disabled the editor-only square Game View crop by default.
- Restored the camera viewport rect to full screen: `x=0`, `y=0`, `width=1`, `height=1`.
- Updated the scene generation script so regenerated preview scenes also avoid square cropping.

### Stable world-space UI panel

File:

- `Assets/Scripts/FixedProjectionPanelPose.cs`

Changes:

- Kept the projection control panel as a world-space panel, not a screen-space overlay.
- Anchored it by camera viewport position with `Camera.ViewportToWorldPoint`.
- Compensated panel scale against FOV changes so recorded size and screen position remain stable while FOV/fisheye changes.
- Current anchor is `ViewportPosition = (0.26, 0.78)`.

### UI sliders stay synchronized with keyboard changes

File:

- `Assets/Scripts/ProjectionControlPanel.cs`

Change:

- The control panel now refreshes slider values from the live FOV/fisheye targets during `Update`, so keyboard-driven changes are reflected in the UI sliders immediately.

### Keyboard controls

File:

- `Assets/Scripts/ProjectionKeyboardControls.cs`

Controls:

- `,` / `.`: decrease/increase fisheye only.
- `-` / `=`: decrease/increase FOV only.
- `[` / `]`: decrease/increase FOV and fisheye together.
- `Backspace`: reset FOV and fisheye.

Note:

- `Q` / `E` are intentionally left free for vertical movement controls.

### Hide XR controller rays for recording

Files:

- `Assets/Scenes/XRSimulatorTemplate.unity`
- `Assets/Editor/XrSimulatorPreviewSceneSetup.cs`

Changes:

- Disabled the left and right controller `XRInteractorLineVisual` components in the current scene.
- The scene generator also disables `XRInteractorLineVisual` and `LineRenderer` on generated controllers.
- UI interaction is kept; only the visible controller ray line is hidden.

### Recorder package

Files:

- `Packages/manifest.json`
- `Packages/packages-lock.json`

Changes:

- Added Unity Recorder package: `com.unity.recorder@5.1.6`.
- Recorder window path in Unity:

`Window > General > Recorder > Recorder Window`

Recording notes:

- If using MP4/H.264, output width and height must be even numbers.
- To match the Game View, use Game View as the Recorder source or set a custom even resolution matching the intended aspect ratio.

## Files Changed In This Branch

- `Attempt1_CenterOnly_Fisheye_Projection_Changes.md`
- `Assets/Editor/XrSimulatorPreviewSceneSetup.cs`
- `Assets/Scenes/XRSimulatorTemplate.unity`
- `Assets/Scripts/CameraFovController.cs`
- `Assets/Scripts/FixedProjectionPanelPose.cs`
- `Assets/Scripts/ProjectionControlPanel.cs`
- `Assets/Scripts/ProjectionKeyboardControls.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
