// app.jsx — design canvas with three OS themes side-by-side. Each
// artboard hosts one Reader instance with its own light/dark state.

function ReaderArtboard({ os, defaultMode = "light" }) {
  const [mode, setMode] = React.useState(defaultMode);
  const themeKey = os + "-" + mode;
  const toggle = () => setMode((m) => (m === "dark" ? "light" : "dark"));

  const inner = (
    <Reader theme={themeKey} mode={mode} onToggleMode={toggle} />
  );

  if (os === "mac") {
    return <MacWindowChrome theme={themeKey} title="README.md — Vault">{inner}</MacWindowChrome>;
  }
  if (os === "win11") {
    return <Win11WindowChrome theme={themeKey} title="Vault — README.md">{inner}</Win11WindowChrome>;
  }
  return <GnomeWindowChrome theme={themeKey} title="README.md">{inner}</GnomeWindowChrome>;
}

const AB_W = 1100;
const AB_H = 720;

function App() {
  const winStyle = { borderRadius: 8, background: "transparent" };

  return (
    <DesignCanvas>
      <DCSection
        id="os-themes"
        title="Markdown reader"
        subtitle="Windows 11 · Fluent. Click ☀/☾ in the sidebar to toggle light/dark."
      >
        <DCArtboard id="win11" label="Windows 11 · Fluent" width={AB_W} height={AB_H} style={winStyle}>
          <ReaderArtboard os="win11" defaultMode="dark" />
        </DCArtboard>
      </DCSection>
    </DesignCanvas>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
