import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# POS card: strong border + deep shadow, so it reads as a card floating above the dashboard
rep(".stage .side{position:absolute;right:-6px;bottom:-24px;width:44%;transform:rotateY(-9deg) rotateX(3deg) translateZ(60px);border-radius:16px;transform-style:preserve-3d}",
    ".stage .side{position:absolute;right:-6px;bottom:-24px;width:46%;transform:rotateY(-9deg) rotateX(3deg) translateZ(60px);border-radius:18px;transform-style:preserve-3d}\n"
    ".stage .side .shot{border:3px solid #fff;outline:1px solid var(--line);border-radius:18px;box-shadow:0 34px 70px rgba(70,35,5,.30),0 10px 22px rgba(70,35,5,.14),0 0 0 6px rgba(255,90,0,.10)}")

# more motion: mouse parallax on the stage, shimmer on the headline accent, screenshot hover, tree entrance, watermark parallax
rep(".wm{transition:transform .6s ease}", """.wm{transition:transform .6s ease;will-change:transform}
@keyframes shimmer{0%{background-position:0% 50%}100%{background-position:200% 50%}}
.hero h1 span{background:linear-gradient(90deg,#FF5A00,#FF9A4D,#FF5A00,#FFB070);background-size:200% 100%;-webkit-background-clip:text;background-clip:text;color:transparent;animation:shimmer 6s linear infinite}
.stage{transition:transform .15s ease-out}
.shot img{transition:transform .6s cubic-bezier(.16,1,.3,1)}
article:hover .shot img{transform:scale(1.015)}
.tree .tn{animation:rise .6s cubic-bezier(.16,1,.3,1) both}
.tree .tn:nth-child(2){animation-delay:.05s}.tree .tn:nth-child(3){animation-delay:.1s}.tree .tn:nth-child(4){animation-delay:.15s}.tree .tn:nth-child(5){animation-delay:.2s}.tree .tn:nth-child(6){animation-delay:.25s}.tree .tn:nth-child(7){animation-delay:.3s}.tree .tn:nth-child(8){animation-delay:.35s}.tree .tn:nth-child(9){animation-delay:.4s}.tree .tn:nth-child(10){animation-delay:.45s}
.step{animation:rise .5s both}
.acts li{transition:transform .15s,background .15s}.acts li:hover{transform:translateY(-1px);background:#fff}
@keyframes pulse{0%,100%{box-shadow:0 12px 26px rgba(255,90,0,.28)}50%{box-shadow:0 12px 34px rgba(255,90,0,.48)}}
.btn.p{animation:pulse 3.2s ease-in-out infinite}
.chapters a::before{transition:width .18s}.chapters a:hover::before{width:7px}
.mark{transition:transform .3s}.top .in:hover .mark{transform:rotate(-8deg) scale(1.06)}""")
rep("@media (prefers-reduced-motion:reduce){.rv{opacity:1;transform:none;transition:none}.rv .shot{transform:none;transition:none}.hero,.hero .in > div:first-child > *,.stage .main,.stage .side{animation:none}}",
    "@media (prefers-reduced-motion:reduce){.rv{opacity:1;transform:none;transition:none}.rv .shot{transform:none;transition:none}.hero,.hero .in > div:first-child > *,.stage .main,.stage .side,.hero h1 span,.tree .tn,.btn.p,.step{animation:none}}")
# JS: mouse parallax for the stage and a gentle watermark drift with scroll
rep("var lb=document.createElement('div');", """var stage=document.querySelector('.stage');if(stage&&!rm&&matchMedia('(hover:hover)').matches){var hero=document.querySelector('.hero');hero.addEventListener('mousemove',function(e){var r=hero.getBoundingClientRect();var x=(e.clientX-r.left)/r.width-.5,y=(e.clientY-r.top)/r.height-.5;stage.style.transform='translate('+(x*14)+'px,'+(y*10)+'px)'});hero.addEventListener('mouseleave',function(){stage.style.transform=''})} var wms=[].slice.call(document.querySelectorAll('.wm'));function wmScroll(){wms.forEach(function(w){var r=w.getBoundingClientRect();if(r.top<innerHeight&&r.bottom>0){w.style.transform='translateY('+((r.top/innerHeight-.5)*40)+'px)'}})} if(!rm){window.addEventListener('scroll',wmScroll,{passive:true});wmScroll()}
var lb=document.createElement('div');""")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
