import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; lines = io.open(p, encoding='utf-8').read().split('\n')
for i, l in enumerate(lines):
    if l.startswith(' "var stage=document.querySelector'):
        body = l[2:]
        assert body.endswith('\\n"'), body[-10:]
        body = body[:-3]
        body = body.replace('\\"', '"').replace('"', '\\"')
        lines[i] = ' "' + body + '\\n"'
        print('fixed line', i + 1)
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
