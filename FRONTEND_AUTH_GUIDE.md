# 🔐 FIREBASE AUTHENTICATION & AUTHORIZATION FRONTEND GUIDE

## 🎯 **Tổng quan**
Hướng dẫn chi tiết tích hợp Firebase Authentication và phân quyền giao diện cho 3 loại người dùng

---

## 🔥 **Firebase Setup**

### **1. Firebase Project Configuration**
```javascript
// firebase.config.js
import { initializeApp } from 'firebase/app';
import { getAuth } from 'firebase/auth';

const firebaseConfig = {
  apiKey: "AIzaSyC49tuQriwSQjBz-Y2JLFJvtbdmS7VHah4",
  authDomain: "whalebooking-e3ea2.firebaseapp.com",
  projectId: "whalebooking-e3ea2",
  storageBucket: "whalebooking-e3ea2.firebasestorage.app",
  messagingSenderId: "58075955129",
  appId: "1:58075955129:web:b70c27a51349ce95cd4068",
  measurementId: "G-3BCN8YD3QH"
};

const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);
export default app;
```

### **2. Environment Variables**
```bash
# .env.local
REACT_APP_FIREBASE_API_KEY=your-api-key
REACT_APP_FIREBASE_AUTH_DOMAIN=whalebooking-e3ea2.firebaseapp.com
REACT_APP_FIREBASE_PROJECT_ID=whalebooking-e3ea2
REACT_APP_FIREBASE_STORAGE_BUCKET=whalebooking-e3ea2.appspot.com
REACT_APP_FIREBASE_MESSAGING_SENDER_ID=your-sender-id
REACT_APP_FIREBASE_APP_ID=your-app-id

# Backend API
REACT_APP_API_BASE_URL=http://localhost:5000/api
```

---

## 🔑 **Authentication Implementation**

