// reader.jsx — themed Markdown Reader, with macOS / Win11 / GNOME chromes.
// Reads window.VAULT and window.MARKDOWN. Renders <Markdown> from markdown.jsx.

// ─────────────────────────────────────────────────────────────
// Themes — one map per (OS, mode). Consumed via inline CSS vars
// on the .reader-root wrapper, so styling cascades freely.
// ─────────────────────────────────────────────────────────────
const FONT_MAC = '-apple-system, BlinkMacSystemFont, "SF Pro Text", "Helvetica Neue", sans-serif';
const FONT_MAC_MONO = '"SF Mono", ui-monospace, Menlo, Monaco, monospace';
const FONT_WIN = '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif';
const FONT_WIN_MONO = '"Cascadia Mono", "Cascadia Code", Consolas, "Courier New", monospace';
const FONT_GNOME = '"Adwaita Sans", "Inter", "Cantarell", "Ubuntu", sans-serif';
const FONT_GNOME_MONO = '"Adwaita Mono", "JetBrains Mono", "Source Code Pro", monospace';

const THEMES = {
  "mac-light": {
    font: FONT_MAC, mono: FONT_MAC_MONO,
    appBg: "#ffffff",
    chromeBg: "#e9e9eb",
    chromeBorder: "rgba(0,0,0,0.18)",
    sidebarBg: "#ebebed",
    sidebarBorder: "rgba(0,0,0,0.08)",
    contentBg: "#ffffff",
    text: "#1d1d1f",
    textMuted: "#6e6e73",
    textFaint: "#8e8e93",
    accent: "#0a6cff",
    rowHover: "rgba(0,0,0,0.04)",
    rowActive: "rgba(0,0,0,0.08)",
    segBg: "rgba(0,0,0,0.06)",
    segActive: "#ffffff",
    segActiveBorder: "rgba(0,0,0,0.12)",
    codeBg: "#f5f5f7",
    codeBorder: "rgba(0,0,0,0.06)",
    tableBorder: "rgba(0,0,0,0.10)",
    tableHeaderBg: "#f5f5f7",
    inlineCodeBg: "rgba(0,0,0,0.05)",
    hlKw: "#a626a4", hlStr: "#50a14f", hlNum: "#986801",
    hlCom: "#a0a1a7", hlFn: "#4078f2", hlPunct: "#383a42",
  },
  "mac-dark": {
    font: FONT_MAC, mono: FONT_MAC_MONO,
    appBg: "#1e1e1e",
    chromeBg: "#2c2c2e",
    chromeBorder: "rgba(0,0,0,0.6)",
    sidebarBg: "#252527",
    sidebarBorder: "rgba(255,255,255,0.06)",
    contentBg: "#1e1e1e",
    text: "#f2f2f7",
    textMuted: "#aeaeb2",
    textFaint: "#8e8e93",
    accent: "#0a84ff",
    rowHover: "rgba(255,255,255,0.05)",
    rowActive: "rgba(255,255,255,0.10)",
    segBg: "rgba(255,255,255,0.06)",
    segActive: "#3a3a3c",
    segActiveBorder: "rgba(255,255,255,0.10)",
    codeBg: "#252527",
    codeBorder: "rgba(255,255,255,0.06)",
    tableBorder: "rgba(255,255,255,0.10)",
    tableHeaderBg: "#252527",
    inlineCodeBg: "rgba(255,255,255,0.08)",
    hlKw: "#c678dd", hlStr: "#98c379", hlNum: "#d19a66",
    hlCom: "#6a737d", hlFn: "#61afef", hlPunct: "#abb2bf",
  },
  "win11-light": {
    font: FONT_WIN, mono: FONT_WIN_MONO,
    appBg: "#f3f3f3",
    chromeBg: "#f3f3f3",
    chromeBorder: "rgba(0,0,0,0.10)",
    sidebarBg: "#f9f9f9",
    sidebarBorder: "rgba(0,0,0,0.06)",
    contentBg: "#ffffff",
    text: "#1b1b1b",
    textMuted: "#5c5c5c",
    textFaint: "#8a8a8a",
    accent: "#0067c0",
    rowHover: "rgba(0,0,0,0.04)",
    rowActive: "rgba(0,103,192,0.10)",
    segBg: "transparent",
    segActive: "#ffffff",
    segActiveBorder: "rgba(0,0,0,0.10)",
    codeBg: "#f6f8fa",
    codeBorder: "rgba(0,0,0,0.06)",
    tableBorder: "rgba(0,0,0,0.10)",
    tableHeaderBg: "#f6f8fa",
    inlineCodeBg: "rgba(0,0,0,0.05)",
    hlKw: "#0000ff", hlStr: "#a31515", hlNum: "#098658",
    hlCom: "#008000", hlFn: "#795e26", hlPunct: "#1b1b1b",
  },
  "win11-dark": {
    font: FONT_WIN, mono: FONT_WIN_MONO,
    appBg: "#202020",
    chromeBg: "#202020",
    chromeBorder: "rgba(0,0,0,0.6)",
    sidebarBg: "#2b2b2b",
    sidebarBorder: "rgba(255,255,255,0.06)",
    contentBg: "#1c1c1c",
    text: "#e5e5e5",
    textMuted: "#a0a0a0",
    textFaint: "#7a7a7a",
    accent: "#4cc2ff",
    rowHover: "rgba(255,255,255,0.04)",
    rowActive: "rgba(76,194,255,0.12)",
    segBg: "transparent",
    segActive: "#383838",
    segActiveBorder: "rgba(255,255,255,0.08)",
    codeBg: "#1e1e1e",
    codeBorder: "rgba(255,255,255,0.06)",
    tableBorder: "rgba(255,255,255,0.10)",
    tableHeaderBg: "#262626",
    inlineCodeBg: "rgba(255,255,255,0.08)",
    hlKw: "#569cd6", hlStr: "#ce9178", hlNum: "#b5cea8",
    hlCom: "#6a9955", hlFn: "#dcdcaa", hlPunct: "#d4d4d4",
  },
  "gnome-light": {
    font: FONT_GNOME, mono: FONT_GNOME_MONO,
    appBg: "#fafafa",
    chromeBg: "#ebebeb",
    chromeBorder: "rgba(0,0,0,0.18)",
    sidebarBg: "#ebebeb",
    sidebarBorder: "rgba(0,0,0,0.10)",
    contentBg: "#ffffff",
    text: "#2e3436",
    textMuted: "#5e5c64",
    textFaint: "#9a9996",
    accent: "#1c71d8",
    rowHover: "rgba(0,0,0,0.04)",
    rowActive: "rgba(28,113,216,0.15)",
    segBg: "rgba(0,0,0,0.06)",
    segActive: "#ffffff",
    segActiveBorder: "rgba(0,0,0,0.10)",
    codeBg: "#f6f5f4",
    codeBorder: "rgba(0,0,0,0.08)",
    tableBorder: "rgba(0,0,0,0.12)",
    tableHeaderBg: "#f6f5f4",
    inlineCodeBg: "rgba(0,0,0,0.06)",
    hlKw: "#9141ac", hlStr: "#26a269", hlNum: "#c64600",
    hlCom: "#77767b", hlFn: "#1c71d8", hlPunct: "#2e3436",
  },
  "gnome-dark": {
    font: FONT_GNOME, mono: FONT_GNOME_MONO,
    appBg: "#1e1e1e",
    chromeBg: "#303030",
    chromeBorder: "rgba(0,0,0,0.7)",
    sidebarBg: "#242424",
    sidebarBorder: "rgba(255,255,255,0.08)",
    contentBg: "#1e1e1e",
    text: "#ffffff",
    textMuted: "#c0bfbc",
    textFaint: "#77767b",
    accent: "#78aeed",
    rowHover: "rgba(255,255,255,0.05)",
    rowActive: "rgba(120,174,237,0.20)",
    segBg: "rgba(255,255,255,0.08)",
    segActive: "#454545",
    segActiveBorder: "rgba(255,255,255,0.10)",
    codeBg: "#262626",
    codeBorder: "rgba(255,255,255,0.06)",
    tableBorder: "rgba(255,255,255,0.10)",
    tableHeaderBg: "#2a2a2a",
    inlineCodeBg: "rgba(255,255,255,0.08)",
    hlKw: "#dc8add", hlStr: "#8ff0a4", hlNum: "#ffbe6f",
    hlCom: "#9a9996", hlFn: "#78aeed", hlPunct: "#ffffff",
  },
};

