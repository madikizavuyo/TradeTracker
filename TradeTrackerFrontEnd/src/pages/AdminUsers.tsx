import { useCallback, useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { UserPlus, Users, AlertTriangle, Check, Loader2 } from 'lucide-react';
import { api } from '@/lib/api';

interface AdminUserRow {
  id: string;
  email: string;
  roles: string[];
}

export default function AdminUsers() {
  const [rows, setRows] = useState<AdminUserRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [grantAdmin, setGrantAdmin] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [formMessage, setFormMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const loadUsers = useCallback(async () => {
    setLoading(true);
    setListError(null);
    try {
      const data = await api.getAdminUsers();
      setRows(data);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { message?: string } }; message?: string };
      setListError(ax?.response?.data?.message || ax?.message || 'Failed to load users');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadUsers();
  }, [loadUsers]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormMessage(null);
    setSubmitting(true);
    try {
      await api.createAdminUser(email.trim(), password, grantAdmin);
      setFormMessage({ type: 'success', text: 'User created. They can sign in with this email and password.' });
      setEmail('');
      setPassword('');
      setGrantAdmin(false);
      await loadUsers();
    } catch (err: unknown) {
      const ax = err as {
        response?: { data?: { message?: string; errors?: string[] } };
        message?: string;
      };
      const d = ax?.response?.data;
      const msg =
        d?.message ||
        (Array.isArray(d?.errors) ? d!.errors!.join(' ') : undefined) ||
        ax?.message ||
        'Failed to create user';
      setFormMessage({ type: 'error', text: msg });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        <div className="flex items-center space-x-3">
          <Users className="h-8 w-8 text-primary" />
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">Users</h1>
            <p className="text-muted-foreground">Create accounts and assign roles. Only administrators can access this page.</p>
          </div>
        </div>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <UserPlus className="h-5 w-5" />
              Add user
            </CardTitle>
            <CardDescription>New users receive the User role unless you grant administrator.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleCreate} className="space-y-4 max-w-md">
              <div className="space-y-2">
                <Label htmlFor="new-email">Email</Label>
                <Input
                  id="new-email"
                  type="email"
                  autoComplete="off"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="new-password">Password</Label>
                <Input
                  id="new-password"
                  type="password"
                  autoComplete="new-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  minLength={8}
                />
                <p className="text-xs text-muted-foreground">Must meet password policy (length, upper, lower, digit, symbol).</p>
              </div>
              <label className="flex items-center gap-2 text-sm cursor-pointer">
                <input type="checkbox" checked={grantAdmin} onChange={(e) => setGrantAdmin(e.target.checked)} />
                Grant administrator role
              </label>
              {formMessage && (
                <div
                  className={`flex items-start gap-2 p-3 rounded-lg text-sm ${
                    formMessage.type === 'success'
                      ? 'bg-success/10 text-success border border-success/20'
                      : 'bg-destructive/10 text-destructive border border-destructive/20'
                  }`}
                >
                  {formMessage.type === 'success' ? <Check className="h-5 w-5 shrink-0" /> : <AlertTriangle className="h-5 w-5 shrink-0" />}
                  <span>{formMessage.text}</span>
                </div>
              )}
              <Button type="submit" disabled={submitting}>
                {submitting ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : null}
                Create user
              </Button>
            </form>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Registered users</CardTitle>
            <CardDescription>All accounts in the system</CardDescription>
          </CardHeader>
          <CardContent>
            {listError && (
              <div className="flex items-center gap-2 p-3 rounded-lg bg-destructive/10 text-destructive mb-4">
                <AlertTriangle className="h-5 w-5 shrink-0" />
                <span>{listError}</span>
              </div>
            )}
            {loading ? (
              <div className="flex items-center gap-2 text-muted-foreground py-8">
                <Loader2 className="h-5 w-5 animate-spin" />
                Loading…
              </div>
            ) : rows.length === 0 ? (
              <p className="text-muted-foreground py-4">No users found.</p>
            ) : (
              <div className="overflow-x-auto rounded-md border border-border">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/40 text-left">
                      <th className="p-3 font-medium">Email</th>
                      <th className="p-3 font-medium">Roles</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r) => (
                      <tr key={r.id} className="border-b border-border last:border-0">
                        <td className="p-3">{r.email}</td>
                        <td className="p-3">
                          <div className="flex flex-wrap gap-1">
                            {(r.roles || []).map((role) => (
                              <Badge key={role} variant={role === 'Admin' ? 'default' : 'outline'}>
                                {role}
                              </Badge>
                            ))}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}
