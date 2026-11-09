mergeInto(LibraryManager.library, {
  OMUMobileDisplayMetrics: function (values) {
    var result = [0, 0, 0, 0, 0, 0, 0, 0, 0];
    try {
      if (typeof wx !== 'undefined') {
        var info = wx.getWindowInfo ? wx.getWindowInfo() : wx.getSystemInfoSync();
        var w = info.windowWidth, h = info.windowHeight;
        var safe = info.safeArea || { left: 0, top: 0, right: w, bottom: h };
        var menu = wx.getMenuButtonBoundingClientRect ? wx.getMenuButtonBoundingClientRect() : null;
        result = [w, h, safe.left, safe.top, safe.right, safe.bottom,
          menu && menu.width > 0 ? menu.bottom : 0, info.pixelRatio || 1, 1];
      }
    } catch (error) {
      // Transient host startup keeps the native Unity safe area for this frame.
    }
    for (var i = 0; i < result.length; i++) HEAPF32[(values >> 2) + i] = Number(result[i]) || 0;
  }
});
