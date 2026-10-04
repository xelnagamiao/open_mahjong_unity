using System;
using UnityEngine;

namespace MahjongClassroom {
    [CreateAssetMenu(menuName = "Mahjong/Classroom Lesson", fileName = "MahjongBasicsLesson")]
    public sealed class MahjongClassroomLesson : ScriptableObject {
        [Serializable]
        public sealed class TileGroup {
            public string heading;
            public int[] tileIds;
        }

        [Serializable]
        public sealed class Page {
            public string section;
            public string title;
            [TextArea(2, 4)] public string summary;
            [TextArea(2, 5)] public string narration;
            public string takeaway;
            public TileGroup[] groups;
        }

        [Serializable]
        public sealed class Tile {
            public int id;
            public string label;
        }

        [SerializeField] private Page[] pages = Array.Empty<Page>();
        [SerializeField] private Tile[] tiles = Array.Empty<Tile>();

        public int PageCount => pages.Length;
        public Page GetPage(int index) => pages[index];

        public Tile FindTile(int id) {
            foreach (var tile in tiles)
                if (tile.id == id) return tile;
            return null;
        }

#if UNITY_EDITOR
        public void SetEditorContent(Page[] content, Tile[] catalog) {
            pages = content;
            tiles = catalog;
        }
#endif
    }
}
