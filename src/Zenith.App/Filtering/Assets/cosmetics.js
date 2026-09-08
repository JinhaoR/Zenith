(() => {
  // This is fixed application code, not a script downloaded from a filter list.
  if (!/^https?:$/.test(location.protocol)) return;
  const bridge = window.chrome?.webview;
  if (!bridge) return;
  const token = crypto.randomUUID();
  let page = location.href;
  let sheet, timer, dirty = true, stopped = false, received = false;
  const classes = new Set(), ids = new Set(), hrefs = new Set();
  const walks = [];
  const add = (set, value, limit, length) => {
    if (value && value.length <= length && set.size < limit && !set.has(value)) {
      set.add(value); dirty = true;
    }
  };
  const visit = element => {
    if (element.nodeType !== 1) return;
    add(ids, element.id, 512, 128);
    for (const value of element.classList) add(classes, value, 512, 128);
    if (element.localName === 'a') add(hrefs, element.getAttribute('href'), 128, 512);
  };
  const enqueue = root => {
    if (root.nodeType !== 1 || root === sheet) return;
    visit(root);
    if (walks.length >= 64) {
      walks.length = 0;
      root = document.documentElement;
    }
    walks.push(document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT));
  };
  const tick = () => {
    timer = undefined;
    if (stopped) return;
    if (page !== location.href) {
      page = location.href;
      classes.clear(); ids.clear(); hrefs.clear(); walks.length = 0;
      if (sheet) document.adoptedStyleSheets = document.adoptedStyleSheets.filter(value => value !== sheet);
      sheet = undefined; dirty = true; received = false;
      if (document.documentElement) enqueue(document.documentElement);
    }
    // Process bounded chunks, yielding between them even on very large pages.
    for (let budget = 500; budget > 0 && walks.length; budget--) {
      const element = walks[0].nextNode();
      if (element) visit(element); else walks.shift();
    }
    if (dirty || !received) {
      dirty = false;
      bridge.postMessage({ kind: 'zenith-cosmetics', token, url: page,
        classes: [...classes], ids: [...ids], hrefs: [...hrefs] });
    }
    timer = setTimeout(tick, 500);
  };
  bridge.addEventListener('message', event => {
    const data = event.data;
    if (data?.kind !== 'zenith-cosmetics' || data.token !== token || data.url !== location.href
        || typeof data.css !== 'string' || data.css.length > 2 * 1024 * 1024) return;
    if (!document.documentElement) return;
    if (!sheet) {
      sheet = new CSSStyleSheet();
      document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
    }
    // CSS is data; never interpolate filter output into executable JavaScript.
    sheet.replaceSync(data.css);
    received = true;
  });
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'attributes') visit(record.target);
      else for (const node of record.addedNodes) enqueue(node);
    }
  });
  observer.observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'id', 'href'] });
  if (document.documentElement) enqueue(document.documentElement);
  tick();
  addEventListener('pagehide', () => { stopped = true; clearTimeout(timer); observer.disconnect(); walks.length = 0; });
  addEventListener('pageshow', event => {
    if (!event.persisted) return;
    stopped = false; dirty = true;
    observer.observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'id', 'href'] });
    enqueue(document.documentElement); tick();
  });
})();
