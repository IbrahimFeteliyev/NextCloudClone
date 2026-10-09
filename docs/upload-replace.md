# Confirmed upload replacement

Uploading an existing file name returns 409 with `details: { fileId, version, canReplace: true }` when the user has READ + WRITE on both the folder and the file. The upload queue pauses and offers Replace existing file, then Confirm replace, or Cancel upload. It never replaces automatically.

The confirmed multipart request to POST `/api/files/upload` includes `replace=true`, `replaceFileId`, and `expectedVersion`. The backend checks the target ID, location, name, revision and permissions again under the shared document lock. An active ONLYOFFICE session blocks replacement. Stale confirmations require a fresh retry. A folder with the same name cannot be replaced by a file; choose a different file name. Rename, move and folder creation retain their name-conflict checks.

Replacement uses FileVersionService.Replace in a database transaction. It keeps file ID, name, owner, created date, parent, favorites and grants. Uploaded content type, size, updated date and internal revision change. Saved manual checkpoints remain available; no automatic checkpoint is created. Failed writes roll back metadata and clean up new objects. REPLACE_FILE is audited. Superseded objects are cleaned up only if unreferenced.

The selection checkboxes were removed because no bulk operation exists. Trash now shares file explorer panel, filters, sorting, list/grid controls, icons, action menus and footer styles. Restore stays owner-only, and 30-day automatic deletion remains disabled.

No environment variables or schema migrations are added by this change. Restart the API and refresh the frontend.