### **1. Auth Service**
```javascript
// services/authService.js
import { 
  signInWithEmailAndPassword,
  createUserWithEmailAndPassword,
  signOut,
  onAuthStateChanged,
  sendPasswordResetEmail,
  updateProfile
} from 'firebase/auth';
import { auth } from '../config/firebase';

class AuthService {
  constructor() {
    this.currentUser = null;
    this.authToken = null;
    this.userRole = null;
  }

  // Đăng nhập với email/password
  async login(email, password) {
    try {
      const userCredential = await signInWithEmailAndPassword(auth, email, password);
      const user = userCredential.user;
      
      // Lấy Firebase ID Token
      const idToken = await user.getIdToken();
      
      // Gửi lên backend để lấy JWT + role
      const response = await fetch(`${process.env.REACT_APP_API_BASE_URL}/auth/login`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          idToken: idToken,
          email: user.email,
          displayName: user.displayName
        })
      });

      const result = await response.json();
      
      if (result.success) {
        // Lưu JWT token và user info
        this.authToken = result.data.accessToken;
        this.userRole = result.data.roles[0]; // Admin, ChuCoSo, hoặc KhachHang
        this.currentUser = result.data.user;
        
        // Lưu vào localStorage
        localStorage.setItem('authToken', this.authToken);
        localStorage.setItem('userRole', this.userRole);
        localStorage.setItem('userData', JSON.stringify(this.currentUser));
        
        return {
          success: true,
          user: this.currentUser,
          role: this.userRole,
          token: this.authToken
        };
      } else {
        throw new Error(result.message || 'Đăng nhập thất bại');
      }
    } catch (error) {
      console.error('Login error:', error);
      throw new Error(this.getErrorMessage(error.code) || error.message);
    }
  }

  // Đăng ký tài khoản mới
  async register(email, password, displayName, phoneNumber, role = 'KhachHang') {
    try {
      const userCredential = await createUserWithEmailAndPassword(auth, email, password);
      const user = userCredential.user;

      // Cập nhật profile
      await updateProfile(user, {
        displayName: displayName
      });

      // Lấy Firebase ID Token
      const idToken = await user.getIdToken();
      
      // Gửi lên backend để tạo user
      const response = await fetch(`${process.env.REACT_APP_API_BASE_URL}/auth/register`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          idToken: idToken,
          email: user.email,
          displayName: displayName,
          phoneNumber: phoneNumber,
          role: role
        })
      });

      const result = await response.json();
      
      if (result.success) {
        return await this.login(email, password); // Tự động đăng nhập
      } else {
        throw new Error(result.message || 'Đăng ký thất bại');
      }
    } catch (error) {
      console.error('Register error:', error);
      throw new Error(this.getErrorMessage(error.code) || error.message);
    }
  }

  // Đăng xuất
  async logout() {
    try {
      await signOut(auth);
      
      // Clear local storage
      localStorage.removeItem('authToken');
      localStorage.removeItem('userRole');
      localStorage.removeItem('userData');
      
      // Reset service state
      this.currentUser = null;
      this.authToken = null;
      this.userRole = null;
      
      return { success: true };
    } catch (error) {
      console.error('Logout error:', error);
      throw error;
    }
  }

  // Quên mật khẩu
  async resetPassword(email) {
    try {
      await sendPasswordResetEmail(auth, email);
      return { success: true, message: 'Email reset mật khẩu đã được gửi' };
    } catch (error) {
      throw new Error(this.getErrorMessage(error.code) || error.message);
    }
  }

  // Kiểm tra trạng thái đăng nhập
  onAuthStateChange(callback) {
    return onAuthStateChanged(auth, async (user) => {
      if (user) {
        // Restore from localStorage if available
        const storedToken = localStorage.getItem('authToken');
        const storedRole = localStorage.getItem('userRole');
        const storedUser = localStorage.getItem('userData');
        
        if (storedToken && storedRole && storedUser) {
          this.authToken = storedToken;
          this.userRole = storedRole;
          this.currentUser = JSON.parse(storedUser);
        }
      } else {
        this.currentUser = null;
        this.authToken = null;
        this.userRole = null;
      }
      
      callback(user);
    });
  }

  // Lấy token hiện tại
  getToken() {
    return this.authToken || localStorage.getItem('authToken');
  }

  // Lấy role hiện tại
  getRole() {
    return this.userRole || localStorage.getItem('userRole');
  }

  // Lấy user hiện tại
  getCurrentUser() {
    if (this.currentUser) {
      return this.currentUser;
    }
    
    const storedUser = localStorage.getItem('userData');
    return storedUser ? JSON.parse(storedUser) : null;
  }

  // Kiểm tra đã đăng nhập
  isAuthenticated() {
    return !!this.getToken();
  }

  // Kiểm tra quyền
  hasRole(requiredRole) {
    const currentRole = this.getRole();
    
    // Admin có tất cả quyền
    if (currentRole === 'Admin') return true;
    
    // So sánh role chính xác
    return currentRole === requiredRole;
  }

  // Kiểm tra multiple roles
  hasAnyRole(roles) {
    const currentRole = this.getRole();
    return roles.includes(currentRole) || currentRole === 'Admin';
  }

  // Error message mapping
  getErrorMessage(errorCode) {
    const errorMessages = {
      'auth/user-not-found': 'Không tìm thấy tài khoản với email này',
      'auth/wrong-password': 'Mật khẩu không chính xác',
      'auth/email-already-in-use': 'Email này đã được sử dụng',
      'auth/weak-password': 'Mật khẩu quá yếu, cần ít nhất 6 ký tự',
      'auth/invalid-email': 'Email không hợp lệ',
      'auth/too-many-requests': 'Quá nhiều lần thử, vui lòng thử lại sau',
      'auth/network-request-failed': 'Lỗi kết nối mạng'
    };
    
    return errorMessages[errorCode] || 'Có lỗi xảy ra, vui lòng thử lại';
  }

  // Refresh token
  async refreshToken() {
    try {
      const user = auth.currentUser;
      if (user) {
        const newToken = await user.getIdToken(true); // Force refresh
        
        // Update backend JWT
        const response = await fetch(`${process.env.REACT_APP_API_BASE_URL}/auth/refresh-token`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ idToken: newToken })
        });

        const result = await response.json();
        
        if (result.success) {
          this.authToken = result.data.accessToken;
          localStorage.setItem('authToken', this.authToken);
          return this.authToken;
        }
      }
      
      throw new Error('Không thể refresh token');
    } catch (error) {
      console.error('Refresh token error:', error);
      await this.logout();
      throw error;
    }
  }
}

export default new AuthService();
```

