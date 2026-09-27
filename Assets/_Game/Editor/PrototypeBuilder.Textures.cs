using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voron.Editor
{
    public static partial class PrototypeBuilder
    {
        private const string TalkFaceSuffix = "_FaceTalk";
        private const string BlinkFaceSuffix = "_FaceBlink";

        private static void PreparePS1ArtTextureImports()
        {
            const string sourceFolder = Root + "/Art/Textures";
            if (!AssetDatabase.IsValidFolder(sourceFolder))
                return;

            foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { sourceFolder })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)))
            {
                ConfigurePointFilteredFace(path);
            }
        }

        private static Texture2D EnsureFaceTexture(CharacterSpec character)
        {
            string sourcePath = FindSourceFacePath(character);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                ConfigurePointFilteredFace(sourcePath);
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                if (source != null)
                    return source;
            }

            string placeholderPath = Root + "/Generated/Characters/Faces/FacePlaceholder_" + character.PrefabName + ".png";
            EnsureGeneratedPng(placeholderPath, CreateFaceTexture(character), false, 128);
            return RequireAsset<Texture2D>(placeholderPath);
        }

        // Talk/blink maps are optional: a missing map leaves the neutral face in place at runtime.
        private static Texture2D FindFaceVariantTexture(CharacterSpec character, string suffix)
        {
            string path = FaceVariantPath(character, suffix);
            ConfigurePointFilteredFace(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool IsFaceVariantUnassigned(Texture2D assigned, CharacterSpec character, string suffix)
        {
            return assigned == null && AssetDatabase.LoadAssetAtPath<Texture2D>(FaceVariantPath(character, suffix)) != null;
        }

        private static string FaceVariantPath(CharacterSpec character, string suffix)
        {
            return FaceArtFolder + "/" + character.PrefabName + suffix + ".png";
        }

        private static bool IsFaceVariantPath(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.EndsWith(TalkFaceSuffix, StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(BlinkFaceSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindSourceFacePath(CharacterSpec character)
        {
            string[] directPaths =
            {
                FaceArtFolder + "/" + character.PrefabName + "_Face.png",
                FaceArtFolder + "/" + character.PrefabName + ".png",
                FaceArtFolder + "/Face_" + character.PrefabName + ".png",
                FaceArtFolder + "/" + character.Id + ".png"
            };

            foreach (string path in directPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                    return path;
            }

            if (!AssetDatabase.IsValidFolder(FaceArtFolder))
                return null;

            string[] paths = AssetDatabase.FindAssets(string.Empty, new[] { FaceArtFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsFaceVariantPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string token in new[] { character.PrefabName }.Concat(character.SearchTokens))
            {
                string normalizedToken = NormalizeToken(token);
                string match = paths.FirstOrDefault(path => NormalizeToken(Path.GetFileNameWithoutExtension(path)).Contains(normalizedToken));
                if (match != null && AssetDatabase.LoadAssetAtPath<Texture2D>(match) != null)
                    return match;
            }

            return null;
        }

        private static Material EnsureFaceMaterial(CharacterSpec character, Texture2D faceTexture)
        {
            string path = Root + "/Materials/Characters/M_Face_" + character.PrefabName + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            Material material = CreateStandardMaterial("M_Face_" + character.PrefabName, Color.white);
            material.mainTexture = faceTexture;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material EnsureItemMaterial(ItemSpec item)
        {
            string path = Root + "/Materials/Items/M_" + ToPascalCase(item.Id) + ".mat";
            return EnsureFlatMaterial(path, "M_" + ToPascalCase(item.Id), item.Color);
        }

        private static PrototypeMaterials EnsurePrototypeMaterials()
        {
            return new PrototypeMaterials(
                EnsureFlatMaterial(Root + "/Materials/Prototype/M_Prototype_Floor.mat", "M_Prototype_Floor", new Color(0.27f, 0.28f, 0.27f)),
                EnsureFlatMaterial(Root + "/Materials/Prototype/M_Prototype_Wall.mat", "M_Prototype_Wall", new Color(0.19f, 0.2f, 0.21f)),
                EnsureFlatMaterial(Root + "/Materials/Prototype/M_Prototype_Trim.mat", "M_Prototype_Trim", new Color(0.12f, 0.13f, 0.14f)));
        }

        private static Material EnsureFlatMaterial(string path, string materialName, Color color)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            Material material = CreateStandardMaterial(materialName, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material CreateStandardMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("The Built-in Render Pipeline Standard shader is unavailable.");

            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.04f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static Sprite EnsureItemIcon(ItemSpec item)
        {
            string path = FindSourceIconPath(item);
            if (string.IsNullOrEmpty(path))
            {
                path = Root + "/UI/Icons/" + item.IconName + ".png";
                EnsureGeneratedPng(path, CreateIconTexture(item), true, 32);
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite ||
                                     importer.spriteImportMode != SpriteImportMode.Single ||
                                     importer.filterMode != FilterMode.Point || importer.mipmapEnabled))
            {
                ConfigureTextureImporter(importer, true, 32);
            }

            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (icon == null)
                throw new InvalidOperationException("Could not import the prototype item icon as a Sprite: " + path);
            return icon;
        }

        private static string FindSourceIconPath(ItemSpec item)
        {
            const string sourceFolder = Root + "/Art/UI/Icons";
            if (!AssetDatabase.IsValidFolder(sourceFolder))
                return null;

            string token = NormalizeToken(item.Id);
            return AssetDatabase.FindAssets(string.Empty, new[] { sourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => NormalizeToken(Path.GetFileNameWithoutExtension(path)).Contains(token) &&
                                        AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null);
        }

        private static bool IsItemIconMissing(ItemSpec item)
        {
            string sourcePath = FindSourceIconPath(item);
            string path = string.IsNullOrEmpty(sourcePath)
                ? Root + "/UI/Icons/" + item.IconName + ".png"
                : sourcePath;
            return AssetDatabase.LoadAssetAtPath<Sprite>(path) == null;
        }

        private static void EnsureGeneratedPng(string assetPath, byte[] pngData, bool sprite, int maxSize)
        {
            string absolutePath = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));

            bool created = !File.Exists(absolutePath);
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, pngData);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (!created)
                return;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
                ConfigureTextureImporter(importer, sprite, maxSize);
        }

        private static void ConfigureTextureImporter(TextureImporter importer, bool sprite, int maxSize)
        {
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (sprite)
                importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maxSize;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = sprite;
            importer.SaveAndReimport();
        }

        private static void ConfigurePointFilteredFace(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        private static byte[] CreateFaceTexture(CharacterSpec character)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            Color32 skin = new Color32(154, 118, 98, 255);
            Color32 shadow = new Color32(102, 76, 68, 255);
            Color32 hair = new Color32(40, 35, 34, 255);
            Color32 eye = new Color32(24, 25, 26, 255);
            Color32 brow = new Color32(45, 36, 34, 255);
            Color32 mouth = new Color32(89, 45, 46, 255);
            Color32 highlight = new Color32(191, 151, 123, 255);

            if (character.Id == "elena_voron")
            {
                skin = new Color32(183, 145, 124, 255);
                shadow = new Color32(133, 96, 84, 255);
                hair = new Color32(55, 32, 30, 255);
                brow = new Color32(58, 37, 34, 255);
                mouth = new Color32(121, 60, 63, 255);
                highlight = new Color32(211, 172, 146, 255);
            }
            else if (character.Id == "dr_ilya_morozov")
            {
                skin = new Color32(165, 148, 130, 255);
                shadow = new Color32(105, 99, 91, 255);
                hair = new Color32(119, 117, 107, 255);
                brow = new Color32(84, 80, 75, 255);
                mouth = new Color32(98, 62, 59, 255);
                highlight = new Color32(195, 177, 154, 255);
            }
            else if (character.Id == "police_officer")
            {
                skin = new Color32(174, 137, 112, 255);
                shadow = new Color32(111, 83, 71, 255);
                hair = new Color32(48, 47, 43, 255);
                brow = new Color32(48, 43, 40, 255);
                mouth = new Color32(105, 57, 53, 255);
                highlight = new Color32(202, 160, 130, 255);
            }

            Fill(pixels, skin);
            FillRect(pixels, size, 12, 4, 104, 120, shadow);
            FillRect(pixels, size, 20, 8, 88, 116, skin);
            FillRect(pixels, size, 24, 20, 80, 108, skin);
            FillRect(pixels, size, 16, 100, 96, 128, hair);
            FillRect(pixels, size, 12, 88, 24, 112, hair);
            FillRect(pixels, size, 104, 88, 116, 112, hair);
            FillRect(pixels, size, 28, 72, 48, 80, brow);
            FillRect(pixels, size, 80, 72, 100, 80, brow);
            FillRect(pixels, size, 28, 60, 48, 68, shadow);
            FillRect(pixels, size, 80, 60, 100, 68, shadow);
            FillRect(pixels, size, 32, 60, 40, 64, eye);
            FillRect(pixels, size, 84, 60, 92, 64, eye);
            FillRect(pixels, size, 56, 48, 72, 64, shadow);
            FillRect(pixels, size, 60, 52, 68, 60, highlight);
            FillRect(pixels, size, 48, 32, 80, 40, mouth);
            FillRect(pixels, size, 48, 40, 80, 44, shadow);
            FillRect(pixels, size, 24, 52, 32, 72, highlight);
            FillRect(pixels, size, 96, 52, 104, 72, shadow);

            if (character.Id == "alexey_voron")
            {
                FillRect(pixels, size, 28, 52, 48, 56, new Color32(87, 82, 77, 255));
                FillRect(pixels, size, 80, 52, 100, 56, new Color32(87, 82, 77, 255));
            }

            if (character.Id == "dr_ilya_morozov")
            {
                DrawLine(pixels, size, 24, 65, 52, 65, shadow);
                DrawLine(pixels, size, 76, 65, 104, 65, shadow);
                DrawLine(pixels, size, 24, 54, 52, 54, shadow);
                DrawLine(pixels, size, 76, 54, 104, 54, shadow);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FacePlaceholder_" + character.PrefabName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] result = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return result;
        }

        private static byte[] CreateIconTexture(ItemSpec item)
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 border = new Color32(48, 46, 42, 255);
            Color32 fill = item.Color;
            Fill(pixels, transparent);
            FillRect(pixels, size, 2, 2, 30, 30, border);
            FillRect(pixels, size, 4, 4, 28, 28, new Color32(31, 33, 35, 255));

            if (item.Id == "police_badge")
            {
                FillRect(pixels, size, 9, 8, 23, 24, fill);
                FillRect(pixels, size, 12, 5, 20, 27, fill);
                FillRect(pixels, size, 13, 11, 19, 19, new Color32(84, 67, 42, 255));
            }
            else if (item.Id == "old_photograph")
            {
                FillRect(pixels, size, 8, 5, 24, 27, new Color32(183, 172, 144, 255));
                FillRect(pixels, size, 10, 14, 22, 24, new Color32(68, 72, 69, 255));
                FillRect(pixels, size, 12, 17, 15, 21, new Color32(119, 116, 103, 255));
                FillRect(pixels, size, 17, 16, 20, 21, new Color32(125, 116, 102, 255));
            }
            else if (item.Id == "apartment_key")
            {
                FillRect(pixels, size, 7, 12, 17, 20, fill);
                FillRect(pixels, size, 14, 14, 26, 18, fill);
                FillRect(pixels, size, 21, 17, 24, 22, fill);
                FillRect(pixels, size, 8, 14, 13, 18, new Color32(33, 35, 36, 255));
            }
            else
            {
                FillRect(pixels, size, 8, 5, 24, 27, new Color32(184, 175, 151, 255));
                FillRect(pixels, size, 8, 5, 12, 27, new Color32(126, 53, 48, 255));
                FillRect(pixels, size, 15, 20, 22, 22, new Color32(83, 79, 70, 255));
                FillRect(pixels, size, 15, 15, 22, 17, new Color32(83, 79, 70, 255));
                FillRect(pixels, size, 15, 10, 21, 12, new Color32(83, 79, 70, 255));
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = item.IconName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] result = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return result;
        }

        private static void Fill(Color32[] pixels, Color32 color)
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
        }

        private static void FillRect(Color32[] pixels, int width, int xMin, int yMin, int xMax, int yMax, Color32 color)
        {
            int height = pixels.Length / width;
            xMin = Mathf.Clamp(xMin, 0, width);
            xMax = Mathf.Clamp(xMax, 0, width);
            yMin = Mathf.Clamp(yMin, 0, height);
            yMax = Mathf.Clamp(yMax, 0, height);
            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                    pixels[y * width + x] = color;
            }
        }

        private static void DrawLine(Color32[] pixels, int width, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < pixels.Length / width)
                    pixels[y0 * width + x0] = color;
                if (x0 == x1 && y0 == y1)
                    break;
                int twiceError = 2 * error;
                if (twiceError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (twiceError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static string NormalizeToken(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private sealed class PrototypeMaterials
        {
            public readonly Material Floor;
            public readonly Material Wall;
            public readonly Material Trim;

            public PrototypeMaterials(Material floor, Material wall, Material trim)
            {
                Floor = floor;
                Wall = wall;
                Trim = trim;
            }
        }
    }
}
