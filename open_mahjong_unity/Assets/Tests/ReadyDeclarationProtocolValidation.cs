#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;

public static class ReadyDeclarationProtocolValidation {
    public static object Run() {
        var routes = new Dictionary<string, string> {
            { "GB", "riichi" }, { "riichi", "riichi" }, { "shanxi", "shanxi" },
            { "jiujiang", "jiujiang" }, { "changchun", "changchun" },
            { "shenyang", "shenyang" }, { "duocai", "duocai" }
        };
        foreach (var route in routes) {
            var manifest = new RuleManifest { OutboundChannel = route.Key };
            string actual = GameStateNetworkManager.ReadyDeclarationChannelFor(manifest);
            if (actual != route.Value)
                throw new InvalidOperationException($"报听通道 {route.Key}: {actual} != {route.Value}");
        }
        return new { checks = routes.Count, routes };
    }
}
#endif
