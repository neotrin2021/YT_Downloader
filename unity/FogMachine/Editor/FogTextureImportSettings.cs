using UnityEditor;

namespace FogMachine.EditorTools
{
    /// <summary>
    /// Auto-applies the correct import settings to baked six-way fog sheets
    /// (anything named FogSixWay_*). They are lighting data, not color:
    /// sRGB off, BC7 (soft gradients band badly under BC1), clamped, no
    /// alpha-is-transparency.
    /// </summary>
    public class FogTextureImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!name.StartsWith("FogSixWay_")) return;

            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC7
        }
    }
}
