import { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { TrendingUp, AlertCircle, CheckCircle2 } from 'lucide-react';
import { useAuth } from '@/lib/AuthContext';

export default function Register() {
  const navigate = useNavigate();
  const { register, isAuthenticated } = useAuth();
  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    confirmPassword: '',
  });
  const [errors, setErrors] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);

  // Navigate to dashboard when authentication is successful
  useEffect(() => {
    if (isAuthenticated) {
      navigate('/dashboard', { replace: true });
    }
  }, [isAuthenticated, navigate]);

  const validateForm = () => {
    const newErrors: string[] = [];

    if (formData.password.length < 6) {
      newErrors.push('Password must be at least 6 characters long');
    }

    if (formData.password !== formData.confirmPassword) {
      newErrors.push('Passwords do not match');
    }

    if (!formData.firstName.trim() || !formData.lastName.trim()) {
      newErrors.push('First name and last name are required');
    }

    setErrors(newErrors);
    return newErrors.length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm()) {
      return;
    }

    setLoading(true);
    setErrors([]);

    try {
      await register(
        formData.email,
        formData.password,
        formData.confirmPassword,
        formData.firstName,
        formData.lastName
      );
      // Navigation will happen automatically via useEffect
    } catch (err: any) {
      // Handle backend error format
      const errorMessage = err.response?.data?.message || 
                          err.response?.data?.error || 
                          'Registration failed. Please try again.';
      const errorDetails = err.response?.data?.errors;
      
      if (errorDetails && Array.isArray(errorDetails) && errorDetails.length > 0) {
        setErrors(errorDetails);
      } else {
        setErrors([errorMessage]);
      }
    } finally {
      setLoading(false);
    }
  };

  const validatePassword = (password: string) => {
    const hasMinLength = password.length >= 6;
    const hasUpperCase = /[A-Z]/.test(password);
    const hasLowerCase = /[a-z]/.test(password);
    const hasNumber = /\d/.test(password);
    
    return {
      hasMinLength,
      hasUpperCase,
      hasLowerCase,
      hasNumber,
      isValid: hasMinLength && hasUpperCase && hasLowerCase && hasNumber
    };
  };

  const passwordStrength = () => {
    const { password } = formData;
    if (password.length === 0) return { strength: 0, label: '', color: '' };
    
    const validation = validatePassword(password);
    let score = 0;
    if (validation.hasMinLength) score += 25;
    if (validation.hasUpperCase) score += 25;
    if (validation.hasLowerCase) score += 25;
    if (validation.hasNumber) score += 25;
    
    if (score === 100) return { strength: 100, label: 'Strong', color: 'bg-success' };
    if (score >= 75) return { strength: 75, label: 'Good', color: 'bg-blue-500' };
    if (score >= 50) return { strength: 50, label: 'Fair', color: 'bg-yellow-500' };
    return { strength: 25, label: 'Weak', color: 'bg-destructive' };
  };

  const strength = passwordStrength();

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        {/* Logo */}
        <div className="flex justify-center mb-8">
          <Link to="/" className="flex items-center space-x-2">
            <TrendingUp className="h-8 w-8 text-primary" />
            <span className="text-2xl font-bold text-primary">TradeTracker</span>
          </Link>
        </div>

        {/* Register Card */}
        <Card>
          <CardHeader>
            <CardTitle>Create Account</CardTitle>
            <CardDescription>Start tracking your trades today</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              {errors.length > 0 && (
                <div className="space-y-1 text-sm text-destructive bg-destructive/10 p-3 rounded-md">
                  {errors.map((error, index) => (
                    <div key={index} className="flex items-center space-x-2">
                      <AlertCircle className="h-4 w-4 flex-shrink-0" />
                      <span>{error}</span>
                    </div>
                  ))}
                </div>
              )}

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="firstName">First Name</Label>
                  <Input
                    id="firstName"
                    placeholder="John"
                    value={formData.firstName}
                    onChange={(e) => setFormData({ ...formData, firstName: e.target.value })}
                    required
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="lastName">Last Name</Label>
                  <Input
                    id="lastName"
                    placeholder="Doe"
                    value={formData.lastName}
                    onChange={(e) => setFormData({ ...formData, lastName: e.target.value })}
                    required
                  />
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="email">Email</Label>
                <Input
                  id="email"
                  type="email"
                  placeholder="you@example.com"
                  value={formData.email}
                  onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="password">Password</Label>
                <Input
                  id="password"
                  type="password"
                  placeholder="••••••••"
                  value={formData.password}
                  onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                  required
                />
                {formData.password && (
                  <div className="space-y-2">
                    <div className="flex items-center justify-between text-xs">
                      <span className="text-muted-foreground">Password strength:</span>
                      <span className={strength.strength >= 75 ? 'text-success' : 'text-muted-foreground'}>
                        {strength.label}
                      </span>
                    </div>
                    <div className="h-1 bg-muted rounded-full overflow-hidden">
                      <div
                        className={`h-full transition-all ${strength.color}`}
                        style={{ width: `${strength.strength}%` }}
                      />
                    </div>
                    <div className="text-xs space-y-1">
                      <div className={`flex items-center space-x-1 ${validatePassword(formData.password).hasMinLength ? 'text-success' : 'text-muted-foreground'}`}>
                        {validatePassword(formData.password).hasMinLength ? <CheckCircle2 className="h-3 w-3" /> : <span className="h-3 w-3 inline-block">○</span>}
                        <span>At least 6 characters</span>
                      </div>
                      <div className={`flex items-center space-x-1 ${validatePassword(formData.password).hasUpperCase ? 'text-success' : 'text-muted-foreground'}`}>
                        {validatePassword(formData.password).hasUpperCase ? <CheckCircle2 className="h-3 w-3" /> : <span className="h-3 w-3 inline-block">○</span>}
                        <span>One uppercase letter (A-Z)</span>
                      </div>
                      <div className={`flex items-center space-x-1 ${validatePassword(formData.password).hasLowerCase ? 'text-success' : 'text-muted-foreground'}`}>
                        {validatePassword(formData.password).hasLowerCase ? <CheckCircle2 className="h-3 w-3" /> : <span className="h-3 w-3 inline-block">○</span>}
                        <span>One lowercase letter (a-z)</span>
                      </div>
                      <div className={`flex items-center space-x-1 ${validatePassword(formData.password).hasNumber ? 'text-success' : 'text-muted-foreground'}`}>
                        {validatePassword(formData.password).hasNumber ? <CheckCircle2 className="h-3 w-3" /> : <span className="h-3 w-3 inline-block">○</span>}
                        <span>One number (0-9)</span>
                      </div>
                    </div>
                  </div>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="confirmPassword">Confirm Password</Label>
                <Input
                  id="confirmPassword"
                  type="password"
                  placeholder="••••••••"
                  value={formData.confirmPassword}
                  onChange={(e) => setFormData({ ...formData, confirmPassword: e.target.value })}
                  required
                />
                {formData.confirmPassword && (
                  <div className="flex items-center space-x-1 text-xs">
                    {formData.password === formData.confirmPassword ? (
                      <>
                        <CheckCircle2 className="h-3 w-3 text-success" />
                        <span className="text-success">Passwords match</span>
                      </>
                    ) : (
                      <>
                        <AlertCircle className="h-3 w-3 text-destructive" />
                        <span className="text-destructive">Passwords do not match</span>
                      </>
                    )}
                  </div>
                )}
              </div>

              <Button type="submit" className="w-full" disabled={loading}>
                {loading ? 'Creating account...' : 'Create Account'}
              </Button>

              <div className="text-center text-sm">
                <span className="text-muted-foreground">Already have an account? </span>
                <Link to="/login" className="text-primary hover:underline font-medium">
                  Sign in
                </Link>
              </div>
            </form>
          </CardContent>
        </Card>

        <div className="mt-6 text-center">
          <Link to="/" className="text-sm text-muted-foreground hover:text-foreground">
            ← Back to home
          </Link>
        </div>
      </div>
    </div>
  );
}

