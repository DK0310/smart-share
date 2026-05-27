---
name: fileshare-frontend
description: Use when building React components, pages, hooks, or integrating with the backend API in the File & Image Sharing Service
---

# Smart Share — Frontend Patterns Skill

## Overview

The frontend is a React 18 SPA built with TypeScript and Vite. It communicates with the ASP.NET Core backend via Axios. Components are small and focused. Complex logic (API calls, state management) lives in custom hooks, not in components.

**Core rule:** Components render UI. Hooks handle logic. Never mix API calls with JSX.

---

## Project Structure

```
frontend/
├── src/
│   ├── api/
│   │   └── api.ts                    ← Axios instance + JWT interceptor
│   │
│   ├── types/
│   │   └── file.types.ts             ← TypeScript interfaces for API data
│   │
│   ├── hooks/
│   │   ├── useUpload.ts              ← File upload with progress tracking
│   │   └── useFileInfo.ts            ← Fetch file metadata by code
│   │
│   ├── components/
│   │   ├── DropZone.tsx              ← Drag-and-drop file selection
│   │   ├── ProgressBar.tsx           ← Upload progress indicator
│   │   └── ImagePreview.tsx          ← Inline image preview
│   │
│   ├── pages/
│   │   ├── UploadPage.tsx            ← Main upload interface
│   │   ├── FilePage.tsx              ← File view/download page
│   │   └── HistoryPage.tsx           ← User's upload history
│   │
│   └── App.tsx                       ← Router setup
│
├── .env                              ← VITE_API_URL=http://localhost:5000/api
├── vite.config.ts                    ← Vite configuration
├── package.json
└── tsconfig.json
```

---

## API Client Setup

**Location:** `frontend/src/api/api.ts`

This is the single point of contact with the backend. Every API call goes through this instance.

```typescript
import axios from 'axios';

// Create axios instance with base URL from environment
const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:5000/api',
});

// ── Request interceptor: Inject JWT token ─────────────────────
// Automatically adds Authorization header to every request
// if a token exists in localStorage
api.interceptors.request.use(cfg => {
  const token = localStorage.getItem('token');
  if (token) {
    cfg.headers.Authorization = `Bearer ${token}`;
  }
  return cfg;
});

// ── Response interceptor: Handle 401 globally ─────────────────
// If ANY request returns 401, the token is expired/invalid.
// Clear it and redirect to login.
api.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export default api;
```

**Rules:**
- NEVER create a second axios instance — always import `api` from this file
- NEVER manually add `Authorization` headers — the interceptor handles it
- NEVER hardcode the API URL — use `VITE_API_URL` environment variable

---

## TypeScript Types

**Location:** `frontend/src/types/file.types.ts`

These types must match the backend's response DTOs exactly.

```typescript
// Matches backend FileResponse DTO
export interface FileInfo {
  code: string;
  originalFilename: string;
  mimeType: string;
  sizeBytes: number;
  createdAt: string;          // ISO 8601 string from backend
  expiresAt: string | null;   // null = no expiration
  maxDownloads: number | null; // null = unlimited
  downloadCount: number;
  url: string;                // Computed: "/f/{code}"
}

// Options for upload request
export interface UploadOptions {
  maxDownloads?: number;
  expiryHours?: number;
  password?: string;
}

// Matches backend LoginRequest/RegisterRequest
export interface AuthRequest {
  email: string;
  password: string;
}

// Matches backend auth response
export interface AuthResponse {
  token: string;
}
```

**When backend adds a new field to FileResponse:** Add the corresponding property here. TypeScript will flag any component that needs updating.

---

## Custom Hooks

### useUpload — File Upload with Progress

**Location:** `frontend/src/hooks/useUpload.ts`

