using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MahjongClassroom;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>Authors the standalone classroom once, with Inspector-editable content and saved UI references.</summary>
public static class MahjongClassroomSceneBuilder {
    public const string ScenePath = "Assets/Scenes/CirnoMahjongClassroom.unity";
    public const string LessonPath = "Assets/MahjongClassroom/MahjongBasicsLesson.asset";
    public const string ArtRoot = "Assets/Art/MahjongClassroom";
    private const string FontPath = ArtRoot + "/Typography/ClassroomSans.asset";
    private const string PortraitPath = ArtRoot + "/Cirno/cirno-normal.png";
    private static readonly Color Ink = Hex("173E4B");
    private static readonly Color Chalk = Hex("F3F6E8");
    private static readonly Color Mint = Hex("A8E1DB");
    private static TMP_FontAsset font;

    [MenuItem("Tools/Mahjong/Classroom/Create or Open Example")]
    public static void CreateOrOpenExample() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式。");
        if (File.Exists(ScenePath)) {
            var existing = SceneManager.GetSceneByPath(ScenePath);
            if (!existing.isLoaded) existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(existing);
            return;
        }

        EnsureFolder("Assets/MahjongClassroom");
        EnsureFolder(ArtRoot + "/Typography");
        var pages = CreatePages();
        var lesson = AssetDatabase.LoadAssetAtPath<MahjongClassroomLesson>(LessonPath);
        if (lesson == null) {
            lesson = ScriptableObject.CreateInstance<MahjongClassroomLesson>();
            lesson.SetEditorContent(pages, CreateTileCatalog());
            AssetDatabase.CreateAsset(lesson, LessonPath);
        }
        font = CreateFont(lesson);
        ConfigurePortrait();
        var portrait = Require<Sprite>(PortraitPath);

        // Additive authoring preserves every open scene, including unsaved user edits.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var cameraObject = new GameObject("Classroom Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 0, -10);
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Hex("F1F0E7");
        camera.orthographic = true;

        var canvasObject = new GameObject("Mahjong Classroom", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        var root = canvasObject.GetComponent<RectTransform>();
        var clickSurface = Stretch("Click Anywhere", root);
        var background = clickSurface.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = Hex("F1F0E7");
        background.raycastTarget = true;
        var controller = clickSurface.gameObject.AddComponent<CirnoClassroomController>();
        var stage = Rect("Stage 1920x1080", clickSurface, 0, 0, 1920, 1080);
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f, .5f);
        stage.anchoredPosition = Vector2.zero;

        Image("Lower Tint", stage, 0, 695, 1920, 385, Hex("DCE9E2"));
        for (int i = 0; i < 12; i++)
            Image("Notebook Rule " + i, stage, 0, 178 + i * 72, 1920, 1, new Color(.12f, .31f, .35f, .04f));
        Image("Header Accent", stage, 66, 53, 6, 80, Ink);
        Text("School Name", stage, 90, 77, 1000, 75, "琪露诺的麻雀教室", 52, Ink, true);
        Text("Course Label", stage, 1354, 75, 500, 35, "国标麻将 / 第 01 课", 24, Ink, false, TextAlignmentOptions.Right);

        var halo = Image("Ice Diamond", stage, 1375, 274, 430, 430, Hex("C6E7E7"));
        halo.rectTransform.localEulerAngles = new Vector3(0, 0, 16);
        var diamond = Image("Ice Diamond Small", stage, 1280, 214, 60, 60, Hex("BAD5DB"));
        diamond.rectTransform.localEulerAngles = new Vector3(0, 0, 30);
        Image("Blackboard Shadow", stage, 75, 197, 1180, 566, Hex("A2BBB5"));
        Image("Blackboard Frame", stage, 64, 183, 1180, 566, Hex("49676A"));
        Image("Blackboard", stage, 73, 192, 1162, 548, Hex("1D424A"));
        Image("Board Top Rule", stage, 113, 272, 1082, 2, Hex("5A7D7B"));
        var section = Text("Lesson Section", stage, 112, 216, 850, 32, "", 21, Mint, true);
        var pageNumber = Text("Page Number", stage, 1018, 214, 175, 36, "", 26, Chalk, false, TextAlignmentOptions.Right);
        var title = Text("Lesson Title", stage, 110, 295, 1090, 62, "", 43, Chalk, true);
        var summary = Text("Lesson Summary", stage, 113, 363, 1082, 70, "", 25, Hex("CCE0D9"));

