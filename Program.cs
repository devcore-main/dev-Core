using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DevCore.Data;
using DevCore.Models;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// DB configuration with fallback to SQLite for local development
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.IsNullOrEmpty(connStr))
    {
        options.UseSqlite("Data Source=devcore.db");
    }
    else
    {
        options.UseNpgsql(connStr);
    }
});

// JSON options: camelCase for the frontend
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// 200MB max upload (videos)
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 200 * 1024 * 1024);


var app = builder.Build();

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// ─── Upload folder (served as static) ────────────────────────────────────────
var uploadFolder = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "uploads");
Directory.CreateDirectory(uploadFolder);


// ─── Auto-migrate + Seed on startup ──────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated(); // Creates tables if they don't exist
        await SeedData(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Warning: {ex.Message}");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  STUDENTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/students", async (AppDbContext db) =>
    Results.Ok(await db.Students.OrderByDescending(s => s.CreatedAt).ToListAsync()));

app.MapGet("/api/students/{id}", async (int id, AppDbContext db) =>
    await db.Students.FindAsync(id) is Student s ? Results.Ok(s) : Results.NotFound());

app.MapPost("/api/students", async ([FromBody] Student student, AppDbContext db) =>
{
    student.Id = 0;
    student.CreatedAt = DateTime.UtcNow;
    if (!student.EnrolledDate.HasValue)
        student.EnrolledDate = DateOnly.FromDateTime(DateTime.UtcNow);
    db.Students.Add(student);
    await db.SaveChangesAsync();

    // ── Auto-create invoice if course has a price ──────────────────
    Invoice? autoInvoice = null;
    if (!string.IsNullOrWhiteSpace(student.Course))
    {
        var course = await db.Courses
            .FirstOrDefaultAsync(c => c.Title.ToLower().Contains((student.Course ?? "").ToLower()));
        if (course != null && course.Price > 0)
        {
            var count = await db.Invoices.CountAsync() + 1;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            autoInvoice = new Invoice
            {
                InvoiceNumber = $"INV-{today.Year}-{count:D4}",
                ClientName    = student.Name,
                ClientEmail   = student.Email ?? "",
                ClientAddress = "",
                IssueDate     = today,
                DueDate       = today.AddDays(14),
                Status        = "Draft",
                Currency      = "EGP",
                SubTotal      = course.Price,
                Total         = course.Price,
                Notes         = $"Auto-generated on enrollment: {course.Title}",
                Items = new List<InvoiceItem> {
                    new InvoiceItem {
                        Description = course.Title,
                        Quantity    = 1,
                        UnitPrice   = course.Price,
                        Total       = course.Price
                    }
                }
            };
            db.Invoices.Add(autoInvoice);
            await db.SaveChangesAsync();
        }
    }

    return Results.Created($"/api/students/{student.Id}", new
    {
        student,
        invoiceId     = autoInvoice?.Id,
        invoiceNumber = autoInvoice?.InvoiceNumber,
        autoInvoiced  = autoInvoice != null
    });
});


app.MapPut("/api/students/{id}", async (int id, [FromBody] Student updated, AppDbContext db) =>
{
    var student = await db.Students.FindAsync(id);
    if (student is null) return Results.NotFound();
    student.Name = updated.Name;
    student.Email = updated.Email;
    student.Phone = updated.Phone;
    student.Course = updated.Course;
    student.Division = updated.Division;
    student.EnrolledDate = updated.EnrolledDate;
    student.Status = updated.Status;
    student.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(student);
});