function themeVars(theme) {
  const t = THEMES[theme];
  return {
    "--rd-font": t.font, "--rd-mono": t.mono,
    "--rd-app-bg": t.appBg, "--rd-chrome-bg": t.chromeBg, "--rd-chrome-border": t.chromeBorder,
    "--rd-sidebar-bg": t.sidebarBg, "--rd-sidebar-border": t.sidebarBorder,
    "--rd-content-bg": t.contentBg,
    "--rd-text": t.text, "--rd-text-muted": t.textMuted, "--rd-text-faint": t.textFaint,
    "--rd-accent": t.accent,
    "--rd-row-hover": t.rowHover, "--rd-row-active": t.rowActive,
    "--rd-seg-bg": t.segBg, "--rd-seg-active": t.segActive, "--rd-seg-active-border": t.segActiveBorder,
    "--rd-code-bg": t.codeBg, "--rd-code-border": t.codeBorder,
    "--rd-table-border": t.tableBorder, "--rd-table-header-bg": t.tableHeaderBg,
    "--rd-icode-bg": t.inlineCodeBg,
    "--hl-kw": t.hlKw, "--hl-str": t.hlStr, "--hl-num": t.hlNum,
    "--hl-com": t.hlCom, "--hl-fn": t.hlFn, "--hl-punct": t.hlPunct,
    color: t.text, fontFamily: t.font,
  };
}

