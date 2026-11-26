using System.Collections.Generic;

namespace OmUpdate {
    // Built into the APK: recovery must not depend on game assemblies or catalogs.
    public static class ContentBootLocalization {
        static readonly Dictionary<string, string[]> Messages = new Dictionary<string, string[]> {
            { "正在准备游戏…", new[] { "正在准备游戏…", "正在準備遊戲…", "Preparing the game…", "Préparation du jeu…", "Spiel wird vorbereitet…", "Подготовка игры…", "ゲームを準備中…", "Preparando el juego…" } },
            { "去官网下载", new[] { "去官网下载", "去官網下載", "Download from website", "Télécharger sur le site officiel", "Von offizieller Website laden", "Скачать с официального сайта", "公式サイトからダウンロード", "Descargar del sitio oficial" } },
            { "关闭游戏", new[] { "关闭游戏", "關閉遊戲", "Exit game", "Quitter le jeu", "Spiel beenden", "Закрыть игру", "ゲームを終了", "Cerrar el juego" } },
            { "游戏启动失败，请重新打开游戏；如仍失败，请重新安装。", new[] { "游戏启动失败，请重新打开游戏；如仍失败，请重新安装。", "遊戲啟動失敗，請重新開啟遊戲；如仍失敗，請重新安裝。", "Unable to start the game. Reopen it; if the problem persists, reinstall the app.", "Le jeu n’a pas pu démarrer. Rouvrez-le ; si le problème persiste, réinstallez-le.", "Das Spiel konnte nicht gestartet werden. Öffnen Sie es erneut; falls das Problem bestehen bleibt, installieren Sie es neu.", "Не удалось запустить игру. Откройте её снова; если ошибка повторится, переустановите игру.", "ゲームを起動できません。ゲームを開き直してください。解決しない場合は再インストールしてください。", "No se pudo iniciar el juego. Vuelve a abrirlo; si sigue fallando, reinstálalo." } },
        };

        public static string Text(string source, int language) {
            if (source == null) return "";
            if (!Messages.TryGetValue(source, out var values)) return source;
            return values[language >= 0 && language < values.Length ? language : 2];
        }
    }
}
