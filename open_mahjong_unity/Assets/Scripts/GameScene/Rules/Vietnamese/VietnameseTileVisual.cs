using System.Collections.Generic;
using UnityEngine;
public static class VietnameseTileVisual {
    private static readonly Dictionary<int,Sprite> sprites=new Dictionary<int,Sprite>();
    private static readonly string[] Names={"总","筒","索","万","大花","喜","元","合"};
    public static bool IsExtra(int tile)=>(tile>=59 && tile<=66)||(tile>=71 && tile<=78);
    // Other regional rules reuse these physical IDs for different flowers/animals.
    public static bool IsCurrentRuleTile(int tile)=>IsExtra(tile) && RuleRegistry.Current?.RuleId=="vietnamese";
    public static string Name(int tile) {
        if(tile>=71 && tile<=78)return Names[tile-71];
        if(tile>=59 && tile<=62)return $"王{tile-58}";
        if(tile>=63 && tile<=66)return $"后{tile-62}";
        if(tile>=51 && tile<=54)return $"季{tile-50}";
        if(tile>=55 && tile<=58)return $"花{tile-54}";
        if(tile<40)return $"{tile%10}{(tile<20?"万":tile<30?"筒":"索")}";
        return tile>=41 && tile<=47?new[]{"东","南","西","北","中","发","白"}[tile-41]:tile.ToString();
    }
    public static Texture2D Texture(int tile)=>Resources.Load<Texture2D>("Vietnamese/Tiles/"+tile);
    public static Sprite Sprite(int tile) {
        if(sprites.TryGetValue(tile,out var value) && value)return value;
        var texture=Texture(tile);if(!texture)return null;
        value=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
        sprites[tile]=value;return value;
    }
}
