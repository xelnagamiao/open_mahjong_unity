using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Bounded choice pages for rule actions with a physical/declared identity caption.</summary>
public static class ActionCandidatePager {
    public const int PageSize = 4;
    public static void Build(Transform parent, IReadOnlyList<ActionCandidate> choices, GameObject blockPrefab, GameObject cardPrefab, TMP_Text labelTemplate) {
        var root = new GameObject("ActionCandidatePages",typeof(RectTransform));
        root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
        var rect=(RectTransform)root.transform;
        float tileHeight=0f;
        int maxTiles=1;
        foreach(var choice in choices) {
            maxTiles=Mathf.Max(maxTiles,choice.TileIds.Length);
            foreach(int tileId in choice.TileIds) tileHeight=Mathf.Max(tileHeight,TileFaceFit.HandSizeFor(tileId,ActionBlock.TileWidth).y);
        }
        float cellWidth=Mathf.Max(324f,maxTiles*ActionBlock.TileWidth+(maxTiles-1)*ActionBlock.TileSpacing+32f);
        float width=cellWidth*2f+14f,cellHeight=34f+tileHeight+13f+6f;
        int pages=Mathf.Max(1,(choices.Count+PageSize-1)/PageSize),page=0;
        int rows=Mathf.Min(2,(choices.Count+1)/2);
        float height=rows*cellHeight+(pages>1?48f:0f);
        // The shared container grows around its centre. Keep the bottom of
        // these taller pages at the single-row baseline, above the action
        // buttons, so the open action can still be clicked to close it.
        float growthOffset=(height-(tileHeight+23f))*.5f;
        rect.sizeDelta=new Vector2(width,height);
        RectTransform Place(Transform target,float x,float y,float w,float h) {
            var r=(RectTransform)target;r.anchorMin=r.anchorMax=new Vector2(.5f,1f);r.pivot=new Vector2(.5f,1f);
            r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,-y);return r;
        }
        TMP_Text Text(Transform target,string value,float x,float y,float w,float h) {
            var label=Object.Instantiate(labelTemplate,target);label.name="CandidateLabel";label.text=value;
            label.raycastTarget=false;label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=22;
            label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Overflow;
            label.alignment=TextAlignmentOptions.Center;Place(label.transform,x,y,w,h);return label;
        }
        var body=new GameObject("Choices",typeof(RectTransform));body.layer=root.layer;body.transform.SetParent(root.transform,false);Place(body.transform,0,-growthOffset,width,rows*cellHeight);
        TMP_Text pageText=null;Button previous=null,next=null;
        void Render() {
            foreach(Transform child in body.transform){child.gameObject.SetActive(false);Object.Destroy(child.gameObject);}
            int end=Mathf.Min(choices.Count,(page+1)*PageSize);
            for(int i=page*PageSize;i<end;i++) {
                var choice=choices[i];var go=Object.Instantiate(blockPrefab,body.transform);go.name="Candidate_"+choice.TargetTile;
                foreach(var layout in go.GetComponents<LayoutGroup>())layout.enabled=false;
                foreach(var fitter in go.GetComponents<ContentSizeFitter>())fitter.enabled=false;
                int slot=i-page*PageSize;Place(go.transform,(slot%2==0?-1:1)*(cellWidth+14f)*.5f,(slot/2)*cellHeight,cellWidth,cellHeight-6);
                var block=go.GetComponent<ActionBlock>();block.actionType=choice.ActionType;block.targetTile=choice.TargetTile;
                Text(go.transform,choice.Caption,0,2,cellWidth-12,30);
                for(int t=0;t<choice.TileIds.Length;t++) {
                    var card=block.AddTile(cardPrefab,choice.TileIds[t]);
                    var cardRect=(RectTransform)card.transform;
                    Place(card.transform,(t-(choice.TileIds.Length-1)/2f)*(ActionBlock.TileWidth+ActionBlock.TileSpacing),34,ActionBlock.TileWidth,cardRect.rect.height);
                }
            }
            if(pageText)pageText.text=(page+1)+" / "+pages;
            if(previous)previous.interactable=page>0;if(next)next.interactable=page+1<pages;
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(parent as RectTransform);
        }
        Button Navigation(string name,string label,float x,int direction) {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.layer=root.layer;go.transform.SetParent(root.transform,false);
            Place(go.transform,x,rows*cellHeight+4-growthOffset,100,38);var image=go.GetComponent<Image>();image.color=new Color(.17f,.43f,.53f,.98f);
            var button=go.GetComponent<Button>();button.targetGraphic=image;Text(go.transform,label,0,0,96,38);
            button.onClick.AddListener(()=>{page=Mathf.Clamp(page+direction,0,pages-1);Render();});return button;
        }
        if(pages>1){previous=Navigation("PreviousCandidates","上一页",-145,-1);next=Navigation("NextCandidates","下一页",145,1);pageText=Text(root.transform,"",0,rows*cellHeight+4-growthOffset,120,38);}
        Render();
    }
}