app.MapDelete("/api/students/{id}", async (int id, AppDbContext db) =>
{
    var student = await db.Students.FindAsync(id);
    if (student is null) return Results.NotFound();
    db.Students.Remove(student);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ADMINS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/admins", async (AppDbContext db) =>
    Results.Ok(await db.Admins.OrderByDescending(a => a.CreatedAt).ToListAsync()));

app.MapGet("/api/admins/{id}", async (int id, AppDbContext db) =>
    await db.Admins.FindAsync(id) is Admin a ? Results.Ok(a) : Results.NotFound());

app.MapPost("/api/admins", async ([FromBody] Admin admin, AppDbContext db) =>
{
    admin.Id = 0;
    admin.CreatedAt = DateTime.UtcNow;
    db.Admins.Add(admin);
    await db.SaveChangesAsync();
    return Results.Created($"/api/admins/{admin.Id}", admin);
});

app.MapPut("/api/admins/{id}", async (int id, [FromBody] Admin updated, AppDbContext db) =>
{
    var admin = await db.Admins.FindAsync(id);
    if (admin is null) return Results.NotFound();
    admin.Name = updated.Name;
    admin.Email = updated.Email;
    admin.Role = updated.Role;
    admin.Team = updated.Team;
    admin.IsActive = updated.IsActive;
    await db.SaveChangesAsync();
    return Results.Ok(admin);
});

app.MapDelete("/api/admins/{id}", async (int id, AppDbContext db) =>
{
    var admin = await db.Admins.FindAsync(id);
    if (admin is null) return Results.NotFound();
    db.Admins.Remove(admin);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  COURSES API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/courses", async (AppDbContext db) =>
    Results.Ok(await db.Courses.OrderBy(c => c.Title).ToListAsync()));

app.MapGet("/api/courses/{id}", async (int id, AppDbContext db) =>
    await db.Courses.FindAsync(id) is Course c ? Results.Ok(c) : Results.NotFound());

app.MapPost("/api/courses", async ([FromBody] Course course, AppDbContext db) =>
{
    course.Id = 0;
    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/api/courses/{course.Id}", course);
});

app.MapPut("/api/courses/{id}", async (int id, [FromBody] Course updated, AppDbContext db) =>
{
    var course = await db.Courses.FindAsync(id);
    if (course is null) return Results.NotFound();
    course.Title = updated.Title;
    course.Division = updated.Division;
    course.Duration = updated.Duration;
    course.Level = updated.Level;
    course.Price = updated.Price;
    course.MaxStudents = updated.MaxStudents;
    course.IsActive = updated.IsActive;
    await db.SaveChangesAsync();
    return Results.Ok(course);
});

app.MapDelete("/api/courses/{id}", async (int id, AppDbContext db) =>
{
    var course = await db.Courses.FindAsync(id);
    if (course is null) return Results.NotFound();
    db.Courses.Remove(course);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  PAYMENTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/payments", async (AppDbContext db) =>
    Results.Ok(await db.Payments.OrderByDescending(p => p.Id).ToListAsync()));

app.MapGet("/api/payments/{id}", async (int id, AppDbContext db) =>
    await db.Payments.FindAsync(id) is Payment p ? Results.Ok(p) : Results.NotFound());

app.MapPost("/api/payments", async ([FromBody] Payment payment, AppDbContext db) =>
{
    payment.Id = 0;
    db.Payments.Add(payment);
    await db.SaveChangesAsync();
    return Results.Created($"/api/payments/{payment.Id}", payment);
});

app.MapPut("/api/payments/{id}", async (int id, [FromBody] Payment updated, AppDbContext db) =>
{
    var payment = await db.Payments.FindAsync(id);
    if (payment is null) return Results.NotFound();
    payment.ClientName = updated.ClientName;
    payment.CourseName = updated.CourseName;
    payment.Amount = updated.Amount;
    payment.Currency = updated.Currency;
    payment.Method = updated.Method;
    payment.Status = updated.Status;
    payment.Date = updated.Date;
    payment.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(payment);
});

app.MapDelete("/api/payments/{id}", async (int id, AppDbContext db) =>
{
    var payment = await db.Payments.FindAsync(id);
    if (payment is null) return Results.NotFound();
    db.Payments.Remove(payment);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  SERVICES API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/services", async (AppDbContext db) =>
    Results.Ok(await db.Services.OrderBy(s => s.Name).ToListAsync()));

app.MapGet("/api/services/{id}", async (int id, AppDbContext db) =>
    await db.Services.FindAsync(id) is Service s ? Results.Ok(s) : Results.NotFound());

app.MapPost("/api/services", async ([FromBody] Service service, AppDbContext db) =>
{
    service.Id = 0;
    db.Services.Add(service);
    await db.SaveChangesAsync();
    return Results.Created($"/api/services/{service.Id}", service);
});

app.MapPut("/api/services/{id}", async (int id, [FromBody] Service updated, AppDbContext db) =>
{
    var service = await db.Services.FindAsync(id);
    if (service is null) return Results.NotFound();
    service.Name = updated.Name;
    service.Team = updated.Team;
    service.Category = updated.Category;
    service.Description = updated.Description;
    service.Price = updated.Price;
    service.IsActive = updated.IsActive;
    await db.SaveChangesAsync();
    return Results.Ok(service);
});

app.MapDelete("/api/services/{id}", async (int id, AppDbContext db) =>
{
    var service = await db.Services.FindAsync(id);
    if (service is null) return Results.NotFound();
    db.Services.Remove(service);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  CLIENTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/clients", async (AppDbContext db) =>
    Results.Ok(await db.Clients.OrderByDescending(c => c.CreatedAt).ToListAsync()));

app.MapGet("/api/clients/{id}", async (int id, AppDbContext db) =>
    await db.Clients.FindAsync(id) is Client c ? Results.Ok(c) : Results.NotFound());

app.MapPost("/api/clients", async ([FromBody] Client client, AppDbContext db) =>
{
    client.Id = 0;
    client.CreatedAt = DateTime.UtcNow;
    db.Clients.Add(client);
    await db.SaveChangesAsync();
    return Results.Created($"/api/clients/{client.Id}", client);
});

app.MapPut("/api/clients/{id}", async (int id, [FromBody] Client updated, AppDbContext db) =>
{
    var client = await db.Clients.FindAsync(id);
    if (client is null) return Results.NotFound();
    client.Name = updated.Name;
    client.Email = updated.Email;
    client.Phone = updated.Phone;
    client.Company = updated.Company;
    client.ServiceRequested = updated.ServiceRequested;
    client.Team = updated.Team;
    client.Status = updated.Status;
    client.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(client);
});

// ═══════════════════════════════════════════════════════════════════════════════
//  TRANSACTIONS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/transactions", async (AppDbContext db) =>
    Results.Ok(await db.Transactions.OrderByDescending(t => t.Date).ToListAsync()));

app.MapGet("/api/transactions/{id}", async (int id, AppDbContext db) =>
    await db.Transactions.FindAsync(id) is Transaction t ? Results.Ok(t) : Results.NotFound());

app.MapPost("/api/transactions", async ([FromBody] Transaction t, AppDbContext db) =>
{
    t.Id = 0;
    t.CreatedAt = DateTime.UtcNow;
    db.Transactions.Add(t);
    await db.SaveChangesAsync();
    return Results.Created($"/api/transactions/{t.Id}", t);
});

app.MapPut("/api/transactions/{id}", async (int id, [FromBody] Transaction updated, AppDbContext db) =>
{
    var t = await db.Transactions.FindAsync(id);
    if (t is null) return Results.NotFound();
    t.Description = updated.Description;
    t.Amount = updated.Amount;
    t.Currency = updated.Currency;
    t.Type = updated.Type;
    t.Category = updated.Category;
    t.Date = updated.Date;
    t.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(t);
});

app.MapDelete("/api/transactions/{id}", async (int id, AppDbContext db) =>
{
    var t = await db.Transactions.FindAsync(id);
    if (t is null) return Results.NotFound();
    db.Transactions.Remove(t);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  RECURRING PAYMENTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/recurring", async (AppDbContext db) =>
    Results.Ok(await db.RecurringPayments.OrderByDescending(r => r.Id).ToListAsync()));

app.MapGet("/api/recurring/{id}", async (int id, AppDbContext db) =>
    await db.RecurringPayments.FindAsync(id) is RecurringPayment r ? Results.Ok(r) : Results.NotFound());

app.MapPost("/api/recurring", async ([FromBody] RecurringPayment r, AppDbContext db) =>
{
    r.Id = 0;
    db.RecurringPayments.Add(r);
    await db.SaveChangesAsync();
    return Results.Created($"/api/recurring/{r.Id}", r);
});

app.MapPut("/api/recurring/{id}", async (int id, [FromBody] RecurringPayment updated, AppDbContext db) =>
{
    var r = await db.RecurringPayments.FindAsync(id);
    if (r is null) return Results.NotFound();
    r.Name = updated.Name;
    r.Amount = updated.Amount;
    r.Currency = updated.Currency;
    r.Frequency = updated.Frequency;
    r.NextDate = updated.NextDate;
    r.Status = updated.Status;
    r.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(r);
});

app.MapDelete("/api/recurring/{id}", async (int id, AppDbContext db) =>
{
    var r = await db.RecurringPayments.FindAsync(id);
    if (r is null) return Results.NotFound();
    db.RecurringPayments.Remove(r);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  STAFF API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/staff", async (AppDbContext db) =>
    Results.Ok(await db.Staff.OrderByDescending(s => s.Id).ToListAsync()));

app.MapGet("/api/staff/{id}", async (int id, AppDbContext db) =>
    await db.Staff.FindAsync(id) is Staff s ? Results.Ok(s) : Results.NotFound());

app.MapPost("/api/staff", async ([FromBody] Staff s, AppDbContext db) =>
{
    s.Id = 0;
    db.Staff.Add(s);
    await db.SaveChangesAsync();
    return Results.Created($"/api/staff/{s.Id}", s);
});

app.MapPut("/api/staff/{id}", async (int id, [FromBody] Staff updated, AppDbContext db) =>
{
    var s = await db.Staff.FindAsync(id);
    if (s is null) return Results.NotFound();
    s.Name = updated.Name;
    s.Email = updated.Email;
    s.Phone = updated.Phone;
    s.Role = updated.Role;
    s.Department = updated.Department;
    s.Salary = updated.Salary;
    s.HireDate = updated.HireDate;
    s.Status = updated.Status;
    s.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(s);
});

app.MapDelete("/api/staff/{id}", async (int id, AppDbContext db) =>
{
    var s = await db.Staff.FindAsync(id);
    if (s is null) return Results.NotFound();
    db.Staff.Remove(s);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  WEB SERVERS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/webservers", async (AppDbContext db) =>
    Results.Ok(await db.WebServers.OrderBy(w => w.Name).ToListAsync()));

app.MapGet("/api/webservers/{id}", async (int id, AppDbContext db) =>
    await db.WebServers.FindAsync(id) is WebServer w ? Results.Ok(w) : Results.NotFound());

app.MapPost("/api/webservers", async ([FromBody] WebServer w, AppDbContext db) =>
{
    w.Id = 0;
    db.WebServers.Add(w);
    await db.SaveChangesAsync();
    return Results.Created($"/api/webservers/{w.Id}", w);
});

app.MapPut("/api/webservers/{id}", async (int id, [FromBody] WebServer updated, AppDbContext db) =>
{
    var w = await db.WebServers.FindAsync(id);
    if (w is null) return Results.NotFound();
    w.Name = updated.Name;
    w.IpAddress = updated.IpAddress;
    w.SshPort = updated.SshPort;
    w.SshUser = updated.SshUser;
    w.SshPassword = updated.SshPassword;
    w.Os = updated.Os;
    w.Provider = updated.Provider;
    w.MonthlyCost = updated.MonthlyCost;
    w.Status = updated.Status;
    w.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(w);
});

app.MapDelete("/api/webservers/{id}", async (int id, AppDbContext db) =>
{
    var w = await db.WebServers.FindAsync(id);
    if (w is null) return Results.NotFound();
    db.WebServers.Remove(w);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  EMAIL ACCOUNTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/emails", async (AppDbContext db) =>
    Results.Ok(await db.EmailAccounts.OrderBy(e => e.Email).ToListAsync()));

app.MapGet("/api/emails/{id}", async (int id, AppDbContext db) =>
    await db.EmailAccounts.FindAsync(id) is EmailAccount e ? Results.Ok(e) : Results.NotFound());

app.MapPost("/api/emails", async ([FromBody] EmailAccount e, AppDbContext db) =>
{
    e.Id = 0;
    db.EmailAccounts.Add(e);
    await db.SaveChangesAsync();
    return Results.Created($"/api/emails/{e.Id}", e);
});

app.MapPut("/api/emails/{id}", async (int id, [FromBody] EmailAccount updated, AppDbContext db) =>
{
    var e = await db.EmailAccounts.FindAsync(id);
    if (e is null) return Results.NotFound();
    e.Email = updated.Email;
    e.Password = updated.Password;
    e.SmtpHost = updated.SmtpHost;
    e.SmtpPort = updated.SmtpPort;
    e.ImapHost = updated.ImapHost;
    e.ImapPort = updated.ImapPort;
    e.Department = updated.Department;
    e.OwnerName = updated.OwnerName;
    e.Status = updated.Status;
    e.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(e);
});

app.MapDelete("/api/emails/{id}", async (int id, AppDbContext db) =>
{
    var e = await db.EmailAccounts.FindAsync(id);
    if (e is null) return Results.NotFound();
    db.EmailAccounts.Remove(e);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  LEGACY CONTACT ENDPOINT (keep existing functionality)
// ═══════════════════════════════════════════════════════════════════════════════
app.MapPost("/api/contact", async ([FromBody] ContactSubmission submission) =>
{
    Console.WriteLine($"[CONTACT] {submission.Name} | {submission.Email} | {submission.Team} | {submission.Service}");
    await Task.Delay(300);
    return Results.Ok(new
    {
        Success = true,
        Message = $"System: Connection established. Hello {submission.Name}, your request for '{submission.Service}' has been routed to the {submission.Team} team. We will get back to you shortly!"
    });
});

// ─── Stats endpoint ───────────────────────────────────────────────────────────
app.MapGet("/api/stats", async (AppDbContext db) =>
{
    var revenue = await db.Transactions.Where(t => t.Type == "Income").SumAsync(t => t.Amount);
    var expenses = await db.Transactions.Where(t => t.Type == "Expense").SumAsync(t => t.Amount);
    var profit = revenue - expenses;

    var srvOnline = await db.WebServers.CountAsync(w => w.Status == "Online");
    var srvMaint = await db.WebServers.CountAsync(w => w.Status == "Maintenance");
    var srvOffline = await db.WebServers.CountAsync(w => w.Status == "Offline");

    var recActiveCount = await db.RecurringPayments.CountAsync(r => r.Status == "Active");
    var recMonthlyTotal = await db.RecurringPayments.Where(r => r.Status == "Active").SumAsync(r => r.Amount);

    var clActive = await db.Clients.CountAsync(c => c.Status == "Active");
    var clLead = await db.Clients.CountAsync(c => c.Status == "Lead");
    var clDone = await db.Clients.CountAsync(c => c.Status == "Completed");

    var staffTotal = await db.Staff.CountAsync();
    var staffSalarySum = await db.Staff.Where(s => s.Status == "Active").SumAsync(s => s.Salary) ?? 0;

    return Results.Ok(new
    {
        students = await db.Students.CountAsync(),
        clients = await db.Clients.CountAsync(),
        courses = await db.Courses.CountAsync(c => c.IsActive),
        transactions = await db.Transactions.CountAsync(),
        services = await db.Services.CountAsync(s => s.IsActive),
        revenue = revenue,
        expenses = expenses,
        profit = profit,
        staff = await db.Staff.CountAsync(s => s.Status == "Active"),
        servers = await db.WebServers.CountAsync(),
        emails = await db.EmailAccounts.CountAsync(),
        srvOnline = srvOnline,
        srvMaint = srvMaint,
        srvOffline = srvOffline,
        recActiveCount = recActiveCount,
        recMonthlyTotal = recMonthlyTotal,
        clActive = clActive,
        clLead = clLead,
        clDone = clDone,
        staffTotal = staffTotal,
        staffSalary = staffSalarySum,
        invoices = await db.Invoices.CountAsync(),
        invoicesPaid = await db.Invoices.CountAsync(i => i.Status == "Paid"),
        invoicesOverdue = await db.Invoices.CountAsync(i => i.Status == "Overdue"),
        invoicesRevenue = await db.Invoices.Where(i => i.Status == "Paid").SumAsync(i => i.Total),
        quotes = await db.Quotes.CountAsync(),
        expensesTotal = await db.Expenses.SumAsync(e => e.Amount)
    });
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ACCOUNTING CRM — INVOICES API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/invoices", async (AppDbContext db) =>
    Results.Ok(await db.Invoices.Include(i => i.Items)
        .OrderByDescending(i => i.CreatedAt).ToListAsync()));

app.MapGet("/api/invoices/{id}", async (int id, AppDbContext db) =>
    await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id)
        is Invoice inv ? Results.Ok(inv) : Results.NotFound());

app.MapPost("/api/invoices", async ([FromBody] Invoice invoice, AppDbContext db) =>
{
    invoice.Id = 0;
    invoice.CreatedAt = DateTime.UtcNow;
    // Auto-generate invoice number
    var count = await db.Invoices.CountAsync() + 1;
    invoice.InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{count:D4}";
    // Recalculate totals from items
    foreach (var item in invoice.Items) { item.Id = 0; item.Total = item.Quantity * item.UnitPrice; }
    invoice.SubTotal = invoice.Items.Sum(i => i.Total);
    invoice.Total = invoice.SubTotal;
    db.Invoices.Add(invoice);
    await db.SaveChangesAsync();
    return Results.Created($"/api/invoices/{invoice.Id}", invoice);
});

app.MapPut("/api/invoices/{id}", async (int id, [FromBody] Invoice updated, AppDbContext db) =>
{
    var inv = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
    if (inv is null) return Results.NotFound();
    inv.ClientName = updated.ClientName;
    inv.ClientEmail = updated.ClientEmail;
    inv.ClientAddress = updated.ClientAddress;
    inv.IssueDate = updated.IssueDate;
    inv.DueDate = updated.DueDate;
    inv.Status = updated.Status;
    inv.Currency = updated.Currency;
    inv.Notes = updated.Notes;
    // Replace line items
    db.InvoiceItems.RemoveRange(inv.Items);
    inv.Items = updated.Items.Select(i => new InvoiceItem {
        InvoiceId = id, Description = i.Description,
        Quantity = i.Quantity, UnitPrice = i.UnitPrice,
        Total = i.Quantity * i.UnitPrice
    }).ToList();
    inv.SubTotal = inv.Items.Sum(i => i.Total);
    inv.Total = inv.SubTotal;
    await db.SaveChangesAsync();
    return Results.Ok(inv);
});

app.MapPatch("/api/invoices/{id}/status", async (int id, [FromBody] StatusUpdate s, AppDbContext db) =>
{
    var inv = await db.Invoices.FindAsync(id);
    if (inv is null) return Results.NotFound();
    inv.Status = s.Status;
    await db.SaveChangesAsync();
    return Results.Ok(inv);
});

app.MapDelete("/api/invoices/{id}", async (int id, AppDbContext db) =>
{
    var inv = await db.Invoices.FindAsync(id);
    if (inv is null) return Results.NotFound();
    db.Invoices.Remove(inv);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ACCOUNTING CRM — QUOTES API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/quotes", async (AppDbContext db) =>
    Results.Ok(await db.Quotes.Include(q => q.Items)
        .OrderByDescending(q => q.CreatedAt).ToListAsync()));

app.MapGet("/api/quotes/{id}", async (int id, AppDbContext db) =>
    await db.Quotes.Include(q => q.Items).FirstOrDefaultAsync(q => q.Id == id)
        is Quote qt ? Results.Ok(qt) : Results.NotFound());

app.MapPost("/api/quotes", async ([FromBody] Quote quote, AppDbContext db) =>
{
    quote.Id = 0;
    quote.CreatedAt = DateTime.UtcNow;
    var count = await db.Quotes.CountAsync() + 1;
    quote.QuoteNumber = $"QT-{DateTime.UtcNow.Year}-{count:D4}";
    foreach (var item in quote.Items) { item.Id = 0; item.Total = item.Quantity * item.UnitPrice; }
    quote.SubTotal = quote.Items.Sum(i => i.Total);
    quote.Total = quote.SubTotal;
    db.Quotes.Add(quote);
    await db.SaveChangesAsync();
    return Results.Created($"/api/quotes/{quote.Id}", quote);
});

app.MapPut("/api/quotes/{id}", async (int id, [FromBody] Quote updated, AppDbContext db) =>
{
    var qt = await db.Quotes.Include(q => q.Items).FirstOrDefaultAsync(q => q.Id == id);
    if (qt is null) return Results.NotFound();
    qt.ClientName = updated.ClientName;
    qt.ClientEmail = updated.ClientEmail;
    qt.ClientAddress = updated.ClientAddress;
    qt.IssueDate = updated.IssueDate;
    qt.ExpiryDate = updated.ExpiryDate;
    qt.Status = updated.Status;
    qt.Currency = updated.Currency;
    qt.Notes = updated.Notes;
    db.QuoteItems.RemoveRange(qt.Items);
    qt.Items = updated.Items.Select(i => new QuoteItem {
        QuoteId = id, Description = i.Description,
        Quantity = i.Quantity, UnitPrice = i.UnitPrice,
        Total = i.Quantity * i.UnitPrice
    }).ToList();
    qt.SubTotal = qt.Items.Sum(i => i.Total);
    qt.Total = qt.SubTotal;
    await db.SaveChangesAsync();
    return Results.Ok(qt);
});

// Convert quote → invoice
app.MapPost("/api/quotes/{id}/convert", async (int id, AppDbContext db) =>
{
    var qt = await db.Quotes.Include(q => q.Items).FirstOrDefaultAsync(q => q.Id == id);
    if (qt is null) return Results.NotFound();
    if (qt.ConvertedInvoiceId.HasValue)
        return Results.BadRequest(new { message = "Quote already converted to an invoice." });

    var count = await db.Invoices.CountAsync() + 1;
    var invoice = new Invoice
    {
        InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{count:D4}",
        ClientId = qt.ClientId, ClientName = qt.ClientName,
        ClientEmail = qt.ClientEmail, ClientAddress = qt.ClientAddress,
        IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Status = "Draft", Currency = qt.Currency,
        SubTotal = qt.SubTotal, Total = qt.Total,
        Notes = $"Converted from {qt.QuoteNumber}. {qt.Notes}",
        CreatedAt = DateTime.UtcNow,
        Items = qt.Items.Select(qi => new InvoiceItem {
            Description = qi.Description, Quantity = qi.Quantity,
            UnitPrice = qi.UnitPrice, Total = qi.Total
        }).ToList()
    };
    db.Invoices.Add(invoice);
    qt.Status = "Converted";
    qt.ConvertedInvoiceId = invoice.Id;
    await db.SaveChangesAsync();
    return Results.Ok(invoice);
});

app.MapDelete("/api/quotes/{id}", async (int id, AppDbContext db) =>
{
    var qt = await db.Quotes.FindAsync(id);
    if (qt is null) return Results.NotFound();
    db.Quotes.Remove(qt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ACCOUNTING CRM — EXPENSES API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/expenses", async (AppDbContext db) =>
    Results.Ok(await db.Expenses.OrderByDescending(e => e.Date).ToListAsync()));

app.MapGet("/api/expenses/{id}", async (int id, AppDbContext db) =>
    await db.Expenses.FindAsync(id) is Expense e ? Results.Ok(e) : Results.NotFound());

app.MapPost("/api/expenses", async ([FromBody] Expense expense, AppDbContext db) =>
{
    expense.Id = 0;
    expense.CreatedAt = DateTime.UtcNow;
    db.Expenses.Add(expense);
    await db.SaveChangesAsync();
    return Results.Created($"/api/expenses/{expense.Id}", expense);
});

app.MapPut("/api/expenses/{id}", async (int id, [FromBody] Expense updated, AppDbContext db) =>
{
    var e = await db.Expenses.FindAsync(id);
    if (e is null) return Results.NotFound();
    e.Description = updated.Description;
    e.Category = updated.Category;
    e.Vendor = updated.Vendor;
    e.Amount = updated.Amount;
    e.Currency = updated.Currency;
    e.Date = updated.Date;
    e.Team = updated.Team;
    e.ReceiptNote = updated.ReceiptNote;
    e.Notes = updated.Notes;
    await db.SaveChangesAsync();
    return Results.Ok(e);
});

app.MapDelete("/api/expenses/{id}", async (int id, AppDbContext db) =>
{
    var e = await db.Expenses.FindAsync(id);
    if (e is null) return Results.NotFound();
    db.Expenses.Remove(e);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ACCOUNTING CRM — CHART OF ACCOUNTS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/accounts", async (AppDbContext db) =>
    Results.Ok(await db.ChartOfAccounts.OrderBy(a => a.Code).ToListAsync()));

app.MapPost("/api/accounts", async ([FromBody] ChartOfAccount acc, AppDbContext db) =>
{
    acc.Id = 0;
    db.ChartOfAccounts.Add(acc);
    await db.SaveChangesAsync();
    return Results.Created($"/api/accounts/{acc.Id}", acc);
});

app.MapPut("/api/accounts/{id}", async (int id, [FromBody] ChartOfAccount updated, AppDbContext db) =>
{
    var a = await db.ChartOfAccounts.FindAsync(id);
    if (a is null) return Results.NotFound();
    a.Code = updated.Code; a.Name = updated.Name;
    a.Type = updated.Type; a.Description = updated.Description;
    a.IsActive = updated.IsActive;
    await db.SaveChangesAsync();
    return Results.Ok(a);
});

app.MapDelete("/api/accounts/{id}", async (int id, AppDbContext db) =>
{
    var a = await db.ChartOfAccounts.FindAsync(id);
    if (a is null) return Results.NotFound();
    db.ChartOfAccounts.Remove(a);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ═══════════════════════════════════════════════════════════════════════════════
//  ACCOUNTING CRM — REPORTS / STATS API
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/accounting/stats", async (AppDbContext db) =>
{
    var now = DateOnly.FromDateTime(DateTime.UtcNow);

    // Invoice stats
    var totalInvoiced = await db.Invoices.SumAsync(i => i.Total);
    var totalPaid = await db.Invoices.Where(i => i.Status == "Paid").SumAsync(i => i.Total);
    var totalOutstanding = await db.Invoices.Where(i => i.Status == "Sent").SumAsync(i => i.Total);
    var overdueInvoices = await db.Invoices
        .Where(i => i.Status != "Paid" && i.Status != "Cancelled" && i.DueDate < now)
        .Select(i => new { i.Id, i.InvoiceNumber, i.ClientName, i.Total, i.Currency, i.DueDate,
            DaysOverdue = now.DayNumber - i.DueDate.DayNumber })
        .OrderByDescending(i => i.DaysOverdue).ToListAsync();

    // Expense breakdown by category
    var expenseByCategory = await db.Expenses
        .GroupBy(e => e.Category)
        .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount) })
        .OrderByDescending(g => g.Total).ToListAsync();

    // Monthly revenue from paid invoices (last 6 months)
    var monthlyRevenue = await db.Invoices
        .Where(i => i.Status == "Paid" && i.IssueDate >= now.AddMonths(-6))
        .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(i => i.Total) })
        .OrderBy(g => g.Year).ThenBy(g => g.Month).ToListAsync();

    // Revenue by client (top 5)
    var revenueByClient = await db.Invoices
        .Where(i => i.Status == "Paid")
        .GroupBy(i => i.ClientName)
        .Select(g => new { Client = g.Key, Total = g.Sum(i => i.Total) })
        .OrderByDescending(g => g.Total).Take(5).ToListAsync();

    // P&L
    var totalExpenses = await db.Expenses.SumAsync(e => e.Amount);
    var netProfit = totalPaid - totalExpenses;

    return Results.Ok(new
    {
        totalInvoiced, totalPaid, totalOutstanding,
        totalExpenses, netProfit,
        overdueCount = overdueInvoices.Count,
        overdueInvoices,
        expenseByCategory,
        monthlyRevenue,
        revenueByClient,
        quotesTotal = await db.Quotes.CountAsync(),
        quotesAccepted = await db.Quotes.CountAsync(q => q.Status == "Accepted"),
        quotesConverted = await db.Quotes.CountAsync(q => q.Status == "Converted")
    });
});

// ═══════════════════════════════════════════════════════════════════════════════
//  COURSE MATERIALS API  (Teachers upload videos/PDFs)
// ═══════════════════════════════════════════════════════════════════════════════
app.MapGet("/api/course-materials", async (AppDbContext db, string? courseId, string? type) =>
{
    var q = db.CourseMaterials.AsQueryable();
    if (!string.IsNullOrEmpty(courseId) && int.TryParse(courseId, out var cid))
        q = q.Where(m => m.CourseId == cid);
    if (!string.IsNullOrEmpty(type))
        q = q.Where(m => m.Type == type);
    return Results.Ok(await q.OrderByDescending(m => m.UploadedAt).ToListAsync());
});

app.MapGet("/api/course-materials/{id}", async (int id, AppDbContext db) =>
    await db.CourseMaterials.FindAsync(id) is CourseMaterial m ? Results.Ok(m) : Results.NotFound());

app.MapPut("/api/course-materials/{id}", async (int id, [FromBody] CourseMaterial updated, AppDbContext db) =>
{
    var mat = await db.CourseMaterials.FindAsync(id);
    if (mat is null) return Results.NotFound();
    mat.Title       = updated.Title;
    mat.Description = updated.Description;
    mat.IsPublic    = updated.IsPublic;
    mat.CourseName  = updated.CourseName;
    mat.CourseId    = updated.CourseId;
    await db.SaveChangesAsync();
    return Results.Ok(mat);
});

app.MapDelete("/api/course-materials/{id}", async (int id, AppDbContext db) =>
{
    var mat = await db.CourseMaterials.FindAsync(id);
    if (mat is null) return Results.NotFound();
    // Also delete the physical file
    var physPath = Path.Combine(uploadFolder, mat.Filename);
    if (File.Exists(physPath)) File.Delete(physPath);
    db.CourseMaterials.Remove(mat);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ─── File Upload ─────────────────────────────────────────────────────────────
// POST /api/upload  (multipart/form-data)
// Fields: file, title, courseId, courseName, duration (video seconds), uploadedBy
var AllowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "pdf", "png", "jpg", "jpeg", "mp4", "webm" };

bool CheckStorage(long requiredKb, string path)
{
    try {
        var drive = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
        return drive.AvailableFreeSpace > requiredKb * 1024;
    } catch { return true; } // if we can't check, allow
}

app.MapPost("/api/upload", async (HttpRequest request, AppDbContext db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Multipart form required" });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file provided" });

    var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
    if (!AllowedExts.Contains(ext))
        return Results.BadRequest(new { error = $"File type .{ext} not allowed. Allowed: pdf, png, jpg, mp4, webm" });

    // Determine type
    var fileType = ext is "mp4" or "webm" ? "Video" : ext is "pdf" ? "PDF" : "Image";

    // Video-specific validation
    int durationSec = 0;
    if (fileType == "Video")
    {
        if (!int.TryParse(form["duration"], out durationSec) || durationSec <= 0)
            return Results.BadRequest(new { error = "Duration (seconds) is required for video uploads" });
        if (durationSec > 3600)
            return Results.BadRequest(new { error = "Video exceeds 1 hour limit (3600 seconds)" });
    }

    // Storage check: estimate file size from upload length
    var requiredKb = file.Length / 1024;
    if (!CheckStorage(requiredKb, uploadFolder))
        return Results.BadRequest(new { error = "Insufficient disk space", requiredMb = requiredKb / 1024 });

    // Save file with unique name to avoid conflicts
    var safeBase   = Path.GetFileNameWithoutExtension(file.FileName)
        .Replace(" ", "_").Replace("..", "");
    var uniqueName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeBase}.{ext}";
    var savePath   = Path.Combine(uploadFolder, uniqueName);
    await using (var stream = File.Create(savePath))
        await file.CopyToAsync(stream);

    // Parse other fields
    int? courseId = int.TryParse(form["courseId"], out var cid) ? cid : null;
    var title      = form["title"].ToString() is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(file.FileName);
    var courseName = form["courseName"].ToString();
    var uploadedBy = form["uploadedBy"].ToString();
    var description = form["description"].ToString();

    var material = new CourseMaterial
    {
        CourseId        = courseId,
        CourseName      = courseName,
        Title           = title,
        Type            = fileType,
        Filename        = uniqueName,
        FileSizeKb      = file.Length / 1024,
        DurationSeconds = durationSec,
        UploadedBy      = uploadedBy,
        UploadedAt      = DateTime.UtcNow,
        Description     = description,
        IsPublic        = false
    };
    db.CourseMaterials.Add(material);
    await db.SaveChangesAsync();

    return Results.Created($"/api/course-materials/{material.Id}", new
    {
        material,
        url = $"/uploads/{uniqueName}"
    });
});

// ─── Public Enrollment + Payment ─────────────────────────────────────────────
// POST /api/enroll
// Body: { name, email, phone, courseName, paymentMethod }
// paymentMethod: "manual" | "card" (card = immediate payment simulation)
app.MapPost("/api/enroll", async ([FromBody] EnrollmentSubmission req, AppDbContext db) =>
{
    var course = await db.Courses
        .FirstOrDefaultAsync(c => c.Title.ToLower().Contains((req.CourseName ?? "").ToLower()));

    if (course is null)
    {
        // Fallback for custom or legacy course names
        var fallbackStudent = new Student
        {
            Name         = req.Name,
            Email        = req.Email,
            Phone        = req.Phone,
            Course       = req.CourseName,
            Division     = "TBD",
            EnrolledDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status       = "Pending",
            CreatedAt    = DateTime.UtcNow
        };
        db.Students.Add(fallbackStudent);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            success     = true,
            studentId   = fallbackStudent.Id,
            studentName = fallbackStudent.Name,
            course      = req.CourseName,
            coursePrice = 0.0,
            message     = $"System: Registration successful! Welcome to the '{req.CourseName}' course, {req.Name}. Check your email ({req.Email}) for access links and schedules."
        });
    }

    // Save student
    var student = new Student
    {
        Name         = req.Name,
        Email        = req.Email,
        Phone        = req.Phone,
        Course       = course.Title,
        Division     = course.Division,
        EnrolledDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status       = "Active",
        Notes        = $"Enrolled via {req.PaymentMethod ?? "manual"} payment"
    };
    db.Students.Add(student);
    await db.SaveChangesAsync();

    // Auto-create invoice if paid course
    Invoice? invoice = null;
    if (course.Price > 0)
    {
        var count = await db.Invoices.CountAsync() + 1;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isPaid = req.PaymentMethod?.ToLower() == "card";

        invoice = new Invoice
        {
            InvoiceNumber = $"INV-{today.Year}-{count:D4}",
            ClientName    = student.Name,
            ClientEmail   = student.Email ?? "",
            ClientAddress = "",
            IssueDate     = today,
            DueDate       = today.AddDays(14),
            Status        = isPaid ? "Paid" : "Draft",
            Currency      = "EGP",
            SubTotal      = course.Price,
            Total         = course.Price,
            Notes         = $"Enrollment: {course.Title} | Payment: {req.PaymentMethod ?? "manual"}",
            Items = new List<InvoiceItem> {
                new InvoiceItem {
                    Description = course.Title,
                    Quantity    = 1,
                    UnitPrice   = course.Price,
                    Total       = course.Price
                }
            }
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
    }

    return Results.Ok(new
    {
        success       = true,
        studentId     = student.Id,
        studentName   = student.Name,
        course        = course.Title,
        coursePrice   = course.Price,
        invoiceId     = invoice?.Id,
        invoiceNumber = invoice?.InvoiceNumber,
        invoiceStatus = invoice?.Status,
        message       = invoice != null
            ? $"Enrolled successfully. Invoice {invoice.InvoiceNumber} created ({invoice.Status})."
            : "Enrolled successfully. No invoice (free course)."
    });
});

app.MapGet("/api/students/by-course/{courseName}", async (string courseName, AppDbContext db) =>
    Results.Ok(await db.Students
        .Where(s => s.Course.ToLower().Contains((courseName ?? "").ToLower()))
        .OrderByDescending(s => s.CreatedAt)
        .ToListAsync()));

app.Run();


// ─── Seed data ────────────────────────────────────────────────────────────────
async Task SeedData(AppDbContext db)
{
    if (!await db.Courses.AnyAsync())
    {
        db.Courses.AddRange(
            new Course { Title = "Practical Penetration Testing & Ethical Hacking", Division = "Zerobyte", Duration = "8 Weeks", Level = "Intermediate-Advanced", Price = 500, MaxStudents = 20 },
            new Course { Title = "Fullstack Software Engineering & CRM Development", Division = "Nova",     Duration = "12 Weeks", Level = "Beginner-Professional",   Price = 700, MaxStudents = 25 },
            new Course { Title = "Enterprise Linux & Network Administration",         Division = "IT",       Duration = "6 Weeks",  Level = "Beginner-Mid",            Price = 400, MaxStudents = 20 }
        );
    }

    if (!await db.Services.AnyAsync())
    {
        db.Services.AddRange(
            new Service { Name = "Penetration Testing",         Team = "Zerobyte", Category = "Security",        Description = "Full black-box and white-box pentesting",  Price = 1500, IsActive = true },
            new Service { Name = "Mobile & Web App Development",Team = "Nova",     Category = "Development",     Description = "End-to-end app development",               Price = 3000, IsActive = true },
            new Service { Name = "Network Security Audit",      Team = "Zerobyte", Category = "Security",        Description = "Firewall config and IDS setup",            Price = 800,  IsActive = true },
            new Service { Name = "System Administration",       Team = "IT",       Category = "Infrastructure",  Description = "Linux/Windows server configuration",       Price = 600,  IsActive = true },
            new Service { Name = "SEO & AEO Optimization",     Team = "Nova",     Category = "Marketing",       Description = "Search & AI engine optimization",          Price = 400,  IsActive = true },
            new Service { Name = "CRM System Engineering",      Team = "Nova",     Category = "Development",     Description = "Custom CRM portal development",            Price = 2500, IsActive = true }
        );
    }

    if (!await db.Admins.AnyAsync())
    {
        db.Admins.AddRange(
            new Admin { Name = "Dev-Core Admin", Email = "devcore.communicate@gmail.com", Role = "Root", Team = "All Teams", IsActive = true },
            new Admin { Name = "Finance Admin User", Email = "financeadmin@dev-core.site", Role = "Finance Admin", Team = "All Teams", IsActive = true },
            new Admin { Name = "Finance User", Email = "finance@dev-core.site", Role = "Finance", Team = "All Teams", IsActive = true },
            new Admin { Name = "Web Admin User", Email = "webadmin@dev-core.site", Role = "Web Admin", Team = "IT", IsActive = true },
            new Admin { Name = "Email Admin User", Email = "emailadmin@dev-core.site", Role = "Email Admin", Team = "Nova", IsActive = true },
            new Admin { Name = "General Admin User", Email = "admin@dev-core.site", Role = "Admin", Team = "All Teams", IsActive = true },
            // Teacher accounts
            new Admin { Name = "Zerobyte Teacher", Email = "zerobyte.teacher@dev-core.site", Role = "Teacher", Team = "Zerobyte", IsActive = true },
            new Admin { Name = "Nova Teacher", Email = "nova.teacher@dev-core.site", Role = "Teacher", Team = "Nova", IsActive = true },
            new Admin { Name = "IT Teacher", Email = "it.teacher@dev-core.site", Role = "Teacher", Team = "IT", IsActive = true }
        );

    }

    if (!await db.Transactions.AnyAsync())
    {
        db.Transactions.AddRange(
            new Transaction { Description = "CRM Project Deposit", Amount = 15000, Currency = "EGP", Type = "Income", Category = "Development", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)), Notes = "Nova client deposit" },
            new Transaction { Description = "Server Hosting Cost - Linode", Amount = 1200, Currency = "EGP", Type = "Expense", Category = "Hosting", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), Notes = "Monthly IT servers fee" },
            new Transaction { Description = "Ethical Hacking Course Enrollment", Amount = 5000, Currency = "EGP", Type = "Income", Category = "Education", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), Notes = "Zerobyte course purchase" },
            new Transaction { Description = "Office Rent", Amount = 8000, Currency = "EGP", Type = "Expense", Category = "Office", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Notes = "Cairo HQ office lease" }
        );
    }

    if (!await db.RecurringPayments.AnyAsync())
    {
        db.RecurringPayments.AddRange(
            new RecurringPayment { Name = "AWS Infrastructure VPS", Amount = 2500, Currency = "EGP", Frequency = "Monthly", NextDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)), Status = "Active", Notes = "Production client hosting" },
            new RecurringPayment { Name = "Instabug Subscription", Amount = 800, Currency = "EGP", Frequency = "Monthly", NextDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), Status = "Active", Notes = "Bug tracker utility" }
        );
    }

    if (!await db.Staff.AnyAsync())
    {
        db.Staff.AddRange(
            new Staff { Name = "Ahmed Hassan", Email = "ahmed@dev-core.site", Phone = "+20 100 111 2222", Role = "Security Researcher / Pentester", Department = "Zerobyte", Salary = 12000, HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)), Status = "Active", Notes = "Active pentester" },
            new Staff { Name = "Sara Mohamed", Email = "sara@dev-core.site", Phone = "+20 100 333 4444", Role = "Fullstack Software Engineer", Department = "Nova", Salary = 15000, HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), Status = "Active", Notes = "React expert" },
            new Staff { Name = "Youssef Ali", Email = "youssef@dev-core.site", Phone = "+20 100 555 6666", Role = "Systems / Network Engineer", Department = "IT", Salary = 10000, HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-9)), Status = "Active", Notes = "Linux guru" }
        );
    }

    if (!await db.WebServers.AnyAsync())
    {
        db.WebServers.AddRange(
            new WebServer { Name = "production-web-01", IpAddress = "172.105.110.15", SshPort = 22, SshUser = "root", SshPassword = "SecurePass123!", Os = "Ubuntu 22.04 LTS", Provider = "Linode", MonthlyCost = 20, Status = "Online", Notes = "Main application hosting" },
            new WebServer { Name = "staging-client-db", IpAddress = "45.79.201.55", SshPort = 22, SshUser = "admin", SshPassword = "StagingSecret321!", Os = "Debian 12", Provider = "DigitalOcean", MonthlyCost = 15, Status = "Online", Notes = "Clients test database" }
        );
    }

    if (!await db.EmailAccounts.AnyAsync())
    {
        db.EmailAccounts.AddRange(
            new EmailAccount { Email = "contact@dev-core.site", Password = "SMTPPassword123!", SmtpHost = "smtp.mailgun.org", SmtpPort = 587, ImapHost = "imap.mailgun.org", ImapPort = 993, Department = "All Teams", OwnerName = "Dev-Core Communications", Status = "Active", Notes = "Public website mail inbox" },
            new EmailAccount { Email = "billing@dev-core.site", Password = "BillingPass456!", SmtpHost = "smtp.google.com", SmtpPort = 587, ImapHost = "imap.google.com", ImapPort = 993, Department = "Finance", OwnerName = "Finance Division", Status = "Active", Notes = "Client payments invoice email" }
        );
    }

    await db.SaveChangesAsync();

    // ── Accounting CRM seed data ─────────────────────────────
    if (!await db.Invoices.AnyAsync())
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Invoices.AddRange(
            new Invoice {
                InvoiceNumber = "INV-2025-0001", ClientName = "TechStart Solutions",
                ClientEmail = "billing@techstart.io", ClientAddress = "Cairo, Egypt",
                IssueDate = today.AddDays(-30), DueDate = today.AddDays(-5),
                Status = "Paid", Currency = "EGP", SubTotal = 15000, Total = 15000,
                Notes = "CRM project phase 1 delivery",
                Items = new List<InvoiceItem> {
                    new() { Description = "CRM System Development", Quantity = 1, UnitPrice = 12000, Total = 12000 },
                    new() { Description = "UI/UX Design & Delivery", Quantity = 1, UnitPrice = 3000, Total = 3000 }
                }
            },
            new Invoice {
                InvoiceNumber = "INV-2025-0002", ClientName = "SecureBank Ltd.",
                ClientEmail = "finance@securebank.com", ClientAddress = "Alexandria, Egypt",
                IssueDate = today.AddDays(-10), DueDate = today.AddDays(20),
                Status = "Sent", Currency = "EGP", SubTotal = 8000, Total = 8000,
                Notes = "Penetration testing report and remediation",
                Items = new List<InvoiceItem> {
                    new() { Description = "Black-box Penetration Test", Quantity = 1, UnitPrice = 6500, Total = 6500 },
                    new() { Description = "Security Report & Documentation", Quantity = 1, UnitPrice = 1500, Total = 1500 }
                }
            },
            new Invoice {
                InvoiceNumber = "INV-2025-0003", ClientName = "Nova Client Corp",
                ClientEmail = "accounts@novacorp.eg", ClientAddress = "Giza, Egypt",
                IssueDate = today.AddDays(-45), DueDate = today.AddDays(-10),
                Status = "Overdue", Currency = "EGP", SubTotal = 5500, Total = 5500,
                Notes = "Mobile app maintenance contract",
                Items = new List<InvoiceItem> {
                    new() { Description = "Monthly App Maintenance", Quantity = 2, UnitPrice = 2000, Total = 4000 },
                    new() { Description = "Bug Fixes & Updates", Quantity = 1, UnitPrice = 1500, Total = 1500 }
                }
            }
        );
    }

    if (!await db.Quotes.AnyAsync())
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Quotes.AddRange(
            new Quote {
                QuoteNumber = "QT-2025-0001", ClientName = "Digital Media Group",
                ClientEmail = "procurement@dmg.eg", ClientAddress = "Cairo, Egypt",
                IssueDate = today.AddDays(-5), ExpiryDate = today.AddDays(25),
                Status = "Sent", Currency = "EGP", SubTotal = 22000, Total = 22000,
                Notes = "Fullstack web platform + admin panel",
                Items = new List<QuoteItem> {
                    new() { Description = "Web Platform Development", Quantity = 1, UnitPrice = 18000, Total = 18000 },
                    new() { Description = "Admin Panel & CRM", Quantity = 1, UnitPrice = 4000, Total = 4000 }
                }
            },
            new Quote {
                QuoteNumber = "QT-2025-0002", ClientName = "Alpha Infrastructure",
                ClientEmail = "cto@alpha-inf.com", ClientAddress = "Heliopolis, Cairo",
                IssueDate = today.AddDays(-15), ExpiryDate = today.AddDays(15),
                Status = "Accepted", Currency = "USD", SubTotal = 3500, Total = 3500,
                Notes = "Network audit and security hardening",
                Items = new List<QuoteItem> {
                    new() { Description = "Network Security Audit", Quantity = 1, UnitPrice = 2500, Total = 2500 },
                    new() { Description = "Firewall Configuration", Quantity = 1, UnitPrice = 1000, Total = 1000 }
                }
            }
        );
    }

    if (!await db.Expenses.AnyAsync())
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Expenses.AddRange(
            new Expense { Description = "Monthly Office Rent", Category = "Office", Vendor = "Cairo HQ Landlord", Amount = 8000, Currency = "EGP", Date = today.AddDays(-1), Team = "All Teams", Notes = "Cairo HQ monthly lease" },
            new Expense { Description = "Linode VPS Hosting", Category = "Hosting", Vendor = "Akamai/Linode", Amount = 1200, Currency = "EGP", Date = today.AddDays(-5), Team = "IT", Notes = "Production server monthly" },
            new Expense { Description = "Figma Pro Subscription", Category = "Software", Vendor = "Figma Inc.", Amount = 450, Currency = "EGP", Date = today.AddDays(-3), Team = "Nova", Notes = "Design tool" },
            new Expense { Description = "Staff Training Materials", Category = "Other", Vendor = "Udemy Business", Amount = 900, Currency = "EGP", Date = today.AddDays(-10), Team = "All Teams", Notes = "Online courses" },
            new Expense { Description = "Google Workspace", Category = "Software", Vendor = "Google", Amount = 600, Currency = "EGP", Date = today.AddDays(-7), Team = "All Teams", Notes = "Email and collaboration" }
        );
    }

    if (!await db.ChartOfAccounts.AnyAsync())
    {
        db.ChartOfAccounts.AddRange(
            new ChartOfAccount { Code = "1000", Name = "Cash & Bank", Type = "Asset", Description = "Company bank accounts and cash on hand", IsActive = true },
            new ChartOfAccount { Code = "1100", Name = "Accounts Receivable", Type = "Asset", Description = "Money owed by clients for invoiced work", IsActive = true },
            new ChartOfAccount { Code = "2000", Name = "Accounts Payable", Type = "Liability", Description = "Amounts owed to vendors and suppliers", IsActive = true },
            new ChartOfAccount { Code = "3000", Name = "Owner Equity", Type = "Equity", Description = "Owner's capital and retained earnings", IsActive = true },
            new ChartOfAccount { Code = "4000", Name = "Service Revenue", Type = "Revenue", Description = "Income from delivered services", IsActive = true },
            new ChartOfAccount { Code = "4100", Name = "Course Revenue", Type = "Revenue", Description = "Income from training courses", IsActive = true },
            new ChartOfAccount { Code = "5000", Name = "Office Expenses", Type = "Expense", Description = "Rent, utilities, office supplies", IsActive = true },
            new ChartOfAccount { Code = "5100", Name = "Technology Expenses", Type = "Expense", Description = "Software, hosting, subscriptions", IsActive = true },
            new ChartOfAccount { Code = "5200", Name = "Salaries & Payroll", Type = "Expense", Description = "Staff salaries and compensation", IsActive = true },
            new ChartOfAccount { Code = "5300", Name = "Marketing Expenses", Type = "Expense", Description = "Ads, SEO, content production", IsActive = true }
        );
    }

    await db.SaveChangesAsync();

    // ── Course Materials seed (sample entries, no real files) ────────────
    if (!await db.CourseMaterials.AnyAsync())
    {
        var zbCourse = await db.Courses.FirstOrDefaultAsync(c => c.Division == "Zerobyte");
        var nvCourse = await db.Courses.FirstOrDefaultAsync(c => c.Division == "Nova");
        db.CourseMaterials.AddRange(
            new CourseMaterial {
                CourseId = zbCourse?.Id, CourseName = zbCourse?.Title ?? "Zerobyte Course",
                Title = "Introduction to Kali Linux", Type = "PDF",
                Filename = "intro_kali_linux.pdf", FileSizeKb = 0,
                UploadedBy = "zerobyte.teacher@dev-core.site", IsPublic = false,
                Description = "Overview and setup guide for Kali Linux"
            },
            new CourseMaterial {
                CourseId = zbCourse?.Id, CourseName = zbCourse?.Title ?? "Zerobyte Course",
                Title = "Lecture 1: Reconnaissance Techniques", Type = "Video",
                Filename = "lecture1_recon.mp4", FileSizeKb = 0, DurationSeconds = 3200,
                UploadedBy = "zerobyte.teacher@dev-core.site", IsPublic = false,
                Description = "Passive and active reconnaissance fundamentals"
            },
            new CourseMaterial {
                CourseId = nvCourse?.Id, CourseName = nvCourse?.Title ?? "Nova Course",
                Title = "React & Node.js Project Starter", Type = "PDF",
                Filename = "react_node_starter.pdf", FileSizeKb = 0,
                UploadedBy = "nova.teacher@dev-core.site", IsPublic = false,
                Description = "Boilerplate setup and project structure guide"
            }
        );
        await db.SaveChangesAsync();
    }
}

// ─── Record types ─────────────────────────────────────────────────────────────
record ContactSubmission(string Name, string Email, string Team, string Service, string Message);
record EnrollmentSubmission(string Name, string Email, string CourseName, string Phone, string? PaymentMethod = "manual");
record StatusUpdate(string Status);


