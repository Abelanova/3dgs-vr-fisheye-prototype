// SOGPostprocessor.cs
// Listens for newly-imported _pos.bytes files that belong to a .sog asset.
// When all four companion .bytes files are imported, creates a standalone
// GaussianSplatAsset (.asset) next to the .sog — the same way Aras's own
// GaussianSplatAssetCreator produces assets.  Users assign this .asset to
// GaussianSplatRenderer, not the .sog file itself.

using System;
using System.IO;
using GaussianSplatting.Runtime;
using GaussianSplatting.SOG;
using UnityEditor;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    public class SOGPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                // Only react to _pos.bytes — one trigger per set of four buffers
                if (!TryGetSOGBasePath(path, out string basePath))
                    continue;

                if (!basePath.EndsWith(SOGImporter.kOutputSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string sourceBasePath = basePath.Substring(0, basePath.Length - SOGImporter.kOutputSuffix.Length);
                string sogPath   = sourceBasePath + ".sog";
                string assetPath = basePath + ".asset";

                if (!File.Exists(sogPath))
                    continue;

                string pathPos   = basePath + "_pos.bytes";
                string pathOther = basePath + "_oth.bytes";
                string pathColor = basePath + "_col.bytes";
                string pathSH    = basePath + "_shs.bytes";

                if (!File.Exists(pathPos) || !File.Exists(pathOther) || !File.Exists(pathColor) || !File.Exists(pathSH))
                    continue;

                // Load the four fixed companion TextAssets. The current callback
                // path can be any one of them depending on Unity import order.
                var taPos   = AssetDatabase.LoadAssetAtPath<TextAsset>(pathPos);
                var taOther = AssetDatabase.LoadAssetAtPath<TextAsset>(pathOther);
                var taColor = AssetDatabase.LoadAssetAtPath<TextAsset>(pathColor);
                var taSH    = AssetDatabase.LoadAssetAtPath<TextAsset>(pathSH);

                if (taPos == null || taOther == null || taColor == null || taSH == null)
                {
                    Debug.LogWarning(
                        $"[SOGPostprocessor] Could not load TextAssets for '{basePath}'. " +
                        "Try reimporting the .sog file.");
                    continue;
                }

                // Re-parse the .sog to obtain bounds and splat count.
                // The WebP images are already in the OS file cache so this is fast.
                SOGRawData rawData;
                try
                {
                    rawData = SOGParser.ParseFromFile(sogPath, SOGWebPDecoder.Decode);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[SOGPostprocessor] Re-parse failed for '{sogPath}': {ex.Message}");
                    continue;
                }

                // Create a fully-wired GaussianSplatAsset as a standalone .asset file.
                // This is the same approach Aras's GaussianSplatAssetCreator uses.
                var asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                asset.Initialize(
                    rawData.count,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.ColorFormat.Float32x4,
                    GaussianSplatAsset.SHFormat.Float32,
                    rawData.boundsMin, rawData.boundsMax, null);
                asset.SetAssetFiles(null, taPos, taOther, taColor, taSH);
                asset.name = Path.GetFileNameWithoutExtension(assetPath);

                var existing = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                else
                {
                    EditorUtility.CopySerialized(asset, existing);
                    EditorUtility.SetDirty(existing);
                    asset = existing;
                }
                AssetDatabase.SaveAssets();

                Debug.Log(
                    $"[SOGPostprocessor] Created '{assetPath}' ({rawData.count:N0} splats). " +
                    "Assign this .asset to GaussianSplatRenderer.");

                // A Float32 SH TextAsset can exceed 1 GB. When several SOG files
                // are reimported in one refresh, keeping each buffer loaded until
                // the callback ends exhausts editor memory. The serialized asset
                // references are already saved, so release these objects before
                // processing the next SOG; Unity reloads them on demand.
                rawData = null;
                Resources.UnloadAsset(taPos);
                Resources.UnloadAsset(taOther);
                Resources.UnloadAsset(taColor);
                Resources.UnloadAsset(taSH);
                Resources.UnloadAsset(asset);
                GC.Collect();
            }
        }

        static bool TryGetSOGBasePath(string path, out string basePath)
        {
            string[] suffixes =
            {
                "_pos.bytes",
                "_oth.bytes",
                "_col.bytes",
                "_shs.bytes",
            };

            foreach (string suffix in suffixes)
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    basePath = path.Substring(0, path.Length - suffix.Length);
                    return true;
                }
            }

            basePath = null;
            return false;
        }
    }
}
