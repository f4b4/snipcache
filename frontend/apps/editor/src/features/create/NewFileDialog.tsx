import { useState, type ChangeEvent, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getGetTreeQueryKey, useSaveFile } from "../../api/generated/editor";
import { Button } from "../../components/Button";
import { Field } from "../../components/Field";
import { Modal } from "../../components/Modal";
import { encodePath, joinPath, normalizeFolder } from "../../lib/paths";
import { createFailedMessage, createPathError, type ExistingPaths } from "./validation";

interface NewFileDialogProps {
  /** Prefilled parent folder ("" for the root). */
  folder: string;
  /** Every path in the repository, for the exists and slug checks. */
  existing: ExistingPaths;
  onClose: () => void;
  onCreated: (path: string) => void;
}

/** Creates a file with one commit. A title seeds the `# Title` line of a Markdown file. */
export function NewFileDialog({ folder: initialFolder, existing, onClose, onCreated }: NewFileDialogProps) {
  const [folder, setFolder] = useState(initialFolder);
  const [name, setName] = useState("");
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string>();
  const save = useSaveFile();
  const queryClient = useQueryClient();

  // An error refers to the input it was shown for; editing clears it.
  const edit = (set: (value: string) => void) => (e: ChangeEvent<HTMLInputElement>) => {
    set(e.target.value);
    setError(undefined);
  };

  const fileName = name.normalize("NFC");
  const isMarkdown = /\.md$/i.test(fileName);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (fileName === "") {
      setError("The name is required.");
      return;
    }
    const path = joinPath(normalizeFolder(folder), fileName);
    const problem =
      createPathError(path, existing) ??
      (/\.[^.]+$/.test(fileName) ? undefined : "Add an extension, e.g. .md, .sql or .ps1.");
    if (problem) {
      setError(problem);
      return;
    }

    const heading = isMarkdown ? title.trim() : "";
    try {
      await save.mutateAsync({
        path: encodePath(path),
        data: { content: heading ? `# ${heading}\n` : "", sha: null, message: null },
      });
    } catch (err) {
      setError(createFailedMessage(err, path));
      return;
    }
    await queryClient.invalidateQueries({ queryKey: getGetTreeQueryKey() });
    onCreated(path);
  }

  return (
    <Modal title="New file" onClose={onClose}>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <Field
          label="File name"
          value={name}
          onChange={edit(setName)}
          placeholder="note.md"
          hint=".md becomes a Markdown page; any other extension a code page."
        />
        <Field label="Folder" value={folder} onChange={edit(setFolder)} placeholder="(root)" />
        {isMarkdown && (
          <Field
            label="Title (optional)"
            value={title}
            onChange={edit(setTitle)}
            hint="Written as the # heading, which the site uses as the page title."
          />
        )}
        {error && (
          <p role="alert" className="mb-3 text-sm text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" disabled={save.isPending}>
            {save.isPending ? "Creating…" : "Create"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
