import { BrowserRouter, Routes, Route, Link } from 'react-router-dom';
import { AuthProvider, useAuth } from './hooks/useAuth';
import UploadPage from './pages/UploadPage';
import FilePage from './pages/FilePage';
import HistoryPage from './pages/HistoryPage';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import './App.css';

function NavBar() {
  const { isAuthenticated, email, logout } = useAuth();

  return (
    <nav className="navbar">
      <Link to="/" className="nav-brand">SmartShare</Link>
      <div className="nav-links">
        <Link to="/">Upload</Link>
        {isAuthenticated ? (
          <>
            <Link to="/history">History</Link>
            <span className="nav-email">{email}</span>
            <button onClick={logout} className="nav-btn">Logout</button>
          </>
        ) : (
          <>
            <Link to="/login">Login</Link>
            <Link to="/register">Register</Link>
          </>
        )}
      </div>
    </nav>
  );
}

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <NavBar />
        <main className="main-content">
          <Routes>
            <Route path="/" element={<UploadPage />} />
            <Route path="/f/:code" element={<FilePage />} />
            <Route path="/history" element={<HistoryPage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
          </Routes>
        </main>
      </BrowserRouter>
    </AuthProvider>
  );
}

export default App;
