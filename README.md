# Dev-Core — Company Website & Admin Management System

A full-stack **ASP.NET Core (Minimal API)** web application serving the Dev-Core corporate website and a complete internal admin management panel. The system powers the public marketing site, course enrollment, service inquiries, and an internal role-based admin dashboard backed by a **PostgreSQL** database.

---

## 🏗️ Architecture Overview

```
dev-core-website/
├── Program.cs               ← All backend Minimal API endpoints + Seed data
├── Models/                  ← EF Core entity models
│   ├── Admin.cs
│   ├── Course.cs
│   ├── Client.cs
│   ├── Staff.cs
│   ├── Transaction.cs
│   ├── RecurringPayment.cs
│   ├── WebServer.cs
│   ├── EmailAccount.cs
│   ├── Student.cs
│   ├── Service.cs
│   └── Payment.cs
├── Data/
│   └── AppDbContext.cs      ← EF Core DbContext (PostgreSQL via Npgsql)
├── appsettings.json         ← Connection string config
└── wwwroot/                 ← Static frontend assets
    ├── index.html           ← Main marketing landing page
    ├── about.html           ← About / team page
    ├── services.html        ← Services showcase
    ├── courses.html         ← Courses catalog + enrollment form
    ├── contact.html         ← Contact / inquiry form
    ├── rateus.html          ← Testimonials / rate us page
    ├── admin.html           ← Admin panel (RBAC protected)
    ├── admin.js             ← Admin panel logic (CRUD + RBAC)
    ├── admin.css            ← Admin panel styles (dark mode)
    ├── app.js               ← Public website logic
    ├── style.css            ← Public website styles (dark/light mode)
    └── logo.png             ← Dev-Core logo
```

---

## ✅ Feature Overview

### 🌐 Public Website
- Fully responsive dark/light mode marketing site
- Animated particle background effects
- Hero section, services showcase, division highlights
- Course catalog with live enrollment form (saves to PostgreSQL Students table)
- Contact form with real-time AI-style chatbot response
- Rate Us / Testimonials page

### 🔐 Admin Panel (`/admin.html`)
- **Role-Based Access Control (RBAC)** with 6 roles
- Login screen with dynamic database authentication + fallback hardcoded accounts
- JWT-free session via `sessionStorage`
- Sidebar navigation with live record count badges

#### Managed Sections:
| Section | Features |
|---|---|
| **Dashboard** | Revenue, expenses, profit, client/staff/server stats |
| **Transactions** | Full CRUD: income and expense tracking |
| **Recurring Payments** | Full CRUD: subscriptions and billing cycles |
| **Clients** | Full CRUD: client CRM with service + team assignment |
| **Staff & Access** | Tabs: Staff Members, Web Servers, Email Accounts, Admin Users, Roles |
| **↳ Admin Users** | Full CRUD: manage admin accounts and their roles |
| **↳ Roles & Access** | Read-only matrix showing permissions per role |
| **Course Materials** | Full CRUD: manage courses (title, division, price, capacity, status) |
| **Web Servers** | Full CRUD: server inventory with SSH credentials |
| **Email Accounts** | Full CRUD: SMTP/IMAP accounts per department |

---

## 🔑 Admin Access Credentials

### Login URL
```
http://your-domain/admin.html
```



### RBAC Permissions Matrix

| Role | Transactions | Recurring | Clients | Staff | Web Servers | Emails | Admin Users | Courses | Course Videos/Materials | My Students |
|---|---|---|---|---|---|---|---|---|---|---|
| **Root** | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full |
| **Admin** | Read | Read | Read | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full |
| **Finance Admin** | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | Full CRUD | None | Read | None | None |
| **Finance** | Read + Add | Read + Add | Read + Add | Read + Add | Read + Add | Read + Add | None | Read | None | None |
| **Web Admin** | Read | Read | Read | None | Full CRUD | None | None | None | None | None |
| **Email Admin** | Read | Read | Read | None | None | Full CRUD | None | None | None | None |
| **Teacher** | None | None | None | None | None | None | None | None | Full CRUD (assigned courses only) | Read (assigned division only) |

---

## 🗄️ Database Schema (PostgreSQL / SQLite fallback)

All tables are auto-created on first startup using EF Core `EnsureCreated()` and seeded with sample data.

| Table | Description |
|---|---|
| `Transactions` | Income and expense entries |
| `RecurringPayments` | Subscription and billing cycles |
| `Clients` | CRM client records |
| `Staff` | Team members and HR data |
| `WebServers` | Server inventory with SSH info |
| `EmailAccounts` | Company email and SMTP/IMAP config |
| `Admins` | Admin panel user accounts with roles |
| `Courses` | Training course catalog |
| `Students` | Public enrollment submissions (saves to Students table) |
| `Services` | Service catalog (offered by each division) |
| `Payments` | Student/client payment records |
| `CourseMaterials` | Course files / videos / materials metadata |
| `Invoices` | Accounting invoices (Draft / Sent / Paid) |
| `InvoiceItems` | Line items for invoices |
| `Quotes` | Accounting service quotes |
| `QuoteItems` | Line items for quotes |
| `Expenses` | Corporate expense records |
| `ChartOfAccounts` | Accounting chart of accounts classifications |

---

## ⚙️ Configuration (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=devcore_db;Username=devcore_user;Password=your_password"
  }
}
```

> 📋 **Create the PostgreSQL database:**
> ```sql
> CREATE USER devcore_user WITH PASSWORD 'your_password';
> CREATE DATABASE devcore_db OWNER devcore_user;
> ```

---

## 🚀 Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0))
- Optional: [PostgreSQL 14+](https://www.postgresql.org/download/)

### Run locally
```bash
# Clone and navigate
git clone https://github.com/your-org/dev-core-website.git
cd dev-core-website