        var groupRoot = Rect("Tile Groups", stage, 112, 457, 1082, 198);
        var groupLayout = groupRoot.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        groupLayout.spacing = 24;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = true;
        groupLayout.childForceExpandHeight = true;
        var groups = new CirnoClassroomController.GroupView[3];
        for (int i = 0; i < groups.Length; i++) groups[i] = CreateGroup(groupRoot, i);
        Image("Takeaway Rule", stage, 113, 675, 1082, 1, Hex("5A7D7B"));
        var takeaway = Text("Takeaway", stage, 113, 687, 1082, 43, "", 22, Mint);
        Image("Chalk", stage, 1110, 744, 47, 6, Chalk);
        Image("Eraser", stage, 1168, 740, 44, 12, Hex("B4C1B9"));

        var portraitWindow = Rect("Portrait Window", stage, 1245, 156, 646, 644);
        portraitWindow.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var character = Image("Cirno - afensorm CC BY 4.0", portraitWindow, 0, 0, 646, 913, Color.white);
        character.sprite = portrait;
        character.preserveAspect = true;

        Image("Dialogue Shadow", stage, 70, 812, 1786, 218, Hex("B4C9C4"));
        Image("Dialogue", stage, 64, 800, 1786, 218, Hex("FFFEF7"));
        Image("Dialogue Accent", stage, 64, 800, 8, 218, Hex("619CA7"));
        Text("Teacher Name", stage, 110, 834, 207, 55, "琪露诺", 36, Ink, true);
        Image("Dialogue Divider", stage, 330, 837, 1, 128, Hex("D4DED7"));
        var narration = Text("Narration", stage, 366, 832, 1425, 102, "", 30, Ink);
        narration.lineSpacing = 12;

        var previous = Button("Previous", stage, 365, 948, 148, 42, "< 上一页", Hex("E6EEEA"), Ink, 20);
        var restart = Button("Restart", stage, 532, 948, 150, 42, "从头学习", Hex("E6EEEA"), Ink, 20);
        var next = Button("Advance", stage, 1433, 944, 358, 48, "", Ink, Chalk, 23);
        var advanceLabel = next.GetComponentInChildren<TextMeshProUGUI>();
        Image("Progress Track", stage, 708, 966, 665, 4, Hex("DDE7E1"));
        var progress = Image("Progress Fill", stage, 708, 966, 665, 4, Hex("5B939D"));
        progress.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        progress.type = UnityEngine.UI.Image.Type.Filled;
        progress.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        progress.fillOrigin = 0;

