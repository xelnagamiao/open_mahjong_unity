mergeInto(LibraryManager.library, {
    PageVisibility_Setup: function() {
        if (typeof document === 'undefined') return;

        // 避免重复注册
        if (document.__omu_visibility_listener_added) return;
        document.__omu_visibility_listener_added = true;

        document.addEventListener('visibilitychange', function () {
            var isVisible = !document.hidden;
            if (typeof unityInstance !== 'undefined' && unityInstance !== null) {
                // 目标 GameObject 由 C# 侧保证命名为 GlobalConfig
                unityInstance.SendMessage('GlobalConfig', 'OnApplicationVisibilityChanged', isVisible ? 1 : 0);
            }
        });
    }
});


