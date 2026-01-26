import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { TrendingUp, BarChart3, Target, FileText, Shield, Zap } from 'lucide-react';

export default function Index() {
  return (
    <div className="min-h-screen bg-background">
      {/* Navigation */}
      <nav className="border-b">
        <div className="container mx-auto flex h-16 items-center justify-between px-6">
          <Link to="/" className="flex items-center space-x-2">
            <TrendingUp className="h-6 w-6 text-primary" />
            <span className="text-xl font-bold text-primary">TradeTracker</span>
          </Link>
          <div className="flex items-center space-x-4">
            <Link to="/login">
              <Button variant="ghost">Sign In</Button>
            </Link>
            <Link to="/register">
              <Button>Get Started</Button>
            </Link>
          </div>
        </div>
      </nav>

      {/* Hero Section */}
      <section className="container mx-auto px-6 py-20">
        <div className="text-center">
          <h1 className="text-5xl font-bold tracking-tight">
            <span className="bg-gradient-to-r from-primary to-blue-600 bg-clip-text text-transparent">
              Track Your Trades
            </span>
            <br />
            Like a Professional
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-lg text-muted-foreground">
            Comprehensive trading journal with advanced analytics, strategy tracking, and performance insights.
            Take your trading to the next level with data-driven decisions.
          </p>
          <div className="mt-10 flex items-center justify-center gap-4">
            <Link to="/register">
              <Button size="lg" className="text-base">
                Start Trading Journal
              </Button>
            </Link>
            <Button size="lg" variant="outline" className="text-base">
              Watch Demo
            </Button>
          </div>
        </div>
      </section>

      {/* Features Grid */}
      <section className="container mx-auto px-6 py-20">
        <h2 className="text-center text-3xl font-bold">Everything You Need to Succeed</h2>
        <p className="mx-auto mt-4 max-w-2xl text-center text-muted-foreground">
          Powerful features designed for serious traders who want to improve their performance
        </p>
        <div className="mt-12 grid gap-6 md:grid-cols-2 lg:grid-cols-4">
          <Card className="border-2 hover:shadow-lg transition-shadow">
            <CardContent className="pt-6">
              <div className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <TrendingUp className="h-6 w-6 text-primary" />
              </div>
              <h3 className="mb-2 text-lg font-semibold">Trade Management</h3>
              <p className="text-sm text-muted-foreground">
                Record and track all your trades with detailed entry and exit information, including images and notes.
              </p>
            </CardContent>
          </Card>

          <Card className="border-2 hover:shadow-lg transition-shadow">
            <CardContent className="pt-6">
              <div className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-lg bg-success/10">
                <BarChart3 className="h-6 w-6 text-success" />
              </div>
              <h3 className="mb-2 text-lg font-semibold">Performance Analytics</h3>
              <p className="text-sm text-muted-foreground">
                Comprehensive analytics with win rates, profit factors, and detailed performance metrics.
              </p>
            </CardContent>
          </Card>

          <Card className="border-2 hover:shadow-lg transition-shadow">
            <CardContent className="pt-6">
              <div className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-lg bg-blue-500/10">
                <Target className="h-6 w-6 text-blue-500" />
              </div>
              <h3 className="mb-2 text-lg font-semibold">Strategy Tracking</h3>
              <p className="text-sm text-muted-foreground">
                Organize trades by strategy and track which approaches work best for your trading style.
              </p>
            </CardContent>
          </Card>

          <Card className="border-2 hover:shadow-lg transition-shadow">
            <CardContent className="pt-6">
              <div className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-lg bg-purple-500/10">
                <FileText className="h-6 w-6 text-purple-500" />
              </div>
              <h3 className="mb-2 text-lg font-semibold">Detailed Reports</h3>
              <p className="text-sm text-muted-foreground">
                Generate comprehensive reports with monthly breakdowns, insights, and trading patterns.
              </p>
            </CardContent>
          </Card>
        </div>
      </section>

      {/* Statistics Section */}
      <section className="bg-muted py-20">
        <div className="container mx-auto px-6">
          <div className="grid gap-8 md:grid-cols-4">
            <div className="text-center">
              <div className="text-4xl font-bold text-primary">10,000+</div>
              <div className="mt-2 text-sm text-muted-foreground">Trades Tracked</div>
            </div>
            <div className="text-center">
              <div className="text-4xl font-bold text-success">95%</div>
              <div className="mt-2 text-sm text-muted-foreground">User Satisfaction</div>
            </div>
            <div className="text-center">
              <div className="text-4xl font-bold text-blue-600">24/7</div>
              <div className="mt-2 text-sm text-muted-foreground">Access Anywhere</div>
            </div>
            <div className="text-center">
              <div className="text-4xl font-bold text-purple-600">50+</div>
              <div className="mt-2 text-sm text-muted-foreground">Analysis Tools</div>
            </div>
          </div>
        </div>
      </section>

      {/* CTA Section */}
      <section className="container mx-auto px-6 py-20">
        <Card className="border-2">
          <CardContent className="p-12 text-center">
            <h2 className="text-3xl font-bold">Ready to Elevate Your Trading?</h2>
            <p className="mx-auto mt-4 max-w-2xl text-muted-foreground">
              Join thousands of traders who are improving their performance with TradeTracker
            </p>
            <div className="mt-8 flex items-center justify-center gap-4">
              <Link to="/register">
                <Button size="lg">Start Free Trial</Button>
              </Link>
              <Button size="lg" variant="outline">
                Contact Sales
              </Button>
            </div>
          </CardContent>
        </Card>
      </section>

      {/* Footer */}
      <footer className="border-t py-8">
        <div className="container mx-auto px-6">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <TrendingUp className="h-5 w-5 text-primary" />
              <span className="font-semibold text-primary">TradeTracker</span>
            </div>
            <p className="text-sm text-muted-foreground">
              © 2024 TradeTracker. All rights reserved.
            </p>
          </div>
        </div>
      </footer>
    </div>
  );
}

