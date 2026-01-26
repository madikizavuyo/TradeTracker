# Frontend Integration Guide - TradeHelper API

## API Configuration

**Base URL:** `http://localhost:5235/api`

**CORS:** Configured for:
- `http://localhost:5173`
- `http://127.0.0.1:5173`

---

## Authentication Flow

### 1. Login
**Endpoint:** `POST /api/auth/login`

**Request:**
```json
{
  "email": "admin@tradehelper.ai",
  "password": "Admin@1234",
  "rememberMe": false
}
```

**Response (Success - 200):**
```json
{
  "message": "Login successful",
  "email": "admin@tradehelper.ai",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600
}
```

**Response (Error - 401):**
```json
{
  "message": "Invalid email or password."
}
```

### 2. Check Authentication
**Endpoint:** `GET /api/auth/check`  
**Headers:** `Authorization: Bearer <token>`

**Response:**
```json
{
  "authenticated": true,
  "email": "admin@tradehelper.ai",
  "claims": [...]
}
```

### 3. Logout
**Endpoint:** `POST /api/auth/logout`  
**Headers:** `Authorization: Bearer <token>`

**Response:**
```json
{
  "message": "Logout successful"
}
```

---

## Protected Endpoints (Require JWT Token)

All protected endpoints require the Authorization header:
```
Authorization: Bearer <your-jwt-token>
```

### Prediction Endpoints

#### Get Prediction History
**Endpoint:** `GET /api/predict/history`  
**Headers:** `Authorization: Bearer <token>`

**Response:**
```json
[
  {
    "date": "2025-01-15",
    "instrument": "EURUSD",
    "score": 7.5
  },
  ...
]
```

#### Run Prediction Manually
**Endpoint:** `POST /api/predict/run`  
**Headers:** `Authorization: Bearer <token>`

**Response:**
```json
"Prediction cycle completed manually."
```

### Email Endpoints

#### Send CSV via Email
**Endpoint:** `POST /api/email/sendcsv`  
**Headers:** `Authorization: Bearer <token>`  
**Content-Type:** `multipart/form-data`

**Request:** FormData with file field

**Response:**
```json
{
  "message": "CSV emailed to user@example.com successfully!"
}
```

---

## Example Frontend Implementation (React/TypeScript)

### 1. API Service Setup

```typescript
// services/api.ts
const API_BASE_URL = 'http://localhost:5235/api';

class ApiService {
  private getToken(): string | null {
    return localStorage.getItem('authToken');
  }

  private async request<T>(
    endpoint: string,
    options: RequestInit = {}
  ): Promise<T> {
    const token = this.getToken();
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
      ...options.headers,
    };

    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(`${API_BASE_URL}${endpoint}`, {
      ...options,
      headers,
      credentials: 'include', // Important for CORS with credentials
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'An error occurred' }));
      throw new Error(error.message || `HTTP error! status: ${response.status}`);
    }

    return response.json();
  }

  // Auth methods
  async login(email: string, password: string, rememberMe: boolean = false) {
    return this.request<{
      message: string;
      email: string;
      token: string;
      expiresIn: number;
    }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password, rememberMe }),
    });
  }

  async checkAuth() {
    return this.request<{
      authenticated: boolean;
      email: string;
      claims: Array<{ type: string; value: string }>;
    }>('/auth/check');
  }

  async logout() {
    return this.request<{ message: string }>('/auth/logout', {
      method: 'POST',
    });
  }

  // Prediction methods
  async getPredictionHistory() {
    return this.request<Array<{
      date: string;
      instrument: string;
      score: number;
    }>>('/predict/history');
  }

  async runPrediction() {
    return this.request<string>('/predict/run', {
      method: 'POST',
    });
  }

  // Email methods
  async sendCsv(file: File) {
    const formData = new FormData();
    formData.append('file', file);

    const token = this.getToken();
    const headers: HeadersInit = {};
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(`${API_BASE_URL}/email/sendcsv`, {
      method: 'POST',
      headers,
      body: formData,
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'An error occurred' }));
      throw new Error(error.message || `HTTP error! status: ${response.status}`);
    }

    return response.json();
  }
}

export const apiService = new ApiService();
```