// ─────────────────────────────────────────────────────────────
// Icons — tiny inline SVGs (12px stroke)
// ─────────────────────────────────────────────────────────────
function Icon({ name, size = 12 }) {
  const s = { width: size, height: size, display: "inline-block", flexShrink: 0 };
  const props = { width: size, height: size, viewBox: "0 0 16 16", fill: "none", stroke: "currentColor", strokeWidth: 1.4, strokeLinecap: "round", strokeLinejoin: "round", style: s };
  switch (name) {
    case "chevron-right": return <svg {...props}><path d="M6 4l4 4-4 4" /></svg>;
    case "chevron-down":  return <svg {...props}><path d="M4 6l4 4 4-4" /></svg>;
    case "folder":        return <svg {...props}><path d="M2 5a1 1 0 0 1 1-1h3l1.5 1.5H13a1 1 0 0 1 1 1V12a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V5z" /></svg>;
    case "folder-open":   return <svg {...props}><path d="M2 5a1 1 0 0 1 1-1h3l1.5 1.5H13a1 1 0 0 1 1 1v1H2V5z" /><path d="M2 6.5h12L13 12.5a1 1 0 0 1-1 .5H3a1 1 0 0 1-1-1V6.5z" /></svg>;
    case "file":          return <svg {...props}><path d="M4 2h5l3 3v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z" /><path d="M9 2v3h3" /></svg>;
    case "file-image":    return <svg {...props}><path d="M4 2h5l3 3v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z" /><path d="M9 2v3h3" /><circle cx="6" cy="9" r="0.8" fill="currentColor" stroke="none" /><path d="M4.5 13l2-2 1.5 1.5L10 10.5l2 2.5" /></svg>;
    case "file-code":     return <svg {...props}><path d="M4 2h5l3 3v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z" /><path d="M9 2v3h3" /><path d="M6 9.5l-1 1.5 1 1.5M10 9.5l1 1.5-1 1.5" /></svg>;
    case "sun":           return <svg {...props}><circle cx="8" cy="8" r="3" /><path d="M8 1v1.5M8 13.5V15M1 8h1.5M13.5 8H15M3 3l1 1M12 12l1 1M3 13l1-1M12 4l1-1" /></svg>;
    case "moon":          return <svg {...props}><path d="M12.5 9.5A5 5 0 1 1 6.5 3.5a4 4 0 0 0 6 6z" /></svg>;
    case "search":        return <svg {...props}><circle cx="7" cy="7" r="4" /><path d="M10 10l3 3" /></svg>;
    case "gear":          return <svg {...props}><circle cx="8" cy="8" r="2" /><path d="M8 1v2M8 13v2M1 8h2M13 8h2M3 3l1.4 1.4M11.6 11.6L13 13M3 13l1.4-1.4M11.6 4.4L13 3" /></svg>;
    case "close":         return <svg {...props}><path d="M4 4l8 8M12 4l-8 8" /></svg>;
    default: return null;
  }
}

