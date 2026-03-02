using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif

namespace Qingque13
{
    /// <summary>
    /// Calculates fan scores using precalculated fan_cache.
    /// Matches C++ get_fan function.
    /// </summary>
    public class QingqueScoring
    {
        private static Dictionary<string, double> fanCache;
        private static bool cacheLoaded = false;

        /// <summary>
        /// Loads the fan cache from Resources/fan_cache.json (Unity) or file path (non-Unity).
        /// Call this once at startup.
        /// </summary>
        public static void LoadFanCache()
        {
            if (cacheLoaded)
                return;

#if UNITY_2017_1_OR_NEWER
            LoadFanCacheFromUnityResources();
#else
            LogError("[Qingque13] LoadFanCache() called without path in non-Unity environment. Use LoadFanCacheFromFile() instead.");
            fanCache = new Dictionary<string, double>();
            cacheLoaded = true;
#endif
        }

#if UNITY_2017_1_OR_NEWER
        private static void LoadFanCacheFromUnityResources()
        {
            var jsonAsset = Resources.Load<TextAsset>("Qingque13/fan_cache");
            if (jsonAsset == null)
            {
                Debug.LogError("[Qingque13] fan_cache.json not found in Resources/Qingque13/");
                fanCache = new Dictionary<string, double>();
                cacheLoaded = true;
                return;
            }

            try
            {
                ParseFanCacheJson(jsonAsset.text);
                Debug.Log($"[Qingque13] Loaded fan cache with {fanCache.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qingque13] Failed to load fan_cache.json: {ex.Message}");
                fanCache = new Dictionary<string, double>();
            }
            cacheLoaded = true;
        }
#endif

        /// <summary>
        /// Loads the fan cache from a file path. Use this for non-Unity environments.
        /// </summary>
        public static void LoadFanCacheFromFile(string filePath)
        {
            if (cacheLoaded)
                return;

            if (!File.Exists(filePath))
            {
                LogError($"[Qingque13] fan_cache.json not found at: {filePath}");
                fanCache = new Dictionary<string, double>();
                cacheLoaded = true;
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(filePath);
                ParseFanCacheJson(jsonText);
                Log($"[Qingque13] Loaded fan cache with {fanCache.Count} entries from {filePath}");
            }
            catch (Exception ex)
            {
                LogError($"[Qingque13] Failed to load fan_cache.json: {ex.Message}");
                fanCache = new Dictionary<string, double>();
            }
            cacheLoaded = true;
        }

        /// <summary>
        /// Parses the fan cache JSON. Works with both Unity and non-Unity.
        /// Expects JSON format: {"fan_cache": [{"code": "...", "value": 1.0}, ...]}
        /// </summary>
        private static void ParseFanCacheJson(string jsonText)
        {
            fanCache = new Dictionary<string, double>();

            // Simple JSON parsing without external dependencies
            // Format: {"fan_cache":[{"code":"...","value":1.0},...]
            int arrayStart = jsonText.IndexOf('[');
            int arrayEnd = jsonText.LastIndexOf(']');
            if (arrayStart < 0 || arrayEnd < 0)
            {
                LogError("[Qingque13] Invalid fan_cache.json format");
                return;
            }

            string arrayContent = jsonText.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            
            // Parse each entry: {"code":"...","value":1.0}
            int pos = 0;
            while (pos < arrayContent.Length)
            {
                int objStart = arrayContent.IndexOf('{', pos);
                if (objStart < 0) break;
                
                int objEnd = arrayContent.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string obj = arrayContent.Substring(objStart, objEnd - objStart + 1);
                
                // Extract code
                int codeStart = obj.IndexOf("\"code\"");
                if (codeStart < 0) { pos = objEnd + 1; continue; }
                int codeValStart = obj.IndexOf('"', codeStart + 6);
                int codeValEnd = obj.IndexOf('"', codeValStart + 1);
                string code = obj.Substring(codeValStart + 1, codeValEnd - codeValStart - 1);

                // Extract value
                int valueStart = obj.IndexOf("\"value\"");
                if (valueStart < 0) { pos = objEnd + 1; continue; }
                int valueColonPos = obj.IndexOf(':', valueStart);
                int valueEnd = obj.IndexOf('}', valueColonPos);
                string valueStr = obj.Substring(valueColonPos + 1, valueEnd - valueColonPos - 1).Trim();
                
                if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    fanCache[code] = value;
                }

                pos = objEnd + 1;
            }
        }

