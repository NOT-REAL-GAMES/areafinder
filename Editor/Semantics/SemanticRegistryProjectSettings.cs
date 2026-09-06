using UnityEditor;

namespace NotRealGames.Areafinder.Editor
{
    internal static class SemanticRegistryProjectSettings
    {
        internal const string ConfigKey = "com.notrealgames.areafinder.semantic-registry";

        public static SemanticRegistryAsset Registry
        {
            get
            {
                EditorBuildSettings.TryGetConfigObject(ConfigKey, out SemanticRegistryAsset registry);
                return registry;
            }
        }

        public static bool TrySetRegistry(SemanticRegistryAsset registry, out string error)
        {
            if (registry != null && !AssetDatabase.Contains(registry))
            {
                error = "The semantic registry must be saved as a project asset.";
                return false;
            }

            if (registry == null)
            {
                EditorBuildSettings.RemoveConfigObject(ConfigKey);
            }
            else
            {
                EditorBuildSettings.AddConfigObject(ConfigKey, registry, true);
            }

            error = null;
            return true;
        }
    }
}
