import { Button } from "../../components/Button";
import { Modal } from "../../components/Modal";

interface ConflictDialogProps {
  /** Discards the local edits and loads the current version. */
  onReload: () => void;
  onKeepEditing: () => void;
}

/** Shown when a save is rejected with 409: the file's SHA on GitHub moved on. */
export function ConflictDialog({ onReload, onKeepEditing }: ConflictDialogProps) {
  return (
    <Modal title="This file changed on GitHub since you opened it" onClose={onKeepEditing}>
      <p className="mb-2 text-sm">Saving now would overwrite those changes, so it was not saved.</p>
      <p className="mb-5 text-sm text-content-muted">
        Reload discards your edits and loads the current version. To keep your text, choose Keep
        editing, copy it out, then reload.
      </p>
      <div className="flex justify-end gap-2">
        <Button onClick={onKeepEditing}>Keep editing</Button>
        <Button variant="primary" onClick={onReload}>
          Reload
        </Button>
      </div>
    </Modal>
  );
}
