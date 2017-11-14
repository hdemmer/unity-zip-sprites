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
        private const int TEX_SIZE = 1024;

        [MenuItem("ZipSprites/Pack")]
        public static void PackCurrentSceneCurrentSettings()
        {
            PackCurrentScene(GetTextureFormatForCurrentPlatform());
        }

        private static TextureFormat GetTextureFormatForCurrentPlatform()
        {
            TextureFormat textureFormat;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                    textureFormat = TextureFormat.ETC2_RGBA8;
                    break;
                case BuildTarget.iOS:
                    textureFormat = TextureFormat.PVRTC_RGBA4;
                    break;
                case BuildTarget.WebGL:
                    textureFormat = TextureFormat.DXT5;
                    break;
                default:
                    Debug.LogWarning("Don't know your texture compression setting for " +
                                                             EditorUserBuildSettings.activeBuildTarget + ". Using DXT5.");
                    textureFormat = TextureFormat.DXT5;
                    break;
            }
            
            return textureFormat;
        }

        public static GameObject PackCurrentScene(TextureFormat textureFormat)
        {
            var roots = GetCurrentSceneRoots();
            var spriteRenderers = FindAndSortSpriteRenderersIn(roots);

            var zipSprites = new ZipSpritesEditor(spriteRenderers, textureFormat);

            return zipSprites.resultGameObject;
        }

        private static GameObject[] GetCurrentSceneRoots()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects();
        }

        private static List<SpriteRenderer> FindAndSortSpriteRenderersIn(IEnumerable<GameObject> gameObjects)
        {
            var result = new List<SpriteRenderer>();
            foreach (var gameObject in gameObjects)
            {
                result.AddRange(gameObject.GetComponentsInChildren<SpriteRenderer>());
            }
            result.Sort((a, b) =>
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
            return result;
        }

        private readonly TextureFormat textureFormat;
        private readonly List<SpriteRenderer> spriteRenderers;
        private byte[] pngData;
        private PackerInfo packerInfo;
        private GameObject resultGameObject;

        private ZipSpritesEditor(IEnumerable<SpriteRenderer> spriteRenderers, TextureFormat textureFormat)
        {
            this.spriteRenderers = new List<SpriteRenderer>(spriteRenderers);
            this.textureFormat = textureFormat;

            RunPacker();
            ReadPackerInfo();
            LoadPngData();
            resultGameObject = new GameObject("ZippedSprites");
            CreateMeshRenderer();
            CreateMeshFilter();
        }

        private void RunPacker()
        {
            var paths = new List<string>();
            foreach (var spriteRenderer in spriteRenderers)
            {
                var sprite = spriteRenderer.sprite;
                var path = AssetDatabase.GetAssetPath(sprite);

                if (sprite && !string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            var pngTextFilePath = Application.dataPath + "/../pngs.txt";
            System.IO.File.WriteAllLines(pngTextFilePath, paths.Distinct().ToArray());

            var args = "--padding 3 -f jsonarray --trim --size "
                       + string.Format("{0}x{1}", TEX_SIZE, TEX_SIZE)
                       + " --output test pngs.txt";

            CommandLineUtility.ExecuteProcess(Application.dataPath + "/../tools/texpack", args,
                Application.dataPath + "/..");

            System.IO.File.Delete(pngTextFilePath);
        }

        private void ReadPackerInfo()
        {
            var jsonFilePath = Application.dataPath + "/../test.json";
            var json = System.IO.File.ReadAllText(jsonFilePath);
            packerInfo = JsonUtility.FromJson<PackerInfo>(json);
            System.IO.File.Delete(jsonFilePath);
        }

        private void LoadPngData()
        {
            var pngFilePath = Application.dataPath + "/../test.png";
            pngData = System.IO.File.ReadAllBytes(pngFilePath);
            System.IO.File.Delete(pngFilePath);
        }

        private Mesh CreateMesh()
        {
            var mesh = new Mesh();

            var numSprites = spriteRenderers.Count;
            var numVerts = numSprites * 4;
            var numIndices = numSprites * 6;

            var verts = new Vector3[numVerts];
            var uv = new Vector2[numVerts];
            var indices = new int[numIndices];

            for (var i = 0; i < numSprites; i++)
            {
                var spriteRenderer = spriteRenderers[i];
                var sprite = spriteRenderer.sprite;

                var spritePath = AssetDatabase.GetAssetPath(spriteRenderer.sprite);
                var spriteRect = packerInfo.GetRectForSprite(spritePath, sprite.pixelsPerUnit);

                verts[4 * i] = spriteRenderer.transform.TransformPoint(spriteRect.min);
                verts[4 * i + 1] =
                    spriteRenderer.transform.TransformPoint(new Vector3(spriteRect.min.x, spriteRect.min.y) +
                                                            new Vector3(spriteRect.size.x, 0f, 0f));
                verts[4 * i + 2] = spriteRenderer.transform.TransformPoint(spriteRect.max);
                verts[4 * i + 3] =
                    spriteRenderer.transform.TransformPoint(new Vector3(spriteRect.min.x, spriteRect.min.y) +
                                                            new Vector3(0f, spriteRect.size.y, 0f));

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

            return mesh;
        }

        private void CreateMeshRenderer()
        {
            var tex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, mipmap: false);
            tex.LoadImage(pngData);
            tex.alphaIsTransparency = true;
            EditorUtility.CompressTexture(tex, textureFormat, 100);

            var meshRenderer = resultGameObject.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Unlit/Transparent"));
            mat.mainTexture = tex;
            meshRenderer.material = mat;
        }

        private void CreateMeshFilter()
        {
            var meshFilter = resultGameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateMesh();
        }

        #region - private class to parse packer info into

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

                [UsedImplicitly] public PackerInfoMetaSize size;
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

            public Rect GetRectForSprite(string spritePath, float pixelsPerUnit)
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

        #endregion
    }
}