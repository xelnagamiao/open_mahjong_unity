#if UNITY_WEBGL || UNITY_EDITOR
using System;

namespace OmUpdate {
    // Embedded in the bootstrap scene. An old cached Player therefore always
    // requests the exact content it was built with.
    [Serializable]
    public sealed class WebTextureManifest {
        public string mainScene;
        public ResourceEntry[] resources;
        public Bundle[] dxt;
        public Bundle[] astc;

        [Serializable]
        public sealed class Bundle {
            public string name;
            public string file;
            public uint crc;
            public int bytes;
        }
    }
}
#endif
