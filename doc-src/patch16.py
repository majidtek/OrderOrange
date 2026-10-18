import io, os, base64
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# the real OrderOrange logo (ClientCore/wwwroot/logo.svg) embedded as a data URI, used in the top bar, drawer and footer
rep("hero_main = shot(", "LOGO = 'data:image/svg+xml;base64,' + base64.b64encode(open(r'D:\\OrderOrange\\OrderOrange.ClientCore\\wwwroot\\logo.svg', 'rb').read()).decode()\nhero_main = shot(")
rep("""'<div class="top"><div class="in"><span class="mark">O</span><b>OrderOrange · Partner Guide</b>""",
    """f'<div class="top"><div class="in"><img class="logo" src="{LOGO}" alt="OrderOrange" width="36" height="36"><b>OrderOrange · Partner Guide</b>""")
rep("""<div class="dhead"><span class="mark">O</span><b>Guide menu</b>""", """<div class="dhead"><img class="logo" src="{LOGO}" alt="" width="30" height="30"><b>Guide menu</b>""")
rep("""'<div class="ft-brand"><span class="mark">O</span><b>OrderOrange</b>""", """f'<div class="ft-brand"><img class="logo" src="{LOGO}" alt="OrderOrange" width="44" height="44"><b>OrderOrange</b>""")
rep(".mark{width:34px;height:34px;", ".logo{width:36px;height:36px;border-radius:10px;flex:none;object-fit:contain}\n.ft-brand .logo{width:44px;height:44px}\n.mark{width:34px;height:34px;")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
