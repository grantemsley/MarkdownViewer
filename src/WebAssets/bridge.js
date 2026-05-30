// MarkdownViewer renderer bridge.
// Receives setDoc / setPrefs / scrollToHeading messages from the WPF shell
// and posts headings / openLink / requestExternal messages back.

(function () {
  "use strict";

  const $ = (sel) => document.querySelector(sel);
  const page = $("#page");
  const scroll = $("#scroll");
  const breadcrumb = $("#breadcrumb");
  const rawframe = $("#rawframe");

  function hideRaw() {
    if (rawframe && !rawframe.hidden) {
      rawframe.hidden = true;
      // Don't reset iframe.src — leaving the previously-loaded doc in
      // memory keeps the renderer process warm so the next setRaw to a
      // same-origin URL doesn't pay cold-start cost.
    }
    if (scroll) scroll.hidden = false;
  }

  // ─── Lazy loaders ────────────────────────────────────────────────────
  // highlight.js and mermaid used to load eagerly in render.html's <head>
  // (~125 KB + 3.3 MB), which delayed the WebView's "ready" event by
  // hundreds of ms even when the document had no code or diagrams. Both
  // now load on first use.

  let _hljsLoading = null;
  function ensureHljs() {
    if (window.hljs) return Promise.resolve(window.hljs);
    if (_hljsLoading) return _hljsLoading;
    _hljsLoading = new Promise((resolve, reject) => {
      const s = document.createElement("script");
      s.src = "lib/highlight/highlight.min.js";
      // Clear the cached promise on failure so the next caller retries
      // instead of getting back the same rejected promise forever.
      s.onload = () => {
        if (!window.hljs) { _hljsLoading = null; reject(new Error("hljs missing after load")); return; }
        resolve(window.hljs);
      };
      s.onerror = (e) => { _hljsLoading = null; reject(e); };
      document.head.appendChild(s);
    });
    return _hljsLoading;
  }

  function highlightInPage(root) {
    if (!window.hljs) return;
    root.querySelectorAll("pre code").forEach((block) => {
      if (block.closest(".mermaid")) return;
      if (block.dataset.hlDone === "1") return;
      try { window.hljs.highlightElement(block); block.dataset.hlDone = "1"; } catch { }
    });
  }

  const MERMAID_BUNDLED = "lib/mermaid/mermaid.min.js";
  const MERMAID_CDN =
    "https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js";

  // mermaid 10.x calls structuredClone, which is absent on older WebView2
  // runtimes. Provide a minimal polyfill so diagrams render even when the
  // embedded runtime predates it (the config it clones is plain JSON).
  if (typeof window.structuredClone !== "function") {
    window.structuredClone = (o) =>
      o === undefined ? o : JSON.parse(JSON.stringify(o));
  }

  function loadScript(src) {
    return new Promise((resolve, reject) => {
      const s = document.createElement("script");
      s.src = src;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error("failed to load " + src));
      document.head.appendChild(s);
    });
  }

  function initMermaid() {
    const dark = document.body.classList.contains("theme-dark");
    window.mermaid.initialize({
      startOnLoad: false,
      theme: dark ? "dark" : "default",
      securityLevel: "strict",
    });
  }

  // Load the bundled (offline) mermaid once; cached across renders.
  let _mermaidBundled = null;
  function ensureMermaid() {
    if (window.mermaid) return Promise.resolve(window.mermaid);
    if (_mermaidBundled) return _mermaidBundled;
    _mermaidBundled = loadScript(MERMAID_BUNDLED).then(() => {
      if (!window.mermaid) throw new Error("mermaid missing after load");
      initMermaid();
      return window.mermaid;
    }).catch((e) => {
      // Drop the cached promise so a later render can retry the bundled
      // load instead of being stuck on the network CDN fallback forever.
      _mermaidBundled = null;
      throw e;
    });
    return _mermaidBundled;
  }

  // Render diagrams resiliently. Try the bundled build; if any diagram is
  // left unrendered — load failed, or run() threw in this runtime — retry via
  // the CDN build. Anything still failing shows the real error inline rather
  // than leaving raw source visible, so failures are diagnosable not silent.
  let _mermaidCdnLoaded = false;
  async function renderMermaid(nodes) {
    let lastErr = null;
    try {
      const m = await ensureMermaid();
      await m.run({ nodes });
    } catch (e) { lastErr = e; }

    let failed = nodes.filter((n) => !n.querySelector("svg"));
    if (failed.length > 0) {
      try {
        if (!_mermaidCdnLoaded) { await loadScript(MERMAID_CDN); _mermaidCdnLoaded = true; }
        initMermaid();
        failed.forEach((n) => n.removeAttribute("data-processed"));
        await window.mermaid.run({ nodes: failed });
      } catch (e) { lastErr = e; }
    }

    failed = nodes.filter((n) => !n.querySelector("svg"));
    failed.forEach((n) => {
      if (n.querySelector(".mermaid-error")) return;
      const note = document.createElement("div");
      note.className = "mermaid-error";
      note.textContent =
        "⚠ Diagram failed to render" +
        (lastErr ? ": " + (lastErr.message || lastErr) : ".");
      n.prepend(note);
    });
  }

  // ─── Prefs application ───────────────────────────────────────────────
  // Custom-tag handling (surfacing non-standard tags like <example>) is done
  // in MarkdownService on the C# side, not here: passing raw unknown tags to
  // the browser builds a malformed DOM. See MarkdownService.NeutralizeCustomTags.
  let bodyStyle = "win11"; // remembered so setMarkdown knows whether to wrap

  function applyPrefs(p) {
    if (!p) return;
    const body = document.body;
    if (p.theme === "dark") body.classList.add("theme-dark");
    else body.classList.remove("theme-dark");

    // Syntax-highlight theme follows app theme.
    const hlTheme = document.getElementById("hl-theme");
    if (hlTheme) {
      const dark = body.classList.contains("theme-dark");
      hlTheme.href = dark
        ? "lib/highlight/styles/github-dark.min.css"
        : "lib/highlight/styles/github.min.css";
    }

    // Body style: pick between the existing Win11 token-based reader and
    // the GitHub stylesheet (separate light/dark variants — the auto file
    // uses prefers-color-scheme and wouldn't follow our explicit theme).
    bodyStyle = p.bodyStyle === "github" ? "github" : "win11";
    body.classList.toggle("md-style-github", bodyStyle === "github");
    body.classList.toggle("md-style-win11", bodyStyle !== "github");
    const ghStyle = document.getElementById("gh-style");
    if (ghStyle) {
      if (bodyStyle === "github") {
        const dark = body.classList.contains("theme-dark");
        ghStyle.href = dark
          ? "lib/github-markdown/github-markdown-dark.css"
          : "lib/github-markdown/github-markdown-light.css";
      } else {
        ghStyle.removeAttribute("href");
      }
    }

    // Accent color (pushed by WPF from the system accent). Exposed as a CSS
    // var so reader.css picks it up; --accent already styles links and the
    // "reloaded" flash.
    if (p.accent) {
      document.documentElement.style.setProperty("--accent", p.accent);
    }

    document.documentElement.style.setProperty("--page-pct", (p.marginPct ?? 85) + "%");
    document.documentElement.style.setProperty("--base-size", (p.fontSize ?? 14) + "px");
    document.documentElement.style.setProperty("--lineno-gutter",
      p.showLineNumbers ? "3em" : "0px");
    if (p.showLineNumbers) body.classList.add("line-numbers");
    else body.classList.remove("line-numbers");

    // Typeface
    const FONTS = {
      system: 'var(--font)',
      sans:   '"Inter","Helvetica Neue",Arial,sans-serif',
      serif:  '"Charter","Iowan Old Style",Georgia,serif',
      mono:   'var(--mono)',
    };
    page.style.fontFamily = FONTS[p.typeface] || FONTS.system;

    // Re-init so an already-loaded mermaid picks up the theme change.
    if (window.mermaid) {
      try { initMermaid(); } catch (e) { /* ignore */ }
    }
  }

  // ─── Doc rendering ───────────────────────────────────────────────────
  function setBreadcrumb(pathStr) {
    breadcrumb.innerHTML = "";
    if (!pathStr) return;
    const parts = pathStr.split(/[\\/]/).filter(Boolean);
    parts.forEach((p, i) => {
      if (i > 0) {
        const sep = document.createElement("span");
        sep.className = "sep";
        sep.textContent = "/";
        breadcrumb.appendChild(sep);
      }
      const span = document.createElement("span");
      if (i === parts.length - 1) span.className = "last";
      span.textContent = p;
      breadcrumb.appendChild(span);
    });
  }

  function flashReloaded() {
    const tag = document.createElement("span");
    tag.className = "reloaded show";
    tag.textContent = "reloaded";
    breadcrumb.appendChild(tag);
    setTimeout(() => tag.classList.remove("show"), 800);
    setTimeout(() => tag.remove(), 1200);
  }

  function showEmpty(msg) {
    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-empty";
    page.innerHTML = `<div class="empty"><div class="empty-msg">${escapeHtml(msg)}</div></div>`;
    breadcrumb.innerHTML = "";
  }

  function escapeHtml(s) {
    return (s ?? "").replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    }[c]));
  }

  function setMarkdown(html, headings, pathStr, reloaded) {
    // Capture scroll BEFORE replacing innerHTML — the browser resets scrollTop
    // to 0 when the content's measured height shrinks, so we need to restore
    // it after the new layout settles.
    const prevScroll = scroll.scrollTop;

    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-markdown";
    setBreadcrumb(pathStr);
    // GitHub body style scopes all its rules to `.markdown-body`, so the
    // rendered markdown has to be wrapped in that class for the styling to
    // apply. Win11 mode renders straight into #page as before.
    page.innerHTML = bodyStyle === "github"
      ? `<article class="markdown-body">${html}</article>`
      : html;

    // Highlight code blocks (skip mermaid). hljs is lazy-loaded the first
    // time a doc with code blocks renders; second+ docs hit a warm module.
    const codeBlocks = page.querySelectorAll("pre code");
    let hasNonMermaidCode = false;
    for (const b of codeBlocks) {
      if (!b.closest(".mermaid")) { hasNonMermaidCode = true; break; }
    }
    if (hasNonMermaidCode) {
      if (window.hljs) {
        highlightInPage(page);
      } else {
        ensureHljs().then(() => highlightInPage(page)).catch(() => { });
      }
    }

    // Decorate each code block with a copy-to-clipboard button. The .page pre
    // gets position:relative in CSS so the button anchors to the block's
    // top-right; it's pinned-faded by default and brightens on hover.
    page.querySelectorAll("pre").forEach((pre) => {
      if (pre.classList.contains("mermaid")) return;
      if (pre.querySelector(".copy-btn")) return; // already decorated
      const code = pre.querySelector("code");
      if (!code) return;
      const btn = document.createElement("button");
      btn.className = "copy-btn";
      btn.type = "button";
      btn.title = "Copy";
      btn.textContent = "Copy";
      btn.addEventListener("click", async (ev) => {
        ev.stopPropagation();
        try {
          await navigator.clipboard.writeText(code.textContent || "");
          btn.textContent = "Copied";
          btn.classList.add("copied");
        } catch {
          btn.textContent = "Failed";
        }
        setTimeout(() => {
          btn.textContent = "Copy";
          btn.classList.remove("copied");
        }, 1200);
      });
      pre.appendChild(btn);
    });

    // Run mermaid against any .mermaid blocks. Markdig's UseDiagrams emits
    // <div class="mermaid">…</div>, so query by class. First call triggers
    // a lazy fetch of mermaid.min.js (~3 MB); subsequent calls use the
    // cached module.
    const mermaidNodes = page.querySelectorAll(".mermaid");
    if (mermaidNodes.length > 0) {
      renderMermaid(Array.from(mermaidNodes));
    }

    // Post heading list back so the outline sidebar can populate.
    postMessage({ type: "headings", headings: headings || [] });

    if (reloaded) {
      // Restore previous scroll on the next frame so layout has settled.
      requestAnimationFrame(() => {
        scroll.scrollTop = prevScroll;
        // One more on the frame after that — mermaid diagrams can resize
        // asynchronously and shift content height post-layout.
        requestAnimationFrame(() => { scroll.scrollTop = prevScroll; });
      });
      flashReloaded();
    } else {
      scroll.scrollTop = 0;
    }
  }

  function setText(body, lang, pathStr) {
    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-text";
    setBreadcrumb(pathStr);
    const pre = document.createElement("pre");
    pre.className = "textfile";
    const code = document.createElement("code");
    if (lang) code.className = "language-" + lang;
    code.textContent = body;
    pre.appendChild(code);
    page.innerHTML = "";
    page.appendChild(pre);
    if (lang) {
      if (window.hljs) {
        try { window.hljs.highlightElement(code); } catch { }
      } else {
        ensureHljs().then(h => { try { h.highlightElement(code); } catch { } }).catch(() => { });
      }
    }
    postMessage({ type: "headings", headings: [] });
    scroll.scrollTop = 0;
  }

  function setImage(payload) {
    const pathStr = payload.path || "";
    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-image";
    setBreadcrumb(pathStr);
    // payload.url is a same-origin app.local/__vault URL, so the <img> loads
    // directly (no cross-origin, no blob shuttling).
    page.innerHTML = `
      <div class="image-viewer">
        <img alt="" src="${escapeAttr(payload.url || "")}">
        <div class="meta">${escapeHtml(pathStr.split(/[\\/]/).pop() || "")}</div>
      </div>`;
    postMessage({ type: "headings", headings: [] });
    scroll.scrollTop = 0;
  }

  function setBinary(pathStr) {
    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-binary";
    setBreadcrumb(pathStr);
    page.innerHTML = `<div class="binary-placeholder">Binary file. Open it in another app.</div>`;
    postMessage({ type: "headings", headings: [] });
    scroll.scrollTop = 0;
  }

  function setRaw(payload) {
    document.body.className = document.body.className.replace(/\bkind-\S+/g, "").trim() + " kind-raw";
    setBreadcrumb(payload.path);
    if (scroll) scroll.hidden = true;
    if (!rawframe) return;
    rawframe.hidden = false;

    // Two paths:
    //  - srcdoc (HTML inline): sandboxed with neither allow-scripts nor
    //    allow-same-origin, so the file renders statically in a null origin and
    //    cannot run scripts, reach window.parent, or postMessage to the host.
    //  - URL (PDF and anything else): a same-origin app.local/__vault URL the
    //    iframe loads directly. The PDF viewer needs to run, so no sandbox; the
    //    engine disables embedded PDF JavaScript by default.
    if (typeof payload.html === "string") {
      // Trade-off: links inside an opened .html file won't navigate — use
      // "Open in default browser" for that.
      rawframe.setAttribute("sandbox", "");
      rawframe.removeAttribute("src");
      rawframe.srcdoc = payload.html;
    } else {
      rawframe.removeAttribute("sandbox");
      rawframe.removeAttribute("srcdoc");
      if (rawframe.getAttribute("src") !== payload.url) {
        rawframe.src = payload.url;
      }
    }
    postMessage({ type: "headings", headings: [] });
  }

  function escapeAttr(s) { return escapeHtml(s).replace(/`/g, "&#96;"); }

  // ─── Transcript filter persistence ───────────────────────────────────
  // Inline `onchange` handlers don't fire on innerHTML-inserted nodes, so
  // we delegate from #page (which stays in the DOM across setDoc calls).
  page.addEventListener("change", (e) => {
    const t = e.target;
    if (!t || t.tagName !== "INPUT" || t.type !== "checkbox") return;
    const id = t.id || "";
    if (!id.startsWith("tf-")) return;
    postMessage({
      type: "transcriptFilter",
      category: id.slice(3),
      checked: !!t.checked,
    });
  });

  // ─── Link interception ───────────────────────────────────────────────
  document.addEventListener("click", (e) => {
    const a = e.target.closest("a[href]");
    if (!a) return;
    const href = a.getAttribute("href");
    if (!href) return;

    // Anchor links (#foo) - let through.
    if (href.startsWith("#")) return;

    e.preventDefault();
    // External http/https - send to OS browser. app.local (our shell + the
    // same-origin /__vault/ files) is in-vault, handled below.
    if (/^https?:\/\//i.test(href) && !href.startsWith("https://app.local/")) {
      postMessage({ type: "requestExternal", url: href });
      return;
    }
    // In-vault link - let WPF resolve it (it understands relative paths against
    // the current doc).
    postMessage({ type: "openLink", href: href, base: page.dataset.basePath || "" });
  });

  // ─── Message dispatch ────────────────────────────────────────────────
  function postMessage(obj) {
    if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
      window.chrome.webview.postMessage(obj);
    }
  }

  window.chrome.webview.addEventListener("message", (e) => {
    const m = e.data;
    if (!m || !m.type) return;
    switch (m.type) {
      case "setPrefs":
        applyPrefs(m);
        break;
      case "setDoc":
        page.dataset.basePath = m.basePath || "";
        if (m.kind !== "raw") hideRaw();
        if (m.kind === "markdown") setMarkdown(m.html, m.headings, m.path, !!m.reloaded);
        else if (m.kind === "text") setText(m.body, m.lang || "", m.path);
        else if (m.kind === "image") setImage(m);
        else if (m.kind === "binary") setBinary(m.path);
        else if (m.kind === "raw") setRaw(m);
        else if (m.kind === "empty") showEmpty(m.message || "Open a folder to get started.");
        break;
      case "scrollToHeading":
        if (m.id) {
          const el = document.getElementById(m.id) || page.querySelector('[id="' + m.id + '"]');
          if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
        }
        break;
      case "find":
        // No-op: WPF uses CoreWebView2.Find natively. Kept as a stub.
        break;
    }
  });

  // Tell host we're ready so initial doc can be sent.
  postMessage({ type: "ready" });
})();
