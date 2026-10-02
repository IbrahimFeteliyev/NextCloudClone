# Universal file preview

Click a file name in either list or grid view, or choose **Open / Preview** from its action menu. A full-size in-app viewer opens. Only the explicit **Download** action saves a local copy. Unsupported types open a metadata panel with a download button.

| Format | Viewer |
| --- | --- |
| MP4, WebM, OGG/OGV, M4V, MOV and other `video/*` | Video.js 8; playback depends on browser codec support |
| JPEG, PNG, WebP, GIF, SVG and other `image/*` | PhotoSwipe 5; responsive fit and zoom |
| PDF | PDF.js 6; page navigation, zoom, scrolling and lazy page rendering |
| Markdown | react-markdown 10 + remark-gfm; headings, lists, links, code and tables |
| TXT, LOG | Read-only text viewer, preserving line breaks |
| CSV | Papa Parse 5 + TanStack Table 8; delimiter detection, quoted cells, header override and pagination |
| DOCX, XLSX, PPTX | Existing ONLYOFFICE signed view/edit and collaborative save flow |

`frontend/src/features/file-preview/previewResolver.ts` selects by specific MIME first, then extension for missing/octet-stream MIME. Plain-text Markdown/CSV uploads use their extension because browsers often report them as `text/plain`. The explorer now includes the normalized content type. Metadata is refreshed and READ checked when opening each non-Office preview; Office uses its existing configuration endpoint.

## API and permissions

- `POST /api/files/{id}/preview`: authenticated READ check; returns name, MIME, size, version and a 15-minute content ticket.
- `GET /api/files/{id}/content`: inline content, authenticated by session Bearer or a scoped preview ticket. Every request checks current READ access, including inherited folder access, explicit overrides and deny grants. Native media uses the ticket because video elements cannot attach the session's Bearer header.
- `HEAD /api/files/{id}/content`: same authorization and headers without reading storage.
- Existing `GET /api/files/{id}/download`: READ checked attachment download, unchanged.

Tickets use the existing configured `Jwt:Key` and issuer with a separate `atlas-file-preview` audience and content purpose. They are restricted to one user/file/version, expire after 15 minutes, and cannot authenticate application endpoints or downloads. A revoked READ grant blocks the next request; a changed file version returns 409. Close and reopen an expired preview to renew its ticket. MinIO stays private and its object keys are never exposed. There are **no new environment variables**; the existing `VITE_API_URL`, JWT and MinIO configuration apply.

Content responses supply Content-Type, Content-Length, inline disposition, Accept-Ranges, version ETag and no-store. One byte range, suffix ranges and open-ended ranges return 206; invalid/unsatisfiable/multiple ranges return 416 with total size. If-Range matching the current ETag preserves the range; mismatches return the full current representation. The API passes offset and length to MinIO and copies directly into the response with cancellation, without building a whole-video memory buffer. CORS exposes the headers PDF.js needs.

Markdown excludes raw HTML and unsafe URL protocols; external images are represented as labels to avoid tracking. SVG is sanitized with DOMPurify, removes embedded HTML/scripts/events/external references, and is displayed as an image. Text and CSV are rendered as escaped React text. Direct inline responses also use `nosniff` and a restrictive sandbox CSP.

## Demo limits

To keep large files responsive, text reads only 512 KB, Markdown 256 KB, and CSV 1 MB. CSV shows at most 5,000 records and 100 columns with 100 rows per page; header detection can be overridden. A truncated final CSV record is discarded. Images are limited to 30 MB, SVG to 2 MB. PDF displays up to 200 pages, rendering only nearby canvases and releasing distant ones. Visible notices explain limits and provide the original Download action. Protected content already loaded into a user's browser remains visible after revocation, as with normal file downloads.

PDF workers are bundled locally. `frontend/scripts/copy-pdf-assets.mjs`, run by `npm run dev/build`, copies fonts, character maps and WASM from the installed PDF.js package into generated public assets. Viewer libraries load in separate chunks; dev dependency optimization prevents the first viewer open from triggering a Vite reload.

## Run and test

1. Start Docker Desktop and run `./scripts/Start-OnlyOfficeDemo.ps1` from the project root to start PostgreSQL, MinIO, ONLYOFFICE and the API with the existing configuration.
2. In another terminal, run `cd frontend`, `npm install`, then `npm run dev`.
3. Open `http://127.0.0.1:5173`, sign in with Finance (`finance@demo.local`, `Demo123!`), upload files into a folder and click their names. Test list/grid and context-menu preview, Close/Escape, explicit Download, image zoom, video playback/seek, PDF pages/zoom, Markdown formatting and CSV header override.
4. Share the test folder with Manager using READ, sign in as Manager and verify previews work while mutation actions and Office editing remain disabled. Deny READ on a file and confirm new content requests fail.
5. Run the automated checks:

```powershell
dotnet build backend
dotnet test backend.Tests
cd frontend
npm test
npm run build
npm audit
```

Optional disposable fixtures: `python scripts/Create-PreviewFixtures.py .cache/preview-fixtures` (requires Pillow/reportlab). Add a browser-compatible MP4 named `demo.mp4` and an Office file to that directory. From PowerShell 7:

```powershell
./scripts/Test-FilePreview.ps1 -FixtureDirectory "$PWD/.cache/preview-fixtures"
./scripts/Test-OnlyOffice.ps1
```

The preview smoke test creates a new demo folder, retains fixtures for inspection and records its folder/file IDs in ignored `.cache/preview-live.json`. `-ExistingFolderId` reuses an existing test folder. It validates real MinIO range byte equality, suffix and invalid ranges, inline versus attachment, READ inheritance, direct deny, ticket revocation and missing-file handling. The Office smoke test exercises its original configuration and security flow independently.

Verified locally: both builds; 60 backend tests; 33 frontend tests; 22 preview HTTP checks against PostgreSQL/MinIO; all 14 existing ONLYOFFICE HTTP checks; browser playback and seeking to 50%; PDF page 2 at 125% zoom; image/SVG zoom, CSV quoted values/header override, Markdown, escaped TXT and bounded large-log preview. Grid/context-menu previews, unsupported-file fallback, Escape to close, and image fit/actions at 390 × 844 were checked. Audit records confirm filename clicks create no download events; the explicit ZIP Download button created exactly one event. A DOCX was edited and saved through ONLYOFFICE, and its version 2 XML retrieved from MinIO contains the edited text. Browser screenshots are saved beside this guide.

## Changed files

- Backend: `Controllers/FileContentController.cs`, `Services/FileContentService.cs`, `Storage/ObjectStorage.cs`, `Services/DocumentService.cs`, `DTOs/Contracts.cs`, `Program.cs`, and `backend.Tests/FileContentTests.cs`.
- Frontend: `src/features/file-preview/` (resolver, modal, viewers, bounded content loading, styles and regression tests), `src/App.tsx`, `src/types.ts`, `src/onlyoffice.ts`, `src/components/OfficeEditor.tsx`, `scripts/copy-pdf-assets.mjs`, `vite.config.ts`, `package.json` and lockfile.
- Demo/docs: `scripts/Create-PreviewFixtures.py`, `scripts/Test-FilePreview.ps1`, this guide, preview screenshots, `README.md` and `.gitignore`.