### **2. HTTP Interceptor**
```javascript
// services/httpClient.js
import authService from './authService';

class HttpClient {
  constructor() {
    this.baseURL = process.env.REACT_APP_API_BASE_URL;
  }

  async request(url, options = {}) {
    const token = authService.getToken();
    
    const config = {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...(token && { 'Authorization': `Bearer ${token}` }),
        ...options.headers
      }
    };

    try {
      const response = await fetch(`${this.baseURL}${url}`, config);
      
      // Handle token expiration
      if (response.status === 401) {
        try {
          await authService.refreshToken();
          // Retry request with new token
          config.headers['Authorization'] = `Bearer ${authService.getToken()}`;
          return await fetch(`${this.baseURL}${url}`, config);
        } catch (error) {
          // Redirect to login
          window.location.href = '/login';
          throw error;
        }
      }

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    } catch (error) {
      console.error('HTTP request error:', error);
      throw error;
    }
  }

  get(url, options = {}) {
    return this.request(url, { ...options, method: 'GET' });
  }

  post(url, data, options = {}) {
    return this.request(url, {
      ...options,
      method: 'POST',
      body: JSON.stringify(data)
    });
  }

  put(url, data, options = {}) {
    return this.request(url, {
      ...options,
      method: 'PUT',
      body: JSON.stringify(data)
    });
  }

  delete(url, options = {}) {
    return this.request(url, { ...options, method: 'DELETE' });
  }

  // For multipart form data (file uploads)
  postForm(url, formData, options = {}) {
    const token = authService.getToken();
    
    return fetch(`${this.baseURL}${url}`, {
      method: 'POST',
      headers: {
        ...(token && { 'Authorization': `Bearer ${token}` }),
        ...options.headers
      },
      body: formData
    });
  }
}

export default new HttpClient();
```

---

## 🛡️ **Role-Based Access Control**

### **1. Route Protection**
```javascript
// components/ProtectedRoute.jsx
import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import authService from '../services/authService';

const ProtectedRoute = ({ children, requiredRole, allowedRoles }) => {
  const location = useLocation();
  const isAuthenticated = authService.isAuthenticated();
  const currentRole = authService.getRole();

  // Chưa đăng nhập → redirect login
  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  // Kiểm tra role nếu có yêu cầu
  if (requiredRole && !authService.hasRole(requiredRole)) {
    return <Navigate to="/unauthorized" replace />;
  }

  // Kiểm tra multiple roles
  if (allowedRoles && !authService.hasAnyRole(allowedRoles)) {
    return <Navigate to="/unauthorized" replace />;
  }

  return children;
};

// Higher-order component for role checking
export const withRoleCheck = (Component, requiredRole) => {
  return (props) => (
    <ProtectedRoute requiredRole={requiredRole}>
      <Component {...props} />
    </ProtectedRoute>
  );
};

// Multiple roles check
export const withAnyRole = (Component, allowedRoles) => {
  return (props) => (
    <ProtectedRoute allowedRoles={allowedRoles}>
      <Component {...props} />
    </ProtectedRoute>
  );
};

export default ProtectedRoute;
```

### **2. Component-Level Protection**
```javascript
// components/RoleBasedComponent.jsx
import React from 'react';
import authService from '../services/authService';

// Hiển thị component dựa trên role
export const RoleBasedComponent = ({ 
  children, 
  allowedRoles, 
  fallback = null 
}) => {
  const hasPermission = authService.hasAnyRole(allowedRoles);
  
  return hasPermission ? children : fallback;
};

// Hook để check role
export const useRole = () => {
  const role = authService.getRole();
  
  return {
    role,
    isAdmin: role === 'Admin',
    isOwner: role === 'ChuCoSo',
    isCustomer: role === 'KhachHang',
    hasRole: (requiredRole) => authService.hasRole(requiredRole),
    hasAnyRole: (roles) => authService.hasAnyRole(roles)
  };
};

// Example usage
const AdminOnlyButton = () => (
  <RoleBasedComponent allowedRoles={['Admin']}>
    <button>Admin Action</button>
  </RoleBasedComponent>
);

const OwnerCustomerComponent = () => (
  <RoleBasedComponent 
    allowedRoles={['ChuCoSo', 'KhachHang']}
    fallback={<div>Không có quyền truy cập</div>}
  >
    <div>Content for owners and customers</div>
  </RoleBasedComponent>
);
```

