mergeInto(LibraryManager.library, {
  // 页面模板在 Unity 加载前写入 window.CoolLandscape：
  //   { ready: true, mobile: bool, blocked: bool, state: 'portrait'|'landscape' }
  //
  // 返回：-1 = 页面桥接尚未就绪（Unity 侧保持阻断）；0 = 可以操作；1 = 竖屏，阻断操作。
  // 只读全局对象，无状态、不碰 heap，因此不需要 __deps。
  WebGLLandscapeGate_GetState: function () {
    if (typeof window === 'undefined' || !window.CoolLandscape || !window.CoolLandscape.ready) {
      return -1;
    }
    return window.CoolLandscape.blocked ? 1 : 0;
  },
});
