# 3DGS VR Fisheye Prototype

Unity prototype for viewing Gaussian splats in VR-style scenes, with fisheye
projection controls and support for both PLY/SPZ and SOG Gaussian Splat assets.

## Requirements

- Unity `6000.0.58f1`
- Visual Studio 2022, with the Unity/game development workload installed
- Windows is recommended for this project setup

The required Unity packages are embedded in this repository. You do not need to
clone `UnityGaussianSplatting` separately.

## Clone And Open

```powershell
git clone https://github.com/Abelanova/3dgs-vr-fisheye-prototype.git
```

Open the cloned folder in Unity Hub with Unity `6000.0.58f1`. The first import
can take a while because Unity needs to restore packages and compile the embedded
Gaussian Splatting and SOG import code.

After Unity opens, load this scene:

```text
Assets/Scenes/XRSimulatorTemplate.unity
```

The scene contains:

- `PutAssetsHere`: the empty object where generated Gaussian Splat assets should
  be assigned.
- `Projection Control Panel`: runtime controls for fisheye and field of view.
- `XR Simulator`: the editor simulator used to preview movement without a headset.

## Convert A PLY Or SPZ File

1. In Unity, open:

   ```text
   Tools > Gaussian Splats > Create GaussianSplatAsset
   ```

2. Set `Input PLY/SPZ File` to your `.ply` or `.spz` file.
3. Choose an output folder inside `Assets`, for example:

   ```text
   Assets/GaussianAssets
   ```

4. Click `Create Asset`.
5. Use the generated `.asset` file as the render asset.

Unity will also create supporting `.bytes` files next to the asset. These files
are generated data and should not be committed to Git.

## Convert A SOG File

1. Drag the `.sog` file into the Unity Project window, somewhere under `Assets`.
2. Wait for Unity to finish importing.
3. The importer creates a generated asset named like:

   ```text
   YourFile_sog.asset
   ```

4. Use `YourFile_sog.asset` as the render asset.

The original `.sog` file is only the source package. The generated `_sog.asset`
is the file you assign to the renderer. Large SOG files can require a lot of RAM
and VRAM during import; if Unity runs out of memory, use a smaller or lower-detail
SOG file.

## Show The Asset In The Scene

1. Open `Assets/Scenes/XRSimulatorTemplate.unity`.
2. In the Hierarchy, select `PutAssetsHere`.
3. In the Inspector, find the `Gaussian Splat Renderer` component.
4. Drag the generated `.asset` file into the renderer's `Asset` field.
5. Press Play.

If the splat is too large, too small, or off-center, adjust the transform of
`PutAssetsHere` in the scene.

## Runtime Controls

In Play Mode, use the `Projection Control Panel` to adjust:

- FOV: changes the fisheye projection field of view.
- Fisheye: blends between normal projection and stronger fisheye projection.

The XR Simulator can be used in the editor to move around the scene. Hold `Shift`
while moving to use the faster movement speed.

## Peripheral Target Inspection Task

The branch includes a lightweight interaction flow for the demo video. It is
injected at runtime, so no scene object or Gaussian asset needs to be edited.

1. Enter Play Mode in `XRSimulatorTemplate` or `DesktopPreview`.
2. Press `T` to start the task.
3. Find and activate three colored peripheral markers.
4. Press `Tab` (or the gamepad left shoulder) to switch between the 60-degree
   perspective baseline and the 180-degree fisheye inspection lens.
5. Aim at a marker and use `Space`, left mouse click, or the right-controller
   trigger to activate it.

Additional controls:

- `R`: reset all markers and return to the perspective baseline.
- `Esc`: end the task and restore the projection values that were active before
  the task started.
- `T`: start or stop the task from the bootstrap overlay.

The markers use the same fisheye center mapping returned by
`GaussianSplatRenderer.GetFisheyeShaderParams`. Their graphics are placed at a
comfortable HUD depth, allowing the interaction cue to remain usable in stereo
VR without pretending that ordinary Unity meshes pass through the Gaussian
covariance shader.

## What Is Committed

This repository commits the Unity project, the clean scene, and the embedded
dependency packages:

- `Packages/org.nesnausk.gaussian-splatting`
- `Packages/com.ollihuttunen.sog-gaussian-splatting`

Do not commit imported splat source files or generated splat data:

- `.ply`
- `.sog`
- generated `.bytes`
- generated `_sog.asset`
- `Assets/GaussianAssets`

Those files can be large and are intentionally ignored by Git.
