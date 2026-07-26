using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.editor
{
    public static class CacheMenu
    {
        private const string MenuPath = "Tools/Avatar Pose Library/Clear All Caches";

        [MenuItem(MenuPath, false, 200)]
        private static void ClearAllCaches()
        {
            var context = DynamicVariables.Settings.Inspector;
            var cachePath = NormalizeCachePath(DynamicVariables.Settings.cachePath);
            if (!IsSafeCachePath(cachePath))
            {
                EditorUtility.DisplayDialog(
                    context.cacheDialogTitle,
                    string.Format(context.cacheUnsafePathMessage, cachePath),
                    context.cacheOkButton);
                Debug.LogError(
                    $"AvatarPoseLibrary.CacheMenu: Refused to delete unsafe cache path: {cachePath}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(cachePath))
            {
                EditorUtility.DisplayDialog(
                    context.cacheDialogTitle,
                    string.Format(context.cacheNotFoundMessage, cachePath),
                    context.cacheOkButton);
                return;
            }

            var assetCount = AssetDatabase.FindAssets(string.Empty, new[] { cachePath }).Length;
            var cacheSize = GetDirectorySize(cachePath);
            var formattedCacheSize = FormatBytes(cacheSize, context.cacheUnknownSize);
            if (!EditorUtility.DisplayDialog(
                    context.cacheDialogTitle,
                    string.Format(
                        context.cacheDeleteConfirmMessage,
                        cachePath,
                        assetCount,
                        formattedCacheSize),
                    context.cacheDeleteButton,
                    context.cacheCancelButton))
            {
                return;
            }

            if (!AssetDatabase.DeleteAsset(cachePath))
            {
                Debug.LogError(
                    $"AvatarPoseLibrary.CacheMenu: Failed to delete cache folder: {cachePath}");
                EditorUtility.DisplayDialog(
                    context.cacheDialogTitle,
                    string.Format(context.cacheDeleteFailedMessage, cachePath),
                    context.cacheOkButton);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"AvatarPoseLibrary.CacheMenu: Deleted {assetCount} cached assets from {cachePath}");
            EditorUtility.DisplayDialog(
                context.cacheDialogTitle,
                string.Format(
                    context.cacheDeleteSuccessMessage,
                    assetCount,
                    formattedCacheSize),
                context.cacheOkButton);
        }

        public static string NormalizeCachePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        public static bool IsSafeCachePath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                string.Equals(path, "Assets", StringComparison.Ordinal) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            var segments = path.Split('/');
            foreach (var segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    return false;
                }
            }

            return true;
        }

        public static long GetDirectorySize(string path)
        {
            try
            {
                long size = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    size += new FileInfo(file).Length;
                }

                return size;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"AvatarPoseLibrary.CacheMenu: Failed to calculate cache size: {exception.Message}");
                return -1;
            }
        }

        public static string FormatBytes(long bytes, string unknownSize)
        {
            if (bytes < 0) return unknownSize;

            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }
    }
}