```typescript
import { useState } from 'react';
import api from '../api/api';
import type { FileInfo, UploadOptions } from '../types/file.types';

export function useUpload() {
  const [progress, setProgress] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const upload = async (
    file: File,
    options: UploadOptions = {}
  ): Promise<FileInfo | null> => {
    setLoading(true);
    setError(null);
    setProgress(0);

    // Build multipart form data
    const form = new FormData();
    form.append('file', file);
    if (options.maxDownloads)
      form.append('maxDownloads', String(options.maxDownloads));
    if (options.expiryHours)
      form.append('expiryHours', String(options.expiryHours));
    if (options.password)
      form.append('password', options.password);

    try {
      const { data } = await api.post<FileInfo>('/files', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: e =>
          setProgress(Math.round((e.loaded * 100) / (e.total ?? 1))),
      });
      return data;
    } catch (err: any) {
      setError(err.response?.data?.error ?? 'Upload failed');
      return null;
    } finally {
      setLoading(false);
    }
  };

  // Reset state for new upload attempt
  const reset = () => {
    setProgress(0);
    setLoading(false);
    setError(null);
  };

  return { upload, reset, progress, loading, error };
}
```

**Usage:**
```typescript
const { upload, progress, loading, error } = useUpload();
const result = await upload(file, { expiryHours: 24 });
if (result) {
  // Success — result is FileInfo
}
// If failed, error state is set automatically
```

### useFileInfo — Fetch File Metadata

**Location:** `frontend/src/hooks/useFileInfo.ts`

```typescript
import { useEffect, useState } from 'react';
import api from '../api/api';
import type { FileInfo } from '../types/file.types';

export function useFileInfo(code: string) {
  const [file, setFile] = useState<FileInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    let cancelled = false; // Prevent state update after unmount

    api.get<FileInfo>(`/files/${code}/meta`)
      .then(r => {
        if (!cancelled) setFile(r.data);
      })
      .catch(e => {
        if (!cancelled && e.response?.status === 404) setNotFound(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => { cancelled = true; }; // Cleanup on unmount
  }, [code]); // Re-fetch if code changes

  return { file, loading, notFound };
}
```

**Usage:**
```typescript
const { file, loading, notFound } = useFileInfo(code);
if (loading) return <p>Loading…</p>;
if (notFound) return <p>File not found</p>;
// file is now FileInfo
```

---

## Components

### DropZone — Drag and Drop File Selection

**Location:** `frontend/src/components/DropZone.tsx`

```typescript
import { useRef, useState, DragEvent } from 'react';

interface DropZoneProps {
  onFile: (file: File) => void;
  disabled?: boolean;
}

export function DropZone({ onFile, disabled }: DropZoneProps) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    if (disabled) return;
    const file = e.dataTransfer.files[0];
    if (file) onFile(file);
  };

  const handleDragOver = (e: DragEvent) => {
    e.preventDefault();
    if (!disabled) setDragging(true);
  };

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={() => setDragging(false)}
      onDrop={handleDrop}
      onClick={() => !disabled && inputRef.current?.click()}
      className={`dropzone ${dragging ? 'dragging' : ''} ${disabled ? 'disabled' : ''}`}
    >
      <input
        ref={inputRef}
        type="file"
        hidden
        disabled={disabled}
        onChange={e => {
          const f = e.target.files?.[0];
          if (f) onFile(f);
          // Reset input so same file can be selected again
          e.target.value = '';
        }}
      />
      <p>Drag & drop a file here, or click to select</p>
      <p className="hint">Max 10 MB</p>
    </div>
  );
}
```

### ProgressBar — Upload Progress

**Location:** `frontend/src/components/ProgressBar.tsx`

```typescript
interface ProgressBarProps {
  value: number; // 0-100
}

export function ProgressBar({ value }: ProgressBarProps) {
  return (
    <div className="progress-track">
      <div
        className="progress-fill"
        style={{ width: `${Math.min(value, 100)}%` }}
      />
      <span>{value}%</span>
    </div>
  );
}
```

### ImagePreview — Inline Image Display

**Location:** `frontend/src/components/ImagePreview.tsx`

```typescript
const IMAGE_MIMES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];

interface ImagePreviewProps {
  code: string;
  mimeType: string;
  filename: string;
}

export function ImagePreview({ code, mimeType, filename }: ImagePreviewProps) {
  if (!IMAGE_MIMES.includes(mimeType)) return null;

  return (
    <div className="image-preview">
      <img
        src={`/api/files/${code}`}
        alt={filename}
        loading="lazy"
      />
    </div>
  );
}
```

