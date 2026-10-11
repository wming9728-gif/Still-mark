// InkPage preview page. Receives rendered HTML from the host and handles display-only concerns:
// code highlighting, scroll mapping, in-page find and link interception. It never edits the source text.
(function () {
  "use strict";

  const host = window.chrome && window.chrome.webview;
  const content = document.getElementById("content");
  const baseEl = document.getElementById("base");
  const post = (msg) => { if (host) host.postMessage(msg); };

  // Languages tried when a fenced block has no language tag (keeps auto-detection fast and sane).
  const AUTO_LANGS = ["c", "cpp", "csharp", "java", "python", "javascript", "typescript", "json", "bash", "powershell",
    "sql", "xml", "css", "go", "rust", "yaml", "ini", "kotlin", "php", "ruby", "lua", "makefile", "cmake", "dockerfile"];
  const hlCache = new Map();
  let lastHtml = null;
  // UI texts come from the host in the current language ("strings" message); these are only fallbacks.
  const strings = { emptyTitle: "Empty document", emptyHint: "", imageBroken: "Image failed to load: " };

  if (window.hljs) hljs.configure({ ignoreUnescapedHTML: true, throwUnescapedHTML: false });

  function highlight(codeEl) {
    const cls = Array.from(codeEl.classList).find(c => c.startsWith("language-"));
    let lang = cls ? cls.slice(9).toLowerCase() : "";
    const text = codeEl.textContent;
    if (!window.hljs || text.length > 200000) return;
    if (lang && !hljs.getLanguage(lang)) return; // unknown tag: leave as plain text
    if (["text", "plain", "plaintext", "txt", "nohighlight"].includes(lang)) return;
    const key = lang + "\u0000" + text;
    let out = hlCache.get(key);
    if (out === undefined) {
      try {
        if (lang) out = hljs.highlight(text, { language: lang, ignoreIllegals: true }).value;
        else {
          const r = text.length < 20000 ? hljs.highlightAuto(text, AUTO_LANGS) : null;
          out = r && r.relevance >= 3 ? r.value : null;
        }
      } catch (e) { out = null; }
      if (hlCache.size > 500) hlCache.clear();
      hlCache.set(key, out);
    }
    if (out) { codeEl.innerHTML = out; codeEl.classList.add("hljs"); }
  }

  function decorate() {
    content.querySelectorAll("table").forEach(t => {
      if (t.parentElement && t.parentElement.classList.contains("tbl")) return;
      const wrap = document.createElement("div");
      wrap.className = "tbl";
      t.parentNode.insertBefore(wrap, t);
      wrap.appendChild(t);
    });
    content.querySelectorAll("pre > code").forEach(highlight);
    content.querySelectorAll("li.task-list-item").forEach(li => li.parentElement && li.parentElement.classList.add("contains-task-list"));
    // Task boxes are display-only (clicks are swallowed below); keep them enabled so they use the accent colour.
    content.querySelectorAll("li.task-list-item input[type=checkbox]").forEach(cb => { cb.disabled = false; cb.tabIndex = -1; cb.setAttribute("aria-readonly", "true"); });
    content.querySelectorAll("img").forEach(img => { img.loading = "lazy"; img.decoding = "async"; });
  }

  // Broken images: show a visible placeholder instead of a silent gap. (error does not bubble → capture phase)
  document.addEventListener("error", (e) => {
    const img = e.target;
    if (!(img instanceof HTMLImageElement) || img.dataset.broken) return;
    img.dataset.broken = "1";
    const box = document.createElement("span");
    box.className = "img-broken";
    box.dataset.src = img.getAttribute("src") || "";
    box.textContent = strings.imageBroken + box.dataset.src;
    if (img.alt) box.title = img.alt;
    img.replaceWith(box);
  }, true);

  // ---------- rendering ----------
  function renderEmpty() {
    const box = document.createElement("div");
    box.className = "empty";
    const b = document.createElement("b");
    b.textContent = strings.emptyTitle;
    box.append(b, document.createElement("br"), strings.emptyHint);
    content.replaceChildren(box);
  }

  function applyStrings(msg) {
    Object.assign(strings, { emptyTitle: msg.emptyTitle, emptyHint: msg.emptyHint, imageBroken: msg.imageBroken });
    if (content.querySelector(":scope > .empty")) renderEmpty();
    content.querySelectorAll(".img-broken").forEach(box => { box.textContent = strings.imageBroken + (box.dataset.src || ""); });
  }

  function render(msg) {
    if (msg.baseHref) baseEl.href = msg.baseHref;
    if (msg.title) document.title = msg.title;
    const anchor = msg.keepScroll === false ? null : currentLine();
    if (msg.html !== lastHtml) {
      lastHtml = msg.html;
      if (!msg.html.trim()) {
        renderEmpty();
      } else {
        content.innerHTML = msg.html;
        decorate();
      }
      lineIndex = null;
      if (findState.query) runFind(findState.query, findState.caseSensitive, 0, true);
    }
    if (typeof msg.endLine === "number" && msg.endLine !== endLine) { endLine = msg.endLine; lineIndex = null; }
    if (typeof msg.scrollLine === "number") scrollToLine(msg.scrollLine, false);
    else if (anchor != null && msg.keepScroll !== false) scrollToLine(anchor, false);
    else if (msg.keepScroll === false) window.scrollTo(0, 0);
  }

  // ---------- source line mapping ----------
  let lineIndex = null; // [{line, el}] in document order; el null = end of the document
  let endLine = null;   // source line just past the last non-empty line (sent by the host)
  function getIndex() {
    if (!lineIndex) {
      lineIndex = Array.from(content.querySelectorAll("[data-line]"))
        .map(el => ({ line: +el.getAttribute("data-line"), el }))
        .filter(x => !isNaN(x.line));
      // The end of the document is an anchor too: without it, everything inside a tall last block (a long code
      // block at the end, say) would map to that block's first line and the two sides would stop following.
      const last = lineIndex[lineIndex.length - 1];
      if (last && endLine != null && endLine > last.line) lineIndex.push({ line: endLine, el: null });
    }
    return lineIndex;
  }
  const topOf = (el) => el.getBoundingClientRect().top + window.scrollY;
  const yOf = (x) => x.el ? topOf(x.el) : content.getBoundingClientRect().bottom + window.scrollY;

  // Fractional source line at the top of the viewport.
  function currentLine() {
    const idx = getIndex();
    if (!idx.length) return null;
    const y = window.scrollY + 16;
    let prev = null;
    for (const x of idx) {
      const t = yOf(x);
      if (t > y) {
        if (!prev) return window.scrollY < 4 ? 0 : x.line;
        const pt = yOf(prev);
        const f = t > pt ? (y - pt) / (t - pt) : 0;
        return prev.line + f * Math.max(0, x.line - prev.line);
      }
      prev = x;
    }
    if (!prev.el) return prev.line;
    const pt = topOf(prev.el), h = Math.max(1, prev.el.offsetHeight);
    return prev.line + Math.min(1, (y - pt) / h);
  }

  let programmaticUntil = 0;
  function scrollToLine(line, smooth) {
    const idx = getIndex();
    if (!idx.length) return;
    let prev = idx[0], next = null;
    for (const x of idx) {
      if (x.line <= line) prev = x; else { next = x; break; }
    }
    let y = yOf(prev);
    if (next && next.line > prev.line && line > prev.line) {
      y += (yOf(next) - y) * Math.min(1, (line - prev.line) / (next.line - prev.line));
    }
    // At the very top keep the sheet's own top padding visible.
    if (prev === idx[0] && line <= prev.line + 0.05) y = 0; else y -= 16;
    programmaticUntil = Date.now() + 250;
    window.scrollTo({ top: Math.max(0, y), behavior: smooth ? "smooth" : "auto" });
  }

  let scrollTimer = 0;
  window.addEventListener("scroll", () => {
    if (Date.now() < programmaticUntil) return;
    clearTimeout(scrollTimer);
    scrollTimer = setTimeout(() => {
      const l = currentLine();
      if (l != null) post({ type: "scroll", line: l });
    }, 30);
  }, { passive: true });

  function scrollToId(id) {
    const el = document.getElementById(id) || document.getElementsByName(id)[0];
    if (el) { programmaticUntil = Date.now() + 400; el.scrollIntoView({ block: "start" }); }
  }

  // ---------- links ----------
  document.addEventListener("click", (e) => {
    const a = e.target.closest && e.target.closest("a[href]");
    if (!a) return;
    e.preventDefault();
    const raw = a.getAttribute("href");
    if (raw.startsWith("#")) { scrollToId(decodeURIComponent(raw.slice(1))); return; }
    post({ type: "link", href: a.href, raw });
  });
  // Task checkboxes are display-only; the source is the truth.
  document.addEventListener("click", (e) => {
    if (e.target instanceof HTMLInputElement && e.target.type === "checkbox") e.preventDefault();
  }, true);

  // ---------- dropping files onto the preview ----------
  // WebView2 is its own OLE drop target, so the host window never sees these drops. Hand the File objects to
  // the host (WebView2 resolves them to real paths) instead of letting the browser try to navigate.
  const hasFiles = (e) => e.dataTransfer && Array.from(e.dataTransfer.types || []).includes("Files");
  document.addEventListener("dragover", (e) => { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; } });
  document.addEventListener("drop", (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    if (host && host.postMessageWithAdditionalObjects && e.dataTransfer.files.length)
      host.postMessageWithAdditionalObjects({ type: "dropFiles" }, e.dataTransfer.files);
  });

  // ---------- find (CSS Custom Highlight API: no DOM mutation, works across inline elements) ----------
  const findState = { query: "", caseSensitive: false, ranges: [], index: -1 };
  function clearFind() {
    if (window.CSS && CSS.highlights) { CSS.highlights.delete("ink-find"); CSS.highlights.delete("ink-find-current"); }
    findState.ranges = []; findState.index = -1;
  }
  function runFind(query, caseSensitive, direction, keepIndex) {
    const prevIndex = findState.index;
    const sameQuery = query === findState.query && caseSensitive === findState.caseSensitive && findState.ranges.length;
    if (!sameQuery || keepIndex) {
      clearFind();
      findState.query = query; findState.caseSensitive = caseSensitive;
      if (!query) { report(); return; }
      const nodes = [], starts = [];
      let text = "";
      const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
      for (let n = walker.nextNode(); n; n = walker.nextNode()) { starts.push(text.length); nodes.push(n); text += n.nodeValue; }
      const hay = caseSensitive ? text : text.toLowerCase();
      const needle = caseSensitive ? query : query.toLowerCase();
      const locate = (pos) => { // binary search node containing pos
        let lo = 0, hi = starts.length - 1;
        while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (starts[mid] <= pos) lo = mid; else hi = mid - 1; }
        return lo;
      };
      for (let i = hay.indexOf(needle); i >= 0 && findState.ranges.length < 5000; i = hay.indexOf(needle, i + Math.max(1, needle.length))) {
        const a = locate(i), b = locate(i + needle.length - 1);
        const r = new Range();
        r.setStart(nodes[a], i - starts[a]);
        r.setEnd(nodes[b], i + needle.length - starts[b]);
        findState.ranges.push(r);
      }
      if (window.CSS && CSS.highlights && findState.ranges.length) CSS.highlights.set("ink-find", new Highlight(...findState.ranges));
      if (keepIndex) {
        findState.index = findState.ranges.length ? Math.min(Math.max(prevIndex, 0), findState.ranges.length - 1) : -1;
        paintCurrent(false);
        report();
        return;
      }
      findState.index = -1;
      direction = direction || 1;
    }
    const n = findState.ranges.length;
    if (n) {
      if (findState.index < 0) {
        // first hit at or below the current viewport
        const y = 0;
        let i = findState.ranges.findIndex(r => r.getBoundingClientRect().top >= y);
        findState.index = direction < 0 ? (i <= 0 ? n - 1 : i - 1) : (i < 0 ? 0 : i);
      } else if (direction) {
        findState.index = (findState.index + direction + n) % n;
      }
      paintCurrent(true);
    }
    report();
  }
  function paintCurrent(scroll) {
    const r = findState.ranges[findState.index];
    if (!r) return;
    if (window.CSS && CSS.highlights) CSS.highlights.set("ink-find-current", new Highlight(r));
    if (scroll) {
      const rect = r.getBoundingClientRect();
      if (rect.top < 40 || rect.bottom > window.innerHeight - 40) {
        programmaticUntil = Date.now() + 250;
        window.scrollBy({ top: rect.top - window.innerHeight / 3 });
      }
    }
  }
  function report() { post({ type: "findResult", count: findState.ranges.length, index: findState.index }); }

  // ---------- host messages ----------
  function onMessage(msg) {
    switch (msg.type) {
      case "render": render(msg); break;
      case "theme": document.documentElement.dataset.theme = msg.theme; break;
      case "strings": applyStrings(msg); break;
      case "layout": document.body.classList.toggle("compact", !!msg.compact); break;
      case "ligatures": document.body.classList.toggle("ligatures", !!msg.on); break;
      case "scrollToLine": scrollToLine(msg.line, !!msg.smooth); break;
      case "scrollToId": scrollToId(msg.id); break;
      case "find": runFind(msg.query || "", !!msg.caseSensitive, msg.direction || 0, false); break;
      case "findClear": findState.query = ""; clearFind(); report(); break;
      case "fontScale": document.getElementById("doc").style.fontSize = (16 * msg.scale) + "px"; break;
    }
  }
  if (host) host.addEventListener("message", (e) => onMessage(e.data));
  window.__ink = { onMessage, currentLine };

  post({ type: "ready" });
})();