---

## 🎨 **UI Components**

### **1. Login Form**
```javascript
// components/LoginForm.jsx
import React, { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import authService from '../services/authService';

const LoginForm = () => {
  const [formData, setFormData] = useState({
    email: '',
    password: ''
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  
  const navigate = useNavigate();
  const location = useLocation();
  
  const from = location.state?.from?.pathname || '/';

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    try {
      const result = await authService.login(formData.email, formData.password);
      
      if (result.success) {
        // Redirect based on role
        const redirectPath = getRoleBasedRedirect(result.role, from);
        navigate(redirectPath, { replace: true });
      }
    } catch (error) {
      setError(error.message);
    } finally {
      setLoading(false);
    }
  };

  const getRoleBasedRedirect = (role, from) => {
    // If user was trying to access a specific page, go there
    if (from !== '/') return from;
    
    // Default redirects by role
    switch (role) {
      case 'Admin':
        return '/admin/dashboard';
      case 'ChuCoSo':
        return '/owner/dashboard';
      case 'KhachHang':
        return '/customer/dashboard';
      default:
        return '/';
    }
  };

  return (
    <div className="login-form">
      <h2>Đăng nhập</h2>
      
      {error && (
        <div className="error-message">
          {error}
        </div>
      )}
      
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="email">Email:</label>
          <input
            type="email"
            id="email"
            value={formData.email}
            onChange={(e) => setFormData({...formData, email: e.target.value})}
            required
            disabled={loading}
          />
        </div>
        
        <div className="form-group">
          <label htmlFor="password">Mật khẩu:</label>
          <input
            type="password"
            id="password"
            value={formData.password}
            onChange={(e) => setFormData({...formData, password: e.target.value})}
            required
            disabled={loading}
          />
        </div>
        
        <button type="submit" disabled={loading}>
          {loading ? 'Đang đăng nhập...' : 'Đăng nhập'}
        </button>
      </form>
      
      <div className="form-links">
        <a href="/register">Chưa có tài khoản? Đăng ký</a>
        <a href="/forgot-password">Quên mật khẩu?</a>
      </div>
    </div>
  );
};

export default LoginForm;
```

### **2. Register Form**
```javascript
// components/RegisterForm.jsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import authService from '../services/authService';

const RegisterForm = () => {
  const [formData, setFormData] = useState({
    email: '',
    password: '',
    confirmPassword: '',
    displayName: '',
    phoneNumber: '',
    role: 'KhachHang' // Default role
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  
  const navigate = useNavigate();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');

    // Validation
    if (formData.password !== formData.confirmPassword) {
      setError('Mật khẩu xác nhận không khớp');
      return;
    }

    if (formData.password.length < 6) {
      setError('Mật khẩu phải có ít nhất 6 ký tự');
      return;
    }

    setLoading(true);

    try {
      const result = await authService.register(
        formData.email,
        formData.password,
        formData.displayName,
        formData.phoneNumber,
        formData.role
      );
      
      if (result.success) {
        // Redirect based on role
        const redirectPath = getRoleBasedRedirect(result.role);
        navigate(redirectPath, { replace: true });
      }
    } catch (error) {
      setError(error.message);
    } finally {
      setLoading(false);
    }
  };

  const getRoleBasedRedirect = (role) => {
    switch (role) {
      case 'Admin':
        return '/admin/dashboard';
      case 'ChuCoSo':
        return '/owner/dashboard';
      case 'KhachHang':
        return '/customer/dashboard';
      default:
        return '/';
    }
  };

  return (
    <div className="register-form">
      <h2>Đăng ký tài khoản</h2>
      
      {error && (
        <div className="error-message">
          {error}
        </div>
      )}
      
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="role">Loại tài khoản:</label>
          <select
            id="role"
            value={formData.role}
            onChange={(e) => setFormData({...formData, role: e.target.value})}
            disabled={loading}
          >
            <option value="KhachHang">Khách hàng</option>
            <option value="ChuCoSo">Chủ cơ sở</option>
          </select>
        </div>

        <div className="form-group">
          <label htmlFor="displayName">Họ tên:</label>
          <input
            type="text"
            id="displayName"
            value={formData.displayName}
            onChange={(e) => setFormData({...formData, displayName: e.target.value})}
            required
            disabled={loading}
          />
        </div>
        
        <div className="form-group">
          <label htmlFor="email">Email:</label>
          <input
            type="email"
            id="email"
            value={formData.email}
            onChange={(e) => setFormData({...formData, email: e.target.value})}
            required
            disabled={loading}
          />
        </div>

        <div className="form-group">
          <label htmlFor="phoneNumber">Số điện thoại:</label>
          <input
            type="tel"
            id="phoneNumber"
            value={formData.phoneNumber}
            onChange={(e) => setFormData({...formData, phoneNumber: e.target.value})}
            disabled={loading}
          />
        </div>
        
        <div className="form-group">
          <label htmlFor="password">Mật khẩu:</label>
          <input
            type="password"
            id="password"
            value={formData.password}
            onChange={(e) => setFormData({...formData, password: e.target.value})}
            required
            disabled={loading}
          />
        </div>

        <div className="form-group">
          <label htmlFor="confirmPassword">Xác nhận mật khẩu:</label>
          <input
            type="password"
            id="confirmPassword"
            value={formData.confirmPassword}
            onChange={(e) => setFormData({...formData, confirmPassword: e.target.value})}
            required
            disabled={loading}
          />
        </div>
        
        <button type="submit" disabled={loading}>
          {loading ? 'Đang đăng ký...' : 'Đăng ký'}
        </button>
      </form>
      
      <div className="form-links">
        <a href="/login">Đã có tài khoản? Đăng nhập</a>
      </div>
    </div>
  );
};

export default RegisterForm;
```