---

## Pages

### UploadPage — Main Upload Interface

**Location:** `frontend/src/pages/UploadPage.tsx`

```typescript
import { useState } from 'react';
import { DropZone } from '../components/DropZone';
import { ProgressBar } from '../components/ProgressBar';
import { useUpload } from '../hooks/useUpload';
import type { FileInfo } from '../types/file.types';

export function UploadPage() {
  const { upload, progress, loading, error } = useUpload();
  const [result, setResult] = useState<FileInfo | null>(null);

  const handleFile = async (file: File) => {
    const info = await upload(file, { expiryHours: 24 });
    if (info) setResult(info);
  };

  const copyLink = () => {
    const url = `${window.location.origin}/f/${result?.code}`;
    navigator.clipboard.writeText(url);
  };

  return (
    <div className="page">
      <h1>Upload a File</h1>

      <DropZone onFile={handleFile} disabled={loading} />

      {loading && <ProgressBar value={progress} />}

      {error && <p className="error">{error}</p>}

      {result && (
        <div className="result">
          <p>
            Your link: <code>/f/{result.code}</code>
          </p>
          <button onClick={copyLink}>Copy Link</button>
        </div>
      )}
    </div>
  );
}
```

### FilePage — View and Download

**Location:** `frontend/src/pages/FilePage.tsx`

```typescript
import { useParams } from 'react-router-dom';
import { useFileInfo } from '../hooks/useFileInfo';
import { ImagePreview } from '../components/ImagePreview';

export function FilePage() {
  const { code } = useParams<{ code: string }>();
  const { file, loading, notFound } = useFileInfo(code!);

  if (loading) return <p>Loading…</p>;
  if (notFound) return <p>File not found or expired.</p>;
  if (!file) return null;

  return (
    <div className="page">
      <h1>{file.originalFilename}</h1>
      <p>
        {(file.sizeBytes / 1024).toFixed(1)} KB · {file.mimeType}
      </p>

      <ImagePreview
        code={file.code}
        mimeType={file.mimeType}
        filename={file.originalFilename}
      />

      <a
        href={`/api/files/${file.code}`}
        download={file.originalFilename}
      >
        <button>Download</button>
      </a>

      {file.maxDownloads && (
        <p>
          {file.downloadCount} / {file.maxDownloads} downloads used
        </p>
      )}

      {file.expiresAt && (
        <p className="expires">
          Expires: {new Date(file.expiresAt).toLocaleDateString()}
        </p>
      )}
    </div>
  );
}
```

---

## Routing

**Location:** `frontend/src/App.tsx`

```typescript
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { UploadPage } from './pages/UploadPage';
import { FilePage } from './pages/FilePage';
import { HistoryPage } from './pages/HistoryPage';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<UploadPage />} />
        <Route path="/f/:code" element={<FilePage />} />
        <Route path="/history" element={<HistoryPage />} />
      </Routes>
    </BrowserRouter>
  );
}
```

**Adding a new route:**
1. Create page component in `src/pages/NewPage.tsx`
2. Add `<Route path="/new" element={<NewPage />} />` in `App.tsx`
3. Link to it: `<Link to="/new">Go</Link>` or `navigate('/new')`

---

## Environment Variables

**Location:** `frontend/.env`

```env
VITE_API_URL=http://localhost:5000/api
```

**Accessing in code:**
```typescript
const apiUrl = import.meta.env.VITE_API_URL;
```

**Rules:**
- All frontend env vars MUST start with `VITE_` (Vite requirement)
- Values are embedded at BUILD TIME, not runtime
- Different `.env` files: `.env` (dev), `.env.production` (prod build)

---

## Error Handling Decision Tree

Use this for EVERY API call:

