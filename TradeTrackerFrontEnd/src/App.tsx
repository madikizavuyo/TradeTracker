import { createBrowserRouter, RouterProvider, Outlet, Navigate } from 'react-router-dom';
import { AuthProvider } from './lib/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import Index from './pages/Index';
import Login from './pages/Login';
import Register from './pages/Register';
import Dashboard from './pages/Dashboard';
import Trades from './pages/Trades';
import TradeForm from './pages/TradeForm';
import TradeDetails from './pages/TradeDetails';
import Strategies from './pages/Strategies';
import Reports from './pages/Reports';
import Settings from './pages/Settings';
import Import from './pages/Import';
import AIInsights from './pages/AIInsights';
import TrailBlazer from './pages/TrailBlazer';
import TrailBlazerLayout from './pages/TrailBlazerLayout';
import { TrailBlazerRefreshProvider } from './contexts/TrailBlazerRefreshContext';
import TrailBlazerScanner from './pages/TrailBlazerScanner';
import TrailBlazerStrength from './pages/TrailBlazerStrength';
import TrailBlazerNewsSentiment from './pages/TrailBlazerNewsSentiment';
import TrailBlazerBiasChanges from './pages/TrailBlazerBiasChanges';

const router = createBrowserRouter(
  [
    {
      element: (
        <AuthProvider>
          <Outlet />
        </AuthProvider>
      ),
      children: [
        { path: '/', element: <Index /> },
        { path: '/login', element: <Login /> },
        { path: '/register', element: <Register /> },
        {
          path: '/dashboard',
          element: (
            <ProtectedRoute>
              <Dashboard />
            </ProtectedRoute>
          ),
        },
        {
          path: '/trades',
          element: (
            <ProtectedRoute>
              <Trades />
            </ProtectedRoute>
          ),
        },
        {
          path: '/trades/new',
          element: (
            <ProtectedRoute>
              <TradeForm />
            </ProtectedRoute>
          ),
        },
        {
          path: '/trades/:id',
          element: (
            <ProtectedRoute>
              <TradeDetails />
            </ProtectedRoute>
          ),
        },
        {
          path: '/trades/:id/edit',
          element: (
            <ProtectedRoute>
              <TradeForm />
            </ProtectedRoute>
          ),
        },
        {
          path: '/strategies',
          element: (
            <ProtectedRoute>
              <Strategies />
            </ProtectedRoute>
          ),
        },
        {
          path: '/reports',
          element: (
            <ProtectedRoute>
              <Reports />
            </ProtectedRoute>
          ),
        },
        {
          path: '/settings',
          element: (
            <ProtectedRoute>
              <Settings />
            </ProtectedRoute>
          ),
        },
        {
          path: '/import',
          element: (
            <ProtectedRoute>
              <Import />
            </ProtectedRoute>
          ),
        },
        {
          path: '/trailblazer',
          element: (
            <ProtectedRoute>
              <TrailBlazerRefreshProvider>
                <TrailBlazerLayout />
              </TrailBlazerRefreshProvider>
            </ProtectedRoute>
          ),
          children: [
            { index: true, element: <TrailBlazer /> },
            { path: 'scanner', element: <TrailBlazerScanner /> },
            { path: 'strength', element: <TrailBlazerStrength /> },
            { path: 'news-sentiment', element: <TrailBlazerNewsSentiment /> },
            { path: 'bias-changes', element: <TrailBlazerBiasChanges /> },
          ],
        },
        {
          path: '/ai-insights',
          element: (
            <ProtectedRoute>
              <AIInsights />
            </ProtectedRoute>
          ),
        },
        { path: '*', element: <Navigate to="/" replace /> },
      ],
    },
  ],
  {
    future: {
      v7_relativeSplatPath: true,
      v7_startTransition: true,
    },
  }
);

function App() {
  return (
    <RouterProvider
      router={router}
      future={{ v7_startTransition: true }}
    />
  );
}

export default App;