### **3. Auth Context Provider**
```javascript
// contexts/AuthContext.jsx
import React, { createContext, useContext, useEffect, useState } from 'react';
import authService from '../services/authService';

const AuthContext = createContext();

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [role, setRole] = useState(null);
  const [loading, setLoading] = useState(true);
  const [isAuthenticated, setIsAuthenticated] = useState(false);

  useEffect(() => {
    const unsubscribe = authService.onAuthStateChange(async (firebaseUser) => {
      if (firebaseUser) {
        // User is signed in
        const userData = authService.getCurrentUser();
        const userRole = authService.getRole();
        
        setUser(userData);
        setRole(userRole);
        setIsAuthenticated(true);
      } else {
        // User is signed out
        setUser(null);
        setRole(null);
        setIsAuthenticated(false);
      }
      
      setLoading(false);
    });

    return unsubscribe;
  }, []);

  const login = async (email, password) => {
    const result = await authService.login(email, password);
    if (result.success) {
      setUser(result.user);
      setRole(result.role);
      setIsAuthenticated(true);
    }
    return result;
  };

  const register = async (email, password, displayName, phoneNumber, userRole) => {
    const result = await authService.register(email, password, displayName, phoneNumber, userRole);
    if (result.success) {
      setUser(result.user);
      setRole(result.role);
      setIsAuthenticated(true);
    }
    return result;
  };

  const logout = async () => {
    await authService.logout();
    setUser(null);
    setRole(null);
    setIsAuthenticated(false);
  };

  const hasRole = (requiredRole) => {
    return authService.hasRole(requiredRole);
  };

  const hasAnyRole = (roles) => {
    return authService.hasAnyRole(roles);
  };

  const value = {
    user,
    role,
    isAuthenticated,
    loading,
    login,
    register,
    logout,
    hasRole,
    hasAnyRole,
    isAdmin: role === 'Admin',
    isOwner: role === 'ChuCoSo',
    isCustomer: role === 'KhachHang'
  };

  return (
    <AuthContext.Provider value={value}>
      {!loading && children}
    </AuthContext.Provider>
  );
};
```

---

## 🚦 **Router Setup**

