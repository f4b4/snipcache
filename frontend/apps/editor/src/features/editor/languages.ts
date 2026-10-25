// Monaco language for a file. Every id returned here must be registered in
// monaco.ts; anything else falls back to plaintext.
const BY_EXTENSION: Record<string, string> = {
  md: "markdown",
  ps1: "powershell",
  psm1: "powershell",
  psd1: "powershell",
  sh: "shell",
  bash: "shell",
  zsh: "shell",
  bat: "bat",
  cmd: "bat",
  sql: "sql",
  json: "json",
  yaml: "yaml",
  yml: "yaml",
  xml: "xml",
  csproj: "xml",
  props: "xml",
  targets: "xml",
  ini: "ini",
  py: "python",
  js: "javascript",
  mjs: "javascript",
  cjs: "javascript",
  ts: "typescript",
  cs: "csharp",
};

export function monacoLanguage(path: string): string {
  const name = path.slice(path.lastIndexOf("/") + 1).toLowerCase();
  if (name === "dockerfile") return "dockerfile";
  const dot = name.lastIndexOf(".");
  if (dot <= 0) return "plaintext";
  return BY_EXTENSION[name.slice(dot + 1)] ?? "plaintext";
}