```
HTTP STATUS        WHAT TO DO                         USER MESSAGE
─────────────────────────────────────────────────────────────────────
200 OK            → Use response data                 (no error)
201 Created       → Use response data                 (no error)
204 No Content    → Operation succeeded               (no error)

400 Bad Request   → Show error.response.data.error    "File exceeds 10 MB"
                    to user (it's user-friendly)

401 Unauthorized  → Interceptor handles:              (auto-redirect to /login)
                    clear token, redirect to login

403 Forbidden     → Show "Access denied"              "You don't have permission"

404 Not Found     → Show "Not found"                  "File not found or expired"

409 Conflict      → Show specific error               "Email already registered"

500 Server Error  → Show generic error                "Something went wrong.
                    Log for debugging                  Please try again later."

Network Error     → No response object                "Check your internet
(no response)       err.response is undefined          connection"
```

### Implementation Pattern

```typescript
try {
  const { data } = await api.post('/endpoint', payload);
  // Handle success
} catch (err: any) {
  if (!err.response) {
    // Network error — no response from server
    setError('Check your internet connection');
    return;
  }

  const status = err.response.status;
  const message = err.response.data?.error;

  switch (status) {
    case 400:
      setError(message ?? 'Invalid request');
      break;
    case 404:
      setError('Not found');
      break;
    case 403:
      setError('Access denied');
      break;
    default:
      setError('Something went wrong. Please try again.');
      break;
  }
  // Note: 401 is handled by the response interceptor in api.ts
}
```

---

## State Management Decision Tree

```
WHERE SHOULD THIS STATE LIVE?

├─ Used by ONE component only?
│  └─ Component's own useState
│     const [value, setValue] = useState(initial);
│
├─ Used by MULTIPLE components on same page?
│  └─ Lift state up to common parent, pass as props
│
├─ Involves API call or complex logic?
│  └─ Custom hook (src/hooks/use{Name}.ts)
│     Hook manages useState + API calls
│     Component just calls hook and renders
│
├─ Needs to persist across page reloads?
│  └─ localStorage
│     Token: localStorage.getItem('token')
│     Theme: localStorage.getItem('theme')
│
├─ Needed globally across ALL pages?
│  └─ React Context + custom hook
│     Create context, provide at App level
│     Components consume via useContext
│
└─ Server data that multiple components read?
   └─ Custom hook with useEffect fetch
      Each component calls the same hook independently
      (or lift to parent and pass as props)
```

---

## Component Design Decision Tree

```
WHAT TYPE OF COMPONENT DO I NEED?

├─ Full page with its own route?
│  └─ PAGE component → src/pages/{Name}Page.tsx
│     - Registered in App.tsx <Route>
│     - Calls hooks for data
│     - Coordinates child components
│
├─ Reusable UI element?
│  └─ COMPONENT → src/components/{Name}.tsx
│     - Accepts ALL data via props
│     - No API calls inside
│     - No complex business logic
│     - Example: DropZone, ProgressBar, ImagePreview
│
├─ Needs to fetch data from API?
│  └─ HOOK → src/hooks/use{Name}.ts
│     - Returns { data, loading, error }
│     - Component calls hook, renders based on state
│     - Example: useUpload, useFileInfo
│
└─ Wraps another component with data fetching?
   └─ CONTAINER pattern
      - Hook fetches data
      - Container handles loading/error states
      - Renders child component with data props
```

---

## End-to-End Workflows

### Creating a New Page

Example: Adding a `/settings` page.

```
1. Create page: src/pages/SettingsPage.tsx
2. Create hook (if needs API): src/hooks/useSettings.ts
3. Add route in App.tsx:
   <Route path="/settings" element={<SettingsPage />} />
4. Add navigation link where needed:
   import { Link } from 'react-router-dom';
   <Link to="/settings">Settings</Link>
```

```typescript
// src/pages/SettingsPage.tsx
export function SettingsPage() {
  return (
    <div className="page">
      <h1>Settings</h1>
      {/* page content */}
    </div>
  );
}
```

### Creating a New Custom Hook

Example: Hook to fetch user's upload history.

```typescript
// src/hooks/useHistory.ts
import { useEffect, useState } from 'react';
import api from '../api/api';
import type { FileInfo } from '../types/file.types';

export function useHistory() {
  const [files, setFiles] = useState<FileInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    api.get<FileInfo[]>('/files/my-uploads')
      .then(r => { if (!cancelled) setFiles(r.data); })
      .catch(e => {
        if (!cancelled)
          setError(e.response?.data?.error ?? 'Failed to load history');
      })
      .finally(() => { if (!cancelled) setLoading(false); });

    return () => { cancelled = true; };
  }, []);

  return { files, loading, error };
}
```

