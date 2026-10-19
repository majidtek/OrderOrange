# -*- coding: utf-8 -*-
"""Instagram, scheduled posts and the WhatsApp dish carousel join the guide, right after the
assistant. Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
if "'Instagram'" not in s.split("'Surveys'")[0].split("parts = [")[1] if "parts = [" in s else "'Instagram'" not in s:
    pass
lines = s.split('\n')
at = [i for i, l in enumerate(lines) if l.startswith("  (F('0205_'), 'OrderOrange AI'")]
assert len(at) == 1, at
if not any(l.startswith("  (F('0225_'), 'Instagram'") for l in lines):
    lines[at[0] + 1:at[0] + 1] = [
     "  (F('0225_'), 'Instagram', 'Your restaurant\\u2019s Instagram, posted from the same place you run the kitchen. Connect the account once; from then on pick a dish photo from the menu or any picture, write the caption, tap the ready hashtags and press <b>Post to Instagram</b>. The card on the right shows the connected account, how long its token has left (it renews itself while you keep posting) and your recent posts. Instagram allows 100 posts a day per account; the counter under the button keeps you inside it.'),",
     "  (F('0226_'), 'Scheduled posts', 'Plan a post now and OrderOrange publishes it at the hour you choose. Add a photo or pick a dish, write the caption, then set the day and the time — or use the shortcuts: <b>Tonight 19:00</b>, <b>Tomorrow 12:00</b>, <b>Tomorrow 19:00</b>. <b>The plan</b> lists everything waiting and everything that already went out, so a week of posts can be set up on a quiet Monday morning. The <b>Menu poster</b> page draws a 1080\\u00d71350 picture of your dishes and prices that can be posted, scheduled or downloaded.'),",
     "  (F('0224_'), 'Today\\u2019s dishes on WhatsApp', 'Your dishes, delivered to the guest\\u2019s WhatsApp as a swipeable carousel: a photo, the name, the price and an <b>Order now</b> button on every card. A tap opens the order with the OrderOrange assistant, so a message becomes a sale in three touches. The cards come straight from your menu — the same photos and prices your app shows — and go out to guests who have opted in, at the hour you choose.'),",
    ]
    io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines)); print('gen.py: three screens after the assistant')

s = io.open('gen2.py', encoding='utf-8').read()
if "'Scheduled posts':" not in s:
    a = " 'OrderOrange AI': ("
    assert s.count(a) == 1
    i = s.index(a); j = s.index('\n', i) + 1
    s = s[:i] + s[i:j] + (" 'Instagram': ('/instagram', ['Connect once, post from the menu', 'Ready hashtags', 'Token renews itself', '100 posts a day, counted']),\n"
                          " 'Scheduled posts': ('/instagram/schedule', ['Pick a photo or a dish', 'Day and time, or a shortcut', 'The plan: waiting and sent', 'Menu poster: draw, post or download']),\n"
                          " 'Today\\u2019s dishes on WhatsApp': ('https://www.orderorange.com', ['Carousel of dishes with photos and prices', 'Order now on every card', 'Straight from your menu', 'Sent at the hour you choose']),\n") + s[j:]
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s); print('gen2.py: META')

s = io.open('gen3.py', encoding='utf-8').read()
if "'Scheduled posts': [" not in s:
    how_anchor = " 'Recipes': ['Open a dish and add each material"
    assert s.count(how_anchor) == 1
    s = s.replace(how_anchor,
        " 'Instagram': ['Open Instagram under Operations and press Connect; sign in to the Instagram account of the restaurant.',"
        " 'Pick a picture from the strip, or upload one; write the caption and tap the hashtags you want.',"
        " 'Press Post to Instagram. The post appears under Recent posts within a minute.'],\n"
        " 'Scheduled posts': ['Add a photo or pick a dish, write the caption, then choose the day and time or press a shortcut.',"
        " 'Press Schedule the post. It waits under The plan and goes out on its own at that hour.',"
        " 'Open Menu poster to draw a picture of several dishes with prices; post it, schedule it or download it.'],\n"
        " 'Today\\u2019s dishes on WhatsApp': ['Keep dish photos and prices up to date on Products - the cards are built from them.',"
        " 'Guests who have messaged the restaurant on WhatsApp receive the carousel.',"
        " 'A tap on Order now opens the order with the assistant; the order arrives in Live orders like any other.'],\n" + how_anchor)
    note_anchor = " 'Recipes': 'Production capacity"
    assert s.count(note_anchor) == 1
    s = s.replace(note_anchor,
        " 'Instagram': 'Square pictures look best. A dish photo from the menu is cropped to a square for you.',\n"
        " 'Scheduled posts': 'Times are the shop\\u2019s local time. A post cannot be scheduled in the past - the page tells you how long until it goes out.',\n"
        " 'Today\\u2019s dishes on WhatsApp': 'WhatsApp only allows marketing messages to people who have opted in, and OrderOrange respects that: nobody receives the carousel who has not written to the restaurant first.',\n" + note_anchor)
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s); print('gen3.py: how-to and notes')

s = io.open('seo.py', encoding='utf-8').read()
old = '"Waste log with reasons, cost and undo",'
if 'Instagram posting and scheduling' not in s:
    assert s.count(old) == 1
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, old + ' "Instagram posting and scheduling from the menu, with a menu poster", "WhatsApp dish carousel with Order now buttons",'))
    print('seo.py: feature list')