### **1. App Router Configuration**
```javascript
// App.jsx
import React from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';

// Auth Components
import LoginForm from './components/LoginForm';
import RegisterForm from './components/RegisterForm';
import ForgotPassword from './components/ForgotPassword';
import Unauthorized from './components/Unauthorized';

// Admin Components
import AdminDashboard from './pages/admin/Dashboard';
import AdminAccommodations from './pages/admin/Accommodations';
import AdminUsers from './pages/admin/Users';
import AdminPayments from './pages/admin/Payments';

// Owner Components
import OwnerDashboard from './pages/owner/Dashboard';
import OwnerAccommodations from './pages/owner/Accommodations';
import OwnerRooms from './pages/owner/Rooms';
import OwnerBookings from './pages/owner/Bookings';

// Customer Components
import CustomerHome from './pages/customer/Home';
import CustomerSearch from './pages/customer/Search';
import CustomerBookings from './pages/customer/Bookings';
import CustomerProfile from './pages/customer/Profile';

// Public Components
import HomePage from './pages/public/Home';
import HotelDetails from './pages/public/HotelDetails';

function App() {
  return (
    <AuthProvider>
      <Router>
        <div className="App">
          <Routes>
            {/* Public Routes */}
            <Route path="/" element={<HomePage />} />
            <Route path="/hotels/:id" element={<HotelDetails />} />
            <Route path="/login" element={<LoginForm />} />
            <Route path="/register" element={<RegisterForm />} />
            <Route path="/forgot-password" element={<ForgotPassword />} />
            <Route path="/unauthorized" element={<Unauthorized />} />

            {/* Admin Routes */}
            <Route path="/admin/*" element={
              <ProtectedRoute requiredRole="Admin">
                <Routes>
                  <Route path="dashboard" element={<AdminDashboard />} />
                  <Route path="accommodations" element={<AdminAccommodations />} />
                  <Route path="users" element={<AdminUsers />} />
                  <Route path="payments" element={<AdminPayments />} />
                  <Route path="" element={<Navigate to="dashboard" replace />} />
                </Routes>
              </ProtectedRoute>
            } />

            {/* Owner Routes */}
            <Route path="/owner/*" element={
              <ProtectedRoute requiredRole="ChuCoSo">
                <Routes>
                  <Route path="dashboard" element={<OwnerDashboard />} />
                  <Route path="accommodations" element={<OwnerAccommodations />} />
                  <Route path="rooms" element={<OwnerRooms />} />
                  <Route path="bookings" element={<OwnerBookings />} />
                  <Route path="" element={<Navigate to="dashboard" replace />} />
                </Routes>
              </ProtectedRoute>
            } />

            {/* Customer Routes */}
            <Route path="/customer/*" element={
              <ProtectedRoute requiredRole="KhachHang">
                <Routes>
                  <Route path="dashboard" element={<CustomerHome />} />
                  <Route path="search" element={<CustomerSearch />} />
                  <Route path="bookings" element={<CustomerBookings />} />
                  <Route path="profile" element={<CustomerProfile />} />
                  <Route path="" element={<Navigate to="dashboard" replace />} />
                </Routes>
              </ProtectedRoute>
            } />

            {/* Fallback */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </div>
      </Router>
    </AuthProvider>
  );
}

export default App;
```

---

## 🎨 **UI Role-Based Styling**

### **1. Role-Based Themes**
```css
/* styles/themes.css */

/* Admin Theme */
.theme-admin {
  --primary-color: #2563eb;
  --secondary-color: #64748b;
  --success-color: #16a34a;
  --warning-color: #ea580c;
  --danger-color: #dc2626;
  --sidebar-bg: #1e293b;
  --content-bg: #f8fafc;
}

/* Owner Theme */
.theme-owner {
  --primary-color: #059669;
  --secondary-color: #64748b;
  --success-color: #16a34a;
  --warning-color: #f59e0b;
  --danger-color: #dc2626;
  --sidebar-bg: #065f46;
  --content-bg: #f0fdf4;
}

/* Customer Theme */
.theme-customer {
  --primary-color: #0ea5e9;
  --secondary-color: #64748b;
  --success-color: #10b981;
  --warning-color: #f59e0b;
  --danger-color: #ef4444;
  --sidebar-bg: #0c4a6e;
  --content-bg: #f0f9ff;
}

/* Apply theme based on role */
.app-container {
  background-color: var(--content-bg);
  color: var(--text-color);
}

.sidebar {
  background-color: var(--sidebar-bg);
}

.btn-primary {
  background-color: var(--primary-color);
  border-color: var(--primary-color);
}
```

