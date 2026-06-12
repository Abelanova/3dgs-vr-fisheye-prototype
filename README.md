# 3DGS Fisheye Projection Unity Prototype

Unity prototype for viewing Gaussian splats with experimental fisheye projection,
camera FOV control, and near-camera fade for VR-style browsing.

## External Checkouts

This repository stores the Unity project. The Gaussian Splatting package is kept
as an external checkout next to the project root:

```powershell
git clone https://github.com/aras-p/UnityGaussianSplatting.git UnityGaussianSplatting
git -C UnityGaussianSplatting checkout -b fisheye-vr-prototype
git -C UnityGaussianSplatting am ..\Patches\0001-UnityGaussianSplatting-fisheye-vr-comfort.patch
```

The Unity package manifest references:

```text
file:../UnityGaussianSplatting/package
```

Open `Assets/Scenes/VRPreview.unity` or recreate it with:

```text
Tools > VR Preview > Create 3DGS VR Preview Scene
```

