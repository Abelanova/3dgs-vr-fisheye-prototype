#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the experimental ChatGPT per-eye nonlinear projection patch to the
/// embedded Gaussian-splatting package. The patch is idempotent and can also be
/// re-applied manually from Tools/3DGS.
///
/// This keeps the branch easy to compare against the original package while
/// making the actual H