### Creating a New Component

Example: Component to display file size in human-readable format.

```typescript
// src/components/FileSize.tsx

interface FileSizeProps {
  bytes: number;
}

export function FileSize({ bytes }: FileSizeProps) {
  const format = (b: number): string => {
    if (b < 1024) return `${b} B`;
    if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
    return `${(b / (1024 * 1024)).toFixed(1)} MB`;
  };

  return <span className="file-size">{format(bytes)}</span>;
}
```

### Adding Authentication to a Page

```typescript
// src/pages/ProtectedPage.tsx
import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

export function ProtectedPage() {
  const navigate = useNavigate();
  const token = localStorage.getItem('token');

  useEffect(() => {
    if (!token) {
      navigate('/login');
    }
  }, [token, navigate]);

  if (!token) return null; // Prevent flash of content

  return (
    <div className="page">
      <h1>Protected Content</h1>
      {/* Only renders if authenticated */}
    </div>
  );
}
```

---

## Testing Patterns

### Component Tests

```typescript
// src/components/ProgressBar.test.tsx
import { render, screen } from '@testing-library/react';
import { ProgressBar } from './ProgressBar';

test('displays progress percentage', () => {
  render(<ProgressBar value={50} />);
  expect(screen.getByText('50%')).toBeInTheDocument();
});

test('clamps width to 100%', () => {
  const { container } = render(<ProgressBar value={150} />);
  const fill = container.querySelector('.progress-fill');
  expect(fill).toHaveStyle({ width: '100%' });
});
```

### Hook Tests

```typescript
// src/hooks/useUpload.test.ts
import { renderHook, act } from '@testing-library/react';
import { useUpload } from './useUpload';

test('initializes with default state', () => {
  const { result } = renderHook(() => useUpload());

  expect(result.current.loading).toBe(false);
  expect(result.current.progress).toBe(0);
  expect(result.current.error).toBeNull();
});

test('sets loading during upload', async () => {
  const { result } = renderHook(() => useUpload());

  // Act would trigger the upload and check loading state
  await act(async () => {
    // Mock API call and verify loading transitions
  });
});
```

### Testing Checklist

```
□ Component renders without errors
□ Props pass through correctly
□ Event handlers fire on interaction
□ Loading state displays correctly
□ Error state displays correctly
□ Empty/null data handled gracefully
□ Conditional rendering works
□ No console errors or warnings
```

---

## Common Mistakes

| ❌ Don't | ✅ Do | Why |
|---|---|---|
| `axios.get()` directly in component | Use custom hook with `useEffect` | Separation of concerns, reusability |
| API call in render body (no useEffect) | Wrap in `useEffect` with dependency array | Prevents infinite re-render loop |
| `useEffect(() => {...})` (no deps) | `useEffect(() => {...}, [dep])` | Missing deps = runs every render |
| `useState` for JWT token | `localStorage.getItem('token')` | Token must persist across page reloads |
| Multiple `setState` calls scattered | One custom hook managing all related state | Single source of truth |
| `any` type everywhere | Define interfaces in `types/` | TypeScript catches bugs at compile time |
| Ignore TypeScript errors | Fix them — they're preventing real bugs | TS errors = potential runtime errors |
| Block UI during async ops | Show loading state (spinner, disabled button) | User needs feedback |
| Forget error handling in catch | Always `setError(message)` in catch block | Users need to know what went wrong |
| Create new axios instance | Import `api` from `src/api/api.ts` | Single instance with interceptors |
| Manual `Authorization` header | Let axios interceptor handle it | DRY, consistent |
| `console.log` for debugging in prod | Remove before commit | Clean console output |

---

## Cross-References

- **Backend API endpoints** → `fileshare-backend/SKILL.md`
- **System architecture** → `fileshare-architecture/SKILL.md`
- **Deploying frontend** → `fileshare-devops/SKILL.md`
- **Database schema (for understanding API shapes)** → `fileshare-database/SKILL.md`
