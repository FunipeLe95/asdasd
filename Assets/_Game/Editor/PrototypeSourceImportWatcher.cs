using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Voron.Editor
{
    internal sealed class PrototypeSourceImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            bool sourceChanged = importedAssets.Any(IsPrototypeSource) || movedAssets.Any(IsPrototypeSource);
            if (sourceChanged)
                PrototypeBuilder.ScheduleAutomaticBuild();
        }

        private static bool IsPrototypeSource(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            string extension = Path.GetExtension(normalizedPath);
            bool model = string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) &&
                         normalizedPath.StartsWith("Assets/_Game/Art/Characters/", StringComparison.OrdinalIgnoreCase);
            bool face = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                        normalizedPath.StartsWith("Assets/_Game/Art/Textures/Faces/", StringComparison.OrdinalIgnoreCase);
            return model || face;
        }
    }
}
