using System;

namespace Salasasa.Home
{
    public static class CompleteLobbyEnvironment
    {
        public static bool IsDevelopment => ConfigManager.Debug;
        public static bool Has(string flag)
        {
#if UNITY_EDITOR
            if (flag == "-local-review" || flag == "-auto-guest") return false;
#endif
            return Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
        }
        public static string Value(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
