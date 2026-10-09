# Manual file versions

Open a file's three-dot menu, select **Version history**, then **Save current version**. Save a checkpoint before editing to keep the previous content. Saving unchanged content is idempotent. Unsaved ONLYOFFICE changes must first reach the backend through its save callback.

Normal uploads and text/ONLYOFFICE saves do not create snapshots. An internal content revision still increments for stale-write detection and preview/editor cache keys. Existing snapshots remain available. Superseded objects without saved snapshots or active editor references are removed after successful saves. ONLYOFFICE stores initial session content and callback receipts independently of history.

READ allows history/download; READ + WRITE allows checkpoints and restore, enforced by the backend. Restore replaces current content and preserves saved snapshots, file identity and permissions. Uncheckpointed current content is not retained. Close active Office sessions first; abandoned sessions can block restore until expiry.

- GET `/api/files/{id}/versions`: snapshots with author/date/size/isCurrent/currentVersion.
- POST `/api/files/{id}/versions`: manual checkpoint (204).
- DELETE `/api/files/{id}/versions/{number}`: permanently remove a checkpoint; requires READ + DELETE. Current file content and active editor content references are preserved. Unreferenced snapshot storage is cleaned up after the database commit. DELETE_FILE_VERSION is audited.
- GET `/api/files/{id}/versions/{number}/download`: checkpoint download.
- POST `/api/files/{id}/versions/{number}/restore`: `{ "expectedVersion": 2 }` (204).

Missing resources return 404, insufficient permissions 403, stale content/active sessions 409. SAVE_FILE_VERSION audits manual checkpoints, RESTORE_FILE_VERSION audits restores, EDIT_FILE audits ordinary saves. See trash-and-pagination.md for required migrations.
