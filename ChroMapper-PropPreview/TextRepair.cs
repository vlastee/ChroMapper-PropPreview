using System.Linq;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// TextMeshPro font assets from a Beat Saber bundle (TextMeshPro 3, Unity 2021) load in
    /// ChroMapper's Unity 6 with their atlas and glyphs but without their default material, and
    /// TextMeshPro then throws on every text that uses them (nothing is drawn). The text itself
    /// still has that material (its shared material), so it is given back to the font, and the
    /// material gets the font's atlas again. The bundle's TextMeshPro shader expects TextMeshPro 3's
    /// vertex data (glyphs come out as solid boxes), so ChroMapper's own copy replaces it.
    /// </summary>
    public static class TextRepair
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        private static readonly System.Collections.Generic.Dictionary<string, Shader?> Own =
            new System.Collections.Generic.Dictionary<string, Shader?>();

        /// <summary>ChroMapper's built-in shader with this TextMeshPro shader's name, if any.</summary>
        private static Shader? OwnShader(Shader? bundled)
        {
            if (bundled == null || !bundled.name.StartsWith("TextMeshPro/")) return null;
            if (!Own.TryGetValue(bundled.name, out var own))
            {
                // Loaded with ChroMapper (not from an asset bundle): its texts already use it.
                own = Resources.FindObjectsOfTypeAll<Shader>()
                    .FirstOrDefault(x => x.name == bundled.name && x != bundled && x.isSupported
                        && Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>().Any(t => t.fontSharedMaterial != null && t.fontSharedMaterial.shader == x));
                Own[bundled.name] = own;
            }
            return own;
        }

        public static void Repair(GameObject go)
        {
            foreach (var text in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                var font = text.font;
                var material = text.fontSharedMaterial;
                if (font == null || material == null) continue;
                var changed = false;
                if (font.material == null)
                {
                    font.material = material;
                    changed = true;
                }
                // The material's link to the atlas can be lost too (glyphs then draw as boxes).
                if (font.atlasTexture != null && material.HasProperty(MainTex) && material.GetTexture(MainTex) != font.atlasTexture)
                {
                    material.SetTexture(MainTex, font.atlasTexture);
                    changed = true;
                }
                // The bundle's TextMeshPro shaders read the old vertex layout; ChroMapper's own
                // shader of the same name matches the TextMeshPro that builds the text here.
                var own = OwnShader(material.shader);
                if (own != null && material.shader != own)
                {
                    material.shader = own;
                    changed = true;
                }
                if (changed) text.SetAllDirty();
            }
        }
    }
}