        controller.ConfigureForEditor(lesson, section, title, summary, narration, takeaway, pageNumber,
            advanceLabel, progress, previous, groups);
        UnityEventTools.AddPersistentListener(previous.onClick, controller.Previous);
        UnityEventTools.AddPersistentListener(restart.onClick, controller.RestartLesson);
        UnityEventTools.AddPersistentListener(next.onClick, controller.Advance);
        OrganizeStage(stage);
        SetPersistentPreviewBackgrounds(controller);
        new GameObject("Classroom EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Canvas.ForceUpdateCanvases();
        Validate(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = canvasObject;
        Debug.Log("[Mahjong Classroom] Created " + ScenePath + " with " + lesson.PageCount + " lesson pages.");
    }

    public static void Validate(Scene scene) {
        var roots = scene.GetRootGameObjects();
        var controller = roots.SelectMany(r => r.GetComponentsInChildren<CirnoClassroomController>(true)).Single();
        if (!controller.ValidateSetup(out string problem)) throw new InvalidOperationException(problem);
        if (roots.Sum(r => r.GetComponentsInChildren<EventSystem>(true).Length) != 1)
            throw new InvalidOperationException("课堂场景需要且只需要一个 EventSystem。");
        foreach (var root in roots)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new InvalidOperationException("Missing script: " + t.name);
    }

    [MenuItem("Tools/Mahjong/Classroom/Update Layout and Tile Appearance")]
    public static void UpdateExample() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var controller = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<CirnoClassroomController>(true)).Single();
        var serialized = new SerializedObject(controller);
        var groups = serialized.FindProperty("groups");
        for (int g = 0; g < groups.arraySize; g++) {
            var group = groups.GetArrayElementAtIndex(g);
            var slots = group.FindPropertyRelative("slots");
            var cards = group.FindPropertyRelative("cards");
            var backgrounds = group.FindPropertyRelative("backgrounds");
            cards.arraySize = backgrounds.arraySize = slots.arraySize;
            for (int i = 0; i < slots.arraySize; i++) {
                var slot = ((GameObject)slots.GetArrayElementAtIndex(i).objectReferenceValue).transform;
                var card = slot.Find("Card") as RectTransform;
                if (card == null) card = CreateCardRoot(slot);
                var face = (UnityEngine.UI.Image)group.FindPropertyRelative("faces").GetArrayElementAtIndex(i).objectReferenceValue;
                face.sprite = null;
                var body = slot.GetComponentsInChildren<UnityEngine.UI.Image>(true).Single(t => t.name == "Tile Body");
                foreach (var image in new[] { body, face }) {
                    image.transform.SetParent(card, false);
                    FillRect(image.rectTransform);
                    image.raycastTarget = false;
                    image.preserveAspect = true;
                    image.useSpriteMesh = false;
                }
                body.transform.SetAsFirstSibling();
                cards.GetArrayElementAtIndex(i).objectReferenceValue = card;
                backgrounds.GetArrayElementAtIndex(i).objectReferenceValue = body;
                var label = (TextMeshProUGUI)group.FindPropertyRelative("labels").GetArrayElementAtIndex(i).objectReferenceValue;
                AnchorTileLabel(label.rectTransform);
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var lesson = AssetDatabase.LoadAssetAtPath<MahjongClassroomLesson>(LessonPath);
        var finalPage = lesson.GetPage(lesson.PageCount - 1);
        finalPage.narration = finalPage.narration.Replace("左键点击，就能从第一课重新复习！", "");
        if (finalPage.takeaway == "本课完成 / 点击继续按钮或画面任意位置，再学一遍。")
            finalPage.takeaway = "一副 144 张：数牌 108 张 · 字牌 28 张 · 花牌 8 张。";
        EditorUtility.SetDirty(lesson);
        AssetDatabase.SaveAssetIfDirty(lesson);
        foreach (var text in controller.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (new[] { "Series", "Course Subtitle", "Teacher Role", "Credits", "Input Hint" }.Contains(text.name))
                UnityEngine.Object.DestroyImmediate(text.gameObject);
        // Attribution remains with the portrait source and full license in Art/MahjongClassroom/Cirno.
        var stage = controller.transform.Find("Stage 1920x1080") as RectTransform;
        OrganizeStage(stage);
        controller.RefreshForEditor();
        SetPersistentPreviewBackgrounds(controller);
        Canvas.ForceUpdateCanvases();
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = controller.gameObject;
    }

    private static void SetPersistentPreviewBackgrounds(CirnoClassroomController controller) {
        var background = Require<Sprite>("Assets/Resources/" + TilePackIds.DefaultHandBgResource + ".png");
        foreach (var image in controller.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (image.name == "Tile Body") image.sprite = background;
    }

    private static RectTransform CreateCardRoot(Transform slot) {
        var card = Rect("Card", slot, 0, 0, 86, 127);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 1f);
        return card;
    }

    private static void AnchorTileLabel(RectTransform label) {
        label.anchorMin = Vector2.zero;
        label.anchorMax = new Vector2(1f, 0f);
        label.pivot = new Vector2(.5f, 0f);
        label.anchoredPosition = Vector2.zero;
        label.sizeDelta = new Vector2(0f, 25f);
    }

    private static void FillRect(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void OrganizeStage(RectTransform stage) {
        if (stage.Find("LessonBoard") != null) return;
        var children = stage.Cast<Transform>().ToArray();
        var background = Rect("Background", stage, 0, 0, 1920, 1080);
        var header = Rect("Header", stage, 64, 40, 1786, 110);
        var board = Rect("LessonBoard", stage, 64, 183, 1180, 566);
        var teacher = Rect("Teacher", stage, 1245, 156, 646, 644);
        var dialogue = Rect("DialoguePanel", stage, 64, 800, 1786, 218);
        var navigation = Rect("Navigation", dialogue, 301, 144, 1426, 48);
        foreach (var child in children) {
            Transform parent;
            if (child.name == "Lower Tint" || child.name.StartsWith("Notebook Rule")) parent = background;
            else if (new[] { "Header Accent", "School Name", "Course Label" }.Contains(child.name)) parent = header;
            else if (child.name.StartsWith("Ice Diamond") || child.name == "Portrait Window") parent = teacher;
            else if (new[] { "Previous", "Restart", "Advance", "Progress Track", "Progress Fill" }.Contains(child.name)) parent = navigation;
            else if (child.name.StartsWith("Dialogue") || child.name == "Teacher Name" || child.name == "Narration") parent = dialogue;
            else parent = board;
            child.SetParent(parent, true);
        }
        navigation.SetAsLastSibling();
    }

    private static CirnoClassroomController.GroupView CreateGroup(RectTransform parent, int index) {
        var root = Rect("Group " + (index + 1), parent, 0, 0, 340, 198);
        var flexible = root.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        flexible.flexibleWidth = 1;
        flexible.minWidth = 0;
        var heading = Text("Group Heading", root, 0, 0, 340, 29, "", 22, Chalk, true, TextAlignmentOptions.Center);
        heading.rectTransform.anchorMin = new Vector2(0, 1);
        heading.rectTransform.anchorMax = new Vector2(1, 1);
        heading.rectTransform.sizeDelta = new Vector2(0, 29);
        var row = Stretch("Tiles", root);
        row.offsetMin = new Vector2(0, 0);
        row.offsetMax = new Vector2(0, -39);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 10;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var view = new CirnoClassroomController.GroupView {
            root = root.gameObject, heading = heading, slots = new GameObject[9],
            cards = new RectTransform[9], backgrounds = new UnityEngine.UI.Image[9],
            faces = new UnityEngine.UI.Image[9], labels = new TextMeshProUGUI[9]
        };
        for (int t = 0; t < view.slots.Length; t++) {
            var slot = Rect("Tile " + (t + 1), row, 0, 0, 86, 159);
            view.slots[t] = slot.gameObject;
            view.cards[t] = CreateCardRoot(slot);
            view.backgrounds[t] = Stretch("Tile Body", view.cards[t]).gameObject.AddComponent<UnityEngine.UI.Image>();
            view.faces[t] = Stretch("Tile Face", view.cards[t]).gameObject.AddComponent<UnityEngine.UI.Image>();
            view.backgrounds[t].raycastTarget = view.faces[t].raycastTarget = false;
            view.labels[t] = Text("Tile Name", slot, -4, 135, 94, 25, "", 17, Chalk, false, TextAlignmentOptions.Center);
            AnchorTileLabel(view.labels[t].rectTransform);
        }
        return view;
    }

    private static MahjongClassroomLesson.Page[] CreatePages() {
        return new[] {
            Page("01 / 认识一副牌", "一副国标麻将，一共 144 张", "把牌先分成三类：数牌、字牌、花牌。\n不用急着记番种，今天先认清牌面。",
                "欢迎来到麻雀教室！我是琪露诺。先一起认识麻将牌吧——数牌 108 张，字牌 28 张，再加 8 张花牌。", "记住三个名字：数牌 · 字牌 · 花牌",
                Group("数牌 / 108 张", 11, 25, 39), Group("字牌 / 28 张", 41, 45, 46), Group("花牌 / 8 张", 51, 55, 56)),
            Page("02 / 数牌 · 万", "万牌：从一万，到九万", "看牌面上的汉字数字和“萬”。\n一到九各是一种牌，每种都有 4 张，共 36 张。",
                "这是万牌！数字告诉你它是几万。比如二万和三万是不同的牌，两张五万才是同一种牌。", "9 种万牌，每种 4 张，共 36 张。",
                Group("万子 / 1—9", Enumerable.Range(11, 9).ToArray())),
            Page("03 / 数牌 · 筒", "筒牌：数一数圆圈", "筒牌也常叫“饼牌”。\n从一筒到九筒，每种 4 张，共 36 张。",
                "这些圆圈就是筒牌的标记。一筒通常画成一个大圆，其他筒牌可以数圆圈。五筒和五万虽然都是五，花色却不同哦。", "数字相同、花色不同，仍然是不同的牌。",
                Group("筒子 / 1—9", Enumerable.Range(21, 9).ToArray())),
            Page("04 / 数牌 · 条", "条牌：竹节里藏着一只鸟", "条牌也叫“索牌”，同样是一到九。\n一条通常画成鸟，不要因为没有竹节就认错它。",
                "找到那只小鸟了吗？它是一条！二条到九条用竹节图案表示。万、筒、条三种花色，每一种都有 36 张。", "3 种花色，每种 36 张，共 108 张数牌。",
                Group("条子 / 1—9", Enumerable.Range(31, 9).ToArray())),
            Page("05 / 字牌", "四种风牌，三种箭牌", "风牌：东、南、西、北。箭牌：中、发、白。\n7 种字牌，每种 4 张，共 28 张。",
                "带文字的这些牌叫字牌。白板有时是空白，有时画着边框。要记住：东、南、西不能像一二三那样组成顺子。", "字牌没有数字顺序，不能组成顺子。",
                Group("风牌与箭牌", 41, 42, 43, 44, 45, 47, 46)),
            Page("06 / 花牌", "八张花牌，每张只有一张", "春、夏、秋、冬；梅、兰、竹、菊。\n花牌与其他牌不同：每一种只有 1 张。",
                "花牌很好认！摸到后要亮出，再从牌墙末端补牌；如果又补到花，就继续补。花牌单独摆放，不拿来组成普通的顺子、刻子或对子。", "摸到花牌：先亮出，再补牌；补到花牌继续补。",
                Group("四季与四君子", Enumerable.Range(51, 8).ToArray())),
            Page("07 / 张数", "同一种常规牌，都有四张", "数牌和字牌，每一种都重复 4 次。\n数牌 108 张 + 字牌 28 张 = 136 张；再加 8 张花牌，共 144 张。",
                "看，这四张都是五万，它们的身份完全一样。数牌与字牌遵守“每种四张”，花牌则是“每种一张”——别把它们记反啦。", "认牌小口诀：数牌字牌各四张，八种花牌各一张。",
                Group("同一种牌 / 四张五万", 15, 15, 15, 15)),
            Page("08 / 基础组合", "先认三个组合：对子、刻子、顺子", "两张相同是对子；三张相同是刻子。\n同一花色的三张连续数牌，才是顺子。",
                "两张二万是一对，三张七筒是一刻，四五六条是一顺。顺子必须同花色，而且不能把九、一、二接起来！这些是之后学习和牌的基础。", "认识组合只是第一步；是否能和牌，还要满足国标和牌条件。",
                Group("对子 / 两张相同", 12, 12), Group("刻子 / 三张相同", 27, 27, 27), Group("顺子 / 同花色连续", 34, 35, 36)),
            Page("09 / 下课前复习", "现在，你已经会认麻将牌了！", "数牌：万、筒、条。字牌：四风与三箭。\n数牌和字牌每种 4 张，花牌每种 1 张，一副共 144 张。",
                "做得好！再看一次：鸟是一条，白板也是字牌，花牌需要补牌。下次再学习摸牌与出牌。", "一副 144 张：数牌 108 张 · 字牌 28 张 · 花牌 8 张。",
                Group("一条 / 数牌", 31), Group("白板 / 字牌", 46), Group("春 / 花牌", 51))
        };
    }

    private static MahjongClassroomLesson.Page Page(string section, string title, string summary,
        string narration, string takeaway, params MahjongClassroomLesson.TileGroup[] groups) =>
        new MahjongClassroomLesson.Page { section = section, title = title, summary = summary,
            narration = narration, takeaway = takeaway, groups = groups };

    private static MahjongClassroomLesson.TileGroup Group(string heading, params int[] ids) =>
        new MahjongClassroomLesson.TileGroup { heading = heading, tileIds = ids };

    private static MahjongClassroomLesson.Tile[] CreateTileCatalog() {
        var tiles = new List<MahjongClassroomLesson.Tile>();
        string[] numbers = { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        string[] suits = { "万", "筒", "条" };
        for (int suit = 1; suit <= 3; suit++)
            for (int rank = 1; rank <= 9; rank++)
                tiles.Add(Tile(suit * 10 + rank, numbers[rank - 1] + suits[suit - 1]));
        string[] honors = { "东", "南", "西", "北", "红中", "白板", "发财" };
        for (int i = 0; i < honors.Length; i++) tiles.Add(Tile(41 + i, honors[i]));
        string[] flowers = { "春", "夏", "秋", "冬", "梅", "兰", "竹", "菊" };
        for (int i = 0; i < flowers.Length; i++) tiles.Add(Tile(51 + i, flowers[i]));
        return tiles.ToArray();
    }

    private static MahjongClassroomLesson.Tile Tile(int id, string label) => new MahjongClassroomLesson.Tile {
        id = id, label = label
    };

    private static TMP_FontAsset CreateFont(MahjongClassroomLesson lesson) {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (asset != null) return asset;
        var source = Require<Font>("Assets/font/思源黑体/SourceHanSansSC-Regular.otf");
        asset = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048,
            AtlasPopulationMode.Dynamic, true);
        asset.name = "Classroom Sans";
        string characters = JsonUtility.ToJson(lesson) +
            "琪露诺的麻雀教室国标麻将第课重新开始从头学习上一页下一页0123456789 /·<>";
        characters = new string(characters.Where(c => !char.IsControl(c)).Distinct().ToArray());
        if (!asset.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("课堂字体缺少字符：" + missing);
        AssetDatabase.CreateAsset(asset, FontPath);
        foreach (var atlas in asset.atlasTextures) {
            atlas.name = "Classroom Sans Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
        }
        asset.material.name = "Classroom Sans Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        return asset;
    }

    private static void ConfigurePortrait() {
        var importer = AssetImporter.GetAtPath(PortraitPath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("缺少琪露诺立绘：" + PortraitPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string path) {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static T Require<T>(string path) where T : UnityEngine.Object {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new InvalidOperationException("Missing " + typeof(T).Name + ": " + path);
        return asset;
    }

    private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h) {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static RectTransform Stretch(string name, Transform parent) {
        var rect = Rect(name, parent, 0, 0, 0, 0);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static UnityEngine.UI.Image Image(string name, Transform parent, float x, float y, float w, float h, Color color) {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, float x, float y, float w, float h,
        string text, float size, Color color, bool bold = false, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) {
        var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.fontSize = size;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.color = color;
        label.text = text;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }

    private static UnityEngine.UI.Button Button(string name, Transform parent, float x, float y,
        float w, float h, string text, Color color, Color textColor, float textSize) {
        var image = Image(name, parent, x, y, w, h, color);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(.84f, .94f, .96f);
        colors.pressedColor = new Color(.66f, .82f, .86f);
        colors.disabledColor = new Color(.8f, .8f, .8f, .45f);
        button.colors = colors;
        Text("Label", image.transform, 0, 0, w, h, text, textSize, textColor, false, TextAlignmentOptions.Center);
        return button;
    }
}
