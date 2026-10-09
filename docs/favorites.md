# Favorites and outgoing shares

The sidebar adds Favorites and Shared by me while retaining Shared With Me, Recent and Audit Logs. Workspace capacity shows used bytes without a fixed 10 GB limit or an invented percentage bar.

Use a file/folder's three-dot menu → Add to favorites. Favorited names show a small star in list/grid view; the menu then offers Remove from favorites. Favorites are per user in PostgreSQL and persist across sessions/devices. READ-only users can star shared documents; stars do not alter document metadata, timestamps, permissions, MinIO objects or versions. Revoked READ access hides the item. Deleting a file/folder cascades its favorite metadata. The root folder cannot be favorited.

`GET /api/explorer?view=favorites` returns readable personal favorites. `GET /api/explorer?view=shared-by-me` returns owned resources with active direct grants to another user, excluding explicit zero-denial grants and inherited-only contents. This view does not list another owner's resources shared onward by a delegate. Searching stays scoped to each top-level view. Opening a folder browses its ordinary contents and uses the existing inherited permissions.

`PUT /api/resources/{file|folder}/{id}/favorite` accepts `{ "isFavorite": true }` or false. It requires the existing authenticated session and current READ permission. Explorer resources include `isFavorite`. The additive Favorites migration creates user/file/folder foreign keys, uniqueness per user/resource and a constraint selecting exactly one resource. Existing content is preserved; API startup applies the migration.

Changed: frontend App/types/preview/styles; backend entities/context/DTOs/WorkspaceController/DocumentService; Favorites migration and snapshot; PermissionTests; README and this guide. No packages or environment variables were added. Start with the usual `./scripts/Start-OnlyOfficeDemo.ps1` and `npm run dev`.

Verified: backend build via test compilation, 80 backend tests, frontend production build and 71 frontend tests; migration/model consistency; live PostgreSQL favorite add/search/remove and outgoing-share filtering. Browser verification is recorded in `favorites-sidebar.png` beside this guide.
