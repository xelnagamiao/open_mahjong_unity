using System;
using UnityEngine;

namespace Salasasa.Home
{
    [Serializable]
    public sealed class LobbyArtwork
    {
        public string sceneName;
        public string collection;
        public string variant;
        public string category;
        public string characterName;
        public Texture2D character;
        public Texture2D background;
        public string characterCredit;
        public string characterSourceUrl;
        public string backgroundCredit;
        public string backgroundSourceUrl;
        public string characterCopyright;
        public LobbyExpression[] expressions;
        public Rect characterUV = new Rect(0, 0, 1, 1);
        public float characterHeight = 960;
        public float characterMaxWidth = 800;
        public Vector2 characterOffset = new Vector2(0, -80);
        public Color backgroundTint = Color.white;
        public Color shadeLeft = Color.clear;
        public Color shadeRight = Color.clear;
    }

    [Serializable]
    public sealed class LobbyExpression
    {
        public string key;
        public string name;
        public string resourcePath;
    }
}
