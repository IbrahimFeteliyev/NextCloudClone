# Trash and workspace controls

Delete soft-deletes files or entire folder trees. Structure, content, saved versions, shares and favorites remain stored. Deleted resources are excluded from normal queries and rejected by previews, downloads, editing and permission checks. Active Office sessions are closed. DELETE on every affected child is still required.

Trash lists top-level deleted resources owned by the signed-in user. Collaborator deletions appear in the owner's Trash. Only the owner can restore. Restore returns the tree to its original parent, preserves permissions and rejects active name conflicts. Restore a deleted parent first if necessary.

Retention is deletion time plus 30 days. No automatic cleanup or expiry cutoff is implemented; items remain recoverable after 30 days. There is no permanent-delete UI in this change.

- GET `/api/trash`
- POST `/api/trash/{type}/{id}/restore`
- GET `/api/explorer?view=recent&page=1&pageSize=20`: includes total/page/pageSize, without the previous 30-item cap.
- GET `/api/audit/page?page=1&pageSize=20`: items/total/page/pageSize after permission filtering. Existing GET `/api/audit` remains available.

Recent and Audit Logs use 20 items per page. Stars are clickable 21px buttons with pressed state and accessible labels, using private favorites in list/grid views. The sidebar sharing promotion is removed.

Restart with `./scripts/Start-OnlyOfficeDemo.ps1` to apply TrashAndManualVersions and OfficeSessionContent migrations automatically; refresh the frontend. No new environment variables are required.
