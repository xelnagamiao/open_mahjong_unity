using System;
using UnityEngine;

namespace Salasasa.Home
{
    [CreateAssetMenu(menuName="Salasasa/Icon library")]
    public sealed class LobbyIconLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id, label;
            public int group;
            public Sprite original;
            [Range(0, 3)] public int defaultStyle = LobbyIconBinding.OriginalStyle;
            public Sprite[] primary = new Sprite[3];
            public Sprite[] accent = new Sprite[3];
        }

        public Entry[] entries;

        public Entry Find(string id)
        {
            foreach (var entry in entries) if (entry.id == id) return entry;
            throw new ArgumentException("Unknown lobby icon: " + id, nameof(id));
        }
    }
}
