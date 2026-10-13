# Departments, groups and Team Folders demo

This demo uses local PostgreSQL records and the existing JWT login. There is no Active Directory, LDAP or ERP connection, and no external identity synchronization. Employee roles and departments are separate: changing a department does not change the Employee/Manager/Admin role.

## Try it

Start Docker Desktop, then run `./scripts/Start-OnlyOfficeDemo.ps1` from the repository. In another terminal run `npm run dev` from `frontend`. The API applies the new migration and adds the organization demo to an existing database once, without replacing personal files or existing shares. No new environment variables are required.

Open http://127.0.0.1:5173 and sign in with password `Demo123!`:

| Account | Department | Default Team Folders |
| --- | --- | --- |
| admin@demo.local | None | All five folders, with full access |
| finance@demo.local | Finance | All Staff (read), Finance (full access) |
| manager@demo.local | Operations | All Staff (read), Operations (full access) |
| employee@demo.local | Human Resources | All Staff (read), Human Resources (full access) |

The fifth Team Folder is IT. Each Team Folder contains a nested folder and a sample Markdown document. The seed runs only when the All Staff group does not exist; restarting does not reset departments, memberships or permissions.

As admin, open **avatar → Organization & Team Folders** to create departments, employees, extra groups and Team Folders. Creating a department creates its managed group. Creating an employee gives them All Staff membership and, when selected, their department membership. Changing an employee's department removes their old department membership and adds the new one. Extra group memberships and individual shares remain in place.

Use **Share → Groups & departments → Add group** on a file or folder to grant group permissions. Team content opens on this tab by default. The **People** tab separates individual permissions from access inherited through groups or parent folders; **Set exception** creates an individual override. Editors stay collapsed until Add, Edit or Override is selected. Choose Can view (1), Can edit (3), Full access (15) or No access (0); Customize permissions preserves the individual permission controls. To demonstrate an exception, share a Finance document directly with the HR employee; remove that direct grant to restore the usual department restriction. To demonstrate project groups, create an extra group, add employees from different departments and create a Team Folder for that group.

## Data model

| Model | Purpose |
| --- | --- |
| Department | Named department; optional `User.DepartmentId` links an employee to it |
| Group | Department group, All Staff, Workspace Administrators, or a manually managed project group |
| UserGroup | Composite user/group membership; evaluated from the database on each request |
| TeamSpace | Organizational ownership scope for a Team Folder and its contents |
| Folder | `TeamSpaceId` identifies team content; `IsTeamRoot` identifies its protected top-level folder |
| Document | Inherits `TeamSpaceId` from its containing folder |
| GroupPermission | One group grant for either a folder or a file, using the existing permission bits |

Existing personal `OwnerId` relationships remain compatible. Team content uses an inactive technical storage account (`team-storage@demo.local`) for that required foreign key. It has no login password and cannot sign in. The actual team scope is `TeamSpaceId`; the UI shows the team name as owner. No employee receives an owner bypass for team content, including the employee who uploads a document.

## Permission rules

READ=1, WRITE=2, DELETE=4, SHARE=8. Full access is 15. Every nonzero grant must include READ; zero means explicit No access.

Resolution starts at the file or selected folder and walks toward its parent:

1. Personal ownership gives full access for personal content only.
2. An individual grant at that level wins, including an explicit No access grant.
3. Otherwise, applicable group grants at that level are combined. Any applicable No access group grant denies access at that level.
4. If that level has no applicable grant, use its parent. A nearer explicit grant replaces inherited access.

Consequently an individual exception can override a group restriction, and a child grant can replace a parent's permissions. Removing a direct grant restores inheritance. Share controls require READ + SHARE; users cannot grant bits they do not possess. Admin role controls organization management, but does not bypass resource authorization. Newly created Team Folders receive an explicit Workspace Administrators grant.

All explorer, download, preview, text editing, upload, sharing and ONLYOFFICE operations reuse `PermissionService`. ONLYOFFICE remains view-only for READ and enables editing for READ + WRITE; callbacks recheck current editing permissions. Department and membership changes take effect on subsequent requests using the existing token. Deactivating an employee rejects their existing authenticated token and removes their resource access. Bytes already downloaded cannot be recalled.

## Existing behavior

Team root folders cannot be renamed, moved or deleted through normal file actions. Team content can move within the same team scope with READ + WRITE + SHARE and a writable destination; cross-team/personal moves are rejected. Deleted team content uses the existing Trash mechanism; users with SHARE at the Team root can manage its recovery, subject to backend permission checks. The 30-day retention label remains informational; no automatic purge runs.

Storage stays in MinIO. Upload replacement, text/Office saves and manual version snapshots reuse the existing storage and version services. Editing does not automatically create a user-visible version snapshot. Favorites remain private to each employee. Team Folders and group shares are visible through Team Folders and Shared With Me; Shared by me continues to represent personally owned outgoing shares.

## Endpoints and implementation

Authenticated catalog: `GET /api/organization`. Admin management: `GET /api/organization/admin`, `POST /api/organization/departments`, `POST /api/organization/groups`, `POST /api/organization/users`, `PUT /api/organization/users/{id}/department`, `PUT /api/organization/users/{id}/active`, `PUT /api/organization/groups/{groupId}/members/{userId}`, and `POST /api/team-folders`.

Group sharing: `GET/PUT /api/resources/{type}/{id}/group-shares` and `DELETE /api/resources/{type}/{id}/group-shares/{groupId}`. The existing explorer accepts `view=teams`.

Core changes are in `Models.cs`, `AppDbContext.cs`, the `DepartmentsAndTeamFolders` migration, `TeamDemoSeed`, `OrganizationService`, `OrganizationController`, `PermissionService`, `DocumentService`, `AuthController` and `Program.cs`. Frontend changes include `OrganizationDialog`, `GroupAccess`, `ShareDialog`, `App`, resource types and styles.

Automated coverage includes department isolation, individual exceptions, group union/denial, permission escalation rejection, team ownership, team root protection, trash recovery, dynamic memberships and ONLYOFFICE permission changes. React tests cover group sharing, department changes, read-only controls and Team Folders navigation. Live API checks additionally verify PostgreSQL/MinIO download permissions and inactive token rejection.