### **2. Dynamic Theme Component**
```javascript
// components/ThemeProvider.jsx
import React, { useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';

const ThemeProvider = ({ children }) => {
  const { role } = useAuth();

  useEffect(() => {
    // Remove existing theme classes
    document.body.classList.remove('theme-admin', 'theme-owner', 'theme-customer');
    
    // Add role-based theme
    if (role === 'Admin') {
      document.body.classList.add('theme-admin');
    } else if (role === 'ChuCoSo') {
      document.body.classList.add('theme-owner');
    } else if (role === 'KhachHang') {
      document.body.classList.add('theme-customer');
    }
  }, [role]);

  return <>{children}</>;
};

export default ThemeProvider;
```

---

## 🔧 **Advanced Features**

### **1. Session Management**
```javascript
// utils/sessionManager.js
class SessionManager {
  constructor() {
    this.sessionTimeout = 30 * 60 * 1000; // 30 minutes
    this.warningTime = 5 * 60 * 1000; // 5 minutes before expiry
    this.timeoutId = null;
    this.warningId = null;
  }

  startSession() {
    this.resetTimeout();
    this.setupActivityListeners();
  }

  resetTimeout() {
    // Clear existing timeouts
    if (this.timeoutId) clearTimeout(this.timeoutId);
    if (this.warningId) clearTimeout(this.warningId);

    // Set warning timeout
    this.warningId = setTimeout(() => {
      this.showSessionWarning();
    }, this.sessionTimeout - this.warningTime);

    // Set session timeout
    this.timeoutId = setTimeout(() => {
      this.expireSession();
    }, this.sessionTimeout);
  }

  setupActivityListeners() {
    const events = ['mousedown', 'mousemove', 'keypress', 'scroll', 'touchstart'];
    
    events.forEach(event => {
      document.addEventListener(event, () => {
        this.resetTimeout();
      }, true);
    });
  }

  showSessionWarning() {
    // Show modal warning user about session expiry
    const extendSession = window.confirm(
      'Phiên đăng nhập sắp hết hạn. Bạn có muốn gia hạn không?'
    );

    if (extendSession) {
      this.resetTimeout();
      // Optionally refresh token
      authService.refreshToken().catch(() => {
        this.expireSession();
      });
    }
  }

  expireSession() {
    authService.logout();
    window.location.href = '/login?reason=session_expired';
  }

  endSession() {
    if (this.timeoutId) clearTimeout(this.timeoutId);
    if (this.warningId) clearTimeout(this.warningId);
  }
}

export default new SessionManager();
```

### **2. Multi-Factor Authentication (Future)**
```javascript
// components/MFASetup.jsx
import React, { useState } from 'react';

const MFASetup = () => {
  const [mfaEnabled, setMfaEnabled] = useState(false);
  const [qrCode, setQrCode] = useState('');
  const [verificationCode, setVerificationCode] = useState('');

  const enableMFA = async () => {
    try {
      // Call backend to setup MFA
      const response = await httpClient.post('/auth/mfa/setup');
      setQrCode(response.data.qrCode);
    } catch (error) {
      console.error('MFA setup error:', error);
    }
  };

  const verifyMFA = async () => {
    try {
      const response = await httpClient.post('/auth/mfa/verify', {
        code: verificationCode
      });
      
      if (response.success) {
        setMfaEnabled(true);
        alert('MFA đã được kích hoạt thành công!');
      }
    } catch (error) {
      alert('Mã xác thực không chính xác');
    }
  };

  return (
    <div className="mfa-setup">
      <h3>Xác thực 2 bước (2FA)</h3>
      
      {!mfaEnabled ? (
        <div>
          <p>Tăng cường bảo mật cho tài khoản của bạn</p>
          <button onClick={enableMFA}>Kích hoạt 2FA</button>
          
          {qrCode && (
            <div>
              <img src={qrCode} alt="QR Code" />
              <input
                type="text"
                value={verificationCode}
                onChange={(e) => setVerificationCode(e.target.value)}
                placeholder="Nhập mã từ ứng dụng Authenticator"
              />
              <button onClick={verifyMFA}>Xác thực</button>
            </div>
          )}
        </div>
      ) : (
        <div>
          <p>✅ Xác thực 2 bước đã được kích hoạt</p>
          <button onClick={() => setMfaEnabled(false)}>Tắt 2FA</button>
        </div>
      )}
    </div>
  );
};

export default MFASetup;
```

---

## 📱 **Mobile App Considerations**

