import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# sticky "Contact us" button + sheet form, posted to the API's contact endpoint
rep("'<div class=\"progress\" id=\"prog\"></div><a id=\"top\"></a>\\n'",
    "'<div class=\"progress\" id=\"prog\"></div><a id=\"top\"></a>\\n'\n"
    " '<button class=\"cbtn\" id=\"cbtn\" aria-label=\"Contact us\"><svg width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\"><path d=\"M4 4h16v12H7l-3 3z\"/></svg><span>Contact us</span></button>'\n"
    " '<div class=\"csheet\" id=\"csheet\"><form class=\"cform\" id=\"cform\" novalidate><div class=\"chead\"><div><span class=\"eyebrow\">Contact us</span><h3>Tell us what you need</h3><p>We answer by email or phone, usually the same day.</p></div><button type=\"button\" class=\"cx\" id=\"cx\" aria-label=\"Close\">×</button></div>'\n"
    " '<label>Email<input name=\"email\" type=\"email\" required placeholder=\"you@example.com\" autocomplete=\"email\"></label>'\n"
    " '<label>Phone<input name=\"phone\" type=\"tel\" required placeholder=\"+968 9xxx xxxx\" autocomplete=\"tel\"></label>'\n"
    " '<label>Title<input name=\"title\" type=\"text\" required maxlength=\"120\" placeholder=\"What is it about?\"></label>'\n"
    " '<label>Message<textarea name=\"text\" required minlength=\"5\" maxlength=\"3000\" rows=\"5\" placeholder=\"Write your message…\"></textarea></label>'\n"
    " '<div class=\"cfoot\"><span class=\"cmsg\" id=\"cmsg\"></span><button type=\"submit\" class=\"btn p\" id=\"csend\">Send message</button></div><p class=\"cnote\">We take up to 100 messages a day while the form is new. If it is full, email <a href=\"mailto:info@orderorange.com\">info@orderorange.com</a>.</p></form></div>'")
rep(".tsearch{cursor:text}", """.tsearch{cursor:text}
.cbtn{position:fixed;right:18px;bottom:18px;z-index:15;display:inline-flex;align-items:center;gap:8px;border:0;cursor:pointer;background:var(--ink);color:#fff;font:800 13px Manrope,sans-serif;padding:12px 16px;border-radius:999px;box-shadow:0 14px 34px rgba(20,14,9,.28);transition:transform .15s,background .15s}
.cbtn:hover{transform:translateY(-2px);background:var(--accent)}
.csheet{position:fixed;inset:0;background:rgba(20,14,9,.5);backdrop-filter:blur(6px);z-index:25;display:none;align-items:flex-end;justify-content:center;padding:0}
.csheet.on{display:flex}
.cform{width:min(560px,100%);background:#fff;border-radius:24px 24px 0 0;padding:20px 20px 24px;display:grid;gap:12px;box-shadow:0 -20px 60px rgba(0,0,0,.25);animation:sheetUp .3s cubic-bezier(.16,1,.3,1) both;max-height:92vh;overflow:auto}
@keyframes sheetUp{from{transform:translateY(40px);opacity:0}to{transform:none;opacity:1}}
.chead{display:flex;align-items:flex-start;gap:12px}.chead h3{margin:6px 0 4px;font:800 20px Manrope,sans-serif;letter-spacing:-.4px}.chead p{margin:0;color:var(--muted);font-size:13.5px}
.cx{margin-inline-start:auto;width:34px;height:34px;border-radius:50%;border:1px solid var(--line);background:#fff;font:800 18px/32px Manrope,sans-serif;cursor:pointer;flex:none}
.cform label{display:grid;gap:5px;font:800 11px Manrope,sans-serif;letter-spacing:1.2px;text-transform:uppercase;color:var(--muted)}
.cform input,.cform textarea{font:600 15px Manrope,sans-serif;color:var(--ink);border:1.5px solid var(--line);border-radius:12px;padding:11px 12px;outline:0;background:var(--bg-2);transition:border-color .15s,box-shadow .15s;width:100%;box-sizing:border-box;resize:vertical}
.cform input:focus,.cform textarea:focus{border-color:var(--accent);box-shadow:0 0 0 4px rgba(255,90,0,.12);background:#fff}
.cform input.bad,.cform textarea.bad{border-color:#D93025}
.cfoot{display:flex;align-items:center;gap:12px;flex-wrap:wrap}.cfoot .btn{margin-inline-start:auto}
.cmsg{font:700 13px Manrope,sans-serif;color:var(--muted);min-height:1em}.cmsg.ok{color:#0F766E}.cmsg.err{color:#D93025}
.cnote{margin:0;font-size:12px;color:var(--muted)}.cnote a{color:var(--accent-deep);font-weight:700}
.cform.sent label,.cform.sent .cfoot .btn{display:none}
.cform .thanks{display:none;text-align:center;padding:26px 10px 10px;gap:8px}.cform.sent .thanks{display:grid}
.thanks b{font:800 22px Manrope,sans-serif}.thanks p{margin:0;color:var(--muted)}.thanks .btn{margin:10px auto 0}
@media (min-width:700px){.csheet{align-items:center;padding:20px}.cform{border-radius:24px}}""")
# thanks block inside the form + the script
rep("<p class=\"cnote\">We take up to 100 messages a day", "<div class=\"thanks\"><b>Thank you!</b><p>Your message is in. We reply by email or phone.</p><button type=\"button\" class=\"btn s\" id=\"cdone\">Close</button></div><p class=\"cnote\">We take up to 100 messages a day")
rep("var lb=document.createElement('div');",
    "var cb=document.getElementById('cbtn'),cs=document.getElementById('csheet'),cf=document.getElementById('cform'),cm=document.getElementById('cmsg'),csend=document.getElementById('csend');"
    "function copen(){cs.classList.add('on');document.body.style.overflow='hidden';setTimeout(function(){var f=cf.querySelector('input');if(f)f.focus()},50)}function cclose(){cs.classList.remove('on');document.body.style.overflow=''}"
    "cb.addEventListener('click',copen);document.getElementById('cx').addEventListener('click',cclose);document.getElementById('cdone').addEventListener('click',function(){cclose();cf.classList.remove('sent');cf.reset();cm.textContent=''});cs.addEventListener('click',function(e){if(e.target===cs)cclose()});"
    "cf.addEventListener('submit',function(e){e.preventDefault();var ok=true;[].slice.call(cf.querySelectorAll('input,textarea')).forEach(function(i){var v=i.checkValidity();i.classList.toggle('bad',!v);if(!v)ok=false});if(!ok){cm.className='cmsg err';cm.textContent='Please fill every field.';return}"
    "var d={email:cf.email.value.trim(),phone:cf.phone.value.trim(),title:cf.title.value.trim(),text:cf.text.value.trim(),source:'guide '+location.pathname};csend.disabled=true;cm.className='cmsg';cm.textContent='Sending…';"
    "fetch('https://api.orderorange.com/api/contact',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(d)}).then(function(r){return r.json().catch(function(){return {}}).then(function(j){return {s:r.status,j:j}})}).then(function(x){csend.disabled=false;if(x.s===200){cf.classList.add('sent');cm.textContent=''}else{cm.className='cmsg err';cm.textContent=(x.j&&x.j.message)||'Something went wrong. Please try again.'}}).catch(function(){csend.disabled=false;cm.className='cmsg err';cm.textContent='No connection. Please try again.'})});"
    "document.addEventListener('keydown',function(e){if(e.key==='Escape'&&cs.classList.contains('on'))cclose()}); "
    "var lb=document.createElement('div');")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
