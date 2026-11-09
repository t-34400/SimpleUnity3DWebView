(function () {
  'use strict';
  // Host View loss must not clear a newer document/element which regained focus.
  if (document.hasFocus()) return false;
  var element = document.activeElement;
  for (var depth = 0; element && depth < 16; depth++) {
    if (element.shadowRoot && element.shadowRoot.activeElement) {
      element = element.shadowRoot.activeElement; continue;
    }
    if (element.tagName === 'IFRAME' || element.tagName === 'FRAME') {
      try { element = element.contentDocument && element.contentDocument.activeElement; }
      catch (_) { return false; } // No cross-origin access or frame-level blur.
      continue;
    }
    break;
  }
  if (!element || typeof element.blur !== 'function') return false;
  var editable = element.isContentEditable;
  if (!element.disabled && !element.readOnly) {
    editable = editable || element.tagName === 'TEXTAREA' ||
      (element.tagName === 'INPUT' &&
       ['text','search','url','tel','email','password','number'].indexOf(element.type) >= 0);
  }
  if (!editable) return false;
  element.blur(); // Normal blur/change semantics only. Never read or write text.
  return true;
})()
