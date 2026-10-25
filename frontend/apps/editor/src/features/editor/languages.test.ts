import { describe, expect, it } from "vitest";
import { monacoLanguage } from "./languages";

describe("monacoLanguage", () => {
  it.each([
    ["notes/README.md", "markdown"],
    ["Scripts/Backup.PS1", "powershell"],
    ["deploy.sh", "shell"],
    ["build.cmd", "bat"],
    ["SQL/count.sql", "sql"],
    ["config.json", "json"],
    ["ci.yml", "yaml"],
    ["App.csproj", "xml"],
    ["docker/Dockerfile", "dockerfile"],
    ["notes.txt", "plaintext"],
    ["Makefile", "plaintext"],
    [".bashrc", "plaintext"],
    ["page.mdx", "plaintext"],
  ])("%s → %s", (path, language) => {
    expect(monacoLanguage(path)).toBe(language);
  });
});
