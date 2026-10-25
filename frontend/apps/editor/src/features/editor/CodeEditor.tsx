import { useEffect, useRef } from "react";
import Editor from "@monaco-editor/react";
import { useColorScheme } from "../../lib/useColorScheme";
import { monacoLanguage } from "./languages";
import "./monaco";

export interface CodeEditorProps {
  path: string;
  /** Text to start with. Later changes are ignored; remount (key) to replace the text. */
  initialValue: string;
  onChange: (value: string) => void;
}

/**
 * Monaco for one file. Loaded lazily (it is most of the bundle), and replaced
 * by a textarea in tests, where Monaco cannot run. Ctrl+S is handled by the
 * editor pane on the window, not here.
 *
 * Uncontrolled on purpose: with a `value` prop, @monaco-editor/react writes
 * the prop back into the editor whenever it differs, and while React lags
 * behind fast typing that overwrites keystrokes with a stale draft.
 */
export default function CodeEditor({ path, initialValue, onChange }: CodeEditorProps) {
  const scheme = useColorScheme();
  const language = monacoLanguage(path);
  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });

  return (
    // No `path` prop: that would keep one Monaco model per file alive and reuse
    // its old text on remount. The pane remounts this for every file instead.
    <Editor
      defaultValue={initialValue}
      language={language}
      theme={scheme === "dark" ? "vs-dark" : "vs"}
      onChange={(next) => onChangeRef.current(next ?? "")}
      options={{
        automaticLayout: true,
        fontSize: 14,
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
        wordWrap: language === "markdown" || language === "plaintext" ? "on" : "off",
        renderWhitespace: "selection",
        tabSize: 2,
      }}
    />
  );
}
