export interface FileResponse {
  code: string;
  originalFilename: string;
  mimeType: string;
  sizeBytes: number;
  downloadCount: number;
  maxDownloads: number | null;
  expiresAt: string | null;
  createdAt: string;
  isImage: boolean;
  isAvailable: boolean;
  hasPassword: boolean;
  thumbnailUrl: string | null;
}

export interface UploadFileRequest {
  file: File;
  maxDownloads?: number;
  expiresAt?: string;
  password?: string;
}

export interface AuthResponse {
  token: string;
  email: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
}

export interface ApiError {
  error: string;
}
