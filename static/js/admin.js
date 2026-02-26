async function loadStudents() {
    const res = await fetch('/api/students');
    const students = await res.json();
    const list = document.getElementById('studentList');
    list.innerHTML = '';
    students.forEach(s=>{
        let li = document.createElement('li');
        li.textContent = `${s.name} - ${s.email}`;
        li.onclick = async ()=>{
            await fetch('/api/students', {
                method:'DELETE',
                headers:{'Content-Type':'application/json'},
                body: JSON.stringify({email:s.email})
            });
            loadStudents();
        };
        list.appendChild(li);
    });
}
async function addStudent(){
    const name = document.getElementById('studentName').value;
    const email = document.getElementById('studentEmail').value;
    await fetch('/api/students',{
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body: JSON.stringify({name,email})
    });
    document.getElementById('studentName').value='';
    document.getElementById('studentEmail').value='';
    loadStudents();
}

async function loadServices() {
    const res = await fetch('/api/services');
    const services = await res.json();
    const list = document.getElementById('serviceList');
    list.innerHTML = '';
    services.forEach(s=>{
        let li = document.createElement('li');
        li.textContent = s.name;
        li.onclick = async ()=>{
            await fetch('/api/services', {
                method:'DELETE',
                headers:{'Content-Type':'application/json'},
                body: JSON.stringify({name:s.name})
            });
            loadServices();
        };
        list.appendChild(li);
    });
}
async function addService(){
    const name = document.getElementById('serviceName').value;
    await fetch('/api/services',{
        method:'POST',
        headers:{'Content-Type':'application/json'},
        body: JSON.stringify({name})
    });
    document.getElementById('serviceName').value='';
    loadServices();
}

// Initial load
loadStudents();
loadServices();
