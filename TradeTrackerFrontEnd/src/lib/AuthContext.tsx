import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { api } from './api';

interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  displayCurrency?: string;
}

interface AuthContextType {
  user: User | null;
  loading: boolean;
  login: (email: string, password: string, rememberMe: boolean) => Promise<void>;
  register: (email: string, password: string, confirmPassword: string, firstName: string, lastName: string) => Promise<void>;
  logout: () => Promise<void>;
  isAuthenticated: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Helper function to decode JWT token
function decodeJWT(token: string) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch (error) {
    console.error('Failed to decode JWT:', error);
    return null;
  }
}

// Check if token is expiring soon (default: 30 minutes before expiry)
function isTokenExpiringSoon(token: string, minutesBeforeExpiry: number = 30): boolean {
  try {
    const decoded = decodeJWT(token);
    if (!decoded || !decoded.exp) return true;

    const expiryTime = decoded.exp * 1000; // Convert to milliseconds
    const timeUntilExpiry = expiryTime - Date.now();
    const minutesUntilExpiry = timeUntilExpiry / 1000 / 60;

    return minutesUntilExpiry < minutesBeforeExpiry;
  } catch (error) {
    console.error('Failed to check token expiry:', error);
    return true; // Assume expired if we can't check
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  // Check authentication using the new checkAuth endpoint
  const checkAuth = async () => {
    try {
      const token = localStorage.getItem('token');
      const storedUser = localStorage.getItem('user');
      
      if (token && storedUser) {
        // Verify token is still valid using checkAuth endpoint
        try {
          const authCheck = await api.checkAuth();
          if (authCheck.authenticated) {
            const userData = {
              id: authCheck.email,
              email: authCheck.email,
              firstName: authCheck.email.split('@')[0],
              lastName: 'User',
            };
            setUser(userData);
            localStorage.setItem('user', JSON.stringify(userData));
          } else {
            // Not authenticated, clear storage
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            setUser(null);
          }
        } catch (error) {
          // Token invalid or expired - try to use stored user if token is not expired
          const decoded = decodeJWT(token);
          if (decoded && decoded.exp && decoded.exp * 1000 > Date.now()) {
            // Token is valid but checkAuth failed, use stored user
            setUser(JSON.parse(storedUser));
          } else {
            // Token expired, clear storage
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            setUser(null);
          }
        }
      } else {
        // No token or user stored
        setUser(null);
      }
    } catch (error) {
      console.error('Auth check failed:', error);
      setUser(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    // Check if user is already authenticated on mount
    checkAuth();
  }, []);

  // Proactive token refresh - check every 5 minutes
  useEffect(() => {
    const checkAndRefreshToken = async () => {
      const token = localStorage.getItem('token');
      
      if (token && isTokenExpiringSoon(token, 30)) {
        console.log('Token expiring soon, refreshing proactively...');
        try {
          const response = await api.refreshToken();
          const newToken = response.data?.token || response.token;
          
          if (newToken) {
            localStorage.setItem('token', newToken);
            console.log('Token refreshed successfully');
          }
        } catch (error) {
          console.error('Proactive token refresh failed:', error);
          // If refresh fails, logout user
          await logout();
        }
      }
    };

    // Check immediately on mount
    checkAndRefreshToken();

    // Then check every 5 minutes
    const interval = setInterval(checkAndRefreshToken, 5 * 60 * 1000);

    return () => clearInterval(interval);
  }, []);

  const login = async (email: string, password: string, rememberMe: boolean) => {
    const response = await api.login(email, password, rememberMe);
    
    // TradeHelper API response format: { message, email, token, expiresIn }
    const { token, email: userEmail } = response;
    
    if (token) {
      // Store JWT token
      localStorage.setItem('token', token);
      
      // Try to get user details from checkAuth endpoint
      try {
        const authCheck = await api.checkAuth();
        const userData = {
          id: authCheck.email, // Use email as ID if no userId available
          email: authCheck.email,
          firstName: authCheck.email.split('@')[0], // Extract from email
          lastName: 'User',
        };
        setUser(userData);
        localStorage.setItem('user', JSON.stringify(userData));
      } catch (error) {
        // If checkAuth fails, use email-based user data
        const userData = {
          id: userEmail || email,
          email: userEmail || email,
          firstName: (userEmail || email).split('@')[0],
          lastName: 'User',
        };
        setUser(userData);
        localStorage.setItem('user', JSON.stringify(userData));
      }
    } else {
      throw new Error('No token received from server');
    }
  };

  const register = async (
    email: string,
    password: string,
    confirmPassword: string,
    firstName: string,
    lastName: string
  ) => {
    const response = await api.register(email, password, confirmPassword, firstName, lastName);
    
    // Extract token and user data from response
    const { token, userId, email: userEmail } = response.data || response;
    
    if (token) {
      // Store JWT token
      localStorage.setItem('token', token);
      
      // Store user data
      const userData = {
        id: userId,
        email: userEmail || email,
        firstName,
        lastName,
      };
      
      setUser(userData);
      localStorage.setItem('user', JSON.stringify(userData));
    } else {
      throw new Error('No token received from server');
    }
  };

  const logout = async () => {
    try {
      await api.logout();
    } catch (error) {
      console.error('Logout failed:', error);
    } finally {
      setUser(null);
      localStorage.removeItem('token');
      localStorage.removeItem('user');
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        loading,
        login,
        register,
        logout,
        isAuthenticated: !!user,
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

