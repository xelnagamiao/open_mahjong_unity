using System.Collections.Generic;
using UnityEngine;

public static class DuocaiTileVisual {
    private static readonly Dictionary<int,Sprite> sprites=new Dictionary<int,Sprite>();
    private static readonly Dictionary<string,Sprite> colored=new Dictionary<string,Sprite>();
    public static bool IsCurrent(int tile)=>RuleRegistry.Current?.RuleId=="duocai"&&tile>=1000&&tile<5040;
    public static string Badge(int tile)=>tile>=1000&&tile<2000?"金":tile>=2000&&tile<3000?"银":tile>=3000&&tile<4000?"赤":null;
    public static Texture2D Texture(int tile)=>DuocaiTiles.Joker(tile)?Resources.Load<Texture2D>("Duocai/Tiles/"+tile):Sprite(tile,true)?.texture;
    public static Sprite Sprite(int tile,bool table=false) {
        if(!DuocaiTiles.Joker(tile))return ColoredSprite(tile,table);
        if(sprites.TryGetValue(tile,out var value)&&value)return value;
        var texture=Texture(tile);if(!texture)return null;
        return sprites[tile]=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
    }
    private static Sprite ColoredSprite(int tile,bool table) {
        var source=table?TileFaceResolver.LoadTableSprite(tile%1000):TileFaceResolver.LoadSprite(tile%1000);
        if(!source)return null;
        string key=tile+":"+source.GetInstanceID();
        if(colored.TryGetValue(key,out var result)&&result)return result;
        var shader=Resources.Load<Shader>("Duocai/InkColor");if(!shader)return source;
        var material=new Material(shader);var rect=source.textureRect;
        material.SetColor("_Ink",tile<2000?new Color(.72f,.48f,.06f):tile<3000?new Color(.42f,.48f,.55f):new Color(.82f,.06f,.1f));
        material.SetVector("_Rect",new Vector4(rect.x/source.texture.width,rect.y/source.texture.height,rect.width/source.texture.width,rect.height/source.texture.height));
        var target=RenderTexture.GetTemporary((int)rect.width,(int)rect.height,0,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        Graphics.Blit(source.texture,target,material);RenderTexture.active=target;
        var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply(false,true);
        RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.Destroy(material);
        return colored[key]=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),source.pixelsPerUnit,0,SpriteMeshType.FullRect);
    }
    public static Color BackColor(int back)=>back==0?new Color(.7f,.08f,.1f):new Color(.07f,.25f,.72f);
}
