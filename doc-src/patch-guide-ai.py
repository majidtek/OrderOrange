# -*- coding: utf-8 -*-
"""The assistant, written properly: what it does for the guest, what it does for the shop,
and why it runs on our own vocabulary instead of an outside service.

Only the screen description takes markup; HOW, NOTES and the catalogue are escaped."""
import io, os, re
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screen
s = io.open('gen.py', encoding='utf-8').read()
start = s.find("  (F('0205_'), 'OrderOrange AI',")
assert start >= 0, 'AI screen missing'
end = s.index("'),\n", start) + len("'),\n")
new = (
 "  (F('0205_'), 'OrderOrange AI', 'One assistant, working both sides of the counter.<br><br>"
 "<b>For your guests</b>, the sparkle button on the shop page takes an order from start to finish in ordinary "
 "language, by typing or by speaking. \\u201c2 pizzas and a cola to Al Mouj\\u201d becomes a basket, an address and a "
 "checkout in one sentence \\u2014 quantities, sizes, notes and the address read together rather than asked for one "
 "at a time. It answers the questions people actually ask: \\u201cdo you have pizza\\u201d, \\u201chow much is the "
 "delivery\\u201d, \\u201cwhere is my order\\u201d, \\u201ccancel it\\u201d, even \\u201cwhat can I get for five "
 "rials\\u201d. It replies in whatever language the guest wrote in, across fourteen of them, and it forgives spelling "
 "\\u2014 a misspelled kebab still finds the kebab. It never invents a dish: something it cannot find is asked about, "
 "not guessed, and it only ever offers real dishes at today\\u2019s prices from kitchens that are open.<br><br>"
 "<b>For your staff</b>, the same brain sits inside the POS as the order assistant. Key or dictate a whole ticket "
 "\\u2014 \\u201c2 koobideh, 1 cola, table 5\\u201d \\u2014 and it lands on that table, which is far faster than "
 "hunting a grid at a rush. It answers questions about the shop at the till, does the arithmetic and the clock maths a "
 "cashier ends up doing anyway, and Ctrl+K searches pages, products, customers and orders from anywhere in the portal."
 "<br><br><b>And it tells you what people wanted.</b> Every question a guest typed is kept and grouped by the address "
 "it came from, so you can read, in their words, what was asked for and not found \\u2014 the dish to put on the menu "
 "next month is usually in that list.<br><br>"
 "<b>It runs on OrderOrange\\u2019s own vocabulary, not an outside AI service.</b> Nothing your guests type leaves the "
 "platform, there is no per-message bill, it keeps working when somebody else\\u2019s service is down, and the same "
 "sentence always produces the same basket \\u2014 which matters when it is taking real money.'),\n")
if 'working both sides of the counter' not in s:
    s = s[:start] + new + s[end:]
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen.py: the assistant rewritten')

# ---------------------------------------------------------------- how-to and notes
s = io.open('gen3.py', encoding='utf-8').read()
m = re.search(r" 'OrderOrange AI': \[.*?\],\n", s, re.S)
assert m, 'AI how-to missing'
how = (" 'OrderOrange AI': ['Keep dish names and prices current: the assistant sells exactly what is on your menu and "
       "nothing else.',"
       " 'Name dishes the way guests say them; misspellings are forgiven, but a clear name in the customer\\u2019s "
       "language always wins.',"
       " 'Set delivery areas and fees in Settings so it can quote them when it takes an address.',"
       " 'On the POS, open the order assistant and type or dictate a ticket such as 2 koobideh, 1 cola, table 5.',"
       " 'Read the assistant report every week or two: what guests asked for and did not get is the shortest route to "
       "your next dish.'],\n")
if 'the shortest route to' not in s:
    s = s[:m.start()] + how + s[m.end():]

i = s.index('NOTES = {')
m2 = re.search(r" 'OrderOrange AI': '.*?',\n", s[i:], re.S)
note = (" 'OrderOrange AI': 'It understands quantities, sizes, notes and an address in one sentence, and it never "
        "invents a dish \\u2014 an item it cannot find is asked about, not guessed. It is deterministic rather than "
        "generative: the same words always produce the same basket, and nothing a guest types is sent to another "
        "company.',\n")
if 'deterministic rather than' not in s:
    assert m2, 'AI note missing'
    s = s[:i + m2.start()] + note + s[i + m2.end():]

# a question owners genuinely ask
anchor = " ('Which browser and devices are supported?',"
faq = (" ('Does the AI send my customers\\u2019 messages to another company?', 'No. The assistant understands orders "
       "using OrderOrange\\u2019s own vocabulary, built into the app, so what a guest types stays on the platform. "
       "There is no per-message cost, nothing to sign up for, and it keeps working when an outside service is "
       "having a bad day.'),\n"
       " ('Can the assistant take a whole order by itself?', 'Yes, start to finish: it finds the dish, asks the "
       "quantity, takes the address, offers the payment methods you accept and places the order, by text or by voice, "
       "in fourteen languages. Your staff get the same assistant inside the POS for keying tickets at a rush.'),\n")
if 'send my customers' not in s:
    assert s.count(anchor) == 1, ('FAQ anchor', s.count(anchor))
    s = s.replace(anchor, faq + anchor)

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to, note and two questions')

# ---------------------------------------------------------------- what search engines are told
s = io.open('seo.py', encoding='utf-8').read()
old = '"AI ordering assistant for guests and staff, by text or voice",'
new = ('"AI ordering assistant for guests and staff, by text or voice", '
       '"Assistant runs on the platform\'s own vocabulary \\u2014 no outside AI service, no per-message cost", '
       '"Report of every question guests asked the assistant",')
if 'no outside AI service' not in s:
    assert s.count(old) == 1, ('featureList', s.count(old))
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('seo.py: assistant features named')
