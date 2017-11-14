using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZipSprites.Editor
{
    public class ZipSpritesEditor
    {
        [System.Serializable]
        private class PackerInfo
        {
            [System.Serializable]
            public class PackerInfoMeta
            {
                [System.Serializable]
                public class PackerInfoMetaSize
                {
                    [UsedImplicitly] public int w;
                    [UsedImplicitly] public int h;
                }

                [UsedImplicitly]
                public PackerInfoMetaSize size;
            }

            [System.Serializable]
            public class PackerInfoEntry
            {
                [UsedImplicitly] public string filename;

                [System.Serializable]
                public class PackerInfoRect
                {
                    [UsedImplicitly] public int x;
                    [UsedImplicitly] public int y;
                    [UsedImplicitly] public int w;
                    [UsedImplicitly] public int h;
                }

                [System.Serializable]
                public class PackerInfoSize
                {
                    [UsedImplicitly] public int w;
                    [UsedImplicitly] public int h;
                }

                [UsedImplicitly] public PackerInfoRect frame;
                [UsedImplicitly] public PackerInfoRect spriteSourceSize;
                [UsedImplicitly] public PackerInfoSize sourceSize;
            }

            [UsedImplicitly] public List<PackerInfoEntry> frames;
            [UsedImplicitly] public PackerInfoMeta meta;

            public Rect GetUvForSprite(string spritePath)
            {
                var spriteInfo = frames.Find(s => s.filename + ".png" == spritePath);
                if (spriteInfo == null)
                {
                    throw new System.Exception("sprite not found: " + spritePath);
                }

                var width = meta.size.w;
                var height = meta.size.h;

                var result = new Rect(
                    0f,
                    0f,
                    spriteInfo.frame.w / (float) width,
                    spriteInfo.frame.h / (float) height
                );
                result.center = new Vector2(
                    (spriteInfo.frame.x + 0.5f * spriteInfo.frame.w) / (float) width,
                    1f - ((spriteInfo.frame.y + 0.5f * spriteInfo.frame.h) / (float) height)
                );
                return result;
            }

            public Rect GetInsetsForSprite(string spritePath, float pixelsPerUnit)
            {
                var spriteInfo = frames.Find(s => s.filename + ".png" == spritePath);
                if (spriteInfo == null)
                {
                    throw new System.Exception("sprite not found: " + spritePath);
                }

                var result = new Rect(
                    spriteInfo.spriteSourceSize.x / pixelsPerUnit,
                    spriteInfo.spriteSourceSize.y / pixelsPerUnit,
                    spriteInfo.frame.w / pixelsPerUnit,
                    spriteInfo.frame.h / pixelsPerUnit
                );

                result.center = new Vector2(
                    0.5f * spriteInfo.spriteSourceSize.x / pixelsPerUnit
                    - 0.5f * (spriteInfo.sourceSize.w - spriteInfo.frame.w - spriteInfo.spriteSourceSize.x) /
                    pixelsPerUnit,
                    -0.5f * spriteInfo.spriteSourceSize.y / pixelsPerUnit
                    + 0.5f * (spriteInfo.sourceSize.h - spriteInfo.frame.h - spriteInfo.spriteSourceSize.y) /
                    pixelsPerUnit
                );

                return result;
            }
        }

        [MenuItem("ZipSprites/Pack")]
        public static void Pack()
        {
            var textureFormat = TextureFormat.DXT5;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                    textureFormat = TextureFormat.ETC2_RGBA8;
                    break;
                case BuildTarget.iOS:
                    textureFormat = TextureFormat.PVRTC_RGBA4;
                    break;
            }
            Pack(textureFormat);
        }

        public static void Pack(TextureFormat textureFormat)
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();

            var srs = new List<SpriteRenderer>();
            foreach (var root in roots)
            {
                srs.AddRange(root.GetComponentsInChildren<SpriteRenderer>());
            }
            srs.Sort((a, b) =>
            {
                {
                    var l = a.sortingLayerID.CompareTo(b.sortingLayerID);
                    if (l != 0) return l;
                }
                {
                    var l = a.sortingOrder.CompareTo(b.sortingOrder);
                    if (l != 0) return l;
                }
                return b.transform.position.z.CompareTo(a.transform.position.z);
            });

            Packer.RebuildAtlasCacheIfNeeded(EditorUserBuildSettings.activeBuildTarget, displayProgressBar: true);

            var paths = new List<string>();

            foreach (var spriteRenderer in srs)
            {
                var sprite = spriteRenderer.sprite;
                var path = AssetDatabase.GetAssetPath(sprite);

                if (sprite && !string.IsNullOrEmpty( path ))
                {
                    paths.Add(path);
                }
            }

            var pngTextFilePath = Application.dataPath + "/../pngs.txt";
            System.IO.File.WriteAllLines(pngTextFilePath, paths.Distinct().ToArray());

            const string args = "--padding 3 -f jsonarray --trim --size 1024x1024 --output test pngs.txt";

            CommandLineUtility.ExecuteProcess(Application.dataPath + "/../tools/texpack", args,
                Application.dataPath + "/..");
            
            System.IO.File.Delete(pngTextFilePath);

            var jsonFilePath = Application.dataPath + "/../test.json";
            var pngFilePath = Application.dataPath + "/../test.png";

            var json = System.IO.File.ReadAllText(jsonFilePath);
            var packerInfo = JsonUtility.FromJson<PackerInfo>(json);
            System.IO.File.Delete(jsonFilePath);

            var pngData = System.IO.File.ReadAllBytes(pngFilePath);
            System.IO.File.Delete(pngFilePath);

            var go = new GameObject("ZippedSprites");
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh();

            var tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, mipmap: false);
            tex.LoadImage(pngData);
            tex.alphaIsTransparency = true;
            EditorUtility.CompressTexture(tex,textureFormat, 100);
            Debug.Log(tex.format);

            var mat = new Material(Shader.Find("Unlit/Transparent"));
            mat.mainTexture = tex;
            meshRenderer.material = mat;

            var numSprites = srs.Count;
            var numVerts = numSprites * 4;
            var numIndices = numSprites * 6;

            var verts = new Vector3[numVerts];
            var uv = new Vector2[numVerts];
            var indices = new int[numIndices];

            for (var i = 0; i < numSprites; i++)
            {
                var spriteRenderer = srs[i];
                var sprite = spriteRenderer.sprite;

                var spritePath = AssetDatabase.GetAssetPath(spriteRenderer.sprite);
                var insets = packerInfo.GetInsetsForSprite(spritePath, sprite.pixelsPerUnit);

                verts[4 * i] = spriteRenderer.transform.TransformPoint(insets.min);
                verts[4 * i + 1] =
                    spriteRenderer.transform.TransformPoint(new Vector3(insets.min.x, insets.min.y) +
                                                            new Vector3(insets.size.x, 0f, 0f));
                verts[4 * i + 2] = spriteRenderer.transform.TransformPoint(insets.max);
                verts[4 * i + 3] =
                    spriteRenderer.transform.TransformPoint(new Vector3(insets.min.x, insets.min.y) +
                                                            new Vector3(0f, insets.size.y, 0f));

                var texRect = packerInfo.GetUvForSprite(spritePath);
                uv[4 * i] = texRect.min;
                uv[4 * i + 1] = texRect.min + new Vector2(texRect.size.x, 0f);
                uv[4 * i + 2] = texRect.max;
                uv[4 * i + 3] = texRect.min + new Vector2(0f, texRect.size.y);

                indices[6 * i] = 4 * i;
                indices[6 * i + 1] = 4 * i + 2;
                indices[6 * i + 2] = 4 * i + 1;
                indices[6 * i + 3] = 4 * i + 0;
                indices[6 * i + 4] = 4 * i + 3;
                indices[6 * i + 5] = 4 * i + 2;
            }

            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = indices;
            meshFilter.sharedMesh = mesh;
        }
    }
}