        /// <summary>
        /// Resets the cache (useful for testing).
        /// </summary>
        public static void ResetCache()
        {
            fanCache = null;
            cacheLoaded = false;
        }

        private static void Log(string message)
        {
#if UNITY_2017_1_OR_NEWER
            Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        private static void LogWarning(string message)
        {
#if UNITY_2017_1_OR_NEWER
            Debug.LogWarning(message);
#else
            Console.WriteLine($"WARNING: {message}");
#endif
        }

        private static void LogError(string message)
        {
#if UNITY_2017_1_OR_NEWER
            Debug.LogError(message);
#else
            Console.WriteLine($"ERROR: {message}");
#endif
        }

        /// <summary>
        /// Calculates the fan score for a fan set by looking up the precalculated cache.
        /// Matches C++ get_fan function.
        /// </summary>
        public static double GetFan(HashSet<QingqueFan> fans)
        {
            if (!cacheLoaded)
                LoadFanCache();

            // Generate fan code (bitset representation)
            string code = GenerateFanCode(fans);

            // Lookup in cache
            if (fanCache.TryGetValue(code, out double value))
            {
                return value;
            }

            // Fallback: calculate from fan values if not in cache
            double totalFan = 0.0;
            foreach (var fan in fans)
            {
                if (QingqueFanMetadata.Tags.TryGetValue(fan, out var tag))
                {
                    totalFan += tag.FanValue;
                }
            }

            LogWarning($"[Qingque13] Fan combination not in cache: {code}, using fallback calculation");
            return totalFan;
        }

        /// <summary>
        /// Calculates the maximum fan score from multiple decompositions.
        /// Removes occasional fans before cache lookup, then adds them back.
        /// Matches C++ get_fan(const w_data& data, const hand& h) function.
        /// </summary>
        public static (double fan, HashSet<QingqueFan> bestFans) GetMaxFan(List<HashSet<QingqueFan>> allFans)
        {
            double maxFan = 0.0;
            HashSet<QingqueFan> bestFans = new HashSet<QingqueFan>();

            foreach (var fans in allFans)
            {
                // Separate occasional fans from regular fans
                var regularFans = new HashSet<QingqueFan>();
                var occasionalFans = new HashSet<QingqueFan>();
                double occasionalFanValue = 0.0;

                foreach (var f in fans)
                {
                    if (QingqueFanMetadata.Tags.TryGetValue(f, out var tag) && tag.IsOccasional)
                    {
                        occasionalFans.Add(f);
                        occasionalFanValue += tag.FanValue;
                    }
                    else
                    {
                        regularFans.Add(f);
                    }
                }

                // Get fan score for regular fans (cache lookup without occasional)
                double currentFan = GetFan(regularFans);
                
                // Add occasional fan values back
                currentFan += occasionalFanValue;

                if (currentFan > maxFan)
                {
                    maxFan = currentFan;
                    // Combine regular and occasional fans for the best set
                    bestFans = new HashSet<QingqueFan>(regularFans);
                    foreach (var occasionalFan in occasionalFans)
                    {
                        bestFans.Add(occasionalFan);
                    }
                }
            }

            return (maxFan, bestFans);
        }

        /// <summary>
        /// Generates a 96-character bitset code from a fan set.
        /// Matches C++ fan_code generation.
        /// </summary>
        private static string GenerateFanCode(HashSet<QingqueFan> fans)
        {
            // C++ uses a bitset of size 96 for fan codes
            // Each position corresponds to a fan index
            char[] code = new char[96];
            for (int i = 0; i < 96; i++)
                code[i] = '0';

            foreach (var fan in fans)
            {
                int index = (int)fan;
                if (index >= 0 && index < 96)
                {
                    code[95 - index] = '1'; // Reverse order to match C++ bitset string representation
                }
            }

            return new string(code);
        }

        /// <summary>
        /// Data structure for deserializing fan_cache.json.
        /// </summary>
        [Serializable]
        private class FanCacheData
        {
            public FanCacheEntry[] entries;
        }

        [Serializable]
        private class FanCacheEntry
        {
            public string code;
            public double value;
        }
    }
}
