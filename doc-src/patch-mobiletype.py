# -*- coding: utf-8 -*-
"""Smaller typography on phones for every guide variant (appended last in <style>, so it wins). Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
q = io.open('seo.py', encoding='utf-8').read()
if 'MOBILE_TYPE' not in q:
    add = ("MOBILE_TYPE = ('@media (max-width:900px){body{font-size:14.5px}.hero h1{font-size:clamp(30px,8.4vw,40px);letter-spacing:-1.4px}.hero p{font-size:15px;line-height:1.55}.hero .notice{font-size:13px}'\n"
           "               '.stats b{font-size:26px}.stats span{font-size:10.5px}.txt p,article p{font-size:14.5px;line-height:1.55}.txt h3,article h3{font-size:19px}section h2,.band h2{font-size:24px}.sub,.band .sub{font-size:13.5px}'\n"
           "               '.end h2{font-size:26px}.end p{font-size:15px}.note{font-size:13px}.how li{font-size:13px}.acts li{font-size:11.5px}.faq p,.faq summary{font-size:14px}.chapters a b{font-size:16px}.catalog .cat-card span{font-size:12.5px}}')\n")
    q = q.replace("def langbar(cur):", add + "\ndef langbar(cur):", 1)
    old = "page = page.replace('</style>', LANGBAR_CSS + '\\n</style>', 1)"
    assert q.count(old) == 1, q.count(old)
    q = q.replace(old, "page = page.replace('</style>', LANGBAR_CSS + MOBILE_TYPE + '\\n</style>', 1)", 1)
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('mobile typography:', 'MOBILE_TYPE + ' in io.open('seo.py', encoding='utf-8').read())
