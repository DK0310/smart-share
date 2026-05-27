import { useState, useEffect } from 'react';
import api from '../api/api';
import type { FileResponse } from '../types/file.types';

interface FileInfoState {
  isLoading: boolean;
  file: FileResponse | null;
  error: string | null;
}

export function useFileInfo(code: string | undefined) {
  const [state, setState] = useState<FileInfoState>({
    isLoading: true,
    file: null,
    error: null,
  });

  useEffect(() => {
    if (!code) {
      setState({ isLoading: false, file: null, error: 'No file code provided.' });
      return;
    }

    const fetchFile = async () => {
      setState({ isLoading: true, file: null, error: null });
      try {
        const response = await api.get<FileResponse>(`/files/${code}/meta`);
        setState({ isLoading: false, file: response.data, error: null });
      } catch (err: any) {
        const errorMsg = err.response?.data?.error || 'File not found.';
        setState({ isLoading: false, file: null, error: errorMsg });
      }
    };

    fetchFile();
  }, [code]);

  return state;
}