// ─────────────────────────────────────────────────────────────
// Sidebar — segmented control + Folder/Outline panes
// ─────────────────────────────────────────────────────────────
function SegmentedControl({ value, onChange, options }) {
  return (
    <div className="rd-seg" role="tablist">
      {options.map((opt) => (
        <button
          key={opt.value}
          role="tab"
          aria-selected={value === opt.value}
          className={"rd-seg-btn " + (value === opt.value ? "rd-seg-btn-active" : "")}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  );
}

// Strip a markdown extension for display. Non-md files always keep their
// extension (otherwise you can't tell `notes.md` from `notes.png`).
function displayName(name, prefs) {
  if (prefs.showExtensions) return name;
  if (/\.md$/i.test(name)) return name.replace(/\.md$/i, "");
  return name;
}

// Pick an icon based on extension.
function fileIcon(name) {
  if (/\.(png|jpe?g|gif|webp|svg)$/i.test(name)) return "file-image";
  if (/\.(js|jsx|ts|tsx|css|json|html|ya?ml|toml)$/i.test(name) || name.startsWith(".")) return "file-code";
  return "file";
}

// Decide if a node should be rendered given prefs. Folders are kept iff they
// recursively contain at least one renderable child (no empty folders).
function nodeVisible(node, prefs) {
  if (node.type === "file") {
    if (node.name.startsWith(".") && !prefs.showHidden) return false;
    if (prefs.showNonMarkdown) return true;
    return /\.md$/i.test(node.name);
  }
  if (node.name.startsWith(".") && !prefs.showHidden) return false;
  return node.children.some((c) => nodeVisible(c, prefs));
}

function FolderTreeNode({ node, depth, activePath, onOpen, path, prefs }) {
  const [open, setOpen] = React.useState(!!node.open);
  if (node.type === "folder") {
    const kids = node.children.filter((c) => nodeVisible(c, prefs));
    return (
      <div>
        <div
          className="rd-row rd-row-folder"
          style={{ paddingLeft: 8 + depth * 12 }}
          onClick={() => setOpen((o) => !o)}
        >
          <Icon name={open ? "chevron-down" : "chevron-right"} size={10} />
          <Icon name={open ? "folder-open" : "folder"} size={13} />
          <span className="rd-row-label">{node.name}</span>
        </div>
        {open && kids.map((c, i) => (
          <FolderTreeNode
            key={c.name + i}
            node={c}
            depth={depth + 1}
            activePath={activePath}
            onOpen={onOpen}
            path={path + "/" + c.name}
            prefs={prefs}
          />
        ))}
      </div>
    );
  }
  const full = path + "/" + node.name;
  const isActive = activePath && full.endsWith("/" + activePath);
  const isMd = /\.md$/i.test(node.name);
  return (
    <div
      className={"rd-row rd-row-file " + (isActive ? "rd-row-active " : "") + (isMd ? "" : "rd-row-file-other")}
      style={{ paddingLeft: 8 + depth * 12 + 10 }}
      onClick={() => isMd && onOpen(node.name)}
    >
      <Icon name={fileIcon(node.name)} size={12} />
      <span className="rd-row-label">{displayName(node.name, prefs)}</span>
    </div>
  );
}

function FolderPane({ vault, activePath, onOpen, prefs }) {
  const roots = vault.tree.filter((n) => nodeVisible(n, prefs));
  return (
    <div className="rd-tree">
      <div className="rd-tree-header">
        <Icon name="folder-open" size={13} />
        <span>{vault.name}</span>
      </div>
      {roots.map((n, i) => (
        <FolderTreeNode key={n.name + i} node={n} depth={0} activePath={activePath} onOpen={onOpen} path="" prefs={prefs} />
      ))}
    </div>
  );
}

// Build a nested outline tree from a flat heading list.
function buildOutline(headings) {
  const root = { children: [] };
  const stack = [{ level: 0, node: root }];
  headings.forEach((h) => {
    const node = { ...h, children: [] };
    while (stack.length && stack[stack.length - 1].level >= h.level) stack.pop();
    stack[stack.length - 1].node.children.push(node);
    stack.push({ level: h.level, node });
  });
  return root.children;
}

function OutlineNode({ node, depth, prefs }) {
  // Compute initial expanded state from prefs:
  //   - collapseBelow: H1..H6 selected -> nodes at that level or deeper start closed
  //     (their children stay hidden until expanded). 7 = none (all open).
  //   - collapseContaining: headings whose text contains this substring start closed.
  const text = (prefs.outlineCollapseContaining || "").trim().toLowerCase();
  const matchesText = text && node.text.toLowerCase().includes(text);
  const collapseByLevel = node.level >= (prefs.outlineCollapseBelow || 7);
  const startClosed = collapseByLevel || matchesText;
  const [open, setOpen] = React.useState(!startClosed);
  const hasKids = node.children.length > 0;
  return (
    <div>
      <div className="rd-row rd-row-outline" style={{ paddingLeft: 8 + depth * 12 }}>
        <span
          className="rd-row-twisty"
          onClick={(e) => { e.stopPropagation(); if (hasKids) setOpen((o) => !o); }}
          style={{ visibility: hasKids ? "visible" : "hidden" }}
        >
          <Icon name={open ? "chevron-down" : "chevron-right"} size={10} />
        </span>
        <a href={"#" + node.id} className="rd-row-label rd-row-h">
          <span className="rd-row-hlevel">H{node.level}</span>
          <span>{node.text}</span>
        </a>
      </div>
      {open && hasKids && node.children.map((c) => (
        <OutlineNode key={c.id} node={c} depth={depth + 1} prefs={prefs} />
      ))}
    </div>
  );
}

function OutlinePane({ source, prefs }) {
  const headings = React.useMemo(() => parseHeadings(source), [source]);
  const tree = React.useMemo(() => buildOutline(headings), [headings]);
  // Re-mount tree when collapse prefs change so initial-open recomputes.
  const key = "k" + prefs.outlineCollapseBelow + "|" + (prefs.outlineCollapseContaining || "");
  return (
    <div className="rd-tree" key={key}>
      <div className="rd-tree-header">Outline · {headings.length}</div>
      {tree.map((n) => <OutlineNode key={n.id} node={n} depth={0} prefs={prefs} />)}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// Preferences modal
// ─────────────────────────────────────────────────────────────
function PrefRow({ label, hint, children, onClick, role }) {
  return (
    <div className="rd-pref-row" onClick={onClick} role={role}>
      <div className="rd-pref-text">
        <div className="rd-pref-label">{label}</div>
        {hint && <div className="rd-pref-hint">{hint}</div>}
      </div>
      <div className="rd-pref-ctrl" onClick={(e) => e.stopPropagation()}>
        {children}
      </div>
    </div>
  );
}

function PrefToggle({ checked, onChange, label, hint }) {
  return (
    <PrefRow
      label={label}
      hint={hint}
      role="button"
      onClick={() => onChange(!checked)}
    >
      <span
        className={"rd-switch " + (checked ? "rd-switch-on" : "")}
        role="switch"
        aria-checked={checked}
        onClick={(e) => { e.stopPropagation(); onChange(!checked); }}
      >
        <span className="rd-switch-knob" />
      </span>
    </PrefRow>
  );
}

function PrefSelect({ value, onChange, options, label, hint }) {
  return (
    <PrefRow label={label} hint={hint}>
      <select className="rd-input rd-select" value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </PrefRow>
  );
}

function PrefNumber({ value, onChange, min, max, step, suffix, label, hint }) {
  return (
    <PrefRow label={label} hint={hint}>
      <div className="rd-stepper">
        <button
          className="rd-stepper-btn"
          onClick={() => onChange(Math.max(min, value - (step || 1)))}
          aria-label="Decrease"
        >−</button>
        <input
          className="rd-input rd-stepper-input"
          type="number" value={value} min={min} max={max} step={step || 1}
          onChange={(e) => {
            const n = Number(e.target.value);
            if (!Number.isNaN(n)) onChange(Math.max(min, Math.min(max, n)));
          }}
        />
        {suffix && <span className="rd-stepper-suffix">{suffix}</span>}
        <button
          className="rd-stepper-btn"
          onClick={() => onChange(Math.min(max, value + (step || 1)))}
          aria-label="Increase"
        >+</button>
      </div>
    </PrefRow>
  );
}

function PrefSlider({ value, onChange, min, max, step, suffix, label, hint }) {
  return (
    <PrefRow label={label} hint={hint}>
      <div className="rd-slider-wrap">
        <input
          className="rd-slider"
          type="range" min={min} max={max} step={step || 1} value={value}
          onChange={(e) => onChange(Number(e.target.value))}
        />
        <span className="rd-slider-readout">{value}{suffix}</span>
      </div>
    </PrefRow>
  );
}

function PrefText({ value, onChange, placeholder, label, hint }) {
  return (
    <PrefRow label={label} hint={hint}>
      <input
        className="rd-input rd-text-input"
        type="text" value={value} placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
      />
    </PrefRow>
  );
}

const TYPEFACE_OPTIONS = [
  { value: "system", label: "System" },
  { value: "sans",   label: "Sans (Inter)" },
  { value: "serif",  label: "Serif (Georgia)" },
  { value: "mono",   label: "Monospace" },
];
const HEADING_OPTIONS = [
  { value: 1, label: "H1" },
  { value: 2, label: "H2" },
  { value: 3, label: "H3" },
  { value: 4, label: "H4" },
  { value: 5, label: "H5" },
  { value: 6, label: "H6" },
  { value: 7, label: "Never" },
];

function PreferencesModal({ prefs, setPrefs, mode, onToggleMode, onClose }) {
  React.useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const update = (patch) => setPrefs({ ...prefs, ...patch });
  return (
    <div className="rd-modal-backdrop" onClick={onClose}>
      <div
        className="rd-modal"
        role="dialog"
        aria-modal="true"
        aria-label="Preferences"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="rd-modal-head">
          <div className="rd-modal-title">Preferences</div>
          <button className="rd-modal-close" onClick={onClose} aria-label="Close">
            <Icon name="close" size={11} />
          </button>
        </div>
        <div className="rd-modal-body">
          <div className="rd-pref-section-title">Appearance</div>
          <PrefToggle
            checked={mode === "dark"}
            onChange={() => onToggleMode()}
            label="Dark mode"
            hint="Use a dark color scheme for chrome and content"
          />

          <div className="rd-pref-section-title">Files</div>
          <PrefToggle
            checked={prefs.showExtensions}
            onChange={(v) => update({ showExtensions: v })}
            label="Show file extensions"
            hint="Display .md after note names"
          />
          <PrefToggle
            checked={prefs.showNonMarkdown}
            onChange={(v) => update({ showNonMarkdown: v })}
            label="Show non-markdown files"
            hint="Reveal images, configs, and other files in the tree"
          />
          <PrefToggle
            checked={prefs.showHidden}
            onChange={(v) => update({ showHidden: v })}
            label="Show hidden files"
            hint="Reveal dotfiles like .gitignore and .obsidian"
          />
          <PrefToggle
            checked={prefs.wrapSidebar}
            onChange={(v) => update({ wrapSidebar: v })}
            label="Wrap text in sidebar"
            hint="Let long file and heading names break onto multiple lines"
          />
          <PrefToggle
            checked={prefs.autoOutline}
            onChange={(v) => update({ autoOutline: v })}
            label="Auto-switch to outline"
            hint="Jump the sidebar to Outline when opening a markdown file"
          />

          <div className="rd-pref-section-title">Reading</div>
          <PrefToggle
            checked={prefs.showExtensions}
            onChange={(v) => update({ showExtensions: v })}
            label="Show file extensions"
            hint="Display .md after note names"
          />
          <PrefToggle
            checked={prefs.showNonMarkdown}
            onChange={(v) => update({ showNonMarkdown: v })}
            label="Show non-markdown files"
            hint="Reveal images, configs, and other files in the tree"
          />
          <PrefToggle
            checked={prefs.showHidden}
            onChange={(v) => update({ showHidden: v })}
            label="Show hidden files"
            hint="Reveal dotfiles like .gitignore and .obsidian"
          />
          <PrefToggle
            checked={prefs.wrapSidebar}
            onChange={(v) => update({ wrapSidebar: v })}
            label="Wrap text in sidebar"
            hint="Let long file and heading names break onto multiple lines"
          />

          <div className="rd-pref-section-title">Reading</div>
          <PrefToggle
            checked={prefs.showLineNumbers}
            onChange={(v) => update({ showLineNumbers: v })}
            label="Show line numbers"
            hint="Display the source line of each block in the left margin"
          />
          <PrefSelect
            value={prefs.typeface}
            onChange={(v) => update({ typeface: v })}
            options={TYPEFACE_OPTIONS}
            label="Typeface"
            hint="Font used for the rendered document"
          />
          <PrefNumber
            value={prefs.fontSize}
            onChange={(v) => update({ fontSize: v })}
            min={11} max={22} step={1} suffix="px"
            label="Font size"
            hint="Base size; headings scale relative to this"
          />
          <PrefSlider
            value={prefs.marginPct}
            onChange={(v) => update({ marginPct: v })}
            min={50} max={100} step={1} suffix="%"
            label="Margins"
            hint="Width of the reading column as a share of the content area"
          />

          <div className="rd-pref-section-title">Outline</div>
          <PrefSelect
            value={prefs.outlineCollapseBelow}
            onChange={(v) => update({ outlineCollapseBelow: Number(v) })}
            options={HEADING_OPTIONS}
            label="Auto-collapse below"
            hint="Hide headings deeper than this level by default"
          />
          <PrefText
            value={prefs.outlineCollapseContaining}
            onChange={(v) => update({ outlineCollapseContaining: v })}
            placeholder="e.g. Done or ✅"
            label="Always collapse containing"
            hint="Headings whose text matches this start collapsed"
          />
        </div>
        <div className="rd-modal-foot">
          <button className="rd-modal-btn rd-modal-btn-primary" onClick={onClose}>Done</button>
        </div>
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// Open-folder popover
// ─────────────────────────────────────────────────────────────
function PinButton({ pinned, onClick, title }) {
  return (
    <button
      className={"rd-pin " + (pinned ? "rd-pin-on" : "")}
      onClick={(e) => { e.stopPropagation(); onClick(); }}
      title={title}
      aria-label={title}
    >
      <svg width="11" height="11" viewBox="0 0 16 16" fill={pinned ? "currentColor" : "none"} stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" strokeLinecap="round">
        <path d="M9.5 1.5l5 5-2 1-1 4-3-3-3 3v-3l-3-3 4-1 1-2z" />
        <path d="M6.5 9.5l-3 3" />
      </svg>
    </button>
  );
}

function FolderRow({ name, pinned, onPin, onOpen, current }) {
  return (
    <div
      className={"rd-open-row " + (current ? "rd-open-row-current" : "")}
      onClick={() => onOpen && onOpen()}
      role={onOpen ? "button" : undefined}
    >
      <Icon name="folder" size={13} />
      <span className="rd-open-row-name">{name}</span>
      {current && <span className="rd-open-row-tag">current</span>}
      <PinButton pinned={pinned} onClick={onPin} title={pinned ? "Unpin" : "Pin"} />
    </div>
  );
}

function OpenMenu({ current, pinned, recents, onTogglePin, onOpenFolder, onPickFolder, onClose }) {
  const ref = React.useRef(null);
  React.useEffect(() => {
    const onDoc = (e) => { if (ref.current && !ref.current.contains(e.target)) onClose(); };
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("mousedown", onDoc);
    window.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      window.removeEventListener("keydown", onKey);
    };
  }, [onClose]);

  const currentPinned = pinned.includes(current);
  // Recents excludes the currently-open folder and anything pinned, then is
  // capped at 3 so the menu stays compact.
  const visibleRecents = recents.filter((n) => n !== current && !pinned.includes(n)).slice(0, 3);

  return (
    <div className="rd-open-menu" ref={ref} role="dialog" aria-label="Open folder">
      {pinned.length > 0 && (
        <div className="rd-open-section">
          <div className="rd-open-section-title">Pinned</div>
          {pinned.map((name) => (
            <FolderRow
              key={"p:" + name} name={name} pinned={true}
              onPin={() => onTogglePin(name)}
              onOpen={() => onOpenFolder(name)}
              current={name === current}
            />
          ))}
        </div>
      )}
      <div className="rd-open-section">
        <div className="rd-open-section-title">Currently open</div>
        <FolderRow
          name={current} pinned={currentPinned}
          onPin={() => onTogglePin(current)}
          current={true}
        />
      </div>
      {visibleRecents.length > 0 && (
        <div className="rd-open-section">
          <div className="rd-open-section-title">Recent</div>
          {visibleRecents.map((name) => (
            <FolderRow
              key={"r:" + name} name={name} pinned={false}
              onPin={() => onTogglePin(name)}
              onOpen={() => onOpenFolder(name)}
            />
          ))}
        </div>
      )}
      <div className="rd-open-foot">
        <button className="rd-open-pick" onClick={onPickFolder}>
          <Icon name="folder-open" size={13} />
          <span>Open folder…</span>
        </button>
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// Sidebar
// ─────────────────────────────────────────────────────────────
const SIDEBAR_MIN = 160;
const SIDEBAR_MAX = 420;

function Sidebar({
  tab, setTab, vault, activePath, onOpen, source,
  onOpenPrefs, prefsOpen, prefs,
  width, onWidthChange,
  currentFolder, pinned, recents, onTogglePin, onOpenFolder, onPickFolder,
}) {
  const dragRef = React.useRef(null);
  const [openMenu, setOpenMenu] = React.useState(false);
  const onMouseDown = (e) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = width;
    const onMove = (ev) => {
      const next = Math.max(SIDEBAR_MIN, Math.min(SIDEBAR_MAX, startW + (ev.clientX - startX)));
      onWidthChange(next);
    };
    const onUp = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      document.body.style.cursor = "";
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
    document.body.style.cursor = "col-resize";
  };
  return (
    <aside className={"rd-sidebar " + (prefs.wrapSidebar ? "rd-sidebar-wrap" : "")} style={{ width }}>
      <div className="rd-sidebar-top">
        <SegmentedControl
          value={tab}
          onChange={setTab}
          options={[{ value: "folder", label: "Folder" }, { value: "outline", label: "Outline" }]}
        />
      </div>
      <div className="rd-sidebar-body">
        {tab === "folder"
          ? <FolderPane vault={vault} activePath={activePath} onOpen={onOpen} prefs={prefs} />
          : <OutlinePane source={source} prefs={prefs} />}
      </div>
      <div className="rd-sidebar-foot">
        <div className="rd-open-anchor">
          <button
            className={"rd-foot-btn " + (openMenu ? "rd-foot-btn-active" : "")}
            onClick={() => setOpenMenu((o) => !o)}
            title="Open folder"
          >
            <Icon name="folder-open" size={12} />
            <span>Open</span>
            <Icon name="chevron-down" size={10} />
          </button>
          {openMenu && (
            <OpenMenu
              current={currentFolder}
              pinned={pinned}
              recents={recents}
              onTogglePin={onTogglePin}
              onOpenFolder={(name) => { onOpenFolder(name); setOpenMenu(false); }}
              onPickFolder={() => { onPickFolder(); setOpenMenu(false); }}
              onClose={() => setOpenMenu(false)}
            />
          )}
        </div>
        <div style={{ flex: 1 }} />
        <button
          className={"rd-foot-btn " + (prefsOpen ? "rd-foot-btn-active" : "")}
          onClick={onOpenPrefs}
          title="Preferences"
        >
          <Icon name="gear" size={12} />
          <span>Preferences</span>
        </button>
      </div>
      <div
        ref={dragRef}
        className="rd-sidebar-resizer"
        onMouseDown={onMouseDown}
        title="Drag to resize"
      />
    </aside>
  );
}

// ─────────────────────────────────────────────────────────────
// Content area — breadcrumb + rendered markdown
// ─────────────────────────────────────────────────────────────
const TYPEFACE_STACK = {
  system: "var(--rd-font)",
  sans:   '"Inter", "Helvetica Neue", Arial, sans-serif',
  serif:  '"Charter", "Iowan Old Style", "Georgia", serif',
  mono:   "var(--rd-mono)",
};

function Content({ path, source, prefs }) {
  const parts = path.split("/");
  const pageStyle = {
    width: prefs.marginPct + "%",
    fontFamily: TYPEFACE_STACK[prefs.typeface] || TYPEFACE_STACK.system,
    fontSize: prefs.fontSize + "px",
  };
  // The line-number gutter only carves out extra space when the existing
  // side margin is narrower than the gutter. When margins are wide enough
  // (default 85%), the numbers float into the existing margin and the
  // reading column keeps its full width.
  const contentStyle = {
    "--rd-page-pct": prefs.marginPct + "%",
    ...(prefs.showLineNumbers ? { "--rd-lineno-gutter": "3em" } : {}),
  };
  return (
    <main className="rd-content" style={contentStyle}>
      <div className="rd-breadcrumb">
        {parts.map((p, i) => (
          <React.Fragment key={i}>
            {i > 0 && <span className="rd-breadcrumb-sep">/</span>}
            <span className={i === parts.length - 1 ? "rd-breadcrumb-active" : ""}>{p}</span>
          </React.Fragment>
        ))}
      </div>
      <div className="rd-scroll">
        <div className="rd-page" style={pageStyle}>
          <Markdown source={source} showLineNumbers={prefs.showLineNumbers} />
        </div>
      </div>
    </main>
  );
}

// ─────────────────────────────────────────────────────────────
// Reader — sidebar + content. Doesn't include OS chrome; that's
// supplied by the wrapping {Mac|Win|Gnome}Window components.
// ─────────────────────────────────────────────────────────────
function Reader({ theme, mode, onToggleMode }) {
  const [tab, setTab] = React.useState("folder");
  const [path, setPath] = React.useState(window.VAULT.open);
  const [source] = React.useState(window.MARKDOWN);
  const [sidebarWidth, setSidebarWidth] = React.useState(240);
  // Open-folder state. Seeded with a realistic mix so the menu has content
  // to show in this mockup. Real builds would persist these to disk.
  const [currentFolder, setCurrentFolder] = React.useState(window.VAULT.name);
  const [pinned, setPinned] = React.useState(["Work Notes"]);
  const [recents, setRecents] = React.useState(["Research", "Personal Journal", "Side Project"]);
  const [prefs, setPrefs] = React.useState({
    // Files
    showExtensions: true,
    showNonMarkdown: false,
    showHidden: false,
    wrapSidebar: false,
    autoOutline: false,
    // Reading
    typeface: "system",
    fontSize: 14,
    marginPct: 85,
    showLineNumbers: false,
    // Outline
    outlineCollapseBelow: 7,        // 7 == never
    outlineCollapseContaining: "",  // empty == never
  });
  const [prefsOpen, setPrefsOpen] = React.useState(false);
  const activeFile = path.split("/").pop();

  const togglePin = (name) => {
    setPinned((prev) => prev.includes(name) ? prev.filter((n) => n !== name) : [...prev, name]);
  };
  const openFolder = (name) => {
    if (name === currentFolder) return;
    setRecents((prev) => [currentFolder, ...prev.filter((n) => n !== name && n !== currentFolder)].slice(0, 6));
    setCurrentFolder(name);
  };
  const pickFolder = async () => {
    // System folder picker — File System Access API. Falls back to a prompt
    // in browsers without support (Firefox, Safari) so the mockup still
    // demonstrates the flow.
    try {
      if (window.showDirectoryPicker) {
        const handle = await window.showDirectoryPicker();
        if (handle && handle.name) openFolder(handle.name);
        return;
      }
    } catch (e) { return; /* user dismissed */ }
    const name = window.prompt("Folder name");
    if (name) openFolder(name);
  };

  return (
    <div className="rd-app" style={themeVars(theme)}>
      <Sidebar
        tab={tab} setTab={setTab}
        vault={window.VAULT}
        activePath={activeFile}
        onOpen={(name) => {
          setPath("1. Projects/Markdown Reader/" + name);
          if (prefs.autoOutline && /\.md$/i.test(name)) setTab("outline");
        }}
        source={source}
        prefs={prefs}
        prefsOpen={prefsOpen}
        onOpenPrefs={() => setPrefsOpen(true)}
        width={sidebarWidth}
        onWidthChange={setSidebarWidth}
        currentFolder={currentFolder}
        pinned={pinned}
        recents={recents}
        onTogglePin={togglePin}
        onOpenFolder={openFolder}
        onPickFolder={pickFolder}
      />
      <Content path={path} source={source} prefs={prefs} />
      {prefsOpen && (
        <PreferencesModal
          prefs={prefs}
          setPrefs={setPrefs}
          mode={mode}
          onToggleMode={onToggleMode}
          onClose={() => setPrefsOpen(false)}
        />
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// OS Chromes
// ─────────────────────────────────────────────────────────────

// macOS traffic lights (12px)
function MacLights() {
  return (
    <div className="rd-mac-lights">
      <span style={{ background: "#ff5f57" }} />
      <span style={{ background: "#febc2e" }} />
      <span style={{ background: "#28c840" }} />
    </div>
  );
}
function MacWindowChrome({ title, children, theme }) {
  return (
    <div className="rd-win rd-win-mac" style={themeVars(theme)}>
      <div className="rd-mac-titlebar">
        <MacLights />
        <div className="rd-mac-title">{title}</div>
        <div style={{ width: 52 }} />
      </div>
      <div className="rd-win-body">{children}</div>
    </div>
  );
}

// Windows 11
function Win11WindowChrome({ title, children, theme }) {
  return (
    <div className="rd-win rd-win-win11" style={themeVars(theme)}>
      <div className="rd-win11-titlebar">
        <div className="rd-win11-title">{title}</div>
        <div className="rd-win11-controls">
          <button className="rd-win11-ctrl" title="Minimize">
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2 5h6" stroke="currentColor" strokeWidth="1" /></svg>
          </button>
          <button className="rd-win11-ctrl" title="Maximize">
            <svg width="10" height="10" viewBox="0 0 10 10"><rect x="2" y="2" width="6" height="6" fill="none" stroke="currentColor" strokeWidth="1" /></svg>
          </button>
          <button className="rd-win11-ctrl rd-win11-close" title="Close">
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2 2l6 6M8 2l-6 6" stroke="currentColor" strokeWidth="1" /></svg>
          </button>
        </div>
      </div>
      <div className="rd-win-body">{children}</div>
    </div>
  );
}

// GNOME (libadwaita)
function GnomeWindowChrome({ title, children, theme }) {
  return (
    <div className="rd-win rd-win-gnome" style={themeVars(theme)}>
      <div className="rd-gnome-titlebar">
        <div style={{ width: 28 }} />
        <div className="rd-gnome-title">{title}</div>
        <div className="rd-gnome-controls">
          <button className="rd-gnome-ctrl" title="Close">
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M2.5 2.5l5 5M7.5 2.5l-5 5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" /></svg>
          </button>
        </div>
      </div>
      <div className="rd-win-body">{children}</div>
    </div>
  );
}

Object.assign(window, {
  THEMES, Reader, MacWindowChrome, Win11WindowChrome, GnomeWindowChrome,
});
