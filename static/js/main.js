// main.js - For Frontend dynamic features with Flask Backend

// Check if user is logged in for pages like pay.html
window.addEventListener('load', () => {
    const currentUser = "{{ session.get('user')|tojson|safe }}"; // Flask inject session
    if (window.location.pathname.includes('pay') && !currentUser) {
        alert('You need to login first!');
        window.location.href = '/login';
    }
});

// Fetch services dynamically (for services.html)
async function loadServices() {
    try {
        const res = await fetch('/api/services');
        if (!res.ok) throw new Error('Failed to load services');
        const services = await res.json();
        const container = document.getElementById('servicesContainer');
        if (!container) return; // Ensure element exists
        container.innerHTML = '';
        services.forEach(s => {
            const div = document.createElement('div');
            div.className = 'service';
            div.innerHTML = `<h2>${s.name}</h2><p>${s.description || ''}</p>`;
            container.appendChild(div);
        });
    } catch (err) {
        console.error(err);
    }
}

// Optional: Logout button
const logoutBtn = document.getElementById('logoutBtn');
if (logoutBtn) {
    logoutBtn.addEventListener('click', async () => {
        await fetch('/logout', { method: 'POST' });
        window.location.href = '/';
    });
}

// Initial load for services
if (document.getElementById('servicesContainer')) {
    loadServices();
}
