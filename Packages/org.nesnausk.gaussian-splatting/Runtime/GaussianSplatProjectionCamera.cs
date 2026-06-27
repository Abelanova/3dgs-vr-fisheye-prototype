// SPDX-License-Identifier: MIT
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    public enum GaussianSplatProjectionCameraRole
    {
        Capture,
        Output
    }

    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class GaussianSplatProjectionCamera : MonoBehaviour
    {
        public GaussianSplatProjectionCameraRole role;
        public Material compositeMaterial;
        public bool compositeActive;
    }
}
