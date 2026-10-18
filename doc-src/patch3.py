import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:60], s.count(a)); s = s.replace(a, b)

# reading time helpers before sec()
rep("def sec(n, id, title, items):", """import re as _re
def _words(t): return len(_re.sub(r'<[^>]+>', ' ', t).split())
def _sec_words(items):
    return sum(_words(d) + sum(_words(a) for a in META[t][1]) + sum(_words(x) for x in HOW.get(t, [])) + _words(NOTES.get(t, '')) for _, t, d in items)
read_min = {i: max(1, round(_sec_words(items) / 200)) for i, _, items in parts}

def sec(n, id, title, items):""")
rep("Chapter {n:02d} · {len(items)} screens</span>", "Chapter {n:02d} · {len(items)} screens · {read_min[id]} min read</span>")
rep("<small>{len(items)} screens</small>", "<small>{len(items)} screens · {read_min[i]} min</small>")
rep("body = intro_html + ", "total_min = max(1, round((sum(_sec_words(items) for _, _, items in parts) + sum(_words(d) for _, d in INTRO) + sum(_words(q + a) for q, a in FAQ)) / 200))\nbody = intro_html + ")
rep("<div><b>3</b><span>Devices</span></div></div></div>", "<div><b>3</b><span>Devices</span></div><div><b>{total_min}</b><span>Min read</span></div></div></div>")

# animations: hero entrance stagger, floating stage, drifting glow
rep(".stage{position:relative;perspective:1600px}", """@keyframes rise{from{opacity:0;transform:translateY(22px)}to{opacity:1;transform:none}}
@keyframes stageIn{from{opacity:0;transform:translateX(50px) rotateY(-16deg) rotateX(4deg)}to{opacity:1;transform:rotateY(-9deg) rotateX(3deg)}}
@keyframes floaty{0%,100%{transform:rotateY(-9deg) rotateX(3deg) translateZ(60px) translateY(0)}50%{transform:rotateY(-9deg) rotateX(3deg) translateZ(60px) translateY(-10px)}}
@keyframes drift{0%,100%{background-position:85% -10%,-10% 110%}50%{background-position:80% 0%,-4% 100%}}
.hero .in > div:first-child > *{animation:rise .8s cubic-bezier(.16,1,.3,1) both}
.hero .in > div:first-child > :nth-child(2){animation-delay:.08s}.hero .in > div:first-child > :nth-child(3){animation-delay:.16s}.hero .in > div:first-child > :nth-child(4){animation-delay:.24s}.hero .in > div:first-child > :nth-child(5){animation-delay:.32s}
.hero{background-size:auto,auto,auto;animation:drift 14s ease-in-out infinite}
.stage{position:relative;perspective:1600px}
.stage .main{animation:stageIn 1s cubic-bezier(.16,1,.3,1) .2s both}
.stage .side{animation:rise .8s cubic-bezier(.16,1,.3,1) .55s both,floaty 6s ease-in-out 1.4s infinite}
.wm{transition:transform .6s ease}
section:hover .wm{transform:translateY(-6px)}
.chapters a em,.toc a em{transition:transform .2s}
.chapters a:hover em{transform:translateX(3px)}
.btn.p{position:relative;overflow:hidden}
.btn.p::after{content:"";position:absolute;inset:0;background:linear-gradient(110deg,transparent 30%,rgba(255,255,255,.35) 50%,transparent 70%);transform:translateX(-120%);transition:transform .6s}
.btn.p:hover::after{transform:translateX(120%)}""")
rep("@media (prefers-reduced-motion:reduce){.rv{opacity:1;transform:none;transition:none}.rv .shot{transform:none;transition:none}}",
    "@media (prefers-reduced-motion:reduce){.rv{opacity:1;transform:none;transition:none}.rv .shot{transform:none;transition:none}.hero,.hero .in > div:first-child > *,.stage .main,.stage .side{animation:none}}")
# the side card float must not fight the static transform rule
rep(".stage .side{position:absolute;right:-6px;bottom:-24px;width:44%;transform:rotateY(-9deg) rotateX(3deg) translateZ(60px);border-radius:16px}",
    ".stage .side{position:absolute;right:-6px;bottom:-24px;width:44%;transform:rotateY(-9deg) rotateX(3deg) translateZ(60px);border-radius:16px;transform-style:preserve-3d}")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