### **1. React Native Firebase**
```javascript
// For React Native apps
import auth from '@react-native-firebase/auth';

class MobileAuthService {
  async signInWithPhone(phoneNumber) {
    const confirmation = await auth().signInWithPhoneNumber(phoneNumber);
    return confirmation;
  }

  async confirmCode(confirmation, code) {
    const userCredential = await confirmation.confirm(code);
    return userCredential;
  }

  async signInWithGoogle() {
    // Google Sign-In implementation
  }

  async signInWithFacebook() {
    // Facebook Sign-In implementation
  }
}
```

### **2. Biometric Authentication**
```javascript
// For mobile biometric auth
import TouchID from 'react-native-touch-id';

const enableBiometric = async () => {
  try {
    const biometryType = await TouchID.isSupported();
    if (biometryType) {
      const isAuthenticated = await TouchID.authenticate('Xác thực để đăng nhập');
      return isAuthenticated;
    }
  } catch (error) {
    console.error('Biometric auth error:', error);
  }
  return false;
};
```

---

## 🧪 **Testing Authentication**

### **1. Unit Tests**
```javascript
// __tests__/authService.test.js
import authService from '../services/authService';

describe('AuthService', () => {
  test('should login with valid credentials', async () => {
    const result = await authService.login('test@example.com', 'password123');
    expect(result.success).toBe(true);
    expect(result.role).toBeDefined();
  });

  test('should throw error with invalid credentials', async () => {
    await expect(
      authService.login('invalid@email.com', 'wrongpassword')
    ).rejects.toThrow();
  });

  test('should check roles correctly', () => {
    authService.userRole = 'Admin';
    expect(authService.hasRole('Admin')).toBe(true);
    expect(authService.hasRole('ChuCoSo')).toBe(true); // Admin has all roles
    expect(authService.hasAnyRole(['ChuCoSo', 'KhachHang'])).toBe(true);
  });
});
```

### **2. Integration Tests**
```javascript
// __tests__/authFlow.test.js
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from '../contexts/AuthContext';
import LoginForm from '../components/LoginForm';

const renderWithProviders = (ui) => {
  return render(
    <BrowserRouter>
      <AuthProvider>
        {ui}
      </AuthProvider>
    </BrowserRouter>
  );
};

describe('Authentication Flow', () => {
  test('should redirect to dashboard after login', async () => {
    renderWithProviders(<LoginForm />);
    
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: 'admin@test.com' }
    });
    
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: 'password123' }
    });
    
    fireEvent.click(screen.getByRole('button', { name: /đăng nhập/i }));
    
    await waitFor(() => {
      expect(window.location.pathname).toBe('/admin/dashboard');
    });
  });
});
```

**🔐 Firebase Authentication & Authorization implementation hoàn tất với đầy đủ tính năng bảo mật và phân quyền!**

---

## 🚀 Google Sign‑in nhanh

Đã bật provider Google trong Firebase (ảnh minh hoạ ở yêu cầu). Ở FE chỉ cần popup Google rồi gửi Firebase ID Token về backend:

```javascript
import { getAuth, GoogleAuthProvider, signInWithPopup } from 'firebase/auth';

export async function loginWithGoogle() {
  const auth = getAuth();
  const provider = new GoogleAuthProvider();
  const credential = await signInWithPopup(auth, provider);
  const idToken = await credential.user.getIdToken();

  const res = await fetch(`${process.env.REACT_APP_API_BASE_URL}/auth/google`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ idToken })
  });
  const json = await res.json();
  if (!json.success) throw new Error(json.message || 'Google login failed');

  // Lưu token/role như các bước ở Auth Service
  localStorage.setItem('authToken', json.data.accessToken);
  localStorage.setItem('userRole', json.data.roles?.[0] ?? 'KhachHang');
  localStorage.setItem('userData', JSON.stringify(json.data.user));
  return json.data;
}
```

Làm mới JWT backend nếu cần:

```javascript
import { getAuth } from 'firebase/auth';

export async function refreshBackendToken() {
  const user = getAuth().currentUser;
  if (!user) return null;
  const idToken = await user.getIdToken(true);
  const res = await fetch(`${process.env.REACT_APP_API_BASE_URL}/auth/refresh-token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ idToken })
  });
  return res.json();
}
```