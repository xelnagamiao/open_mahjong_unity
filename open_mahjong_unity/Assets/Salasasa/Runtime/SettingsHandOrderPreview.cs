using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    public sealed class SettingsHandOrderPreview : MonoBehaviour
    {
        public static readonly int[] Example = {11,12,13,34,35,36,27,28,29,41,41,45,45};
        public RectTransform[] groups;
        public TMP_Text[] labels;
        public Image[] faces;
        public SettingsShapeGraphic surface;
        public int[] Hand { get; private set; } = (int[])Example.Clone();
        public int[] Dragons { get; private set; } = new[] {45,46,47};
        public int[] RiichiDragons { get; private set; } = new[] {45,46,47};
        public Image[] bodies = new Image[19];

        void OnEnable() { TileFaceResolver.OnPackChanged += Refresh; if (Application.isPlaying) Refresh(); }
        void OnDisable() { TileFaceResolver.OnPackChanged -= Refresh; }

        public void Refresh()
        {
            if (!Application.isPlaying) return;
            RefreshImages();
        }

#if UNITY_EDITOR
        public void RefreshForBake() => RefreshImages();
#endif
        void RefreshImages()
        {
            if (faces == null || faces.Length != 19 || bodies == null || bodies.Length != 19) return;
            Hand = (int[])Example.Clone();
            Dragons = new[] {45,46,47}; RiichiDragons = new[] {45,46,47};
            TileIdOrder.SortPreview(Hand);
            TileIdOrder.SortPreview(Dragons);
            TileIdOrder.SortPreview(RiichiDragons, true);
            for (int i=0;i<faces.Length;i++)
            {
                int id=i<13?Hand[i]:i<16?Dragons[i-13]:RiichiDragons[i-16];
                var root=(RectTransform)faces[i].transform.parent;
                if (bodies[i] == null) throw new InvalidOperationException("Settings preview must be baked before playback.");
                TileFaceFit.ApplyHandLayers(root,faces[i],ref bodies[i],id);
                faces[i].raycastTarget=false;
            }
        }

        public void Layout(float width, TMP_FontAsset font, Color ink, Color fill)
        {
            surface.Paint(fill,Color.clear,0,SettingsShapeGraphic.Shape.Round,4);
            float[] xs={16,width-340,width-166};
            float[] widths={width-380,158,150};
            for(int g=0;g<3;g++)
            {
                SettingsStyleCollection.Place(groups[g],xs[g],8,widths[g],92);
                SettingsStyleCollection.Place(labels[g].rectTransform,0,0,widths[g],24);
                labels[g].font=font;labels[g].fontSharedMaterial=font.material;labels[g].fontSize=17;labels[g].color=ink;
            }
            float tileWidth=Mathf.Min(38,(widths[0]-24)/13);
            for(int i=0;i<faces.Length;i++)
            {
                int slot=i<13?i:i<16?i-13:i-16;
                SettingsStyleCollection.Place((RectTransform)faces[i].transform.parent,slot*(tileWidth+2),28,tileWidth,52);
            }
            Refresh();
        }
    }
}