# Then run:
dotnet run
```

> 💡 **Zero-Configuration SQLite Fallback:**
> If no connection string is specified in `appsettings.json` (or if it is left empty), the app automatically falls back to an embedded SQLite database (`devcore.db`) and automatically seeds all sample data, including the new Course Materials and Teacher logins. This allows running and testing immediately out of the box without Postgres installation.

The app starts at `http://localhost:5000`. The database will auto-create tables and seed them on first run.

---

## 🚀 Production Deployment (Linux + NGINX)

### 1. Publish the Project

```bash
dotnet publish -c Release -o ./publish
```

For servers without .NET SDK (self-contained):
```bash
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish
```

### 2. Transfer to Server

```bash
# Compress
tar -czvf devcore-publish.tar.gz -C ./publish .

# Upload
scp devcore-publish.tar.gz yusef@your-server-ip:/tmp/

# On the server:
sudo mkdir -p /var/www/dev-core
sudo tar -xzvf /tmp/devcore-publish.tar.gz -C /var/www/dev-core
sudo chown -R www-data:www-data /var/www/dev-core
```

### 3. Configure Systemd Service

```bash
sudo nano /etc/systemd/system/devcore.service
```

```ini
[Unit]
Description=Dev-Core ASP.NET Core Web Application
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/dev-core
ExecStart=/usr/bin/dotnet /var/www/dev-core/DevCore.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=dev-core
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable devcore.service
sudo systemctl start devcore.service
sudo systemctl status devcore.service
```

### 4. NGINX Reverse Proxy Configuration

```bash
sudo nano /etc/nginx/sites-available/dev-core.site
```

```nginx
server {
    listen 80;
    server_name dev-core.site www.dev-core.site;

    location / {
        proxy_pass         http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/dev-core.site /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

### 5. SSL / HTTPS (Let's Encrypt)

```bash
sudo apt install certbot python3-certbot-nginx -y
sudo certbot --nginx -d dev-core.site -d www.dev-core.site
```

Certbot auto-configures HTTPS (port 443) and HTTP→HTTPS redirects.

---

## 📡 API Endpoints

All endpoints accept/return JSON. The admin panel uses these endpoints for CRUD operations.

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/admins` | List all admin users |
| POST | `/api/admins` | Create admin user |
| PUT | `/api/admins/{id}` | Update admin user |
| DELETE | `/api/admins/{id}` | Delete admin user |
| GET | `/api/courses` | List all courses |
| POST | `/api/courses` | Create course |
| PUT | `/api/courses/{id}` | Update course |
| DELETE | `/api/courses/{id}` | Delete course |
| GET | `/api/transactions` | List all transactions |
| POST | `/api/transactions` | Create transaction |
| PUT | `/api/transactions/{id}` | Update transaction |
| DELETE | `/api/transactions/{id}` | Delete transaction |
| GET | `/api/recurring` | List recurring payments |
| POST | `/api/recurring` | Create recurring payment |
| PUT | `/api/recurring/{id}` | Update recurring payment |
| DELETE | `/api/recurring/{id}` | Delete recurring payment |
| GET | `/api/clients` | List all clients |
| POST | `/api/clients` | Create client |
| PUT | `/api/clients/{id}` | Update client |
| DELETE | `/api/clients/{id}` | Delete client |
| GET | `/api/staff` | List all staff |
| POST | `/api/staff` | Create staff member |
| PUT | `/api/staff/{id}` | Update staff member |
| DELETE | `/api/staff/{id}` | Delete staff member |
| GET | `/api/webservers` | List all web servers |
| POST | `/api/webservers` | Add web server |
| PUT | `/api/webservers/{id}` | Update server |
| DELETE | `/api/webservers/{id}` | Remove server |
| GET | `/api/emails` | List email accounts |
| POST | `/api/emails` | Add email account |
| PUT | `/api/emails/{id}` | Update email account |
| DELETE | `/api/emails/{id}` | Remove email account |
| GET | `/api/stats` | Dashboard aggregate statistics |
| POST | `/api/contact` | Handle contact form submissions |
| POST | `/api/enroll` | Handle course enrollment, creates student record, and automatically generates a draft invoice if course has a price |
| POST | `/api/upload` | Handle video/file upload for Course Materials (max 200MB, videos <= 1hr limit checked) |
| GET | `/api/course-materials` | List course materials, optionally filtered by course/division |
| DELETE | `/api/course-materials/{id}` | Delete a course material record and remove physical file from disk |

---

## 🔒 Security Notes

> [!IMPORTANT]
> Before deploying to production:
> - Change the default admin password from `Dev-Core1234` to a strong, unique password
> - Move credential management to environment variables or a secrets manager
> - Restrict the `/admin.html` route via NGINX IP whitelisting for extra protection
> - Enable HTTPS before going live (Certbot instructions above)
> - Use `dotnet user-secrets` or environment variables for the database connection string

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 8 Minimal API |
| ORM | Entity Framework Core 8 (Npgsql) |
| Database | PostgreSQL 14+ |
| Frontend | Vanilla HTML5, CSS3, JavaScript (ES2022) |
| Fonts | Inter + Fira Code (Google Fonts) |
| Web Server | NGINX (reverse proxy) |
| Process Manager | systemd |
| SSL | Let's Encrypt (Certbot) |

---

*© 2025 Dev-Core — Zerobyte · Nova · IT Divisions*
