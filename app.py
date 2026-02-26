from flask import Flask, render_template, request, redirect, url_for, session, jsonify
import json
import os

app = Flask(__name__)
app.secret_key = "devcore_secret"

DB_FILE = "data/db.json"

# Helper functions
def load_data():
    if not os.path.exists(DB_FILE):
        with open(DB_FILE, "w") as f:
            json.dump({"users": [], "students": [], "services": []}, f)
    with open(DB_FILE, "r") as f:
        return json.load(f)

def save_data(data):
    with open(DB_FILE, "w") as f:
        json.dump(data, f, indent=4)

# ------------------------
# Routes
# ------------------------

@app.route("/")
def index():
    return render_template("index.html")

@app.route("/services")
def services():
    return render_template("services.html")

@app.route("/pay")
def pay():
    if "user" not in session:
        return redirect(url_for("login"))
    return render_template("pay.html")

@app.route("/signup", methods=["GET","POST"])
def signup():
    if request.method == "POST":
        name = request.form["name"]
        email = request.form["email"]
        password = request.form["password"]

        # prevent duplicate users
        data = load_data()
        if any(u["email"] == email for u in data["users"]):
            return "Email already exists!"
        data["users"].append({"name": name, "email": email, "password": password})
        save_data(data)
        return redirect(url_for("login"))

    return render_template("signup.html")

@app.route("/login", methods=["GET","POST"])
def login():
    if request.method == "POST":
        email = request.form["email"]
        password = request.form["password"]

        # Admin check
        if email == "admin@devcore" and password == "admin123":
            session["user"] = {"name":"Admin","email":email,"is_admin":True}
            return redirect(url_for("admin"))

        # Regular user check
        data = load_data()
        user = next((u for u in data["users"] if u["email"] == email and u["password"] == password), None)
        if user:
            user["is_admin"] = False
            session["user"] = user
            return redirect(url_for("index"))

        return "Invalid credentials!"

    return render_template("login.html")

@app.route("/logout", methods=["GET","POST"])
def logout():
    session.pop("user", None)
    return redirect(url_for("login"))

# ------------------------
# Admin Panel
# ------------------------
@app.route("/admin")
def admin():
    if "user" not in session or not session["user"].get("is_admin"):
        return redirect(url_for("login"))
    return render_template("admin.html")

# ------------------------
# API for CRUD
# ------------------------

@app.route("/api/students", methods=["GET","POST","DELETE"])
def api_students():
    if "user" not in session or not session["user"].get("is_admin"):
        return jsonify({"error":"Unauthorized"}), 403

    data = load_data()
    if request.method == "GET":
        return jsonify(data["students"])
    if request.method == "POST":
        student = request.json
        data["students"].append(student)
    if request.method == "DELETE":
        email = request.json.get("email")
        data["students"] = [s for s in data["students"] if s["email"] != email]

    save_data(data)
    return jsonify({"status":"ok"})

@app.route("/api/services", methods=["GET","POST","DELETE"])
def api_services():
    if "user" not in session or not session["user"].get("is_admin"):
        return jsonify({"error":"Unauthorized"}), 403

    data = load_data()
    if request.method == "GET":
        return jsonify(data["services"])
    if request.method == "POST":
        service = request.json
        data["services"].append(service)
    if request.method == "DELETE":
        name = request.json.get("name")
        data["services"] = [s for s in data["services"] if s["name"] != name]

    save_data(data)
    return jsonify({"status":"ok"})

# ------------------------
# Run App
# ------------------------
if __name__ == "__main__":
    app.run(debug=True)
