# -*- coding: utf-8 -*-
"""Cancelling and correcting an invoice, as three screens in the Money chapter.

Remember: HOW, NOTES and the REPORTS catalogue are html.escape'd — only the screen
descriptions below accept <b>. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the three screens
s = io.open('gen.py', encoding='utf-8').read()
anchor = "  (F('0074_'), 'Payroll',"
screens = (
 "  (F('0212_'), 'Invoices', 'An invoice is never quietly edited. This is the register of every one you have "
 "issued \\u2014 number, time, customer, how many items, total and where the order got to \\u2014 searchable by number "
 "and filtered by date. Two actions sit on each row: the pencil <b>replaces</b> the invoice with a corrected copy, "
 "and the cross <b>cancels</b> it. Both ask for a reason, and both leave the original where it was.'),\n"
 "  (F('0213_'), 'Cancelled invoices', 'What was taken back, and by whom. Each row keeps the invoice number, who "
 "added it and when, who removed it and when, the total that was on it, the reason the person typed, and the number "
 "of the invoice that replaced it \\u2014 or a dash when nothing did. This is the page an accountant asks for when the "
 "numbering has a gap in it, and the reason it can always be answered. Excel and A4 print, like every other list.'),\n"
 "  (F('0214_'), 'What changed on an invoice', 'A correction, shown as a correction. The old number and the new one "
 "sit side by side with who made the change, when, and the note they left; underneath, the invoice <b>before</b> and "
 "<b>after</b> line by line, with what was added picked out in green and the new total against the old one. Months "
 "later the question is never \\u201cwas this edited?\\u201d but \\u201cwhat exactly changed, and why\\u201d \\u2014 "
 "and this answers it without anyone having to remember.'),\n")
if "'Cancelled invoices'" not in s:
    assert s.count(anchor) == 1, ('gen.py anchor', s.count(anchor))
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, screens + anchor))
    print('gen.py: three invoice screens added to Money')

# ---------------------------------------------------------------- how-to and notes (no markup here)
s = io.open('gen3.py', encoding='utf-8').read()

anchor = " 'Payroll': ['Choose the month with the arrows.',"
how = (" 'Invoices': ['Find the invoice by number, or narrow the list by date.',"
       " 'Press the pencil to issue a corrected copy: change the lines, and the old invoice is replaced rather than overwritten.',"
       " 'Press the cross to cancel one outright; type the reason, because it is kept with it.',"
       " 'Both actions need the invoice.edit or invoice.cancel permission \\u2014 give them only to the people who should have them.'],\n"
       " 'Cancelled invoices': ['Open the Removed invoices tab to see everything that was taken back.',"
       " 'Read across the row: who added it, who removed it, the reason, and which invoice replaced it.',"
       " 'Export to Excel or print A4 when your accountant asks about a gap in the numbering.'],\n"
       " 'What changed on an invoice': ['Open the Edit history tab.',"
       " 'Each entry shows the old number, the new one, who changed it and the note they left.',"
       " 'Compare the two panels: the lines are listed before and after, and anything added is picked out.'],\n")
if "'Cancelled invoices':" not in s.split('NOTES')[0]:
    assert s.count(anchor) == 1, ('HOW anchor', s.count(anchor))
    s = s.replace(anchor, how + anchor)

notes = (" 'Invoices': 'Correcting an invoice creates a NEW one and keeps the old: the numbering never has a silent "
         "hole in it, and the pair can always be explained. That is why there is no plain edit button.',\n"
         " 'Cancelled invoices': 'The reason is typed by the person cancelling and cannot be changed afterwards. "
         "Tell your team to write something a stranger could read in six months, not \\u201cmistake\\u201d.',\n"
         " 'What changed on an invoice': 'Nothing here can be deleted. If an invoice was corrected twice, both "
         "corrections are listed, oldest first.',\n")
i = s.index('NOTES = {')
anchor2 = " 'Payroll': "
if "'Cancelled invoices': 'The reason is typed" not in s:
    j = s.index(anchor2, i)
    s = s[:j] + notes + s[j:]

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to steps and notes')

# ---------------------------------------------------------------- paths and key actions
s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Payroll': ("
entry = (" 'Invoices': ('/invoices', ['Search by number, filter by date', 'Pencil replaces, cross cancels',"
         " 'A reason is required and kept']),\n"
         " 'Cancelled invoices': ('/invoices', ['Who added it, who removed it', 'The reason they typed',"
         " 'Which invoice replaced it']),\n"
         " 'What changed on an invoice': ('/invoices', ['Old number and new, side by side',"
         " 'Every line before and after', 'Added lines picked out']),\n")
if "'Cancelled invoices':" not in s:
    assert s.count(anchor) == 1, ('gen2 anchor', s.count(anchor))
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, entry + anchor))
    print('gen2.py: META entries')
