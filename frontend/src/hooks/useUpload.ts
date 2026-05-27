import { useState } from 'react';
import api from '../api/api';
import type { FileResponse } from '../types/file.types';

interface UploadState {
  isUploading: boolean;
  progress: number;
  result: FileResponse | null;
  error: string | null;
}

export function useUpload() {
  const [state, setState] = useState<UploadState>({
    isUploading: false,
    progress: 0,
    result: null,
    error: null,
  });

  const upload = async (
    file: File,
    options?: {
      maxDownloads?: number;
      expiresAt?: string;
      password?: string;
    }
  ) => {
    setState({ isUploading: true, progress: 0, result: null, error: null });

    const formData = new FormData();
    formData.append('file', file);
    if (options?.maxDownloads) formData.append('maxDownloads', String(options.maxDownloads));
    if (options?.expiresAt) formData.append('expiresAt', options.expiresAt);
    if (options?.password) formData.append('password', options.password);

    try {
      const response = await api.post<FileResponse>('/files', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: (progressEvent) => {
          const percent = progressEvent.total
            ? Math.round((progressEvent.loaded * 100) / progressEvent.total)
            : 0;
          setState((prev) => ({ ...prev, progress: percent }));
        },
      });

      setState({ isUploading: false, progress: 100, result: response.data, error: null });
      return response.data;
    } catch (err: any) {
      const errorMsg = err.response?.data?.error || 'Upload failed. Please try again.';
      setState({ isUploading: false, progress: 0, result: null, error: errorMsg });
      return null;
    }
  };

  const reset = () => {
    setState({ isUploading: false, progress: 0, result: null, error: null });
  };

  return { ...state, upload, reset };
}
