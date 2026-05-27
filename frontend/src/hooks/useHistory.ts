import { useState, useEffect, useCallback } from 'react';
import api from '../api/api';
import type { FileResponse } from '../types/file.types';

interface HistoryState {
  isLoading: boolean;
  files: FileResponse[];
  error: string | null;
}

export function useHistory() {
  const [state, setState] = useState<HistoryState>({
    isLoading: true,
    files: [],
    error: null,
  });

  const fetchHistory = useCallback(async () => {
    setState({ isLoading: true, files: [], error: null });
    try {
      const response = await api.get<FileResponse[]>('/files/my-uploads');
      setState({ isLoading: false, files: response.data, error: null });
    } catch (err: any) {
      const errorMsg = err.response?.data?.error || 'Failed to load history.';
      setState({ isLoading: false, files: [], error: errorMsg });
    }
  }, []);

  const deleteFile = async (code: string) => {
    try {
      await api.delete(`/files/${code}`);
      setState((prev) => ({
        ...prev,
        files: prev.files.filter((f) => f.code !== code),
      }));
      return true;
    } catch (err: any) {
      return false;
    }
  };

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  return { ...state, refresh: fetchHistory, deleteFile };
}
