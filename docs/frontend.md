---
name: frontend-patterns
description: React 18 + TypeScript patterns for File & Image Sharing Service — components, API calls, hooks, routing, upload progress. Use when writing any React/frontend code.
---

# Frontend Patterns — React 18 + TypeScript

## Project Setup

```bash
npm create vite@latest frontend -- --template react-ts
cd frontend && npm install axios react-router-dom
```

---

## API Wrapper (`src/api/api.ts`)

```typescript
import axios from 'axios';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:5000/api',
});

api.interceptors.request.use(cfg => {
  const token = localStorage.getItem('token');
  if (token) cfg.headers.Authorization = `Bearer ${token}`;
  return cfg;
});

export default api;
```

---

## Types (`src/types/file.types.ts`)

```typescript
export interface FileInfo {
  code: string;
  originalFilename: string;
  mimeType: string;
  sizeBytes: number;
  createdAt: string;
  expiresAt: string | null;
  maxDownloads: number | null;
  downloadCount: number;
  url: string;
}

export interface UploadOptions {
  maxDownloads?: number;
  expiryHours?: number;
  password?: string;
}
```

---

## Custom Hook — useUpload (`src/hooks/useUpload.ts`)

```typescript
import { useState } from 'react';
import api from '../api/api';
import type { FileInfo, UploadOptions } from '../types/file.types';

export function useUpload() {
  const [progress, setProgress] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const upload = async (file: File, options: UploadOptions = {}): Promise<FileInfo | null> => {
    setLoading(true);
    setError(null);
    setProgress(0);

    const form = new FormData();
    form.append('file', file);
    if (options.maxDownloads) form.append('maxDownloads', String(options.maxDownloads));
    if (options.expiryHours) form.append('expiryHours', String(options.expiryHours));

    try {
      const { data } = await api.post<FileInfo>('/files', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: e => setProgress(Math.round((e.loaded * 100) / (e.total ?? 1))),
      });
      return data;
    } catch (err: any) {
      setError(err.response?.data?.error ?? 'Upload failed');
      return null;
    } finally {
      setLoading(false);
    }
  };

  return { upload, progress, loading, error };
}
```

---

## Custom Hook — useFileInfo (`src/hooks/useFileInfo.ts`)

```typescript
import { useEffect, useState } from 'react';
import api from '../api/api';
import type { FileInfo } from '../types/file.types';

export function useFileInfo(code: string) {
  const [file, setFile] = useState<FileInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    api.get<FileInfo>(`/files/${code}/meta`)
      .then(r => setFile(r.data))
      .catch(e => { if (e.response?.status === 404) setNotFound(true); })
      .finally(() => setLoading(false));
  }, [code]);

  return { file, loading, notFound };
}
```

---

## DropZone Component (`src/components/DropZone.tsx`)

```typescript
import { useRef, useState, DragEvent } from 'react';

interface Props {
  onFile: (file: File) => void;
  disabled?: boolean;
}

export function DropZone({ onFile, disabled }: Props) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const file = e.dataTransfer.files[0];
    if (file) onFile(file);
  };

  return (
    <div
      onDragOver={e => { e.preventDefault(); setDragging(true); }}
      onDragLeave={() => setDragging(false)}
      onDrop={handleDrop}
      onClick={() => inputRef.current?.click()}
      className={`dropzone ${dragging ? 'dragging' : ''}`}
    >
      <input
        ref={inputRef}
        type="file"
        hidden
        disabled={disabled}
        onChange={e => { const f = e.target.files?.[0]; if (f) onFile(f); }}
      />
      <p>Drag & drop a file here, or click to select</p>
      <p className="hint">Max 10 MB</p>
    </div>
  );
}
```

---

## ProgressBar Component (`src/components/ProgressBar.tsx`)

```typescript
interface Props { value: number; }

export function ProgressBar({ value }: Props) {
  return (
    <div className="progress-track">
      <div className="progress-fill" style={{ width: `${value}%` }} />
      <span>{value}%</span>
    </div>
  );
}
```

---

## ImagePreview Component (`src/components/ImagePreview.tsx`)

```typescript
const IMAGE_MIMES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];

interface Props { code: string; mimeType: string; filename: string; }

export function ImagePreview({ code, mimeType, filename }: Props) {
  const isImage = IMAGE_MIMES.includes(mimeType);
  const src = `/api/files/${code}`;

  if (!isImage) return null;
  return (
    <div className="image-preview">
      <img src={src} alt={filename} loading="lazy" />
    </div>
  );
}
```

---

## Pages

### UploadPage (`src/pages/UploadPage.tsx`)

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
    navigator.clipboard.writeText(`${window.location.origin}/f/${result?.code}`);
  };

  return (
    <div className="page">
      <h1>Upload a File</h1>
      <DropZone onFile={handleFile} disabled={loading} />
      {loading && <ProgressBar value={progress} />}
      {error && <p className="error">{error}</p>}
      {result && (
        <div className="result">
          <p>Your link: <code>/f/{result.code}</code></p>
          <button onClick={copyLink}>Copy Link</button>
        </div>
      )}
    </div>
  );
}
```

### FilePage (`src/pages/FilePage.tsx`)

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
      <p>{(file.sizeBytes / 1024).toFixed(1)} KB · {file.mimeType}</p>
      <ImagePreview code={file.code} mimeType={file.mimeType} filename={file.originalFilename} />
      <a href={`/api/files/${file.code}`} download={file.originalFilename}>
        <button>Download</button>
      </a>
      {file.maxDownloads && (
        <p>{file.downloadCount} / {file.maxDownloads} downloads used</p>
      )}
    </div>
  );
}
```

---

## Routing (`src/App.tsx`)

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

---

## Env Variables

```env
# frontend/.env
VITE_API_URL=http://localhost:5000/api
```
