import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
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
import MT5Integration from './pages/MT5Integration';
import MLTrading from './pages/MLTrading';
import AIInsights from './pages/AIInsights';

function App() {
  return (
    <Router>
      <AuthProvider>
        <Routes>
          {/* Public Routes */}
          <Route path="/" element={<Index />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />

          {/* Protected Routes */}
          <Route
            path="/dashboard"
            element={
              <ProtectedRoute>
                <Dashboard />
              </ProtectedRoute>
            }
          />
          <Route
            path="/trades"
            element={
              <ProtectedRoute>
                <Trades />
              </ProtectedRoute>
            }
          />
          <Route
            path="/trades/new"
            element={
              <ProtectedRoute>
                <TradeForm />
              </ProtectedRoute>
            }
          />
          <Route
            path="/trades/:id"
            element={
              <ProtectedRoute>
                <TradeDetails />
              </ProtectedRoute>
            }
          />
          <Route
            path="/trades/:id/edit"
            element={
              <ProtectedRoute>
                <TradeForm />
              </ProtectedRoute>
            }
          />
          <Route
            path="/strategies"
            element={
              <ProtectedRoute>
                <Strategies />
              </ProtectedRoute>
            }
          />
          <Route
            path="/reports"
            element={
              <ProtectedRoute>
                <Reports />
              </ProtectedRoute>
            }
          />
          <Route
            path="/settings"
            element={
              <ProtectedRoute>
                <Settings />
              </ProtectedRoute>
            }
          />
          <Route
            path="/import"
            element={
              <ProtectedRoute>
                <Import />
              </ProtectedRoute>
            }
          />
          <Route
            path="/mt5"
            element={
              <ProtectedRoute>
                <MT5Integration />
              </ProtectedRoute>
            }
          />
          <Route
            path="/ml-trading"
            element={
              <ProtectedRoute>
                <MLTrading />
              </ProtectedRoute>
            }
          />
          <Route
            path="/ai-insights"
            element={
              <ProtectedRoute>
                <AIInsights />
              </ProtectedRoute>
            }
          />

          {/* Fallback */}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </Router>
  );
}

export default App;

