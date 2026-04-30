// API URL
const API_URL = '';

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupLoginForm();
    setupRegisterForm();
});

function setupLoginForm() {
    const loginForm = document.getElementById('login-form');
    if (loginForm) {
        loginForm.addEventListener('submit', handleLogin);
    }
}

function setupRegisterForm() {
    const registerForm = document.getElementById('register-form');
    if (registerForm) {
        registerForm.addEventListener('submit', handleRegister);
    }
}

// Handle login
async function handleLogin(e) {
    e.preventDefault();
    console.log('Login form submitted');
    
    const email = document.getElementById('login-email').value;
    const password = document.getElementById('login-password').value;
    
    console.log('Email:', email, 'Password:', password ? '***' : 'empty');
    
    if (!email || !password) {
        showToast('Por favor completa todos los campos', 'error');
        return;
    }
    
    try {
        console.log('Sending login request...');
        const response = await fetch(`${API_URL}/api/users/login`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ email, password })
        });
        
        console.log('Response status:', response.status);
        
        if (response.ok) {
            const user = await response.json();
            console.log('User logged in:', user);
            localStorage.setItem('scrumUser', JSON.stringify(user));
            showToast('Inicio de sesión exitoso');
            window.location.href = 'app.html';
        } else {
            const error = await response.text();
            console.error('Login failed:', error);
            showToast(error || 'Credenciales incorrectas', 'error');
        }
    } catch (error) {
        console.error('Login error:', error);
        showToast('Error al conectar con el servidor', 'error');
    }
}

// Toggle password visibility
function togglePassword(inputId = 'login-password', iconId = 'toggle-icon') {
    const passwordInput = document.getElementById(inputId);
    const toggleIcon = document.getElementById(iconId);

    if (passwordInput && toggleIcon) {
        if (passwordInput.type === 'password') {
            passwordInput.type = 'text';
            toggleIcon.classList.remove('fa-eye');
            toggleIcon.classList.add('fa-eye-slash');
        } else {
            passwordInput.type = 'password';
            toggleIcon.classList.remove('fa-eye-slash');
            toggleIcon.classList.add('fa-eye');
        }
    }
}

// Fill demo credentials
function fillDemo(email) {
    document.getElementById('login-email').value = email;
    document.getElementById('login-password').value = 'admin123';
    document.getElementById('login-password').focus();
}

// Handle register
async function handleRegister(e) {
    e.preventDefault();
    console.log('Register form submitted');

    const name = document.getElementById('register-name').value;
    const email = document.getElementById('register-email').value;
    const password = document.getElementById('register-password').value;
    const confirm = document.getElementById('register-confirm').value;
    const acceptTerms = document.getElementById('accept-terms').checked;

    console.log('Name:', name, 'Email:', email, 'Password:', password ? '***' : 'empty', 'Accept:', acceptTerms);

    if (!name || !email || !password || !confirm) {
        showToast('Por favor completa todos los campos', 'error');
        return;
    }

    if (!acceptTerms) {
        showToast('Debes aceptar los términos y condiciones', 'error');
        return;
    }

    if (password !== confirm) {
        showToast('Las contraseñas no coinciden', 'error');
        return;
    }

    if (password.length < 6) {
        showToast('La contraseña debe tener al menos 6 caracteres', 'error');
        return;
    }

    try {
        console.log('Sending register request...');
        const response = await fetch(`${API_URL}/api/users/register`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                name,
                email,
                password,
                role: 'Developer'
            })
        });

        console.log('Response status:', response.status);

        if (response.ok) {
            showToast('¡Cuenta creada exitosamente! Revisa tu correo');
            setTimeout(() => {
                window.location.href = 'login.html';
            }, 2000);
        } else {
            const error = await response.text();
            console.error('Register failed:', error);
            showToast(error || 'Error al crear cuenta', 'error');
        }
    } catch (error) {
        console.error('Register error:', error);
        showToast('Error al conectar con el servidor', 'error');
    }
}

// Show toast notification
function showToast(message, type = 'success') {
    const toast = document.getElementById('toast');
    const toastMessage = document.getElementById('toast-message');
    
    if (toast && toastMessage) {
        toastMessage.textContent = message;
        toast.className = `toast ${type} show`;
        
        setTimeout(() => {
            toast.classList.remove('show');
        }, 3000);
    }
}

// Make functions globally available
window.togglePassword = togglePassword;
window.fillDemo = fillDemo;
window.showToast = showToast;
