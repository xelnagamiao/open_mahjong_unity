#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

public static class SelfStatusBannerValidation {
    public static string Run() {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required");
        var canvas = GameCanvas.Instance;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        GameObject Root(string field) => (GameObject)typeof(GameCanvas).GetField(field, flags).GetValue(canvas);
        var roots = new[] { Root("selfFuritenIndicator"), Root("langyongWaveIndicator"), Root("selfShunheIndicator") };
        var checks = new List<string>();
        bool automaticWasActive = AutoAction.Instance.gameObject.activeSelf;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        void Layout() {
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)roots[0].transform.parent);
            roots[0].GetComponentInParent<SelfStatusStack>().RefreshPosition();
            Canvas.ForceUpdateCanvases();
        }
        void CheckBanner(GameObject root, string name) {
            Layout();
            var banner = root.GetComponent<SelfStatusBanner>();
            var rect = (RectTransform)root.transform;
            Check(root.activeInHierarchy && rect.rect.width > 0 && rect.rect.height > 0, name + ": visible geometry");
            foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>()) {
                var eventCamera = graphic.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : graphic.canvas.worldCamera;
                var center = RectTransformUtility.WorldToScreenPoint(eventCamera, graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
                Check(!graphic.raycastTarget || !graphic.Raycast(center, eventCamera), name + ": display does not intercept input " + graphic.name);
            }
            var label = root.GetComponentInChildren<TMP_Text>();
            label.ForceMeshUpdate();
            var glyphs = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c=>c.isVisible).ToArray();
            float glyphTop = glyphs.Max(c=>label.rectTransform.TransformPoint(c.topLeft).y);
            float glyphBottom = glyphs.Min(c=>label.rectTransform.TransformPoint(c.bottomLeft).y);
            float rowCenter = rect.TransformPoint(rect.rect.center).y;
            Check(Mathf.Abs((glyphTop+glyphBottom)*0.5f-rowCenter)<1f, name + ": visible text vertically centered");
            Check(label.preferredWidth <= label.rectTransform.rect.width + 1f, name + ": full text fits");
            Check(label.preferredHeight <= label.rectTransform.rect.height + 1f, name + ": text height fits");
            Check(label.rectTransform.anchoredPosition.x + label.preferredWidth <= banner.ContentWidth, name + ": solid background covers label");
            foreach (var card in root.GetComponentsInChildren<StaticCard>()) {
                var cr = (RectTransform)card.transform;
                Check(cr.anchoredPosition.x + cr.rect.width <= banner.ContentWidth, name + ": solid background covers tile");
                Check(cr.rect.height <= rect.rect.height, name + ": tile fits height");
                var tileCorners = new Vector3[4];
                cr.GetWorldCorners(tileCorners);
                Check(Mathf.Abs((tileCorners.Max(p=>p.y)+tileCorners.Min(p=>p.y))*0.5f-rowCenter)<1f, name + ": tile vertically centered");
            }
            using (var vh = new UnityEngine.UI.VertexHelper()) {
                root.GetComponent<SelfStatusFade>().ModifyMesh(vh);
                var vertices = new List<UIVertex>(); vh.GetUIVertexStream(vertices);
                Check(vertices.Count > 0, name + ": rendered gradient mesh");
                float left = vertices.Min(v => v.position.x), right = vertices.Max(v => v.position.x);
                Check(vertices.Where(v => Mathf.Abs(v.position.x-right)<0.1f).All(v => v.color.a==0), name + ": transparent right edge");
                Check(vertices.Where(v => v.position.x <= left + banner.ContentWidth + 0.1f).All(v => v.color.a>=230), name + ": main color remains behind content");
            }
            var rowCorners = new Vector3[4]; rect.GetWorldCorners(rowCorners);
            var autoCorners = new Vector3[4]; AutoAction.Instance.SidebarFooter.GetWorldCorners(autoCorners);
            if (AutoAction.Instance.gameObject.activeInHierarchy)
                Check(rowCorners.Max(p=>p.y) < autoCorners.Min(p=>p.y), name + ": below auto-action panel");
            var rowCanvas = root.GetComponentInParent<Canvas>();
            var camera = rowCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rowCanvas.worldCamera;
            var screenCorners = rowCorners.Select(p=>RectTransformUtility.WorldToScreenPoint(camera,p)).ToArray();
            Check(screenCorners.All(p=>p.x>=-1 && p.x<=Screen.width+1 && p.y>=-1 && p.y<=Screen.height+1), name + ": fully within screen");
        }
        try {
            canvas.ClearSelfStatusIndicators();
            Check(roots.All(r => !r.activeSelf), "reset clears every status");
            canvas.SetSelfBaoIndicator(false, 15, false, false);
            Check(!roots[2].activeSelf, "unseen bao payload cannot reveal a tile");
            foreach (int id in new[] { 11, 15, 29, 31, 41, 46, 47 }) {
                canvas.SetSelfBaoIndicator(true, id, false, false);
                Check(roots[2].GetComponent<SelfStatusBanner>().TileId==id, "authorized bao tile " + id);
                CheckBanner(roots[2], "bao " + id);
            }
            Check(roots[2].GetComponent<UnityEngine.UI.Image>().color == roots[1].GetComponent<UnityEngine.UI.Image>().color, "bao and wave share blue theme");
            int children = roots[2].GetComponentsInChildren<Transform>(true).Length;
            for (int i=0; i<4; i++) canvas.SetSelfBaoIndicator(true, 47, false, false);
            Check(roots[2].GetComponentsInChildren<Transform>(true).Length==children, "repeated refresh reuses tile");
            var state = new ChangchunGameState();
            var info = typeof(ChangchunGameState).GetProperty("Info");
            foreach (string phase in new[] { "before_draw", "normal", "final_four" }) {
                info.SetValue(state, new ChangchunInfo { bao_visible=true, bao_tile=15, window=phase });
                state.RefreshSelfStatusIndicators();
                Check(roots[2].activeSelf && roots[2].GetComponent<SelfStatusBanner>().TileId==15, "live state retains known bao " + phase);
                CheckBanner(roots[2], phase);
            }
            info.SetValue(state, new ChangchunInfo { bao_visible=false, bao_tile=0, window="normal" });
            state.RefreshSelfStatusIndicators();
            Check(!roots[2].activeSelf && roots[2].GetComponent<SelfStatusBanner>().TileId==0, "change/unauthorized view clears stale bao");
            canvas.SetSelfBaoIndicator(false, 0, true, false);
            CheckBanner(roots[2], "no bao");
            Check(roots[2].GetComponent<SelfStatusBanner>().TileId==0, "exhausted hides tile");
            canvas.SetSelfBaoIndicator(false, 0, false, true);
            CheckBanner(roots[2], "final four without visible bao");
            canvas.ClearSelfStatusIndicators();
            canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotFuriten, true);
            canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotLangyongWave, true);
            CheckBanner(roots[0], "furiten"); CheckBanner(roots[1], "wave");
            Color red=roots[0].GetComponent<UnityEngine.UI.Image>().color, blue=roots[1].GetComponent<UnityEngine.UI.Image>().color;
            Check(red.r>red.b && blue.b>blue.r, "red furiten and blue wave remain distinct");
            var a=new Vector3[4];var b=new Vector3[4];
            ((RectTransform)roots[0].transform).GetWorldCorners(a);((RectTransform)roots[1].transform).GetWorldCorners(b);
            Check(b.Max(p=>p.y) < a.Min(p=>p.y), "simultaneous statuses do not overlap");
            canvas.ClearSelfStatusIndicators();
            canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe, true, "顺和:12番");
            CheckBanner(roots[2], "shared custom text");
            Check(roots[2].GetComponent<SelfStatusBanner>().TileId==0, "shared slot does not retain bao tile");
            var sharedColor=roots[2].GetComponent<UnityEngine.UI.Image>().color;
            Check(sharedColor.r>sharedColor.b, "ordinary shared status restores its own theme");
            state.OnSessionReset();
            Check(roots.All(r=>!r.activeSelf), "session reset clears statuses");
            AutoAction.Instance.gameObject.SetActive(false);
            canvas.ClearSelfStatusIndicators();
            canvas.SetSelfBaoIndicator(true, 47, false, true);
            CheckBanner(roots[2], "replay two-line bao without auto-action panel");
            canvas.ClearSelfStatusIndicators();
            canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotFuriten, true);
            canvas.SetSelfStatusIndicator(GameCanvas.StatusSlotLangyongWave, true);
            CheckBanner(roots[0], "replay furiten without auto-action panel");
            CheckBanner(roots[1], "replay wave without auto-action panel");
            return JsonConvert.SerializeObject(new {passed=checks.Count,screen=new{Screen.width,Screen.height},checks});
        } finally {
            canvas.ClearSelfStatusIndicators();
            AutoAction.Instance.gameObject.SetActive(automaticWasActive);
        }
    }
}
#endif