### 2. Auth Context/Hook

```typescript
// hooks/useAuth.ts
import { useState, useEffect, createContext, useContext } from 'react';
import { apiService } from '../services/api';

interface AuthContextType {
  token: string | null;
  email: string | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  isAuthenticated: boolean;
  loading: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [token, setToken] = useState<string | null>(null);
  const [email, setEmail] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const storedToken = localStorage.getItem('authToken');
    if (storedToken) {
      setToken(storedToken);
      // Verify token is still valid
      apiService.checkAuth()
        .then((data) => {
          setEmail(data.email);
        })
        .catch(() => {
          // Token invalid, clear it
          localStorage.removeItem('authToken');
          setToken(null);
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email: string, password: string) => {
    try {
      const response = await apiService.login(email, password);
      setToken(response.token);
      setEmail(response.email);
      localStorage.setItem('authToken', response.token);
    } catch (error) {
      throw error;
    }
  };

  const logout = () => {
    setToken(null);
    setEmail(null);
    localStorage.removeItem('authToken');
    apiService.logout().catch(() => {
      // Ignore errors on logout
    });
  };

  return (
    <AuthContext.Provider
      value={{
        token,
        email,
        login,
        logout,
        isAuthenticated: !!token,
        loading,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
```

### 3. Login Component Example

```typescript
// components/Login.tsx
import { useState } from 'react';
import { useAuth } from '../hooks/useAuth';

export function Login() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      await login(email, password);
      // Redirect to dashboard or home
    } catch (err: any) {
      setError(err.message || 'Login failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <div>
        <label>Email:</label>
        <input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
      </div>
      <div>
        <label>Password:</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
      </div>
      {error && <div style={{ color: 'red' }}>{error}</div>}
      <button type="submit" disabled={loading}>
        {loading ? 'Logging in...' : 'Login'}
      </button>
    </form>
  );
}
```

### 4. Protected Route Component

```typescript
// components/ProtectedRoute.tsx
import { Navigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, loading } = useAuth();

  if (loading) {
    return <div>Loading...</div>;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}
```

---

## Default Credentials

- **Email:** `admin@tradehelper.ai`
- **Password:** `Admin@1234`

---

## Error Handling

All endpoints return errors in this format:
```json
{
  "message": "Error description"
}
```

Common HTTP status codes:
- `200` - Success
- `400` - Bad Request (validation errors)
- `401` - Unauthorized (invalid credentials or missing token)
- `500` - Server Error

---

## Important Notes

1. **Token Storage:** Store the JWT token in `localStorage` or a secure cookie
2. **Token Expiration:** Tokens expire after 1 hour (3600 seconds). Implement refresh logic if needed
3. **CORS:** The API allows credentials, so include `credentials: 'include'` in fetch requests
4. **Error Handling:** Always wrap API calls in try-catch blocks and show user-friendly error messages

---

## Complete API Endpoints Summary

### Authentication
- `POST /api/auth/login` - Login and get JWT token
- `POST /api/auth/logout` - Logout (client-side token removal)
- `GET /api/auth/check` - Check authentication status
- `POST /api/auth/create-admin` - Create/reset admin user (no auth required)

### Predictions
- `GET /api/predict/history` - Get prediction history (requires auth)
- `POST /api/predict/run` - Run prediction cycle manually (requires auth)

### Email
- `POST /api/email/sendcsv` - Send CSV file via email (requires auth)

---

## Quick Start Example

```typescript
// Quick example of using the API
import { apiService } from './services/api';

// Login
const loginResponse = await apiService.login('admin@tradehelper.ai', 'Admin@1234');
console.log('Token:', loginResponse.token);

// Get prediction history
const history = await apiService.getPredictionHistory();
console.log('History:', history);

// Run prediction
await apiService.runPrediction();
console.log('Prediction completed');
```

---

This guide provides everything needed to integrate your frontend with the TradeHelper API!

