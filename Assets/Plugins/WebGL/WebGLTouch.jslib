mergeInto(LibraryManager.library, {
  // 触屏特性检测（Task 6 修复轮）。一个纯函数：无状态、不碰 heap、不引用任何 $ 依赖，
  // 所以不需要 __deps（对比 WebGLWebSocket.jslib 的 $WebSocketNetState 陷阱）。
  //
  // ⚠️ **守卫范围（必须如实说明）**：本函数只在 **WebGL 出包**时才被 emscripten 编进产物
  // （C# 侧是 `#if UNITY_WEBGL && !UNITY_EDITOR` 的 DllImport）。编辑器里——包括本项目的
  // EditMode/PlayMode 测试——走的是 C# 的 `Input.touchSupported` 分支，**本函数的正确性
  // 在本地按构造无法验证**，只能靠出包后在真机/浏览器验（T8 安排）。
  //
  // 两条判据取或，各自都有失效面，所以都要：
  //   - navigator.maxTouchPoints：iPad 默认"桌面视图"下为 5，可用；但很老的浏览器可能没有该属性。
  //   - 'ontouchend' in document：Safari/Chrome 全系可用。
  // 已知代价：**触屏笔记本会命中 'ontouchend' → 被误判为移动端**。多认的后果只是多显示一层
  // 触控 UI（可点，但用户多半在用鼠标）；判定不对时用 MobileInputBinder 的人工档位或
  // URL `?touch=0` 覆盖 —— 那是唯一可靠的入口。
  WebGLTouch_HasTouch: function () {
    return (navigator.maxTouchPoints > 0 || 'ontouchend' in document) ? 1 : 0;
  },
});
