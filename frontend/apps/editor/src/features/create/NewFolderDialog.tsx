import { useState, type ChangeEvent, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getGetTreeQueryKey, useCreateFolder } from "../../api/generated/editor";
import { Button } from "../../components/Button";
import { Field } from "../../components/Field";
import { Modal } from "../../components/Modal";
import { joinPath, normalizeFolder } from "../../lib/paths";
import { createFailedMessage, createPathError, type ExistingPaths } from "./validation";

interface NewFolderDialogProps {
  /** Prefilled parent folder ("" for the root). */
  folder: string;
  /** Every path in the repository, for the exists and slug checks. */
  existing: ExistingPaths;
  onClose: () => void;
  /** Receives the folder's new README.md, which the editor opens. */
  onCreated: (readmePath: string) => void;
}

/**
 * Creates a folder. Git has no empty folders, so the backend commits the
 * folder's README.md (`# <title or name>`), which becomes its index page.
 */
export function NewFolderDialog({ folder: initialParent, existing, onClose, onCreated }: NewFolderDialogProps) {
  const [parent, setParent] = useState(initialParent);
  const [name, setName] = useState("");
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string>();
  const create = useCreateFolder();
  const queryClient = useQueryClient();

  // An error refers to the input it was shown for; editing clears it.
  const edit = (set: (value: string) => void) => (e: ChangeEvent<HTMLInputElement>) => {
    set(e.target.value);
    setError(undefined);
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (name === "") {
      setError("The name is required.");
      return;
    }
    const path = joinPath(normalizeFolder(parent), name.normalize("NFC"));
    const problem = createPathError(path, existing) ?? createPathError(`${path}/README.md`, existing);
    if (problem) {
      setError(problem);
      return;
    }

    let readme: string;
    try {
      const result = await create.mutateAsync({ data: { path, title: title.trim() || null } });
      readme = result.path;
    } catch (err) {
      setError(createFailedMessage(err, path));
      return;
    }
    await queryClient.invalidateQueries({ queryKey: getGetTreeQueryKey() });
    onCreated(readme);
  }

  return (
    <Modal title="New folder" onClose={onClose}>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <Field label="Folder name" value={name} onChange={edit(setName)} placeholder="Postgres" />
        <Field label="Parent folder" value={parent} onChange={edit(setParent)} placeholder="(root)" />
        <Field
          label="Title (optional)"
          value={title}
          onChange={edit(setTitle)}
          hint="Heading of the folder's README.md, its index page. Defaults to the folder name."
        />
        {error && (
          <p role="alert" className="mb-3 text-sm text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" disabled={create.isPending}>
            {create.isPending ? "Creating…" : "Create"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
