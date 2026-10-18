# -*- coding: utf-8 -*-
"""Idempotent: 'Receiving stock by barcode' screen (mobile purchase) in the Money chapter + SEO feature."""
import io, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
Image.open(r'C:/Users/Administrator/.claude/uploads/3f109875-61f7-48dc-95ed-ef557c0e8fdc/9685857a-image.jpg').convert('RGB').crop((0, 86, 923, 1880)).save('frame_0208_0208.0s.png')

def patch(p, pairs):
    s = io.open(p, encoding='utf-8').read()
    for a, b, marker in pairs:
        if marker in s: continue
        assert s.count(a) == 1, (p, a[:60], s.count(a)); s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

patch('gen.py', [("  (F('0070_'), 'Payments to suppliers',",
 "  (F('0208_'), 'Receiving stock by barcode', 'Purchases work on a phone at the back door as well as on the PC. Scan a barcode, type a material code, or tap a material on the shelf; a quantity goes in the same box, so <b>7*20</b> means twenty of item No.\u00a07. Choose the supplier, enter the invoice number and date, pick cash, card or later with installments, and press Receive stock: stock levels, unit costs and the supplier\u2019s account update at once.'),\n  (F('0070_'), 'Payments to suppliers',", "'Receiving stock by barcode'")])
patch('gen2.py', [(" 'Payments to suppliers':", " 'Receiving stock by barcode': ('/materials/purchases', ['Scan, type a code or tap a material', 'Quantity shorthand: 7*20', 'Cash, card or installments']),\n 'Payments to suppliers':", "'Receiving stock by barcode'")])
patch('gen3.py', [
 (" 'Purchases': [", " 'Receiving stock by barcode': ['Choose Raw materials or Products, then scan the barcode or type the code; add a quantity as code*qty, for example 7*20.', 'Pick the supplier, enter the invoice number and its date; a material not on the shelf yet is added on the spot with Add material.', 'Choose Cash, Card or Later / installments and press Receive stock; the purchase appears on the Purchases page and in the Bill report.'],\n 'Payments to suppliers': [", "'Receiving stock by barcode': ["),
 (" 'Payments to suppliers': '", " 'Receiving stock by barcode': 'The same phone camera that scans table QR codes scans supplier barcodes; no scanner hardware is needed, though a USB scanner works on the PC too.',\n 'Payments to suppliers': '", "'Receiving stock by barcode': '"),
])
q = io.open('seo.py', encoding='utf-8').read()
if 'Stock and purchases by barcode' not in q:
    q = q.replace('"featureList": ["Free restaurant POS system",', '"featureList": ["Free restaurant POS system", "Stock and purchases by barcode with supplier invoices and installments",')
    q = q.replace(' 40 ', ' 41 ').replace('٤٠', '٤١').replace('۴۰', '۴۱').replace('40画面', '41画面').replace('40个', '41个')
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('purchase screen patched')
