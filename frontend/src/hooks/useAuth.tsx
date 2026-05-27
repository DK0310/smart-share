import { useState, useCallback, createContext, useContext } from 'react';
import type { ReactNode } from 'react';
import api from '../api/api';
import type { AuthResponse } from '../types/file.types';

interface AuthState {
  isAuthenticated: boolean;
  email: string | null;
  token: string | null;
}

interface AuthContextType extends AuthState {
  login: (email: string, password: string) => Promise<string | null>;
  register: (email: string, password: string) => Promise<string | null>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(() => {
    const token = localStorage.getItem('token');
    const email = localStorage.getItem('email');
    return {
      isAuthenticated: !!token,
      email,
      token,
    };
  });

  const login = useCallback(async (email: string, password: string): Promise<string | null> => {
    try {
      const response = await api.post<AuthResponse>('/auth/login', { email, password });
      const { token, email: userEmail } = response.data;
      localStorage.setItem('token', token);
      localStorage.setItem('email', userEmail);
      setState({ isAuthenticated: true, email: userEmail, token });
      return null; // no error
    } catch (err: any) {
      return err.response?.data?.error || 'Login failed.';
    }
  }, []);

  const register = useCallback(async (email: string, password: string): Promise<string | null> => {
    try {
      const response = await api.post<AuthResponse>('/auth/register', { email, password });
      const { token, email: userEmail } = response.data;
      localStorage.setItem('token', token);
      localStorage.setItem('email', userEmail);
      setState({ isAuthenticated: true, email: userEmail, token });
      return null; // no error
    } catch (err: any) {
      return err.response?.data?.error || 'Registration failed.';
    }
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem('token');
    localStorage.removeItem('email');
    setState({ isAuthenticated: false, email: null, token: null });
  }, []);

  return (
    <AuthContext.Provider value={{ ...state, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
