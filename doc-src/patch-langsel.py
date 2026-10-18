# -*- coding: utf-8 -*-
"""Language switcher: a compact globe button in the top bar (native picker) instead of a wrapping row of pills. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
q = io.open('seo.py', encoding='utf-8').read()
if 'class="lsel"' not in q:
    old_css_start = q.index("LANGBAR_CSS = (")
    old_css_end = q.index("def langbar(cur):")
    new_css = '''LANGBAR_CSS = ('.lsel{position:relative;flex:0 0 36px;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);display:inline-flex;align-items:center;justify-content:center;gap:6px;margin-inline-end:8px;color:var(--ink-2);font:800 12.5px Manrope,sans-serif;cursor:pointer}'
               '.lsel:hover{border-color:var(--accent);color:var(--accent-deep)}.lsel svg{width:18px;height:18px;flex:0 0 18px}.lsel b{display:none;white-space:nowrap}.lsel i{display:none;font-style:normal;opacity:.6}'
               '.lsel select{position:absolute;inset:0;width:100%;height:100%;opacity:0;cursor:pointer;font-size:16px}'
               '@media (min-width:901px){.lsel{width:auto;padding:0 12px 0 10px;border-radius:999px}.lsel b,.lsel i{display:inline}}'
               '.hero .notice{margin:14px 0 0;padding:10px 14px;border-radius:12px;background:var(--panel);color:var(--ink-2);font-size:14px;line-height:1.5}'
               '[dir="rtl"] .hero{direction:rtl}[dir="rtl"] .hero h1{letter-spacing:0}')
'''
    q = q[:old_css_start] + new_css + q[old_css_end:]
    old_fn_start = q.index("def langbar(cur):")
    old_fn_end = q.index("EN = L['en']")
    new_fn = '''def langbar(cur):
    """A globe button that opens the phone's own picker; on wide screens it also shows the language name."""
    opts = ''.join(f'<option value="{url(k)}"{" selected" if k == cur else ""}>{L[k]["name"]}</option>' for k in ORDER)
    return ('<label class="lsel" title="Language"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>'
            f'<b>{L[cur]["name"]}</b><i>▾</i><select aria-label="Language" onchange="location.href=this.value">{opts}</select></label>')

'''
    q = q[:old_fn_start] + new_fn + q[old_fn_end:]
    # move it from above the hero into the top bar, before the menu button
    q = q.replace("    page = page.replace('<div class=\"hero\">', langbar(l) + '<div class=\"hero\">', 1)",
                  "    page = page.replace('<button class=\"tmb\" id=\"tmb\"', langbar(l) + '<button class=\"tmb\" id=\"tmb\"', 1)")
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('language selector patched:', 'class="lsel"' in io.open('seo.py', encoding='utf-8').read())
