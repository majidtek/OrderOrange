import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
a = s.index(' "var tq=document.getElementById')
b = s.index('\nvar lb=document.createElement')
block = s[a:b]
s = s[:b] + '\n "' + s[b+1:]          # restore the literal prefix the patch swallowed
# the block is one Python string literal that was split over real newlines; rejoin it as several literals
lines = block.strip('\n').split('\n')
assert lines[0].startswith(' "var tq=')
first = lines[0][2:]                      # drop the leading ' "'
rest = lines[1:]
out = []
out.append(' "' + first.replace('"', '\\"').replace('\\\\"', '\\"') + '\\n"\n') if False else None
def lit(t):
    return ' "' + t.replace('\\', '\\\\').replace('"', '\\"') + '\\n"\n'
out = [lit(first)] + [lit(t) for t in rest]
s = s[:a] + ''.join(out) + s[b:]
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('fixed', len(out), 'lines')
