// SOGImporter.cs
// ScriptedImporter for .sog files.
// Decodes the SOG file and writes binary .bytes buffers next to it.
// The actual GaussianSplatAsset (.asset) is created by SOGPostprocessor
// after the .bytes files are imported — this follows Aras's own workflow
// where GaussianSplatAsset is a standalone .asset file referencing TextAssets.
//
// What this importer produces for the .sog itself: a lightweight marker object
// (GaussianSplatAsset without TextAsset refs — not valid for rendering).
// Tell users to assign the generated .asset file to GaussianSplatRenderer.

using System;
using System.IO;
using GaussianSplatting.Runtime;
using GaussianSplatting.SOG;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    [ScriptedImporter(version: 5, ext: "sog")]
    public class SOGImporter : ScriptedImporter
    {
        internal const string kOutputSuffix = "_sog";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string sogPath  = ctx.assetPath;
            string baseName = Path.GetFileNameWithoutExtension(sogPath);
            string dir      = Path.GetDirectoryName(sogPath);

            string outputBase = Path.Combine(dir, $"{baseName}{kOutputSuffix}");
            string pathPos   = $"{outputBase}_pos.bytes";
            string pathOther = $"{outputBase}_oth.bytes";
            string pathColor = $"{outputBase}_col.bytes";
            string pathSH    = $"{outputBase}_shs.bytes";

            bool bytesExist =
                File.Exists(pathPos)   &&
                File.Exists(pathOther) &&
                File.Exists(pathColor) &&
                File.Exists(pathSH);

            Vector3 boundsMin = Vector3.zero, boundsMax = Vector3.one;
            int splatCount = 0;

            if (!bytesExist)
            {
                // First import: decode SOG and write the four binary buffers.
                SOGRawData rawData;
                try
                {
                    rawData = SOGParser.ParseFromFile(sogPath, SOGWebPDecoder.Decode);
                }
                catch (Exception ex)
                {
                    ctx.LogImportError($"SOG: Failed to parse '{sogPath}': {ex.Message}");
                    return;
                }

                try
                {
                    SOGConverter.CreateAsset(rawData, pathPos, pathOther, pathColor, pathSH);
                }
                catch (Exception ex)
                {
                    ctx.LogImportError($"SOG: Buffer write failed: {ex.Message}");
                    return;
                }

                boundsMin  = rawData.boundsMin;
                boundsMax  = rawData.boundsMax;
                splatCount = rawData.count;

                Debug.Log($"[SOGImporter] Decoded '{baseName}' ({splatCount:N0} splats). " +
                    "SOGPostprocessor will create the .asset once .bytes are imported.");
            }

            // Create a lightweight marker for the .sog file itself.
            // The real GaussianSplatAsset (usable by GaussianSplatRenderer) is created
            // separately as baseName_sog.asset by SOGPostprocessor.
            var marker = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            if (splatCount > 0)
            {
                marker.Initialize(
                    splatCount,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.ColorFormat.Float32x4,
                    GaussianSplatAsset.SHFormat.Float32,
                    boundsMin, boundsMax, null);
            }
            marker.name = baseName;
            ctx.AddObjectToAsset("GaussianSplatAsset", marker);
            ctx.SetMainObject(marker);
        }
    }
}
